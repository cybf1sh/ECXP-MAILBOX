# Warsztat rozwiązań w Obserwatorium

W raporcie analizy pracy agentów wybierz **Modele decyzyjne → Edytuj wariant grafu** przy wybranym wątku. Edytor tworzy niezależny szkic rozwiązania na podstawie węzłów i odnośników do oryginalnych wiadomości.

- Dodawaj kroki, warunki TAK/NIE, pomysły na narzędzia i testy rozwiązania.
- Wybierz węzeł na grafie lub liście, wpisz opis i uzasadnienie, metodę, oczekiwany wynik oraz kryterium testu. Zatwierdź przyciskiem **Zastosuj zmiany węzła**.
- Dodawaj i usuwaj połączenia; ich etykiety mogą opisywać warunek lub wynik, np. TAK, NIE, test nieudany.
- Oceny „hipoteza”, „do testu”, „potwierdzone”, „odrzucone” są deklaracjami autora wariantu. Nie zmieniają FIN ani treści wiadomości.
- **Zapisz w tej przeglądarce** i **Wczytaj zapis lokalny** przechowują jeden wariant danego grafu. Dostępność zależy od obsługi lokalnego magazynu przez przeglądarkę, zwłaszcza dla plików file://.
- **Pobierz JSON** zachowuje przenośny plik wariantu. Można pobrać kilka nazwanych wersji i porównać rozwiązania. **Importuj wariant JSON** odtwarza plik przy tym samym grafie. Plik innego grafu lub z nieprawidłowymi połączeniami zostanie odrzucony.

Edycja nie jest zapisywana automatycznie do oryginalnego HTML. Przed zamknięciem raportu zapisz wariant lub pobierz JSON. Usunięcie węzła usuwa również jego połączenia; ostatni zapis można ponownie wczytać. JSON może zawierać fragmenty prywatnych wiadomości.

To ręczny warsztat planowania i porównywania alternatyw. Nie wykonuje kroków, nie generuje samodzielnie narzędzi ani nie uruchamia modelu AI. Nowe eksporty HTML zawierają edytor bez dodatkowych bibliotek i usług sieciowych. Wcześniej wyeksportowane raporty nie aktualizują się automatycznie.
