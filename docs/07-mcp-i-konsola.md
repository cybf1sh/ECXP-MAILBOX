# 07 – Adapter MCP i konsola operatora

## Adapter `ecxp-mcp.exe` [w budowie]

Serwer MCP na **stdio**: JSON-RPC 2.0, jedna wiadomość na linię, UTF-8. Na stdout trafiają wyłącznie odpowiedzi JSON-RPC, a diagnostyka idzie na stderr. Adapter jest klientem stosu `ecxpd` (patrz [03](03-polaczenia.md)), więc nie pisze bezpośrednio w skrzynce.

Obsługiwane metody: `initialize` (wersja protokołu MCP odbijana od klienta, domyślnie `2025-06-18`), `notifications/initialized`, `ping`, `tools/list`, `tools/call`, `resources/list`, `prompts/list`, paczki (batch).

### Narzędzia

| Narzędzie | Wymagane | Opis |
|---|---|---|
| `ecxp_przypisz` | ja, projekt, modul | sesja zgłasza, że prowadzi moduł (trasa, rola, id sesji). Przejmuje kolejkę poprzedniczki |
| `ecxp_odbierz` | ja | wiadomości dla węzła: najpierw pilne, potem od najstarszej. Wpis `dostarczona`. Pole `integralnosc`. Części wiadomości (`czesci`): plik dostaje `sciezka` – kopię tylko do odczytu w `kontekst\zalaczniki\` pod własną nazwą; obrazy (do 5 MB, po sprawdzeniu SHA-256) adapter dołącza do wyniku jako treść obrazu |
| `ecxp_wyslij` | ja, do, typ, tresc | adresy z [05](05-adresowanie-i-para-1-1.md). Lustro i kopie dla pary. `odpowiedz_na` potwierdza oryginał |
| `ecxp_potwierdz` | ja, id, wynik | ACK: `obsluzona` \| `przyjeta` \| `odrzucona`, idempotentne |
| `ecxp_stan` | ja | ile czeka na węzeł, los wysłanych, węzły, karty, spójność dziennika |
| `ecxp_trasy` | – | pary, trasy i role projektu |
| `ecxp_watek` | id | cała rozmowa sprawy w kolejności czasu |
| `ecxp_kontekst_zapisz` | ja + (tresc \| sciezka) | obiekt kontekstu → `sha256` |
| `ecxp_kontekst_pobierz` | ja, sha256 | porcja obiektu (`od`, `ile`) |
| `ecxp_przekaz` | ja, do, … | pakiet przekazania pracy `ecxp/przekazanie` |
| `ecxp_zadanie` | ja, id, stan | zmiana stanu zadania + artefakty |
| `ecxp_karta` | ja | publikacja karty węzła |
| `ecxp_sygnal` | ja | sygnał życia bez odbioru |

Pole `ja` ma postać `codex-<nazwa>` albo `claude-<nazwa>`. Nazwy `wlasciciel` i `ecxp-straznik` są **zastrzeżone**, a adapter je odrzuca.

### Przykład rozmowy MCP

```json
→ {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}
← {"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2025-06-18","capabilities":{"tools":{"listChanged":false}},
     "serverInfo":{"name":"ecxp","title":"ECXP – Evidsec Context eXchange Protocol","version":"…"},"instructions":"…"}}
→ {"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"ecxp_wyslij",
     "arguments":{"ja":"codex-elan","do":"claude/elan","projekt":"evidlockv3","modul":"elan","typ":"pytanie","tresc":"…"}}}
