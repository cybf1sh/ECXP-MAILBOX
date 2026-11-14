# Techniki i dowody dla wszystkich projektów

## Konfiguracja agentów i scripts

Zakładka **Konfiguracja i scripts** automatycznie odczytuje bieżące ustawienia agentów przypisanych do projektu w `stan/agenci`. Pokazuje wybrane pola operacyjne: model, stronę, moduł, katalog pracy, uprawnienia, limit uruchomień, aktywność i wymaganie odpowiedzi. Nie eksportuje historii sesji ani dodatkowych pól środowiska/sekretów z konfiguracji ECXP.

Z katalogów pracy odczytuje znane pliki profilu (`AGENTS.md`, `CLAUDE.md`, `agent-info.json`, `scope.md`, `ECXP-START.txt`, szablon zlecenia, manifesty narzędzi, `package.json`, lokalne ustawienia `.codex` i `.claude`) oraz tekstowy kod z `scripts` i podkatalogów. Profile z katalogu agentów aplikacji dołącza po dopasowaniu tożsamości do nadawcy, odbiorcy lub dosłownej wzmianki w wiadomościach. Wzmianka jest oznaczona jako powiązanie kontekstowe, nie dowód użycia. Ścieżki są pobierane z konfiguracji stanowiska, bez zaszywania konkretnego dysku.

Analiza statyczna pokazuje definicje, importy, opcje i wartości domyślne, warunki, obsługę błędów oraz rozpoznane techniki. Każdy fragment odsyła do pełnego kodu z numerami wierszy. Źródła mają SHA-256 i czas modyfikacji UTC. Instrukcje agenta są wyświetlane jako materiał źródłowy, skrypty nie są uruchamiane. Konfiguracja i kod nie są oceniane filtrem FIN; działa wspólne wyszukiwanie i sortowanie.

To aktualny odczyt, nie rekonstrukcja konfiguracji podczas historycznej sesji. Brak katalogu `scripts`, nieczytelny plik i przekroczenie limitu są jawne. Limity: 512 KB/pliki, 8 MB źródeł profili, 200 plików na profil i 10000 wpisów przeglądu. Pomijane są dowiązania, pliki binarne i katalogi zależności. Ekstrakcja obejmuje do 100 trafień z pierwszych 3000 wierszy; pełny odczyt pozostaje dostępny.

Na danych `big-bounty-cert` sprawdzono ustawienia ośmiu agentów ECXP i profile `codex-cyber` oraz `claude-cyber`, wspomniane w wiadomościach. Oba profile mają `scripts/scoped_http.py`. Wspólny katalog pracy agentów ECXP nie ma własnego `scripts`, co raport pokazuje wprost. Testy: 36 kontroli analizy, 39 interakcji raportu projektu oraz 38 raportu wszystkich projektów — bez błędów. Interakcje sprawdzono w jsdom, nie w nowej sesji Opery.

W Obserwatorium otwórz analizę wiadomości, następnie zakładkę **Techniki i dowody**. Zapisy można wyszukiwać, sortować i filtrować wynikiem FIN wspólnie z modelami i wiadomościami. Wynik FIN nadal dotyczy wiadomości lub zadania, a nie automatycznie każdej opisanej metody.

Każdy zapis zawiera osobne rozwijane pola:

- technika — rozpoznana etykieta i oryginalny cytat;
- narzędzie, w tym nazwy własnych skryptów i jawnie nazwanych narzędzi;
- parametry i polecenia, bez odtwarzania brakujących argumentów;
- fragment kodu lub logu (nie jest automatycznie poleceniem wykonanym przez agenta);
- jawny powód wyboru lub ograniczenie;
- zapisany wynik, kontrole, kontekst i wskazania dowodów.

