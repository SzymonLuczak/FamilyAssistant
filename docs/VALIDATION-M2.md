# Weryfikacja Milestone 2 — 22.09.2026

Na docelowym Dellu zbudowano i uruchomiono obrazy Linux z Chromium 153,
Node.js 22, .NET 8 i Python 3.12. Wszystkie trzy usługi mają zdrowy status.
Smoke test sprawdza Core z hosta i gatewaye przez wewnętrzną sieć Dockera.

Testy automatyczne używają atrap WhatsApp, nigdy prawdziwych wysyłek:

- Node: 9/9 — API, QR, walidacja, wybór grupy, dry-run, deduplikacja po odtworzeniu
  gatewaya, niepewny wynik, timeout, limit retry i circuit breaker.
- Python: 2/2 — start i health endpoint.
- Core: 13/13 — start, raportowanie stanu i awarii gatewaya oraz strona QR.
  Łącznie 24/24 testy.

Rzeczywisty klient uruchomiono do etapu `qr`. Potwierdza to start Chromium,
połączenie z WhatsApp Web oraz generowanie kodu parowania. Nie jest to jeszcze
potwierdzenie zalogowanej sesji, listy rzeczywistych grup ani wysyłki.

Do ręcznego odbioru po stronie użytkownika pozostaje skan QR, wybór grupy,
próba dry-run, świadome włączenie wysyłki i ręczna wiadomość oraz restart
sparowanej sesji. WHATSAPP_SEND_ENABLED pozostaje false; żadnej wiadomości
do prawdziwej grupy nie wysłano. Nie wykonano git push.

Trwałość stanu adaptera jest sprawdzona testem odtworzenia obiektu z dysku.
Po późniejszym sparowaniu potwierdzono odtworzenie kontenera i powrót do ready
bez nowego QR. Naprawiono odczyt grup, sprawdzono 10/10 testów Node i zapisano
jedną grupę wskazaną przez użytkownika. Wysyłka nadal false; nie wykonano
rzeczywistej wysyłki. Łącznie aktualne zestawy zawierają 25 testów (Core 13,
Python 2, Node 10).

Audyt npm: 5 wysokich ostrzeżeń w łańcuchu Puppeteer/extract-zip, pozostawionych
jawnie w decyzji 0002. Pobieranie przeglądarki przez Puppeteer jest wyłączone;
Chromium pochodzi z apt. Ostrzeżenie Starlette/AnyIO z M1 pozostaje bez zmian.