← {"jsonrpc":"2.0","id":2,"result":{"content":[{"type":"text","text":"{\"id\":\"…\",\"seq\":4,\"termin_potwierdzenia_min\":240,…}"}],"isError":false}}
```

Błąd protokołu ECXP (np. para niepełna) zwraca `isError: true` z opisem w `content`. Nie jest to błąd JSON-RPC.

### Rejestracja (planowana)
- **Codex**: `~\.codex\config.toml` → `[mcp_servers.ecxp] command = '<skrzynka>\narzedzia\ecxp-mcp.exe'`
- **Claude Code**: `claude mcp add --scope user ecxp -- "<skrzynka>\narzedzia\ecxp-mcp.exe"`

## Konsola `ECXP-MAILBOX.exe`

Klient stosu z rolą `operator`. Operatora rozpoznaje stos po obrazie procesu (patrz [08](08-bezpieczenstwo.md)). Konsola nie jest protokołem, tylko jedną z aplikacji.

```mermaid
flowchart LR
    subgraph Konsola
        PR[wybór projektu] --> MO[moduły i pary]
        MO --> AG["agenci: role, stan,<br/>nowy agent"]
        AG --> SP["sprawa: łańcuch ról,<br/>rozmowa na żywo"]
        SP --> PI["pisz: para / strona / rola,<br/>nowa sprawa lub kontynuacja"]
        SP --> ST["steruj: wstrzymaj, wznów,<br/>przekaż, przypnij, decyzja"]
    end
    Konsola <-->|ramki ECXP, wypychanie DANE| S[(ecxpd)]
