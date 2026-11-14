# ECXP-MAILBOX 3.15.3 — analiza pracy w Obserwatorium

Ustawienia → Zaawansowane → Obserwatorium agentów → **Wiadomości, decyzje i metody…** analizuje wybrany projekt lub wszystkie projekty. **Analizuj agenta…** otwiera historię zadań i CLI; przycisk u góry tego okna otwiera pełną analizę agenta wraz z powiązanym kontekstem.

## Zakres

- Pełna treść wiadomości, tekstowe i strukturalne części kontekstu, ograniczenia, kontrole oraz opisy plików; odnośniki wewnątrz raportu otwierają wiadomości źródłowe.
- Sygnały decyzji, odmów, przeszkód, uzasadnień i proponowanych alternatyw z cytatami i autorem. Pytania oraz linie cytowane znakiem `>` nie są deklaracjami autora.
- Porównanie wzmianek o metodach według projektu i agenta: liczba wątków, ostatni wynik FIN autora w danym wątku, brak wyniku oraz wykres udziału pozytywnych FIN. Współwystępowanie metody i wyniku nie jest dowodem związku przyczynowego.
- Grafy kolejności wiadomości i warunków tak/nie; odrębne, przerywane pola propozycji kolejnego kroku, oznaczonych jako niesprawdzone hipotezy. Uzasadnienie odmowy pozostaje cytatem, a nie domysłem.
- Lokalne wyszukiwanie treści źródeł. Przejście do źródła ujawnia wiadomość również po ukryciu jej filtrem.
- Przycisk **Eksportuj pełną analizę HTML…** zapisuje samodzielny raport z treścią, grafami, porównaniami, wyszukiwaniem i odnośnikami. Dotychczasowy eksport na głównym ekranie Obserwatorium pozostaje raportem liczników.

## Zasady i ograniczenia

Analiza jest lokalna i regułowa, z rozpoznawaniem wybranych polskich i angielskich sformułowań. Nie wywołuje modelu AI, nie odtwarza aktywacji sieci neuronowej ani ukrytego rozumowania. Wzmianka nie dowodzi zastosowania metody; reguły mogą przeoczyć parafrazę lub rozpoznać relację cudzej wypowiedzi. Oprócz kategorii wbudowanych obsługiwane są jawne zapisy `Metoda: …`, `Technika: …`, `Method: …`. Pełne źródła są dostępne do weryfikacji.

Kontekst jest łączony wyłącznie w obrębie projektu przez identyfikatory odpowiedzi, identyfikatory logiczne i niepuste identyfikatory wątku. Kopie logiczne są deduplikowane, a odpowiedzi na fizyczne kopie kierowane do reprezentanta wiadomości. Operacja odbywa się na kopiach obiektów i nie zmienia skrzynki. Brak danych pozostaje oznaczony jako nieustalony; załączniki nie są automatycznie otwierane. Eksport zawiera treść wiadomości z wybranego zakresu; raport jest zapisywany tylko lokalnie w miejscu wskazanym przez operatora. Treść jest kodowana HTML i nie jest wykonywana jako kod.

## Weryfikacja wydania

- TestObserwatorium: 16 PASS (odmowy, uzasadnienia, alternatywy, części kontekstu, własna metoda, separacja projektów, deduplikacja, odpowiedzi na kopie, brak mutacji danych, kodowanie HTML, kompletność odnośników, pusty zakres).
- TestProtokolu: 39 PASS; TestKonfiguracji: 22 PASS; TestyRozmow: 110 PASS; selftest konsoli: PASS.
- Odczyt rzeczywistej skrzynki: 520 wiadomości, 7 projektów; sprawdzono izolację każdego projektu i wszystkie wewnętrzne odnośniki. Generowanie raportu około 0,24 s. Raporty z rzeczywistymi danymi pozostają poza Git.
- Obejrzano zainstalowane okna: menu, filtr projektu ecxp-mailbox, zmianę wykresów, cytaty i grafy pełnej analizy. Interaktywny zapis przez systemowe okno pliku i kliknięcie źródła nie zostały dokończone podczas równoległego korzystania z okna przez operatora; plik HTML wygenerowano i zweryfikowano programowo.
- Instalator przebudowany, zasoby 7/7 zgodne. Końcowa aktualizacja istniejącej instalacji zakończyła się kodem 0. Nie wykonano testu na odrębnym czystym stanowisku Windows.

Pakiet: `D:\Projekty\ecxp-mailbox\builds\release-3.15.3\narzedzia\ECXP-Setup.exe`. Samodzielny instalator zawiera konsolę, stos, adapter MCP i zasoby; nie zawiera skrzynki użytkownika ani raportów analitycznych.
