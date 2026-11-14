# ECXP-MAILBOX 3.15.4 — czytelne, interaktywne raporty

Obserwatorium → Wiadomości, decyzje i metody zawiera teraz raport z JavaScriptem osadzonym w HTML. Funkcje działają lokalnie bez CDN, połączenia z usługą AI ani bibliotek doinstalowywanych u użytkownika.

## Obsługa

- Zakładki: Modele decyzyjne (domyślna), Decyzje i odmowy, Porównanie metod, Wiadomości źródłowe.
- Wyszukiwanie na bieżąco w modelach i wiadomościach. Filtry: pozytywne, negatywne, niewykonane, brak wyniku FIN. Przy modelu filtr dotyczy zapisanych FIN całego wątku, przy wiadomości jej własnego FIN. Model z różnymi FIN może pasować do kilku kategorii; nie wybieramy arbitralnie jednego wyniku.
- Sortowanie chronologiczne w obu kierunkach i grupowanie według wyniku. Liczniki widocznych pozycji oraz czytelny stan pustego wyniku.
- Zwijane modele i wiadomości ograniczają ilość tekstu na ekranie. Kolory są uzupełnione opisami tekstowymi.
- Model jest rysowany jako SVG: węzły, romby warunków, strzałki i gałęzie TAK/NIE. Przyciski zmieniają powiększenie. Pełne dowody można rozwinąć obok modelu.
- Węzły ze źródłem są klikalne. Przejście do wiadomości przełącza zakładkę, rozwija treść i usuwa filtry, które mogłyby ukryć wskazane źródło.
- Eksport zapisuje kompletny HTML wraz z JavaScriptem i stylem. Stan roboczy filtrów nie jest utrwalany: po otwarciu raport zaczyna od wszystkich wyników i najnowszych wpisów.

Graf przedstawia porządek zarejestrowanych wiadomości i ocenę dostępności dowodów. Ciągłe strzałki nie dowodzą przyczynowości ani ukrytego rozumowania modelu. Przerywane gałęzie pozostają propozycjami do sprawdzenia, a nie twierdzeniem o wykonanym działaniu. Zasady analizy tekstu z 3.15.3 pozostają bez zmian.

## Weryfikacja 2026-11-14

- TestObserwatorium: 16 PASS.
- TestRaportu.cjs: 26 PASS na **pliku zapisanym przez systemowe okno eksportu z zainstalowanej aplikacji**, 511 wiadomości po deduplikacji, 205 wątków. Sprawdzono wszystkie filtry, obie kolejności czasu, grupowanie wyniku, wyszukiwanie pustego wyniku i reset, przełączanie zakładek, generowanie SVG, romby, strzałki, alternatywy, zoom, przejście do ukrytego źródła, rozwijanie/zamykanie wiadomości oraz kompletność odnośników.
- Obejrzano nowy raport w zainstalowanej aplikacji, rozwinięto model i kliknięciem węzła otwarto poprawną wiadomość. Eksport przez przycisk i dialog Zapisz jako zakończył się sukcesem.
- Lokalny plik testowy: `D:\Projekty\ecxp-mailbox\builds\release-3.15.4\obserwatorium-eksport-test.html` (2 209 276 bajtów). Zawiera prywatne wiadomości i pozostaje poza Git.
- Przeglądarka narzędzia automatyzacji odrzuciła URL file://; nie obchodzono tej blokady. JavaScript zapisanego pliku sprawdzono w jsdom, bez przeglądarki i dostępu sieciowego. Nie deklarujemy testu wizualnego wyeksportowanego pliku w zewnętrznej przeglądarce.
- Instalator: zgodność 7/7 zasobów; aktualizacja lokalnej instalacji zakończona kodem 0.

Test JS można odtworzyć poleceniem `node narzedzia/zrodlo/TestRaportu.cjs <katalog zawierający node_modules/jsdom> <wyeksportowany HTML>`. jsdom jest wyłącznie zależnością testową; nie jest składnikiem instalatora ani raportu.
