# Mailbox 3.12.0 / protokół ECXP 1.1 — 13 listopada 2026

Polecenie operatora: wprowadzić siedem zmian w protokole (najpierw 1 i 2, potem
pozostałe), naprawić puste pole tokenów, dać jeden kolor wiadomościom wysłanym
i odebranym w tym samym wątku oraz dodać opcję układania wiadomości i zadań
wątku w jedną główną gałąź.

Wszystkie zmiany protokołu są zgodne wstecz: nagłówek `ecxp/1` zostaje, dochodzą
pola i polecenia. Starsze wiadomości i starsze pakiety agentów działają jak dotąd.

## Protokół 1.1

1. **Wynik FIN jako pole.** `ecxp_wyslij(typ=fin, wynik=pozytywny|negatywny|niewykonane, …)`.
   - pozytywny – wykonane, wynik dobry; negatywny – wykonane, wynik zły;
     niewykonane – zadania nie wykonano. Stos zapisuje też `odmowa` (odmowa
     dostawcy modelu) i `nieokreslony` (agent w tle nie podał wyniku).
   - FIN sesji bez wyniku jest odrzucany z instrukcją. Zgodność: „POZYTYWNY / NEGATYWNY”
     na początku treści nadal jest przyjmowane.
   - Agent w tle podaje wynik pierwszą linią odpowiedzi („WYNIK: …”), stos ją odczytuje i usuwa.
   - Wspólny FIN ma wynik łączny (wszyscy pozytywnie → pozytywny; ktokolwiek
     negatywnie → negatywny; inaczej niewykonane / nieokreślony).
   - Odmowa dostawcy to teraz „NIEWYKONANE: odmowa dostawcy modelu”, bez słowa NEGATYWNY.
2. **Jeden identyfikator wiadomości.** Każda kopia wiadomości do pary niesie pole
   `wiadomosc` (id kopii dla pierwszego adresata według nazwy). Odpowiedź zapisuje
   `odpowiedz_na_wiadomosc`; w `odpowiedz_na` można podać ten identyfikator zamiast
   id własnej kopii. Doręczenie nadal jest osobne dla każdego adresata.
3. **Wątek z identyfikatorem i tytułem.** Rozmowa to pole `watek` pierwszej
   wiadomości, nie „ten sam temat” — dwa zadania o tym samym temacie są osobnymi
   rozmowami. Pole `tytul` zachowuje oryginalny tytuł (z polskimi znakami); `temat`
   zostaje skrótem. Wiadomości starego formatu i wpisy z czatu łączą się po temacie jak dotąd.
4. **Postęp jako typ.** `typ=postep` (opcjonalnie `krok`, `z_krokow`) zapisuje etap
   zadania i nigdy go nie zamyka; `w_trakcie` działa jak dotąd. Pytanie strażnika
   STAN-ZADANIA prosi teraz o `typ=postep`.
5. **Możliwości stosu.** Powitanie i `ping` podają `stos` („ecxpd 1.6” z wersji
   pliku, dotąd na stałe „ecxpd 1.0”), `wersja_protokolu`, listę `polecenia`,
   `typy` i `wyniki_fin`. Konsola przed wywołaniem sprawdza, czy stos zna polecenie.
6. **Przywracanie.** Polecenie `przywroc` przenosi wiadomość z archiwum albo kosza
   do bieżących (zdarzenie `przywrocona`); nic nie jest wysyłane ponownie.
7. **Obecność adresata.** Polecenie `obecnosc`: rodzaj (`sesja` / `tlo`), ostatni
   kontakt, czy adresat odbierze wiadomość, opis. Agent w tle zgłasza obecność
   turami. Ostrzeżenie przy wysyłce korzysta z tego jednego zapisu.

## Konsola

- **Wynik przy FIN:** ✓ POZYTYWNY (zielony), ✗ NEGATYWNY (czerwony), ⊘ NIEWYKONANE
  i ⛔ odmowa dostawcy (pomarańczowy) — na liście, w podglądzie, czytniku, schowku i śledzeniu.
- **Jeden kolor na wątek:** kody, linie „↩ odpowiedź na…” i plakietki wszystkich
  wiadomości rozmowy — wysłanych i odebranych — mają kolor wątku.
- **Gałęzie wątków** (pole wyboru pod drzewkiem folderów): wątek jest nagłówkiem-gałęzią,
  pod nim wcięte, połączone kreską w kolorze wątku wszystkie jego wiadomości
  i zadania. Klik w nagłówek zwija / rozwija gałąź. Ustawienie jest zapamiętywane.
- **Folder 🗑 Kosz** i pozycja menu **↺ Przywróć do bieżących** w folderach
  Zarchiwizowane i Kosz.
- **Oryginalne tytuły** rozmów (np. „Stan testów – żółć”) zamiast skrótów wielkimi literami.
- **Tokeny zadania:** pole było puste dla agentów w tle, bo zapis tury powstaje tuż
  przed FIN, a konsola szukała tury zakończonej po nim. Naprawione; dodatkowo
  agenci Codex w tle zapisują zużycie z zapisu swojego wątku.

## Wersje

ECXP-MAILBOX i instalator 3.12.0, `ecxpd` 1.6, `ecxp-mcp` 1.5.
Pakiet: `builds/release-3.12.0/narzedzia/` (poza Git).

## Testy (13.11.2026)

| Test | Wynik |
|---|---|
| `TestProtokolu` (nowy: punkty 1–7) | 36 PASS |
| `TestyRozmow` (GUI: +8 tytuł, gałęzie, kolor wątku, Kosz) | 78 PASS |
| `TestStraznika`, `TestDoreczania`, `TestPentestu`, `TestStartuPentestu`, `TestAktywnosci` | 21 / 24 / 21 / 14 / 23 PASS |
| `ECXP-MAILBOX.exe /selftest` | PASS |

Nie sprawdzono na żywo: FIN z polem `wynik` i `typ=postep` z prawdziwej sesji
Codex / Claude (sesje dostaną nowe instrukcje po ponownym uruchomieniu aplikacji)
oraz zapisu tokenów agenta Codex w tle.
