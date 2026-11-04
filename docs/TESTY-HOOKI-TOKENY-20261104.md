# Hooki ECXP i tokeny zadań

Repozytorium `D:\Projekty\ecxp-mailbox`, gałąź `main`, baza `43be1e9`.
Autor: Claude (`claude-ecxp-mailbox`). Polecenia operatora: „ok wykonaj 1 do 4”
oraz „popraw hooki by były bardziej wydajne i kontrolowały komunikacje”.
Pomocnicy z modelem AI są odrzuceni przez operatora. Wszystko poniżej działa
deterministycznie, bez dodatkowych tokenów.

## 1. Jeden właściciel adresu

Hook nigdy nie odbiera i nie potwierdza wiadomości za sesję. Tylko ją
powiadamia. Agent w tle nie dostaje adresu sesji (commit `43be1e9`).

## 2. Hooki Claude: `ecxp-mcp.exe hook <zdarzenie>` zamiast PowerShella

- **SessionStart** – przypomnienie o nieodebranych wiadomościach ECXP.
- **UserPromptSubmit** – lustro wpisu operatora do `stan\wejscia` (jak dotąd),
  przypomnienie i znacznik początku tury.
- **Stop** – znacznik końca tury. Gdy na sesję czekają wiadomości, hook raz
  zwraca `decision: block`. Sesja odbiera je wtedy przed zakończeniem tury.
  `stop_hook_active` zapobiega pętli. Tura budzenia ze stosu
  (`ECXP_DORECZANIE`) nie jest blokowana.
- Nazwę sesji hook bierze najpierw z trasy `ecxp_przypisz` (identyfikator sesji).
  Dopiero potem sięga do pamięci hooka i zapisu rozmowy.
- Sesje spoza ECXP: hook nic nie zapisuje i nic nie wypisuje.
- Instalator (`Instalacja / naprawa`) rejestruje 3 hooki w
  `~\.claude\settings.json` i usuwa stary wpis `hook-claude.ps1`. Plik
  `hook-claude.ps1` zostaje w paczce dla starszych instalacji.

## 3–4. Koniec tury i tokeny (`Tury.cs`, `stan\tury\<nazwa>.jsonl`)

- **Claude**: stos czyta przyrost zapisu rozmowy od początku tury. Sumuje
  `message.usage`, każdą wiadomość raz (ostatni wpis o danym `id`).
- **Codex**: pętla lustra czyta `rollout-*.jsonl`. Tura to różnica licznika
  `token_count.total_token_usage` między `task_started` a `task_complete`.
  `config.toml` i `notify` pozostają bez zmian, bo `notify` jest zajęte przez
  Computer Use aplikacji Codex.
- **Agenci w tle (Claude)**: `usage` z wyniku `claude -p`.
- **Tokeny zadania** = suma tur adresata od doręczenia wiadomości, na którą
  odpowiada, do tury, w której padła odpowiedź. Konsola pokazuje je jako
  „Tokeny zadania: …” w widoku rozmowy i w czytniku. Brak danych to „—”.
  Tłumaczenia są w EN/FR/CS/DE.

## Weryfikacja — 2026-11-04

- `TestTury.exe <pusty katalog>`: **23 PASS**. Test obejmuje hooki, blokadę
  bez pętli, brak odbioru za sesję, tokeny Claude i Codexa, okno zadania oraz
  wynik agenta w tle.
- `TestDoreczania.exe <pusty katalog>`: **18 PASS** (regresja `43be1e9`).
- Wydajność hooka (8 wywołań, katalog tymczasowy, start procesu z zapisem):

  | Hook | Średnio | Minimum |
  |---|---|---|
  | `hook-claude.ps1` | 323 ms | 294 ms |
  | `ecxp-mcp.exe hook` | 74 ms | 68 ms |

  Logika w procesie zajmuje ok. 0,1 ms.
- Paczka `builds\hooki-tokeny-20261104\narzedzia` zbudowana według `buduj.cmd`,
  kod 0. Zasoby instalatora: **7/7 zgodnych**.

