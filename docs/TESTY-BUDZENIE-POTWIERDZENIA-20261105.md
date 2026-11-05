# Budzenie Codexa i widoczne potwierdzenia — 2026-11-05

Repozytorium: D:\Projekty\ecxp-mailbox, main, baza b6b5fcc4b6ef8530d921d9d5c7b4ae1cd4b392ff.
Zakres uzgodniony z Claude: wiadomości ECXP 5100 i 497d.
Autor: Codex; niezależna recenzja: Claude. Instalator po recenzji.

## Przyczyny

- Doręczanie uruchamiało osobny app-server i odmawiało budzenia po wykryciu
  reguł allow. Otwarty w desktopie wątek mógł dodatkowo zwrócić active writer.
  Odbierająca tura zastępcza miała tylko odczyt, więc nie wykonywała zwykłych zadań.
- Odbiór i pierwszy FIN pary były tylko stanem w stosie. Operator nie widział
  zakończenia jednej strony do czasu wspólnego FIN.
- Okno śledzenia uważało każdy FIN z pary za odpowiedź obu adresatów.

## Zmiana

Codex CLI 0.166.0: oficjalne codex queue --thread UUID --message TEXT --resume.
Powiadomienie trafia do istniejącej sesji i używa jej modelu oraz jej uprawnień.
Nie ma nowego agenta ani zmian konfiguracji lub reguł zgód.
Uruchamiany jest natywny codex.exe, bez cmd.exe/PowerShell, z cytowaniem Windows CRT.
Sam kod wyjścia 0 nie wystarcza: wymagane jest potwierdzenie z prawidłowym UUID wątku.
Dodanie do kolejki nie jest przedstawiane jako odbiór ani wykonanie zadania.
Zdarzenie pliku w do-codex wyzwala powiadomienie; kontrola okresowa co 30 s pozostaje
na wypadek zgubienia zdarzenia. Ponowienia nadal ograniczone jak dotąd.

Dokumentacja oficjalna:
https://learn.chatgpt.com/docs/cli/reference (codex queue).
Ostrzeżenie dokumentacji: nieudane --resume może dać kod 0; dlatego dowodem
wykonania będzie dopiero ACK i FIN ECXP, a nie zakończenie CLI.

Odbiór wysyła od razu potwierdzenie dla operatora. Pierwszy FIN pary jest od razu
widocznym wynikiem częściowym. Wspólny FIN nadal zamyka obieg po obu stronach.
Metadane etap_obiegu rozróżniają te wiadomości. Częściowy FIN nie kończy wiersza
drugiego adresata. Stare wiadomości bez metadanych zachowują poprzednią obsługę.
Wersja klienta 3.5.0.0; wersja instalatora 1.4.0.0.

## Kontrole autora

Pakiet roboczy: builds/budzenie-potwierdzenia-20261105, kompilator systemowy
Framework64/v4.0.30319/csc.exe, opcje i zasoby z buduj.cmd.

- TestPotwierdzen: 25 PASS — odbiór, częściowy i wspólny FIN, idempotencja,
  zgodność starych wiadomości, zadanie pojedynczego agenta, argumenty natywnej kolejki.
- TestyRozmowQA: 43 PASS — w tym Pomoc, trwały motyw, szkice, eksporty i stabilność.
- TestySesji: 17 PASS.
- TestTury: 33 PASS.
- TestDoreczania: 24 PASS.
- TestSledzenia: PASS (głębokość 1, zachowane zaznaczenie).
- /selftest: 21 PASS.
- /selftest-stos: 43 PASS, prawdziwy transport na odizolowanej skrzynce.
- Łącznie 207 kontroli PASS. Kod procesu każdego testu 0. Ostrzeżenia CS0649 nie były wyciszane.
- Native queue: znacznik QUEUE-20261105-A przyjęty przez CLI jako
  01a1ace8-2615-77f2-9fe0-e6107886a952 i rzeczywiście dotarł jako wiadomość
  do TEGO samego czatu bez wpisu operatora.

## Stan w chwili zgłoszenia do recenzji

