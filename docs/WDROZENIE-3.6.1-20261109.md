# Wdrożenie ECXP-MAILBOX 3.6.1.0 — 2026-11-09

Wdrożono obsługę odmów dostawców bez ponawiania i fałszywych błędów startu
zespołu. Szczegóły implementacji i przypadków regresji opisuje
[raport poprawek](POPRAWKI-ODMOWY-START-20261109.md).

## Pakiet i instalacja

- Instalator: `builds/release-3.6.1/narzedzia/ECXP-Setup.exe`.
- SHA-256: `3A959000748C32909ED990FB07F6A5DAA4FC64C10F6DC2AA446CE1AE65063A55`.
- GUI i instalator mają wersję **3.6.1.0**. Numery zestawów stosu i MCP
  pozostają odpowiednio 1.1.0.0 i 1.2.0.0; nie są numerem całego wydania.
- Kompilacja całego pakietu: PASS, tylko ostrzeżenia CS0649.
- Zasoby instalatora: **7/7** identycznych sum z plikami wydania.
- Kopia poprzedniej instalacji i konfiguracji:
  `builds/backup-before-3.6.1/` (pominięto wyłącznie chwilowy plik blokady CLI).
- Reinstalacja do `C:\Users\user\AppData\Local\ECXP`: kod zakończenia **0**.
  Porównanie SHA-256 zainstalowanych GUI, stosu, MCP i instalatora: **4/4 PASS**.
- Końcowa instalacja zgłosiła brak dostępu przy ponownym zapisie ustawień
  hooków Claude. Odczyt potwierdził, że istnieją już prawidłowe polecenia
  `ecxp-mcp.exe hook` dla `SessionStart`, `UserPromptSubmit` i `Stop`.
  Nie zmieniano uprawnień plików ani ochrony systemu.

## Testy i przegląd

- TestOdmow: **18 PASS**.
- TestStartuPentestu: **14 PASS**, w tym sukces po 65 sekundach, pojedynczy
  start, zerwane połączenie oraz natychmiastowy powrót odpytywania usług
  podczas startu. Ostatnie dwa sprawdzenia dodano po wykryciu w ręcznym teście,
  że okresowy odczyt usług czekał w tej samej sesji za długim startem.
- TestPentestu: **20 PASS**.
- Przegląd zmian i `git diff --check`: PASS. Trwałe ID odmowy zapisywane jest
  przed FIN; odmowa nie przechodzi do partnera ani do transportu zastępczego.
  Nowe ID pracy tego samego agenta pozostaje dozwolone.

## GUI i cały zespół w osobnym projekcie

Zainstalowane programy uruchomiono z innym katalogiem skrzynki:
`builds/gui-post-361-final/mailbox`, projekt **test-gui-361-final**.
Nie używano EvidLock ani innych istniejących projektów. PATH wyłącznie
instancji testowej wskazywał lokalne atrapy CLI; nie wysyłano żądań do
zewnętrznych dostawców modeli.

Z GUI wybrano kategorię **bezpieczeństwo / testy** i utworzono zespół.
Kontrola inicjalizacji Codexa była celowo opóźniona o 65 sekund. Stos
potwierdził start po ponad minucie, bez fałszywego błędu uruchomienia.
Czterej testerzy otrzymali po jednej syntetycznej odmowie. Po zebraniu ich
wyników uruchomiono obu recenzentów, również z jedną odmową na osobę:

| Rola | Codex | Claude |
|---|---|---|
| Tester 1 | 1 tura, odmowa, negatywny FIN | 1 tura, odmowa, negatywny FIN |
| Tester 2 | 1 tura, odmowa, negatywny FIN | 1 tura, odmowa, negatywny FIN |
| Recenzent | 1 tura, odmowa, negatywny FIN | 1 tura, odmowa, negatywny FIN |

Wynik: **6 żądań, 6 tur, 6 zakończeń NIEWYKONANE, 3 zamknięte obiegi,
0 przekazań partnerowi**. Liczba żądań pozostała równa 6 po upływie okresu
ponawiania i po ponownym uruchomieniu instancji testowej.
Jest to zaliczony test obsługi odmów, a nie potwierdzenie wykonania pentestu
ani pozytywna recenzja podatności przez modele.

Przed poprawieniem atrap dwie próby miały błędy fixture: opóźniono RPC
gotowości z osobnym limitem 30 sekund, a następnie zabrakło odpowiedzi
`data: []` na `thread/loaded/list`. Ich wyników nie zaliczono jako testu
braku ponowień. Pełny wynik wyżej pochodzi z nowej, pustej skrzynki.

Po dołożeniu ochrony odpytywania usług ponownie zbudowano i zainstalowano
ostateczny pakiet. W jego GUI otwarto projekt test-gui-361-final, sprawdzono
połączenie ze stosem oraz zakładkę Agenci: dwie pary testerów i para
recenzentów. Zachowane negatywne wyniki były widoczne w rozmowach.
Instancja testowa została następnie zamknięta; zwykła instancja pozostaje
uruchomiona. Artefakty robocze i kopie pozostają lokalnie poza Gitem.
