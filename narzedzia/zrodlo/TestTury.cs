// Test hooków ECXP i liczenia tokenów (Tury.cs, Hooki.cs). Pusty katalog, syntetyczne zapisy rozmów i wiadomości,
// bez uruchamiania agentów. Kompilacja: Mcp.cs-owy RDZEN + Hooki.cs + ten plik, /main:Ecxp.TestTury.
// Uruchomienie: TestTury.exe <pusty katalog>
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace Ecxp
{
    static class TestTury
    {
        static int zle;
        static readonly StringBuilder log = new StringBuilder();
        static void Ok(bool c, string t) { log.AppendLine((c ? "PASS  " : "FAIL  ") + t); if (!c) zle++; }

        static string Hook(string zd, Dictionary<string, object> we)
        {
            var wy = new StringWriter();
            Hooki.Uruchom(zd, new StringReader(P.Js().Serialize(we)), wy);
            return wy.ToString();
        }

        static string Asystent(string id, int we, int cr, int cw, int wy)
        {
            return P.Js().Serialize(P.D("type", "assistant", "message", P.D("id", id, "role", "assistant", "usage",
                P.D("input_tokens", we, "cache_read_input_tokens", cr, "cache_creation_input_tokens", cw, "output_tokens", wy)))) + "\n";
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
                Environment.SetEnvironmentVariable("ECXP_DORECZANIE", null);
                const string ses = "aaaaaaaa-1111-2222-3333-444444444444";
                var zapis = Path.Combine(root, "rozmowa-" + ses + ".jsonl");
                File.WriteAllText(zapis, "{\"type\":\"user\",\"message\":{\"role\":\"user\",\"content\":\"start\"}}\n", new UTF8Encoding(false));
                var we = P.D("session_id", ses, "transcript_path", zapis);

                // 1. rozpoznanie sesji: tylko przez trasę z identyfikatorem sesji; sesja spoza ECXP – hook milczy
                Ok(Hook("SessionStart", we).Length == 0 && !Directory.Exists(Path.Combine(root, "stan", "tury", "sygnaly")), "sesja spoza ECXP: hook nic nie wypisuje i nic nie zapisuje");
                File.Delete(Path.Combine(root, "stan", "hook-sesje", ses + ".txt"));
                Siec.Przypisz("claude-th", "test", "hooki", ses, new List<string> { "tester" }, "", "claude-th");
                Ok(Hooki.Nazwa(ses, zapis, "") == "claude-th", "nazwa sesji z trasy ecxp_przypisz (bez czytania zapisu rozmowy)");

                // 2. start sesji i wpis operatora: przypomnienie tylko, gdy coś czeka; lustro wpisu do stan\wejscia
                Ok(Hook("SessionStart", we).Length == 0, "start sesji bez oczekujących wiadomości – brak przypomnienia");
                var w1 = P.Wyslij(new P.Nowa { Od = "codex-th", Do = "claude-th", Projekt = "test", Modul = "hooki", Typ = "pytanie", Temat = "PYTANIE-TESTOWE", Tresc = "Syntetyczne pytanie." });
                var s1 = Hook("SessionStart", we);
                Ok(s1.Contains("[ECXP]") && s1.Contains("PYTANIE-TESTOWE"), "start sesji: przypomnienie z tematem oczekującej wiadomości");
                var u1 = Hook("UserPromptSubmit", P.D("session_id", ses, "transcript_path", zapis, "prompt", "Syntetyczny wpis operatora."));
                Ok(u1.Contains("PYTANIE-TESTOWE"), "wpis operatora: przypomnienie");
                var wej = Directory.GetFiles(Path.Combine(root, "stan", "wejscia"), "*.json");
                Ok(wej.Length == 1 && Pliki.Czytaj(wej[0])["tekst"].ToString() == "Syntetyczny wpis operatora.", "wpis operatora trafia do lustra (stan\\wejscia)");
                Hook("UserPromptSubmit", P.D("session_id", ses, "transcript_path", zapis, "prompt", "<system-reminder>x</system-reminder>"));
                Ok(Directory.GetFiles(Path.Combine(root, "stan", "wejscia"), "*.json").Length == 1, "wpis systemowy nie trafia do lustra");

                // 3. koniec tury: jedno „block”, bez pętli, bez odbioru za sesję
                File.AppendAllText(zapis, Asystent("msg_A", 10, 1000, 200, 50) + Asystent("msg_A", 10, 1000, 200, 70) + Asystent("msg_B", 5, 1500, 0, 30), new UTF8Encoding(false));
                var st1 = Hook("Stop", P.D("session_id", ses, "transcript_path", zapis, "stop_hook_active", false));
                var dec = st1.Trim().Length > 0 ? P.Js().Deserialize<Dictionary<string, object>>(st1) : null;
                Ok(dec != null && P.S(dec, "decision") == "block" && P.S(dec, "reason").Contains("ecxp_odbierz(ja=claude-th)"), "koniec tury z oczekującą wiadomością: decision=block z poleceniem odbioru");
                Ok(Hook("Stop", P.D("session_id", ses, "transcript_path", zapis, "stop_hook_active", true)).Length == 0, "stop_hook_active: drugi raz nie blokuje (brak pętli)");
                Ok(P.Znajdz((string)w1["id"]).Katalog == "do-claude", "hook nie odebrał i nie potwierdził wiadomości za sesję");
                Environment.SetEnvironmentVariable("ECXP_DORECZANIE", "1");
                Ok(Hook("Stop", P.D("session_id", ses, "transcript_path", zapis, "stop_hook_active", false)).Length == 0, "tura budzenia ze stosu: bez blokady");
                Environment.SetEnvironmentVariable("ECXP_DORECZANIE", null);

                // 4. tokeny Claude: sygnały start/stop, każda wiadomość raz (ostatni wpis o danym id), przyrost między turami
                Tury.PrzetworzSygnaly();
                var t1 = Tury.Lista("claude-th");
                Ok(t1.Count == 1 && t1[0].Wejscie == 15 && t1[0].CacheOdczyt == 2500 && t1[0].CacheZapis == 200 && t1[0].Wyjscie == 100,
                    "tura 1: suma usage bez powtórzeń (wejście 15, cache 2500+200, wyjście 100): " + (t1.Count > 0 ? t1[0].Razem.ToString() : "brak"));
                Ok(!Directory.GetFiles(Tury.DirSygnaly, "*.json").Any(), "sygnały przetworzone i usunięte");
                P.Odbierz("claude-th", 10);
                Hook("UserPromptSubmit", P.D("session_id", ses, "transcript_path", zapis, "prompt", "Drugi wpis."));
                File.AppendAllText(zapis, Asystent("msg_C", 1, 100, 0, 9), new UTF8Encoding(false));
                Ok(Hook("Stop", P.D("session_id", ses, "transcript_path", zapis, "stop_hook_active", false)).Length == 0, "koniec tury bez oczekujących wiadomości – bez blokady");
                Tury.PrzetworzSygnaly();
                var t2 = Tury.Lista("claude-th");
                Ok(t2.Count == 2 && t2[1].Razem == 110 && t2[0].Razem == t1[0].Razem, "tura po odebraniu: tylko przyrost (110 tokenów), poprzednia bez zmian");

                // 5. tokeny Codexa z rollout: delta licznika narastającego między task_started a task_complete
                var plikC = Path.Combine(root, "rollout-2026-11-04T22-00-00-bbbbbbbb-1111-2222-3333-444444444444.jsonl");
                Func<int, int, int, Dictionary<string, object>> licz = (i, c, o) => P.D("type", "token_count", "info", P.D("total_token_usage", P.D("input_tokens", i, "cached_input_tokens", c, "cache_write_input_tokens", 0, "output_tokens", o)));
                var cz = DateTime.Now;
                Tury.ZdarzenieCodex(plikC, "codex-th", licz(1000, 800, 50), cz);
                Tury.ZdarzenieCodex(plikC, "codex-th", P.D("type", "task_started", "turn_id", "t1"), cz);
                Tury.ZdarzenieCodex(plikC, "codex-th", licz(1600, 1300, 90), cz);
                Tury.ZdarzenieCodex(plikC, "codex-th", licz(2500, 2100, 140), cz);
                Tury.ZdarzenieCodex(plikC, "codex-th", P.D("type", "task_complete", "turn_id", "t1"), cz.AddSeconds(1));
                var tc = Tury.Lista("codex-th");
                Ok(tc.Count == 1 && tc[0].Wejscie == 200 && tc[0].CacheOdczyt == 1300 && tc[0].Wyjscie == 90 && tc[0].Sesja.StartsWith("bbbbbbbb"),
                    "Codex: tura = różnica licznika (wejście 200, cache 1300, wyjście 90), wątek z nazwy pliku");
                Tury.ZdarzenieCodex(plikC, "codex-th", P.D("type", "task_complete", "turn_id", "t2"), cz);
                Ok(Tury.Lista("codex-th").Count == 1, "Codex: koniec tury bez znanego początku – bez zgadywania");
                // recenzja Codexa (P2): task_started bez wcześniejszego licznika nie może liczyć od zera
                var plikB = Path.Combine(root, "rollout-bez-bazy.jsonl");
                Tury.ZdarzenieCodex(plikB, "codex-tb", P.D("type", "task_started", "turn_id", "x"), cz);
                Tury.ZdarzenieCodex(plikB, "codex-tb", licz(10000, 8000, 500), cz);
                Tury.ZdarzenieCodex(plikB, "codex-tb", P.D("type", "task_complete", "turn_id", "x"), cz);
                Ok(Tury.Lista("codex-tb").Count == 0, "Codex: tura bez licznika sprzed startu – brak danych, nie 10 500");

                // recenzja Codexa (P2): sygnały Stop przetwarzane razem – każda tura do swojej granicy bajtów
                var zapis2 = Path.Combine(root, "rozmowa-kolejka.jsonl");
                File.WriteAllText(zapis2, "{\"type\":\"user\"}\n", new UTF8Encoding(false));
                Tury.Sygnal("start", "claude-tk", "ses-kolejka", zapis2); System.Threading.Thread.Sleep(15);
                File.AppendAllText(zapis2, Asystent("a", 10, 0, 0, 1), new UTF8Encoding(false)); Tury.Sygnal("stop", "claude-tk", "ses-kolejka", zapis2); System.Threading.Thread.Sleep(15);
                File.AppendAllText(zapis2, "{\"type\":\"user\"}\n", new UTF8Encoding(false)); Tury.Sygnal("start", "claude-tk", "ses-kolejka", zapis2); System.Threading.Thread.Sleep(15);
                File.AppendAllText(zapis2, Asystent("b", 20, 0, 0, 2), new UTF8Encoding(false)); Tury.Sygnal("stop", "claude-tk", "ses-kolejka", zapis2);
                Tury.PrzetworzSygnaly();
                var kol = Tury.Lista("claude-tk");
                Ok(kol.Count == 2 && kol[0].Razem == 11 && kol[1].Razem == 22, "Claude: opóźnione sygnały – tury 11 i 22, nie jedna 33 (" + string.Join(",", kol.Select(x => x.Razem.ToString()).ToArray()) + ")");

                // 6. tokeny zadania: tury adresata od doręczenia do pierwszej tury po odpowiedzi
                var a1 = Tury.DlaOdpowiedzi("claude-th", DateTime.Now.AddHours(-1), DateTime.Now.AddMinutes(-1));
                Ok(a1 != null && a1.Razem == t2[0].Razem && a1.Id == "1", "zadanie: odpowiedź w turze 1 – liczy się tura, w której padła odpowiedź");
                var a = Tury.DlaOdpowiedzi("claude-th", DateTime.Now.AddHours(-1), t2[1].Czas);
                Ok(a != null && a.Razem == t2.Sum(x => x.Razem) && a.Id == "2", "zadanie: suma obu tur do odpowiedzi (" + (a != null ? a.Razem.ToString() : "brak") + ")");
                Ok(Tury.DlaOdpowiedzi("claude-th", DateTime.Now.AddMinutes(1), DateTime.Now.AddMinutes(2)) == null, "zadanie bez tur – brak danych (konsola pokaże „—”)");

                // 7. agent w tle: usage z wyniku claude -p
                var tl = Tury.ZWynikuClaude("log\n{\"type\":\"result\",\"session_id\":\"s1\",\"usage\":{\"input_tokens\":10,\"cache_creation_input_tokens\":38195,\"cache_read_input_tokens\":150593,\"output_tokens\":1166}}\n");
                Ok(tl != null && tl.Razem == 10 + 38195 + 150593 + 1166, "agent w tle: usage z wyniku JSON Claude");

                // 8. wydajność: hook jako program (bez PowerShella)
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 20; i++) Hook("UserPromptSubmit", P.D("session_id", ses, "transcript_path", zapis, "prompt", "<system-reminder>pomiar</system-reminder>"));
                log.AppendLine("INFO  logika hooka w procesie: " + (sw.ElapsedMilliseconds / 20.0).ToString("0.0") + " ms na wywołanie");

                string spoj; P.Stany(out spoj);
                Ok(spoj.StartsWith("spójny"), "dziennik po teście: " + spoj);
            }
            catch (Exception ex) { log.AppendLine("FAIL  wyjątek: " + ex); zle++; }
            log.AppendLine(zle == 0 ? "WYNIK: wszystkie testy PASS" : "WYNIK: FAIL " + zle);
            Console.Write(log.ToString());
            try { if (root.Length > 0) File.WriteAllText(Path.Combine(root, "test-tury.txt"), log.ToString(), new UTF8Encoding(false)); } catch { }
            return zle;
        }
    }
}
