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
