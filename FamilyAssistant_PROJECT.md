# Family Assistant — specyfikacja projektu

## 1. Cel

Zbudować prywatnego rodzinnego asystenta działającego stale na domowym Dellu.

System ma automatycznie:
1. pobierać plan lekcji dzieci z VULCAN/UONET+,
2. pobierać wydarzenia z rodzinnego Google Calendar,
3. łączyć dane w jeden plan dnia,
4. uwzględniać rodzinne reguły logistyczne, np. kto kogo odbiera,
5. wysyłać czytelne wiadomości na wskazaną rodzinną grupę WhatsApp,
6. wykrywać zmiany i konflikty,
7. w kolejnych etapach analizować e-paragony Biedronki, wydatki oraz prowadzić inteligentną listę zakupów.

Projekt ma być prywatny, lokalny, modularny i łatwy do rozwijania przez OpenHands.

---

## 2. Założenia

- Host: Windows 11 Pro na dedykowanym Dellu.
- Uruchamianie: Docker Compose.
- Strefa czasowa: Europe/Warsaw.
- Język wiadomości: polski.
- System działa 24/7 i automatycznie wstaje po restarcie hosta.
- Nie publikować żadnych usług do Internetu bez wyraźnej potrzeby.
- Dane rodziny, tokeny i sesje nie mogą trafiać do repozytorium Git.
- Wszystkie integracje z zewnętrznymi serwisami muszą być odseparowane adapterami.
- WhatsApp i VULCAN traktować jako integracje podatne na zmiany.
- OpenHands może edytować kod i uruchamiać testy, ale nie może samodzielnie wykonywać `git push`, publikować obrazu ani usuwać danych bez potwierdzenia użytkownika.

---

## 3. Architektura MVP

### 3.1. FamilyAssistant.Core

Technologia:
- .NET 8 Worker Service
- C#
- Entity Framework Core
- SQLite
- Quartz.NET lub Hangfire do harmonogramu

Odpowiedzialność:
- harmonogram,
- agregacja danych,
- reguły rodzinne,
- detekcja konfliktów,
- generowanie treści wiadomości,
- przechowywanie stanu,
- deduplikacja powiadomień,
- komunikacja z adapterami.

### 3.2. FamilyAssistant.VulcanGateway

Technologia:
- Python 3
- FastAPI
- nieoficjalny klient UONET+/VULCAN, np. `vulcan-api`

Odpowiedzialność:
- autoryzacja VULCAN,
- pobieranie uczniów,
- pobieranie planu lekcji,
- pobieranie zmian/odwołanych zajęć, jeśli udostępnia je używany klient,
- normalizacja danych do własnego modelu DTO.

Gateway NIE może przekazywać do Core surowych danych zależnych od biblioteki VULCAN.

Przykładowe API wewnętrzne:

GET /health
GET /students
GET /students/{id}/schedule?date=YYYY-MM-DD

### 3.3. FamilyAssistant.WhatsAppGateway

Technologia:
- Node.js 22+
- TypeScript
- `whatsapp-web.js`
- `LocalAuth`
- Puppeteer/Chrome

Odpowiedzialność:
- utrzymywanie sesji WhatsApp Web,
- jednorazowe parowanie kodem QR,
- wyszukanie/skonfigurowanie docelowej grupy,
- wysyłanie wiadomości,
- opcjonalne odbieranie prostych komend z grupy,
- health-check połączenia.

Przykładowe API:

GET /health
GET /groups
POST /messages
POST /messages/group/{groupId}

Sesja WhatsApp musi być przechowywana na trwałym wolumenie Dockera.

UWAGA:
To integracja nieoficjalna. Kod ma zakładać, że WhatsApp Web może zmienić zachowanie. Brak połączenia z WhatsApp nie może zatrzymywać pobierania danych z VULCAN/Google.

### 3.4. Google Calendar

Integracja wykonywana bezpośrednio przez FamilyAssistant.Core.

- Google Calendar API
- OAuth 2.0
- zakres tylko do odczytu
- token przechowywany poza repozytorium
- konfiguracja listy kalendarzy, które Family Assistant ma uwzględniać

