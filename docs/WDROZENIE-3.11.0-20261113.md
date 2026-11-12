# Mailbox 3.11.0 — 13 listopada 2026

Polecenia operatora: znaczniki, które jednoznacznie wskazują, na które zadanie
albo wiadomość jest odpowiedź (wybrany wariant A: krótki kod, kolorowy, ze słowem
„wątek do zadania…” w tytule), oraz przebudowa okna rozmów na wzór programu
pocztowego: drzewko z folderami Odebrane, Wysłane, Niewysłane, Zarchiwizowane.

## Znaczniki wiadomości

- Każda wiadomość ma **kod** `#c73f` — cztery ostatnie znaki identyfikatora, te
  same co na końcu nazw plików w folderach `Odebrane` / `Wysłane`. Kod ma stały
  kolor (ten sam kod = ten sam kolor w liście, podglądzie i czytniku).
- **Lista rozmów:** kolorowa plakietka przed tytułem — „Wątek do zadania #c73f”
  (albo pytania / informacji / blokady / odpowiedzi / wiadomości), a przy odebranej
  odpowiedzi linia „↩ odpowiedź na zadanie #c73f z 12.11 01:27”. Stan Twojej
  wiadomości: „▸ Twoja wiadomość #c73f: …”.
- **Podgląd rozmowy:** kod przy każdej wiadomości; pod odpowiedzią „↩ odpowiedź na
  zadanie #c73f · „TEMAT” · wysłane przez Ciebie 12.11.2026 01:27” w kolorze kodu
  tamtej wiadomości; pod wiadomością „↪ odpowiedzi na tę wiadomość: #8a21 Claude ·
  ikony (FIN, 01:28)”.
- **Czytnik i schowek:** plakietki z kodem i „odpowiedź na…”, w tekście kopiowanym
  linie „Znacznik: #8a21” i „↩ odpowiedź na …”.
- Wiadomość wysłana do pary (dwie kopie) ma jeden kod; odpowiedź na wiadomość
  z archiwum też jest rozpoznawana („· w archiwum”).
- Pliki widoku `Odebrane` / `Wysłane` (stos): linia „Znacznik:” i kod w „Odpowiedź na:”.

## Drzewko folderów (układ jak w programie pocztowym)

Zakładka Rozmowy: po lewej **FOLDERY**, w środku lista, po prawej podgląd.

- 📬 Wszystkie · 📥 Odebrane · 📤 Wysłane · 📝 Niewysłane · 🗄 Zarchiwizowane —
  z licznikami („Odebrane (12, nowe: 3)”; folder z nowymi pogrubiony).
- Gdy rozmowy są z kilku projektów, foldery mają **podfoldery projektów**.
- **Zarchiwizowane** pokazuje rozmowy z `archiwum\RRRR-MM` (najnowsze 400), tylko do
  odczytu: bez archiwizowania, usuwania i zamykania; zostają Otwórz w oknie,
  Pokaż w folderze i Odpowiedz. Archiwum jest czytane dopiero po wejściu do folderu.
- Pod drzewkiem dotychczasowe filtry (Wymaga uwagi, Widok, Moduły, Szukaj).
- Wąskie okno (poniżej 1100 px) albo przycisk „Foldery i filtry”: zamiast drzewka
  pasek przycisków folderów nad listą, jak dotąd.

## Wersje

ECXP-MAILBOX i instalator 3.11.0, `ecxpd` 1.5 (widok folderów), `ecxp-mcp` 1.4.
Pakiet: `builds/release-3.11.0/narzedzia/` (poza Git).

## Testy (13.11.2026)

| Test | Wynik |
|---|---|
| `TestyRozmow` (GUI: +5 znaczniki, +8 drzewko i archiwum; 3 przebiegi) | 70 PASS |
| `TestStraznika`, `TestDoreczania`, `TestPentestu`, `TestStartuPentestu`, `TestAktywnosci` | 21 / 24 / 21 / 14 / 23 PASS |
| `ECXP-MAILBOX.exe /selftest` | PASS |
