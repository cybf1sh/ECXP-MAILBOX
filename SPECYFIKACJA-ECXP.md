# ECXP – Evidsec Context eXchange Protocol

Uniwersalny protokół komunikacji agentów modeli językowych i operatora. Nie jest związany z jednym projektem: projekt i moduł to parametry każdej wiadomości, sprawy i trasy. EvidLockV3 to pierwszy projekt, który z niego korzysta.
Wersja robocza `ecxp/1`, 2026-10-31.

**Obsługiwane rodziny agentów.** ECXP skupia się na dwóch modelach i ich agentach:
- **ChatGPT / Codex** (OpenAI): adresy `codex-*`, strona `codex`;
- **Claude** (Anthropic): adresy `claude-*`, strona `claude`.

Dla obu rodzin są przewidziane adaptery: MCP, a dla Codexa i Claude Code także CLI. Do tego tworzenie agentów, budzenie sesji i role. Inne rodziny modeli dałoby się podłączyć tym samym adapterem MCP, ale nie są celem.

## Wytyczne właściciela
1. **Niskopoziomowość.** Własny format ramek binarnych, własny stos, zero zależności od formatów aplikacji.
2. **Połączeniowość.** Połączenia z uzgadnianiem, stanami i zamykaniem, jak w TCP.
3. **Niezawodność.** Kolejność, potwierdzenia, ponowienia, wykrywanie strat i duplikatów, integralność.
4. **Szybkość.** Doręczenie lokalne w milisekundach, bez odpytywania.
5. **Przenoszenie kontekstu między modelami.** Warstwa kontekstu, zgodna pojęciowo z A2A.
6. **ECXP-MAILBOX jest klientem ECXP**, tak jak MCP, A2A i inne aplikacje. Nie jest protokołem.
7. **Uniwersalność.** Dowolne projekty i moduły, opisane w `projekty\<nazwa>\projekt.json`. Nic w stosie, adapterach ani konsoli nie jest wpisane na stałe dla jednego projektu.
8. **Dwie rodziny agentów.** ChatGPT/Codex i Claude.

