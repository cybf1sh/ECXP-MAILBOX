# ECXP – dokumentacja techniczna

**ECXP (Evidsec Context eXchange Protocol)** to protokół komunikacji między agentami modeli językowych, ChatGPT/Codex i Claude, oraz człowiekiem-operatorem. Ma cztery cechy:
- niskopoziomowy, z własną ramką binarną i własnym stosem;
- połączeniowy, z uzgadnianiem i stanami;
- niezawodny, jak TCP;
- uniwersalny: projekt i moduł są parametrami.

Nadrzędna zasada to **1:1**. W projekcie i module pracuje para, Codex i Claude, z tożsamymi zadaniami, tożsamym wejściem od operatora i jedną wspólną gałęzią.

| Dokument | Zawartość |
|---|---|
| [Wydanie 3.14.0](WDROZENIE-3.14.0-20261113.md) | katalog agentów, wybór, wyłączanie i konfiguracja agentów (plikiem albo wiadomością), czytelny strażnik |
| [Wydanie 3.13.0](WDROZENIE-3.13.0-20261113.md) | niezawodność i szybkość: indeks, stan doręczeń, powtórki, dokańczanie, dzierżawa tury |
| [Wydanie 3.12.0](WDROZENIE-3.12.0-20261113.md) | protokół ECXP 1.1 (7 zmian), kolor wątku, gałęzie wątków, Kosz, tokeny |
| [Wydanie 3.11.0](WDROZENIE-3.11.0-20261113.md) | znaczniki wiadomości (kod, wątek do…, odpowiedź na…), drzewko folderów z archiwum |
| [Wydanie 3.10.0](WDROZENIE-3.10.0-20261112.md) | okno „Co robi agent”, spis gałęzi na bieżąco, poprawki strażnika po teście na żywo |
| [Wydanie 3.9.0](WDROZENIE-3.9.0-20261112.md) | menu prawego klawisza: ponów/usuń, odpowiedz/archiwizuj/usuń, kosz |
| [Wydanie 3.8.0](WDROZENIE-3.8.0-20261111.md) | strażnik zadań (etap zadań, pytanie agenta), stan wysłanych na liście TYTUŁ |
| [Wydanie 3.7.0](WDROZENIE-3.7.0-20261110.md) | działające opcje zespołu bezpieczeństwa, usuwanie projektu, testy i instalator |
| [01-architektura.md](01-architektura.md) | warstwy, procesy, potok, przepływ danych |
| [02-ramka.md](02-ramka.md) | format ramki binarnej bajt po bajcie, typy, flagi, skrót, przykład |
| [03-polaczenia.md](03-polaczenia.md) | uzgadnianie trójstronne, maszyna stanów, zapytania, wypychanie, zamykanie |
| [04-niezawodnosc.md](04-niezawodnosc.md) | seq, ACK, ponowienia, eskalacja, zapis atomowy, dziennik łańcuchowy, strażnik |
| [05-adresowanie-i-para-1-1.md](05-adresowanie-i-para-1-1.md) | adresy, tablica tras, przejęcie sesji, lustro i kopie dla pary, wspólna gałąź |
| [06-kontekst-i-zadania.md](06-kontekst-i-zadania.md) | części wiadomości, obiekty SHA-256, pakiet przekazania, zadania, role, karty |
| [07-mcp-i-konsola.md](07-mcp-i-konsola.md) | adapter MCP (13 narzędzi) i konsola operatora ECXP-MAILBOX |
| [08-bezpieczenstwo.md](08-bezpieczenstwo.md) | model zagrożeń, uwierzytelnienie operatora, granice |

Specyfikacja ogólna, czyli wytyczne i decyzje właściciela, jest w [../SPECYFIKACJA-ECXP.md](../SPECYFIKACJA-ECXP.md).

## Stan wykonania

Oznaczenia w dokumentach: **[jest]** zaimplementowane w kodzie, **[w budowie]** kod jest, trwają testy, **[plan]** tylko projekt.

| Część | Stan |
|---|---|
| Ramka binarna 72 B + SHA-256, potok nazwany z ACL | w budowie |
| Uzgadnianie klient↔stos (HELLO/WITAJ/ACK), ZAPYTANIE/ODPOWIEDZ, wypychanie DANE, PING, FIN, RST | w budowie |
| Rozpoznanie operatora po obrazie procesu klienta | w budowie |
| Kolejki trwałe, zapis atomowy, dziennik łańcuchowy SHA-256 | jest (testy PASS) |
| Seq w parze nadawca→adresat, ACK (jawny i doklejony), ponowienia, eskalacja, wykrywanie zaginięć | jest (testy PASS) |
| Adresowanie (`para:`, `codex/`, `claude/`, `rola:`), tablica tras, przejęcie sesji | w budowie |
| Zasada 1:1: lustro zadań, kopie dla pary (operator ↔ strona pary) | w budowie |
| Części, obiekty kontekstu, zadania, role, karty, nowe zadanie z parą | w budowie |
| Przechwycenie wejścia operatora z okien czatu (hook Claude, odczyt zapisu wątku Codexa) | plan |
| Podpis operatora (DPAPI), ramka DECYZJA | plan |
| Umowa wspólnej gałęzi, STAN_GALEZI, wykrywanie rozbieżności HEAD | plan |
| SACK, okno sterowania przepływem między agentami, RTO wykładnicze | plan |
| Brama A2A (tylko LAN) | plan |
- [09 – Decyzje, łańcuchy ról, wspólna gałąź, lustro czatów, agenci w tle, instalacja](09-decyzje-lancuchy-galezie-lustro-agenci.md)
