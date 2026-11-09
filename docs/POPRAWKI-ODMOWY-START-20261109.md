# Odmowy dostawców i opóźniony start zespołu

## Zmiana zachowania

Mailbox rozpoznaje `cyberPolicy`, `stop_reason=refusal` oraz komunikat
`safeguards flagged this session` jako osobną przyczynę `odmowa`. Nie uznaje
udanego raportu wspominającego o odmowie za błąd.

Odmowa kończy bieżące zadanie negatywnym FIN `NIEWYKONANE`, bez przekazywania
partnerowi, ponownego wywołania modelu ani przejścia na zimny tryb CLI.
Identyfikator pracy jest zapisany przed wysłaniem FIN w `odmowy_prac` rekordu
agenta. Odtworzenie pętli/restart nie może ponowić tego ID nawet przy problemie
z doręczeniem FIN. Nowe zadanie tego samego agenta nie jest blokowane.
W przypadku pytania wysyłana jest negatywna odpowiedź, a pytanie znika z kolejki.

Oba transporty Claude sprawdzają metadane odmowy również wtedy, gdy CLI zwraca
exit code 0 lub is_error=false. Ciepły transport parsuje JSON strukturalnie;
odstępy między kluczami i wartościami nie powodują utraty wyniku.

GUI wysyła jedno zlecenie startu w wątku tła i oczekuje na rzeczywisty wynik
bez zwykłego limitu 60 sekund. Okno nadal przetwarza zdarzenia. Kolejna próba
startu w tej samej konsoli podczas oczekiwania nie wysyła drugiego zlecenia.
W czasie startu okresowe odpytywanie usług jest pomijane: serwer obsługuje
żądania jednej sesji kolejno, więc taki odczyt z wątku GUI czekałby za startem.
Zerwanie połączenia pokazuje status niepotwierdzony i prośbę o sprawdzenie
agentów przed ponowieniem; nie twierdzi, że zespół nie został uruchomiony.
Odświeżenie widoku po odebraniu sukcesu nie zmienia wyniku uruchomienia na błąd.

Oczekiwanie bez limitu dotyczy tylko startu pentestu. Pozostałe wywołania
klienta nadal zachowują dotychczasowy timeout. Nie wprowadzono automatycznego
ponawiania startu po rozłączeniu ani mechanizmu trwałego wznowienia statusu
po zamknięciu całej konsoli; w takiej sytuacji stan trzeba sprawdzić w projekcie.

## Testy — 52 PASS

- **TestOdmow: 18 PASS.** Klasyfikacja obu dostawców i JSON zagnieżdżonego,
  odróżnienie sukcesu i błędów technicznych, negatywny FIN bez przejęcia pracy
  przez partnera, zapis odmowy i dopuszczenie nowego ID. Rzeczywista pętla
  agentów i transport Claude używają sztucznego CLI: jedno żądanie, jedna
  tura, brak fallbacku i powtórzeń przez kolejne przebiegi pętli. Sprawdzono
  również zimny CLI zwracający kod 0/is_error=false oraz odmowę pytania.
- **TestStartuPentestu: 14 PASS.** Rzeczywiste GUI, klient ramkowy i lokalny
  serwer named pipe. Serwer celowo czeka 65 sekund, po czym zwraca sukces.
  Potwierdzono natychmiastowy powrót GUI, działanie timera, jedno żądanie mimo
  dwóch prób i komunikat sukcesu. Drugi scenariusz zrywa połączenie: wynik
  jest nieznany, a nie fałszywie oznaczony jako nieudany start.
  Oba scenariusze sprawdzają także, że odpytywanie usług wraca natychmiast
  podczas trwającego startu i nie blokuje obsługi zdarzeń formularza.
- **TestPentestu: 20 PASS.** Regresja planu sześciu agentów, bariery raportów,
  negatywnych wyników i recenzji bez duplikatów.
- Cały stos, GUI, MCP i instalator: kompilacja PASS (ostrzeżenia CS0649).
  `git diff --check`: PASS.

Nie ponawiano zleceń odrzuconych przez dostawców. Testy używały fixture
lokalnego CLI i serwera, bez dostępu do dostawców modeli i bez sieci zewnętrznej.
Nie testowano EvidLock ani innych istniejących projektów.

Katalog testu odmów: `builds/odmowy-53ebde8726df46f68c10e59d3b0245c5/`.
Katalog testu GUI: `builds/test-startu-56d6f052030d4fc883028b710872c32c/`.
Oddzielna kompilacja: `builds/poprawki-odmowy-start/narzedzia/`.
Źródła testów: `narzedzia/zrodlo/TestOdmow.cs` i `TestStartuPentestu.cs`.

Poprawki wdrożono do działającej instancji jako wersję 3.6.1.0.
Szczegóły instalacji i sprawdzenia GUI po wdrożeniu:
[WDROZENIE-3.6.1-20261109.md](WDROZENIE-3.6.1-20261109.md).