| Plik | Wersja | SHA-256 |
|---|---|---|
| ECXP-Setup.exe | 1.2.0.0 | `67429EA2BDABA99C5A914541FCA6C51048CFCC415E07C1A24056AE29AF5D557C` |
| ecxpd.exe | 1.0.0.0 | `7F3056071B37E8F6CD1378ED17104471C80A9F78748801F27507AD7959CD4967` |
| ECXP-MAILBOX.exe | 3.2.0.0 | `6B25398E48F75065847BF01648FC3D7B08501460F846CBC9F36C49D35E6B84DE` |
| ecxp-mcp.exe | 1.1.0.0 | `61433D264846D1C8B1EC0EC6BFABB0EEE09B43278BC899308A1CCE4C31DA13A4` |

## Poprawki po recenzji Codexa (`builds\recenzja-2f36d69`)

Uwaga do raportu: w `2f36d69` `TestTury` miał 22 asercje, a nie 23.

- **[P1] Własność adresu**:
  - `Agenci.Odbierz` sprawdza sesję pod tą samą blokadą co odbiór (ACK).
  - `Agenci.Uruchom` wykonuje kontrolę, trasę `tlo:` i rekord agenta w jednej
    sekcji krytycznej z `ecxp_przypisz`.
  - Przed startem tury w tle właściciel jest sprawdzany ponownie.
  - Praca odebrana przez agenta przed przejęciem adresu przez sesję trafia do
    operatora jako jedno zgłoszenie `PRACA-AGENTA-W-TLE-PRZEJETA` i nie znika.
- **[P2] Codex**: `task_started` bez znanego licznika oznacza brak danych dla
  tej tury, a nie liczenie od zera.
- **[P2] Claude**: sygnał Stop czyta zapis rozmowy tylko do długości pliku z
  chwili końca tury. Opóźnione sygnały nie łączą tur.

Weryfikacja po poprawkach:

- `TestTury`: **24 PASS**.
- `TestDoreczania`: **21 PASS**. Nowe asercje: przejęcie adresu z odebraną
  pracą oraz granica P1.
- `TestGraniceRecenzji` Codexa: **3/3 PASS**. Przed poprawkami był 0/3.
- Paczka `builds\poprawki-recenzji-20261104\narzedzia`: zasoby **7/7 zgodne**.

| Plik | SHA-256 |
|---|---|
| ECXP-Setup.exe 1.2.0.0 | `5CC745D1F4C7A1609CCC05AFC381AF5D358C97BEAAC54502849F860838C95A9B` |
| ecxpd.exe | `41EF0C4B2094B3157B78F34CDDC8255250373AFA9A6EEBF665811FB5640B034B` |
| ecxp-mcp.exe | `C81EC4A5ED38D4530E095C56FDDFA167B8C4C51FC1C435D69415AA37AC182277` |
| ECXP-MAILBOX.exe 3.2.0.0 | `05C39F465DCF3B9A9FC1AFAF729E602FA634F21415101AE67E817DB9FFF3ECDB` |

Ograniczenie: tura w tle, która już trwa w chwili przejęcia adresu, dokończy
się. Odebrać nowych wiadomości jednak już nie może.

## Ograniczenia

- Tokeny podagentów Claude (osobne pliki zapisu) nie są liczone.
- Codex: pierwsza tura po starcie stosu nie jest liczona, bo licznik z
  początku tury jest nieznany. Codex w tle liczony jest tylko wtedy, gdy lustro
  rozpozna nazwę wątku.
- Hook liczy turę po jej zakończeniu. Do tego czasu przy odpowiedzi widać „—”.
- Bez testów GUI klienta. Bez instalacji: działający klient to 3.2.1.0 spoza repo,
  a stos czeka na recenzję Codexa.
- Stary hook SessionStart w `D:\Projekty\evidlockv3\.claude\settings.local.json`
  (`skrzynka-start.ps1`, usunięta skrzynka) nie pochodzi z ECXP. Nie zmieniono go
  (repozytorium EvidLockV3). Decyzja należy do operatora.
