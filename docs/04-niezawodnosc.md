# 04 – Niezawodność

Gwarancja ECXP: **wiadomość zostaje potwierdzona przez adresata albo nadawca i operator dostają alarm z przyczyną. Ciche zgubienie jest niemożliwe.**

Ograniczenie: stos nie obudzi modelu, który nie ma otwartej tury. Wiadomość czeka trwale, a zegar ponowień wymusza reakcję operatora.

## Identyfikacja wiadomości [jest]

| Pole | Opis |
|---|---|
| `id` | `RRRRMMDD-GGMMSS-<moduł>-<TEMAT>-<4 hex losowe>`, jednocześnie nazwa pliku `<id>.json` |
| `seq` | numer kolejny w parze **nadawca→adresat**: licznik `stan\seq\<od>__<do>.txt`, zwiększany pod blokadą |
| `skrot` | `SHA-256(id ‖ "|" ‖ od ‖ "|" ‖ do ‖ "|" ‖ seq ‖ "|" ‖ tresc)` |
| `watek` / `kontekst_id` | identyfikator sprawy (pierwsza wiadomość wątku, lustro pary albo podany) |
| `odpowiedz_na` | `id` wiadomości, na którą to odpowiedź |
| `termin_potwierdzenia_min` | termin ACK zależny od adresata |

## Stany wiadomości

```mermaid
stateDiagram-v2
    [*] --> WYSLANA: zapis atomowy + wpis "wyslana" (SHA-256 pliku)
    WYSLANA --> DOSTARCZONA: adresat pobrał (ecxp_odbierz) → "dostarczona"
    DOSTARCZONA --> POTWIERDZONA: ecxp_potwierdz / odpowiedź z odpowiedz_na → "potwierdzona"
    WYSLANA --> PONOWIENIE: termin minął
    DOSTARCZONA --> PONOWIENIE: termin minął
    PONOWIENIE --> PONOWIENIE: kolejny termin (najwyżej 2 ponowienia)
    PONOWIENIE --> POTWIERDZONA: ACK
    PONOWIENIE --> ESKALACJA: po 2. ponowieniu nadal brak ACK
    ESKALACJA --> [*]: alarm u operatora (+ przekazanie do codex-koordynacja dla czatów modułów Codexa)
    WYSLANA --> ZAGINIONA: wpis w dzienniku jest, pliku nie ma
    ZAGINIONA --> [*]: alarm u operatora
    POTWIERDZONA --> [*]: plik w przeczytane\
```

## Terminy potwierdzenia [jest]

| Adresat | Termin | Uzasadnienie |
|---|---|---|
| wiadomość pilna | 15 min | |
| `codex-koordynacja` (i `codex`, `codex-core`) | 30 min | automatyzacja co 10 min |
| czaty modułów Codexa, sesje Claude | 240 min | czytają między etapami pracy |
| `wlasciciel` | brak | pilnuje konsola: alarm wizualny |

Ponowienie liczy się od wysłania albo od ostatniego ponowienia. Projekt docelowy: odstęp RTO narastający wykładniczo (T, 2T, 4T) **[plan]**.

## Potwierdzenia [jest]

- **Jawne**: `ecxp_potwierdz(id, wynik)`, gdzie wynik to `obsluzona`, `przyjeta` albo `odrzucona` (z powodem). Powtórne potwierdzenie jest **idempotentne** i zwraca `juz_potwierdzona`.
- **Doklejone** (jak ACK w danych TCP): `ecxp_wyslij(odpowiedz_na=id)` od adresata oryginału potwierdza oryginał w tej samej operacji, pod tą samą blokadą.
- **Zgodność wstecz**: plik przeniesiony do `przeczytane\` bez narzędzia strażnik zapisuje jako potwierdzenie z adnotacją.
- Potwierdzić można tylko wiadomość zaadresowaną do siebie, czyli gdy `DoMnie(ja, do)` jest prawdziwe. Ogólny `codex` i `codex-core` obsługuje koordynacja, ogólny `claude` dowolna sesja Claude.

## Zapis atomowy [jest]

```mermaid
flowchart LR
    A[serializacja JSON] --> B["FileStream(id.tmp, CreateNew,<br/>FileOptions.WriteThrough)"]
    B --> C["Write + Flush(true)<br/>(dane na nośniku)"]
    C --> D["File.Move(id.tmp → id.json)<br/>(atomowa zmiana nazwy)"]
    D --> E["SHA-256 pliku → wpis 'wyslana'"]
