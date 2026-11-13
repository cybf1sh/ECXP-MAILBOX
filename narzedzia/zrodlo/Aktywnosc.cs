// ECXP – „Co robi agent”: bieżące działania agentów (bez modelu AI, tylko zapis i odczyt zdarzeń).
//  * Agenci w tle: stos zapisuje zdarzenia ciepłego procesu (Claude stream-json, Codex app-server) do
//    stan\agenci\<nazwa>.na-zywo.jsonl – najwyżej ostatnie ~200 wpisów, każdy jedna linia JSON {czas, rodzaj, opis}.
//  * Sesje w aplikacjach (Claude Code, Codex Desktop): konsola czyta tylko końcówkę zapisu rozmowy sesji
//    (~/.claude/projects/*/<sesja>.jsonl, ~/.codex/sessions/**/rollout-*-<wątek>.jsonl). Nic nie jest tam zapisywane.
// Opisy są krótkie (do 140 znaków): rodzaj działania i jego cel (plik, polecenie, zapytanie). Treść plików i wyniki
// poleceń nie są zapisywane; wartości wyglądające na hasła lub klucze są maskowane.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Ecxp
{
    class WpisAktywnosci
    {
        public DateTime Czas; public string Rodzaj = "", Opis = "";
    }

    class StanAktywnosci
    {
        public string Nazwa = "", Zrodlo = "", Plik = "", Praca = "", Uwaga = "";
        public bool Pracuje; public DateTime? Od;
        public List<WpisAktywnosci> Wpisy = new List<WpisAktywnosci>();
        public WpisAktywnosci Ostatni { get { return Wpisy.Count > 0 ? Wpisy[Wpisy.Count - 1] : null; } }
    }

    static class Aktywnosc
    {
        public const int Maks = 200, MaksOpisu = 140;
        static string Dir { get { return Path.Combine(P.Root, "stan", "agenci"); } }
        public static string Plik(string nazwa) { return Path.Combine(Dir, Path.GetFileName(nazwa) + ".na-zywo.jsonl"); }
        static readonly Dictionary<string, object> blokady = new Dictionary<string, object>();

        // ---------- zapis (stos) ----------
        public static void Zapisz(string nazwa, string rodzaj, string opis)
        {
            if (string.IsNullOrEmpty(nazwa) || string.IsNullOrEmpty(opis)) return;
            object bl; lock (blokady) { if (!blokady.TryGetValue(nazwa, out bl)) blokady[nazwa] = bl = new object(); }
            try
            {
                lock (bl)
                {
                    Directory.CreateDirectory(Dir);
                    var p = Plik(nazwa);
                    var linia = P.Js().Serialize(P.D("czas", P.Teraz(), "rodzaj", rodzaj ?? "", "opis", Skroc(opis))) + "\n";
                    File.AppendAllText(p, linia, new UTF8Encoding(false));
                    // przycinanie: po przekroczeniu ~2× limitu zostaje ostatnie Maks wpisów
                    if (new FileInfo(p).Length > 120 * Maks * 2)
                    {
                        var l = File.ReadAllLines(p, Encoding.UTF8);
                        File.WriteAllLines(p + ".tmp", l.Skip(Math.Max(0, l.Length - Maks)), new UTF8Encoding(false));
                        File.Copy(p + ".tmp", p, true); File.Delete(p + ".tmp");
                    }
                }
            }
            catch { }   // podgląd nigdy nie przerywa pracy agenta
        }

        // Zdarzenie strumienia Claude (-p --output-format stream-json).
        public static void ZClaude(string nazwa, Dictionary<string, object> z)
        {
            foreach (var w in WpisyClaude(z)) Zapisz(nazwa, w.Rodzaj, w.Opis);
        }

        // Powiadomienie Codex app-server (JSON-RPC bez id).
        public static void ZCodex(string nazwa, string metoda, Dictionary<string, object> pa)
        {
            var w = WpisCodexAppServer(metoda, pa);
            if (w != null) Zapisz(nazwa, w.Rodzaj, w.Opis);
        }

        // ---------- opisy ----------
        static readonly Regex ReSekret = new Regex(@"(?i)\b(password|passwd|pwd|haslo|hasło|token|secret|api[_-]?key|apikey|authorization|bearer|client[_-]?secret)\b(\s*[:=]\s*|\s+)(""[^""]*""|'[^']*'|\S+)");
        static readonly Regex ReToken = new Regex(@"(?i)\b(bearer|basic|token)\s+[A-Za-z0-9._~+/=-]{6,}");
        public static string Maskuj(string s) { return ReSekret.Replace(ReToken.Replace(s ?? "", "$1 ***"), "$1$2***"); }

        public static string Skroc(string s, int maks = MaksOpisu)
        {
            s = Regex.Replace(Maskuj(s ?? ""), @"\s+", " ").Trim();
            return s.Length > maks ? s.Substring(0, maks - 1) + "…" : s;
        }

        // Ścieżka: końcówka (nazwa pliku z katalogiem nadrzędnym), bez długiego prefiksu.
        public static string Sciezka(string p)
        {
            p = (p ?? "").Trim().Trim('"');
            if (p.Length <= 70) return p;
            var cz = p.Replace('/', '\\').Split('\\').Where(x => x.Length > 0).ToList();
            return cz.Count >= 2 ? "…\\" + cz[cz.Count - 2] + "\\" + cz[cz.Count - 1] : "…" + p.Substring(p.Length - 68);
        }

        static string Pole(Dictionary<string, object> d, params string[] klucze)
        {
            if (d == null) return "";
            foreach (var k in klucze) { var v = P.S(d, k); if (v.Length > 0) return v; }
            return "";
        }

        // Narzędzie Claude Code (nazwa + argumenty) → krótki opis.
        public static string OpisNarzedzia(string nazwa, Dictionary<string, object> we)
        {
            nazwa = nazwa ?? "";
            switch (nazwa)
            {
                case "Read": case "NotebookRead": return "📄 czyta " + Sciezka(Pole(we, "file_path", "notebook_path", "path"));
                case "Write": return "✏ zapisuje " + Sciezka(Pole(we, "file_path", "path"));
                case "Edit": case "MultiEdit": case "NotebookEdit": return "✏ edytuje " + Sciezka(Pole(we, "file_path", "notebook_path", "path"));
                case "Bash": case "PowerShell": return "▶ uruchamia " + Polecenie(Pole(we, "command"));
                case "Grep": return "🔎 szuka „" + Pole(we, "pattern") + "”" + (Pole(we, "path", "glob").Length > 0 ? " w " + Sciezka(Pole(we, "path", "glob")) : "");
                case "Glob": return "🔎 szuka plików " + Pole(we, "pattern");
                case "WebFetch": return "🌐 pobiera " + Pole(we, "url");
                case "WebSearch": return "🌐 szuka w sieci „" + Pole(we, "query") + "”";
                case "Task": case "Agent": return "🤖 podagent: " + Pole(we, "description", "prompt");
                case "TodoWrite": return "📝 aktualizuje plan";
                case "Skill": return "📚 umiejętność " + Pole(we, "skill", "name");
            }
            if (nazwa.StartsWith("mcp__ecxp__", StringComparison.Ordinal)) return "📨 ECXP: " + nazwa.Substring(11);
            if (nazwa.StartsWith("mcp__", StringComparison.Ordinal))
            {
                var cz = nazwa.Substring(5).Split(new[] { "__" }, 2, StringSplitOptions.None);
                return "🔌 " + cz[0] + (cz.Length > 1 ? ": " + cz[1] : "");
            }
            return "🔧 " + nazwa;
        }

        // Zdarzenie Claude (strumień albo linia zapisu rozmowy) → wpisy.
        public static List<WpisAktywnosci> WpisyClaude(Dictionary<string, object> z)
        {
            var wy = new List<WpisAktywnosci>();
            if (z == null) return wy;
            var typ = P.S(z, "type");
            var czas = CzasZ(P.S(z, "timestamp"));
            Action<string, string> dodaj = (r, o) => wy.Add(new WpisAktywnosci { Czas = czas, Rodzaj = r, Opis = Skroc(o) });
            if (typ == "system" && P.S(z, "subtype") == "init") { dodaj("start", "▶ start sesji"); return wy; }
            if (typ == "result") { dodaj(P.B(z, "is_error") ? "blad" : "koniec", P.B(z, "is_error") ? "⚠ koniec tury z błędem" : "✓ koniec tury"); return wy; }
            var msg = z.ContainsKey("message") ? z["message"] as Dictionary<string, object> : null;
            if (msg == null) return wy;
            var tresc = msg.ContainsKey("content") ? msg["content"] : null;
            if (typ == "user")
            {
                var s = tresc as string;
                if (s != null && s.Trim().Length > 0 && !s.TrimStart().StartsWith("<")) dodaj("wpis", "📥 wpis: " + s);
                return wy;
            }
            if (typ != "assistant") return wy;
            foreach (var o in (tresc as System.Collections.IEnumerable) ?? new object[0])
            {
                var c = o as Dictionary<string, object>;
                if (c == null) continue;
                switch (P.S(c, "type"))
                {
                    case "tool_use": dodaj("narzedzie", OpisNarzedzia(P.S(c, "name"), c.ContainsKey("input") ? c["input"] as Dictionary<string, object> : null)); break;
                    case "text": if (P.S(c, "text").Trim().Length > 0) dodaj("pisze", "✍ pisze: " + P.S(c, "text")); break;
                    case "thinking": dodaj("mysli", "💭 myśli"); break;
                }
            }
            if (P.S(msg, "stop_reason") == "end_turn") dodaj("koniec", "✓ koniec tury");
            return wy;
        }

        // Codex app-server: item/started, turn/started, turn/completed.
        public static WpisAktywnosci WpisCodexAppServer(string metoda, Dictionary<string, object> pa)
        {
            if (metoda == "turn/started") return new WpisAktywnosci { Czas = DateTime.Now, Rodzaj = "start", Opis = "▶ początek tury" };
            if (metoda == "turn/completed")
            {
                var tu = pa != null && pa.ContainsKey("turn") ? pa["turn"] as Dictionary<string, object> : null;
                var st = tu != null ? P.S(tu, "status") : "";
                return new WpisAktywnosci { Czas = DateTime.Now, Rodzaj = st == "completed" || st == "" ? "koniec" : "blad", Opis = st == "completed" || st == "" ? "✓ koniec tury" : "⚠ tura: " + st };
            }
            if (metoda != "item/started" || pa == null || !pa.ContainsKey("item")) return null;
            var it = pa["item"] as Dictionary<string, object>;
            if (it == null) return null;
            string opis;
            switch (P.S(it, "type"))
            {
                case "commandExecution": opis = "▶ uruchamia " + KomendaCodex(it.ContainsKey("command") ? it["command"] : null); break;
                case "fileChange":
                    var sc = ((it.ContainsKey("changes") ? it["changes"] : null) as System.Collections.IEnumerable ?? new object[0]).Cast<object>()
                        .Select(x => x as Dictionary<string, object>).Where(x => x != null).Select(x => Sciezka(P.S(x, "path"))).Where(x => x.Length > 0).ToList();
                    opis = "✏ edytuje " + (sc.Count == 0 ? "pliki" : string.Join(", ", sc.Take(3)) + (sc.Count > 3 ? " (+" + (sc.Count - 3) + ")" : "")); break;
                case "mcpToolCall": opis = P.S(it, "server") == "ecxp" ? "📨 ECXP: " + P.S(it, "tool") : "🔌 " + P.S(it, "server") + ": " + P.S(it, "tool"); break;
                case "webSearch": opis = "🌐 szuka w sieci „" + P.S(it, "query") + "”"; break;
                case "reasoning": opis = "💭 myśli"; break;
                case "agentMessage": opis = "✍ pisze odpowiedź"; break;
                case "imageView": opis = "🖼 ogląda obraz"; break;
                case "userMessage": return null;
                default: opis = "🔧 " + P.S(it, "type"); break;
            }
            return new WpisAktywnosci { Czas = DateTime.Now, Rodzaj = P.S(it, "type") == "agentMessage" ? "pisze" : "narzedzie", Opis = Skroc(opis) };
        }

        // „"C:\…\pwsh.exe" -Command '…'” / „bash -lc "…"” → samo polecenie (bez programu powłoki i cudzysłowów wokół).
        static readonly Regex RePowloka = new Regex(@"^\s*(?:""[^""]*(?:pwsh|powershell|cmd|bash|sh)(?:\.exe)?""|\S*(?:pwsh|powershell|cmd|bash|sh)(?:\.exe)?)\s+(?:-NoProfile\s+|-NoLogo\s+|-NonInteractive\s+)*(?:-Command|-c|-lc|/c)\s+(.*)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        public static string BezPowloki(string s)
        {
            var m = RePowloka.Match(s ?? "");
            if (!m.Success) return s ?? "";
            var k = m.Groups[1].Value.Trim();
            if (k.Length >= 2 && (k[0] == '\'' || k[0] == '"') && k[k.Length - 1] == k[0]) k = k.Substring(1, k.Length - 2);
            return k.Replace("'\"'\"'", "'").Replace("'\"'", "'").Replace("\"'", "'");
        }

        static string KomendaCodex(object c)
        {
            var s = c as string;
            if (s != null) return BezPowloki(s);
            var l = (c as System.Collections.IEnumerable ?? new object[0]).Cast<object>().Select(x => Convert.ToString(x)).ToList();
            // ["powershell.exe","-Command","…"] / ["bash","-lc","…"] – liczy się samo polecenie
            if (l.Count >= 3 && Regex.IsMatch(l[1], "^-(lc|c|Command)$", RegexOptions.IgnoreCase)) return l[l.Count - 1];
            return string.Join(" ", l);
        }

        static readonly Regex ReNarzedzieExec = new Regex(@"tools\.([A-Za-z0-9_]+)\s*\(");
        // Wieloliniowy skrypt: pierwsza znacząca linia i liczba pozostałych (bez linii ustawień powłoki).
        public static string Polecenie(string k)
        {
            var l = (k ?? "").Replace("\r", "").Split('\n').Select(x => x.Trim())
                .Where(x => x.Length > 0 && !Regex.IsMatch(x, @"^(\$ErrorActionPreference|\$ProgressPreference|Set-StrictMode|set -e|#)", RegexOptions.IgnoreCase)).ToList();
            if (l.Count == 0) return (k ?? "").Trim();
            return l[0] + (l.Count > 1 ? " (+" + (l.Count - 1) + " linii)" : "");
        }

        static readonly Regex ReKomenda = new Regex(@"\b(?:command|cmd)\s*:\s*(?:String\.raw)?(\[[^\]]*\]|""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*'|`[^`]*`)");
        static readonly Regex RePlikPatch = new Regex(@"\*\*\* (?:Update|Add|Delete) File: ([^\r\n\\""]+)");

        // Codex Desktop: wywołanie „exec” (kod JS wołający narzędzia) → opis pierwszego znaczącego narzędzia.
        public static string OpisCodexExec(string kod)
        {
            kod = kod ?? "";
            var nazwy = ReNarzedzieExec.Matches(kod).Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
            if (nazwy.Count == 0) return "🔧 skrypt narzędzi";
            var opisy = new List<string>();
            foreach (var n in nazwy.Take(3))
            {
                if (Regex.IsMatch(n, "^(shell|shell_command|exec_command|run|local_shell|powershell)$"))
                {
                    var m = ReKomenda.Match(kod);
                    var k = m.Success ? m.Groups[1].Value : "";
                    if (k.StartsWith("[")) k = string.Join(" ", Regex.Matches(k, @"""((?:[^""\\]|\\.)*)""").Cast<Match>().Select(x => x.Groups[1].Value).Where(x => !Regex.IsMatch(x, "^(bash|sh|powershell(\\.exe)?|pwsh|cmd(\\.exe)?|-lc|-c|-Command|/c)$", RegexOptions.IgnoreCase)));
                    else if (k.Length >= 2) k = k.Substring(1, k.Length - 2);
                    opisy.Add("▶ uruchamia " + Polecenie(k.Replace("\\\\", "\u0001").Replace("\\\"", "\"").Replace("\\'", "'").Replace("\\n", "\n").Replace("\\t", " ").Replace("\u0001", "\\")));
                }
                else if (n == "apply_patch")
                {
                    var pl = RePlikPatch.Matches(kod).Cast<Match>().Select(x => Sciezka(x.Groups[1].Value.Trim())).Distinct().ToList();
                    opisy.Add("✏ edytuje " + (pl.Count == 0 ? "pliki" : string.Join(", ", pl.Take(3))));
                }
                else if (n.StartsWith("mcp__ecxp__", StringComparison.Ordinal)) opisy.Add("📨 ECXP: " + n.Substring(11));
                else if (n.StartsWith("mcp__", StringComparison.Ordinal)) { var cz = n.Substring(5).Split(new[] { "__" }, 2, StringSplitOptions.None); opisy.Add("🔌 " + cz[0] + (cz.Length > 1 ? ": " + cz[1] : "")); }
                else if (Regex.IsMatch(n, "web_search|search_web|search", RegexOptions.IgnoreCase)) opisy.Add("🌐 szuka w sieci");
                else if (n == "view_image") opisy.Add("🖼 ogląda obraz");
                else if (n == "update_plan") opisy.Add("📝 aktualizuje plan");
                else opisy.Add("🔧 " + n);
            }
            return string.Join(" · ", opisy);
        }

        // Linia zapisu rozmowy Codex (rollout-*.jsonl) → wpis albo null.
        public static WpisAktywnosci WpisCodexZapisu(Dictionary<string, object> z)
        {
            if (z == null) return null;
            var pl = z.ContainsKey("payload") ? z["payload"] as Dictionary<string, object> : null;
            if (pl == null) return null;
            var czas = CzasZ(P.S(z, "timestamp"));
            var typ = P.S(z, "type"); var pt = P.S(pl, "type");
            Func<string, string, WpisAktywnosci> w = (r, o) => new WpisAktywnosci { Czas = czas, Rodzaj = r, Opis = Skroc(o) };
            if (typ == "event_msg")
            {
                if (pt == "task_started") return w("start", "▶ początek tury");
                if (pt == "task_complete") return w("koniec", "✓ koniec tury");
                if (pt == "turn_aborted") return w("blad", "⚠ tura przerwana");
                return null;
            }
            if (typ != "response_item") return null;
            switch (pt)
            {
                case "custom_tool_call": return w("narzedzie", P.S(pl, "name") == "exec" ? OpisCodexExec(P.S(pl, "input")) : "🔧 " + P.S(pl, "name"));
                case "function_call":
                    var n = P.S(pl, "name");
                    if (n == "wait" || n == "sleep") return w("czeka", "⏳ czeka");
                    if (n == "shell" || n == "shell_command" || n == "exec_command") return w("narzedzie", OpisCodexExec("tools." + n + "(" + P.S(pl, "arguments") + ")"));
                    return w("narzedzie", n.StartsWith("mcp__ecxp__") ? "📨 ECXP: " + n.Substring(11) : "🔧 " + n);
                case "local_shell_call": return w("narzedzie", "▶ uruchamia polecenie");
                case "web_search_call": return w("narzedzie", "🌐 szuka w sieci");
                case "reasoning": return w("mysli", "💭 myśli");
                case "message":
                    var rola = P.S(pl, "role");
                    if (rola == "assistant") return w("pisze", "✍ pisze odpowiedź");
                    if (rola == "user")
                    {
                        var t = string.Join(" ", ((pl.ContainsKey("content") ? pl["content"] : null) as System.Collections.IEnumerable ?? new object[0]).Cast<object>()
                            .Select(x => x as Dictionary<string, object>).Where(x => x != null).Select(x => P.S(x, "text")));
                        if (t.Trim().Length > 0 && !t.TrimStart().StartsWith("<")) return w("wpis", "📥 wpis: " + t);
                    }
                    return null;
            }
            return null;
        }

        static DateTime CzasZ(string s)
        {
            DateTimeOffset d;
            return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out d) ? d.LocalDateTime : DateTime.Now;
        }

        // ---------- odczyt (konsola) ----------
        // Ostatnie linie pliku dopisywanego przez inny proces (bez blokowania zapisu).
        public static List<string> Ogon(string plik, int bajtow)
        {
            var l = new List<string>();
            try
            {
                using (var fs = new FileStream(plik, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    bool przyciete = fs.Length > bajtow;
                    if (przyciete) fs.Seek(-bajtow, SeekOrigin.End);
                    using (var r = new StreamReader(fs, Encoding.UTF8))
                    {
                        if (przyciete) r.ReadLine();     // pierwsza linia urwana
                        string s; while ((s = r.ReadLine()) != null) if (s.Length > 0) l.Add(s);
                    }
                }
            }
            catch { }
            return l;
        }

        static List<WpisAktywnosci> CzytajWlasne(string nazwa)
        {
            var js = P.Js(); var wy = new List<WpisAktywnosci>();
            foreach (var s in Ogon(Plik(nazwa), 64 * 1024))
                try { var d = js.Deserialize<Dictionary<string, object>>(s); wy.Add(new WpisAktywnosci { Czas = CzasZ(P.S(d, "czas")), Rodzaj = P.S(d, "rodzaj"), Opis = P.S(d, "opis") }); } catch { }
            return wy;
        }

        public static List<WpisAktywnosci> ZZapisuClaude(IEnumerable<string> linie)
        {
            var js = P.Js(); js.MaxJsonLength = int.MaxValue; var wy = new List<WpisAktywnosci>();
            foreach (var s in linie)
            {
                if (s.IndexOf("\"type\":\"assistant\"", StringComparison.Ordinal) < 0 && s.IndexOf("\"type\":\"user\"", StringComparison.Ordinal) < 0) continue;
                try { wy.AddRange(WpisyClaude(js.Deserialize<Dictionary<string, object>>(s))); } catch { }
            }
            return wy;
        }

        public static List<WpisAktywnosci> ZZapisuCodex(IEnumerable<string> linie)
        {
            var js = P.Js(); js.MaxJsonLength = int.MaxValue; var wy = new List<WpisAktywnosci>();
            foreach (var s in linie)
            {
                if (s.IndexOf("\"response_item\"", StringComparison.Ordinal) < 0 && s.IndexOf("\"event_msg\"", StringComparison.Ordinal) < 0) continue;
                try { var w = WpisCodexZapisu(js.Deserialize<Dictionary<string, object>>(s)); if (w != null) wy.Add(w); } catch { }
            }
            return wy;
        }

        // Pliki zapisów rozmów: wynik wyszukiwania pamiętany 60 s (także brak), żeby odświeżanie co sekundę nie przeszukiwało dysku.
        static readonly Dictionary<string, KeyValuePair<DateTime, string>> pamiecPlikow = new Dictionary<string, KeyValuePair<DateTime, string>>();
        static string Znajdz(string klucz, Func<string> szukaj)
        {
            lock (pamiecPlikow)
            {
                KeyValuePair<DateTime, string> kv;
                if (pamiecPlikow.TryGetValue(klucz, out kv) && (kv.Value != null && File.Exists(kv.Value) || DateTime.Now - kv.Key < TimeSpan.FromSeconds(60))) return kv.Value;
            }
            string p = null; try { p = szukaj(); } catch { }
            lock (pamiecPlikow) pamiecPlikow[klucz] = new KeyValuePair<DateTime, string>(DateTime.Now, p);
            return p;
        }

        public static string DomClaude()
        {
            var e = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
            return !string.IsNullOrEmpty(e) ? e : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        }
        public static string DomCodex()
        {
            var e = Environment.GetEnvironmentVariable("CODEX_HOME");
            return !string.IsNullOrEmpty(e) ? e : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        }

        static string ZapisClaude(string sesja)
        {
            sesja = (sesja ?? "").Replace("local_", "");
            if (!Regex.IsMatch(sesja, "^[0-9a-fA-F-]{8,}$")) return null;
            return Znajdz("claude:" + sesja, () =>
            {
                var d = Path.Combine(DomClaude(), "projects");
                return Directory.Exists(d) ? Directory.GetDirectories(d).Select(x => Path.Combine(x, sesja + ".jsonl")).FirstOrDefault(File.Exists) : null;
            });
        }

        static string ZapisCodex(string watek)
        {
            if (!Regex.IsMatch(watek ?? "", "^[0-9a-fA-F-]{8,}$")) return null;
            return Znajdz("codex:" + watek, () =>
            {
                var d = Path.Combine(DomCodex(), "sessions");
                return Directory.Exists(d) ? Directory.EnumerateFiles(d, "rollout-*" + watek + ".jsonl", SearchOption.AllDirectories).FirstOrDefault() : null;
            });
        }

        // Sesja Claude Code przypisana do nazwy przez hook ECXP (stan\hook-sesje\<sesja>.txt), najnowsza.
        static string SesjaZHooka(string nazwa)
        {
            try
            {
                var d = Path.Combine(P.Root, "stan", "hook-sesje");
                if (!Directory.Exists(d)) return null;
                return new DirectoryInfo(d).GetFiles("*.txt").OrderByDescending(f => f.LastWriteTime).Take(400)
                    .Where(f => { try { return File.ReadAllText(f.FullName).Trim() == nazwa; } catch { return false; } })
                    .Select(f => Path.GetFileNameWithoutExtension(f.Name)).FirstOrDefault();
            }
            catch { return null; }
        }

        // Stan agenta: tło (własne zdarzenia stosu) albo sesja w aplikacji (końcówka zapisu rozmowy).
        public static StanAktywnosci Podglad(string nazwa)
        {
            var st = new StanAktywnosci { Nazwa = nazwa };
            var rec = Pliki.Czytaj(Path.Combine(Dir, Path.GetFileName(nazwa) + ".json"));
            // sesja w aplikacji (trasa z prawdziwym identyfikatorem sesji, nie „tlo:”) ma pierwszeństwo przed nieaktywnym agentem w tle
            var trasa = Siec.Trasy().Where(x => x.Nazwa == nazwa && (x.Sesja ?? "").Length > 0 && !x.Sesja.StartsWith("tlo:"))
                .OrderByDescending(x => x.Aktywna).ThenByDescending(x => x.Od).FirstOrDefault();
            if (P.S(rec, "strona").Length > 0 && P.S(rec, "katalog").Length > 0 && (P.B(rec, "aktywny") || trasa == null))
            {
                st.Zrodlo = "tlo"; st.Plik = Plik(nazwa);
                st.Wpisy = CzytajWlasne(nazwa);
                var od = P.S(rec, "tura_od");
                DateTimeOffset t;
                if (od.Length > 0 && Agenci.TuraZyje(rec) && DateTimeOffset.TryParse(od, CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) { st.Pracuje = true; st.Od = t.LocalDateTime; }
                st.Praca = P.S(rec, "tura_praca");
                if (st.Wpisy.Count == 0) st.Uwaga = "Brak zapisanych działań – pojawią się przy następnej turze (w trybie zastępczym widać tylko początek i koniec tury).";
                return st;
            }
            var strona = nazwa.StartsWith("codex-") ? "codex" : nazwa.StartsWith("claude-") ? "claude" : trasa != null ? trasa.Strona : "";
            string plik = null;
            if (strona == "claude")
            {
                st.Zrodlo = "claude";
                plik = trasa != null ? ZapisClaude(trasa.Sesja) : null;
                if (plik == null) plik = ZapisClaude(SesjaZHooka(nazwa));
                if (plik != null) st.Wpisy = ZZapisuClaude(Ogon(plik, 512 * 1024));
            }
            else if (strona == "codex")
            {
                st.Zrodlo = "codex";
                plik = trasa != null ? ZapisCodex(trasa.Sesja) : null;
                if (plik != null) st.Wpisy = ZZapisuCodex(Ogon(plik, 512 * 1024));
            }
            st.Plik = plik ?? "";
            if (st.Wpisy.Count > Maks) st.Wpisy = st.Wpisy.Skip(st.Wpisy.Count - Maks).ToList();
            if (plik == null) { st.Uwaga = strona.Length == 0 ? "Nieznany agent." : "Nie znaleziono zapisu rozmowy tej sesji (sesja nie zgłosiła identyfikatora albo zapis jest na innym stanowisku)."; return st; }
            // sesja w aplikacji: pracuje, gdy ostatnie zdarzenie to nie koniec tury i było niedawno
            var ost = st.Ostatni;
            if (ost != null && ost.Rodzaj != "koniec" && ost.Rodzaj != "blad" && DateTime.Now - ost.Czas < TimeSpan.FromMinutes(10))
            {
                st.Pracuje = true;
                var start = st.Wpisy.LastOrDefault(x => x.Rodzaj == "start" || x.Rodzaj == "wpis");
                st.Od = start != null ? start.Czas : ost.Czas;
            }
            return st;
        }

        // Jedna linia do listy rozmów: „uruchamia pytest… (1 min)” – tylko gdy agent właśnie pracuje (prefiks dodaje konsola).
        public static string Teraz(StanAktywnosci st)
        {
            if (st == null || !st.Pracuje) return "";
            var o = st.Wpisy.LastOrDefault(x => x.Rodzaj != "koniec" && x.Rodzaj != "start");
            if (o == null) return st.Od.HasValue ? "pracuje od " + P.Wiek(DateTime.Now - st.Od.Value) : "pracuje";
            var opis = Regex.Replace(o.Opis, @"^\S+\s", "");   // bez ikony
            if (opis.Length > 60) opis = opis.Substring(0, 59) + "…";
            return opis + " (" + P.Wiek(DateTime.Now - o.Czas) + ")";
        }
    }
}
