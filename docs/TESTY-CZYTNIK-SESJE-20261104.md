# Czytnik i sesje — wersja 3.1, 2026-11-04

Repozytorium: `D:\Projekty\ecxp-mailbox`, uzgodniona gałąź `main`, baza
`fce09fc6a5c189b69bdfd25c8fab9beda9ebdb0e`. Potwierdzenie Claude w ECXP:
`20261104-023211-ecxp-mailbox-UMOWA-GALEZI-0c5d`.

## Zmiany

- Osobny czytnik z większą powierzchnią treści, metadanymi, kopiowaniem
  i eksportem PDF/HTML całej wiadomości.
- Suwak 75–250%, Segoe UI/Consolas i niezależny motyw: aplikacji,
  jasny lub czarne tło/biały tekst. Zachowane zaznaczenie, kotwica czytania
  i skala po odświeżeniu. Inne wiadomości nie przebudowują czytnika.
- PDF przez Microsoft Print to PDF w osobnym procesie. W tym systemie drukowanie
  wewnątrz procesu GUI powodowało awarię na wyjściu (c0020001/c000041d).
  Eksport został odizolowany od procesu GUI, a regresje uruchamiane są pod pełną
  pętlą WinForms z uporządkowanym zamknięciem. Plik docelowy zastępowany jest dopiero po
  poprawnym eksporcie. HTML nie uruchamia treści jako kodu i nie ładuje zewnętrznych zasobów.
- Codex: oficjalne `codex://new?path=`, odczyt mapowania identyfikatorów
  z metadanych desktopu i walidacja korzenia `project/read`.
  Bez zapisu global-state, baz lub konfiguracji użytkownika Codexa.
- Potwierdzona przyczyna braku otwarcia: `thread/resume` zwracał
  `already has an active writer`. Samo zamknięcie procesu RPC nie wystarczało.
  Teraz po pierwszej turze wykonywane są `thread/unsubscribe` i potwierdzenie unload.
  Zerowy czas unload jest argumentem wyłącznie własnego procesu app-server.
- Praca w tle z wyłączonym pokazywaniem sesji nie otwiera okien desktopu.
- Claude: pełny pakiet, bez cichego obcinania do 12000 znaków. Długi pakiet
  przekazywany przez pełną lokalną migawkę. Decyzja o dostępie do folderu
  i przycisk Wyślij pozostają w aplikacji Claude.

## Kontrole autora

Kompilator: `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`,
`/optimize+ /codepage:65001`, referencje i zasoby jak w `buduj.cmd`.
Pakiet: `D:\Projekty\ecxp-builds\reader-sessions-20261104\narzedzia`.
Kompilacje szeregowe, bez podmiany działającej instalacji.

- `TestyRozmowQA.exe <pusty katalog>`: 21 asercji GUI/eksportu, kod wyjścia 0.
- `TestySesji.exe`: katalogi i mapowanie, brak zapisu stanu Codexa, URI/Unicode,
  trwałość, unload, blokada powtarzania wykonanej pracy po błędzie oddawania sesji,
  pełne pakiety Claude, nowe tłumaczenia EN/FR/CS/DE.
- Syntetyczny PDF: 4 strony A4; PyMuPDF potwierdził wszystkie 180 akapitów
  w kolejności i końcowy marker `OSTATNI ZNAK ŻÓŁĆ`.
- Rzeczywisty test `SesjeAplikacji.ZalozCodex`:
  `01a1a44b-677b-7762-926f-c956b3c25783`. Desktop potwierdził `status=idle`,
  `cwd=D:\Projekty\ecxp-mailbox`,
  `projectId=a9f1b59c-a3d7-4f6b-a3f5-20fefe6e01e0`.
  Bez ręcznego przenoszenia i bez poprawiania tego testu narzędziem nawigacji.
- `/selftest <pusty katalog>`: 21 kontroli protokołu PASS; raport `selftest.txt`, kod procesu 0.
- Instalator: 7/7 zasobów zgodnych SHA-256 z pakietem, klient 3.1.0.0.
- `git diff --check`: bez błędów.

## Ograniczenia

Tryb automatyczny otwiera Codexa po pierwszej odpowiedzi i zwolnieniu zewnętrznego
właściciela zapisu. Operatorowi przedstawiono alternatywę natychmiastowego otwarcia
z przygotowanym poleceniem i ręcznym przyciskiem Wyślij.

Testy Claude sprawdzają katalog i kompletność pakietu; nie wysłano automatycznie
nowej rozmowy w aplikacji Claude. Nie zmieniano ustawień zaufania ani zabezpieczeń.
Wyłącznie dane syntetyczne. Przechwytywanie ekranu wyłączono na prośbę operatora.

Niezależna recenzja Claude: oczekuje. Brak USER_ACCEPTED, push/merge i uruchamiania
instalatora na działającym stanowisku.
