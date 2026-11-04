// Test regresyjny doręczania (04.11.2026): agent w tle nie może odbierać za sesję w aplikacji pod tym samym adresem
// ani połykać pytań/zadań/recenzji od innych agentów. Pusty katalog, dane syntetyczne, bez uruchamiania agentów (tur).
// Kompilacja: jak ecxp-mcp.exe (RDZEN z buduj.cmd) + ten plik, /main:Ecxp.TestDoreczania. Uruchomienie: TestDoreczania.exe <pusty katalog>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Ecxp
{
    static class TestDoreczania
    {
        static int zle;
        static readonly StringBuilder log = new StringBuilder();
        static void Ok(bool c, string t) { log.AppendLine((c ? "PASS  " : "FAIL  ") + t); if (!c) zle++; }

        static bool WKolejce(string id) { return P.Znajdz(id) != null && P.Znajdz(id).Katalog == "do-claude"; }

        // Ostatnia tura 45 min temu: limit 1 tury na godzinę zatrzymuje nową turę, a 30-minutowe rozgrzewanie się nie włącza.
        static void BezTur(string nazwa)
        {
            var p = Path.Combine(P.Root, "stan", "agenci", nazwa + ".json");
            var d = Pliki.Czytaj(p);
            d["na_godzine"] = 1;
            d["uruchomienia"] = new List<object> { P.D("przyczyna", "", "czas", DateTimeOffset.Now.AddMinutes(-45).ToString("yyyy-MM-dd'T'HH:mm:sszzz"), "praca", "", "sekund", 1, "kod", 0, "wynik", "test") };
            Pliki.Zapisz(p, d);
        }

        [STAThread]
        public static int Main(string[] args)
        {
            string root = args.Length > 0 ? Path.GetFullPath(args[0]) : "";
            try
            {
                if (root.Length == 0) throw new Exception("Podaj pusty katalog testu.");
                Directory.CreateDirectory(root);
                if (Directory.EnumerateFileSystemEntries(root).Any()) throw new Exception("Katalog testu musi być pusty: " + root);
                P.Root = root;
                foreach (var d in P.Foldery) Directory.CreateDirectory(Path.Combine(root, d));
                var katalog = Path.Combine(root, "projekty", "test"); Directory.CreateDirectory(katalog);
                var role = new List<string> { "recenzent" };
                bool cli = Agenci.Program("claude").Length > 0;
                Ok(cli, "Claude Code CLI w PATH (wymagany przez Agenci.Uruchom; tur testu nie uruchamia)");

                // 0. reguła tur
                Ok(Agenci.WymagaTury(P.Wlasciciel, "pytanie") && !Agenci.WymagaTury(P.Wlasciciel, "zadanie") && !Agenci.WymagaTury(P.Wlasciciel, "informacja"),
                    "operator: pytanie = tura, zadanie prowadzi obieg, informacja = sam ACK");
                Ok(Agenci.WymagaTury("codex-x", "pytanie") && Agenci.WymagaTury("codex-x", "zadanie") && Agenci.WymagaTury("codex-x", "recenzja") && Agenci.WymagaTury("codex-x", "blokada"),
                    "agent: pytanie, zadanie, recenzja i blokada = tura (nie przepadają)");
                Ok(!Agenci.WymagaTury("codex-x", "informacja") && !Agenci.WymagaTury("codex-x", "odpowiedz") && !Agenci.WymagaTury(P.Straznik, "blokada"),
                    "agent: informacja i odpowiedź = sam ACK; alarmy strażnika bez tury");

                // 1. incydent 04.11: sesja w aplikacji ma adres, zadanie „w tle” próbuje założyć agenta o tej samej nazwie
                Siec.Przypisz("claude-tsesja", "evidlockv3", "mod-a", "9103dc06-e8c1-466f-9b2f-744aba2325e6", role, "", "claude-tsesja");
                Ok(Agenci.SesjeInteraktywne().Contains("claude-tsesja"), "sesja z identyfikatorem aplikacji rozpoznana jako interaktywna");
                var w1 = P.Wyslij(new P.Nowa { Od = "codex-tsesja", Do = "claude-tsesja", Projekt = "evidlockv3", Modul = "mod-a", Typ = "informacja", Temat = "PROSBA-O-RECENZJE", Tresc = "Syntetyczna prośba o recenzję." });
                bool odrzucony = false; string powod = "";
                try { Agenci.Uruchom("claude-tsesja", "ecxp-test", "mod-a", katalog, "", "edycja", role, "", 12); }
                catch (EcxpException ex) { odrzucony = true; powod = ex.Message; }
                Ok(odrzucony && powod.Contains("ma już sesję"), "agent w tle pod adresem sesji odrzucony: " + powod);
                Ok(!File.Exists(Path.Combine(root, "stan", "agenci", "claude-tsesja.json")), "brak rekordu agenta w tle dla adresu sesji");
                Agenci.KrokPetli();
                Ok(WKolejce((string)w1["id"]), "po kroku pętli agentów wiadomość nadal czeka na sesję (nie połknięta)");
                var odb1 = P.Odbierz("claude-tsesja", 10);
                Ok(odb1.Any(x => P.S(x, "id") == (string)w1["id"]), "sesja sama odbiera swoją wiadomość");

                // 2. odwrotna kolejność: agent w tle istniał wcześniej (jak rekord z 21:17), potem sesja zgłosiła ten adres
                if (cli)
                {
                    Agenci.Uruchom("claude-tpozniej", "ecxp-test", "mod-c", katalog, "", "edycja", role, "", 12);
                    BezTur("claude-tpozniej");
                    Ok(Agenci.ObslugiwaniWTle().Contains("claude-tpozniej"), "agent w tle obsługiwany, dopóki adres nie ma sesji");
                    var w2 = P.Wyslij(new P.Nowa { Od = "codex-tpozniej", Do = "claude-tpozniej", Projekt = "evidlockv3", Modul = "mod-c", Typ = "recenzja", Temat = "RECENZJA-PAKIETU", Tresc = "Syntetyczna recenzja." });
                    var w3 = P.Wyslij(new P.Nowa { Od = "codex-tpozniej", Do = "claude-tpozniej", Projekt = "evidlockv3", Modul = "mod-c", Typ = "informacja", Temat = "INFO", Tresc = "Syntetyczna informacja." });
                    Siec.Przypisz("claude-tpozniej", "evidlockv3", "mod-c", "11111111-2222-3333-4444-555555555555", role, "", "claude-tpozniej");
                    Ok(!Agenci.ObslugiwaniWTle().Contains("claude-tpozniej"), "po zgłoszeniu sesji stos przestaje odbierać za agenta w tle");
                    Agenci.KrokPetli();
                    Ok(WKolejce((string)w2["id"]) && WKolejce((string)w3["id"]), "recenzja i informacja czekają na sesję po kroku pętli");
                    Ok(P.Odbierz("claude-tpozniej", 10).Count == 2, "sesja odbiera obie wiadomości");
                }

                // 3. zwykły agent w tle (bez sesji): pytanie od drugiej strony nie przepada, informacja kończy się na ACK
                if (cli)
                {
                    Agenci.Uruchom("claude-ttlo", "ecxp-test", "mod-d", katalog, "", "edycja", role, "", 12);
                    BezTur("claude-ttlo");
                    var q = P.Wyslij(new P.Nowa { Od = "codex-ttlo", Do = "claude-ttlo", Projekt = "ecxp-test", Modul = "mod-d", Typ = "pytanie", Temat = "PYTANIE-PARTNERA", Tresc = "Syntetyczne pytanie." });
                    var inf = P.Wyslij(new P.Nowa { Od = "codex-ttlo", Do = "claude-ttlo", Projekt = "ecxp-test", Modul = "mod-d", Typ = "informacja", Temat = "INFO", Tresc = "Syntetyczna informacja." });
                    Agenci.KrokPetli();
                    var rec = Pliki.Czytaj(Path.Combine(root, "stan", "agenci", "claude-ttlo.json"));
                    Ok(P.L(rec, "pytania").Contains((string)q["id"]), "pytanie partnera zapisane jako praca agenta (tura z odpowiedzią do nadawcy)");
                    Ok(!WKolejce((string)inf["id"]) && P.Znajdz((string)inf["id"]).Katalog == "przeczytane", "informacja partnera potwierdzona odbiorem (bez tury)");
                    Ok(P.Lista(rec, "uruchomienia").Count == 1, "test nie uruchomił tury agenta");
                    // 4. wyłączony agent nie jest już „obsługiwany w tle” – doręczanie wraca do budzenia i alarmów
                    Agenci.Zatrzymaj("claude-ttlo");
                    Ok(!Agenci.ObslugiwaniWTle().Contains("claude-ttlo"), "wyłączony agent nie blokuje budzenia i alarmów doręczania");
                }

                string spoj; P.Stany(out spoj);
                Ok(spoj.StartsWith("spójny"), "dziennik po teście: " + spoj);
            }
            catch (Exception ex) { log.AppendLine("FAIL  wyjątek: " + ex); zle++; }
            log.AppendLine(zle == 0 ? "WYNIK: wszystkie testy PASS" : "WYNIK: FAIL " + zle);
            Console.Write(log.ToString());
            try { if (root.Length > 0) File.WriteAllText(Path.Combine(root, "test-doreczania.txt"), log.ToString(), new UTF8Encoding(false)); } catch { }
            return zle;
        }
    }
}
