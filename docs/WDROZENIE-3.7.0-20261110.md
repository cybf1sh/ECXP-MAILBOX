# Mailbox 3.7.0 — 10 listopada 2026

## Uruchomienie zespołu

1. Kliknij **Nowe zadanie**, wpisz nazwę nowego projektu, moduł i opis.
2. Wybierz kategorię **Bezpieczeństwo / testy**.
3. Podaj cel oraz dozwolone hosty, konta, mechanizmy, limity i zasady testów.
4. W formularzu zaznacz potrzebne uprawnienia. Dla WebFetch wpisz same domeny
   (np. `example.com`) albo zaznacz import i wybierz JSON/TXT.
5. Opcjonalnie wybierz **Claude: użyj Opus 4.8** lub zaznacz własny model
   Claude/Codexa i podaj dokładny identyfikator. Niezaznaczone opcje własnego
   modelu przywracają widoczną wartość domyślną. Nie ma cichej zamiany modelu
   po odmowie dostawcy.
6. Uruchom zespół. Czterech testerów (po dwóch Codex i Claude) wykonuje pracę;
   dwa odrębne zadania recenzji startują po uzyskaniu czterech wyników.

WebFetch importuje wyłącznie domeny z `permissions.allow` w regułach
`WebFetch(domain:example.com)` albo z listy TXT. Nie uruchamia hooków,
nie importuje innych narzędzi ani modeli. JSON z regułami WebFetch `deny`/`ask`
wymaga rozstrzygnięcia przed importem. Nie są akceptowane URL z portem/ścieżką,
argumenty CLI ani globalny wildcard. Limit wynosi 64 domeny i 64 KB pliku.

Checkboxy obowiązują wszystkich sześciu agentów, w ciepłym i jednorazowym
transporcie, i pozostają zapisane w metadanych po restarcie. Claude ma osobny
plik uprawnień i listę dostępnych narzędzi. Prywatne ustawienia allow nie są
dziedziczone; zarządzane polityki dostawcy pozostają nadrzędne. Wyłączone
narzędzia dostają również reguły deny. Włączony terminal może wykonywać własny
ruch sieciowy: lista domen WebFetch ogranicza to narzędzie, nie cały komputer.
Opcje zespołu nie podnoszą uprawnień Windows.

Codex dostaje wybrany model oraz politykę odczytu/zapisu i sieci. W razie
konieczności użycia starszego transportu jednorazowego połączenie „tylko
odczyt + sieć” zgłasza brak obsługi zamiast zmieniać zakres uprawnień.

## Instrukcje testerów i pamięć projektu

Pierwszym celem są błędy bezpieczeństwa z rzeczywistym wpływem. Siedem etapów
obejmuje mapę mechanizmów, granice zaufania, hipotezy, analizę kodu i harnessy,
ograniczone próby zdalne, kontrolę negatywną/PoC i przekazanie dowodów.
Wskazany plik, np. `ext/session/session.c`, wymaga sprawdzenia wersji,
funkcji, wywołujących i związku z badanym mechanizmem. Sam przykład nie
przesądza o technologii aplikacji.

Instrukcje obejmują SQLi, konteksty XSS i polygloty, Command Injection,
IDOR/BAC i izolację tenantów, reset hasła, SSRF/XXE, konfigurację Nginx,
LFI/Path Traversal oraz własne skrypty PoC. Dobór przypadków wynika z
potwierdzonych mechanizmów, kodu i zakresu. Próby odczytu oraz callbacki
wykorzystują przygotowane znaczniki. Metody analizy klas podobnych do
PHP2Shell obejmują czas życia danych i ścieżki błędów; eksperymenty z awariami
i presją pamięci mają lokalny harness z limitami.

Chaining wymaga dowodów każdego ogniwa i zgodności warunków przejścia.
Demonstracyjny exploit powstaje w izolowanej kopii; na celu zdalnym dowód
pozostaje niedestrukcyjny. Recenzent odtwarza ogniwa, kontrolę negatywną i PoC;
brak reprodukcji oznacza **NIEPOTWIERDZONE**. Nieudany lub niewykonany test
nie oznacza bezpieczeństwa aplikacji.

