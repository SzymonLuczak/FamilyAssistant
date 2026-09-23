import { createRequire } from "node:module";
import { execFileSync } from "node:child_process";
import type { WhatsAppPort } from "./gateway.js";
import { readGroupSummaries } from "./group-reader.js";

// Keep upstream's inconsistent declaration files behind our tested adapter contract.
const whatsapp = createRequire(import.meta.url)("whatsapp-web.js") as {
  Client: new (options: Record<string, unknown>) => WhatsAppPort & {
    pupPage: { evaluate: (fn: typeof readGroupSummaries) => ReturnType<WhatsAppPort["getChats"]> };
  };
  LocalAuth: new (options: { dataPath: string }) => unknown;
};

export function createClient(): WhatsAppPort {
  const executablePath = process.env.CHROMIUM_PATH ?? "/usr/bin/chromium";
  const version = execFileSync(executablePath, ["--version"], { encoding: "utf8" }).match(/\d+\.\d+\.\d+\.\d+/)?.[0];
  if (!version) throw new Error("Cannot determine Chromium version");
  const client = new whatsapp.Client({
    // WhatsApp's compatibility page does not accept the HeadlessChrome product name.
    userAgent: `Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/${version} Safari/537.36`,
    webVersionCache: { type: "none" },
    deviceName: "Family Assistant Dell",
    authStrategy: new whatsapp.LocalAuth({ dataPath: process.env.WHATSAPP_SESSION_PATH ?? "/app/.wwebjs_auth" }),
    puppeteer: {
      executablePath,
      headless: true,
      timeout: 60000,
      args: ["--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage"],
    },
    authTimeoutMs: 60000,
    // Keep the interactive pairing page usable while the user reaches for their phone.
    qrMaxRetries: 0,
  });
  client.getChats = () => client.pupPage.evaluate(readGroupSummaries);
  return client;
}
