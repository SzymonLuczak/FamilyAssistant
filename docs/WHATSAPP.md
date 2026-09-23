# WhatsApp — obsługa na Dellu

Wszystkie polecenia wykonuj w katalogu repozytorium. Gateway nie publikuje portu
na hosta; skrypt administracyjny używa Docker Compose. Nie ma automatycznej wysyłki.

## Parowanie

```powershell
docker compose up --build -d --wait
./scripts/whatsapp.ps1 start
./scripts/whatsapp.ps1 status
./scripts/whatsapp.ps1 qr
```

Poczekaj na status `qr`, następnie otwórz http://localhost:8080/whatsapp/pair.
Ta strona automatycznie odświeża QR i usuwa go po zalogowaniu lub rozłączeniu.
Plik `secrets/whatsapp-qr.svg` jest tylko awaryjną, nieruchomą kopią kodu.
W telefonie: WhatsApp → Połączone urządzenia → Połącz urządzenie.
Kod wygasa; ponów polecenie `qr`, jeżeli nie został zeskanowany w porę.
Status `ready` oznacza zakończone parowanie. `authenticated` oznacza jeszcze
ładowanie klienta. Przy `error`, `auth_failure` lub `disconnected` sprawdź
połączenie i użyj ponownie `start`. Skrypt nie usuwa sesji.

Sesja jest na wolumenie whatsapp-session. Na istniejącym wolumenie ze starszego
szkieletu, jeśli wystąpi błąd praw zapisu, jednorazowa migracja uprawnień:

```powershell
docker compose run --rm --no-deps --user root --entrypoint chown whatsapp-gateway -R node:node /app/.wwebjs_auth
```

Nie używaj `docker compose down -v`, jeśli chcesz zachować sesję.

## Wybór grupy i próba bez wysyłki

```powershell
./scripts/whatsapp.ps1 groups
./scripts/whatsapp.ps1 select-group -GroupId 'ID_Z_LISTY'
./scripts/whatsapp.ps1 test-message -MessageId 'manual-dry-run-1'
```

Przy `WHATSAPP_SEND_ENABLED=false` wynik to `dry_run`; nic nie trafia na WhatsApp.
Wybrana grupa jest zapamiętywana. Inne grupy i pojedyncze kontakty są odrzucane.
Nazwy i identyfikatory grup są pokazywane tylko w odpowiedzi na lokalne polecenie.

## Ręczny test prawdziwej wysyłki

Dopiero po sprawdzeniu grupy ustaw lokalnie w `.env`:

```env
WHATSAPP_SEND_ENABLED=true
WHATSAPP_AUTO_CONNECT=true
```

Odtwórz gateway, poczekaj na `ready`, następnie sam wywołaj test:

```powershell
docker compose up -d --wait whatsapp-gateway
./scripts/whatsapp.ps1 status
./scripts/whatsapp.ps1 test-message -MessageId 'manual-real-1' -ConfirmSend
```

To polecenie faktycznie wysyła wiadomość do wybranej grupy. Bez `-ConfirmSend`
skrypt odmawia, jeśli aktywna jest wysyłka. Gateway także wymaga flagi true.
Po teście można przywrócić false i ponownie odtworzyć gateway.

Nie używaj identyfikatora próby dry-run do prawdziwej wysyłki. Powtórzenie ID
z tą samą treścią zwraca poprzedni wynik. Inna treść z tym samym ID daje konflikt.
`unknown` lub `attempting` oznacza niepewny wynik: sprawdź WhatsApp przed
jakąkolwiek ponowną próbą. Nie ma automatycznego ponawiania takich wysyłek.

## Sprawdzenie trwałości sesji

Po sparowaniu i ustawieniu AUTO_CONNECT=true:

```powershell
docker compose restart whatsapp-gateway
./scripts/whatsapp.ps1 status
```

Poczekaj na `ready` bez nowego QR. Jeśli dostawca unieważni sesję, parowanie
może być potrzebne ponownie. Tego testu nie da się ukończyć przed skanowaniem QR.

## API i ograniczenia

- GET /health: żywotność procesu, niezależna od połączenia.
- GET /status: połączenie, flaga wysyłki, wybrana grupa, circuit breaker.
- POST /auth/start: jawne uruchomienie klienta.
- GET /auth/qr: aktualny SVG, bez cache.
- GET /groups: grupy zalogowanego konta.
- PUT /config/group: JSON `{"groupId":"..."}`.
- POST /messages: JSON `{"id":"unikalne-id","text":"treść"}`.
- POST /messages/group/{groupId}: ten sam format, tylko dla wybranej grupy.

Core pokazuje stan WhatsApp w /health/integrations i pozostaje dostępny przy
awarii gatewaya. Nie ma jeszcze harmonogramu, komend przychodzących ani outboxa.
Rejestr ID nie ma automatycznego czyszczenia; dotyczy niewielkiej liczby testów M2.
Ograniczenia zależności i retry opisano w `decisions/0002-whatsapp.md`.

## Grupa propozycji zakupów

`./scripts/whatsapp.ps1 select-shopping-group -GroupId 'ID_Z_LISTY'` wybiera drugą dozwoloną grupę — na propozycje zakupów. Bramka przyjmuje z niej tylko odpowiedzi złożone z liczb (np. `1 3 5`). Szczegóły: [SHOPPING.md](SHOPPING.md).
