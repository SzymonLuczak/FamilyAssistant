import { test } from "node:test";
import assert from "node:assert/strict";
import { EventEmitter } from "node:events";
import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { Gateway, type WhatsAppPort } from "../src/gateway.js";

class Fake extends EventEmitter implements WhatsAppPort {
  sent = 0; reads = 0; failReads = 0; failSend = false;
  async initialize() {}
  async destroy() {}
  async getChats() {
    this.reads++;
    if (this.failReads-- > 0) throw new Error("offline");
    return [{ isGroup: true, id: { _serialized: "family@g.us" }, name: "Family" },
      { isGroup: true, id: { _serialized: "shopping@g.us" }, name: "Shopping" },
      { isGroup: false, id: { _serialized: "person@c.us" }, name: "Person" }];
  }
  async sendMessage() { this.sent++; if (this.failSend) throw new Error("unknown delivery"); }
}
async function setup(t: any, enabled = false) {
  const directory = await mkdtemp(join(tmpdir(), "family-gateway-"));
  t.after(() => rm(directory, { recursive: true, force: true }));
  const client = new Fake();
  const gateway = new Gateway(() => client, directory, enabled, 100, async () => {});
  await gateway.load(); await gateway.start(); client.emit("ready");
  return { gateway, client, directory };
}
test("QR invalidated on authentication and disconnect; liveness independent", async t => {
  const { gateway, client } = await setup(t);
  client.emit("qr", "private-qr"); assert.equal(gateway.getQr(), "private-qr");
  client.emit("authenticated"); assert.throws(() => gateway.getQr());
  client.emit("disconnected"); assert.equal(gateway.status().connection, "disconnected");
  await assert.rejects(gateway.groups(), /whatsapp_not_ready/);
});
test("select only an existing group; dry-run never sends", async t => {
  const { gateway, client } = await setup(t);
  await assert.rejects(gateway.selectGroup("person@c.us"), /group_not_found/);
  await gateway.selectGroup("family@g.us");
  assert.equal((await gateway.send("test1", "Hello")).status, "dry_run");
  assert.equal(client.sent, 0);
  await assert.rejects(gateway.send("test2", "Hello", "other@g.us"), /group_not_allowed/);
});
test("selection and dedup survive restart; dry-run cannot become real send", async t => {
  const { gateway, client, directory } = await setup(t);
  await gateway.selectGroup("family@g.us"); await gateway.send("test1", "Hello");
  const next = new Gateway(() => client, directory, true);
  await next.load(); await next.start(); client.emit("ready");
  assert.equal(next.status().groupId, "family@g.us");
  assert.equal((await next.send("test1", "Hello")).status, "dry_run");
  assert.equal(client.sent, 0);
  await assert.rejects(next.send("test1", "Changed"), /idempotency_conflict/);
});
test("enabled send requires readiness; sends once and deduplicates", async t => {
  const { gateway, client } = await setup(t, true);
  await gateway.selectGroup("family@g.us");
  assert.equal((await gateway.send("one", "Hello")).status, "sent");
  await gateway.send("one", "Hello"); assert.equal(client.sent, 1);
  client.emit("disconnected"); await assert.rejects(gateway.send("two", "Hello"), /whatsapp_not_ready/);
});
test("ambiguous delivery is persisted and never automatically retried", async t => {
  const { gateway, client, directory } = await setup(t, true);
  await gateway.selectGroup("family@g.us"); client.failSend = true;
  assert.equal((await gateway.send("one", "Hello")).status, "unknown");
  const next = new Gateway(() => client, directory, true); await next.load();
  assert.equal((await next.send("one", "Hello")).status, "unknown");
  assert.equal(client.sent, 1);
});
test("read retries bounded; circuit opens after repeated failures", async t => {
  const { gateway, client } = await setup(t);
  client.failReads = 2; assert.equal((await gateway.groups()).length, 2); assert.equal(client.reads, 3);
  client.failReads = 20;
  for (let i = 0; i < 3; i++) await assert.rejects(gateway.groups(), /groups_unavailable/);
  assert.equal(client.reads, 12);
  await assert.rejects(gateway.groups(), /circuit_open/); assert.equal(client.reads, 12);
});
test("timed-out send cannot be retried", async t => {
  const { gateway, client } = await setup(t, true);
  await gateway.selectGroup("family@g.us");
  client.sendMessage = () => { client.sent++; return new Promise(() => {}); };
  assert.equal((await gateway.send("slow", "Hello")).status, "unknown");
  await gateway.send("slow", "Hello"); assert.equal(client.sent, 1);
});
test("shopping group: only numeric replies from that group are kept; sends allowed there", async t => {
  const { gateway, client, directory } = await setup(t, true);
  await gateway.selectGroup("family@g.us");
  const reply = (id: string, remote: string, body: string) => ({ id: { _serialized: id, remote }, body, timestamp: 1 });
  await gateway.receive(reply("a", "shopping@g.us", "1 3")); // ignored: group not selected yet
  await gateway.selectShoppingGroup("shopping@g.us");
  await gateway.receive(reply("b", "family@g.us", "1 2"));
  await gateway.receive(reply("c", "shopping@g.us", "Kupiłem mleko"));
  await gateway.receive(reply("d", "shopping@g.us", " 1, 3-5 "));
  await gateway.receive(reply("d", "shopping@g.us", " 1, 3-5 "));
  assert.deepEqual(gateway.inbox().map(m => [m.id, m.text]), [["d", "1, 3-5"]]);
  const next = new Gateway(() => client, directory, true); await next.load();
  assert.equal(next.inbox().length, 1);
  await gateway.acknowledge(["d"]); assert.equal(gateway.inbox().length, 0);
  assert.equal((await gateway.send("p1", "Propozycje", "shopping@g.us")).status, "sent");
  await assert.rejects(gateway.send("p2", "x", "other@g.us"), /group_not_allowed/);
  assert.equal(gateway.status().shoppingGroupId, "shopping@g.us");
});
test("polling fallback queues numeric replies once and only after the latest proposal", async t => {
  const { gateway, client } = await setup(t, true);
  await gateway.selectGroup("family@g.us"); await gateway.selectShoppingGroup("shopping@g.us");
  await gateway.send("shopping-proposal-1", "1. Mleko", "shopping@g.us");
  const now = Math.floor(Date.now() / 1000);
  (client as any).recentMessages = async () => ({ found: true, loaded: 0, messages: [
    { id: { _serialized: "old", remote: { _serialized: "shopping@g.us" } }, body: "1", timestamp: now - 3600 },
    { id: { _serialized: "new", fromMe: true, remote: { _serialized: "shopping@g.us" } }, to: "shopping@g.us", body: "2-4", timestamp: now + 1 },
    { id: { _serialized: "chat", remote: "shopping@g.us" }, body: "ok, kupię", timestamp: now + 2 }] });
  await gateway.pollShoppingGroup(); await gateway.pollShoppingGroup();
  assert.deepEqual(gateway.inbox().map(m => m.id), ["new"]);
  await gateway.acknowledge(["new"]); await gateway.pollShoppingGroup();
  assert.equal(gateway.inbox().length, 0);
});
