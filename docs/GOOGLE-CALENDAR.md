# Google Calendar — Milestone 3

Adres konfiguracji: http://localhost:8080/google. Używaj localhost, również podczas
logowania; adres przekierowania i host przeglądarki muszą się zgadzać.

## Jednorazowa konfiguracja Google

1. W Google Cloud wybierz istniejący projekt i włącz Google Calendar API.
2. W Google Auth Platform skonfiguruj ekran zgody; jeśli aplikacja jest w trybie
   Testing, dodaj swoje konto jako użytkownika testowego.
3. Utwórz klienta OAuth typu **Web application / Aplikacja internetowa**.
4. Dodaj dokładny redirect URI: `http://localhost:8080/google/callback`.
5. Pobierz JSON klienta i zapisz jako `secrets/google-oauth.json` w repozytorium.
   Nie używaj klucza API ani pliku konta usługi. Nie wklejaj sekretu do rozmów.
6. Otwórz stronę konfiguracji i wybierz „Połącz konto Google — tylko odczyt”.
7. Po zgodzie zaznacz od 1 do 20 kalendarzy i zapisz wybór. Przyciski Dzisiaj/Jutro
   pobierają bieżące wydarzenia, bez zapisu do Google i bez wysyłki WhatsApp.

Sekrety są montowane do Core tylko do odczytu. Pliki tokens.json i calendars.json
trafiają do wolumenu google-token, z prawami zapisu dla użytkownika app.
Zmiana portu Core wymaga odpowiedniego redirect URI w Google i Compose.

Przy wcześniejszym pustym wolumenie z M1 można jednorazowo naprawić właściciela:

```powershell
docker compose exec -T --user root family-core chown app:app /app/secrets/google
```

## Mapowanie osób

Skopiuj `config/calendar-members.example.json` do `config/calendar-members.json`.
Wstaw własne identyfikatory, imiona i aliasy. Ten prywatny plik jest ignorowany
przez Git. Przykład wpisu: `{"id":"child-1","name":"Ada","calendarAliases":["Adrianna"]}`.

Tytuł `[Ada] Basen` lub `[Adrianna] Basen` przypisuje wydarzenie do child-1.
Samo wystąpienie imienia jako części innego słowa nie przypisuje osoby.
Wiele tagów przypisuje wiele osób. Nie jest używany LLM. Konfiguracja rodziny
w YAML pozostaje zarezerwowana na reguły kolejnych milestone'ów; ten adapter
wczytuje mały plik JSON z mapowaniem kalendarza.

## API

- GET /google/status: konfiguracja/autoryzacja i zapisane kalendarze (bez tokenów).
- POST /google/connect: OAuth; formularz musi zawierać token CSRF.
- GET /google/callback: weryfikacja state/cookie i wymiana kodu.
- GET /google/calendars: kalendarze dostępne dla konta.
- POST /google/calendars: tablica ID, token CSRF w X-CSRF-TOKEN.
- GET /google/events/today oraz /google/events/tomorrow: znormalizowane wydarzenia.

Strefa domyślna: Europe/Warsaw. Zakres dnia uwzględnia DST (23/24/25 godzin).
Wydarzenia cykliczne są rozwijane przez Google; odwołane są pomijane.
Całodniowe mają startDate i endDateExclusive, bez sztucznej godziny.
Zwracane są externalId, calendarId, title, start/end lub daty, assignedMembers,
location oraz osobna lista errors. Błąd jednego kalendarza nie ukrywa pozostałych.

## Ograniczenia

Status authorized oznacza zapisany token, nie bieżący test dostępności Google.
Unieważniony refresh token wymaga ponownego logowania. Google może ograniczać
ważność tokenów aplikacji pozostającej w trybie Testing. Nie ma jeszcze
snapshotów, harmonogramu ani automatycznych wiadomości; to kolejne milestone'y.
Nie używaj `docker compose down -v`, jeśli chcesz zachować dane autoryzacji.

Źródła:
[OAuth dla aplikacji internetowych](https://developers.google.com/identity/protocols/oauth2/web-server),
[tworzenie klienta](https://developers.google.com/workspace/guides/create-credentials),
[events.list](https://developers.google.com/calendar/api/v3/reference/events/list).
