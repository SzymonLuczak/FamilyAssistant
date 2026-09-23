# Weryfikacja Google Calendar — 22.09.2026

Na docelowym Dellu zakończono OAuth konta Google i zapisano wybór dwóch kalendarzy.
Rzeczywisty odczyt API zakończył się powodzeniem:

- dziś, 22.09.2026: 1 wydarzenie, 0 błędów kalendarzy;
- jutro, 23.09.2026: 3 wydarzenia, 0 błędów kalendarzy.

Nie zapisano tutaj nazw kalendarzy, tytułów wydarzeń, identyfikatorów ani tokenów.
Plik klienta Web OAuth znajduje się w ignorowanym secrets/google-oauth.json.
Autoryzacja i wybrane kalendarze są w wolumenie google-token.

Testy Core: 25/25, w tym OAuth/state/PKCE, odświeżanie tokenów, stronicowanie,
wybór kalendarzy, odmowa dostępu, circuit breaker, zmiana czasu i mapowanie
wydarzeń całodniowych oraz osób. Wcześniej sprawdzone zestawy: Python 2/2,
WhatsApp 10/10. Smoke test wszystkich usług przeszedł po wdrożeniu M3.

WhatsApp pozostaje ready, z zapisaną grupą i wyłączoną wysyłką. Nie wykonano
zmian wydarzeń Google, wysyłek WhatsApp ani git push. Kolejny etap: Milestone 4,
adapter VULCAN. Harmonogram i podsumowania należą do kolejnych milestone'ów.
