# 0004 — eduVULCAN zamiast starszego klienta UONET+

Użytkownik potwierdził konto eduVULCAN. Przykładowy `vulcan-api` ze specyfikacji
nie odpowiada temu wariantowi rejestracji. Izolowany gateway Python/FastAPI
korzysta z Iris, przypiętego do konkretnego commita, bez zmiany Core na Python.

Nie zbieramy hasła. Importujemy lokalny plik HTML/JSON z przepływu
`https://eduvulcan.pl/api/ap`, walidujemy format i czas ważności JWT, a ich
podpisy sprawdza dostawca przy rejestracji. Dekodowane JWT służą wyłącznie
do wyboru ograniczonej ścieżki tenant; nigdy do autoryzacji lokalnych danych.
Adresy odczytu ograniczono do HTTPS na lekcjaplus.vulcan.net.pl.

Snapshoty są atomowymi plikami w prywatnym wolumenie na tym etapie.
Nie dodajemy jeszcze EF/SQLite ani harmonogramu. Model nie udaje pewności
przy nieznanych zmianach lekcji. Test na koncie użytkownika pozostaje
warunkiem uznania integracji produkcyjnej za zweryfikowaną.
