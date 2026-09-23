# 0002 — WhatsApp, Milestone 2

Gateway używa whatsapp-web.js 1.34.7, LocalAuth oraz Chromium z obrazu Debian.
Sesja, wybór grupy i rejestr deduplikacji są w wolumenie whatsapp-session.
Wewnętrzne API jest dostępne tylko w Dockerze. Lokalna administracja odbywa się
przez scripts/whatsapp.ps1 i docker exec. QR jest zwracany jako SVG z no-store,
nie jest logowany; zapisany lokalny plik jest w ignorowanym secrets/.

Kontener ma osobną sieć wyjściową do WhatsApp, pozostaje użytkownikiem node
i korzysta z init do sprzątania procesów Chromium. Chromium działa z no-sandbox
w kontenerze; nie ma dostępu do hostowego profilu przeglądarki.

Po problemie z parowaniem wyłączono narzucany przez bibliotekę identyfikator
Chrome 101 (wersja identyfikatora Chrome jest odczytywana z zainstalowanego Chromium)
oraz cache wersji Web. Próba domyślnego identyfikatora HeadlessChrome kończyła się
timeoutem podczas inicjalizacji. Ustawiono stały hostname kontenera, aby odtworzenie
kontenera nie zmieniało nazwy hosta zapisanej w blokadzie profilu Chromium.
Core udostępnia lokalną stronę /whatsapp/pair z odświeżaniem QR co 2 sekundy.
Proxy udostępnia tylko odczyt QR, nie operacje wysyłki ani listę grup.
Nie stanowi to dowodu usunięcia błędu parowania przed ponownym skanowaniem.

Po udanym parowaniu standardowe getChats() nie działało z aktualnym WhatsApp Web.
Adapter pobiera teraz wyłącznie identyfikatory i nazwy grup z kolekcji Chat,
bez serializowania wiadomości lub uczestników przez getChatModel().
Odczyt pozostaje zależny od nieoficjalnego modelu WhatsApp Web i jest izolowany
w group-reader.ts. Potwierdzono działanie na sparowanym koncie oraz testem
wykluczającym kontakty prywatne i kanały. Nazwa i ID wybranej prywatnej grupy
są przechowywane poza repozytorium.

Liveness /health pozostaje 200 podczas rozłączenia. /status pokazuje rzeczywisty
stan klienta. Start jest jawny (/auth/start), opcjonalny autostart włącza
WHATSAPP_AUTO_CONNECT=true. Sesja nie jest kasowana przy zatrzymaniu.
Nie ma automatycznego logout ani usuwania danych. Rozłączenie/auth_failure
wymaga sprawdzenia statusu i jawnego ponownego startu.

Odczyt grup: najwyżej 3 próby z krótkim backoffem, timeout 15 s na operację,
30-sekundowy circuit breaker po 3 nieudanych wywołaniach. Timeout nie powoduje
równoległego ponowienia tej samej operacji w danym wywołaniu.

Wiadomości: tylko wybrana i wcześniej zweryfikowana grupa. Domyślny dry-run
zapisuje hash i identyfikator, bez treści. Idempotency ID jest wymagany.
Zapis attempting poprzedza wysłanie; błąd/timeout daje unknown. Nigdy nie
ponawiamy automatycznie wysyłki z nieznanym wynikiem. Crash z attempting także
blokuje duplikat. Dry-run nie zmienia się w wysyłkę po włączeniu flagi.
Rejestr nie jest jeszcze pełnym outboxem i nie odtwarza starych wiadomości.
Nie ma automatycznych nadawców ani obsługi rozmów przychodzących.

Adapter izoluje wadliwe deklaracje typów biblioteki za własnym interfejsem;
sprawdzanie typów kodu i testów pozostaje włączone.

Audyt npm wykrył 5 wysokich ostrzeżeń w łańcuchu Puppeteer/extract-zip.
Problem dotyczy rozpakowywania pobieranej przeglądarki. Pobieranie Puppeteer
jest wyłączone, Chromium instalujemy przez apt. Nie stosujemy automatycznego
downgrade ani niezweryfikowanej zmiany głównej wersji Puppeteer. Ostrzeżenia
pozostają do usunięcia po zgodnej aktualizacji zależności.

Źródła: https://wwebjs.dev/guide/creating-your-bot/authentication
oraz https://docs.wwebjs.dev/Client.html.
