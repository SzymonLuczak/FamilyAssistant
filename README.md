# Family Assistant — Milestone 1–5

Szkielet trzech usług: .NET 8, Python/FastAPI i Node.js/TypeScript.
Usługi startują bez kont i sekretów. WhatsApp ma opcjonalne, jawne parowanie QR
i ręczny test wysyłki. Domyślnie wysyłka i automatyczne łączenie są wyłączone.
Google Calendar jest gotowy do konfiguracji OAuth tylko do odczytu.
Adapter eduVULCAN ma lokalny import rejestracji, listę uczniów, plan i kopie ostatnich odczytów.
Potwierdzono rzeczywisty odczyt trzech uczniów; interpretacja zmian wymaga dalszej weryfikacji.
Podsumowanie łączy szkołę z Google i stałymi zasadami odbioru. Quartz przygotowuje
lokalne podglądy rano i wieczorem, a SQLite zapewnia zapis i deduplikację.
Dodano opcjonalną trwałą kolejkę wysyłającą zaplanowane podsumowania:
[docs/DELIVERY.md](docs/DELIVERY.md). Przykładowa konfiguracja ma wysyłkę wyłączoną.
Instrukcja parowania i testu: [docs/WHATSAPP.md](docs/WHATSAPP.md).
Kalendarze: [docs/GOOGLE-CALENDAR.md](docs/GOOGLE-CALENDAR.md), panel http://localhost:8080/google.
Szkoła: [docs/VULCAN.md](docs/VULCAN.md), panel http://localhost:8080/vulcan.
Pulpit ze wszystkimi modułami: http://localhost:8080/
Podsumowanie: [docs/DAILY-SUMMARY.md](docs/DAILY-SUMMARY.md), panel http://localhost:8080/summary.
Zakupy: [docs/SHOPPING.md](docs/SHOPPING.md), panel http://localhost:8080/shopping.
Połączenie dwóch kont Biedronki: [docs/BIEDRONKA-CONNECTION.md](docs/BIEDRONKA-CONNECTION.md), panel http://localhost:8080/shopping/biedronka. Pierwsze logowanie i sprawdzenie pobierania wymagane przed uznaniem synchronizacji za uruchomioną.
Import JSON Biedronki łączy historię obu kart, usuwa duplikaty i udostępnia wspólną listę produktów do zatwierdzenia.

## Podstawa i zakres

Projekt porównano z dostarczonym `FamilyAssistant_PROJECT.md`, którego kopia
znajduje się w repo. Pracujemy na docelowym Dellu. Decyzje architektoniczne
dla kolejnych integracji znajdują się w `docs/decisions/`.

## Struktura

```text
FamilyAssistant.sln
src/
  FamilyAssistant.Core/             # gospodarz przyszłej logiki i harmonogramu
  FamilyAssistant.VulcanGateway/    # adapter eduVULCAN
  FamilyAssistant.WhatsAppGateway/  # adapter WhatsApp
tests/FamilyAssistant.Core.Tests/
scripts/                           # testy kontenerowe i smoke test
compose.yaml
compose.test.yaml
.env.example
```

Usługi są niezależne. Core odczytuje status WhatsApp z limitem 3 sekund.
Wykrywanie konfliktów jest poza bieżącym zakresem. Adapter eduVULCAN korzysta z przypiętej wersji Iris.

## Uruchomienie przez Docker

Wymagania: działający Docker Engine w trybie kontenerów Linux i Docker Compose v2.
W katalogu repozytorium:

```powershell
Copy-Item .env.example .env
Copy-Item config/family.example.yaml config/family.yaml
New-Item -ItemType Directory -Force imports/biedronka
docker compose up --build -d --wait
docker compose ps
./scripts/smoke.ps1
```

| Usługa | Status | Health check |
| --- | --- | --- |
| Core | http://localhost:8080/ | http://localhost:8080/health |
| VULCAN (tylko sieć Docker) | http://vulcan-gateway:8000/ | http://vulcan-gateway:8000/health |
| WhatsApp (tylko sieć Docker) | http://whatsapp-gateway:3000/ | http://whatsapp-gateway:3000/health |

