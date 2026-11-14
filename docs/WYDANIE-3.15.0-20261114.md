# ECXP-MAILBOX 3.15.0 — ustawienia zaawansowane

Konsola i instalator 3.15.0, stos ecxpd 1.9, adapter MCP 1.7. Format ramek pozostaje ECXP 1.1.

## Gdzie są nowe opcje

Przycisk **Ustawienia… → Zaawansowane** w górnej części głównego okna:

| Pozycja | Działanie |
|---|---|
| Lokalizacje plików i komunikacji | Folder dokumentacji komunikacji, Odebrane, Wysłane i plik stanu modułów; wybór dysku i katalogu |
| Komunikacja sesji — otwórz katalog | Otwiera wybrany folder; zakłada brakujące README i historię ustaleń bez nadpisywania istniejących plików |
| Gdzie znajdują się pliki ECXP? | Rozwijane drzewo plików instalacji, skrzynki, komunikacji oraz konfiguracji MCP; otwieranie lokalizacji i kopiowanie ścieżki |
| Przenieś / importuj skrzynkę | Osobny kreator kopiowania kompletnej skrzynki do pustego folderu, opcjonalnie z przełączeniem lokalizacji |
| Ustawienia protokołu | Terminy potwierdzeń pilnych, koordynacji i zwykłych wiadomości oraz czas łączenia; przywracanie wartości domyślnych |
| Konfiguracja MCP — Codex / Claude | Edytor plików użytkownika i ręcznie wskazanego pliku projektu, z kopią i kontrolą równoległych zmian |

**Agenci → Nadaj role / przypisz…** zawiera własną rolę: nazwę, obowiązki, oczekiwany wynik i granice. Własne role są dostępne przy kolejnych przypisaniach i w trasach ECXP. Zachowano i dokończono istniejące zmiany Claude.

Zakładka **Stan modułów** wyświetla ręcznie prowadzony plik, domyślnie `STAN-MODULOW.md` w skrzynce. Wyjaśnia brak pliku, usuwa nieaktualną treść po jego zniknięciu i nie pokazuje wtedy etykiety „zmieniony”. Nie jest automatycznym raportem stanu agentów.

## Komunikacja i konfiguracja

Ustawienia są w `<skrzynka>/stan/konfiguracja.json`. Katalog komunikacji przechowuje ustalenia i przekazania sesji, a nie oryginały wiadomości protokołu. Wybór katalogu nie przenosi ani nie wznawia automatyzacji. Nowe pakiety startowe sesji zawierają wskazanie tego katalogu. Istniejących czatów ani ich instrukcji aplikacji nie modyfikuje się automatycznie.

Przy braku ustawienia program rozpoznaje istniejący `komunikacja-sesji` w repozytoriach zarejestrowanych projektów, jeśli znaleziono dokładnie jeden taki katalog. W pozostałych przypadkach proponuje podkatalog skrzynki. Operator może wskazać inny folder. Przycisk kopiowania ustaleń kopiuje pliki bezpośrednio w folderze źródłowym i odmawia nadpisania pliku w celu.

Domyślne terminy pozostają 15 / 30 / 240 minut, a czas łączenia 45 sekund. Terminy dotyczą nowych wiadomości. Nie wyłączają integralności, ACK ani kontroli uprawnień.

