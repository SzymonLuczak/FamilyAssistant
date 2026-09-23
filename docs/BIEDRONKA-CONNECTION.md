# Dwa konta Biedronki — dodatek przeglądarki

Panel z instrukcją: `http://localhost:8080/shopping/biedronka`.

Wcześniejszy moduł noVNC (`biedronka-gateway`) jest wyłączony i dostępny tylko w profilu Compose `legacy-biedronka`. Obecnie paragony pobiera dodatek Chrome/Edge `src/FamilyAssistant.BiedronkaExtension`, działający w zwykłej, zalogowanej przez użytkownika przeglądarce.

## Instalacja

1. Utwórz dwa profile Chrome lub Edge — po jednym na konto (Szymon, Joanna).
2. W każdym profilu otwórz `chrome://extensions` / `edge://extensions`, włącz tryb dewelopera, wybierz „Załaduj rozpakowane” i wskaż `C:\development\FamilyAssistant\src\FamilyAssistant.BiedronkaExtension`.
3. Zaloguj się na `https://moja.biedronka.pl/panel/paragons`. Hasło i weryfikację podajesz wyłącznie na stronie Biedronki.
4. W dodatku wybierz Konto 1 lub Konto 2, wpisz imię z powitania („Cześć …!”) i zapisz. Dodatek przerywa pobieranie, gdy zalogowane jest inne konto.
5. Kliknij „Pobierz wybrany zakres”, sprawdź wynik, potem włącz „Sprawdzaj co 6 godzin”.

## Przepływ plików

- Dodatek zapisuje pliki do `Downloads/FamilyAssistant/account-1|account-2/<id>.json|pdf` domyślnego katalogu pobierania profilu.
- `BIEDRONKA_DOWNLOADS_DIR` w `.env` wskazuje ten katalog (np. `C:/Users/szymon/Downloads/FamilyAssistant`); Compose montuje go tylko do odczytu jako `/app/browser-receipts`.
- Core co 30 s odczytuje JSON-y starsze niż 5 s, importuje je z etykietą `BIEDRONKA_ACCOUNT_1_LABEL` / `BIEDRONKA_ACCOUNT_2_LABEL` i nigdy nie przenosi ani nie usuwa oryginałów. Kopię i znacznik `.ok` / `.error` zapisuje w `imports/biedronka/browser-imports/account-N/`, więc ten sam plik nie jest przetwarzany ponownie. Plik zablokowany przez przeglądarkę jest pomijany do następnego skanu.
- Deduplikacja paragonów działa po kluczu fiskalnym, więc wcześniejsze importy ręczne nie zostaną zdublowane.
- `GET /shopping/biedronka/status` zwraca liczniki zaakceptowanych i odrzuconych plików. Nie potwierdzają one zalogowania ani kompletności historii.

## Ograniczenia

- Sprawdzanie automatyczne działa tylko, gdy dany profil przeglądarki jest uruchomiony, i obejmuje domyślny zakres strony (14 dni). Starsze okresy wybierz na stronie i pobierz ręcznie.
- Strona pokazuje maksymalnie 50 transakcji w zakresie; przy tym limicie dodatek ostrzega, żeby zawęzić daty.
- Wygasła sesja lub weryfikacja kończy się statusem błędu (znaczek „!” na ikonie dodatku); logujesz się ponownie samodzielnie.
- Automatyzacja zależy od układu strony Biedronki.
- PDF-y (starsze transakcje) wymagają jeszcze OCR.
- Pierwszy przebieg po instalacji dodatku wymaga ręcznej weryfikacji.
