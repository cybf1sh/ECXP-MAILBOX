# 08 – Bezpieczeństwo

## Założenia
- ECXP działa **lokalnie**, na jednym komputerze i koncie Windows. Łącznością w sieci LAN zajmie się dopiero ewentualna brama A2A **[plan]**. Internet nigdy.
- Treść wiadomości to **dane**, a nie polecenia. Nie nadaje uprawnień ani agentowi, ani stosowi.
- Kanał nie przenosi danych spraw, dowodów, haseł, kluczy ani odpowiedzi modeli z danymi spraw, tylko hashe, liczby, nazwy plików i testów.

## Model zagrożeń

| Zagrożenie | Ochrona | Stan |
|---|---|---|
| inny użytkownik Windows łączy się z potokiem | ACL potoku: tylko bieżący użytkownik (`PipeSecurity`) | w budowie |
| proces podszywa się pod operatora przez potok | stos sprawdza obraz procesu klienta (`GetNamedPipeClientProcessId` → ścieżka exe). Rola `operator` tylko dla `ECXP-MAILBOX.exe` z katalogu skrzynki | w budowie |
| agent podaje `ja = wlasciciel` | adapter MCP i stos odrzucają nazwy zastrzeżone | jest |
| zmiana wiadomości po wysłaniu | SHA-256 pliku w dzienniku + `skrot` treści → `integralnosc: USZKODZONA` | jest |
| zmiana lub usunięcie wpisu dziennika | łańcuch SHA-256 → „dziennik NARUSZONY” | jest |
| uszkodzona lub obca ramka na potoku | magic, wersja, limit długości, SHA-256 ramki → zerwanie | w budowie |
| wiadomość znika | strażnik porównuje dziennik z plikami → „zaginiona” + alarm | jest |
| agent milczy | terminy, ponowienia, eskalacja do operatora | jest |
| wyciek sekretu przez obiekt kontekstu | odmowa importu ścieżek sekretów i dowodów, limit 32 MB | jest |
| potwierdzenie cudzej wiadomości | `DoMnie(ja, do)` wymagane | jest |
| proces z prawem zapisu podkłada plik do kolejki z pominięciem stosu | brak wpisu `wyslana` w dzienniku: wiadomość oznaczona „brak wpisu w dzienniku”. Pełna ochrona wymaga podpisów węzłów | częściowo |

## Uwierzytelnienie operatora

| Poziom | Mechanizm | Stan |
|---|---|---|
| 1 | konto Windows (ACL potoku) | w budowie |
| 2 | obraz procesu klienta = `ECXP-MAILBOX.exe` w katalogu skrzynki | w budowie |
| 3 | podpis wiadomości operatora kluczem chronionym DPAPI (`CurrentUser`); agent weryfikuje podpis przez stos | plan |
| 4 | ramka `DECYZJA` (push, scalenie, USER_ACCEPTED, usunięcie) z osobnym potwierdzeniem w konsoli | plan, czeka na decyzję właściciela, czy ma moc decyzji z czatu |

Dopóki poziomy 3–4 nie działają, decyzje operatora (push, scalenie, USER_ACCEPTED dla dokładnego commitu, usuwanie, zmiany rezerwacji i ustawień bezpieczeństwa) zapadają wyłącznie w czacie. Wiadomości operatora przez ECXP to pytania, zadania i odpowiedzi.

## Granice
- Stos nie uwierzytelnia agentów kryptograficznie. Nazwa węzła jest deklarowana przy HELLO, a pilnowane są tylko nazwy zastrzeżone. Podpisy węzłów są możliwe w przyszłej wersji.
- Każdy proces bieżącego użytkownika z prawem zapisu do katalogu skrzynki może zmienić pliki. ECXP **wykrywa** zmiany (dziennik, skróty), ale ich nie **uniemożliwia**.
- Stos nie wybudza modeli. Opóźnienie reakcji zależy od automatyzacji Codexa (np. co 10 min) i od operatora.