Test całego zadania wysłanego z ECXP-MAILBOX na żywo jest jeszcze do wykonania.
Nie traktujemy samego znacznika transportu jako zakończonego testu zadania.
Nie przebudowano jeszcze instalatora ani nie podmieniono działającej instalacji.
Brak push/merge/USER_ACCEPTED. Stan orkiestracji nie był ręcznie edytowany.


## Poprawki po recenzji Claude e776

- Potwierdzenie odbioru nie wykonuje ACK-przy-okazji. W dzienniku zadanie pozostaje
  przyjęte (przyjeta), a wykonanie odnotowuje dopiero obieg_fin. Test obejmuje
  zarówno ecxp_odbierz, jak i jawny w_trakcie.
- Ostrzeżenie could not resume jest jawnie pokazywane. Potwierdzenie dodania
  do kolejki oznacza poprawny transport, nie udane wykonanie lub odbiór zadania.
  Po dodaniu powiadomienia stos nie dodaje następnych kopii; po 5 min bez ACK
  alarmuje. Aktywna sesja ma 2 min na samodzielny odbiór. Przy starcie wrócił
  okres 20 s na rejestrację sesji.
- Atrapa CLI jest w repo (AtrapaKolejkiCodex.cs). Test bez argumentów zwraca
  instrukcję użycia i kod 2 zamiast wyjątku.
- Marker QUEUE-B z --resume wykazał rzeczywiste zachowanie na stanowisku:
  dodano wiadomość, lecz CLI ostrzegło, że lokalny daemon nie działa. To jest
  dowód dodania do kolejki, nadal nie jest dowodem obudzenia bezczynnej sesji.
- Uruchomienie automatycznego budzenia przy obecnych regułach zgód pozostaje
  decyzją operatora wskazaną w recenzji. Do tej decyzji i pozytywnej recenzji
  poprawki nie są wdrażane.

Odtworzenie testu potwierdzeń (z narzedzia/zrodlo; CSC = systemowy csc.exe):
1. CSC /target:exe /out:<builds>/AtrapaKolejkiCodex.exe AtrapaKolejkiCodex.cs
2. CSC z opcjami RDZEN z buduj.cmd oraz TestPotwierdzen.cs,
   /target:exe /main:Ecxp.TestPotwierdzen /out:<builds>/TestPotwierdzen.exe
3. TestPotwierdzen.exe <pusty katalog> <builds>/AtrapaKolejkiCodex.exe

## Wdrożenie zatwierdzone przez operatora

Operator w tym czacie wybrał „Tak, włącz i przetestuj” dla poprawki
43d2658736048877e2d6ff0eb89bd57643269cc8 i uruchomienia lokalnej usługi Codexa.
Niezależna recenzja Claude 20261105-194135-ecxp-mailbox-RECENZJA-43D2658-PO-E776-1dc0
zatwierdziła kod po usunięciu P2 z potwierdzeniem odbioru.

Wdrożono 2026-11-05 o 19:53 czasu Europe/Warsaw. Zaktualizowano pliki
w narzedzia repozytorium oraz w C:\Users\user\AppData\Local\ECXP\narzedzia.
Wykonano kontrolowaną podmianę binariów z kopią poprzedniej wersji.
Restart objął wyłącznie procesy klienta 6464 i stosu 28304.
Nowy klient ma PID 11912, nowy stos PID 13508. Łączność MCP działa po restarcie.
Ustawienia klienta, konfiguracja Codexa i pliki reguł zachowały sumy SHA-256.

- Klient 3.5.0.0 ma SHA-256
  CCF1708FC7E07121B42D401589CF1322E3141AE30385D2879C84676E32DD4636.
- Instalator 1.4.0.0 ma SHA-256
  5566BE7B34A22687D6634E05BD6ADFACA1412302327BC8F981A9975CE10C3087.
- Wszystkie 7 zasobów instalatora porównano z plikami wejściowymi. Wynik PASS.
- Kopia 12 wcześniejszych plików jest w
  builds/budzenie-potwierdzenia-20261105/backup-przed-3.5-20261105-195256.
- Działający adapter MCP korzysta jeszcze z poprzedniego obrazu. Windows wymagał
  przeniesienia zajętego pliku na nazwę ecxp-mcp.exe.stary-20261105-195316.
  Nowe uruchomienia korzystają ze zaktualizowanego ecxp-mcp.exe.
