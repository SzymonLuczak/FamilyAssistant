import http from 'node:http';
import { spawn } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { mkdir, writeFile, readFile, stat, rename, unlink } from 'node:fs/promises';
import puppeteer from 'puppeteer-core';

const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
const accounts = [1, 2].map(id => ({ id, label: process.env[`ACCOUNT_${id}_LABEL`] || `Konto ${id}`, status: 'starting', browser: null, page: null, lastCheck: null, imported: 0 }));
const children = [];
await mkdir('/state', { recursive: true, mode: 0o700 });
const token = randomBytes(32).toString('hex');
await writeFile('/state/vnc-tokens', accounts.map(a => `${token}-${a.id}: 127.0.0.1:${5900 + a.id}`).join('\n') + '\n', { mode: 0o600 });
function launch(command, args) {
  const child = spawn(command, args, { stdio: 'ignore' });
  children.push(child);
  child.on('error', () => { console.error('Required gateway process unavailable'); process.exit(1); });
  return child;
}
for (const a of accounts) {
  await mkdir(`/state/account-${a.id}`, { recursive: true, mode: 0o700 });
  // One container owns these profiles. Only stale process-lock files are removed;
  // session/cookie data stays untouched when the container is recreated.
  for (const path of [`/tmp/.X${a.id}-lock`, `/tmp/.X11-unix/X${a.id}`, ...['SingletonLock', 'SingletonCookie', 'SingletonSocket'].map(name => `/state/account-${a.id}/${name}`)]) {
    await unlink(path).catch(error => { if (error.code !== 'ENOENT') throw error; });
  }
  await mkdir(`/state/download-${a.id}`, { recursive: true, mode: 0o700 });
  await mkdir(`/imports/account-${a.id}`, { recursive: true });
  launch('Xvfb', [`:${a.id}`, '-screen', '0', '1280x900x24', '-nolisten', 'tcp']);
}
await sleep(1500);
for (const a of accounts) launch('x11vnc', ['-display', `:${a.id}`, '-rfbport', String(5900 + a.id), '-localhost', '-forever', '-shared', '-nopw', '-quiet']);
launch('websockify', ['--web=/usr/share/novnc', '--token-plugin=TokenFile', '--token-source=/state/vnc-tokens', '6080']);

async function collect(a) {
  const page = a.page;
  a.status = 'checking';
  await page.goto('https://moja.biedronka.pl/panel/paragons', { waitUntil: 'domcontentloaded', timeout: 45000 });
  if (!page.url().startsWith('https://moja.biedronka.pl/panel/')) { a.status = 'login_required'; return; }
  await page.waitForFunction(() => { const b = [...document.querySelectorAll('button')].find(b => b.textContent.trim().toUpperCase() === 'ZASTOSUJ'); return b && !b.disabled; }, { timeout: 30000 });
  // The default two-week window overlaps successive checks. Stable IDs deduplicate downloads.
  // Historical backfill is separate; this job must never claim that the full history was fetched.
  let manifest = [];
  try { manifest = JSON.parse(await readFile(`/state/manifest-${a.id}.json`, 'utf8')); } catch (e) { if (e.code !== 'ENOENT') throw e; }
  for (let pageNumber = 1; pageNumber <= 5; pageNumber++) {
  const urls = await page.$$eval('a[href*="download/json/"]', es => es.map(e => e.href));
  for (const url of [...new Set(urls)]) {
    const parsed = new URL(url);
    if (parsed.origin !== 'https://moja.biedronka.pl' || !/^\/panel\/download\/json\/\d+$/.test(parsed.pathname)) continue;
    const id = parsed.pathname.split('/').at(-1);
    if (manifest.includes(id)) continue;
    const expected = `/state/download-${a.id}/paragon_${id}.json`;
    await page.evaluate(href => { const link = [...document.querySelectorAll('a')].find(e => e.href === href); if (!link) throw new Error('download link absent'); link.click(); }, url);
    let ready = false;
    for (let attempt = 0; attempt < 60; attempt++) {
      await sleep(500);
      try { const info = await stat(expected); if (info.size > 0 && info.size <= 8 * 1024 * 1024) { JSON.parse(await readFile(expected, 'utf8')); ready = true; break; } } catch {}
    }
    if (!ready) throw new Error('download_incomplete');
    const destination = `/imports/account-${a.id}/${id}.json`;
    await writeFile(destination + '.tmp', await readFile(expected), { mode: 0o600 });
    await rename(destination + '.tmp', destination);
    manifest.push(id);
    await writeFile(`/state/manifest-${a.id}.json.tmp`, JSON.stringify(manifest), { mode: 0o600 });
    await rename(`/state/manifest-${a.id}.json.tmp`, `/state/manifest-${a.id}.json`);
  }
  const next = await page.$(`span.page[data-page="${pageNumber + 1}"]`);
  if (!next || pageNumber === 5) break;
  const previous = await page.$eval('a[href*="download/"]', e => e.href);
  await next.click();
  await page.waitForFunction(old => { const first = document.querySelector('a[href*="download/"]'); return first && first.href !== old; }, { timeout: 30000 }, previous);
  }
  a.imported = manifest.length;
  a.lastCheck = new Date().toISOString();
  a.status = 'ready';
}

