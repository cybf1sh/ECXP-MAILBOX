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
