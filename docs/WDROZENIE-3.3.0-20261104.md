# Wdrożenie ECXP-MAILBOX 3.3.0 / ECXP-Setup 1.3 — 2026-11-04 22:53

Operator: „Scal Pomoc i wdróż”. Wykonawca: Claude (`claude-ecxp-mailbox`).
Od 22:49 pracuje sam Claude, a Codex jest wstrzymany (decyzja operatora).

## Zakres

- Lokalne scalenie `claude/przycisk-pomoc` (`2865be1`, przycisk „Pomoc”) do
  `main`: commit `9fda7b7`, bez konfliktów.
- Wersje: klient 3.3.0.0, instalator 1.3 (`1c5489d`).
- Zawartość: poprawka połykania wiadomości (`43be1e9`), hooki `ecxp-mcp.exe`,
  kontrola końca tury i tokeny (`2f36d69`) oraz poprawki po recenzjach Codexa
  (`a82f34b`, `865d49a`).
- Ostatnie dwie rundy poprawek nie przeszły ponownej recenzji Codexa. Codex
  został wstrzymany, zanim ją wykonał. Jego test granic przechodzi 3/3.

## Paczka `builds\wdrozenie-3.3.0-20261104\narzedzia`

| Plik | Wersja | SHA-256 |
|---|---|---|
| ECXP-MAILBOX.exe | 3.3.0.0 | `CFB265A92D0612C0086E4D6A0026D7691D6625C630D5CE7AB7A6DC6592328411` |
| ecxpd.exe | 1.0.0.0 | `AAC1F86D0E0207E6F3219FE10780C553A62EB6159A76721E84B383F1EEF7DA94` |
| ecxp-mcp.exe | 1.1.0.0 | `E25784DBC4E902120CCE5E98795C0C0E12DBCECE44C0C6AA1FA7B448BE6E21EB` |
| ECXP-Setup.exe | 1.3.0.0 | `A4D30C4A117995C6D873A39C4E83059737963F075029E41C2573C79114906547` |

Kontrole przed wdrożeniem:

- `TestTury`, `TestDoreczania`, `TestGraniceRecenzji` (Codex): PASS.
- `ECXP-MAILBOX.exe /selftest <pusty katalog>`: PASS, kod 0.
- Zasoby instalatora: 7/7 zgodnych.

## Kroki na stanowisku (`C:\Users\user\AppData\Local\ECXP`)

1. Pod stosem nie pracował żaden agent w tle (0 procesów potomnych `ecxpd`).
2. Kopia zapasowa w `narzedzia\kopie\przed-3.3.0-20261104-225339`: poprzednie
   programy (klient 3.2.1 `8466BD82…`) oraz `~\.claude\settings.json`.
3. `ECXP-MAILBOX.exe`, `ecxpd.exe` i `ecxp-mcp.exe`: zmiana nazwy działającego
   pliku na `.stary-<ticks>`, potem kopia nowego. `ECXP-Setup.exe` zastąpiony.
   Wszystkie skróty SHA-256 zgodne z paczką.
4. Zatrzymanie konsoli i stosu, bez drzewa procesów. Start nowej konsoli
   (PID 19360), która uruchomiła nowy stos (PID 26040).
5. `ECXP-MAILBOX.exe /instaluj /cicho`:
   - w `~\.claude\settings.json` zmienił się tylko klucz `hooks`: trzy wpisy
     `ecxp-mcp.exe" hook SessionStart|UserPromptSubmit|Stop`, a `hook-claude.ps1`
     usunięty;
   - serwer MCP `ecxp` zarejestrowany ponownie (ta sama ścieżka);
   - w Codexie `[mcp_servers.ecxp]` bez zmian, `notify` (Computer Use) zachowany.

## Weryfikacja po wdrożeniu

- Dziennik stosu: start 22:53:52, połączenia konsoli i MCP, bez błędów.
- Wiadomość `TEST-WDROZENIA-3-3` do `claude-ecxp-mailbox`:
  - została w `do-claude`;
  - hook `SessionStart` o niej przypomniał (nazwa z trasy, 83 ms);
  - sesja odebrała ją sama.
