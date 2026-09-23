import { createHash } from "node:crypto";
import { mkdir, readFile, writeFile, rename } from "node:fs/promises";
import { join } from "node:path";

export type Group = { id: string; name: string };
export interface WhatsAppPort {
  on(event: string, listener: (...args: any[]) => void): unknown;
  initialize(): Promise<void>;
  destroy(): Promise<void>;
  getChats(): Promise<Array<{ isGroup: boolean; id: { _serialized: string }; name: string }>>;
  sendMessage(id: string, text: string): Promise<unknown>;
}
export class GatewayError extends Error {
  constructor(public code: string, public status = 503) { super(code); }
}
type Delivery = { hash: string; status: "dry_run" | "attempting" | "sent" | "unknown" };
type State = { groupId?: string; deliveries: Record<string, Delivery> };
export class Gateway {
  private client?: WhatsAppPort;
  private connection = "disabled";
  private qr?: { value: string; expires: number };
  private state: State = { deliveries: {} };
  private busy = false;
  private failures = 0;
  private blockedUntil = 0;
  constructor(private factory: () => WhatsAppPort, private directory: string,
    private sendEnabled = false, private timeoutMs = 15000,
    private pause: (ms: number) => Promise<void> = ms => new Promise(r => setTimeout(r, ms))) {}

  async load() {
    await mkdir(this.directory, { recursive: true, mode: 0o700 });
    try {
      this.state = JSON.parse(await readFile(join(this.directory, "gateway.json"), "utf8"));
      if (!this.state.deliveries || typeof this.state.deliveries !== "object") throw new Error("Invalid state");
    } catch (error) {
      if ((error as NodeJS.ErrnoException).code !== "ENOENT") throw error;
    }
  }
  private async save() {
    const path = join(this.directory, "gateway.json");
    await writeFile(`${path}.tmp`, JSON.stringify(this.state), { mode: 0o600 });
    await rename(`${path}.tmp`, path);
  }
  private async limited<T>(operation: Promise<T>): Promise<T> {
    let timer: ReturnType<typeof setTimeout>;
    try {
      return await Promise.race([operation, new Promise<never>((_, reject) => {
        timer = setTimeout(() => reject(new GatewayError("operation_timeout")), this.timeoutMs);
      })]);
    } finally { clearTimeout(timer!); }
  }
  status() {
    return { connection: this.connection, sendEnabled: this.sendEnabled,
      groupId: this.state.groupId ?? null, circuitOpen: Date.now() < this.blockedUntil };
  }
  getQr() {
    if (!this.qr || Date.now() >= this.qr.expires) throw new GatewayError("qr_unavailable", 409);
    return this.qr.value;
  }
  async start() {
    if (["starting", "qr", "authenticated", "ready"].includes(this.connection)) return;
    if (this.client) await this.stop();
    this.connection = "starting";
    this.qr = undefined;
    const client = this.factory();
    this.client = client;
    const active = () => this.client === client;
    client.on("qr", (value: string) => {
      if (active()) { this.qr = { value, expires: Date.now() + 45000 }; this.connection = "qr"; }
    });
    client.on("authenticated", () => { if (active()) { this.qr = undefined; this.connection = "authenticated"; } });
    client.on("ready", () => { if (active()) { this.qr = undefined; this.connection = "ready"; this.failures = 0; this.blockedUntil = 0; } });
    client.on("auth_failure", () => { if (active()) { this.qr = undefined; this.connection = "auth_failure"; } });
    client.on("disconnected", () => { if (active()) { this.qr = undefined; this.connection = "disconnected"; } });
    // initialize can remain pending while the user scans. Do not block the HTTP server.
    void client.initialize().catch((error: unknown) => {
      if (active()) { this.qr = undefined; this.connection = "error"; }
      const message = error instanceof Error ? error.message : typeof error === "string" ? error : "";
      const category = /timeout|timed out/i.test(message) ? "timeout" :
        /browser|launch|singleton/i.test(message) ? "browser_launch" :
        /permission|EACCES/i.test(message) ? "permissions" :
        /net::|navigation/i.test(message) ? "navigation" : "other";
      console.error(JSON.stringify({ event: "whatsapp_initialization_failed", category }));
    });
  }
  async stop() {
    const client = this.client;
    this.client = undefined;
    this.qr = undefined;
    this.connection = "disabled";
    if (client) await this.limited(client.destroy());
  }
  async groups(): Promise<Group[]> {
    if (this.connection !== "ready" || !this.client) throw new GatewayError("whatsapp_not_ready");
    if (Date.now() < this.blockedUntil) throw new GatewayError("circuit_open");
    for (let attempt = 0; attempt < 3; attempt++) {
      try {
        const chats = await this.limited(this.client.getChats());
        this.failures = 0;
        return chats.filter(c => c.isGroup).map(c => ({ id: c.id._serialized, name: c.name }));
      } catch (error) {
        if (error instanceof GatewayError && error.code === "operation_timeout") break;
        if (attempt < 2) await this.pause(250 * 2 ** attempt);
      }
    }
    if (++this.failures >= 3) this.blockedUntil = Date.now() + 30000;
    throw new GatewayError("groups_unavailable");
  }
  async selectGroup(id: string) {
    if (this.busy) throw new GatewayError("operation_in_progress", 409);
    this.busy = true;
    try {
      if (!(await this.groups()).some(g => g.id === id)) throw new GatewayError("group_not_found", 400);
      this.state.groupId = id;
      await this.save();
    } finally { this.busy = false; }
  }
  async send(id: string, text: string, groupId?: string) {
    if (!/^[a-zA-Z0-9_-]{1,100}$/.test(id) || !text.trim() || text.length > 4000)
      throw new GatewayError("invalid_message", 400);
    if (!this.state.groupId) throw new GatewayError("group_not_selected", 409);
    if (groupId && groupId !== this.state.groupId) throw new GatewayError("group_not_allowed", 403);
    if (this.busy) throw new GatewayError("operation_in_progress", 409);
    this.busy = true;
    try {
      const hash = createHash("sha256").update(`${this.state.groupId}\n${text}`).digest("hex");
      const prior = this.state.deliveries[id];
      if (prior) {
        if (prior.hash !== hash) throw new GatewayError("idempotency_conflict", 409);
        return { ...prior, duplicate: true };
      }
      if (this.sendEnabled && (this.connection !== "ready" || !this.client)) throw new GatewayError("whatsapp_not_ready");
      const record: Delivery = { hash, status: this.sendEnabled ? "attempting" : "dry_run" };
      this.state.deliveries[id] = record;
      await this.save(); // Persist BEFORE the side effect. Never retry an ambiguous delivery.
      if (!this.sendEnabled) {
        console.info(JSON.stringify({ event: "message_dry_run", id, hash }));
        return record;
      }
      try {
        await this.limited(this.client!.sendMessage(this.state.groupId, text));
        record.status = "sent";
      } catch {
        record.status = "unknown";
      }
      await this.save();
      console.info(JSON.stringify({ event: "message_result", id, hash, status: record.status }));
      return record;
    } finally { this.busy = false; }
  }
}
