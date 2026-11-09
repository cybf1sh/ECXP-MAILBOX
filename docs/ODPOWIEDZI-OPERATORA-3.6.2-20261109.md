# Odpowiedzi operatora uruchamiają agentów — 3.6.2.0

## Przyczyna

Przycisk Odpowiedz i szybki edytor GUI wysyłają typ `odpowiedz`.
`Agenci.WymagaTury` dopuszczało od operatora tylko typ `pytanie`.
Wiadomości operatora były więc odbierane i potwierdzane (ACK), lecz nie
trafiały do kolejki pracy. Potwierdzenie doręczenia nie oznaczało wykonania.
Ten przebieg potwierdził dziennik działającej instancji dla wiadomości
wysłanych między 23:08 a 23:12, bez kolejnych tur adresatów.

## Poprawka

Odpowiedź operatora jest teraz kolejną pracą agenta i otrzymuje odpowiedź
z właściwym `odpowiedz_na`. Dotyczy wiadomości z odwołaniem do poprzedniego
raportu i bez odwołania. Informacje nadal kończą się na ACK, a odpowiedzi
między agentami nie tworzą nieskończonej wymiany. Identyfikatory zadań
odrzuconych przez dostawcę pozostają zablokowane; nowe ID pracy jest dozwolone.

Historycznych, potwierdzonych już wiadomości nie odtworzono automatycznie:
operator wysłał kilka kopii podobnego polecenia. Ostatnią należy wysłać
ponownie, aby nie uruchamiać wielu duplikatów ani starych poleceń.

## Testy — 60 PASS

- `TestOdpowiedziOperatora`: 13 PASS. Prawdziwa pętla i transporty Codex
  app-server oraz Claude stream-json z lokalnymi atrapami. Dwie wiadomości
  do każdego dostawcy: zwykła odpowiedź i dalsza odpowiedź na raport.
  Cztery żądania, cztery wyniki, puste kolejki, brak ponowień i zachowane
  ID wcześniejszej odmowy. Spójność dziennika potwierdzona.
- `TestOdmow`: 18 PASS. Brak ponawiania/fallbacku po odmowie, trwałość ID,
  negatywne zakończenia i odblokowanie nowych zadań.
- `TestPentestu`: 20 PASS. Plan sześciu agentów, bariera wyników,
  negatywne zakończenia i recenzje bez duplikatów.
- Test na zainstalowanym `ecxpd.exe`: 9 PASS w nowej skrzynce. Obaj
  dostawcy otrzymali po jednej odpowiedzi operatora i zwrócili po jednym
  wyniku. Wiadomości przygotowano przed startem stosu, zachowując jeden
  proces zapisujący. Test zakończył i posprzątał własne procesy.
- Kompilacja stosu, GUI, MCP i instalatora: PASS (ostrzeżenia CS0649).
- Zasoby instalatora: 7/7 zgodnych sum z plikami wydania.

Testy wykonywano w nowych katalogach `builds/odpowiedzi-final-*` oraz
`builds/regresja-odpowiedzi-*`, w projekcie test-odpowiedzi. Nie używano
EvidLock, sieci zewnętrznej ani rzeczywistych modeli. Nie włączano
przechwytywania pulpitu. Testy te potwierdzają transport i kolejkowanie,
nie wykonanie rzeczywistego pentestu.

## Pakiet

Instalator: `builds/release-3.6.2/narzedzia/ECXP-Setup.exe`.
SHA-256: `AD3CCA4C5D8A3E781DD0A8B0961EB33CD6801FCFCE0477EE4B03B3078DE4084B`.

W czasie diagnozy operator utworzył również nowe zadanie zwykłej pary.
Claude je odebrał i zwrócił raport o braku uprawnienia WebFetch. To odrębna
kwestia dostępu narzędzia do stron, nie awaria odbioru wiadomości. Nie
zmieniano ustawień bezpieczeństwa ani uprawnień narzędzi.

## Wdrożenie

Przed instalacją zaczekano na zakończenie bieżącej tury Codexa i potwierdzono
brak otwartych obiegów oraz oczekujących pytań. Kopia poprzedniej instancji
i konfiguracji: `builds/backup-before-3.6.2/`.
Reinstalacja do `C:\Users\user\AppData\Local\ECXP` zakończyła się kodem 0.
Zainstalowane GUI, stos, MCP i instalator mają sumy identyczne z wydaniem
(4/4 PASS). GUI i instalator zgłaszają wersję 3.6.2.0.
Log potwierdza ponowne połączenie zwykłej konsoli z właściwą skrzynką.
Test po wdrożeniu nie obejmował przechwytywania ani nagrywania pulpitu.
