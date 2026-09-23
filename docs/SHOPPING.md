# Wspólna lista zakupów

## Aktualizacja: analiza ilości i pobranie historii

Kolejny etap: zaimportowano również 69 JSON-ów Joanny, łącznie 138 unikalnych paragonów. Z obu kont zapisano po 31 PDF-ów do późniejszego OCR. Konto Szymona ma niepobraną historię sprzed 18.09.2025, Joanny sprzed 29.08.2025 (również graniczne dni wymagają sprawdzenia pod kątem limitu 50 wyników). Stałe pobieranie z obu kont realizuje dodatek Chrome/Edge opisany w [BIEDRONKA-CONNECTION.md](BIEDRONKA-CONNECTION.md) (moduł noVNC wyłączony); wymaga logowania użytkownika w obu profilach przeglądarki. Wcześniejsze liczby poniżej opisują poprzedni etap.

Na życzenie użytkownika zakres rozszerzono o pobieranie historii z kont i docelowe automatyczne sprawdzanie nowych zakupów. Na 23.09.2026 wykonano jednorazowe pobranie przez zalogowaną przeglądarkę: 69 JSON z pierwszego konta, wszystkie zaimportowane, oraz 31 starszych transakcji PDF z obrazami (zarchiwizowane, bez importu OCR). Starsza historia sprzed 18.09.2025 nie jest jeszcze pobrana. Drugie konto i stała synchronizacja nadal wymagają podłączenia; sesja przeglądarki nie jest sesją backendu. Nie traktować tego etapu jako włączonego monitorowania.

Panel pokazuje łączną kupioną ilość i medianę odstępów między dniami zakupów. Prognoza liczy medianę ilości na dzień z zakończonych cykli (ilość w dniu zakupu / odstęp do następnego zakupu), a następnie dzieli ostatnią kupioną ilość przez to tempo. Pozwala to uwzględnić większy ostatni zakup. Minimum to trzy dni zakupów. Bardzo dawne zakupy nie otrzymują oznaczenia „Może się kończyć”. Wynik jest orientacyjny, bazuje wyłącznie na zaimportowanej historii i nie uwzględnia zakupów poza nią, faktycznych zapasów ani zużycia. Nazwy wariantów produktów nie są jeszcze łączone semantycznie.

Parser obsługuje również osobne rabaty na paragon (vouchery, rekompensaty). Suma paragonu to fiskalna kwota towarów po rabatach; kaucje za opakowania są poza nią. Kwoty pozycji uwzględniają rabaty przypisane pozycji, ale rabatu globalnego nie rozdzielamy arbitralnie na produkty. Ilości pozostają niezmienione. Powtórny import może uzupełnić pustą etykietę konta bez dublowania paragonu. Testy Core: 62 zaliczone.

Strona `/shopping` łączy historię zakupów gospodarstwa domowego z dowolnej liczby kart Biedronki. Eksportuj paragony jako JSON w aplikacji Biedronka. Na stronie wybierz pliki i opcjonalną nazwę karty; dla drugiej karty wykonaj drugi import z inną nazwą. Nie wymaga to logowania aplikacji do kont Biedronki.

Alternatywnie umieść gotowe pliki w `imports/biedronka/`. Skanowanie odbywa się co 30 sekund. Kopiuj duże pliki z rozszerzeniem `.tmp`, a po zakończeniu zmień je na `.json`. Importer przenosi poprawne pliki do `processed/`, a nieobsługiwane do `rejected/` wraz z opisem błędu. Import folderowy pozostawia kartę nieoznaczoną. Import przez stronę zapisuje dane w bazie, bez archiwizacji oryginału JSON.

Parser obsługuje dokumenty Biedronki `JPK_KASA_PARAGON_v2-0` w polu `data` eksportu. Dekoduje treść JWS, ale nie weryfikuje kryptograficznie podpisu. Kwoty przechowuje w groszach; `brutto` pozycji uwzględnia już rabaty. Sprawdza zgodność sum, dodatnie ilości i walutę PLN. Zwroty, anulowania, inne schematy i paragony z opakowaniami powodującymi niezgodność sum wymagają rozszerzenia parsera; nie są częściowo importowane. Limit pliku: 8 MB.

Identyfikator urządzenia fiskalnego, pamięci i dokumentu tworzy klucz deduplikacji, niezależny od oznaczenia karty. Konflikt zawartości jest odrzucany. Paragon i jego pozycje zapisują się w jednej transakcji w osobnej bazie `shopping.sqlite` na wolumenie `family-data`. Pliki, baza i prawdziwe paragony nie trafiają do Git.

Produkty łączą się wyłącznie po nazwie bez końcowej litery VAT, z ujednoliconymi odstępami i wielkością liter. Nie łączymy automatycznie różnych marek ani gramatur. „Zmień nazwę” ustawia czytelną etykietę bez zmiany klucza kolejnych importów. Łączenie różnych skrótów tego samego produktu pozostaje kolejnym etapem.

