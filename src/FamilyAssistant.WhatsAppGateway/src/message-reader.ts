// Runs inside WhatsApp Web. Returns only the last text messages of one chat,
// with the fields the gateway needs to detect numeric shopping replies.
// WhatsApp Web keeps messages of unopened chats out of memory, so load them first.
export async function readRecentMessages(chatId: string) {
  const req = (globalThis as any).require;
  const collections = req("WAWebCollections");
  const chat = collections.Chat.get(chatId)
    ?? collections.Chat.getModelsArray().find((c: any) => c?.id?._serialized === chatId);
  if (!chat?.msgs?.getModelsArray) return { found: false, loaded: 0, types: {}, messages: [] };
  let loaded = 0;
  try {
    const loader = req("WAWebChatLoadMessages");
    for (let n = 0; n < 3 && chat.msgs.getModelsArray().length < 15; n++) {
      const earlier = await loader.loadEarlierMsgs({ chat });
      if (!earlier?.length) break;
      loaded += earlier.length;
    }
  } catch { /* fall back to whatever is in memory */ }
  const types: Record<string, number> = {};
  for (const m of chat.msgs.getModelsArray().slice(-15)) types[String(m?.type)] = (types[String(m?.type)] ?? 0) + 1;
  // WhatsApp Web ids are MsgKey/Wid objects; depending on version they expose _serialized or only toString().
  const wid = (value: any): string | undefined => {
    if (typeof value === "string") return value;
    if (typeof value?._serialized === "string") return value._serialized;
    const text = typeof value?.toString === "function" ? value.toString() : "";
    return text && text !== "[object Object]" ? text : undefined;
  };
  const messages = chat.msgs.getModelsArray()
    .filter((m: any) => m?.type === "chat" && typeof m.body === "string")
    .slice(-15)
    .map((m: any) => ({
      id: { _serialized: wid(m.id), remote: wid(m.id?.remote), fromMe: !!m.id?.fromMe },
      body: m.body.length <= 100 ? m.body : "",
      timestamp: typeof m.t === "number" ? m.t : undefined,
    }))
    .filter((m: any) => typeof m.id._serialized === "string");
  return { found: true, loaded, types, messages };
}
