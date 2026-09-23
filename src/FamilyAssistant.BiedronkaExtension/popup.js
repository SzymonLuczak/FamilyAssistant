const $ = id => document.getElementById(id);
const config = await chrome.storage.local.get(['account', 'expectedName', 'enabled']);
$('account').value = config.account || 'account-1'; $('name').value = config.expectedName || ''; $('enabled').checked = !!config.enabled;
$('save').onclick = async () => {
  if (!$('name').value.trim()) { $('status').textContent = 'Podaj imię z powitania konta.'; return; }
  await chrome.storage.local.set({ account: $('account').value, expectedName: $('name').value.trim(), enabled: $('enabled').checked });
  await chrome.runtime.sendMessage({ type: 'configure' }); $('status').textContent = 'Ustawienia zapisane.';
};
$('open').onclick = () => chrome.tabs.create({ url: 'https://moja.biedronka.pl/panel/paragons' });
$('run').onclick = async () => {
  $('status').textContent = 'Uruchamiam…';
  try { await chrome.runtime.sendMessage({ type: 'run' }); }
  catch (error) { $('status').textContent = 'Dodatek nie odpowiada: ' + error.message + '. Przeładuj go w chrome://extensions.'; }
};
async function refresh() { const { status, updatedAt } = await chrome.storage.local.get(['status', 'updatedAt']); $('status').textContent = (status || 'Jeszcze nie sprawdzano.') + (updatedAt ? '\n' + new Date(updatedAt).toLocaleString('pl-PL') : ''); }
document.querySelector('h2').textContent += ' v' + chrome.runtime.getManifest().version;
void refresh(); setInterval(refresh, 2000);