Statusy: propozycja, do kupienia, kupione, odrzucone. Import nie nadpisuje decyzji użytkownika. Kupione i odrzucone produkty można ponownie dodać ręcznie. Po trzech różnych dniach zakupu pojawia się orientacyjna data kolejnego zakupu: ostatnia data plus mediana odstępów. Nie jest to pomiar zużycia ani zapasów; duże zakupy promocyjne mogą zaburzać wynik. Wcześniej produkty są tylko kandydatami do ręcznego wyboru. Jednostek nie zgadujemy, jeśli brak ich w źródle. Lista zakupów nie wysyła wiadomości WhatsApp.

## Propozycje i lista na WhatsApp

Przepływ: propozycje → grupa „Proponowane Zakupy”, odpowiedź numerami → lista na grupę „Tablica informacyjna” (ta sama grupa co plan rodziny).

1. Utwórz na WhatsApp grupę „Proponowane Zakupy” z kontem połączonym z Family Assistant.
2. `./scripts/whatsapp.ps1 groups`, a następnie `./scripts/whatsapp.ps1 select-shopping-group -GroupId 'ID_GRUPY'`.
3. W `.env`: `SHOPPING_WHATSAPP_ENABLED=true` (wymaga też `WHATSAPP_SEND_ENABLED=true`), opcjonalnie `SHOPPING_PROPOSAL_DAY=Monday,Thursday` (jeden lub więcej dni po przecinku) i `SHOPPING_PROPOSAL_TIME=16:00` dla automatycznej wysyłki; spóźnione uruchomienie wysyła propozycje do 3 godzin po czasie. Potem `docker compose up --build -d --wait`.
4. Propozycje wysyła przycisk „Wyślij propozycje na WhatsApp” w `/shopping` albo harmonogram. Lista zawiera do 15 produktów o statusie „propozycja”, kupowanych w co najmniej dwa różne dni; najpierw te, które „mogą się kończyć”.
5. Odpowiedz w grupie numerami, np. `1 3 5` lub `2-4`. Core co 20 s odbiera odpowiedź, oznacza produkty jako „do kupienia”, wysyła pełną listę „Do kupienia” na tablicę i potwierdzenie w grupie propozycji. Kolejne odpowiedzi dopisują produkty do tej samej listy.

Bramka zapisuje wyłącznie krótkie odpowiedzi złożone z liczb z grupy propozycji; inne wiadomości nie są odczytywane ani przechowywane. Wysyłać może tylko do dwóch wybranych grup. Numery odnoszą się do ostatnio wysłanych propozycji. Oznaczanie „kupione” pozostaje na stronie `/shopping`.

## Czytelne nazwy produktów

`config/product-names.json` zamienia skróty z paragonów na czytelne nazwy (np. `ĆwiartkaKurczaVac kg` → „Ćwiartka z kurczaka (na wagę)”). Klucz to nazwa z paragonu wielkimi literami. Plik jest wczytywany ponownie po każdej zmianie, bez restartu. Produkty spoza słownika dostają automatyczne rozdzielenie słów i jednostek. Nazwa ustawiona ręcznie („Zmień nazwę”) ma pierwszeństwo. Słownik obejmuje 746 nazw z paragonów pobranych 23.09.2026; nowe produkty można do niego dopisywać.

## Okazje z gazetek Biedronki

Gazetki na biedronka.pl są publikowane wyłącznie jako obrazy stron (`/pl/gazetki` → `press,id,…` → `galleryLeaflet.init(<uuid>)` → `leaflet-api.prod.biedronka.cloud/api/leaflets/<uuid>` z listą obrazów). `LeafletScanner` czyta każdą stronę modelem Claude (vision), zapisuje oferty w `leaflets.json` na wolumenie danych i dopasowuje je do produktów kupowanych co najmniej w dwa różne dni. Każda strona jest czytana raz; kolejne sprawdzenia czytają tylko nowe gazetki.

- Wersja standardowa (P). Pomijane domyślnie: wersja „z ladą” (`-l-oferta`), Home, znicze, Hity i Inspiracje, „Nie do wyrzucenia”, porównanie cen (`LeafletExclude`, regex na tytule).
- Sprawdzanie co 12 h i przed propozycjami (pon./czw. 6:00), limit `LEAFLET_MAX_PAGES` stron na przebieg (domyślnie 120), przycisk „Sprawdź gazetki teraz” w `/shopping`.
- W propozycjach WhatsApp produkt z promocją ma dopisek „🏷 cena, warunki, do dd.MM” i trafia wyżej na listę. `same=false` oznacza zamiennik tego samego rodzaju.
- Wymaga `ANTHROPIC_API_KEY` w `.env` (console.anthropic.com), model `LEAFLET_MODEL` (domyślnie `claude-sonnet-5`). Koszt zależy od liczby stron; sprawdź aktualny cennik.
- Wynik jest orientacyjny: model może źle odczytać cenę lub datę. Przed zakupem sprawdź gazetkę (link do strony przy każdej okazji). Ceny nie zależą od konkretnego sklepu — gazetka jest ogólnopolska (Skarżyńskiego 6 = wersja standardowa).
