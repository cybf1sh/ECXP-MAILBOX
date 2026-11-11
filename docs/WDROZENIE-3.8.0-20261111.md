# Mailbox 3.8.0 — 11 listopada 2026

Polecenie operatora: dodać do klienta agenta-hook, który sprawdza, czy zadanie
lub zapytanie jest wykonywane i na jakim jest etapie (uruchamiany w oknie
Agenci, czas sprawdzania suwakiem 10 s – 1 min, domyślnie 1 min), oraz pokazać
przy wysłanych wiadomościach w oknie głównym (TYTUŁ) aktualny stan: czy dotarła
i kto odebrał. Decyzja operatora: strażnik czyta stan lokalnie **i** pyta agenta.

## Strażnik zadań (zakładka Agenci)

Panel **🛡 Strażnik zadań** pod listą par (także Ustawienia → „🛡 Strażnik
zadań…”):

- **▶ Uruchom strażnika / ■ Zatrzymaj strażnika** — strażnik to hook konsoli
  (timer w ECXP-MAILBOX, bez modelu AI i bez tokenów). Domyślnie wyłączony.
- **Sprawdzaj co:** suwak od 10 s do 1 min, domyślnie 1 min.
- **Pytaj agenta o etap po N min bez zmian** (domyślnie 10 min, włączone) —
  gdy zadanie w obiegu stoi bez zmiany etapu, strażnik prosi stos o pytanie
  sesji agenta. Najwyżej raz na N minut na zadanie; stos dodatkowo blokuje
  ponowne pytanie przez 60 s.
- **Zapytaj agenta teraz** — to samo pytanie dla zaznaczonego zadania.
- **Powiadomienia** — dymek przy zmianie etapu.
- Lista: wysłano, do kogo, temat, etap, ostatnia zmiana, ostatnie pytanie
  o etap. Dwuklik otwiera wiadomość.

Śledzone są zadania i zapytania operatora z ostatnich 7 dni, które jeszcze
czekają na wynik (każda kopia pary osobno). Ustawienia zapisują się w
`narzedzia\ustawienia-wlasciciel.txt` (`straznik=włączony|sekundy|pytaj|minuty|dymki`).

### Pytanie agenta o etap (protokół)

- Nowe polecenie operatora w stosie: `zapytaj_o_etap` (`id` zadania).
- Sesja w aplikacji Codex/Claude (z MCP) dostaje od `ecxp-straznik`
  informację o temacie `STAN-ZADANIA-<temat>` z dokładnym wywołaniem:
  `ecxp_wyslij(do=wlasciciel, typ=w_trakcie, odpowiedz_na=<id zadania>, tresc=<etap>)`.
- `w_trakcie` z treścią zapisuje się w obiegu jako **etap podany przez agenta**
  (zdarzenie `obieg_etap` w dzienniku, do 400 znaków). To nie jest FIN —
  zadanie zostaje otwarte. `w_trakcie` po FIN jest odrzucany jak dotąd.
- Pytanie nie jest dołączane do obiegu zadania, więc nawet zwykła odpowiedź
  agenta na samo pytanie nie może zamknąć zadania.
- Agent w tle (bez MCP) nie dostaje pytania — informacje od strażnika nie
  uruchamiają jego tury. Jego stan widać bez pytania: stos zapisuje
  początek tury (`tura_od`, `tura_praca` w `stan\agenci\<nazwa>.json`), a
  konsola pokazuje „agent w tle pracuje od …”.
- Instrukcje serwera MCP dla agentów opisują ten wyjątek od zasady „bez
  meldunków pośrednich”.

## Stan wysłanych na liście TYTUŁ

Każda rozmowa z Twoją wysłaną wiadomością ma dodatkową linię
**„▸ Twoja wiadomość: …”** w kolorze etapu, np.:

- `w kolejce, czeka na odbiór`
- `odebrane przez Codex · moduł po 12 s · w trakcie, czeka na FIN · agent: „Etap 2 z 3: piszę testy” (3 min temu)`
- `odebrana przez Claude · moduł po 4 s · czeka na odpowiedź`
- `FIN po 7 min 10 s`, `NIEDORĘCZONA – alarm`, `wycofana przez Ciebie`

Ten sam opis (z „kto odebrał” i etapem od agenta) pokazują podgląd
wiadomości i okno „📡 Śledzenie wysłanych…”.

## Wersje

ECXP-MAILBOX i instalator 3.8.0, `ecxpd` 1.2, `ecxp-mcp` 1.3.
Pakiet: `builds/release-3.8.0/narzedzia/` (poza Git).

## Testy (pakiet 3.8.0, 11.11.2026)

| Test | Wynik |
|---|---|
| `TestStraznika` (nowy: pytanie, etap, brak pomyłki z FIN, agent w tle) | 21 PASS |
| `TestyRozmow` (GUI, w tym 6 nowych asercji strażnika i listy TYTUŁ) | 50 PASS |
| `TestySesji` | 17 PASS |
| `ECXP-MAILBOX.exe /selftest` | 21 PASS |
| `TestDoreczania`, `TestPentestu`, `TestStartuPentestu` | PASS (jak 3.7.0) |
| `TestOpcjiPentestu`, `TestUsuwaniaProjektu`, `TestNapiszOdpowiedz`, `TestOdmow`, `TestOdpowiedziOperatora` | FAIL **identycznie w 3.7.0 i 3.8.0** — na tym stanowisku uruchamiany z testów `codex app-server` nie odpowiada na `initialize` w czasie testu (ręcznie odpowiada) |

Nie sprawdzono na żywo: odpowiedzi prawdziwej sesji Codex/Claude na pytanie
STAN-ZADANIA (sprawdzona jest ścieżka stosu i MCP na danych syntetycznych).