```

| Funkcja | Stan |
|---|---|
| rozmowy w wątkach, kolory typów, stan doręczenia, filtry, wyszukiwanie | jest |
| „Napisz” i „Odpowiedz”: projekt, moduł, adresat (para / strona / rola), rodzaj, pilne – przez stos | jest |
| załączniki: plik, zrzut ekranu (wszystkie monitory), obraz lub pliki ze schowka → obiekty kontekstu SHA-256 → części `plik`; otwieranie po sprawdzeniu skrótu, typy wykonywalne tylko pokazywane w folderze | jest |
| Ctrl+V / Shift+Insert w oknach z załącznikami (Napisz, Nowe zadanie): obraz lub pliki ze schowka trafiają do załączników, tekst do pola; przeciąganie plików na okno | jest |
| czytelny widok rozmowy: bloki wiadomości, większa treść z wcięciem, załączniki do kliknięcia, dane techniczne drobno | jest |
| motyw jasny / ciemny (pasek tytułu, listy, zakładki, logo), zapamiętany w ustawieniach operatora | jest |
| spis gałęzi jako strona HTML+JS: grupy A–F, filtry, wyszukiwanie, sortowanie, gałęzie z kolorowym stanem, bieżący stan katalogów | jest |
| „Wyczyść”: archiwizacja przeczytanych do `archiwum\RRRR-MM\`, nic nie kasuje | jest |
| zakładka Połączenia: węzły, ostatni kontakt, ocena | jest |
| pary 1:1 obok siebie, role, nowe zadanie z parą, załącznikami i pakietami startowymi, znaczniki 🔗ECXP | jest |
| „Bieżąca sesja”: pasek pod nagłówkiem z nazwą pary i stanem zgłoszenia obu stron (✓ / ⏳ / ⚠, np. sesja Claude w chmurze), wyróżnienie ▶ jej rozmów i wiersza agentów, filtr „tylko ta sesja”; ustawiana przez Nowe zadanie, „▶ Ustaw jako bieżącą” albo dwuklik pary | jest |
| Nowe zadanie → sesje w aplikacjach: Codex dostaje obrazy z załączników w pierwszej turze, a po niej wątek otwiera się w aplikacji Codex linkiem `codex://threads/<id>` (wątek założony z zewnątrz pojawia się na liście „Najnowsze” dopiero po otwarciu); pakiet Claude podaje ścieżki załączników i każe zgłosić brak narzędzi ECXP (sesja w chmurze) | jest |
| łańcuchy ról (programista → tester → recenzent → decyzja → integrator), wznowienie | jest ([09](09-decyzje-lancuchy-galezie-lustro-agenci.md)) |
| agenci w tle (Claude Code CLI; Codex CLI, jeśli jest), uprawnienia, limit tur, alarm przy błędzie | jest |
| podpisane decyzje operatora („Decyzja…”), kolumny wspólnej gałęzi i łańcucha w zakładce Agenci, instalator stanowiska | jest |
| status agentów CLI (Claude Code CLI: `claude auth status`; Codex CLI: `codex login status`) w nagłówku okna, w zakładce Agenci i na pasku stanu; przyciski działają w tle, bez okien konsoli: „Zainstaluj…” – instalacja przez npm po potwierdzeniu, „Zaloguj (w tle)” – logowanie (otwiera się tylko strona logowania w przeglądarce), „▶ Agent w tle…” – agent jako usługa stosu; wyjście CLI trafia do `stan\cli-<nazwa>.log`; status odświeża się co 10 s i pokazuje „aktywne” | jest |
| menu ikony w zasobniku (prawy klawisz): projekt, stan stosu, „Nowe dla Ciebie”, pełne okno, Napisz, Nowe zadanie, Decyzja, podmenu „Status agentów CLI” z akcją dla każdego CLI, „Sprawdź status teraz” i dziennikiem usług, zakładki Agenci / Połączenia / Stan modułów, spis gałęzi, język, motyw, O programie, instalator, folder skrzynki, Zakończ; menu buduje się przy każdym otwarciu | jest |
| tryb przykładowy do zrzutów: `ECXP-MAILBOX.exe /demo <katalog> 1|2 [en]` – wymyślony projekt demo-app, bez prawdziwych danych | jest |
| usługi Claude i Codex (agenci w tle jednej strony): Start / Stop / Uruchom ponownie z konsoli i z menu zasobnika; Stop przerywa trwającą turę i wstrzymuje kolejne, restart kasuje przerwę po błędzie; stan w `stan\uslugi.json`; dwa widoki: A – pasek w nagłówku, B – karty usług (prawy klawisz przełącza) | jest |
| „⏻ Zakończ usługi” (pasek, karty, menu zasobnika) i „Zakończ” konsoli: całkowite zamknięcie wszystkich procesów agentów z drzewami potomnymi – aplikacje Claude i Codex, Claude Code, CLI (także przez node), adaptery ecxp-mcp, agenci w tle ECXP; konsola i stos ecxpd są chronione; okno potwierdzenia pokazuje, co zostanie zamknięte; po „Zakończ” usługi, które działały, wznawia następny start konsoli (operacje stosu `uslugi_zakoncz`, `uslugi_po_starcie`); podgląd bez zamykania: `ECXP-MAILBOX.exe /procesy-agentow <plik>` | jest |
| instalator stanowiska `ECXP-Setup.exe` (jeden plik, programy jako zasoby): kreator PL/EN/FR/CS/DE, instalacja dla użytkownika do `%LOCALAPPDATA%\ECXP`, rejestracja w Claude Code i Codex, autostart, skróty, wpis w Aplikacjach; aktualizacja i odinstalowanie zostawiają dane; odinstalowanie i opcje „bez skrótu” zmieniają tylko to, co wskazuje na własny katalog; tryb cichy `/cicho [/katalog] [/jezyk] [/bez-rejestracji] [/bez-autostartu] [/bez-pulpitu] [/bez-uruchomienia]`, `/odinstaluj [/cicho]`, podgląd stron `/demo 1|2|3` | jest |
| języki interfejsu: polski, angielski, francuski, czeski, niemiecki (słownik `narzedzia/zrodlo/jezyki.json` w zasobach programu, wybór w nagłówku, zapamiętany); treści wiadomości agentów nie są tłumaczone | jest |
| okno „O programie” (About): wersja, opis ECXP i ECXP-MAILBOX, odnośniki do evidsec.pl, evidsec.pl/ecxp/ i repozytorium | jest |

Testy: `ECXP-MAILBOX.exe /selftest <pusty katalog>` (rdzeń protokołu) i `ECXP-MAILBOX.exe /selftest-stos <pusty katalog>` (operator przez prawdziwy stos: para, lustro, kopie 1:1, przejęcie, role, załącznik). Spis gałęzi bez okna: `ECXP-MAILBOX.exe /spis-galezi [/ciemny]`.
