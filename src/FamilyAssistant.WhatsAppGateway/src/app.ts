import { createServer } from "node:http";
import QRCode from "qrcode";
import { Gateway, GatewayError } from "./gateway.js";

export function createApp(gateway?: Gateway) {
  return createServer(async (req, res) => {
    const path = new URL(req.url ?? "/", "http://localhost").pathname;
    res.setHeader("Content-Type", "application/json");
    res.setHeader("Cache-Control", "no-store");
    try {
    if (req.method === "GET" && path === "/health") {
      res.end(JSON.stringify({ status: "healthy" }));
    } else if (req.method === "GET" && path === "/") {
      res.end(JSON.stringify({ service: "FamilyAssistant.WhatsAppGateway", milestone: 2,
        integrations: gateway?.status().connection ?? "disabled" }));
    } else if (gateway && req.method === "GET" && path === "/status") {
      res.end(JSON.stringify(gateway.status()));
    } else if (gateway && req.method === "GET" && path === "/auth/qr") {
      const svg = await QRCode.toString(gateway.getQr(), { type: "svg" });
      res.setHeader("Content-Type", "image/svg+xml");
      res.end(svg);
    } else if (gateway && req.method === "GET" && path === "/inbox") {
      res.end(JSON.stringify(gateway.inbox()));
    } else if (gateway && req.method === "GET" && path === "/groups") {
      res.end(JSON.stringify(await gateway.groups()));
    } else if (gateway && req.method === "POST" && path === "/auth/start") {
      await gateway.start();
      res.statusCode = 202;
      res.end(JSON.stringify(gateway.status()));
    } else if (gateway && ["POST", "PUT"].includes(req.method ?? "") &&
      (path === "/config/group" || path === "/config/shopping-group" || path === "/inbox/ack" || path === "/messages" || path.startsWith("/messages/group/"))) {
      if (!req.headers["content-type"]?.startsWith("application/json")) throw new GatewayError("json_required", 415);
      let body = "";
      for await (const chunk of req) {
        body += chunk.toString();
        if (Buffer.byteLength(body) > 16384) throw new GatewayError("body_too_large", 413);
      }
      let data: Record<string, unknown>;
      try { data = JSON.parse(body); } catch { throw new GatewayError("invalid_json", 400); }
      if (!data || typeof data !== "object") throw new GatewayError("invalid_body", 400);
      if (path === "/config/shopping-group" && req.method === "PUT") {
        if (typeof data.groupId !== "string") throw new GatewayError("invalid_group", 400);
        await gateway.selectShoppingGroup(data.groupId);
        res.end(JSON.stringify(gateway.status()));
      } else if (path === "/inbox/ack" && req.method === "POST") {
        if (!Array.isArray(data.ids) || !data.ids.every(i => typeof i === "string")) throw new GatewayError("invalid_ids", 400);
        await gateway.acknowledge(data.ids as string[]);
        res.end(JSON.stringify({ acknowledged: data.ids.length }));
      } else if (path === "/config/group" && req.method === "PUT") {
        if (typeof data.groupId !== "string") throw new GatewayError("invalid_group", 400);
        await gateway.selectGroup(data.groupId);
        res.end(JSON.stringify(gateway.status()));
      } else if (req.method === "POST" && path.startsWith("/messages")) {
        if (typeof data.id !== "string" || typeof data.text !== "string") throw new GatewayError("invalid_message", 400);
        const groupId = path.startsWith("/messages/group/") ? decodeURIComponent(path.slice(16)) : undefined;
        res.end(JSON.stringify(await gateway.send(data.id, data.text, groupId)));
      } else throw new GatewayError("method_not_allowed", 405);
    } else {
      res.statusCode = 404;
      res.end(JSON.stringify({ error: "not_found" }));
    }
    } catch (error) {
      res.statusCode = error instanceof GatewayError ? error.status : 500;
      res.end(JSON.stringify({ error: error instanceof GatewayError ? error.code : "internal_error" }));
    }
  });
}
