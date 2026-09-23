(async () => {
  const delay = ms => new Promise(r => setTimeout(r, ms));
  const links = () => [...document.querySelectorAll('a[href*="download/"]')].map(a => a.href);
  const first = () => links()[0];
  const ready = () => [...document.querySelectorAll('button')].some(b => b.textContent.trim().toUpperCase() === 'ZASTOSUJ' && !b.disabled);
  async function wait(test) { for (let n = 0; n < 80; n++) { if (test()) return; await delay(500); } throw Error('Nie udało się odczytać historii. Sprawdź stronę Biedronki.'); }
  if (location.origin !== 'https://moja.biedronka.pl' || location.pathname !== '/panel/paragons') throw Error('Zaloguj się bezpośrednio w Biedronce.');
  await wait(ready);
  // The header uses a decorative font and may transform case; collect every greeting variant.
  const text = (document.body.innerText + '\n' + document.body.textContent).normalize('NFC');
  const greetings = [...new Set([...text.matchAll(/cze[sś][cć]\s*,?\s+([^!\n]{1,60})!/giu)].map(m => m[1].trim()))];
  if (!greetings.length) throw Error('Nie potwierdzono zalogowanego konta (brak powitania „Cześć …!”).');
  const greeting = greetings;
  await wait(() => links().length || /brak transakcji|nie znaleziono|brak paragonów/i.test(document.body.innerText));
  const receipts = new Map();
  let pages = 0;
  for (let index = 0; index < 100; index++) {
    pages++;
    for (const url of links()) {
      const match = /\/download\/(json\/)?(\d{16})$/.exec(new URL(url).pathname);
      if (match && (!receipts.has(match[2]) || match[1])) receipts.set(match[2], url);
    }
    const current = document.querySelector('span.page.active, span.page.selected');
    const nextNumber = current ? Number(current.dataset.page) + 1 : index + 2;
    const next = document.querySelector(`span.page[data-page="${nextNumber}"]`);
    if (!next) return { greeting, urls: [...receipts.values()], pages, rangeLimited: receipts.size >= 50 };
    const previous = first(); next.click();
    await wait(() => first() && first() !== previous);
  }
  throw Error('Przerwano: nietypowa liczba stron.');
})()