MVP nie modyfikuje wydarzeń Google Calendar.

---

## 4. Model domeny

### FamilyMember

- Id
- Name
- Type: Adult / Child
- VulcanStudentId nullable
- CalendarAliases[]
- Enabled

### SchoolDay

- FamilyMemberId
- Date
- Lessons[]
- FirstLessonStart
- LastLessonEnd
- SourceUpdatedAt

### Lesson

- Subject
- Start
- End
- Status: Planned / Cancelled / Changed / Unknown
- Room nullable
- Teacher nullable

### CalendarEvent

- ExternalId
- CalendarId
- Title
- Start
- End
- IsAllDay
- AssignedMembers[]
- Location nullable

### FamilyRule

Przykłady:
- we wtorek Tobiasza odbiera Aniela,
- określone wydarzenie dotyczy konkretnego dziecka,
- ile minut przed wydarzeniem przypominać,
- minimalny czas pomiędzy końcem szkoły a kolejnym wydarzeniem.

### Notification

- Id
- Type
- Date
- PayloadHash
- SentAt
- Status
- Error nullable

PayloadHash służy do tego, aby nie wysyłać drugi raz identycznej wiadomości.

---

## 5. Konfiguracja rodziny

Utworzyć plik:

`config/family.yaml`

Przykład:

```yaml
timezone: Europe/Warsaw

family:
  - name: Agata
    type: child
    vulcanStudentId: CHANGE_ME

  - name: Aniela
    type: child
    vulcanStudentId: CHANGE_ME

  - name: Jeremiasz
    type: child
    vulcanStudentId: CHANGE_ME

  - name: Tobiasz
    type: child
    vulcanStudentId: CHANGE_ME

rules:
  pickups:
    - day: Tuesday
      child: Tobiasz
      pickedUpBy: Aniela

notifications:
  morningSummary: "07:00"
  tomorrowSummary: "20:00"
  changeCheckIntervalMinutes: 30
```

Nie wpisywać sekretów do `family.yaml`.

---

## 6. Funkcjonalność MVP

### F1 — pobranie planu szkolnego

Każdego ranka system:
- pobiera plan wszystkich skonfigurowanych dzieci,
- ustala pierwszą i ostatnią aktywną lekcję,
- ignoruje odwołane zajęcia przy wyliczaniu godziny końca,
- zapisuje snapshot planu.

Jeżeli VULCAN jest niedostępny:
- system korzysta z ostatniego poprawnego snapshotu,
- oznacza dane jako potencjalnie nieaktualne,
- nie spamuje błędami na grupie rodzinnej.

### F2 — wydarzenia Google Calendar

Pobrać:
- wszystkie wydarzenia na dzisiaj,
- wszystkie wydarzenia na jutro,
- opcjonalnie kolejne 48 godzin.

Obsłużyć:
- wydarzenia godzinowe,
- wydarzenia całodniowe,
- kilka wskazanych kalendarzy.

### F3 — poranny plan dnia

Domyślnie o 07:00 wygenerować jedną wiadomość.

Przykład:

👨‍👩‍👧‍👦 *Plan rodziny — wtorek, 22 września*

🏫 *Szkoła*
Agata — koniec 13:25
Aniela — koniec 14:20
Jeremiasz — koniec 15:15
Tobiasz — koniec 12:30

📅 *Po szkole*
16:00 — Tobiasz: trening
17:30 — Agata: dentysta
19:00 — spotkanie

🚗 *Odbiory*
Tobiasza odbiera dziś Aniela.

⚠️ *Uwaga*
Jeremiasz kończy o 15:15, a następne wydarzenie zaczyna się o 15:30.

Jeżeli nie ma wydarzeń, sekcja nie powinna być pokazywana.

### F4 — detekcja zmian

Co 30 minut w określonych godzinach, np. 06:30–17:00:
- ponownie sprawdzić plan,
- porównać ze snapshotem.