for (const a of accounts) {
  a.browser = await puppeteer.launch({ executablePath: '/usr/bin/chromium', headless: false, userDataDir: `/state/account-${a.id}`, defaultViewport: null,
    env: { ...process.env, DISPLAY: `:${a.id}` }, args: ['--no-sandbox', '--disable-dev-shm-usage', '--disable-gpu', '--window-size=1280,900', '--no-first-run'] });
  a.page = (await a.browser.pages())[0];
  const cdp = await a.page.createCDPSession();
  await cdp.send('Browser.setDownloadBehavior', { behavior: 'allow', downloadPath: `/state/download-${a.id}` });
  await a.page.goto('https://moja.biedronka.pl/oauth/login', { waitUntil: 'domcontentloaded', timeout: 45000 }).catch(() => {});
  a.status = 'login_required';
}

// The worker starts only after an explicit local POST; login windows remain undisturbed.
const busy = new Set();
async function check(a) {
  if (busy.has(a.id)) return;
  busy.add(a.id);
  try { await collect(a); }
  catch { a.status = 'check_failed'; }
  finally { busy.delete(a.id); }
}
let enabled = false;
try { enabled = (await readFile('/state/enabled', 'utf8')).trim() === 'true'; } catch {}
setInterval(() => { if (enabled) for (const a of accounts) void check(a); }, 6 * 60 * 60 * 1000);
if (enabled) for (const a of accounts) void check(a);

http.createServer(async (req, res) => {
  res.setHeader('Cache-Control', 'no-store');
  res.setHeader('Content-Type', 'application/json');
  if (req.url === '/health') return res.end('{"status":"ok"}');
  if (req.url === '/status' && req.method === 'GET') return res.end(JSON.stringify({ enabled, intervalHours: 6, historyComplete: false, accounts: accounts.map(({ id, label, status, lastCheck, imported }) => ({ id, label, status, lastCheck, downloaded: imported })) }));
  // Internal Docker API; only Core is allowed to expose links with its localhost and CSRF checks.
  if (req.url === '/login-links' && req.method === 'GET') return res.end(JSON.stringify(accounts.map(a => ({ id: a.id, label: a.label, path: `/vnc.html?autoconnect=1&resize=scale&path=${encodeURIComponent('websockify?token=' + token + '-' + a.id)}` }))));
  if (req.url === '/enable' && req.method === 'POST') {
    enabled = true;
    await writeFile('/state/enabled', 'true', { mode: 0o600 });
    for (const a of accounts) void check(a);
    return res.end('{"enabled":true}');
  }
  res.statusCode = 404; res.end('{}');
}).listen(3001, '0.0.0.0');

async function stop() { for (const a of accounts) await a.browser?.close().catch(() => {}); for (const child of children) child.kill(); process.exit(0); }
process.on('SIGTERM', stop); process.on('SIGINT', stop);