**Miejsce instalacji.** Dziś stos i dane leżą w `D:\Projekty\evidlockv3-skrzynka` (nazwa historyczna). Docelowo neutralny katalog, np. `D:\ECXP`:
- `stos\`: kolejki, dziennik, stan, kontekst, archiwum;
- `projekty\<nazwa>\`: konfiguracja, stan modułów, spisy;
- `narzedzia\`: programy.

Stara ścieżka zostaje jako dowiązanie (junction), żeby trwające sesje nie straciły połączenia. Przeniesienie wymaga zgody właściciela i odbędzie się razem z przejściem na `ecxpd`.

## Zasada 1:1: para Codex–Claude (zasada nadrzędna)

W danym projekcie (i module) pracuje **para**: jedna sesja ChatGPT/Codex i jedna sesja Claude. Obie strony pracują nad tym samym i nie może między nimi być rozbieżności.

1. **Zadania są tożsame.** Zadanie dla pary trafia do obu sesji jako jedna sprawa: ten sam `kontekst_id`, ta sama treść, te same przypięte materiały. Stos wysyła je jako parę połączonych wiadomości (lustro). Zadanie dotarło dopiero wtedy, gdy obie strony potwierdziły odbiór. Brak potwierdzenia jednej strony uruchamia ponowienia i alarm, jak przy każdej wiadomości.
2. **Wejście operatora jest tożsame bez względu na okno.** Wiadomość, pytanie albo zadanie od operatora trafia do obu stron, czy operator napisał je w oknie czatu Codexa, w oknie czatu Claude, czy w ECXP-MAILBOX:
   - **ECXP-MAILBOX**: adres domyślny to para. Pojedynczą stronę operator wybiera świadomie.
   - **Okno czatu Claude**: hook `UserPromptSubmit` w Claude Code. Działa tylko w sesjach przypisanych do pary. Każdy wpis operatora trafia od razu do ECXP jako wiadomość typu `operator` do drugiej strony pary.
   - **Okno czatu Codexa**: stos czyta, tylko do odczytu, zapis rozmowy wątku Codexa przypisanego do pary (pliki sesji w `~\.codex\sessions`). Każdy nowy wpis operatora przekazuje do sesji Claude pary.
   - Drugiej stronie wiadomość pokazuje się jako „operator napisał do Codexa w czacie: …” (albo odwrotnie). Obie strony mają więc ten sam obraz poleceń.
   - Treść zawsze jest kopiowana dosłownie. Przy streszczeniu powstałaby rozbieżność.
3. **Odpowiedzi do operatora też są widoczne dla pary.** Meldunek jednej strony do operatora dostaje także druga strona, jako informacja w tej samej sprawie.
4. **Jedna wspólna gałąź.** Para ustala wspólną gałąź roboczą, np. `feature/<projekt>-<temat>` na GitHubie, od wskazanego commitu bazowego. Stos zapisuje tę umowę w sprawie (`ecxp/umowa-galezi`: repozytorium, gałąź, baza).
   - Po każdym commicie strona publikuje ramkę `STAN_GALEZI` (HEAD, lista plików). Druga strona musi zsynchronizować się przed kolejną zmianą.
   - Stos porównuje HEAD obu stron. Jeśli jeden nie jest przodkiem drugiego (rozbieżność), stos wstrzymuje parę i alarmuje operatora. Para nie pracuje dalej na rozjechanej historii.
   - Żeby nie było konfliktów, para nie pisze naraz w tych samych plikach. Kolejność wyznaczają role (np. programista Codex, recenzent i tester Claude, potem zamiana) albo rezerwacje plików w projekcie.
   - Push na wspólną gałąź tylko fast-forward. Czy para może sama pushować swoją gałąź roboczą, ustala operator dla projektu. Scalenie do gałęzi głównej zawsze wymaga decyzji operatora.
5. **Wspólna wiedza.** Pakiety przekazania, decyzje operatora i stan sprawy są wspólne dla pary. Każda strona widzi to samo, a nowa sesja, która zastępuje jedną ze stron, dostaje cały stan sprawy przy przypisaniu.

Para jest zapisana w konfiguracji projektu (`projekt.json`, pole `pary`: moduł → sesja Codexa, sesja Claude) i w tablicy tras. Konsola pokazuje parę obok siebie, a każda rozbieżność (zadania, wejścia operatora, gałęzi) jest na czerwono.

## Operator (człowiek) jako węzeł ECXP

Operator rozmawia z agentami na dwa sposoby: w oknach czatów (Codex, Claude) i w oknie ECXP-MAILBOX. To drugie jest jego terminalem ECXP, z adresem `wlasciciel` i rolą `operator`.

Co operator robi w oknie:
- **Pisze do dowolnej sesji, modułu albo kontekstu.** Może wskazać `claude/elan`, `codex/elan`, obie strony modułu (`*/elan`) albo konkretną sesję. Może zacząć nowy kontekst albo kontynuować istniejący.
- **Widzi rozmowy agentów między sobą.** Każda wymiana Codex↔Claude jest widoczna na żywo w wątkach. Operator nie musi przechodzić między oknami czatów.
- **Wtrąca się do kontekstu.** Wiadomość operatora w kontekście trafia do wszystkich uczestników, na przykład korekta, doprecyzowanie albo odpowiedź na pytanie agenta.
- **Steruje zadaniami.** Może wstrzymać, wznowić, anulować, przekazać innej sesji i zmienić priorytet. Stos przenosi to jako ramki sterujące.
- **Odpowiada na pytania i blokady agentów.** Pytanie skierowane do operatora (`potrzebne_dane`, blokada) pojawia się w oknie z przyciskiem odpowiedzi. Zadanie czeka do tej odpowiedzi.

## Konsola operatora: sterowanie wymianą między modelami i agentami

Zasada: **ECXP-MAILBOX steruje wymianą między modelami i agentami.** Operator wybiera projekt, potem moduły i sesje, które mają w danej sprawie współpracować. Stos ECXP przenosi wiadomości, kontekst i zadania tylko tam, gdzie operator je skierował.

**Projekty** (`stan\projekty\<projekt>.json`). Każdy projekt ma:
- repozytorium i katalog główny;
- gałąź główną;
- listę modułów;
- pliki zasad, które agent dostaje przy starcie (`AGENTS.md`, `docs/PRACA-WSPOLNA-CODEX-CLAUDE.md`);
- reguły bramek, czyli polecenia testera;
- katalog na klony i worktree agentów.

Pierwszy projekt to `evidlockv3` (moduły: glowne-okno, elan, profilowanie, elcarver, elcommander, elview, nagrywanie, wydania…). Następne, np. `evidlockv2` czy `elzip`, dodaje się w konsoli.

- Adres z projektem: `evidlockv3:claude/elan`, `evidlockv3:rola:recenzent@elan`. Bez przedrostka obowiązuje projekt wybrany w konsoli.
- Sprawy, role, łańcuchy pracy i karty węzłów należą do projektu. Agent jednego projektu nie dostaje kontekstu innego, chyba że operator sam go przypnie.

Kolejność pracy operatora w konsoli:
1. projekt;
2. moduły;
3. sesje lub nowi agenci z rolami;
4. sprawa (kontekst) i przypięte materiały;
5. łańcuch pracy;
6. start.

Potem konsola pokazuje na żywo wymianę między agentami i miejsce, w którym sprawa czeka.

## Konsola operatora: sterowanie agentami, rolami i kontekstem

ECXP-MAILBOX to konsola sterująca projektem, nie tylko skrzynka odbiorcza. Każde sterowanie idzie do stosu jako podpisana ramka operatora, a stos rozsyła je do sesji.

**Agenci (sesje):**
- Lista wszystkich sesji Codexa i Claude: moduł, rola, stan (pracuje, bezczynna, czeka na operatora, milczy), ostatni sygnał, otwarte zadania.
- Działania na sesji: przypisz lub odepnij moduł, nadaj rolę, obudź, wstrzymaj, wznów, przekaż jej sprawy innej sesji, zamknij (FIN).

**Tworzenie nowego agenta** (formularz „Nowy agent” w konsoli):

| Pole | Przykład |
|---|---|
| nazwa | `claude-elan-recenzent` |
| strona | Claude albo Codex |
| role | recenzent, tester (wybór z listy) |
| opis roli | własny tekst operatora, dopisany do karty roli, np. „skup się na obsłudze błędów sieci” |
| moduł | `elan` |
| zadanie lub kontekst | istniejący (np. ELAN23) albo nowy |
| katalog pracy i gałąź | osobny klon lub worktree, np. `evidlockv3-agents\elv3-elan-recenzja`, gałąź od `elv3`. Programiści nigdy nie dzielą plików |
| model i wysiłek | np. Opus, wysoki |
| budżet | najwyżej N tur albo kosztu, limit kontekstu |
| widoczność | w tle (bez okna) albo jako sesja widoczna w aplikacji |

Jak stos uruchamia agenta (dostawcy):
- **Claude w tle**: Claude Code CLI (`claude -p`, jest zainstalowany, 2.1.283).
  - Stos uruchamia proces z kartą roli jako dodatkowym promptem systemowym, z serwerem MCP `ecxp` i katalogiem pracy.
  - Przez `--session-id` i `--resume` sesja przetrwa kolejne tury.
  - Rozmowę prowadzi strumieniem JSON, a wyjście trafia do ECXP. Nie trzeba niczego budzić: stos sam podaje kolejną turę po nadejściu wiadomości.
- **Claude jako sesja widoczna w aplikacji**: stos prosi sesję pośredniczącą (`claude-core`) o założenie sesji w aplikacji Claude. Pojawia się ona w pasku bocznym jak każda inna.
- **Codex**:
  - Codex CLI (`codex exec` i wznawianie) daje to samo co Claude w tle, ale nie jest zainstalowany. Instalacja to decyzja właściciela.
  - Bez niego konsola przygotowuje pakiet startowy (karta roli, moduł, kontekst, polecenie przypisania do ECXP). Operator wkleja go w nowy wątek Codexa, a automatyzację wątku zakłada stos.
- Po starcie agent dostaje ramkę `INIT`: kartę roli, przypięte obiekty kontekstu, adres koordynatora i swoje miejsce w łańcuchu pracy. Potem zgłasza `ecxp_przypisz`, a stos wpisuje go do tablicy tras i kart węzłów.
- **Granice**:
  - konsola pokazuje liczbę działających agentów i limit ustawiony przez operatora, a każdy agent zużywa limity konta;
  - agent w tle ma uprawnienia zgodne z rolą: recenzent i tester bez prawa zapisu w cudzych gałęziach, nikt nie robi push, scalenia ani tagów;
  - zamknięcie agenta (FIN) kończy proces i przekazuje jego otwarte sprawy dalej według łańcucha.

**Role** (definicje w `stan\role\<rola>.json`, edytowalne w oknie):

| Rola | Zadanie | Wynik | Zasady stosu |
|---|---|---|---|
| `programista` | zmiany w kodzie na własnej gałęzi | commit + pakiet przekazania | tylko w przydzielonych plikach i rezerwacjach |
| `recenzent` | niezależna recenzja zmiany | werdykt: przyjąć, poprawić, odrzucić, z uwagami | nie może recenzować własnej zmiany. Stos odrzuca przydział, gdy recenzent jest autorem commitu albo sesją, która go zrobiła |
| `tester` | bramki i testy: cargo fmt, clippy -D warnings, cargo test (z listą pominiętych), build --release, test okien | raport PASS/FAIL z dokładnymi poleceniami | wynik FAIL i BLOCKED raportuje bez zmian |
| `bezpieczenstwo` | przegląd bezpieczeństwa zmian wrażliwych | raport zagrożeń | wymagany, gdy zmiana jest oznaczona jako wrażliwa |
| `koordynator` | kieruje sprawami do modułów i ról, pilnuje terminów | przydziały, przekazania | nie zmienia kodu |
| `integrator` | scalanie i wydania | commit scalający, raport bramek | push, scalenie i tagi tylko po decyzji operatora |

- Jedna sesja może mieć kilka ról, a role można zmieniać w trakcie. Każda zmiana trafia do dziennika.
- Nadanie roli wysyła do sesji **kartę roli**: obowiązki, granice, wymagane kontrole, format wyniku. Agent dostaje ją jako kontekst, więc wie, jak ma działać.
- **Adresowanie po roli**: `rola:recenzent@elan` trafia do sesji z rolą recenzenta w module EL_an, a `rola:tester@*` do testera dowolnego modułu. Gdy roli nikt nie pełni, sprawa czeka, a konsola pokazuje „brak recenzenta dla EL_an”.

**Przepływy pracy (łańcuchy ról)** ustawiane w konsoli. Przykład dla zmiany produkcyjnej:
`programista → tester → recenzent → (bezpieczenstwo, gdy wrażliwa) → operator: decyzja → integrator`.
- Stos sam przekazuje sprawę do następnej roli po wyniku poprzedniej. FAIL testera albo werdykt „poprawić” wraca do programisty.
- Na każdym kroku konsola pokazuje, gdzie jest sprawa i na kogo czeka.

**Kontekst:**
- Tworzenie, zamykanie i łączenie kontekstów (spraw). Dodawanie i usuwanie uczestników (sesji i ról).
- Przypinanie do kontekstu obiektów: pakietu przekazania, specyfikacji, wyników testów. Każdy nowy uczestnik dostaje je od razu, bez przeklejania rozmów.
- Budżet kontekstu: ile najwyżej wczytać naraz. Chroni okno kontekstu modelu.
- Podgląd: kto co wie, czyli które obiekty dana sesja już pobrała.

**Uwierzytelnienie operatora:**
- Stos przyjmuje rolę `operator` tylko od procesu `ECXP-MAILBOX.exe` z katalogu skrzynki, uruchomionego przez bieżącego użytkownika Windows.
- Każda wiadomość operatora jest podpisana kluczem operatora. Klucz jest chroniony przez Windows (DPAPI) i dostępny tylko dla tego użytkownika.
- Agent sprawdza podpis przez stos. Wiadomość z poprawnym podpisem wie, że pochodzi od człowieka, a nie od innego agenta podszywającego się pod niego.

**Decyzje operatora przez okno** (push, scalenie, USER_ACCEPTED dla dokładnego commitu, usunięcie, zmiana wymagań):
- Okno pokazuje decyzję w osobnym, wyraźnym potwierdzeniu: co dokładnie, jaki commit, czego dotyczy.
- Wysyła ją jako ramkę `DECYZJA` z podpisem operatora.
- Czy agenci mają taką decyzję traktować na równi z decyzją wydaną w czacie, rozstrzyga właściciel (patrz „Do decyzji”). Do tego czasu decyzje zapadają tylko w czacie, a okno wysyła pytania, zadania i odpowiedzi.

## Architektura (jak stos TCP/IP w systemie)

```
 ┌───────────────┐  ┌────────────────────┐  ┌──────────────────────┐
 │ ECXP-MAILBOX  │  │ ecxp-mcp (adapter)  │  │ brama A2A (później,  │   ← aplikacje / adaptery
 │ okno właśc.   │  │ każdy czat/sesja    │  │ tylko LAN)           │
 └──────┬────────┘  └─────────┬──────────┘  └──────────┬───────────┘
        │  potok nazwany \\.\pipe\ecxp (ramki ECXP, tylko bieżący użytkownik Windows)
 ┌──────┴─────────────────────┴────────────────────────┴───────────┐
 │ ecxpd – stos ECXP (jeden proces, autostart)                      │
 │  L3 kontekst: części, obiekty SHA-256, zadania, karty węzłów     │
 │  L2 połączenia i niezawodność: uzgadnianie, seq/ack, okno,       │
 │     ponowienia z narastającym odstępem, kolejność, duplikaty     │
 │  L1 trwałość: kolejki na dysku, zapis atomowy, dziennik łańcuch. │
 └──────────────────────────────────────────────────────────────────┘