`GET /` zwraca nazwę usługi, milestone i stan integracji.
`GET /health` sprawdza tylko działanie lokalnej usługi, nie dostępność dostawców.
Core odpowiada tekstem `Healthy`, gatewaye JSON-em `{"status":"healthy"}`.
WhatsApp udostępnia wewnętrzne API opisane w docs/WHATSAPP.md.

Port Core i strefę czasową można zmienić w `.env`. Po zmianie portu przekaż
je też do smoke testu, np. `./scripts/smoke.ps1 -CorePort 9080`.
Tylko Core publikuje port na `127.0.0.1`; gatewaye są w wewnętrznej sieci Docker.
Kontenery działają bez roota.
`restart: unless-stopped` pozwala odtworzyć działające usługi po restarcie
silnika Docker; sam Docker musi uruchamiać się z systemem.
Stan `unhealthy` sam w sobie nie restartuje kontenera.

```powershell
docker compose logs --tail 100
docker compose down
```

Zarezerwowano wolumeny family-data, whatsapp-session, vulcan-session i google-token.
WhatsApp zapisuje na swoim wolumenie sesję oraz wybór grupy i rejestr ID wiadomości.
Google zapisuje tokeny OAuth, a VULCAN certyfikat i kopie planu w osobnych
wolumenach. `family-data` przechowuje bazę podglądów SQLite. `.env`, sekrety, bazy
i lokalne wyniki kompilacji są pomijane przez Git i kontekst budowania obrazu.

## Testy

Wszystkie testy w izolowanych kontenerach:

```powershell
./scripts/test.ps1
```

Bez PowerShell, uruchom kolejno (każde polecenie musi zakończyć się kodem 0):

```sh
docker compose -f compose.test.yaml build
docker compose -f compose.test.yaml run --rm core-tests
docker compose -f compose.test.yaml run --rm vulcan-tests
docker compose -f compose.test.yaml run --rm whatsapp-tests
```

Testy sprawdzają start bez sekretów, health checks, ochronę lokalnych formularzy,
OAuth, normalizację danych, kopie awaryjne i kontrolę wysyłki. Smoke test sprawdza trzy
rzeczywiście uruchomione procesy przez HTTP.

## Rozwój lokalny

Wymagania: .NET SDK 8, Python 3.12+ i Node.js 22+ z npm.

Core (z katalogu repozytorium):

```sh
dotnet test FamilyAssistant.sln --configuration Release
dotnet run --project src/FamilyAssistant.Core --urls http://127.0.0.1:8080
```

VULCAN (z `src/FamilyAssistant.VulcanGateway`):

```powershell
python -m venv .venv
./.venv/Scripts/python -m pip install -r requirements-dev.txt
./.venv/Scripts/python -m pytest -q
./.venv/Scripts/python -m uvicorn app.main:app --host 127.0.0.1 --port 8001
```

Na Linux użyj `.venv/bin/python`.

WhatsApp (z `src/FamilyAssistant.WhatsAppGateway`):

```powershell
npm ci
npm test
$env:PORT = '3001'
npm start
```

Na Linux użyj `PORT=3001 npm start`. `npm test` obejmuje kompilację TypeScript.

## Następne kroki

1. Uzupełnić własne zasady odbioru oraz aliasy w config/family.yaml.
2. Zweryfikować zastępstwa i odwołania przed wyznaczaniem godzin odbioru.
3. Milestone 6: różnice planu i wykrywanie konfliktów.
4. Potwierdzić pierwszą zaplanowaną wysyłkę w historii panelu i na grupie.

Domyślnie `WHATSAPP_SEND_ENABLED=false`. True zezwala na ręczne wywołanie wysyłki
do wybranej grupy; nie uruchamia automatycznego nadawcy.
Prywatny `config/family.yaml` jest ignorowany przez Git i wczytywany przez moduł podsumowań.
Przykład zawiera fikcyjne osoby. `/health/integrations` pokazuje stan WhatsApp;
VULCAN pokazuje stan rejestracji; Google stan konfiguracji lub autoryzacji.
Logi Dockera są rotowane: 3 pliki po 10 MB na usługę.

Źródła techniczne: [kontenery FastAPI](https://fastapi.tiangolo.com/deployment/docker/),
[health checks ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks?view=aspnetcore-8.0).
