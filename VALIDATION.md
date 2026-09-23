# Weryfikacja Milestone 1

Wykonano 22.09.2026 na docelowym Dellu z Windows:

| Sprawdzenie | Wynik |
| --- | --- |
| .NET 8.0.425 — kompilacja Release i testy | 4/4 zaliczone |
| Python 3.12 — pytest | 2/2 zaliczone |
| Node.js 24.19.0 — kompilacja TypeScript i test HTTP | 1/1 zaliczony |
| Trzy rzeczywiste procesy — HTTP `/health` i `/` | 3/3 zaliczone |
| `docker compose config --quiet` | zaliczone |
| `docker compose -f compose.test.yaml config --quiet` | zaliczone |
| Budowanie obrazów Linux — Docker Engine 29.8.0 | 3/3 obrazy aplikacji i 3/3 obrazy testowe zbudowane |
| Testy w kontenerach Linux (.NET 8, Python 3.12, Node 22) | 10/10 zaliczonych: Core 7, Python 2, Node 1 |
| `docker compose up --build -d --wait --wait-timeout 120` | zaliczone; 3/3 kontenery healthy |
| `scripts/smoke.ps1` przez localhost i sieć Docker | 3/3 zaliczone; integracje disabled |
| Użytkownicy procesów w kontenerach | Core UID 1654, VULCAN UID 10001, WhatsApp UID 1000; bez roota |

Pierwszą próbę blokował niedostępny silnik Docker Desktop. Po jego uruchomieniu
wykonano pełne testy kontenerowe oraz start i smoke test obrazów aplikacji.
Kontenery pozostawiono uruchomione, z portami ograniczonymi do 127.0.0.1:
Core 8080; gatewaye nie publikują portów. Można je zatrzymać przez `docker compose down`.
FastAPI/Starlette zgłosiło jedno ostrzeżenie o przestarzałym aliasie AnyIO w
bibliotece testowej; testy zakończyły się powodzeniem.

Repozytorium zainicjalizowano lokalnie na gałęzi `main`. Nie skonfigurowano
zdalnego repozytorium, nie wykonano commita ani git push.

Odczytano dostarczony FamilyAssistant_PROJECT.md i dodano jego kopię do repo.
Uzupełniono worker, status integracji, konfigurację rodziny, wolumeny,
wewnętrzną sieć gatewayów, rotację logów, flagę wyłączenia wysyłki i ignorowanie
prywatnych plików. Testy kontenerowe oraz smoke test po zmianach przeszły.
Restart Windows nie był wykonywany; autostart Docker Desktop należy sprawdzić
przy następnym planowanym restarcie komputera.
