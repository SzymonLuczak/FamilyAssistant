# Weryfikacja zakupów — 2026-09-23

Aktualizacja (dodatek przeglądarki): moduł noVNC przeniesiono do profilu `legacy-biedronka`. Dodano `BrowserReceiptInbox` (import z `Downloads/FamilyAssistant/account-N` bez modyfikacji oryginałów, etykieta konta, znaczniki `.ok`/`.error`, pomijanie plików zablokowanych) i dwa testy Core. Skrypty dodatku przechodzą `node --check`; `receiptTarget` sprawdzony ręcznie. Kontenery zbudowane i zdrowe. Dodatek 0.1.2 (poprawione porównanie imienia z powitania, widoczny status uruchomienia): pierwszy przebieg na koncie Szymona pobrał 5 plików JSON, Core zaimportował wszystkie 5 (`.ok`, bez błędów). Konto Joanny (osobny profil): 5 plików JSON, wszystkie zaimportowane bez błędów. `core-tests` w Dockerze: 67/67 zaliczonych. Do zrobienia: potwierdzenie cyklu 6-godzinnego.

Aktualizacja po pobraniu kont: 138 paragonów (69 Szymon, 69 Joanna) zaimportowanych bez duplikatów, 62 PDF-y odłożone do OCR. Core: 65 testów zaliczonych (dodatkowo vouchery, ilość ostatniego zakupu, CSRF i Host panelu połączenia). Cztery kontenery uruchomione; moduł Biedronki sprawdzony po odtworzeniu kontenera. Podgląd noVNC pokazuje prawdziwą stronę logowania Biedronki; przeglądarka wymaga logowania i weryfikacji użytkownika. Nie wykonano jej automatycznie. Pierwszy uwierzytelniony cykl pobierania nowego modułu nie jest jeszcze przetestowany, harmonogram pozostaje wyłączony. Testy jednostkowe Core nie stanowią weryfikacji zewnętrznego logowania ani kompletności historii.

- `dotnet test tests/FamilyAssistant.Core.Tests`: 60 testów zakończonych powodzeniem, w tym 9 nowych przypadków zakupów.
- Sprawdzone: kwoty po rabatach, ilości z przecinkiem, niezgodność sum, anulowanie, niepoprawny format, deduplikacja między kartami, trwałość nazw i statusów, konflikt zawartości, łączenie historii i prognoza medianą.
- Obraz produkcyjny Core zbudowany i uruchomiony. Wszystkie trzy kontenery zdrowe.
- Import dwóch dostarczonych plików: 2 paragony, 27 pozycji / produktów, 41543 grosze. Oba oryginały zarchiwizowane lokalnie w ignorowanym katalogu `imports/biedronka/processed`. Pliki w Downloads zachowane.
- Powtórzenie importu przez HTTP z poprawnym tokenem formularza zwróciło `added: false`; brak duplikatu.
- POST bez tokenu formularza odrzucony kodem 400.
- Widok `/shopping` sprawdzony w przeglądarce: zgodne liczby, lista kandydatów, przyciski zatwierdzenia/odrzucenia/zmiany nazwy i pusta lista „Do kupienia” do samodzielnego wyboru.
- Nie przypisano dostarczonym plikom nazw kart bez informacji o ich pochodzeniu. Nie wysłano wiadomości zakupowych na WhatsApp.
- Nie zmieniano gatewayów. Nowy zakres zweryfikowano testami Core; nie powtarzano ich wcześniejszych testów Python/Node.
