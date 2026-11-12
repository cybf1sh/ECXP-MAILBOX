# Mailbox 3.9.0 — 12 listopada 2026

Polecenie operatora: menu prawego klawisza przy każdej wiadomości —
w Niewysłanych „ponów, usuń”, przy odebranych „archiwizuj, odpowiedz, usuń”
(także w podglądzie odebranej wiadomości).

## Menu prawego klawisza

| Miejsce | Pozycje |
|---|---|
| **Niewysłane** | ↻ Ponów wysyłkę… · 🗑 Usuń… |
| **Rozmowa odebrana** (lista Wszystkie / Odebrane) | ↩ Odpowiedz… · 🗄 Archiwizuj · 🗑 Usuń… na górze, niżej dotychczasowe pozycje |
| **Rozmowa wysłana** (lista Wszystkie / Wysłane) | dotychczasowe pozycje (Wyślij ponownie, Obudź, Zamknij wątek…), na końcu 🗑 Usuń… |
| **Podgląd wiadomości** | 📋 Kopiuj oraz akcje pokazanej rozmowy (odebrana: Odpowiedz / Archiwizuj / Usuń; wysłana: Wyślij ponownie / Usuń; Niewysłane: Ponów / Usuń) |

- **Ponów wysyłkę** otwiera formularz wypełniony szkicem albo niedoręczoną
  wiadomością — sprawdzasz i klikasz Wyślij; nic nie odchodzi samo.
- **Archiwizuj** przenosi rozmowę do `archiwum\RRRR-MM` (nic nie jest kasowane;
  rozmowa znika z listy).
- **Usuń** (także klawisz Delete) zawsze pyta o potwierdzenie. Wiadomości nie są
  kasowane trwale: stos przenosi pliki do `kosz\RRRR-MM` — znikają z konsoli
  i z archiwum, ale można je odzyskać ręcznie. Niedoręczone nie trafią już do
  adresatów, strażnik przestaje o nie upominać. Wyjątek: lokalny szkic, który
  nigdy nie został wysłany, jest usuwany trwale (okno potwierdzenia to mówi).

## Stos

Nowe polecenie operatora `usun` (`pliki`, `powod`) — jak `wycofaj`, ale celem
jest `kosz\RRRR-MM`; w dzienniku zdarzenie `usunieta`.

## Wersje

ECXP-MAILBOX i instalator 3.9.0, `ecxpd` 1.3, `ecxp-mcp` 1.3 (bez zmian).
Pakiet: `builds/release-3.9.0/narzedzia/` (poza Git).

## Testy (12.11.2026)

| Test | Wynik |
|---|---|
| `TestyRozmow` (GUI, w tym 4 nowe asercje menu i kosza) | 54 PASS |
| `TestStraznika`, `TestDoreczania`, `TestPentestu`, `TestStartuPentestu` | 21 / 24 / 21 / 14 PASS |
| `ECXP-MAILBOX.exe /selftest` | 22 PASS |
