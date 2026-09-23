// Runs inside WhatsApp Web. Read only the fields needed for group selection;
// avoid getChatModel(), which also loads participants and last-message metadata.
export function readGroupSummaries() {
  const collections = (globalThis as any).require("WAWebCollections");
  return collections.Chat.getModelsArray()
    .filter((chat: any) => chat.id?.server === "g.us")
    .map((chat: any) => ({
      isGroup: true,
      id: { _serialized: chat.id._serialized },
      name: chat.formattedTitle ?? chat.name ?? "",
    }))
    .filter((chat: any) => typeof chat.id._serialized === "string" && typeof chat.name === "string");
}
