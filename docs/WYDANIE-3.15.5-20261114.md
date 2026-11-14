# ECXP-MAILBOX 3.15.5 — warsztat grafów, techniki i źródła agentów

Obserwatorium rozszerza analizę dla wszystkich projektów. Wariant grafu można edytować jako niezależny plan: dodawać kroki, warunki, testy i pomysły na narzędzia, łączyć je, zapisywać lokalnie oraz przenosić plikiem JSON. Oryginalne wiadomości pozostają niezmienione.

Zakładka **Techniki i dowody** wydobywa nazwy metod, narzędzia, parametry, jawne uzasadnienia i wyniki wraz z cytatami oraz odnośnikami. Rozróżnia propozycje, negacje, deklaracje i zbiorcze relacje. Lokalnie wskazane artefakty można odczytać do raportu z SHA-256 i numerami wierszy. Brakujące i niejednoznaczne pliki są jawnie oznaczane.

Zakładka **Konfiguracja i scripts** odczytuje ustawienia agentów, powiązane profile i kod z katalogów `scripts`. Pokazuje definicje, opcje i warunki bez uruchamiania kodu. To bieżąca konfiguracja, nie rekonstrukcja historycznego środowiska. HTML zawiera interakcje i źródła lokalnie, bez CDN i usług AI.

Szczegóły obsługi: [warsztat grafów](WARSZTAT-GRAFOW.md) i [techniki oraz profile](OBSERWATORIUM-TECHNIKI.md).

## Weryfikacja wydania

- TestObserwatorium: 36 PASS.
- TestRaportu: 39 PASS dla raportu projektu i 38 PASS dla wszystkich projektów; TestWariantu: 9 PASS.
- Selftest zainstalowanego klienta: wszystkie kontrole PASS, kod zakończenia 0.
- Opera, raport lokalny: wyszukiwanie Semgrep i rozwijanie narzędzi z cytatami, przejście do właściwej pełnej wiadomości, wyszukiwanie `scoped_http.py`, analiza oraz pełny kod z numerami wierszy.
- Opera, edytor: dodanie i zmiana testowej alternatywy z uzasadnieniem, zapis i przywrócenie wariantu w lokalnym magazynie, dodanie połączenia „NIE”, kontrola widoczności nowego węzła i strzałki. Import/eksport JSON sprawdzono na poziomie testów danych, nie deklarujemy pełnego testu dialogu pobierania/importu w Operze.
- Kompilacja klienta i instalatora zakończona poprawnie; pozostają wcześniejsze ostrzeżenia kompilatora CS0108 i CS0649. Wersja plikowa 3.15.5.0. Zainstalowany klient ma identyczny SHA-256 jak paczka.
- Instalator zakończył kopiowanie, rejestrację MCP i uruchomienie klienta. Lokalnie zgłosił odmowę zapisu ustawień hooka Claude w `settings.json`; hook nie został dodany. Nie zmieniano zabezpieczeń tego pliku. Jest to ograniczenie integracji hooka, niezależne od Obserwatorium.

Paczka instalacyjna i prywatne raporty testowe są poza Git. Aktualizacja nie zmienia wcześniej wyeksportowanych HTML — potrzebny jest nowy eksport.
