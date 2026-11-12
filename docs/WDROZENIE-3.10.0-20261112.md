# Mailbox 3.10.0 — 12 listopada 2026

Polecenia operatora: okno podglądu „co akurat dany agent wykonuje” (propozycja
zaakceptowana: „Tak, buduj”), naprawa „Spisu gałęzi” („Brak pliku
SPIS-GALEZI-*.json”) i test, czy strażnik zadań faktycznie działa.

## 👁 Co robi agent

Okno z bieżącymi działaniami agenta, odświeżane co sekundę:

- **Otwieranie:** zakładka Agenci → prawy przycisk na parze → „👁 Co robi agent
  Codex… / Claude…”; prawy przycisk na rozmowie albo w podglądzie wiadomości →
  „👁 Co teraz robi agent?”.
- **Nagłówek:** „● pracuje od 4 min · praca: TEMAT” albo „○ bezczynny ·
  ostatnie działanie 12 min temu”, źródło danych i uwagi.
- **Lista:** czas i działanie: 📥 zadanie/wpis, 📄 czyta, ✏ edytuje,
  ▶ uruchamia, 🔎 szuka, 🌐 sieć, 📨 ECXP, 💭 myśli, ✍ pisze, ✓ koniec tury,
  ⚠ błąd/odmowa. Przyciski: „📄 Log tur” (agenci w tle), „📂 Pokaż zapis”.
- **Lista rozmów (TYTUŁ):** przy Twojej ostatniej wiadomości do agenta, gdy agent
  właśnie nad nią pracuje: „· 👁 agent teraz: uruchamia pytest… (1 min)”.

Skąd dane (opisy do 140 znaków, bez treści plików i wyników poleceń; wartości
wyglądające na hasła, tokeny i klucze są maskowane):

| Agent | Źródło |
|---|---|
| agent w tle Claude | stos zapisuje zdarzenia `stream-json` ciepłego procesu do `stan\agenci\<nazwa>.na-zywo.jsonl` (ostatnie ~200) |
| agent w tle Codex | stos zapisuje powiadomienia `codex app-server` (item/started, turn/…) do tego samego pliku |
| sesja w aplikacji Claude | konsola czyta końcówkę zapisu rozmowy `~/.claude/projects/*/<sesja>.jsonl` (tylko odczyt) |
| sesja w aplikacji Codex | konsola czyta końcówkę `~/.codex/sessions/**/rollout-*-<wątek>.jsonl` (tylko odczyt) |

W trybie zastępczym agentów w tle (jednorazowe CLI) widać tylko początek i koniec
tury. Zamiast proponowanego hooka `PostToolUse` sesje w aplikacjach są czytane
z zapisu rozmowy — bez dodatkowego hooka na każdym narzędziu (bez opóźnień
w sesjach spoza ECXP).

## Spis gałęzi na bieżąco

Gdy w katalogu skrzynki nie ma gotowego `SPIS-GALEZI-*.json` (dawny spis
EvidLockV3 — nadal pokazywany, jeśli jest), konsola buduje spis sama, w tle:
repozytoria Git z umów gałęzi par, katalogów agentów i folderów projektów, ich
kopie robocze (worktree), gałęzie (na zdalnym / tylko w klonie albo do
wypchnięcia), niezapisane zmiany i stash. Tylko odczyt: `git --no-optional-locks`,
bez `fetch`. Grupy A–F i strona HTML jak dotąd; opisy bez odniesień do elv3.

## Strażnik zadań — test na żywo i poprawki

Test z prawdziwym zadaniem do agenta w tle `claude-ikony` (projekt test-ikony):
zadanie doręczone w 1 s, ale agent kończył turę po 12–20 s — przed kolejnym
sprawdzeniem strażnika (co 20 s), stąd „śledzone: 0”. Ten sam kod strażnika na
kopii skrzynki z chwili przed FIN: „śledzone: 1 · TEST-STRAZNIKA-2 · odebrane
przez Claude · ikony po 0 s · w trakcie”. Poprawki:

- **Zakończone w ostatnich 15 min zostają na liście** strażnika (etap „FIN po …”),
  także te, które skończyły się między dwoma sprawdzeniami — z dymkiem.
  Status: „w toku: N · zakończone (15 min): M”.
- **Fałszywe ostrzeżenie „Adresat … nie odzywa się od 8 dni”** przy wysyłce do
  aktywnego agenta w tle (odzywa się tylko w turach) — usunięte; ostrzeżenie
  zostaje, gdy usługa agentów w tle tej strony jest zatrzymana.
- **Agent w tle obiecywał odpowiedź „później”** (polecenie w tle, koniec tury),
  a obietnica stawała się FIN-em. Każda praca agenta w tle kończy się teraz
  uwagą, że odpowiedź na końcu tury jest wynikiem końcowym.

## Wersje

ECXP-MAILBOX i instalator 3.10.0, `ecxpd` 1.4, `ecxp-mcp` 1.4.
Pakiet: `builds/release-3.10.0/narzedzia/` (poza Git).

## Testy (12.11.2026)

| Test | Wynik |
|---|---|
| `TestAktywnosci` (nowy: opisy, maskowanie, Claude/Codex, zapis i podgląd) | 23 PASS |
| `TestyRozmow` (GUI, w tym okno „Co robi agent” i menu) | 57 PASS |
| `TestStraznika`, `TestDoreczania`, `TestPentestu`, `TestStartuPentestu` | 21 / 24 / 21 / 14 PASS |
| `ECXP-MAILBOX.exe /selftest` | PASS |
| Na żywo, tylko odczyt: podgląd sesji `claude-ecxp-mailbox` i `codex-ecxp-mailbox`, spis gałęzi (1 repozytorium, 4 worktree) | działa |
