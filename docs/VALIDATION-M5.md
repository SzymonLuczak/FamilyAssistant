# Milestone 5 — walidacja podsumowań

22.09.2026 wdrożono panel /summary, agregację szkoły i Google, formatter,
reguły rodzinne z YAML, Quartz.NET oraz EF Core/SQLite dla podglądów.
Konfiguracja lokalna zawiera troje zarejestrowanych uczniów i nie zawiera
niepotwierdzonych zasad odbioru.

Testy Core po wdrożeniu: 45/45 w Dockerze. Ostatnie testy niezmienianych
gatewayów: VULCAN 17/17 i WhatsApp 10/10. Łącznie 72 testy w bieżącym
zestawie, bez wykonywania rzeczywistych wysyłek.

Potwierdzono działanie Quartz w logach, stan preview_only i godziny 07:00/20:00.
Sprawdzono w przeglądarce prawdziwe podsumowanie na 22.09.2026: troje uczniów,
jedno wydarzenie Google i ostrzeżenie o niejednoznacznej zmianie lekcji.
Zapis w SQLite zakończył się powodzeniem. Uruchomiono też pobieranie planu
na jutro; wyniku tej operacji nie potwierdzono przed wyłączeniem Dockera.

23.09.2026 użytkownik poprosił o pokazywanie godzin z planu mimo zmian.
Formatter teraz uwzględnia oryginalne godziny lekcji oznaczonych jako zmienione
i dodaje „zastępstwo / zmiana — godziny z planu”. Potwierdzone odwołania
pomija przy wyznaczaniu końca i opisuje jako „lekcja odwołana”. Brak danych
oraz stare kopie zachowują dotychczasowe ostrzeżenia.

Po tej korekcie lokalne testy .NET Release: 45/45. Test regresji obejmuje
zmienioną ostatnią aktywną lekcję i odwołaną późniejszą lekcję.

Podczas wdrażania korekty z 23.09 Docker Desktop początkowo nie startował:
próba przemianowania pliku `sailor-ingest.sock` do `.stale` kończy się
komunikatem „The file cannot be accessed by the system”. Kontrola
automatyczna zablokowała usunięcie nieaktywnych plików runtime. Nie zmieniono
wolumenów ani sesji i nie wykonano resetu do ustawień fabrycznych.

Docker ponownie stał się dostępny; nie powtarzano zablokowanego usuwania.
Przebudowano i uruchomiono tylko family-core. Smoke potwierdził zdrowe
usługi, rejestrację VULCAN i autoryzację Google. WhatsApp po ponownym
starcie pozostaje disabled zgodnie z wyłączonym automatycznym łączeniem.

**Korekta została wdrożona i sprawdzona w przeglądarce 23.09 o 08:56.**
Świeży plan dnia pokazuje godziny trojga uczniów, dopiski o zmianach przy
dwóch planach oraz trzy wydarzenia Google. Starsze wpisy historii zachowują
treść z chwili ich utworzenia. Wysyłka WhatsApp jest nadal wyłączona.
Nie wykonano git push.