Przy dozwolonym zapisie agenci mają utrwalać zredagowane wnioski i własne
werdykty w `wiedza-bezpieczenstwa/<nazwa-agenta>/` w projekcie. Kolejna tura
korzysta z nich jako danych historycznych, ponownie sprawdza warunki i
proponuje warianty oraz regresje. Hipotezy odrzucone pozostają opisanymi
lekcjami. To pamięć projektu, nie trenowanie wag modeli. Przy braku zapisu
wnioski trafiają do raportu. Polecenie w prompcie nie gwarantuje kompletnego
audytu ani powstania wszystkich artefaktów; raport pokazuje ograniczenia.

## Trwałe usuwanie projektu

Wybierz konkretny projekt i kliknij **Usuń projekt…** obok **Nowe zadanie**.
Domyślnie usuwane są dane i katalog projektu Mailbox. Osobne, domyślnie
odznaczone checkboxy wybierają lokalne sesje Codexa, sesje/pamięć Claude
oraz uruchomienie natywnego pomocnika przez systemowe UAC (`runas`).

Zawsze najpierw widoczny jest plan. Zaznaczenie danych dostawców dodaje
ostrzeżenie, konieczność wpisania dokładnej nazwy projektu i zaznaczenia
świadomości nieodwracalności. Sam checkbox niczego nie usuwa. Operacja
pomija Kosz; UAC nie przyznaje modelom samodzielnego prawa kasowania.

Codex używa oficjalnego `thread/list`/`thread/delete`, obejmuje aktywne
i archiwalne rozmowy oraz potomków i czyści lokalne wpisy wybranego korzenia.
Powiązanie z innym katalogiem lub wielokorzeniowy projekt blokuje usunięcie.
Claude korzysta z oficjalnego `project purge` lub `purge` (zależnie od CLI),
podglądu `--dry-run` i dopiero potem `--yes`; dodatkowo usuwane są rozpoznane
dane tymczasowe sesji tego projektu.

Ścieżki poza zakresem i junction/symlinki są odrzucane. Bilet jest jednorazowy,
wygasa po 15 minutach i wymaga zgodności planu. Agenci projektu są zatrzymywani,
a nowe prace blokowane na czas operacji. Współdzielone załączniki i dane innych
projektów pozostają. Usunięcie wiadomości zachowuje spójność dziennika.
Błąd dostawcy daje wynik częściowy z wykonanymi krokami i zachowuje projekt
Mailbox; usunięte wcześniej dane innego dostawcy nie są odtwarzane.

Operacja nie obejmuje kopii zapasowych, współdzielonych danych globalnych,
retencji usług w chmurze ani bezpiecznego nadpisywania nośnika. Po awarii
pomocnika należy sprawdzić wynik operacji i blokadę projektu przed ponowieniem.

## Weryfikacja

Ostatnie przejścia ośmiu zestawów w nowych, syntetycznych katalogach:
**180 PASS, 0 FAIL**. Po recenzji powtórzono testy zmienionych mechanizmów.

| Zestaw | PASS |
|---|---:|
| Opcje GUI i cały zespół przez kontrolowane fixture CLI | 32 |
| Usuwanie, separacja projektów, bilet, UAC i błąd częściowy | 39 |
| Rzeczywiste formularze Napisz/Odpowiedz, obaj dostawcy fixture | 15 |
| Odmowy, awaria, Stop, utrata ACK i zgodność starego CLI | 22 |
| Plan testerów, bariera recenzji i anulowanie startu | 21 |
| Obsługa odpowiedzi operatora | 13 |
| Doręczanie i brak przejmowania cudzej sesji | 24 |
| Opóźniony start, rozłączenie i responsywność GUI | 14 |

Dodatkowo finalny natywny Mailbox przeszedł 3 kontrole autoryzacji i procesu
pomocnika usuwania, wyłącznie na dwóch świeżych projektach syntetycznych.
Potwierdzono też rzeczywisty, niedestrukcyjny podgląd obu CLI na nowym projekcie.

Przez finalny transport `Cieply` rzeczywisty Claude Opus 4.8 pobrał
`https://example.com/` i zwrócił `MAILBOX_FETCH_OK`; rzeczywisty Codex
`gpt-6-luna` zwrócił `MAILBOX_CODEX_OK` w trybie tylko odczyt bez sieci.
Rzeczywiste WebFetch do `www.iana.org`, niewymienionego na liście domen,
zostało odrzucone przez reguły narzędzia i zapisane w `permission_denials`.
Są to testy faktycznych dostawców; testy deterministyczne sześciu agentów
korzystają z fixture i nie dowodzą skuteczności pentestu konkretnej aplikacji.
UAC zweryfikowano na poziomie `ProcessStartInfo`; okno zgody administratora
nie było automatycznie zatwierdzane. Nie nagrywano pulpitu.

