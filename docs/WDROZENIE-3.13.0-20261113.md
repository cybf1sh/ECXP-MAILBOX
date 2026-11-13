# Mailbox 3.13.0 — niezawodność i szybkość protokołu — 13 listopada 2026

Polecenie operatora: wprowadzić wszystkie punkty z oceny „co poprawić w protokole, żeby
był niezawodny i szybki”, zaczynając od indeksu wiadomości i stanu doręczeń.

Format danych się nie zmienia: dochodzą pola `nr` (wiadomość) i `klucz` (żądanie
wysyłki). Starsze wiadomości i starsze pakiety agentów działają jak dotąd.

## Szybkość

| Operacja (skrzynka operatora: 460 wiadomości, 2501 wpisów dziennika) | Przed | Po |
|---|---|---|
| wyszukanie wiadomości po identyfikatorze (`Znajdz`) | 68 ms | 3,7 ms |
| wszystkie wiadomości z archiwum | 67 ms | 3,7 ms |
| stan doręczeń z dziennika (`Stany`) | 32 ms | 0,1 ms |

Pomiar: 20 powtórzeń, tylko odczyt, ten sam komputer. Pierwszy odczyt po starcie
procesu trwa jak dotąd (wtedy powstaje indeks).

1. **Indeks wiadomości w pamięci.** Plik wiadomości nie zmienia się po zapisie, więc
   raz odczytany zostaje w pamięci procesu (klucz: ścieżka, czas zapisu, rozmiar).
   Kolejne odczyty to lista plików folderu i kopie obiektów. Plik zmieniony na dysku
   jest czytany ponownie, a jego naruszenie wykrywane jak dotąd.
2. **Stan doręczeń przyrostowo.** Po pierwszym pełnym odczycie dziennika doczytywane są
   tylko nowe wpisy. Pełna kontrola łańcucha skrótów: przy starcie, co 5 minut oraz gdy
   plik zmalał albo zmienił się bez przyrostu. Ostatni skrót przy dopisywaniu zdarzenia
   jest brany z końcówki pliku, nie z całości.
3. **Powiadomienia zamiast odpytywania.** Stos już wcześniej wysyłał konsoli zdarzenie
   „zmiana”, a konsola obserwuje pliki; pełny przegląd „na wszelki wypadek” jest teraz
   co 30 s zamiast co 15 s.
4. **Krótsza blokada.** Budowa widoku folderów `Odebrane` / `Wysłane` (nawet kilkaset
   plików) trzyma globalną blokadę stosu tylko na czas odczytu stanu; zapis plików
   widoku dzieje się poza nią, więc wysyłka i odbiór na niego nie czekają.
   Blokady osobnej dla każdego projektu nie wprowadzono — jedna blokada zostaje
   gwarancją spójności dziennika.

## Niezawodność

1. **Identyfikator żądania.** `wyslij` przyjmuje pole `klucz`: ponowienie z tym samym
   kluczem (24 h) zwraca poprzedni wynik z `powtorzone: true` i nie tworzy drugiej
   wiadomości. Konsola wysyła klucz formularza. Dla agentów bez klucza: ta sama treść
   do tego samego adresata w ciągu 60 s jest traktowana jako powtórka. Błąd niczego
   nie zapamiętuje — po błędzie można ponowić.
2. **Dokańczanie przerwanych operacji.**
   - Wysyłka do pary zapisuje zamiar (`stan\zamiary`) przed pierwszą kopią. Gdy stos
     padnie między kopiami, po starcie dosyła brakującą kopię (z tym samym
     identyfikatorem wiadomości) i zakłada obieg zadania.
   - Obieg, w którym wszyscy skończyli, a wspólny FIN nie powstał, jest domykany: jeśli
     FIN już istnieje — tylko oznaczenie, inaczej FIN powstaje teraz. Bez duplikatów.
   - Sprawdzane przy każdym obiegu strażnika stosu; zdarzenia `zamiar_dokonczony`,
     `obieg_dokonczony` w dzienniku.
3. **Kolejność według numeru.** Każda wiadomość dostaje rosnący numer `nr` w całym
   stosie; wiadomości z tej samej chwili układają się według niego, nie losowo.
4. **Dzierżawa tury.** Trwająca tura agenta w tle co 15 s zapisuje puls. Bez pulsu
   przez 60 s konsola nie pokazuje „agent pracuje”. Po restarcie stosu znaczniki tur
   znikają. Limit tury: domyślnie 30 min, pole `limit_tury_min` (5–240) w zapisie agenta.
5. **Sprawdzanie pól.** `wyslij` odrzuca nieznane pole (z nazwą i listą dozwolonych)
   oraz zły typ (logiczne, listy, liczby, tekst) — od razu, z jasnym błędem.

## Wersje

ECXP-MAILBOX i instalator 3.13.0, `ecxpd` 1.7, `ecxp-mcp` 1.6.
Pakiet: `builds/release-3.13.0/narzedzia/` (poza Git).

## Testy (13.11.2026)

| Test | Wynik |
|---|---|
| `TestNiezawodnosci` (nowy: indeks, dziennik, powtórki, kolejność, pola, dzierżawa, dokańczanie) | 33 PASS |
| `TestProtokolu`, `TestStraznika`, `TestDoreczania`, `TestPentestu`, `TestStartuPentestu`, `TestAktywnosci` | 39 / 21 / 24 / 21 / 14 / 24 PASS |
| `TestyRozmow` (GUI, 2 przebiegi) | 78 PASS |
| `ECXP-MAILBOX.exe /selftest` | PASS |

Nie sprawdzono: prawdziwej awarii stosu w środku operacji (testy odtwarzają stan po
awarii na danych syntetycznych) oraz zachowania przy bardzo dużej skrzynce (dziesiątki
tysięcy wiadomości).
