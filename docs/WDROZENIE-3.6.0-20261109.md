# Mailbox 3.6.0.0 — wdrożono 2026-11-09

Naprawiono wykonywanie narzędzi Codexa przez Mailbox. Standardowy start
`codex app-server` oraz zastępczy `codex exec` otrzymują oficjalną opcję
`-c features.prefer_mxc=true`. Na zgodnym Windows wybierany jest MXC;
pozostałe systemy zachowują skonfigurowany backend legacy. Polityki zapisu,
read-only, sieci i zgód pozostają przekazywane przez Mailbox.

Podstawa: [oficjalna dokumentacja Windows sandbox](https://learn.chatgpt.com/docs/windows/windows-sandbox).
Zweryfikowano na Windows 11 10.0.26300 i CLI 0.166.0. Dokumentacja opisuje
próbę MXC dla CLI 0.162.0 lub nowszego; starszych wersji nie zatwierdzono.
Brak gotowości zatrzymuje tworzenie zespołu z komunikatem błędu.

Odrzucony prototyp aktywacji pakietu przez narzędzie diagnostyczne Microsoft
nie jest częścią kodu, kompilacji ani instalatora. Nie przywracano pliku
TestPentestProces.exe z kwarantanny Nortona i nie wyłączano ochrony.

## Weryfikacja

- Z GUI osobnej instancji utworzono zespół w projekcie **test-zespol-mxc**,
  identyfikator uruchomienia **244ed561**. EvidLock nie był testowany.
- Dwaj testerzy Codex (`gpt-6.1-sol`) oraz dwaj Claude (`claude-opus-5-5`)
  odczytali lokalny sample_app.py, stworzyli własne testy i wykonali je.
  Wyniki: 4, 16, 3 i 22 metody testowe, wszystkie PASS. Testerzy Codex
  dodatkowo raportują 94 i 196 sprawdzeń; Claude wykonał deterministyczny fuzzing.
- Recenzenci Codex i Claude otrzymali zadania dopiero po czterech FIN.
  Obaj ponownie wykonali cztery skrypty i wydali **GO**. Recenzent Claude
  dodatkowo ocenił zestawy na mutantach implementacji.
- Niezależna recenzja bieżących Cieply.cs i Ecxpd.cs przez Claude Opus 5.5:
  **GO dla nowej poprawki**. Poprzedni NO-GO dotyczy usuniętego prototypu.
- Testy izolacji przez rzeczywisty app-server MXC: zapis w cwd i dodatkowym
  writableRoot dozwolony, poza zakresem odrzucony, zapis read-only odrzucony.
  Własny serwer loopback: networkAccess=false blokuje, true pozwala.
- Zimny `exec` rzeczywiście utworzył cold-ok.txt i wykonał polecenie sprawdzające.
- TestPentestu: 20 PASS; TestGotowosciPentestu: 3 PASS. Powolny readiness nie
  blokuje ping, nieuprawniony klient nie uruchamia CLI, brak gotowości nie
  tworzy zespołu. Walidacja Pentest.Sprawdz nie odczytuje ani nie zmienia stanu.
- Pełna kompilacja: PASS (dotychczasowe ostrzeżenia CS0649).
  Zasoby instalatora: 7/7 zgodnych SHA-256. git diff --check: PASS.

Pierwsza próba testu granic uruchamiała serwer w katalogu repozytorium,
przez co podkatalog uznany za zewnętrzny nadal należał do jego workspace.
Poprawiono katalog startowy testu, zgodnie z rzeczywistym startem Mailboxa,
i wykonano test ponownie na świeżych danych. Nie był to wynik PASS.

To test integracji i syntetycznej funkcji kontroli dostępu, nie audyt
bezpieczeństwa dowolnej aplikacji. Nie weryfikowano starszych CLI ani komputerów
bez MXC; w razie braku gotowości uruchomienie ma zakończyć się błędem.
Zimny tryb sprawdzono funkcjonalnie; granice sieci i dodatkowych katalogów
sprawdzono przez command/exec app-servera, nie osobno w zimnym exec.

## Artefakty lokalne

- Zespół i raporty: builds/mxc-team-20261109/stan/obieg/ oraz stan/pentesty/.
- Skrypty i dowody retestu: builds/mxc-team-20261109/projekty/test-zespol-mxc/.
- Recenzja kodu: builds/review-integracji-20261109/review-mxc.json.
- Końcowa kontrola granic: builds/mxc-boundaries-36b2b00cf2b746908356d0d52953e9b3/.
- Zimny exec: builds/mxc-boundaries-01d1bc88350945e8bece747b3b8c3dcd/.
- Instalator: builds/release-3.6.0/narzedzia/ECXP-Setup.exe.
- SHA-256 instalatora: AC56B42BED43A17DE7CC645EFC26C972BB9886A0E712DE161C88183AB7274435.

## Instalacja

Po udanych testach i recenzjach wykonano reinstalację do
C:\Users\user\AppData\Local\ECXP. GUI pokazuje kategorię bezpieczeństwo / testy
i przycisk zespołu sześciu specjalistów. Pliki ECXP-MAILBOX.exe, ecxpd.exe,
ecxp-mcp.exe i ECXP-Setup.exe są zgodne z przetestowaną kompilacją.
Wersja pliku GUI: 3.6.0.0; wpis Windows: 3.6.0. Stos i konsola są połączone.

Kopia danych przed wdrożeniem: builds/backup-before-mxc-20261109/ECXP.
Pominięto wyłącznie zablokowany tymczasowy claude-ecxp-mailbox.lock.
Instalator zgłosił odmowę ponownego zapisu .claude/settings.json. Zweryfikowano,
że istniejące UserPromptSubmit, SessionStart i Stop już wywołują ecxp-mcp.exe
w instalacji ECXP. Rejestracja MCP Claude i Codexa zakończyła się poprawnie.

Nie wysyłano commita do zdalnego repozytorium.
