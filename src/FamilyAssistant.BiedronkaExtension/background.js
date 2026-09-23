import { receiptTarget } from './rules.mjs';
const period = 'biedronka-check';
// Accepts "Szymon", "Cześć Szymon!" or "SZYMON" — only the name itself is compared.
const normalizeName = value => String(value).normalize('NFKC').replace(/[\u200B-\u200D\uFEFF]/g, '').replace(/\s+/g, ' ').trim()
  .toLocaleLowerCase('pl').replace(/^cze[sś][cć]\s*,?\s*/u, '').replace(/[!.,\s]+$/u, '');
const pause = ms => new Promise(r => setTimeout(r, ms));
async function status(message, error = false) {
  await chrome.storage.local.set({ status: message, updatedAt: new Date().toISOString() });
  await chrome.action.setBadgeText({ text: error ? '!' : '' });
}
async function configureAlarm() {
  const { enabled } = await chrome.storage.local.get('enabled');
  if (enabled && !await chrome.alarms.get(period)) await chrome.alarms.create(period, { periodInMinutes: 360 });
  if (!enabled) await chrome.alarms.clear(period);
}
async function run(manual) {
  const { lease } = await chrome.storage.session.get('lease');
  if (lease && Date.now() - lease < 3 * 60 * 1000) {
    if (manual) await status(`Poprzednie sprawdzanie jeszcze trwa (od ${new Date(lease).toLocaleTimeString('pl-PL')}). Spróbuj za kilka minut.`, true);
    return;
  }
  if (manual) await status('Uruchamiam pobieranie…');
  await chrome.storage.session.set({ lease: Date.now() });
  let ownedTab;
  try {
    const config = await chrome.storage.local.get(['account', 'expectedName', 'completed', 'pending']);
    if (!config.account || !config.expectedName) throw Error('Najpierw zapisz konto i imię właściciela.');
    let tab;
    if (manual) {
      [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
      if (!tab?.url?.startsWith('https://moja.biedronka.pl/panel/paragons')) throw Error('Otwórz historię transakcji Biedronki i ponów pobieranie.');
    } else {
      tab = await chrome.tabs.create({ url: 'https://moja.biedronka.pl/panel/paragons', active: false });
      ownedTab = tab.id;
      for (let n = 0; n < 60; n++) { const t = await chrome.tabs.get(tab.id); if (t.status === 'complete') break; await pause(500); }
    }
    await status('Sprawdzam historię…');
    const [result] = await chrome.scripting.executeScript({ target: { tabId: tab.id }, files: ['collector.js'] });
    if (!result?.result) throw Error('Odczyt nie powiódł się. Otwórz Biedronkę i sprawdź logowanie lub weryfikację.');
    const { greeting, urls, rangeLimited } = result.result;
    const found = [].concat(greeting);
    const expected = normalizeName(config.expectedName);
    const matches = found.some(name => { const n = normalizeName(name); return n === expected || n.split(' ')[0] === expected; });
    if (!matches) throw Error(`Zalogowano inne konto niż skonfigurowane (strona: „${found.join('”, „')}”, ustawienia: „${config.expectedName}”). Pobieranie zatrzymane.`);
    const completed = config.completed || {};
    // Recover download completion after a service-worker restart without assuming that starting it was success.
    for (const [key, id] of Object.entries(config.pending || {})) {
      const [download] = await chrome.downloads.search({ id });
      if (download?.state === 'complete' && download.exists && download.mime !== 'text/html') completed[key] = true;
      else if (download?.state === 'in_progress') throw Error('Poprzednie pobieranie jeszcze trwa.');
    }
    await chrome.storage.local.set({ completed, pending: {} });
    let count = 0;
    for (const url of urls) {
      const target = receiptTarget(url, config.account);
      const key = `${config.account}/${target.id}`;
      if (completed[key]) continue;
      const id = await chrome.downloads.download({ url, filename: target.filename, conflictAction: 'overwrite', saveAs: false });
      await chrome.storage.local.set({ pending: { [key]: id } });
      let done = false;
      for (let n = 0; n < 90; n++) {
        const [download] = await chrome.downloads.search({ id });
        if (download?.state === 'interrupted') throw Error('Pobieranie przerwane. Sprawdź Biedronkę i katalog pobierania.');
        if (download?.state === 'complete') { if (download.mime === 'text/html' || !download.exists) throw Error('Pobrano stronę logowania zamiast paragonu. Zaloguj się ponownie.'); done = true; break; }
        await pause(500);
      }
      if (!done) throw Error('Pobieranie trwa zbyt długo. Ponów sprawdzenie później.');
      completed[key] = true; count++;
      await chrome.storage.local.set({ completed, pending: {} });
    }
    await status(`Zapisano ${count} nowych plików. JSON-y sprawdzi Family Assistant, PDF-y wymagają OCR.${rangeLimited ? ' Zakres ma co najmniej 50 transakcji — wybierz starsze daty i pobierz ponownie.' : ''}`);
    if (ownedTab) await chrome.tabs.remove(ownedTab);
  } catch (error) {
    await status(error.message || 'Sprawdzenie nie powiodło się. Zaloguj się w Biedronce.', true);
    // Keep a failed tab available for the user; never solve or alter a challenge.
  } finally { await chrome.storage.session.remove('lease'); }
}
chrome.runtime.onInstalled.addListener(configureAlarm);
chrome.runtime.onStartup.addListener(async () => { await configureAlarm(); const { enabled } = await chrome.storage.local.get('enabled'); if (enabled) await run(false); });
chrome.alarms.onAlarm.addListener(alarm => { if (alarm.name === period) void run(false); });
chrome.runtime.onMessage.addListener((message, sender, reply) => {
  if (sender.id !== chrome.runtime.id) return;
  if (message.type === 'configure') { configureAlarm().then(() => reply({ ok: true })); return true; }
  if (message.type === 'run') { run(true).catch(error => status('Błąd dodatku: ' + (error?.message || error), true)); reply({ ok: true }); }
});
