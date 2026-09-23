# Zaplanowana wysyłka WhatsApp

Użytkownik zlecił uruchomienie wysyłki podsumowań rano i wieczorem do wybranej
grupy rodzinnej. Domyślny przykład konfiguracji nadal ma wysyłkę wyłączoną.
Na docelowym komputerze prywatny plik `.env` włącza:

```dotenv
SUMMARY_SCHEDULER_ENABLED=true
SUMMARY_SEND_ENABLED=true
WHATSAPP_SEND_ENABLED=true
WHATSAPP_AUTO_CONNECT=true
WHATSAPP_GROUP_ID=<identyfikator wcześniej wybranej grupy>
```

O 07:00 powstaje plan na dziś, o 20:00 na jutro, w Europe/Warsaw.
Godziny pochodzą z family.yaml. Kolejka wysyła wyłącznie wpisy morning/tomorrow.
Przyciski podglądu nie wysyłają wiadomości. Nie dodano komendy wysyłającej
testową wiadomość ani nie uruchomiono zaległych podsumowań poza oknem czasu.

## Trwałość i ponowienia

Oddzielna baza `/app/data/outbox.sqlite` (EF Core) w wolumenie family-data
nie zmienia istniejącego schematu podglądów. Unikalny klucz slotu rodzaju/data
zamraża treść i odbiorcę. Jeżeli proces zakończy się pomiędzy zapisaniem
podglądu i dodaniem go do kolejki, kolejny przebieg Quartz w bieżącym oknie
uzupełni brakujący wpis. Stare podglądy spoza okna nie są wysyłane.

Dispatcher sprawdza kolejkę co 30 sekund. Zapisuje próbę i 90-sekundową
dzierżawę przed kontaktem z gatewayem. Maksymalnie 5 prób, z rosnącą przerwą,
zawsze ten sam identyfikator i ta sama treść. Gateway zapisuje identyfikator
przed wywołaniem WhatsApp i nie wykonuje ponownie niepewnej wysyłki.
Utrata odpowiedzi HTTP może prowadzić do ponownego odczytu wyniku przez
ten sam POST z identycznym ID, bez ponownej wysyłki przez gateway.

Przed wysłaniem sprawdzane są połączenie, flaga wysyłki i dokładny ID grupy.
Wywołanie zawiera docelowy ID również w URL, a gateway ponownie sprawdza
zgodność z wybraną grupą. Zmiana wyboru grupy nie przekieruje już zapisanych
wiadomości. Połączenie używa zapisanej sesji przy starcie kontenera;
unieważniona sesja nadal wymaga ponownego sparowania QR.

Wiadomość wygasa 30 minut od zaplanowanej godziny. Nierozpoczęta jest
oznaczana expired. Po rozpoczętej próbie bez rozstrzygnięcia stan jest unknown,
bo samo przekroczenie terminu nie dowodzi braku wysłania. Nie ma przycisku
ponownej wysyłki unknown ani automatycznego generowania nowego ID.

## Historia w panelu /summary

- pending: oczekuje lub czeka na kolejną próbę;
- sending: trwa próba albo oczekuje odzyskania dzierżawy po restarcie;
- sent: biblioteka WhatsApp potwierdziła wywołanie, nie jest to potwierdzenie
  przeczytania wiadomości przez odbiorców;
- unknown: sprawdź grupę; dalsze automatyczne próby są zatrzymane;
- blocked: sprawdź grupę/ustawienia; nie wykonujemy przekierowania;
- expired: okno wysyłki minęło przed rozpoczęciem.

Outbox przechowuje treść lokalnie i trwałe identyfikatory deduplikacji.
Nie usuwaj bazy kolejki lub rejestru gatewaya, aby „ponowić” wiadomość:
utraciłoby to ochronę przed duplikatami. Bazy, .env i sesje są ignorowane
przez Git. Treści i identyfikator grupy nie są logowane przez dispatcher.

Wyłączenie nowych wysyłek: SUMMARY_SEND_ENABLED=false i odtworzenie Core.
Można dodatkowo wyłączyć WHATSAPP_SEND_ENABLED w gatewayu. Podglądy działają
dalej. Wysyłka wymaga działającego Dockera i połączenia z internetem.
