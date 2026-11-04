// ECXP – hooki Claude Code bez PowerShella: ecxp-mcp.exe hook SessionStart | UserPromptSubmit | Stop.
// Zasada jednego właściciela: hook nigdy nie odbiera ani nie potwierdza wiadomości za sesję – tylko ją powiadamia.
//  * SessionStart: przypomnienie o nieodebranych wiadomościach ECXP (kontekst modelu).
//  * UserPromptSubmit: lustro wpisu operatora do stan\wejscia (zasada 1:1), przypomnienie, początek tury (tokeny).
//  * Stop: koniec tury (tokeny); gdy na sesję czekają wiadomości – jedno „decision: block”, żeby je odebrała
//    przed zakończeniem (stop_hook_active chroni przed pętlą).
// Sesje spoza ECXP: hook nic nie zapisuje i nic nie wypisuje. Błąd hooka nigdy nie blokuje pracy (kod 0).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Ecxp
{
    static class Hooki
    {
        static readonly Regex RePakiet = new Regex(@"Twoja nazwa w ECXP: (claude-[a-z0-9-]+)");
        static readonly Regex RePrzypisz = new Regex(@"""name"":""mcp__ecxp__ecxp_przypisz""[^}]*?""ja"":""(claude-[a-z0-9-]+)""");
        static readonly Regex ReSystemowe = new Regex(@"^\s*<(task-notification|system-reminder|local-command|command-name|command-message|bash-input|bash-stdout|user-prompt-submit-hook)");
        static readonly Regex ReDo = new Regex(@"""do""\s*:\s*""([^""]*)""");
        static readonly Regex ReTemat = new Regex(@"""temat""\s*:\s*""((?:[^""\\]|\\.)*)""");

        public static int Uruchom(string zdarzenie, TextReader we, TextWriter wy)
        {
            try
            {
                var js = P.Js(); js.MaxJsonLength = int.MaxValue;
                var d = js.Deserialize<Dictionary<string, object>>(we.ReadToEnd()) ?? new Dictionary<string, object>();
                var sesja = P.S(d, "session_id"); var zapis = P.S(d, "transcript_path");
                if (sesja.Length == 0) return 0;
                bool budzenie = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ECXP_DORECZANIE"));   // tura budzenia ze stosu
                var tekst = P.S(d, "prompt");
                var nazwa = Nazwa(sesja, zapis, zdarzenie == "UserPromptSubmit" ? tekst : "");
                if (nazwa.Length == 0) return 0;
                switch (zdarzenie)
                {
                    case "SessionStart":
                        {
                            var czeka = Czekajace(nazwa);
                            if (czeka.Count > 0) wy.WriteLine(Przypomnienie(nazwa, czeka));
                            return 0;
                        }
                    case "UserPromptSubmit":
                        {
                            if (budzenie || tekst.Length == 0 || ReSystemowe.IsMatch(tekst)) return 0;
                            Tury.Sygnal("start", nazwa, sesja, zapis);
                            var czeka = Czekajace(nazwa);
                            if (czeka.Count > 0) wy.WriteLine(Przypomnienie(nazwa, czeka));
                            if (RePakiet.IsMatch(tekst)) return 0;   // pakiet startowy nie jest wpisem dla drugiej strony
                            var dir = Path.Combine(P.Root, "stan", "wejscia"); Directory.CreateDirectory(dir);
                            var plik = Path.Combine(dir, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + sesja.Substring(0, Math.Min(8, sesja.Length)) + ".json");
                            File.WriteAllText(plik + ".tmp", P.Js().Serialize(P.D("strona", "claude", "nazwa", nazwa, "sesja", sesja, "tekst", tekst, "czas", DateTime.Now.ToString("o"))), new UTF8Encoding(false));
                            File.Move(plik + ".tmp", plik);
                            return 0;
                        }
                    case "Stop":
                        {
                            Tury.Sygnal("stop", nazwa, sesja, zapis);
                            if (budzenie || P.B(d, "stop_hook_active")) return 0;
                            var czeka = Czekajace(nazwa);
                            if (czeka.Count > 0)
                                wy.WriteLine(P.Js().Serialize(P.D("decision", "block", "reason", Przypomnienie(nazwa, czeka))));
                            return 0;
                        }
                }
            }
            catch { }
            return 0;
        }

        static string Przypomnienie(string nazwa, List<string> czeka)
        {
            return "[ECXP] Na adres " + nazwa + " czeka nieodebranych wiadomości: " + czeka.Count + " (" + string.Join(", ", czeka.Take(6)) +
                "). Odbierz je narzędziem ecxp_odbierz(ja=" + nazwa + ") i potwierdź każdą – zanim zajmiesz się resztą.";
        }

        // Tematy wiadomości czekających na ten adres – tylko pole „do” i „temat”, bez parsowania całych plików.
        public static List<string> Czekajace(string nazwa)
        {
            var wynik = new List<string>();
            var dir = Path.Combine(P.Root, P.Folder(nazwa));
            if (!Directory.Exists(dir)) return wynik;
            foreach (var f in Directory.GetFiles(dir, "*.json").OrderBy(x => x))
            {
                try
                {
                    var t = File.ReadAllText(f, Encoding.UTF8);
                    var m = ReDo.Match(t);
                    if (!m.Success || !P.DoMnie(nazwa, m.Groups[1].Value)) continue;
                    var tm = ReTemat.Match(t);
                    wynik.Add(tm.Success ? Regex.Unescape(tm.Groups[1].Value) : Path.GetFileNameWithoutExtension(f));
                }
                catch { }
            }
            return wynik;
        }

        // Nazwa ECXP sesji: 1) trasa z tym identyfikatorem sesji (ecxp_przypisz), 2) pamięć hooka, 3) pakiet startowy we wpisie,
        // 4) zapis rozmowy (pakiet wklejony przez operatora albo własne ecxp_przypisz) – najwyżej co 5 minut.
        public static string Nazwa(string sesja, string zapis, string tekst)
        {
            var t = Siec.Trasy().Where(x => x.Aktywna && x.Strona == "claude" && (x.Sesja ?? "").Replace("local_", "") == sesja).Select(x => x.Nazwa).FirstOrDefault();
            if (!string.IsNullOrEmpty(t)) return t;
            var cache = Path.Combine(P.Root, "stan", "hook-sesje"); Directory.CreateDirectory(cache);
            var plikC = Path.Combine(cache, Path.GetFileName(sesja) + ".txt");
            var pamiec = File.Exists(plikC) ? File.ReadAllText(plikC).Trim() : "";
            if (Regex.IsMatch(pamiec, "^claude-[a-z0-9-]+$")) return pamiec;
            var m = RePakiet.Match(tekst ?? "");
            if (!m.Success)
            {
                long ostatnio = 0;
                var mp = Regex.Match(pamiec, @"^-\|(\d+)$"); if (mp.Success) long.TryParse(mp.Groups[1].Value, out ostatnio);
                if (DateTime.UtcNow.Ticks - ostatnio <= 3000000000L) return "";
                if (zapis.Length > 0 && File.Exists(zapis))
                    using (var fs = new FileStream(zapis, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var r = new StreamReader(fs, Encoding.UTF8))
                    {
                        string l;
                        while ((l = r.ReadLine()) != null)
                        {
                            if (l.IndexOf("\"type\":\"user\"", StringComparison.Ordinal) >= 0 && l.IndexOf("\"tool_use_id\"", StringComparison.Ordinal) < 0)
                            { m = RePakiet.Match(l); if (m.Success) break; }
                            else if (l.IndexOf("\"type\":\"assistant\"", StringComparison.Ordinal) >= 0 && l.IndexOf("mcp__ecxp__ecxp_przypisz", StringComparison.Ordinal) >= 0)
                            { m = RePrzypisz.Match(l); if (m.Success) break; }
                        }
                    }
            }
            if (m.Success) { File.WriteAllText(plikC, m.Groups[1].Value); return m.Groups[1].Value; }
            File.WriteAllText(plikC, "-|" + DateTime.UtcNow.Ticks);
            return "";
        }
    }
}