- Hook `Stop` z `stop_hook_active=true` nie blokuje.
- Sygnał tury przetworzony przez stos: `stan\tury\claude-ecxp-mailbox.jsonl`
  zawiera prawdziwą turę sesji.
- Agent w tle `claude-ecxp-mailbox` pozostaje wyłączony (`aktywny=false`).

## Podmiana 23:20 – `c4dcca8` („O programie”)

- Okno „O programie” pokazuje wersję pliku 3.3.0.0, a akapit o TCP jest
  usunięty. Stos ma wersję 1.1.0.0, a `ecxp-mcp` wersję 1.2.0.0.
- Paczka `builds\o-programie-3.3.0-20261104`: `/selftest` PASS, zasoby 7/7.
  Klient `1AE3FF96…`, ecxpd `4CB37A53…`, ecxp-mcp `8320F6E6…`, Setup `FDEDB611…`.
- Kopia poprzedniej kompilacji: `narzedzia\kopie\przed-o-programie-20261104-232029`.
- Ta sama procedura: 0 agentów pod stosem, zmiana nazwy i kopia, restart
  (konsola PID 38548, stos PID 42844). Hooki bez zmian (te same ścieżki).
- Test: `TEST-PODMIANY-C4DCCA8` czekała w skrzynce, hook o niej przypomniał,
  a sesja ją odebrała.

## Podmiana 23:42 – `b47f532` (Codex w tle bez serwera ECXP)

- Zgłoszenie operatora: zadanie w tle dla Codexa („odbierz wiadomość i odpisz
  jaki dzis dzień”) zwróciło poprawną odpowiedź, ale z dopiskiem, że narzędzie
  ECXP wymaga zgody, a polityka blokuje jej udzielenie.
- Przyczyna: Codex w tle (`app-server`, zasada zgód „never”) wczytywał
  `config.toml` z serwerem `ecxp`. Agent Claude w tle miał już pustą
  konfigurację MCP.
- Poprawka: `-c mcp_servers.ecxp.enabled=false` dla Codexa w tle, tylko gdy
  `config.toml` ma `[mcp_servers.ecxp]`. Dla nieistniejącego serwera ten sam
  parametr powoduje błąd startu Codexa (sprawdzone).
- Paczka `builds\codex-bez-ecxp-20261104`: `/selftest` PASS, zasoby 7/7.
  Kopia poprzedniej kompilacji: `narzedzia\kopie\przed-codex-bez-ecxp-20261104-234244`.
- Bezczynny ciepły proces Codexa (start bez parametru) zamknięty. Restart:
  konsola PID 34516, stos PID 10236.
- Test na żywo: pytanie od `claude-ecxp-mailbox` do agenta w tle
  `codex-bez-modulu` („Odbierz wiadomość i odpisz jednym zdaniem…”):
  - ciepły Codex wystartował z `-c mcp_servers.ecxp.enabled=false`;
  - tura trwała 14 s;
  - odpowiedź brzmiała „Dziś jest środa.”, bez komunikatu o zgodzie;
  - stos zwrócił ją nadawcy (praca z pytania agenta, `43be1e9`).
- Ograniczenie: tokenów tury Codexa w tle nie zapisano, bo lustro nie zna nazwy
  wątku `app-server` agenta w tle. To możliwe usprawnienie.

## Podmiana 2026-11-05 00:06 – `66f0420` (obserwacja skrzynki sesji)

- Problem operatora: zadanie wysłane z konsoli czekało, aż operator napisze
  coś w oknie sesji Claude. Aplikacja nie pozwala ECXP zacząć tury w otwartej
  rozmowie.
- Rozwiązanie bez AI: `ecxp-mcp.exe czekaj <nazwa>`, uruchamiane przez sesję
  w tle. Kończy się, gdy przyjdzie wiadomość, a zakończenie polecenia w tle
  budzi sesję w aplikacji Claude. Hooki SessionStart i Stop pilnują, żeby
  obserwacja działała. Lustro czatu wysyła jedną kopię na partnera.
