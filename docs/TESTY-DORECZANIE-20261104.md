# Doręczanie — agent w tle nie połyka wiadomości sesji

Repozytorium `D:\Projekty\ecxp-mailbox`, gałąź `main`, baza `7b5a1e1`.
Autor: Claude (`claude-ecxp-mailbox`), na polecenie operatora „popraw sam”.
Recenzja: Codex (`codex-ecxp-mailbox`).

## Incydent 04.11.2026

O 21:17 zadanie „w tle” z konsoli założyło agenta w tle `claude-ecxp-mailbox`.
Pod tym samym adresem pracowała już sesja Claude w aplikacji. Pętla agentów
(`Agenci.Petla`) odbierała za tego agenta całą kolejkę adresu. Odbiór
potwierdza wiadomość (ACK), więc 7 wiadomości od Codexa i odpowiedź operatora
przepadły bez alarmu. Usługa doręczania pomijała ten adres, bo miał rekord
agenta w tle (nawet wyłączonego), więc nie budziła sesji ani nie alarmowała.

## Poprawka

- `Agenci.SesjeInteraktywne()` rozpoznaje adres z sesją w aplikacji. Jest to
  aktywna trasa z identyfikatorem sesji. Agenci w tle mają trasę `tlo:<nazwa>`.
- `Agenci.Uruchom` nie zakłada agenta w tle pod adresem sesji. Zadanie czeka
  w skrzynce sesji, a konsola pokazuje powód w „Nie uruchomiono”.
- `Agenci.KrokPetli` (wydzielony z `Petla` bez zmiany działania) pomija agenta
  w tle, gdy adres ma sesję. Chroni to też starszy rekord agenta.
- `Agenci.WymagaTury`: pytanie, zadanie, recenzja i blokada od innego agenta
  zostają pracą agenta w tle. Agent odpowiada nadawcy. Wcześniej przepadały po
  ACK. Informacje i odpowiedzi nadal kończą się na ACK.
- `Doreczanie` i nadzór obiegu (`Ecxpd`) liczą tylko agentów rzeczywiście
  obsługiwanych w tle (`Agenci.ObslugiwaniWTle`). Adres sesji i wyłączony agent
  podlegają budzeniu i alarmom.

## Weryfikacja — 2026-11-04

- `TestDoreczania.exe <pusty katalog>`: **18 PASS**, kod 0. Dane syntetyczne,
  bez uruchamiania tur agentów. Test woła prawdziwy krok pętli agentów.
- Kontrola mutacyjna: po usunięciu obu zabezpieczeń test daje **FAIL 6**
  (wiadomości połknięte). Test wykrywa więc błąd z incydentu.
- Paczka `builds\doreczanie-20261104\narzedzia` zbudowana według `buduj.cmd`,
  kod 0. Zasoby instalatora: **7/7 zgodnych SHA-256**.

| Plik | Wersja | SHA-256 |
|---|---|---|
| ECXP-Setup.exe | 1.2.0.0 | `254DCE8520902B4B6CA218237266DEBE00E8D7BAE9455BA24E62CEACFE4D8A4E` |
| ecxpd.exe | 1.0.0.0 | `E2151046A03DC6E96591F5C9208C15A84DA0CA03C19FB8E968C0C900E30B2F55` |
| ECXP-MAILBOX.exe | 3.2.0.0 | `50B89AC9DE2B0840358296A832AF9FC0AF04B6FBBECA210A63436054664FC7F6` |
| ecxp-mcp.exe | 1.1.0.0 | `BA9213127C17E873DDE28C0C17330817A21CACB4DF9443456BF4C8C4D3AC4FF0` |

## Ograniczenia

- Nie uruchamiano testów GUI klienta ani `/selftest`, żeby okna nie przejmowały
  pracy operatora.
- Paczki nie zainstalowano. Działająca instalacja ma klienta 3.2.1.0 spoza repo.
  Paczka z repo ma klienta 3.2.0.0, więc instalacja cofnęłaby wersję klienta.
- Obejście incydentu wykonano ręcznie: rekord `stan\agenci\claude-ecxp-mailbox.json`
  ma `aktywny=false`.
- Brak push, scalenia i USER_ACCEPTED.
