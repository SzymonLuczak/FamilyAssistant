# 0001 — Granice Milestone 1

Status: przyjęte. Podstawa: FamilyAssistant_PROJECT.md dostarczony przez użytkownika.

Core używa hosta ASP.NET Core z BackgroundService: worker zapewnia cykl życia
usługi w tle, a HTTP udostępnia health checks. Web SDK jest celowy i nie zmienia
wybranej technologii .NET 8. Worker czeka na zatrzymanie, bez wykonywania jobów.

EF Core/SQLite i Quartz.NET lub Hangfire zostaną dodane wraz z funkcjami
utrwalania i harmonogramu, nie jako nieużywane zależności szkieletu.
Gatewaye nie instalują jeszcze klientów VULCAN ani WhatsApp/Puppeteer.

Health checks oznaczają żywotność usług. /health/integrations w Core jawnie
pokazuje disabled dla niezrealizowanych integracji, zamiast sugerować połączenie.
Timeouty, retry, circuit breaker i outbox dojdą wraz z klientami integracji.

Gatewaye nie publikują portów hosta. Sieć family-assistant-network jest
wewnętrzna (internal: true) na czas szkieletu. Włączenie prawdziwych integracji
wymaga później świadomego zapewnienia dostępu wychodzącego do dostawców.
Core jest dodatkowo w sieci host-access, aby Docker Desktop udostępniał jego
port na localhost. Gatewaye pozostają wyłącznie w sieci wewnętrznej.

Cztery nazwane wolumeny rezerwują miejsca na dane i sesje; nie zawierają jeszcze
bazy ani sesji. Ich prawa zapisu należy zweryfikować przy implementacji persystencji.
Konfiguracja prywatna config/family.yaml jest ignorowana przez Git i Docker build.
W repo znajduje się wyłącznie przykład z fikcyjnymi osobami; M1 go nie wczytuje.

Logi Dockera używają rotacji local: maksymalnie 3 pliki po 10 MB na usługę.
To limit rozmiaru, nie gwarancja retencji 30 dni. Wysyłka jest wyłączona domyślnie;
ustawienie flagi true w M1 nadal niczego nie wysyła, bo brak implementacji.