Edytor MCP obsługuje JSON i TOML. JSON musi być obiektem; TOML sprawdza zainstalowany Codex CLI w tymczasowym, odizolowanym `CODEX_HOME`, zanim plik zostanie zapisany. Bez CLI zapis TOML jest odrzucany z wyjaśnieniem. Wspierany jest niestandardowy `CODEX_HOME` przy wyborze konfiguracji do edycji. Po zmianach należy ponownie uruchomić odpowiedniego klienta MCP. Informacje o konfiguracji Codexa: [oficjalna dokumentacja MCP](https://learn.chatgpt.com/docs/extend/mcp?surface=cli).

## Migracja i import

Kreator otwiera się po potwierdzeniu zamknięcia konsoli i stosu. Trwająca tura agenta w tle blokuje tę operację. Czaty użytkownika pozostają otwarte; klientów MCP trzeba uruchomić ponownie po przełączeniu. Kreator można uruchomić również poleceniem `ECXP-MAILBOX.exe /migracja /root "pełna ścieżka skrzynki"`.

Kopia obejmuje pliki, puste katalogi, dziennik, wiadomości, kontekst, konfigurację i role. Cel musi być pusty i rozłączny ze źródłem; działający stos lub konsola blokuje kopiowanie. Dowiązania są odrzucane. Każdy skopiowany plik jest sprawdzany SHA-256. Źródło pozostaje zachowane. W razie błędu częściowa kopia w celu pozostaje do sprawdzenia; źródło nie jest wtedy przełączane. Nie ma scalania dwóch istniejących dzienników ani importu dowolnego formatu pocztowego.

Przełączenie zapisuje w źródle `ecxp-przeniesiono.json`. Kolejne uruchomienia konsoli, stosu i adaptera podążają za przekierowaniem; nie trzeba ręcznie zmieniać dotychczasowych argumentów `/root` lub `--root`. Ustawione ścieżki komunikacji, widoków i stanu modułów wewnątrz źródła są przeliczane na cel. Zewnętrzne katalogi i katalogi robocze agentów pozostają niezależne. Kopiowanie nie zmienia rejestracji instalacji Windows.

Foldery Odebrane/Wysłane są czytelnymi widokami, nie kolejkami protokołu. Po zmianie ich położenia stos odtwarza widok. Stare kopie są zachowane również po migracji.

## Pakiet na inne stanowiska Windows

Samodzielny `ECXP-Setup.exe` zawiera konsolę, stos, adapter MCP, hook Claude, ikonę i dwa pliki logo. Nie wymaga katalogu źródeł ani układu dysków maszyny programisty. Domyślna instalacja jest w profilu bieżącego użytkownika; kreator pozwala wskazać inny katalog. Wymagane są Windows z .NET Framework 4.x oraz odpowiednie aplikacje/CLI Codex i Claude do używania tych integracji. Brak CLI nie jest zastępowany konfiguracją z maszyny autora.

Poprawiono także wersję zapisywaną w metadanych instalacji Windows: 3.15.0 zamiast starej stałej 3.7.0.

Lokalny pakiet: `builds/release-3.15.0/`, sumy `sha256.json`, kontrola zasobów `installer-resources.json`. Programy i instalator są lokalnymi artefaktami, zgodnie z regułami repozytorium nie trafiają do Git.

## Weryfikacja

| Zestaw | PASS |
|---|---:|
| TestKonfiguracji: zapis przez okna, kopie, konflikty, walidacja ścieżek i migracja | 22 |
| TestNiezawodnosci, w tym własne role | 55 |
| TestProtokolu | 39 |
| TestyRozmow: menu Zaawansowane, własna rola, stan modułów, PDF i istniejące funkcje | 110 |
| TestDoreczania | 24 |
| TestStraznika | 21 |
| TestAktywnosci | 24 |
| TestKatalogu | 32 |
| TestPentestu | 21 |
| TestStartuPentestu | 14 |
| TestMigracji: rzeczywisty stos, odmowy uprawnień, migracja, integralność, TOML | 11 |

Łącznie 373 kontrole PASS, dodatkowo selftest konsoli PASS. Test migracji uruchomiono z niezależnego pakietu w katalogu ze spacjami i polskimi znakami. Obejrzano obrazy nowych okien oraz menu. Sprawdzono zgodność 7/7 zasobów instalatora z plikami wydania. Kompilator nadal zgłasza istniejące ostrzeżenia o nieprzypisywanych polach oraz ukrywaniu `Tag`.

Nie wykonano instalacji na odrębnym, czystym komputerze Windows ani migracji produkcyjnej skrzynki. Nie zmieniono aktywnej konfiguracji MCP użytkownika.