- Oficjalne codex app-server daemon start uruchomiło lokalną usługę 0.166.0.
  Polecenie daemon version potwierdziło running. Logi są w katalogu testu.
- QUEUE-20261105-B rzeczywiście dotarł do tego czatu automatycznie o 19:47.
  Wiadomość dodano wcześniej podczas aktywnej pracy. Osobny test wiadomości
  wysłanej dopiero podczas bezczynności pozostaje do wykonania.

Manifest wdrożenia deployment.json i porównanie zasobów installer-resources.json
znajdują się w builds/budzenie-potwierdzenia-20261105.
Przygotowano z Claude test IDLE-43D2658-20261105. Po zakończeniu tury Claude
wyśle syntetyczne pytanie ECXP. Codex ma odebrać je automatycznie, utworzyć
E2E-CODEX-OK.txt w katalogu testu i odesłać powiązaną odpowiedź przez ECXP.
Wynik tego testu będzie dopisany po faktycznym odbiorze.


## Test rzeczywistego zadania po bezczynności

Poprzednia tura tego czatu zakończyła się 2026-11-05 o 19:55:01.
Operator wysłał przez klienta ECXP polecenie „skompiluj, podmień instalator i commit”
o 19:57:34.821, identyfikator
20261105-195734-ecxp-mailbox-KOMPILACJA-ZWOLNIONA-E346856-7a42.
Wiadomość nadeszła po 2 min 33 s bezczynności.

Stos dodał powiadomienie do natywnej kolejki o 19:57:35.099,
identyfikator 01a1ad12-d534-70b2-8194-96b3e0528885.
Tura 01a1ad12-e740-7821-bf32-69e1d8e7cbe2 rozpoczęła się o 19:57:39,
około 5 s po wysłaniu. Jej pierwszym wejściem było automatyczne powiadomienie ECXP.
Odbiór ecxp_odbierz nastąpił najpóźniej o 19:57:45 i zwrócił integralnosc=ok.
Brak dodatkowego wpisu operatora w czacie między zakończeniem poprzedniej
tury a automatycznym powiadomieniem. Czasy sprawdzono przez read_thread
i dziennik działającego ecxpd.

W tej automatycznie uruchomionej turze ponownie skompilowano instalator
na polecenie operatora, porównano 7 osadzonych zasobów i podmieniono obie
kopie w narzedzia repozytorium i instalacji AppData. Klient pozostał 3.5.0.0.
Końcowy instalator 1.4.0.0 ma SHA-256
F1AB736451A757D42FB83CB05B78A8E3FA397EAEC71E3F14D8281849B6D8945A.
Ta kompilacja zastępuje wcześniejszą kompilację instalatora z godziny 19:53.
Kompilator Framework generuje nowy obraz PE przy ponownym budowaniu.

Dowód wykonania E2E-CODEX-OK.txt zawiera marker IDLE-43D2658-20261105,
identyfikator rzeczywistej wiadomości operatora, granicę czasu odbioru i hash
instalatora. Plik jest w builds/budzenie-potwierdzenia-20261105.
Zachowano również native-delivery-owner-7a42.json i build-setup-owner-7a42.log.

Ograniczenie diagnostyczne pozostaje widoczne. Stan transportu zawiera
kolejka_codex=true i ostrzezenie_wznowienia=true. Lokalny daemon zgłosił
active writer dla wątku otwartego w desktopie. Mimo ostrzeżenia desktop
rzeczywiście uruchomił turę z natywnej kolejki. Kryteria obudzenia, odbioru
i wykonania są spełnione. Kryterium całkowitego braku ostrzeżenia jest
niespełnione i pozostaje jawne w raporcie.

Wiadomość operatora miała typ odpowiedz i powstała jako odpowiedź na wcześniejszy
meldunek. Taki wpis nie zakłada obiegu zadania wymagającego FIN. Wynik zostanie
odesłany jako powiązana odpowiedz, zgodnie z normalną ścieżką protokołu.
Niezależna weryfikacja tego przebiegu przez Claude jest w toku.
