# 03 – Połączenia

ECXP ma dwa poziomy połączeń:

1. **Łącze klient↔stos** (L4), lokalne, przez potok nazwany. Uzgadnianie, numeracja, wypychanie, zamykanie. **[w budowie]**
2. **Połączenie logiczne między węzłami** (L2), np. `wlasciciel ↔ codex-koordynacja` albo `codex-elan ↔ claude-elan`. Trwałe na dysku, przeżywa restart stosu i komputera, numerowane `seq` w parze. **[jest]** w zakresie seq/ACK/ponowień. Stany SYN/FIN na poziomie pary: **[plan]**.

## 1. Łącze klient↔stos

### Uzgadnianie trójstronne

```mermaid
sequenceDiagram
    participant C as Klient (konsola / ecxp-mcp)
    participant S as ecxpd
    C->>S: HELLO [SYN] seq=x  {ja, rodzaj, protokol, pid}
    Note right of S: sprawdza obraz procesu klienta<br/>(GetNamedPipeClientProcessId),<br/>przyznaje nazwę i rolę
    S->>C: WITAJ [SYN|ACK] polaczenie=P, seq=y, ack=x+1  {ja}
    C->>S: ACK [ACK] polaczenie=P, seq=x+1, ack=y+1
    Note over C,S: NAWIĄZANE
```

Klient odrzuca uzgadnianie, gdy:
- odpowiedź nie jest typu WITAJ;
- nie ma kompletu flag SYN|ACK;
- `ack ≠ x+1`.

Stos odpowiada RST albo BLAD, gdy odmawia. Przykład: ktoś inny niż `ECXP-MAILBOX.exe` z katalogu skrzynki prosi o rolę operatora.

### Zapytanie i odpowiedź

```mermaid
sequenceDiagram
    participant C as Klient
    participant S as ecxpd
    C->>S: ZAPYTANIE [PSH|ACK] seq=n, ack=a  {op:"wyslij", args:{…}}
    S->>C: ODPOWIEDZ [ACK] seq=m, ack=n+1  {ok:true, wynik:{…}}
    Note left of C: dopasowanie odpowiedzi do zapytania po ack = n+1<br/>(wiele zapytań może być w locie)
```

- Klient czeka na odpowiedź z `ack = n+1`, domyślnie najwyżej 60 s. Brak odpowiedzi to błąd „stos nie odpowiedział w czasie”.
- `{ok:false, blad:"…"}` albo ramka BLAD dają po stronie klienta wyjątek z dokładnym opisem, np. „Para modułu 'elan' jest niepełna – brak sesji: Claude”.
- Operacje (`op`): `wyslij`, `odbierz`, `potwierdz`, `stan`, `watek`, `sygnal`, `przypisz`, `trasy`, `rola`, `nowe_zadanie`, `kontekst_zapisz`, `kontekst_pobierz`, `przekaz`, `zadanie`, `karta`, `archiwizuj`, `straz`, `ping`.

### Wypychanie (bez odpytywania)

```mermaid
sequenceDiagram
    participant S as ecxpd
    participant K as Konsola
    Note over S: nowa wiadomość / zmiana stanu w stosie
    S->>K: DANE [PSH] seq=m  {zdarzenie}
    K->>S: ACK ack=m+1
    Note over K: odświeżenie widoku natychmiast
```

### Utrzymanie i zamykanie

```mermaid
sequenceDiagram
    participant A as Strona A
    participant B as Strona B
    A->>B: PING seq=k
    B->>A: PONG [ACK] ack=k+1
    Note over A,B: …
    A->>B: FIN [FIN|ACK] seq=n
    Note over A,B: A zamyka potok, B zwalnia sesję
    B--xA: RST {blad}  (zerwanie przy błędzie)
```

### Maszyna stanów łącza (strona klienta)

```mermaid
stateDiagram-v2
    [*] --> ZAMKNIETE
    ZAMKNIETE --> LACZENIE: Connect(400 ms) / uruchom ecxpd, gdy brak (≤45 s)
    LACZENIE --> SYN_WYSLANY: HELLO [SYN]
    SYN_WYSLANY --> NAWIAZANE: WITAJ [SYN|ACK], ack=x+1 / ACK
    SYN_WYSLANY --> ZAMKNIETE: RST / BLAD / zły WITAJ
    SYN_WYSLANY --> LACZENIE: zerwanie w trakcie uzgodnienia (ponowienie ≤6)
    NAWIAZANE --> NAWIAZANE: ZAPYTANIE↔ODPOWIEDZ, DANE→ACK, PING→PONG
    NAWIAZANE --> ZAMKNIETE: FIN wysłany (Dispose)
    NAWIAZANE --> ZAMKNIETE: FIN / RST odebrany, koniec strumienia, zły skrót
```

## 2. Połączenie logiczne między węzłami (para nadawca→adresat)

Numeracja i potwierdzenia między węzłami są trwałe na dysku. Patrz [04-niezawodnosc.md](04-niezawodnosc.md).

Projekt docelowego cyklu życia połączenia pary **[plan]**:

```mermaid
stateDiagram-v2
    [*] --> ZAMKNIETE
    ZAMKNIETE --> SYN_WYSLANY: pierwsza wiadomość (SYN niesie dane, jak TCP Fast Open)
    SYN_WYSLANY --> NAWIAZANE: adresat odebrał i potwierdził (SYN+ACK)
    SYN_WYSLANY --> RESET: brak potwierdzenia po ponowieniach → alarm u operatora
    NAWIAZANE --> NAWIAZANE: wiadomości seq n, n+1, …; ACK skumulowany
    NAWIAZANE --> FIN_CZEKA: FIN (sprawa zakończona / sesja zamknięta)
    FIN_CZEKA --> ZAMKNIETE: ACK FIN
    NAWIAZANE --> RESET: rozbieżność, utrata sesji bez następcy
    RESET --> ZAMKNIETE: operator rozstrzyga
```

Węzły modelowe są **turowe**: działają tylko w czasie tury. Dlatego stos przechowuje SYN i dane do chwili, gdy adresat się odezwie, a uzgadnianie nie opóźnia pierwszej wiadomości.
