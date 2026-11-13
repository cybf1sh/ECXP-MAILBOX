# Wydanie 3.14.0 — katalog agentów, wybór i konfiguracja agentów przez operatora

Data: 2026-11-13 · konsola 3.14.0 · ecxpd 1.8 · gałąź `claude/katalog-agentow`

## Co się zmienia dla operatora

**Katalog agentów** (zakładka Agenci → „🗂 Katalog agentów…”, także z prawego menu)
- katalog roboczy agentów Codex i Claude jest ustawieniem konsoli (domyślnie `E:\Agents`, jeśli istnieje) – można go zmienić,
- dodawanie nowego agenta (folder, strona, nazwa, moduł, opcjonalnie wzór z istniejącego agenta),
- edycja plików agenta wprost z okna: instrukcja `AGENTS.md` (Codex) / `CLAUDE.md` (Claude), `scope.md`, pliki JSON, skrypty w `scripts\`; raporty tylko do odczytu,
- każda zmiana zapisuje poprzednią wersję w `<agent>\.kopie`,
- uruchomienie agenta z katalogu w ECXP.

**Konfiguracja agenta na dwa sposoby** (prawy klawisz na parze w zakładce Agenci)
1. „📝 Edytuj agenta Codex / Claude (pliki konfiguracji)…” – od razu otwiera pliki konfiguracyjne tego agenta z dysku.
2. „✉ Zmień konfigurację agentów wiadomością…” – operator opisuje zmianę zwykłym tekstem, wybiera jednego albo obu agentów pary;
   każdy dostaje zadanie naniesienia zmiany w swoich plikach i odpowiada FIN, co zmienił. Przed wysłaniem konsola robi kopię
   `AGENTS.md` / `CLAUDE.md` / `scope.md` w `.kopie`.

Ponadto „⚙ Skonfiguruj agenta…”: katalog pracy, uprawnienia, model, liczba tur na godzinę, limit czasu tury (polecenie stosu `ustaw_agenta`).

**Wyłączanie i zatrzymywanie agentów**
- „⏸ Wyłącz / ▶ Włącz agenta” – wyłączony agent nie dostaje zadań (adres pary go pomija, adres bezpośredni jest odrzucany), stos nie uruchamia jego tur i nie budzi sesji; nic nie jest kasowane,
- „■ Zatrzymaj teraz agenta” – przerywa trwającą turę i wyłącza agenta,
- lista pokazuje „⏸ WYŁĄCZONY”.

**Nowe zadanie / moduł**
- pola „Agent Codex” i „Agent Claude”: operator wybiera agenta z katalogu agentów albo „domyślny – folder projektu ECXP”,
- kategoria „bezpieczeństwo / testy” nie wymusza już stałego zespołu 6 specjalistów – jest to opcja do zaznaczenia; bez niej zadanie trafia do wybranych agentów.

**Strażnik zadań**
- nagłówek mówi wprost: WŁĄCZONY (co ile sprawdza, kiedy ostatnio) albo WYŁĄCZONY,
- po zaznaczeniu pary lista pokazuje tylko zadania jej agentów; klik w puste miejsce listy pokazuje wszystkie pary.

## Zmiany w stosie

- `wylacz_agenta` (z opcją `przerwij`), `ustaw_agenta` – oba w `P.PoleceniaStosu`,
- `stan\wylaczeni.json`, zdarzenia `agent_wylaczony`, `agent_wlaczony`, `agent_ustawienia`,
- `nowe_zadanie`: pola `katalog_codex`, `katalog_claude` (katalog pracy strony; folder projektu pozostaje katalogiem zapisu), `narzedzia_testow`.

## Testy

| Zestaw | Wynik |
|---|---|
| TestNiezawodnosci | 47 PASS |
| TestProtokolu | 39 PASS |
| TestKatalogu | 29 PASS |
| TestStraznika | 21 PASS |
| TestDoreczania | 24 PASS |
| TestPentestu | 21 PASS |
| TestStartuPentestu | 14 PASS |
| TestAktywnosci | 24 PASS |
| TestyRozmow (okno) | 92 PASS |
| selftest konsoli | PASS |

## Czego nie sprawdzono

- na żywych agentach: `nowe_zadanie` z agentem z katalogu, zmiana konfiguracji wiadomością (agent faktycznie edytuje plik i odsyła FIN), „Zatrzymaj teraz” na trwającej turze,
- prawe menu i panel strażnika nie były oglądane na zrzucie z prawdziwymi parami (dane testowe nie mają par) – sprawdzane testami okna po nazwach pozycji i tekstach.

## Ograniczenia

- zmiana konfiguracji wiadomością wymaga, by agent mógł zapisywać pliki (uprawnienia „edycja” lub „pelne”); okno ostrzega przy „odczyt”,
- agent pracujący jako sesja w aplikacji bez folderu w katalogu agentów nie ma plików do edycji – „Edytuj agenta” otwiera wtedy katalog agentów.
