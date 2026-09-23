// Local administration over docker exec; no host-facing gateway port.
let input = "";
for await (const chunk of process.stdin) input += chunk.toString();
const request = JSON.parse(input) as { path: string; method?: string; body?: unknown };
const response = await fetch(`http://127.0.0.1:3000${request.path}`, {
  method: request.method ?? "GET",
  headers: { "Content-Type": "application/json" },
  body: request.body ? JSON.stringify(request.body) : undefined,
  signal: AbortSignal.timeout(55000),
});
const result = await response.text();
if (!response.ok) { console.error(result); process.exitCode = 1; }
else process.stdout.write(result);
