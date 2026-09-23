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
  recentMessages?(chatId: string): Promise<{ found: boolean; loaded: number; types?: Record<string, number>; messages: IncomingMessage[] }>;
}
export class GatewayError extends Error {
  constructor(public code: string, public status = 503) { super(code); }
}
type Delivery = { hash: string; status: "dry_run" | "attempting" | "sent" | "unknown" };
export type InboxMessage = { id: string; text: string; at: number };
type State = { groupId?: string; shoppingGroupId?: string; inbox?: InboxMessage[]; seen?: string[]; proposalAt?: number; deliveries: Record<string, Delivery> };
type Wid = string | { _serialized?: string } | undefined;
export type IncomingMessage = { id?: { _serialized?: string; remote?: Wid; fromMe?: boolean }; from?: Wid; to?: Wid; body?: string; timestamp?: number };
const wid = (value: Wid): string | undefined => {
  if (typeof value === "string") return value;
  if (typeof value?._serialized === "string") return value._serialized;
  const text = typeof (value as any)?.toString === "function" ? String(value) : "";
  return text && text !== "[object Object]" ? text : undefined;
};
// Only short numeric replies ("1 3 5", "2-4") are kept; other chat content is never stored.
const selectionReply = /^\d[\d\s,;.\-\u2013]*$/;
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
      groupId: this.state.groupId ?? null, shoppingGroupId: this.state.shoppingGroupId ?? null,
      pendingReplies: this.state.inbox?.length ?? 0, circuitOpen: Date.now() < this.blockedUntil };
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
    client.on("message_create", (message: IncomingMessage) => {
      if (active()) this.events++;
      if (active()) void this.receive(message, "event").catch(() => console.error(JSON.stringify({ event: "shopping_reply_not_saved" })));
    });
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
  async receive(message: IncomingMessage, source = "event") {
    const group = this.state.shoppingGroupId;
    const id = wid(message?.id as Wid);
    if (!group || !id) return;
    const chats = [wid(message?.id?.remote), wid(message?.from), wid(message?.to)];
    if (!chats.includes(group)) return;
    const text = (message?.body ?? "").trim();
    const at = message.timestamp ?? Math.floor(Date.now() / 1000);
    const seen = this.state.seen ??= [];
    // Log only metadata: never message content.
    const log = (result: string) => console.info(JSON.stringify({ event: "shopping_reply", source, result }));
    if (seen.includes(id)) return;
    if (text.length > 100 || !selectionReply.test(text)) return log("ignored_not_numeric");
    if (this.state.proposalAt && at < this.state.proposalAt) return log("ignored_before_proposal");
    seen.push(id); this.state.seen = seen.slice(-300);
    const inbox = this.state.inbox ??= [];
    inbox.push({ id, text, at });
    this.state.inbox = inbox.slice(-50);
    await this.save();
    log("queued");
  }
  // Diagnostic metadata only (counts and timestamps), logged when it changes.
  private logPoll(data: Record<string, unknown>) {
    const line = JSON.stringify({ event: "shopping_poll", ...data });
    if (line !== this.lastPoll) { this.lastPoll = line; console.info(line); }
  }
  // Fallback when WhatsApp does not emit events for messages typed on the phone.
  private lastPoll = "";
  private events = 0;
  async pollShoppingGroup() {
    const group = this.state.shoppingGroupId;
    const skipped = !group ? "no_shopping_group" : this.connection !== "ready" ? "not_ready:" + this.connection : !this.client?.recentMessages ? "no_reader" : "";
    if (skipped) { this.logPoll({ skipped }); return; }
    try {
      const result = await this.limited(this.client!.recentMessages!(group!));
      const messages = result.messages;
      this.logPoll({ chatFound: result.found, loadedFromStore: result.loaded > 0, types: result.types ?? {}, events: this.events, messages: messages.length, numeric: messages.filter(m => selectionReply.test((m.body ?? "").trim())).length,
        newest: messages.reduce((t, m) => Math.max(t, m.timestamp ?? 0), 0), proposalAt: this.state.proposalAt ?? null });
      for (const message of messages) await this.receive(message, "poll");
    } catch (error) {
      const reason = error instanceof GatewayError ? error.code : error instanceof Error ? error.name + ": " + error.message.slice(0, 120) : "unknown";
      console.error(JSON.stringify({ event: "shopping_poll_failed", reason }));
    }
  }
  inbox() { return this.state.inbox ?? []; }
  async acknowledge(ids: string[]) {
    this.state.inbox = (this.state.inbox ?? []).filter(m => !ids.includes(m.id));
    await this.save();
  }
  async selectShoppingGroup(id: string) {
    if (this.busy) throw new GatewayError("operation_in_progress", 409);
    this.busy = true;
    try {
      if (!(await this.groups()).some(g => g.id === id)) throw new GatewayError("group_not_found", 400);
      this.state.shoppingGroupId = id;
      await this.save();
    } finally { this.busy = false; }
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
    if (groupId && groupId !== this.state.groupId && groupId !== this.state.shoppingGroupId) throw new GatewayError("group_not_allowed", 403);
    const target = groupId ?? this.state.groupId;
    if (this.busy) throw new GatewayError("operation_in_progress", 409);
    this.busy = true;
    try {
      const hash = createHash("sha256").update(`${target}\n${text}`).digest("hex");
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
        await this.limited(this.client!.sendMessage(target, text));
        record.status = "sent";
        if (target === this.state.shoppingGroupId && id.startsWith("shopping-proposal-")) this.state.proposalAt = Math.floor(Date.now() / 1000) - 5;
      } catch {
        record.status = "unknown";
      }
      await this.save();
      console.info(JSON.stringify({ event: "message_result", id, hash, status: record.status }));
      return record;
    } finally { this.busy = false; }
  }
}
