# Motyw wszystkich okien i Niewysłane — klient 3.2

Repozytorium: `D:\Projekty\ecxp-mailbox`, gałąź `main`.
Przyrost względem `db8a6370eaccd02b90aeee331ffcf02e8efb508a`.
Wspólna gałąź i zakres autora uzgodnione z istniejącym Claude przez ECXP.
Rozszerzenie zakresu przekazane wiadomością
`20261104-194543-ecxp-mailbox-ZAKRES-GLOBALNY-MOTYW-NIEWYSLANE-edfe`.
Nie edytowano stanu orkiestracji ani rezerwacji.

## Zachowanie

- Motyw jasny, ciemny lub konsolowy jest wspólny dla otwartych i nowych okien.
  Konsolowy używa czarnego tła i białego tekstu. Wybór pozostaje po zamknięciu
  czytnika i ponownym uruchomieniu klienta, do jawnej zmiany przez użytkownika.
  Ustawienia i rodzina czcionki czytników są zapisywane atomowo w
  `narzedzia/ustawienia-wygladu.json` katalogu danych ECXP.
- Nowy filtr „Niewysłane” zbiera szkice, błędy wysyłki i problemy doręczenia,
  zgodnie z wyborem operatora (opcja 3). Rozróżnia je etykietami.
- Szybka odpowiedź i osobny edytor zapisują treść, adresata, wybór agenta,
  odpowiedź na wiadomość i odwołania do załączników. Plik lokalny:
  `narzedzia/niewyslane-wlasciciela.json` w danych ECXP.
  Zamknięcie edytora zachowuje szkic. Szkice nie są wysyłane automatycznie.
- Kliknięcie niewysłanej pozycji otwiera edytor. Podgląd szkicu nie potwierdza
  wiadomości ECXP. Edytor i szybka odpowiedź nie nadpisują się wzajemnie.
- Przyjęta przez stos wysyłka usuwa szkic. Zamknięcie formularza ani kolejny
  autozapis nie odtwarzają wysłanej pozycji. Potwierdzone doręczenie usuwa wpis
  błędu z tego filtra; historia wiadomości pozostaje w zwykłych widokach.
- Błąd aktualizacji widoku po przyjęciu wysyłki nie jest przedstawiany jako
  nieudane wysłanie i nie uruchamia powtórnej wysyłki.
- Instalator 1.2 zawiera klienta 3.2 i aktualny stos/MCP z tej samej kompilacji.

## Weryfikacja autora — 2026-11-04

Pakiet odizolowany: `D:\Projekty\ecxp-builds\global-theme-unsent-20261104`.
Kompilator systemowy: `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`.
Opcje i zasoby jak w `narzedzia/zrodlo/buduj.cmd`, wyjście do katalogu pakietu.
Kompilacje szeregowe. Istniejące ostrzeżenia CS0649 pozostają widoczne.

- `TestyRozmowQA.exe <pusty katalog>`: **40 PASS**, kod procesu 0.
  Pełna pętla WinForms, własne dane syntetyczne. Obejmuje stabilność czytania,
  suwaki, motyw istniejących/nowych okien, ponowny odczyt ustawień,
  eksporty oraz pełny cykl zapisu, odtworzenia i usuwania szkiców.
- `TestySesji.exe`: **17 PASS**, kod procesu 0; w tym mapowanie projektów,
  przekazanie sesji, pakiet Claude i nowe etykiety EN/FR/CS/DE.
- `ECXP-MAILBOX.exe /selftest <pusty katalog>`: **21 PASS**, kod procesu 0.
- PyMuPDF: **4 strony A4**, wszystkie **180 akapitów** w kolejności,
  końcowy marker `OSTATNI ZNAK ŻÓŁĆ` zachowany.
- Zasoby instalatora: **7/7 zgodnych SHA-256** z plikami pakietu.
- `git diff --check`: PASS.

SHA-256 klienta 3.2.0.0:
`78B5EAD85AA2C09852AEC5CFD13AA78BD2349E8C44F6E082C40191ECC9236BE5`.

SHA-256 instalatora 1.2.0.0:
`78E9F7186C14C469C1995617D0D8F313560746806BD82C9B5ABF85A61B57D19C`.

## Stan i ograniczenia

Niezależna recenzja istniejącej sesji Claude: **oczekuje**. Odbiór ECXP
nie przyniósł jeszcze recenzji `db8a637` ani tego przyrostu. Brak recenzji
nie oznacza zgody na wdrożenie. Działająca instalacja nie została podmieniona.

Rzeczywisty test nowej sesji Codexa i ograniczenia integracji Claude są opisane
w `TESTY-CZYTNIK-SESJE-20261104.md`; tego testu nie powtarzano, ponieważ kod
integracji sesji nie zmienił się w tym przyroście. Nie wykonano automatycznego
wysłania rozmowy przez natywną aplikację Claude.

Przechwytywanie ekranu i Computer Use pozostają wyłączone na polecenie operatora.
Testy GUI były programowe, bez zrzutów ekranu. Brak push/merge i USER_ACCEPTED.