Regresję można powtórzyć z Windows PowerShell:

```powershell
narzedzia\zrodlo\testuj-3.7.ps1 -Stack D:\sciezka\narzedzia\ecxpd.exe
```

Wymagane są .NET Framework 4 i Python w `C:\Python314\python.exe` (ścieżka
fixture na tym stanowisku). Skrypt tworzy nowy katalog wyników w `builds/`.

## Pakiet

Źródła: `narzedzia/zrodlo/`. Pakiet i instalator:
`builds/release-3.7.0/narzedzia/`. Instalator jest także w zwykłej lokalizacji
`narzedzia/ECXP-Setup.exe`. GUI i instalator: **3.7.0.0**.
Kopia sprzed reinstalacji: `builds/backup-before-3.7.0/`.
Zestawienie wyników: `builds/weryfikacja-3.7.0.json`. Szczegóły ostatnich
przejść: `builds/regresja-review-final-3.7.0/` oraz
`builds/regresja-guard-final-3.7.0/`.
Ostatnie poprawki recenzji i testy: `builds/regresja-final-review2-3.7.0/`.

Konfiguracje Claude wymagające `env` lub `apiKeyHelper` w prywatnych plikach
ustawień nie należą do zweryfikowanego zakresu tej wersji. Testowano natywne
uwierzytelnienie tego stanowiska. Izolowana konfiguracja zespołu nie importuje
tych prywatnych ustawień automatycznie.

## Recenzja i reinstalacja

Niezależna recenzja Claude obejmowała wcześniej zatwierdzone `Cieply.cs`
i `Ecxpd.cs`. Wykryte błędy poprawiono: brak ponowień po awarii/Stop oraz
niepewnym ACK startu tury; zachowany fallback po odrzuceniu opcji CLI przed
pracą; blokada mutacji i porównanie epoki po preflight; nieaktywna rejestracja
aż do zapisania opcji; zatrzymanie i wycofanie nieudanego zespołu. Końcowy
werdykt: **GO**, bez nowych defektów blokujących w sprawdzonych plikach.
Pozostałe części oceniono lokalnie i objęto wskazanymi testami; recenzja
dwóch plików nie jest pełnym audytem całej aplikacji.

Reinstalacja do `C:\Users\user\AppData\Local\ECXP` zakończyła się kodem 0.
Wszystkie cztery zainstalowane programy mają SHA-256 zgodne z finalnym
pakietem. Nowa konsola i stos uruchomiły się i zestawiły połączenie.
Zachowano dane, ustawienia i skróty; kopia poprzedniej instancji pozostaje
w `builds/backup-before-3.7.0/`.

Po reinstalacji wykonano w osobnych, nowych katalogach:

- 32 kontrole GUI opcji i sześciu agentów fixture przez zainstalowany stos;
- 15 kontroli formularzy **Napisz/Odpowiedz** (obie strony, brak duplikatów);
- 3 kontrole zainstalowanego natywnego GUI/pomocnika usuwania;
- rzeczywiste odpowiedzi Opus 4.8 i Codexa oraz rzeczywistą odmowę WebFetch
  dla domeny spoza przyznanej listy.

Wszystkie te sprawdzenia przeszły. Nie nagrywano pulpitu ani nie zatwierdzano
automatycznie UAC. Wyniki: `builds/gui-po-wdrozeniu-3.7.0/`,
`builds/native-installed-3.7.0.txt`, `builds/real-options-3.7.0.json`,
`builds/wdrozenie-3.7.0.json` i `builds/review-final-inline-3.7.0/review.json`.

SHA-256 instalatora 3.7.0.0:
`F3957512CBB2CC3538C64FC3E9D1E956A3B40C44A299D5C3762DEFFE4E58B2AD`.

## Dokumentacja dostawców

- [Claude: ustawienia i łączenie list uprawnień](https://code.claude.com/docs/en/settings)
- [Claude CLI: setting-sources, settings i tools](https://code.claude.com/docs/en/cli-reference)
- [Claude: reguły WebFetch](https://code.claude.com/docs/en/permissions#webfetch)
- [Claude: usuwanie lokalnego stanu](https://code.claude.com/docs/en/claude-directory#clear-local-data)
- [Codex app-server](https://learn.chatgpt.com/docs/app-server)
- [OWASP: Path Traversal](https://community.owasp.org/attacks/Path_Traversal)