- Paczka `builds\czuwanie-20261105`: `/selftest` PASS, zasoby 7/7, TestTury
  33 PASS. Kopia: `narzedzia\kopie\przed-czuwanie-20261105-000636`.
- Bezczynny ciepły Codex (`codex-bez-modulu`) zamknięty. Restart: konsola
  PID 36060, stos PID 28444.
- Sesja `claude-ecxp-mailbox`: obserwacja uruchomiona (PID 31188, blokada
  `stan\czuwanie\claude-ecxp-mailbox.lock`). Test na żywo wykonuje operator.
- Codex: aplikacja Codex nie ma sprawdzonego mechanizmu budzenia po poleceniu
  w tle. Stos nadal nie budzi Codexa przy regułach `decision="allow"`.

## Podmiana 2026-11-05 00:12 – `aa58ccb` (obieg: wycofane zadania, FIN przez `ecxp_zadanie`)

- Objaw: strażnik przypominał o FIN zadania z 23:47, które operator wycofał
  przyciskiem „Wyślij ponownie”. `ecxp_zadanie(zakonczone)` nie zamykało obiegu.
- Paczka `builds\obieg-wycofane-20261105`: `/selftest` PASS, zasoby 7/7,
  TestDoreczania 24 PASS. Kopia: `narzedzia\kopie\przed-obieg-20261105-001209`.
- Bezczynny ciepły Codex zamknięty. Restart: konsola PID 6948, stos PID 32888.
  Obserwacja skrzynki sesji działała bez przerwy.
- Efekt na żywo:
  - zadanie 1c98 zamknięte przez `ecxp_zadanie`, obieg zakończony, operator
    dostał wspólny FIN `20261105-001221-…-67ac`;
  - zadania 0799 i 43df wycofane, więc obieg zamknięty bez FIN;
  - zadanie fa93 zamknięte jako wycofane. Kopia ae14 Codexa została odebrana
    przed wycofaniem i czeka na FIN Codexa.
- Zgoda na zamknięcie procesów i restart: operator („Tak, wdróż”) po
  zablokowaniu pierwszej próby przez kontrolę uprawnień.

## Podmiana 2026-11-05 00:37 – `e5c52c5` (klient 3.4.0: śledzenie wysłanych)

- Okno „📡 Śledzenie wysłanych…” (Ustawienia, ikona w zasobniku). Licznik „od
  wysłania” odświeża się co sekundę. Okno pokazuje etap, oś czasu z dziennika
  i obiegu, filtr 24 h, 3 dni, 7 dni albo tylko oczekujące. W widoku rozmowy
  jest linia „Śledzenie: … · od wysłania: …”.
- Paczka `builds\sledzenie-3.4.0-20261105`: `/selftest` PASS, zasoby 7/7.
  Kopia: `narzedzia\kopie\przed-3.4.0-20261105-003711`.
- Bezczynny ciepły Codex zamknięty. Restart: konsola 3.4.0.0 PID 47592, stos
  PID 28304. Obserwacja skrzynki sesji działała bez przerwy.
- Okna nie testowano na ekranie. Test wykonuje operator. Testy GUI Codexa
  wymagają teraz pliku `Sledzenie.cs` na liście kompilacji.

## Wycofanie

1. Zamknij konsolę i proces `ecxpd`.
2. Skopiuj programy z `narzedzia\kopie\przed-3.3.0-20261104-225339` do
   `narzedzia`.
3. Przywróć `claude-settings.json` jako `~\.claude\settings.json`.
4. Uruchom konsolę.

## Uwagi

- Działające serwery MCP sesji (`ecxp-mcp.exe.stary-…`) używają starego kodu do
  czasu restartu swojej sesji. Hooki uruchamiają już nowy program.
- Stary hook SessionStart w `D:\Projekty\evidlockv3\.claude\settings.local.json`
  (`skrzynka-start.ps1`) nadal istnieje. Nie należy do ECXP i pozostał bez zmian.
- Pliki `.stary-*` w `narzedzia` można usunąć po potwierdzeniu działania. To
  decyzja operatora.
- Brak push na GitHub.
