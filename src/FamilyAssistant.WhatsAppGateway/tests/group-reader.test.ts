import { test } from "node:test";
import assert from "node:assert/strict";
import { readGroupSummaries } from "../src/group-reader.js";

test("group discovery uses names and IDs without serializing private chat data", () => {
  const scope = globalThis as any;
  const previous = scope.require;
  scope.require = (name: string) => {
    assert.equal(name, "WAWebCollections");
    return { Chat: { getModelsArray: () => [
      { id: { server: "g.us", _serialized: "1@g.us" }, formattedTitle: "Grupa testowa",
        serialize: () => { throw new Error("Must not serialize participants or messages"); } },
      { id: { server: "c.us", _serialized: "2@c.us" }, name: "Private contact" },
      { id: { server: "newsletter", _serialized: "3@newsletter" }, name: "Channel" },
      { id: null },
    ] } };
  };
  try {
    assert.deepEqual(readGroupSummaries(), [{ isGroup: true, id: { _serialized: "1@g.us" }, name: "Grupa testowa" }]);
  } finally { scope.require = previous; }
});