```

Czytelnik widzi tylko pliki `.json`, więc nigdy nie trafi na połowę zapisu. Plik `.tmp` starszy niż 2 min to przerwane wysyłanie, które `ecxp_odbierz` zgłasza jako ostrzeżenie.

## Dziennik łańcuchowy [jest]

Plik `dziennik\dziennik.jsonl` jest tylko dopisywany, a każdy wpis zajmuje jedną linię.

```
body_i  = JSON wpisu i, zawierający "poprz": hash_{i-1}  (hash_0 = 64 × "0")
hash_i  = SHA-256( hash_{i-1} ‖ body_i )
linia_i = body_i bez końcowego "}" ‖ ,"hash":"<hash_i>"}
```

```mermaid
flowchart LR
    Z["000…0"] --> W1["wpis 1<br/>poprz=000…0<br/>hash=h1"]
    W1 --> W2["wpis 2<br/>poprz=h1<br/>hash=h2"]
    W2 --> W3["wpis 3<br/>poprz=h2<br/>hash=h3"]
    W3 --> W4["…"]
```

Weryfikacja przechodzi wszystkie linie i dla każdej przelicza `SHA-256(hash_{i-1} ‖ body_i)`. Zmiana dowolnego bajtu wpisu albo usunięcie wpisu psuje wszystkie następne skróty. Konsola pokazuje wtedy „dziennik: NARUSZONY: wpis n …”.

Zdarzenia: `wyslana`, `dostarczona`, `potwierdzona`, `ponowienie`, `eskalacja`, `zaginiona`, `zarchiwizowana`, `zadanie`, `obiekt`, `karta`, `przypisanie`, `nowe_zadanie`.

Zapis każdego wpisu: `FileMode.Append` + `WriteThrough` + `Flush(true)`, pod blokadą.

## Strażnik [jest]

Strażnik uruchamia się co 15 s i przy każdej zmianie w skrzynce. Dla każdej wiadomości z wpisem `wyslana` bez `potwierdzona`:

```mermaid
flowchart TD
    S[wiadomość niepotwierdzona] --> F{plik istnieje?}
    F -- nie --> ZG["wpis 'zaginiona' + alarm"]
    F -- "tak, w przeczytane/archiwum" --> PT["wpis 'potwierdzona'<br/>(przeniesiona ręcznie)"]
    F -- "tak, w kolejce" --> T{termin > 0 i minął?}
    T -- nie --> OK[czekaj]
    T -- tak --> N{ponowienia < 2?}
    N -- tak --> PN["wpis 'ponowienie' (nr+1)<br/>widoczne w ecxp_odbierz"]
    N -- nie --> ES["wpis 'eskalacja' + alarm u operatora"]
    ES --> CX{adresat to czat modułu Codexa?}
    CX -- tak --> KO["zadanie BRAK-POTWIERDZENIA<br/>do codex-koordynacja"]
```

## Integralność przy odbiorze [jest]

`ecxp_odbierz` zwraca dla każdej wiadomości pole `integralnosc`:
- `ok`: SHA-256 pliku zgadza się z wpisem `wyslana`, a `skrot` z treścią;
- `USZKODZONA: plik zmieniony po wysłaniu`, `USZKODZONA: skrót treści się nie zgadza` albo `USZKODZONA: zły JSON`. Agent nie wykonuje takiej wiadomości i zgłasza ją nadawcy oraz operatorowi.

## Testy [jest]

`ECXP-MAILBOX.exe /selftest <pusty katalog>` (uruchamiać z PowerShell) daje 20 przypadków, wszystkie PASS. Obejmują m.in.:
- zapis atomowy, odbiór i polskie znaki;
- izolację adresatów i ACK doklejony;
- idempotencję potwierdzeń, odmowę cudzego potwierdzenia i odrzucenie podszycia;
- wykrycie zmiany po wysłaniu i wykrycie zaginięcia;
- wykrycie usunięcia wpisu dziennika.
