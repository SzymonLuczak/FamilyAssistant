export function receiptTarget(url, account) {
  if (!['account-1', 'account-2'].includes(account)) throw Error('Wybierz konto.');
  const parsed = new URL(url);
  const match = /^\/panel\/download\/(json\/)?(\d{16})$/.exec(parsed.pathname);
  if (parsed.origin !== 'https://moja.biedronka.pl' || !match || parsed.search || parsed.hash || parsed.username || parsed.password) throw Error('Nieprawidłowy adres paragonu.');
  const type = match[1] ? 'json' : 'pdf';
  return { id: `${match[2]}.${type}`, filename: `FamilyAssistant/${account}/${match[2]}.${type}` };
}