Każde trafienie ma lokalizację: pole wiadomości lub plik, numer wiersza i odnośnik do pełnego źródła. Cytaty są zachowywane z negacjami. Plan, pytanie, negacja/przeszkoda i zbiorcza relacja mają odrębne oznaczenia. Nadawca zbiorczego FIN nie jest automatycznie uznawany za wykonawcę. Pozostały tekst pozostaje zapisem autora o niezweryfikowanym wykonaniu.

Reguły obejmują m.in. testy automatyczne, API, UI, analizę statyczną, zależności, logów, normalizację wejścia, przekierowania, limity, uwierzytelnianie i testy różnicowe. Działają niezależnie od nazwy projektu. Własne metody i narzędzia można opisać w zwykłej części tekstowej ECXP:

```text
Technika: porównanie kontraktu danych
Narzędzie: mój walidator
Parametry: separator=; tryb=offline
Polecenie: python walidator.py --fixture dane-testowe.csv
Uzasadnienie: wybrałem tę metodę, ponieważ pozwala odtworzyć błąd bez sieci.
Wynik: 4/4 PASS
Dowody: walidacja-log.txt
```

To przykład formatu, a nie dowód wykonania. Aby raport był szczegółowy, agenci muszą zapisywać faktyczne polecenia, parametry, wyniki i artefakty. Analizator nie uzupełnia braków domysłami i nie odczytuje ukrytego rozumowania.

## Lokalne artefakty

Wybierz jeden projekt, otwórz analizę i kliknij **Dołącz lokalne dowody do analizy projektu…**. Wskaż katalog projektu. W eksporcie HTML znajdą się kopie treści odczytanych plików TXT, MD, JSON, JSONL i LOG wskazanych nazwą w wiadomościach, częściach kontekstu lub polu `pliki`. Nie otwieramy URL-i i nie wykonujemy skryptów.

Pliki są dopasowywane wyłącznie po jednoznacznej nazwie w wybranym katalogu i podkatalogach; braki i niejednoznaczności są jawne. Pomijamy dowiązania/reparse points, `.git`, `node_modules` i `.venv`. Limit przeglądu wynosi 20000 wpisów, odczytu 512 KB na plik i 8 MB na raport. Niepełny przegląd nie pozwala uznać dopasowania za jednoznaczne. Raport wszystkich projektów nie używa jednego katalogu dowodów dla różnych projektów.

Każdy odczytany plik ma ścieżkę względną, SHA-256 odczytanej zawartości, numerowane wiersze i odnośniki z kart działań. Z plików wyodrębniamy również szczegóły: do 120 trafień z pierwszych 3000 wierszy, pomijając wiersze dłuższe niż 4000 znaków. Cała odczytana treść pozostaje dostępna. Aktualna kopia lokalna nie musi być identyczna z plikiem w chwili pracy agenta — SHA-256 identyfikuje kopię odczytaną teraz, nie dowodzi wykonania ani prawdziwości logu.

Raport może zawierać prywatne dane źródłowe. HTML jest samodzielny, działa bez sieci, a źródła są wyświetlane jako tekst. Stare eksporty nie aktualizują się automatycznie.

## Weryfikacja na skrzynce 2026-11-14

Po deduplikacji projekt `big-bounty-cert` zawierał 218 wiadomości w 38 wątkach. Ekstrakcja z samych wiadomości dała 900 trafień w polach (nie 900 wykonanych działań), w tym 24 zapisy parametrów/poleceń i 12 jawnych powodów lub ograniczeń. W wybranym katalogu projektu odczytano 43 jednoznacznie wskazane pliki. Nieobecne i niejednoznaczne odwołania pozostały oznaczone zamiast tworzenia pozornych dowodów.

Testy obejmują izolację projektów, własne metody przyszłych projektów, negacje, propozycje, zbiorczy FIN, parametry i numery wierszy, odczyt artefaktów z hashem, brakujące i niejednoznaczne pliki, kodowanie HTML oraz interaktywne filtry i odnośniki. Prywatne raporty testowe pozostają poza Git.