Powiadamiać wyłącznie, jeżeli:
- zmieniła się godzina zakończenia szkoły,
- odwołano ostatnią aktywną lekcję,
- dodano istotne wydarzenie,
- zmiana powoduje konflikt logistyczny.

Przykład:

⚠️ *Zmiana planu*
Jeremiasz kończy dziś o 14:20 zamiast 15:15.
Ostatnia lekcja została odwołana.

### F5 — plan na jutro

Domyślnie o 20:00:

🌙 *Jutro — środa*

Agata — koniec 14:20
Aniela — koniec 13:25
Jeremiasz — koniec 15:15
Tobiasz — koniec 13:25

📅 16:30 — trening Tobiasza
📅 18:00 — spotkanie

Nie wysyłać wiadomości, jeśli funkcja jest wyłączona w konfiguracji.

### F6 — konflikty

MVP wykrywa:
- wydarzenie zaczynające się przed końcem lekcji,
- wydarzenie zaczynające się w krótkim czasie po lekcjach.

Konfigurowalny próg:

```yaml
rules:
  minimumTravelMinutes: 30
```

Przykład:

⚠️ Tobiasz kończy o 14:20, a trening zaczyna się o 14:35 — tylko 15 min przerwy.

---

## 7. Komendy WhatsApp — etap 1.1

Bot reaguje WYŁĄCZNIE w skonfigurowanej grupie rodzinnej.

Obsługiwane komendy:

`plan`
- aktualny plan dnia

`jutro`
- plan na jutro

`kiedy Tobiasz`
- godzina zakończenia lekcji

`dzisiaj`
- szkoła + kalendarz + odbiory

`status`
- czy źródła VULCAN, Google i WhatsApp działają

Nie reagować na zwykłą rozmowę.

Nie używać LLM do interpretowania wiadomości w pierwszej wersji.
Najpierw stosować deterministyczny parser komend.

---

## 8. Odporność na błędy

Każdy adapter musi posiadać:
- timeout,
- retry z ograniczeniem,
- health endpoint,
- logowanie błędów,
- circuit breaker lub analogiczny mechanizm.

Awaria pojedynczej integracji nie może zatrzymywać Core.

Przykład:
jeżeli VULCAN nie działa, Google Calendar nadal ma być pobrany, a system może wysłać plan z oznaczeniem:

`⚠️ Nie udało się potwierdzić dzisiejszego planu VULCAN.`

Nie wysyłać technicznych stack trace na WhatsApp.

---

## 9. Bezpieczeństwo

Sekrety:
- Google OAuth client secret,
- Google refresh token,
- dane/sesja VULCAN,
- sesja WhatsApp

przechowywać w:
- lokalnym `.env`,
- Docker secrets lub lokalnym bezpiecznym katalogu.

`.gitignore` musi obejmować:
- `.env`
- `secrets/`
- `.wwebjs_auth/`
- tokeny Google
- lokalną bazę danych, jeśli zawiera dane prywatne
- eksporty VULCAN/Biedronka

OpenHands nie może wypisywać sekretów do logów ani commitować ich do repo.

Porty gatewayów powinny być dostępne wyłącznie w wewnętrznej sieci Docker Compose.

---

## 10. Logging

Logować:
- czas rozpoczęcia joba,
- źródło danych,
- powodzenie/błąd,
- liczbę pobranych rekordów,
- hash wysłanej wiadomości,
- status WhatsApp.

Nie logować:
- tokenów,
- PIN-ów,
- cookies,
- pełnych danych uwierzytelniających.

Logi rotowane, np. maksymalnie 30 dni.

---

## 11. Docker Compose

Usługi:

```text
family-core
vulcan-gateway
whatsapp-gateway
```

Wspólna wewnętrzna sieć:

`family-assistant-network`

Wolumeny:
- `family-data`
- `whatsapp-session`
- `vulcan-session`
- `google-token`

Core nie powinien być publicznie wystawiony poza hosta.

---

## 12. Health checks

Core powinien mieć możliwość ustalenia:

```json
{
  "core": "ok",
  "vulcan": "ok",
  "googleCalendar": "ok",
  "whatsapp": "ok"
}
```

