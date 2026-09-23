# Podsumowanie dnia — Milestone 5

Panel: http://localhost:8080/summary. Przyciski przygotowują podgląd na dziś
albo jutro i zapisują go lokalnie. Ręczne podglądy nie wysyłają wiadomości.
Po włączeniu SUMMARY_SEND_ENABLED scheduler przekazuje poranne i wieczorne
podsumowania do trwałej kolejki. Szczegóły w [DELIVERY.md](DELIVERY.md).

## Dane i rodzinne zasady

Core wczytuje prywatny `config/family.yaml`. Przykład bez danych osobowych:
`config/family.example.yaml`. Każde aktywne dziecko musi mieć identyfikator
z `/vulcan/status`; osoby dorosłe nie wymagają identyfikatora. Wyłączone
osoby nie są pobierane. Zduplikowane nazwy/identyfikatory, nieprawidłowe
godziny i nieistniejące osoby w zasadach odbioru powodują czytelny błąd,
zamiast cichego pomijania reguł.

Stała reguła odbioru obowiązuje w dniu, którego dotyczy podsumowanie,
nie w dniu jego wygenerowania. Odbiór jest pomijany dla potwierdzonego dnia
bez aktywnych lekcji. Przy niepewnym planie pozostaje opisem stałego
ustalenia do potwierdzenia, bez podawania pewnej godziny.

Tytuły kalendarza można przypisać do osób przez `[Imię]` albo `[alias]`
z `calendarAliases`. Bez jawnego dopasowania wydarzenie pozostaje wspólne.
Nie przypisujemy osób przez fragmenty wyrazów. Podsumowanie używa aliasów
z family.yaml; dotychczasowy panel Google nadal może używać osobnego
calendar-members.json. Wybrane kalendarze są pobierane z istniejącej
konfiguracji modułu Google, nie z przykładowego pola googleCalendar w YAML.

Pierwsza i ostatnia lekcja są wyliczane ponownie z aktywnych zajęć.
Odwołania są pomijane przy wyliczaniu końca i opisane w nawiasie.
Na prośbę użytkownika przy niejednoznacznej zmianie pokazujemy oryginalne
godziny z planu z dopiskiem „zastępstwo / zmiana — godziny z planu”.
Nie oznacza to potwierdzenia zmienionej godziny przez dostawcę. Dane stare
albo niedostępne nadal nie dają potwierdzonej godziny zakończenia. Awaria Google nie ukrywa szkoły,
a awaria jednego planu nie ukrywa pozostałych dzieci i kalendarzy.
Ostrzeżenia są umieszczone przed resztą treści. Puste sekcje są pomijane,
wydarzenia całodniowe mają osobną etykietę. Godziny są polskie.

Limit podglądu wynosi 4000 znaków. Jeżeli danych jest więcej, komunikat
zawiera jawne ostrzeżenie o skróceniu oraz odesłanie do pełnych paneli.

## Harmonogram

Quartz.NET co minutę sprawdza aktualną konfigurację. Domyślnie o 07:00
przygotowuje plan na dziś, a o 20:00 plan na jutro. Strefa Europe/Warsaw
uwzględnia zmianę czasu. Włączanie: `SUMMARY_SCHEDULER_ENABLED=true` w Compose.
Uruchomienie poza Compose domyślnie nie włącza harmonogramu.

Po restarcie jest 30-minutowe okno nadrobienia pominiętego podglądu.
Po tym czasie nie tworzymy spóźnionego planu. Równoległe wykonanie tego
samego zadania Quartz jest zabronione. Już zapisany slot dnia nie jest
ponownie pobierany przez scheduler. Podgląd niepełny również zamyka slot;
nowy odczyt jest dostępny ręcznie w panelu. Zmienione dane będą obsługiwane
odrębnie w Milestone 6, bez wielokrotnego publikowania porannej wiadomości.

## Zapis i deduplikacja

Entity Framework Core + SQLite, plik `/app/data/summary.sqlite` w istniejącym
wolumenie family-data. To pierwsza wersja schematu, tworzona przez
EnsureCreated; przed przyszłą zmianą schematu należy dodać migrację.
Katalog ma uprawnienia 0700, baza 0600, usługa nie działa jako root.

Każdy wpis ma klucz rodzaj/data, SHA-256 treści, daty utworzenia/aktualizacji,
stan `draft` i znacznik niepełnych danych. Ponowny identyczny podgląd
nie tworzy nowego wpisu ani nie zmienia daty aktualizacji. Zmiana treści
aktualizuje wpis ręczny; scheduler pomija istniejące wpisy swojego rodzaju.
Godzina bieżącego odczytu nie jest częścią hasha dla świeżych danych.
Historia pokazuje 10 ostatnich wpisów; zapis usuwa wpisy starsze niż 30 dni.
Historia jest wyraźnie oznaczona jako zapis, który może być nieaktualny.

Podgląd NIE oznacza dostarczenia wiadomości. Odrębny outbox zapisuje stan
wysyłki i korzysta z trwałej deduplikacji gatewaya. Historia w panelu rozróżnia
podglądy oraz wysyłkę na WhatsApp.

## Dalszy zakres

- Ustalenie rzeczywistych reguł odbioru i aliasów.
- Porównanie niejednoznacznych zmian lekcji z dziennikiem.
- Milestone 6: różnice planów, konflikty, minimumTravelMinutes i powiadomienia.
- Weryfikacja pierwszego rzeczywistego dostarczenia o porze harmonogramu.
