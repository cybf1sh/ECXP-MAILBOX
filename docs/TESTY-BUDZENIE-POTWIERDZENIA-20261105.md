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

- TestPotwierdzen: 21 PASS — odbiór, częściowy i wspólny FIN, idempotencja,
  zgodność starych wiadomości, zadanie pojedynczego agenta, argumenty natywnej kolejki.
- TestyRozmowQA: 43 PASS — w tym Pomoc, trwały motyw, szkice, eksporty i stabilność.
- TestySesji: 17 PASS.
- TestTury: 33 PASS.
- TestDoreczania: 24 PASS.
- TestSledzenia: PASS (głębokość 1, zachowane zaznaczenie).
- /selftest: 21 PASS.
- /selftest-stos: 43 PASS, prawdziwy transport na odizolowanej skrzynce.
- Kod procesu każdego testu: 0. Ostrzeżenia CS0649 nie były wyciszane.
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