Jeżeli WhatsApp jest rozłączony:
- wiadomości trafiają do kolejki,
- po ponownym połączeniu system wysyła tylko wiadomości nadal aktualne.

Nie wysyłać starych przypomnień po kilku godzinach.

---

# ETAP 2 — Biedronka i wydatki

## 13. Import e-paragonów

Na początku NIE automatyzować logowania do Biedronki.

Oficjalny e-paragon można pobrać jako JSON, dlatego MVP zakupowe ma obserwować katalog:

`imports/biedronka/`

Po pojawieniu się nowego `.json`:
- walidacja,
- odczyt danych,
- wykrycie duplikatu,
- import pozycji,
- archiwizacja pliku do `processed/`.

Model:

### Receipt

- Id
- Store
- PurchasedAt
- Total
- ExternalHash

### ReceiptItem

- ReceiptId
- RawName
- NormalizedProductId
- Quantity
- Unit
- UnitPrice
- TotalPrice

---

## 14. Normalizacja nazw produktów

Ten sam produkt może występować pod różnymi nazwami.

Przykład:

```text
MLEKO UHT 3.2 1L
MLEKO 3,2% UHT
MLEKO POLSKIE 1L
```

System powinien pozwolić mapować je do:

`Mleko 3.2% 1L`

Na początku:
- reguły tekstowe,
- aliasy zapisane w bazie.

LLM może później sugerować mapowania, ale nie powinien automatycznie zmieniać danych bez potwierdzenia.

---

# ETAP 3 — inteligentna lista zakupów

## 15. ShoppingListItem

- ProductId
- Status: Suggested / Confirmed / Purchased / Dismissed
- Reason
- AddedAt
- Confidence 0–1

### Przykładowe powody

- ręcznie dodany,
- przewidywane zużycie,
- długo niekupowany,
- cykliczny produkt,
- potwierdzony brak.

---

## 16. Szacowanie zużycia

Dla powtarzalnych produktów wyliczać:

- średni odstęp między zakupami,
- medianę odstępu,
- średnią kupowaną ilość,
- ostatnią datę zakupu.

Nie twierdzić, że produktu na pewno nie ma.

Używać języka:

- `prawdopodobnie się kończy`,
- `warto sprawdzić`,
- `zwykle kupujemy co X dni`.

Przykład:

🛒 *Może się kończyć*
Mleko — ostatni zakup 4 dni temu; zwykle kupujemy co 3–5 dni.
Chleb — ostatni zakup 2 dni temu.
Tabletki do zmywarki — warto sprawdzić zapas.

---

## 17. Komendy zakupowe WhatsApp

Etap 3:

`dodaj mleko`
`usuń mleko`
`kupione mleko`
`lista zakupów`
`co się kończy`
`ile wydaliśmy w tym miesiącu`
`ile wydaliśmy na nabiał`

Bot powinien potwierdzić zmianę:

`✅ Mleko dodane do listy zakupów.`

---

# ETAP 4 — automatyczny import Biedronki

Dopiero po stabilnym działaniu ręcznego importu JSON.

Cel:
- sprawdzić możliwość stabilnego, zgodnego z regulaminem pobierania e-paragonów z konta,
- preferować oficjalny mechanizm/API, jeżeli będzie dostępny,
- nie przechowywać hasła, jeśli można użyć trwałej sesji/tokena,
- automatyzację przeglądarki traktować jako rozwiązanie opcjonalne i eksperymentalne.

---

## 18. Testy

### Unit tests

Core:
- wyznaczanie ostatniej lekcji,
- ignorowanie odwołanych zajęć,
- konflikt czasowy,
- family rules,
- deduplikacja,
- generowanie komunikatu.

Receipt module:
- import JSON,
- duplikaty,
- normalizacja nazw,
- statystyki zużycia.

### Integration tests

- mock VulcanGateway,
- mock Google Calendar,
- mock WhatsAppGateway.

Nie wykonywać prawdziwych wysyłek WhatsApp podczas automatycznych testów.

### Smoke test

