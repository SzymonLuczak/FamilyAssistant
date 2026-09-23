# 0003 — Google Calendar

Oficjalne REST API przez HttpClient w Core; brak klienta z uprawnieniami zapisu.
OAuth web flow z PKCE S256, losowym state, powiązaniem z cookie HttpOnly/SameSite
Lax, 10-minutowym limitem i jednokrotnym użyciem. Zakres calendar.readonly.
Zmiany lokalnych ustawień i rozpoczęcie zgody chroni ASP.NET Antiforgery.
Ścieżki /google akceptują wyłącznie host localhost/loopback; Core publikuje
port tylko na loopback. Nie jest to panel przeznaczony do sieci LAN/Internetu.

Wymiana i odświeżanie tokenów są serializowane. Tokeny zapisujemy atomowo do
osobnego wolumenu (0600 na Linux), nie w Git, odpowiedziach API ani logach.
Nowa zgoda wymaga własnego refresh tokenu i kasuje wybór starych kalendarzy;
nie łączymy danych autoryzacyjnych dwóch kont. Logowanie HTTP zapytań Google
i ASP.NET request diagnostics ograniczono do Warning, aby nie zapisywać kodów
autoryzacji z query string ani identyfikatorów kalendarzy w logach Info.

Odczyty mają timeout 10 s, do 3 prób dla błędów przejściowych, pojedyncze
odświeżenie tokenu po 401, circuit breaker 30 s po 3 błędnych wywołaniach.
Paginacja ma ochronę przed powtarzającym się tokenem i limit 100 stron.
Wybrany kalendarz musi pochodzić z calendarList zalogowanego konta.

Granice dni są obliczane w Europe/Warsaw. Daty całodniowe zachowują koniec
wyłączny. Rekurencję rozwija Google (singleEvents=true). Nazwy, lokalizacje,
sekrety i pełne treści zdarzeń nie są logowane. UI renderuje dane przez textContent.
Odpowiedzi i ekran OAuth nie są cache'owane. Snapshoty i odświeżanie okresowe
pozostają poza M3. Testy korzystają z atrap HTTP i fikcyjnych danych.
