# Wysyłka podsumowań — 23.09.2026

Zaimplementowano trwały outbox SQLite, stały identyfikator wiadomości,
zamrożenie odbiorcy i treści, dzierżawę próby przed wysyłką, ograniczone
ponowienia i obsługę niepewnego wyniku. Podglądy ręczne nie trafiają do kolejki.
Zapis planu i outbox mają odrębne bazy; ponowny przebieg bieżącego slotu
uzupełnia ewentualną przerwę między tymi zapisami. Nie zmieniono schematu
istniejącej bazy podglądów ani reguł rejestru deduplikacji gatewaya.

Pełne testy przez scripts/test.ps1 w Dockerze:

| Usługa | Testy |
|---|---|
| Core | 51/51 |
| VULCAN | 17/17 |
| WhatsApp | 10/10 |
| Razem | 78/78 |

Nowe testy: niezmienność odbiorcy i treści, odtworzenie dzierżawy po
restarcie, utrata odpowiedzi i ponowienie z tym samym ID, brak powtórek
sent/unknown/blocked, pomijanie spóźnionego planu, odrzucenie ręcznego
podglądu i brak POST przy niezgodnym odbiorcy. Testy nie wysyłają prawdziwych
wiadomości.

Wdrożenie: przebudowano Core i odtworzono Core oraz WhatsAppGateway.
W prywatnym .env włączono harmonogram, wysyłkę po obu stronach i automatyczne
łączenie WhatsApp. ID grupy przypisano po potwierdzeniu, że wybrana grupa
jest grupą wskazaną przez użytkownika. Nie zapisano jej ID w dokumentacji.
Sesja po odtworzeniu kontenera wróciła do ready bez nowego QR.

Smoke: zdrowe trzy usługi; Core scheduled_delivery, VULCAN registered,
Google authorized, WhatsApp ready/sendEnabled=true. Panel wyświetla godziny
07:00 na dziś oraz 20:00 na jutro i osobną historię dostarczenia. Historia
była pusta, więc nie wysłano zaległych wiadomości poza oknem harmonogramu.

Pierwsza rzeczywista zaplanowana wysyłka pozostaje do potwierdzenia o 20:00
23.09.2026. Stan sent oznacza potwierdzenie biblioteki, nie przeczytanie
wiadomości przez odbiorców.

Na osobne zlecenie użytkownika wysłano jedną ręczną wiadomość testową.
Gateway zwrócił sent, a użytkownik potwierdził otrzymanie wiadomości na grupie.
Potwierdza to rzeczywistą wysyłkę gatewaya; automatyczny przebieg całego
harmonogramu nadal wymaga pierwszego zaplanowanego wykonania.
