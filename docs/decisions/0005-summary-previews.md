# 0005 — trwałe podglądy podsumowań

Milestone 5 łączy dane od już połączonych dostawców. Implementuje formatter,
rodzinne zasady z YAML, harmonogram Quartz.NET oraz zapis EF Core/SQLite.
Przygotowanie treści jest oddzielone od jej dostarczenia. Na obecnym etapie
wszystkie wpisy mają stan draft; moduł nie wywołuje WhatsAppGateway.

Klucz rodzaju i daty ogranicza poranny/wieczorny wpis do jednego na dzień.
SHA-256 treści zapobiega zmianie identycznego podglądu. Utrwalenie jest
niezależne od pamięci Quartz, więc przeżywa restart procesu. Przywrócenie
po awarii ograniczono do 30 minut od planowanej godziny, aby nie tworzyć
spóźnionych porannych planów wieczorem.

Niepełne dane są jawne. Według korekty użytkownika z 23.09.2026 zmiana
lekcji nie ukrywa godzin z planu: dopisujemy informację o zastępstwie/zmianie,
a potwierdzone odwołania pomijamy przy wyliczaniu końca i opisujemy je.
Nie reinterpretujemy nieznanych kodów VULCAN jako potwierdzonego nowego
terminu. Brak danych i stare kopie nadal mają odrębne ostrzeżenia.

Zależności przypięte: YamlDotNet 18.1.0, EF Core SQLite 8.0.28,
Quartz.Extensions.Hosting 3.20.1. Wybrano dostępne wersje zgodne z .NET 8.
Detekcja konfliktów, wykorzystanie minimumTravelMinutes i wysyłkowy outbox
pozostają poza zakresem tej zmiany.
