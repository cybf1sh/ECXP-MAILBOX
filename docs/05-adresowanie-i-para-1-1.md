# 05 – Adresowanie i zasada 1:1

## Adresy

| Postać | Przykład | Rozwiązanie | Stan |
|---|---|---|---|
| `wlasciciel` | `wlasciciel` | operator (konsola ECXP-MAILBOX) | jest |
| `codex-<nazwa>` / `claude-<nazwa>` | `codex-elan` | dokładnie ten węzeł | jest |
| `codex` / `codex-core` | `codex` | `codex-koordynacja` | jest |
| `claude` | `claude` | dowolna sesja Claude (pierwsza, która odbierze) | jest |
| `para:<moduł>` albo `*/<moduł>` | `para:elan` | **obie** strony pary modułu w projekcie wiadomości | w budowie |
| `codex/<moduł>`, `claude/<moduł>` | `claude/elan` | aktualna sesja danej strony w module | w budowie |
| `rola:<rola>@<moduł>` | `rola:tester@elcarver` | sesje z rolą w module (`@*` – w całym projekcie) | w budowie |

Adresy `para:`, `codex/`, `claude/` i `rola:` wymagają pola `projekt`. W odpowiedzi projekt jest dziedziczony z oryginału.

```mermaid
flowchart TD
    A["adres + projekt"] --> B{postać}
    B -- "wlasciciel / codex-x / claude-x" --> W[jeden węzeł]
    B -- "para:m / */m" --> P["Para(projekt, m)"]
    P --> P1{obie strony?}
    P1 -- tak --> P2["[codex-…, claude-…] – lustro"]
    P1 -- nie --> P3["błąd: para niepełna – brak sesji X<br/>(zasada 1:1 wymaga obu)"]
    B -- "codex/m / claude/m" --> S["Para(projekt, m)[strona]"]
    B -- "rola:r@m" --> R["trasy aktywne z rolą r w m"]
    R --> R1{ktoś pełni?}
    R1 -- nie --> R2["błąd: nikt nie pełni roli r"]
```

## Tablica tras (`stan\trasy.json`)

Każdy wpis: `projekt`, `modul`, `strona` (codex/claude), `nazwa`, `sesja` (id wątku Codexa albo sesji Claude), `role[]`, `opis`, `od`, `aktywna`.

Gdy trasy brak, para jest brana z pola `pary` w `projekty\<projekt>\projekt.json`:

```json
{ "nazwa": "evidlockv3",
  "moduly": ["elan", "elcarver"],
  "pary": { "elan": { "codex": "codex-elan", "claude": "claude-elan", "tytul": "🔗ECXP EL_an" } } }
```

### Przypisanie i przejęcie sesji (jak DHCP/ARP)

```mermaid
sequenceDiagram
    participant N as Nowa sesja (claude-elan-2)
    participant S as ecxpd
    participant Q as Kolejka starej sesji (claude-elan)
    N->>S: ecxp_przypisz(ja=claude-elan-2, projekt, modul=elan, sesja=<id>, role)
    S->>S: stara trasa claude/elan → aktywna=false
    S->>S: nowa trasa → aktywna=true, wpis "przypisanie"
    S->>Q: każda niepotwierdzona wiadomość do claude-elan
    S->>N: kopia z adnotacją „przekazane po przejęciu modułu”
    S->>Q: oryginał → potwierdzona „przyjeta: przekazana do …”
    S-->>N: {przejeto_od, przekazane[], para}
```

Nic nie ginie przy zmianie sesji. Wiadomości poprzedniej strony przechodzą do następczyni w tym samym wątku.

## Zasada 1:1

W projekcie i module pracuje **para**: jedna sesja ChatGPT/Codex i jedna sesja Claude. Obie strony mają to samo, a rozbieżność jest błędem.

### 1. Lustro zadań [w budowie]

