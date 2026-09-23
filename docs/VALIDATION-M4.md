# Walidacja adaptera eduVULCAN — 22.09.2026

Zaimplementowano lokalny import rejestracji eduVULCAN, odczyt uczniów,
plan zwykły i dodatkowy, zachowawczą normalizację zmian, pierwszą/ostatnią
lekcję dla jednoznacznego planu oraz atomowe kopie awaryjne.

Wyniki testów w Dockerze:

| Usługa | Wynik |
|---|---|
| Core .NET 8 | 28/28 |
| VULCAN Python/FastAPI | 17/17 |
| WhatsApp TypeScript | 10/10 |
| Razem | 55/55 |

Pełny zestaw uruchomiono przez scripts/test.ps1. Po zmianie opisu trybu
na read_only ponownie przebudowano i uruchomiono testy Core i VULCAN.
Ostrzeżenia Python: starszy styl modeli Pydantic w Iris i API testowe
Starlette/AnyIO; brak błędów testów.

Przebudowano i uruchomiono Core oraz VULCAN. Trzy usługi mają zdrowe
health checks. Smoke: Google authorized, WhatsApp ready, VULCAN not_connected.
Wolumen VULCAN jest zapisywalny dla nieuprzywilejowanego użytkownika usługi.
Panel /vulcan otwarto w przeglądarce: poprawny stan braku połączenia,
instrukcja rejestracji i komunikat po próbie importu bez pliku.
git diff --check: bez błędów białych znaków (są ostrzeżenia normalizacji CRLF).

Ograniczenia: konto eduVULCAN nie zostało jeszcze połączone. Testy korzystają
z danych sztucznych i nie potwierdzają działania nieoficjalnego API na
prawdziwym koncie, kompletności zastępstw ani uprawnień tego konta.
Następny krok: użytkownik loguje się na stronie dostawcy zgodnie z panelem,
importuje eksport lokalnie, potem porównujemy plan z dziennikiem.
Milestone 4 nie jest jeszcze odebrany na danych rzeczywistych.

Brak git push. Wysyłanie na WhatsApp pozostaje wyłączone.

## Potwierdzenie po połączeniu konta

Użytkownik potwierdził działanie. Odczyt lokalnego API potwierdził stan
registered i 3 uczniów. Pobrano dzisiejszy plan każdego ucznia z dostawcy:
7, 6 i 4 zajęcia; wszystkie wyniki stale=false. Jeden plan zawiera zmianę
wymagającą sprawdzenia (requiresReview=true); pozostałe dwa są jednoznaczne
według obecnej normalizacji. Nie zapisywano nazw uczniów ani treści planu
w tym raporcie. Nie wysyłano wiadomości WhatsApp.

Rejestracja i rzeczywisty odczyt zostały potwierdzone. Nadal pozostaje
porównanie interpretacji zastępstw z dziennikiem przed automatycznym
wyznaczaniem godziny odbioru dla planów ze zmianami.
