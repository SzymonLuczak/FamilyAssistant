import { test } from "node:test";
import assert from "node:assert/strict";
import { once } from "node:events";
import type { AddressInfo } from "node:net";
import { createApp } from "../src/app.js";

test("starts without credentials; health works and sending is unavailable", async (t) => {
  const server = createApp();
  server.listen(0, "127.0.0.1");
  await once(server, "listening");
  t.after(() => new Promise<void>((resolve, reject) => server.close(error => error ? reject(error) : resolve())));
  const base = `http://127.0.0.1:${(server.address() as AddressInfo).port}`;
  const health = await fetch(`${base}/health`);
  assert.equal(health.status, 200);
  assert.deepEqual(await health.json(), { status: "healthy" });
  const status = await fetch(base);
  assert.equal((await status.json()).integrations, "disabled");
  assert.equal((await fetch(`${base}/messages`, { method: "POST" })).status, 404);
  assert.equal((await fetch(`${base}/auth/qr`)).status, 404);
});
