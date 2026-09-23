import { test } from "node:test";
import assert from "node:assert/strict";
import { once, EventEmitter } from "node:events";
import type { AddressInfo } from "node:net";
import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { createApp } from "../src/app.js";
import { Gateway } from "../src/gateway.js";

test("HTTP routes validate bodies, protect group boundary and return QR privately", async t => {
  const directory = await mkdtemp(join(tmpdir(), "gateway-http-"));
  const fake = Object.assign(new EventEmitter(), {
    initialize: async () => {}, destroy: async () => {},
    getChats: async () => [{ isGroup: true, id: { _serialized: "test@g.us" }, name: "Test" }],
    sendMessage: async () => { throw new Error("Must never send in dry-run"); },
  });
  const gateway = new Gateway(() => fake, directory); await gateway.load();
  const server = createApp(gateway); server.listen(0, "127.0.0.1"); await once(server, "listening");
  t.after(async () => {
    await new Promise<void>(r => server.close(() => r()));
    await rm(directory, { recursive: true, force: true });
  });
  const base = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
  const request = (path: string, method = "GET", body?: string) => fetch(base + path, {
    method, body, headers: { "Content-Type": "application/json" },
  });
  assert.equal((await request("/auth/start", "POST")).status, 202);
  assert.equal((await request("/auth/qr")).status, 409);
  fake.emit("qr", "fake-test-qr");
  const qr = await request("/auth/qr");
  assert.equal(qr.headers.get("cache-control"), "no-store");
  assert.match(await qr.text(), /<svg/);
  fake.emit("ready");
  assert.equal((await request("/auth/qr")).status, 409);
  assert.equal((await request("/config/group", "PUT", '{"groupId":"test@g.us"}')).status, 200);
  assert.equal((await request("/messages", "POST", 'broken')).status, 400);
  assert.equal((await request("/messages", "POST", 'null')).status, 400);
  assert.equal((await request("/messages", "POST", '{"id":3,"text":"hello"}')).status, 400);
  assert.equal((await request("/messages/group/other@g.us", "POST", '{"id":"1","text":"hello"}')).status, 403);
  const dry = await request("/messages", "POST", '{"id":"1","text":"hello"}');
  assert.equal((await dry.json()).status, "dry_run");
  fake.emit("disconnected");
  assert.equal((await request("/health")).status, 200);
  assert.equal((await request("/groups")).status, 503);
});