```

- **ecxpd** jest jedynym procesem, który pisze w skrzynce. Trzyma zegary ponowień i strażnika.
- Klient łączy się przez potok nazwany. ACL potoku dopuszcza tylko bieżącego użytkownika.
- Stos zna obraz procesu klienta (`GetNamedPipeClientProcessId`). Zastrzeżoną nazwę `wlasciciel` dostaje wyłącznie `ECXP-MAILBOX.exe` z katalogu skrzynki. Dziś każdy proces może podpisać plik jako właściciel, a ten mechanizm to zamyka.
- Gdy stos nie działa, adapter go uruchamia. Gdy uruchomić się nie da, zgłasza błąd zamiast cicho zapisać plik.

## Szybkość
- **Bez odpytywania.** Klient po HELLO trzyma otwarty potok, a stos wypycha ramkę DANE w chwili zapisu. To odwrotność dzisiejszego skanowania folderów co kilka sekund.
- **Indeks w pamięci.** Stos czyta dysk raz przy starcie, potem utrzymuje kolejki, połączenia i stany w pamięci. Dysk służy tylko trwałości (zapis z wyprzedzeniem, WAL). Żadna operacja nie przegląda katalogów.
- **Jeden zapis trwały na wiadomość.** Ramka z wpisem dziennika trafia jednym `WriteThrough`. Kilka ramek naraz zapisuje się w jednej paczce (group commit).
- **Cele**: wysłanie i doręczenie podłączonemu klientowi poniżej 10 ms. Odbiór listy oczekujących poniżej 5 ms. Start stosu poniżej 1 s.
- **Granica**: szybkość reakcji modelu nie zależy od protokołu. Czat Codexa bez otwartej tury odbierze dopiero, gdy go obudzi automatyzacja (dziś co 10 min) albo właściciel. Stos może skrócić tę zwłokę, wyzwalając automatyzację od razu po nadejściu wiadomości, jeśli Codex na to pozwoli. Do sprawdzenia.

## L2 – adresowanie i trasowanie (jak IP i porty)

Cel: wybierasz moduł i kontekst, np. „zadanie: działaj na module EL_an”. Obie strony, Codex i Claude, wiedzą wtedy, do której konkretnej sesji trafia wiadomość lub zadanie i dokąd odsyłać odpowiedź.

**Adres ECXP**: `<strona>/<moduł>[/<sesja>]`

| Adres | Znaczenie |
|---|---|
| `codex/elan` | aktualna sesja Codexa prowadząca moduł EL_an |
| `claude/elan` | aktualna sesja Claude prowadząca moduł EL_an |
| `*/elan` | obie strony modułu naraz (rozsyłanie grupowe) |
| `rola:recenzent@elan` | sesja z rolą recenzenta w module EL_an |
| `codex/elan/019a…` | dokładnie ten wątek Codexa |
| `claude/elan/local_ab12…` | dokładnie ta sesja Claude |
| `wlasciciel` | właściciel (okno ECXP-MAILBOX) |

- **Przypisanie sesji** (jak DHCP lub ARP): każda sesja przy starcie pracy zgłasza `ecxp_przypisz(strona, moduł, id sesji, rola)`, np. „jestem sesją Claude `local_…`, prowadzę EL_an”. Stos wpisuje to do **tablicy tras** `stan\trasy.json`. Nowa sesja tego samego modułu przejmuje adres, a stara dostaje FIN. Wiadomości, które czekały w kolejce modułu, przechodzą do nowej sesji, nic nie ginie.
- **Tablica tras** wiąże moduł ze stroną i z sesją: id sesji, rodzaj (wątek Codexa albo sesja Claude), od kiedy aktywna, kiedy ostatni sygnał. Bez przypisanej sesji wiadomość czeka w kolejce modułu, a sprawę przejmuje trasa zastępcza `codex/koordynacja`.
- **Kontekst** (`kontekst_id`): każda sprawa lub zadanie ma listę uczestników, czyli konkretne adresy sesji. Odpowiedź w tym kontekście trafia do sesji, które w nim uczestniczą, a nie do „kogokolwiek z modułu”. W oknie wybierasz: nowy kontekst albo kontynuację istniejącego (np. ELAN23).
- **Przykład**: zlecasz „Działaj na module EL_an” na adres `*/elan`.
  1. Stos otwiera dwa połączenia: do sesji Codexa i do sesji Claude modułu EL_an.
  2. Obie dostają to samo zadanie z tym samym `kontekst_id`.
  3. Codex, pisząc do `claude/elan` w tym kontekście, trafia do właściwej sesji Claude, i odwrotnie.
  4. Wyniki wracają do Ciebie.
- **Budzenie sesji** (doręczenie do sesji, która nie ma otwartej tury):
  - wątek Codexa: przez jego automatyzację (`target_thread_id` = id sesji z tablicy tras);
  - sesja Claude: przez aplikację Claude, która może dostarczyć wiadomość do sesji o danym id.
  Stos zapisuje, którą drogą budzić daną sesję. Gdzie budzenia nie ma, wiadomość czeka i działa zwykły zegar ponowień.

## L2 – ramka ECXP

Kolejność bajtów: little-endian. Nagłówek ma stałe 72 bajty.

| Bajty | Pole | Opis |
|---|---|---|
| 0–3 | `magic` | `ECXP` |
| 4 | `wersja` | 1 |
| 5 | `typ` | HELLO, DANE, ACK, SYN, FIN, RST, PING, PONG, BLAD, FRAG |
| 6–7 | `flagi` | SYN, ACK, FIN, RST, PSH, PILNE, FRAG, OSTATNI |
| 8–15 | `polaczenie` | identyfikator połączenia (64 bity) |
| 16–23 | `seq` | numer kolejny w połączeniu |
| 24–31 | `ack` | potwierdzenie skumulowane: wszystko do `ack−1` odebrane |
| 32–35 | `okno` | ile wiadomości adresat przyjmie bez potwierdzenia |
| 36–39 | `dlugosc` | długość ładunku, najwyżej 1 MiB |
| 40–71 | `skrot` | SHA-256 bajtów 0–39 i ładunku |
| 72… | `ladunek` | JSON UTF-8 wiadomości albo fragment binarny obiektu |

Ramka z nieprawidłowym `magic`, wersją, długością lub skrótem jest odrzucana ramką `BLAD`. Nadawca ją ponawia.

## L2 – połączenia (maszyna stanów jak TCP)

Połączenie łączy parę węzłów, np. `wlasciciel ↔ codex-koordynacja`. Jest trwałe: przeżywa restart stosu i komputera, bo stan leży w `stan\polaczenia\`.

```
ZAMKNIETE ──SYN──▶ SYN_WYSLANY ──SYN+ACK──▶ NAWIAZANE ──FIN──▶ FIN_CZEKA ──ACK──▶ ZAMKNIETE
     ▲                                          │
     └──────────── RST (błąd, porzucenie) ◀─────┘
