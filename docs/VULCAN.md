# eduVULCAN — rejestracja i plan

Panel: http://localhost:8080/vulcan. Hasła nie podajemy Family Assistant.

1. W panelu otwórz link logowania eduVULCAN.
2. Zaloguj się na stronie dostawcy i dokończ jego wymagane zgody.
3. Po powrocie na https://eduvulcan.pl/api/ap zapisz stronę przez Ctrl+S jako
   „Strona internetowa, tylko HTML”. Pusta strona nie przesądza o błędzie:
   dane rejestracji mogą znajdować się w ukrytym polu formularza.
4. Wybierz zapisany HTML w lokalnym panelu i kliknij „Połącz uczniów”.
5. Po udanej rejestracji usuń eksport, wybierz ucznia i pobierz plan.

Przepływ opiera się na dokumentacji Iris, ale nie został jeszcze sprawdzony
na koncie użytkownika. Jeśli dostawca nie przekieruje na powyższy adres,
nie eksportuj zwykłej strony konta ani plików cookies. Zgłoś sam komunikat.
JSON odpowiedzi rejestracji również jest obsługiwany, ale nie trzeba go
ręcznie kopiować ani wklejać w rozmowie.

Eksport zawiera tymczasowe uprawnienia. Panel przesyła go tylko do lokalnego
adaptera, który rejestruje certyfikat u dostawcy. Eksport, tokeny JWT i hasło
nie są zapisywane. Certyfikat z kluczem prywatnym, uczniowie i snapshoty
są przechowywane w wolumenie `vulcan-session`, plik `state.json`, uprawnienia 0600.
Udany import zastępuje poprzednie konto i wszystkie snapshoty. Nieudany import
zachowuje lokalny stan; rejestracja po stronie dostawcy mogła jednak nastąpić,
dlatego nie ponawiamy jej automatycznie.

Panel jest dostępny tylko przez lokalny Core na porcie 8080; gateway nie
publikuje portu na hoście. Formularz jest chroniony CSRF, sprawdzaniem Host
i zakazem buforowania. Nie udostępniaj panelu przez publiczny tunel.

## Wynik odczytu

- Lista uczniów zawiera imię/nazwisko, szkołę i klasę; identyfikatory są
  oddzielne dla ucznia i jednostki.
- Plan obejmuje zwykłe i dodatkowe zajęcia, z ograniczoną paginacją.
- Jawne `ClassAbsence` jest oznaczane jako odwołanie. Pozostałe zastępstwa
  mają stan `change_requires_review`; pokazujemy oryginalne godziny i uwagę,
  a pierwsza/ostatnia lekcja pozostaje nieustalona. Nie zgadujemy znaczenia
  nieudokumentowanych kodów zmiany. Przed automatycznym odbiorem dziecka
  konieczne będzie zweryfikowanie tych mapowań na rzeczywistych danych.
- Udany odczyt atomowo zapisuje kopię dla konkretnego ucznia i dnia.
  Błąd jednego z dwóch odczytów (zwykłe/dodatkowe zajęcia) nie nadpisuje kopii
  niepełnym planem. Kopia awaryjna zawiera `stale: true` i czas `fetchedAt`.
- Brak kopii przy błędzie daje błąd, nigdy pusty plan. Pusty plan jest
  poprawnym wynikiem wyłącznie po udanej odpowiedzi dostawcy.
- Limit: 120 kopii, daty od 30 dni wstecz do 90 dni naprzód. Odczyt ma
  łączny limit 28 sekund i do dwóch prób przy timeout/OSError. Po trzech
  nieudanych operacjach następuje 30 sekund przerwy.

`registered` oznacza zapisany certyfikat, nie gwarancję bieżącej łączności.
Odczyt planu potwierdza dostęp. Nie ma jeszcze zautomatyzowanego odnawiania
rejestracji, odpytywania harmonogramem ani wysyłania planu na WhatsApp.
W tej wersji import uczniów z różnych tenantów jest jawnie odrzucany.
Nie implementujemy starego logowania token/symbol/PIN ani omijania płatnych
uprawnień dostawcy.

## Źródło adaptera

[Iris — rejestracja eduVULCAN](https://github.com/bbrjpl1310b/iris/blob/3d5d85bc3653dfebb6231a7f886c76aa09112844/docs/getting-started.md).
Wersja przypięta do commita `3d5d85bc3653dfebb6231a7f886c76aa09112844`.
Iris: AGPL-3.0-only, pełny tekst w `docs/third-party/iris-LICENSE`.
Nieoficjalny interfejs może ulec zmianie. Walidacja modeli Iris zgłasza
ostrzeżenia Pydantic; adapter nie wyłącza walidacji odpowiedzi planu.
