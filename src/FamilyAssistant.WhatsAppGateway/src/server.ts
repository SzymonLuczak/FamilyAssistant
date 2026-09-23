import { createApp } from "./app.js";
import { Gateway } from "./gateway.js";
import { createClient } from "./client.js";

const port = Number(process.env.PORT ?? 3000);
if (!Number.isInteger(port) || port < 1 || port > 65535) {
  throw new Error("PORT must be an integer between 1 and 65535");
}
const gateway = new Gateway(createClient, process.env.WHATSAPP_STATE_PATH ?? "/app/.wwebjs_auth/gateway",
  process.env.WHATSAPP_SEND_ENABLED === "true");
await gateway.load();
const server = createApp(gateway);
if (process.env.WHATSAPP_AUTO_CONNECT === "true") await gateway.start();
server.listen(port, "0.0.0.0", () => console.log(`WhatsApp gateway skeleton listening on ${port}`));
for (const signal of ["SIGINT", "SIGTERM"] as const) {
  process.on(signal, () => {
    server.close(() => { void gateway.stop().finally(() => process.exit(0)); });
    setTimeout(() => process.exit(1), 20000).unref();
  });
}
