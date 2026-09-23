import { test } from "node:test";
import assert from "node:assert/strict";
import { readRecentMessages } from "../src/message-reader.js";

test("recent message reader returns only text messages of the requested chat", async () => {
  const scope = globalThis as any;
  const previous = scope.require;
  const msgs: any[] = [
    { type: "image", body: "", id: { _serialized: "i", remote: { _serialized: "g@g.us" } }, t: 1 },
    { type: "chat", body: "2-4", id: { _serialized: "a", remote: { _serialized: "g@g.us" }, fromMe: true }, t: 5 },
    { type: "chat", body: "1", id: { remote: { toString: () => "g@g.us" }, toString: () => "false_g@g.us_B" }, t: 6 },
  ];
  scope.require = (name: string) => name === "WAWebChatLoadMessages" ? { loadEarlierMsgs: async () => [] } : ({ Chat: { get: (id: string) => id === "g@g.us" ? { msgs: { getModelsArray: () => msgs } } : undefined, getModelsArray: () => [] } });
  try {
    assert.deepEqual((await readRecentMessages("g@g.us")).messages, [{ id: { _serialized: "a", remote: "g@g.us", fromMe: true }, body: "2-4", timestamp: 5 },
      { id: { _serialized: "false_g@g.us_B", remote: "g@g.us", fromMe: false }, body: "1", timestamp: 6 }]);
    assert.equal((await readRecentMessages("other@g.us")).found, false);
  } finally { scope.require = previous; }
});