Komenda developerska:

`docker compose up`

musi uruchomić wszystkie usługi i zwrócić zdrowy status bez konieczności modyfikowania kodu.

---

## 19. Kolejność implementacji dla OpenHands

### Milestone 1 — Skeleton

- utworzyć repo,
- solution .NET,
- gateway Node,
- gateway Python,
- Docker Compose,
- konfiguracja,
- health checks,
- testy.

### Milestone 2 — WhatsApp

- QR,
- LocalAuth,
- zapis sesji,
- wybór grupy,
- testowa wiadomość,
- retry.

Nie wysyłać niczego do prawdziwej grupy automatycznie przed ręcznym testem użytkownika.

### Milestone 3 — Google Calendar

- OAuth,
- odczyt wskazanego kalendarza,
- dzisiaj/jutro,
- mapowanie wydarzeń.

### Milestone 4 — VULCAN

- rejestracja klienta,
- pobranie uczniów,
- pobranie planu,
- normalizacja,
- snapshot.

### Milestone 5 — Daily summary

- agregacja,
- formatter,
- scheduler,
- family rules,
- deduplikacja.

### Milestone 6 — Changes & conflicts

- okresowe sprawdzanie,
- diff,
- konflikty,
- powiadomienia o zmianie.

### Milestone 7 — Biedronka

- importer JSON,
- baza produktów,
- wydatki.

### Milestone 8 — Shopping Assistant

- lista zakupów,
- komendy WhatsApp,
- estymacja zużycia.

---

## 20. Definition of Done MVP

MVP jest ukończone, gdy:

1. `docker compose up -d` uruchamia cały system.
2. Po pierwszym skonfigurowaniu WhatsApp sesja przeżywa restart kontenera.
3. System pobiera wydarzenia z Google Calendar.
4. System pobiera plan co najmniej jednego ucznia z VULCAN.
5. System wylicza prawidłową godzinę zakończenia lekcji.
6. O 07:00 tworzy wiadomość z planem szkoły i wydarzeniami.
7. Wiadomość trafia na skonfigurowaną grupę WhatsApp.
8. Ta sama wiadomość nie jest wysyłana drugi raz.
9. Zmiana ostatniej lekcji powoduje oddzielne powiadomienie.
10. Restart Windows/Dockera nie wymaga ponownej konfiguracji poza sytuacjami, gdy zewnętrzny serwis unieważni sesję.
11. Sekrety nie występują w repozytorium.
12. Wszystkie testy przechodzą.

---

## 21. Instrukcja dla OpenHands

Przed rozpoczęciem:
1. przeczytaj cały dokument,
2. utwórz plan implementacji,
3. realizuj milestone po milestone,
4. po każdym milestone uruchom testy,
5. zapisuj decyzje architektoniczne w `docs/decisions/`,
6. nie omijaj błędów poprzez wyłączanie testów,
7. nie zmieniaj technologii bez uzasadnienia,
8. nie commituj sekretów,
9. nie wykonuj `git push`,
10. nie wysyłaj wiadomości na realną rodzinną grupę bez ręcznego przełączenia `WHATSAPP_SEND_ENABLED=true`.

Domyślnie:

```env
WHATSAPP_SEND_ENABLED=false
```

Podczas developmentu wiadomości mają być zapisywane do logu/outboxa.

---

## 22. Pierwsze zadanie dla OpenHands

> Przeczytaj `PROJECT.md`. Zaimplementuj wyłącznie Milestone 1 — Skeleton. Utwórz strukturę repozytorium, FamilyAssistant.Core w .NET 8, FamilyAssistant.VulcanGateway w Python/FastAPI, FamilyAssistant.WhatsAppGateway w Node.js/TypeScript oraz Docker Compose. Dodaj health checks, przykładową konfigurację bez sekretów, testy startowe i README z instrukcją uruchomienia. Nie implementuj jeszcze prawdziwego logowania do VULCAN, Google ani WhatsApp. Nie wykonuj git push. Po zakończeniu pokaż wykonane zmiany, wyniki testów oraz listę następnych kroków.