```mermaid
sequenceDiagram
    participant O as Operator (ECXP-MAILBOX)
    participant S as ecxpd
    participant CX as codex-elan
    participant CL as claude-elan
    O->>S: wyslij(do="para:elan", typ=zadanie, projekt=evidlockv3)
    S->>S: Rozwiaz → [codex-elan, claude-elan], lustro_id = L-…
    par lustro
        S->>CX: zadanie (watek = L-…, lustro=[codex-elan, claude-elan])
    and
        S->>CL: zadanie (watek = L-…, lustro=[codex-elan, claude-elan])
    end
    S-->>O: {adresaci:[2], wyslane:[2], lustro_id}
    Note over S: każda kopia ma własny seq, termin i ACK:<br/>zadanie doręczone dopiero po ACK obu stron
```

### 2. Kopie dla pary [w budowie]

To, co przechodzi między operatorem a **jedną** stroną pary, dostaje też druga strona. Treść jest kopiowana dosłownie i poprzedzona nagłówkiem.

```mermaid
flowchart LR
    O[Operator] -- "do codex-elan" --> CX[codex-elan]
    O -. "kopia: Operator napisał do Codex · elan:" .-> CL[claude-elan]
    CL -- "meldunek do operatora" --> O
    CL -. "kopia: Claude · elan napisał do operatora:" .-> CX
```

- Kopia ma `kopia_dla = <id oryginału>` i ten sam `watek`.
- Kopie powstają dla każdej pary, do której należy strona: projekt i moduł z tras albo z `projekt.json`.

### 3. Wejście operatora z okien czatu [plan]

| Okno | Mechanizm przechwycenia |
|---|---|
| ECXP-MAILBOX | adres domyślny `para:<moduł>` |
| czat Claude | hook Claude Code `UserPromptSubmit` (tylko w sesjach przypisanych do pary) → `ecxp_wyslij(do=codex/<moduł>, typ=operator)` |
| czat Codexa | stos czyta tylko do odczytu zapis wątku przypisanego do pary (`~\.codex\sessions\…`); nowy wpis operatora → kopia do `claude/<moduł>` |

### 4. Wspólna gałąź [plan]

```mermaid
sequenceDiagram
    participant CX as Codex (para elan)
    participant S as ecxpd
    participant CL as Claude (para elan)
    participant GH as GitHub
    CX->>S: umowa-galezi {repo, galaz: feature/evidlockv3-elan-x, baza: <sha>}
    S->>CL: umowa-galezi (lustro)
    CL->>S: ACK (zgoda)
    CX->>GH: commit A → push (fast-forward)
    CX->>S: STAN_GALEZI {HEAD=A, pliki}
    S->>CL: STAN_GALEZI → Claude: fetch + sprawdzenie, że A jest potomkiem jego HEAD
    CL->>GH: commit B na A → push (ff)
    CL->>S: STAN_GALEZI {HEAD=B}
    S->>S: czy HEAD jednej strony jest przodkiem drugiej?
    alt rozbieżność (żaden nie jest przodkiem)
        S-->>CX: RST pary: wstrzymane
        S-->>CL: RST pary: wstrzymane
        S-->>S: alarm u operatora
    end
```

Zasady:
- Para nie pisze naraz w tych samych plikach. Kolejność wyznaczają role: np. Codex programista, Claude tester i recenzent, w następnej sprawie odwrotnie.
- Push na wspólną gałąź tylko fast-forward. Scalenie do gałęzi głównej wyłącznie po decyzji operatora.

## Nowe zadanie z parą [w budowie]

Operacja `nowe_zadanie(projekt, modul, tytul, opis, role_codex, role_claude)`:

1. dopisuje moduł i parę `codex-<moduł>` / `claude-<moduł>` do `projekt.json`;
2. przypisuje obie trasy z rolami;
3. tworzy **pakiety startowe** `stan\start\<nazwa>.txt`. Każdy zawiera: nazwę sesji ze wspólnym znacznikiem `🔗ECXP`, adres partnera, role, polecenia startowe (`ecxp_przypisz`, `ecxp_odbierz`) i treść zadania;
4. wysyła zadanie na `para:<moduł>` (lustro).