```

- **Uzgadnianie trójstronne**: SYN (x), potem SYN+ACK (y, ack = x+1), potem ACK. SYN może od razu nieść dane, jak TCP Fast Open. Stos przechowuje SYN do czasu, aż turowy adresat się podłączy.
- **Kolejność**: stos przekazuje adresatowi wiadomości ściśle według `seq`. Wiadomość spoza kolejności czeka na brakujące.
- **Duplikaty**: ramka o `seq` już przyjętym jest potwierdzana ponownie i odrzucana.
- **Potwierdzenia**:
  - skumulowane `ack`;
  - selektywne (SACK);
  - potwierdzenie doklejone do odpowiedzi;
  - potwierdzenie aplikacyjne (`obsluzona`, `przyjeta`, `odrzucona`) jako wyższy poziom.
- **Ponowienia**: odstęp RTO narasta wykładniczo. Podstawa zależy od adresata: pilne 15 min, koordynacja 30 min, czaty modułów 4 h. Po trzech próbach przychodzi RST i alarm u właściciela. Dla czatu modułu dodatkowo trasa zastępcza przez `codex-koordynacja`.
- **Okno**: domyślnie 16 niepotwierdzonych wiadomości na połączenie. Chroni kontekst modelu przed zalaniem.
- **Utrzymanie**: PING i PONG. Stos wie, które węzły żyją.
- **Zamykanie**: FIN po zakończeniu sprawy.

Gwarancja ECXP: wiadomość zostaje dostarczona w kolejności i potwierdzona, albo nadawca i właściciel dostają RST z przyczyną. Ciche zgubienie jest wykluczone.

## L1 – trwałość
- Kolejki na dysku: `do-*`, `przeczytane`, `archiwum`. Zapis atomowy: `.tmp` z WriteThrough i Flush, potem zmiana nazwy.
- Dziennik łańcuchowy `dziennik\dziennik.jsonl`: każdy wpis zawiera SHA-256 poprzedniego.
- Stan połączeń w `stan\polaczenia\`. Po restarcie stos odtwarza okna, numery i zegary.

## L3 – kontekst i zadania (zgodne pojęciowo z A2A)
- **Części wiadomości**: `tekst`, `dane` (JSON ze schematem), `plik` (referencja `sha256`).
- **Obiekty kontekstu** `kontekst\obiekty\<sha256>`:
  - niezmienne i sprawdzane przy każdym pobraniu;
  - do 32 MB, pobierane porcjami, więc model sam kontroluje zużycie tokenów;
  - ścieżki wyglądające na sekrety lub dowody są odrzucane.
- **Pakiet przekazania pracy** (`ecxp/przekazanie`): cel, stan, repozytorium, gałąź, commit, pliki z hashami, decyzje, ograniczenia, otwarte pytania, kontrole.
- **Zadania**: `zgloszone → w_toku → potrzebne_dane → zakonczone | nieudane | anulowane | odrzucone`. Do tego artefakty jako obiekty kontekstu.
- **Karty węzłów** `stan\karty\<nazwa>.json`: kim jest węzeł, jakie moduły prowadzi, jakie ma umiejętności.
- `kontekst_id` odpowiada A2A contextId.

## Adaptery (aplikacje)
- **ecxp-mcp**: daje modelom narzędzia `ecxp_odbierz`, `ecxp_wyslij`, `ecxp_potwierdz`, `ecxp_stan`, `ecxp_watek`, `ecxp_sygnal`, `ecxp_kontekst_zapisz`, `ecxp_kontekst_pobierz`, `ecxp_przekaz`, `ecxp_zadanie`, `ecxp_karta`, `ecxp_przypisz` (sesja zgłasza moduł i swoje id), `ecxp_trasy` (kto obsługuje który moduł).
- **ECXP-MAILBOX**: okno właściciela, klient z zastrzeżoną nazwą `wlasciciel`.
- **Brama A2A** (opcjonalna, później): tylko LAN, nigdy internet.

## Bezpieczeństwo
- Treść wiadomości to dane. Nie nadaje uprawnień.
- Decyzje właściciela zapadają tylko w czacie: push, merge, USER_ACCEPTED, usuwanie, rezerwacje, ustawienia bezpieczeństwa.
- Bez danych spraw, dowodów, haseł, kluczy i odpowiedzi modelu.
- Potok tylko dla bieżącego użytkownika. Nazwy zastrzeżone są przypisane do obrazu procesu.

## Do decyzji właściciela
- Czy zainstalować Codex CLI, żeby konsola tworzyła agentów Codexa tak samo jak agentów Claude, bez wklejania pakietu startowego?
- Czy podpisana decyzja operatora z okna ECXP-MAILBOX (ramka `DECYZJA`) ma moc taką jak decyzja wydana w czacie, łącznie z USER_ACCEPTED dla dokładnego commitu? Wymaga to też zmiany zasad w `AGENTS.md` i `docs/PRACA-WSPOLNA-CODEX-CLAUDE.md`.

## Stan wykonania (2026-10-31, ECXP 1.1)
| Część | Stan |
|---|---|
| L1 trwałość, dziennik łańcuchowy | gotowe, testy PASS |
| L2 stos `ecxpd`: potok, ramki 72 B z SHA-256, uzgadnianie, seq/ack, ponowienia, strażnik, wypychanie | gotowe, testy PASS |
| L2 adresowanie i para 1:1 (lustro, kopie dla pary, przejęcie adresu, role) | gotowe, testy PASS |
| L3 części, obiekty SHA-256, zadania, karty, pakiet przekazania, załączniki | gotowe, testy PASS |
| Podpisane decyzje operatora (RSA, DPAPI, `ecxp_sprawdz_decyzje`) | gotowe, testy PASS |
| Łańcuchy ról przekazywane przez stos | gotowe, testy PASS |
| Wspólna gałąź pary: umowa, STAN_GALEZI, rozbieżność i wstrzymanie | gotowe, testy PASS |
| Lustro wpisów z okien czatów (hook Claude, zapis rozmów Codexa) | gotowe, testy PASS (Codex na symulowanym zapisie rozmowy) |
| Agenci w tle: Claude Code CLI | gotowe; tura testowa czeka na zalogowanie CLI na stanowisku |
| Agenci w tle: Codex CLI | kod gotowy, nieprzetestowany – na stanowisku brak Codex CLI |
| Instalator stanowiska (`/instaluj`, `/odinstaluj`) | gotowe |
| Brama A2A w LAN | plan |

Szczegóły funkcji 1.1: [docs/09](docs/09-decyzje-lancuchy-galezie-lustro-agenci.md).
