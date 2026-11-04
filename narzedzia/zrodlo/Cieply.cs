// ECXP – ciepli agenci w tle: jeden stale działający proces na agenta, gotowy, zanim przyjdzie zadanie.
//  * Codex: „codex app-server” (JSON-RPC na stdin/stdout). Każde zadanie to nowy wątek (świeży kontekst) w tym samym
//    procesie – wątek zakłada się w kilkadziesiąt milisekund.
//  * Claude: „claude -p” w trybie strumieniowym (stream-json). Po zadaniu proces jest zamykany, a pętla agentów od razu
//    uruchamia nowy – świeży kontekst i znów gotowy.
// Zimny start (Node, konfiguracja, logowanie) dzieje się w tle; na ścieżce zadania zostaje sam czas modelu.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Ecxp
{
    static class Cieply
    {
        public class Wynik { public int Kod = -1; public string Odpowiedz = "", Wyjscie = ""; }

        class Proc
        {
            public string Nazwa = "", Strona = "", Klucz = "";
            public Process Pr; public StreamWriter We;
            public DateTime Ostatnio = DateTime.Now;
            public volatile bool Gotowy, Martwy, Zajety;
            public readonly StringBuilder Dziennik = new StringBuilder();
            // Claude: linie wyniku („type”: „result”)
            public readonly Queue<string> Wyniki = new Queue<string>();
            // Codex: odpowiedzi JSON-RPC i stan tury
            public int Nr;
            public readonly Dictionary<int, Dictionary<string, object>> Odp = new Dictionary<int, Dictionary<string, object>>();
            public string Ost = "", StatusTury, BladTury, CodexHome;
            public bool BladProjektu, TuraWystartowala;
            public readonly ManualResetEvent KoniecTury = new ManualResetEvent(false);
        }

        static readonly Dictionary<string, Proc> procesy = new Dictionary<string, Proc>();

        // Tryb zastępczy: gdy ciepły proces nie działa na tym stanowisku (starsze CLI, brak app-server / stream-json / --name,
        // zablokowana praca bez pytań) – jednorazowe „codex exec” / „claude -p”, wolniej, ale wszędzie, gdzie są Codex i Claude.
        // Po pierwszej porażce strona zostaje w trybie zastępczym do restartu stosu; ECXP_BEZ_CIEPLYCH=1 – zawsze zastępczy.
        static readonly HashSet<string> zastepczy = new HashSet<string>();

        // ---------- „Pokaż sesje agentów”: okno rozmowy otwiera się samo ----------
        // Zgłoszenie trafia do stan\pokaz; okna otwiera zwykły proces stosu (nie proces UAC – aplikacje nie mogą działać
        // jako administrator). Codex: wątek w aplikacji Codex od startu zadania; Claude: okno Claude Code z sesją po zakończeniu.
        public static bool WProcesieUac;
        static string DirPokaz { get { return Path.Combine(P.Root, "stan", "pokaz"); } }
        static void ZglosPokaz(Dictionary<string, object> rec, string strona, string id)
        {
            if (rec == null || !P.B(rec, "pokaz_sesje") || string.IsNullOrEmpty(id)) return;
            try
            {
                Directory.CreateDirectory(DirPokaz);
                Pliki.Zapisz(Path.Combine(DirPokaz, P.S(rec, "nazwa") + "-" + DateTime.Now.Ticks + ".json"),
                    P.D("strona", strona, "id", id, "katalog", P.S(rec, "katalog"), "nazwa", P.S(rec, "nazwa")));
            }
            catch { }
        }
        public static void PokazOczekujace()
        {
            if (WProcesieUac || !Directory.Exists(DirPokaz)) return;
            foreach (var f in Directory.GetFiles(DirPokaz, "*.json"))
            {
                Dictionary<string, object> d;
                try { d = Pliki.Czytaj(f); File.Move(f, f + ".pokazane"); } catch { continue; }
                try
                {
                    if (P.S(d, "strona") == "codex-projekt")
                        CodexProjekt.OtworzProjekt(P.S(d, "katalog"));
                    else if (P.S(d, "strona") == "codex")
                        CodexProjekt.OtworzWatek(P.S(d, "id"));
                    else
                    {
                        var prog = Agenci.Program("claude");
                        if (prog.Length > 0)
                            Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/k \"\"" + prog + "\" --resume " + P.S(d, "id") + "\"")
                                { UseShellExecute = true, WorkingDirectory = Directory.Exists(P.S(d, "katalog")) ? P.S(d, "katalog") : P.Root });
                    }
                    Log("pokazano sesję " + P.S(d, "nazwa") + " (" + P.S(d, "strona") + ")");
                }
                catch (Exception ex) { Log("pokaż sesję " + P.S(d, "nazwa") + ": " + ex.Message); }
            }
        }
        public static bool Zastepczy(string strona)
        {
            if (Environment.GetEnvironmentVariable("ECXP_BEZ_CIEPLYCH") == "1") return true;
            lock (zastepczy) return zastepczy.Contains(strona);
        }
        static void PrzejdzNaZastepczy(string strona, string powod)
        {
            lock (zastepczy) { if (!zastepczy.Add(strona)) return; }
            Log("ciepli agenci: " + strona + " – tryb zastępczy (jednorazowe uruchomienia): " + (powod.Length > 300 ? powod.Substring(0, 300) : powod));
            Zamknij(x => x.StartsWith(strona + "-"));
        }
        public static Action<string> Log = s => { };
        public const int BezczynnoscMin = 30;
        static string Dir { get { return Path.Combine(P.Root, "stan", "agenci"); } }

        // Konfiguracja procesu: zmiana uprawnień, katalogów albo modelu = nowy proces.
        static string Klucz(Dictionary<string, object> rec)
        {
            return string.Join("|", new[] { P.S(rec, "strona"), P.S(rec, "katalog"), P.S(rec, "uprawnienia"), P.S(rec, "podwyzszone_klucz"), P.S(rec, "model"),
                string.Join(";", P.L(rec, "katalogi_zapisu")) });
        }

        static void Dopisz(Proc w, string t)
        {
            lock (w.Dziennik) { if (w.Dziennik.Length > 20000) w.Dziennik.Remove(0, 10000); w.Dziennik.AppendLine(t); }
        }

        // ---------- rozgrzewanie ----------
        public static bool Cieplly(string nazwa) { lock (procesy) { Proc w; return procesy.TryGetValue(nazwa, out w) && !w.Martwy; } }

        // Uruchamia proces agenta w tle, jeśli go nie ma (albo ma inną konfigurację). Nie czeka.
        public static void Rozgrzej(Dictionary<string, object> rec)
        {
            var n = P.S(rec, "nazwa");
            if (Zastepczy(P.S(rec, "strona"))) return;
            lock (procesy)
            {
                Proc w;
                if (procesy.TryGetValue(n, out w))
                {
                    if (!w.Martwy && w.Klucz == Klucz(rec)) return;
                    if (w.Zajety) return;
                    procesy.Remove(n); ZamknijProc(w);
                }
                w = new Proc { Nazwa = n, Strona = P.S(rec, "strona"), Klucz = Klucz(rec) };
                procesy[n] = w;
                var kopia = rec;
                new Thread(() => { try { Uruchom(w, kopia); } catch (Exception ex) { w.Martwy = true; Dopisz(w, "start: " + ex.Message); Log("ciepły " + n + ": " + ex.Message); } })
                    { IsBackground = true, Name = "ecxp-cieply-" + n }.Start();
            }
        }

        static ProcessStartInfo Psi(string prog, string args, string katalog)
        {
            var psi = prog.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? new ProcessStartInfo(prog, args)
                : new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /s /c \"\"" + prog + "\" " + args + "\"");
            psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.WorkingDirectory = katalog;
            psi.RedirectStandardInput = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = Encoding.UTF8; psi.StandardErrorEncoding = Encoding.UTF8;
            psi.EnvironmentVariables["ECXP_DORECZANIE"] = "1";   // hook-claude.ps1: tura agenta w tle nie jest wpisem operatora (bez lustra)
            return psi;
        }

        static List<string> Zapis(Dictionary<string, object> rec)
        {
            var l = new List<string> { P.S(rec, "katalog") };
            l.AddRange(P.L(rec, "katalogi_zapisu").Where(Directory.Exists));
            return l.Where(x => x.Length > 0).Select(x => x.TrimEnd(Path.DirectorySeparatorChar)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        static bool Podwyzszone(Dictionary<string, object> rec) { return P.S(rec, "podwyzszone_klucz").Length > 0 && P.S(rec, "uprawnienia") == "pelne"; }

        static void Uruchom(Proc w, Dictionary<string, object> rec)
        {
            var prog = Agenci.Program(w.Strona);
            if (prog.Length == 0) throw new EcxpException("brak " + w.Strona + " CLI");
            var katalog = P.S(rec, "katalog");
            var pisze = P.S(rec, "uprawnienia") != "odczyt";
            ProcessStartInfo psi;
            if (w.Strona == "claude")
            {
                var narz = new List<string> { "Read", "Grep", "Glob" };
                if (pisze) { narz.Add("Edit"); narz.Add("Write"); }
                if (P.S(rec, "uprawnienia") == "pelne") { narz.Add("Bash"); narz.Add("PowerShell"); }
                Directory.CreateDirectory(Dir);
                var mcpCfg = Path.Combine(Dir, w.Nazwa + ".mcp.json");                 // pusta konfiguracja MCP: agent nie widzi ECXP
                File.WriteAllText(mcpCfg, "{\"mcpServers\":{}}", new UTF8Encoding(false));
                var args = "-p --input-format stream-json --output-format stream-json --verbose --mcp-config \"" + mcpCfg + "\" --strict-mcp-config --allowedTools " + string.Join(" ", narz) +
                           // zadanie z UAC (zgoda operatora): bez pytań o zgodę – w trybie bez okna każda prośba kończyłaby się odmową
                           (Podwyzszone(rec) ? " --permission-mode bypassPermissions" : pisze ? " --permission-mode acceptEdits" : "") +
                           (pisze ? string.Concat(Zapis(rec).Skip(1).Select(z => " --add-dir \"" + z + "\"")) : "") +
                           (P.S(rec, "model").Length > 0 ? " --model " + P.S(rec, "model") : "") +
                           // nazwa sesji jak przy sesjach w aplikacji („🔗ECXP …”) – widoczna na liście /resume
                           " --name \"" + NazwaSesji(rec, w.Nazwa).Replace("\"", "'") + "\"";
                psi = Psi(prog, args, katalog);
            }
            else psi = Psi(prog, BezEcxpCodex() + "-c thread_unload_delay_secs=0 app-server", katalog);
            var pr = Process.Start(psi);
            w.Pr = pr;
            w.We = new StreamWriter(pr.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
            ZapiszPidy();
            pr.ErrorDataReceived += (s, e) => { if (e.Data != null) Dopisz(w, e.Data); };
            pr.BeginErrorReadLine();
            new Thread(() => Czytaj(w)) { IsBackground = true, Name = "ecxp-cieply-czyt-" + w.Nazwa }.Start();
            if (w.Strona == "codex")
            {
                var init = Rpc(w, "initialize", P.D("clientInfo", P.D("name", "ecxp", "title", "ECXP", "version", "1.2"), "capabilities", P.D("experimentalApi", true)), 120000);
                w.CodexHome = P.S(init, "codexHome");
                Pisz(w, P.D("jsonrpc", "2.0", "method", "initialized", "params", P.D()));
            }
            w.Gotowy = true;
            w.Ostatnio = DateTime.Now;
            Log("ciepły " + w.Nazwa + ": gotowy (" + w.Strona + ")");
        }

        // Agent Codex w tle nie widzi ECXP – jak agent Claude (pusta konfiguracja MCP): stos odbiera i odpowiada za niego,
        // a wywołanie narzędzia ECXP przy zasadzie zgód „never” kończy się odmową i mylącym komunikatem w odpowiedzi (04.11.2026).
        // Wyłączenie tylko, gdy config.toml ma serwer ecxp – dla nieistniejącego serwera Codex nie wystartowałby
        // („failed to load bootstrap configuration”). Konfiguracja użytkownika pozostaje bez zmian (parametr -c).
        public static string BezEcxpCodex()
        {
            try
            {
                var dom = Environment.GetEnvironmentVariable("CODEX_HOME");
                if (string.IsNullOrEmpty(dom)) dom = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
                var cfg = Path.Combine(dom, "config.toml");
                if (File.Exists(cfg) && File.ReadAllLines(cfg).Any(l => l.Trim() == "[mcp_servers.ecxp]")) return "-c mcp_servers.ecxp.enabled=false ";
            }
            catch { }
            return "";
        }

        static void Pisz(Proc w, object o) { lock (w.We) w.We.WriteLine(P.Js().Serialize(o)); }

        static void Czytaj(Proc w)
        {
            var js = P.Js();
            try
            {
                string l;
                while ((l = w.Pr.StandardOutput.ReadLine()) != null)
                {
                    if (w.Strona == "claude")
                    {
                        if (l.Contains("\"type\":\"result\"")) lock (w.Wyniki) { w.Wyniki.Enqueue(l); Monitor.PulseAll(w.Wyniki); }
                        continue;
                    }
                    Dictionary<string, object> m;
                    try { m = js.Deserialize<Dictionary<string, object>>(l); } catch { continue; }
                    if (m == null) continue;
                    var metoda = P.S(m, "method");
                    var pa = m.ContainsKey("params") ? m["params"] as Dictionary<string, object> : null;
                    if (m.ContainsKey("id") && metoda.Length > 0)
                    {
                        // prośba o zgodę przy zasadzie „never” nie powinna przyjść; gdyby przyszła – odmowa (agent zgłosi to w wyniku)
                        Dopisz(w, "prośba " + metoda + " – odmowa");
                        object wynik = metoda.Contains("elicitation") ? (object)P.D("action", "decline", "content", P.D())
                                     : metoda.Contains("permissions") ? (object)P.D("permissions", new List<object>(), "scope", "turn")
                                     : P.D("decision", "decline");
                        Pisz(w, P.D("jsonrpc", "2.0", "id", m["id"], "result", wynik));
                        continue;
                    }
                    if (m.ContainsKey("id")) { int id; if (int.TryParse(P.S(m, "id"), out id)) lock (w.Odp) { w.Odp[id] = m; Monitor.PulseAll(w.Odp); } continue; }
                    if (metoda == "item/completed" && pa != null && pa.ContainsKey("item"))
                    {
                        var it = pa["item"] as Dictionary<string, object>;
                        if (it != null && P.S(it, "type") == "agentMessage") w.Ost = P.S(it, "text");
                    }
                    else if (metoda == "turn/completed")
                    {
                        var tu = pa != null && pa.ContainsKey("turn") ? pa["turn"] as Dictionary<string, object> : null;
                        w.StatusTury = tu != null ? P.S(tu, "status") : "";
                        if (tu != null && w.StatusTury != "completed") w.BladTury = js.Serialize(tu.ContainsKey("error") ? tu["error"] : tu);
                        w.KoniecTury.Set();
                    }
                    else if (metoda == "error" && pa != null) Dopisz(w, "błąd: " + js.Serialize(pa));
                }
            }
            catch (Exception ex) { Dopisz(w, "czytanie: " + ex.Message); }
            w.Martwy = true;
            w.KoniecTury.Set();
            lock (w.Wyniki) Monitor.PulseAll(w.Wyniki);
            lock (w.Odp) Monitor.PulseAll(w.Odp);
        }

        static Dictionary<string, object> Rpc(Proc w, string metoda, Dictionary<string, object> par, int ms)
        {
            int id = Interlocked.Increment(ref w.Nr);
            Pisz(w, P.D("jsonrpc", "2.0", "id", id, "method", metoda, "params", par));
            var koniec = DateTime.Now.AddMilliseconds(ms);
            lock (w.Odp)
                while (!w.Odp.ContainsKey(id))
                {
                    var zostalo = (int)(koniec - DateTime.Now).TotalMilliseconds;
                    if (zostalo <= 0 || w.Martwy) throw new EcxpException("Codex app-server nie odpowiedział na " + metoda + ".");
                    Monitor.Wait(w.Odp, Math.Min(zostalo, 500));
                }
            var r = w.Odp[id];
            lock (w.Odp) w.Odp.Remove(id);
            if (r.ContainsKey("error")) throw new EcxpException("Codex " + metoda + ": " + P.Js().Serialize(r["error"]));
            return r.ContainsKey("result") ? r["result"] as Dictionary<string, object> : null;
        }

        // ---------- wykonanie jednej pracy ----------
        // rejestr: proces tury (Stop / Uruchom ponownie w konsoli przerywa go jak dotąd).
        public static Wynik Wykonaj(Dictionary<string, object> rec, string tekst, int limitMin, Action<Process> rejestr)
        {
            var strona = P.S(rec, "strona");
            if (Zastepczy(strona)) return Jednorazowo(rec, tekst, limitMin, rejestr);
            string powod;
            var wy = WykonajCiepla(rec, tekst, limitMin, rejestr, out powod);
            if (powod == null) return wy;
            // ciepły proces nie działa na tym stanowisku – to samo zadanie jeszcze raz w trybie zastępczym
            PrzejdzNaZastepczy(strona, powod);
            return Jednorazowo(rec, tekst, limitMin, rejestr);
        }

        // Rozpoznanie „ten tryb nie działa na tym stanowisku” (a nie zwykły błąd zadania).
        static string NieDziala(Proc w, Wynik wy, bool wystartowal)
        {
            if (w.TuraWystartowala) return null; // nie powtarzaj wykonanej pracy po błędzie oddawania sesji
            if (w.BladProjektu) return null; // błąd projektu nie może uruchomić drugiej, nieprzypisanej sesji
            if (!wystartowal) return "proces nie wystartował: " + Dziennik(w);
            if (wy.Kod == 0) return null;
            var t = (wy.Wyjscie ?? "") + "\n" + Dziennik(w);
            if (w.Strona == "codex" && System.Text.RegularExpressions.Regex.IsMatch(t, @"-32601|[Mm]ethod not found|unknown variant|unknown method|nie odpowiedział na (initialize|thread/start|turn/start)")) return t;
            if (w.Strona == "claude" && w.Martwy && System.Text.RegularExpressions.Regex.IsMatch(t, @"unknown option|error: option|[Uu]nknown argument|bypassPermissions|stream-json|--name|not allowed|disabled|zakończył się bez wyniku")) return t;
            return null;
        }

        static Wynik WykonajCiepla(Dictionary<string, object> rec, string tekst, int limitMin, Action<Process> rejestr, out string nieDziala)
        {
            nieDziala = null;
            var n = P.S(rec, "nazwa");
            Rozgrzej(rec);
            Proc w;
            lock (procesy) w = procesy[n];
            w.Zajety = true; w.TuraWystartowala = false;
            var wy = new Wynik();
            bool wystartowal = false;
            try
            {
                var doGotowosci = DateTime.Now.AddMinutes(3);
                while (!w.Gotowy && !w.Martwy && DateTime.Now < doGotowosci) Thread.Sleep(50);
                if (!w.Gotowy) { wy.Kod = 1; wy.Wyjscie = "proces agenta nie wystartował: " + Dziennik(w); }
                else
                {
                    wystartowal = true;
                    if (rejestr != null) rejestr(w.Pr);
                    if (w.Strona == "codex") WykonajCodex(w, rec, tekst, limitMin, wy);
                    else WykonajClaude(w, rec, tekst, limitMin, wy);
                }
            }
            catch (Exception ex) { wy.Kod = 1; wy.Wyjscie = "błąd: " + ex.Message + "\n" + Dziennik(w); }
            finally
            {
                w.Zajety = false; w.Ostatnio = DateTime.Now;
                // Claude: świeży kontekst na następne zadanie – proces zamknięty, pętla agentów od razu uruchomi nowy.
                // Codex i każdy proces po błędzie: wątek/kontekst też nie przechodzi dalej (nowy wątek albo nowy proces).
                if (w.Strona == "claude" || wy.Kod != 0 || w.Martwy) lock (procesy) { if (procesy.ContainsKey(n) && procesy[n] == w) procesy.Remove(n); ZamknijProc(w); }
            }
            nieDziala = NieDziala(w, wy, wystartowal);
            return wy;
        }

        // ---------- tryb zastępczy: jednorazowe uruchomienie (jak przed ciepłymi procesami) ----------
        static Wynik Jednorazowo(Dictionary<string, object> rec, string tekst, int limitMin, Action<Process> rejestr)
        {
            var wy = new Wynik();
            var strona = P.S(rec, "strona"); var n = P.S(rec, "nazwa");
            var prog = Agenci.Program(strona);
            if (prog.Length == 0) { wy.Kod = 1; wy.Wyjscie = "brak " + strona + " CLI na tym stanowisku"; return wy; }
            var katalog = P.S(rec, "katalog");
            var pisze = P.S(rec, "uprawnienia") != "odczyt";
            Directory.CreateDirectory(Dir);
            var plikWyniku = Path.Combine(Dir, n + ".wynik.txt");
            var startTury = DateTime.Now.AddSeconds(-1);      // wynik tylko z tej tury (plik nadpisuje „-o”)
            string args;
            if (strona == "claude")
            {
                var narz = new List<string> { "Read", "Grep", "Glob" };
                if (pisze) { narz.Add("Edit"); narz.Add("Write"); }
                if (P.S(rec, "uprawnienia") == "pelne") { narz.Add("Bash"); narz.Add("PowerShell"); }
                var mcpCfg = Path.Combine(Dir, n + ".mcp.json");
                File.WriteAllText(mcpCfg, "{\"mcpServers\":{}}", new UTF8Encoding(false));
                // bez trybu „bypassPermissions” (może być zablokowany na stanowisku) – narzędzia dozwolone listą
                args = "-p --output-format json --mcp-config \"" + mcpCfg + "\" --strict-mcp-config --allowedTools " + string.Join(" ", narz) +
                       (pisze ? " --permission-mode acceptEdits" : "") +
                       (pisze ? string.Concat(Zapis(rec).Skip(1).Select(z => " --add-dir \"" + z + "\"")) : "") +
                       (P.S(rec, "model").Length > 0 ? " --model " + P.S(rec, "model") : "");
            }
            else
            {
                var tryb = Podwyzszone(rec) ? "danger-full-access" : pisze ? "workspace-write" : "read-only";
                args = BezEcxpCodex() + "exec --skip-git-repo-check -C \"" + katalog + "\" -s " + tryb +
                       (pisze && tryb != "danger-full-access" ? string.Concat(Zapis(rec).Skip(1).Select(z => " --add-dir \"" + z + "\"")) : "") +
                       " -o \"" + plikWyniku + "\"" +
                       (P.S(rec, "model").Length > 0 ? " -m " + P.S(rec, "model") : "") + " -";
            }
            var psi = Psi(prog, args, katalog);
            var wyjscie = new StringBuilder();
            try
            {
                using (var pr = Process.Start(psi))
                {
                    if (rejestr != null) rejestr(pr);
                    pr.OutputDataReceived += (s2, e) => { if (e.Data != null) lock (wyjscie) wyjscie.AppendLine(e.Data); };
                    pr.ErrorDataReceived += (s2, e) => { if (e.Data != null) lock (wyjscie) wyjscie.AppendLine(e.Data); };
                    pr.BeginOutputReadLine(); pr.BeginErrorReadLine();
                    var we = new StreamWriter(pr.StandardInput.BaseStream, new UTF8Encoding(false));
                    we.Write(tekst); we.Close();
                    if (!pr.WaitForExit(limitMin * 60 * 1000)) { try { pr.Kill(); } catch { } wy.Kod = -1; lock (wyjscie) wy.Wyjscie = "przerwano po " + limitMin + " min\n" + wyjscie; return wy; }
                    pr.WaitForExit();
                    wy.Kod = pr.ExitCode;
                }
            }
            catch (Exception ex) { wy.Kod = 1; wy.Wyjscie = "błąd: " + ex.Message; return wy; }
            lock (wyjscie) wy.Wyjscie = "(tryb zastępczy)\n" + wyjscie;
            if (wy.Kod != 0) return wy;
            if (strona == "codex")
            {
                try { wy.Odpowiedz = File.Exists(plikWyniku) && File.GetLastWriteTime(plikWyniku) >= startTury ? File.ReadAllText(plikWyniku, Encoding.UTF8).Trim() : ""; } catch { }
                var mw = System.Text.RegularExpressions.Regex.Match(wy.Wyjscie, @"session id:\s*([0-9a-f-]{36})");
                if (mw.Success) ZglosPokaz(rec, "codex", mw.Groups[1].Value);
            }
            else
                foreach (var linia in wy.Wyjscie.Split('\n').Reverse())
                {
                    var t = linia.Trim();
                    if (!t.StartsWith("{") || !t.Contains("\"result\"")) continue;
                    try { var j = P.Js().Deserialize<Dictionary<string, object>>(t); if (P.B(j, "is_error")) wy.Kod = 1; else { wy.Odpowiedz = P.S(j, "result").Trim(); ZglosPokaz(rec, "claude", P.S(j, "session_id")); } } catch { }
                    break;
                }
            return wy;
        }

        // ---------- widoczność sesji w aplikacjach ----------
        // Każdy projekt ECXP = osobny projekt w aplikacji Codex: folder projektu, nazwa z ikoną projektu („💡 test-pogoda”).
        // Nowy projekt – zakładany przy pierwszym zadaniu; kolejne zadania dokładają tylko wątki (klucz idempotencji = folder).

        // Ikony z projekt.json: projektu („ikona”) i modułu („ikony_modulow”).
        static void Ikony(Dictionary<string, object> rec, out string projektu, out string modulu)
        {
            projektu = ""; modulu = "";
            try
            {
                var pj = Pliki.Czytaj(Path.Combine(P.Root, "projekty", P.S(rec, "projekt"), "projekt.json"));
                projektu = P.S(pj, "ikona");
                object im; if (pj.TryGetValue("ikony_modulow", out im) && im is Dictionary<string, object>) modulu = P.S((Dictionary<string, object>)im, P.S(rec, "modul"));
            }
            catch { }
        }

        // Nazwa sesji w oknie agenta: „🔗ECXP 💡 moduł” (ikona modułu, a gdy jej brak – projektu).
        static string NazwaSesji(Dictionary<string, object> rec, string zapasowa)
        {
            var t = P.S(rec, "opis").Length > 0 ? P.S(rec, "opis") : zapasowa;
            string ip, im; Ikony(rec, out ip, out im);
            var ik = im.Length > 0 ? im : ip;
            if (ik.Length == 0 || t.Contains(ik)) return t;
            const string zn = "🔗ECXP";
            return t.StartsWith(zn) ? zn + " " + ik + " " + t.Substring(zn.Length).TrimStart() : ik + " " + t;
        }

        static void ZglosProjektCodex(string katalog)
        {
            Directory.CreateDirectory(DirPokaz);
            Pliki.Zapisz(Path.Combine(DirPokaz, "projekt-" + Guid.NewGuid().ToString("N") + ".json"),
                P.D("strona", "codex-projekt", "katalog", katalog, "nazwa", "projekt Codex"));
        }

        // Claude Code: folder projektu ECXP oznaczony jako zaufany (bez pytania „Do you trust this folder?”) – tylko foldery
        // założone przez ECXP (…\projekty\…), kopia ~/.claude.json raz dziennie przed zmianą. Zgoda właściciela 2026-11-04.
        public static void ZaufajClaude(string katalog)
        {
            try
            {
                var root = Path.GetFullPath(Path.Combine(P.Root, "projekty")).TrimEnd(Path.DirectorySeparatorChar);
                var kat = Path.GetFullPath(katalog).TrimEnd(Path.DirectorySeparatorChar);
                if (!kat.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return;
                var plik = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");
                if (!File.Exists(plik)) return;
                var js = P.Js(); js.MaxJsonLength = int.MaxValue;
                var d = js.Deserialize<Dictionary<string, object>>(File.ReadAllText(plik, Encoding.UTF8));
                if (d == null) return;
                object po; var proj = d.TryGetValue("projects", out po) && po is Dictionary<string, object> ? (Dictionary<string, object>)po : new Dictionary<string, object>();
                object wo; var wpis = proj.TryGetValue(kat, out wo) ? wo as Dictionary<string, object> : null;
                if (wpis != null && P.B(wpis, "hasTrustDialogAccepted")) return;
                if (wpis == null) wpis = P.D("allowedTools", new List<object>(), "mcpContextUris", new List<object>(), "enabledMcpjsonServers", new List<object>(),
                    "disabledMcpjsonServers", new List<object>(), "hasClaudeMdExternalIncludesApproved", false, "hasClaudeMdExternalIncludesWarningShown", false);
                wpis["hasTrustDialogAccepted"] = true;
                proj[kat] = wpis; d["projects"] = proj;
                var kopia = plik + ".przed-ecxp-" + DateTime.Now.ToString("yyyyMMdd") + ".bak";
                if (!File.Exists(kopia)) File.Copy(plik, kopia);
                var tmp = plik + ".ecxp-" + DateTime.Now.Ticks + ".tmp";
                File.WriteAllText(tmp, js.Serialize(d), new UTF8Encoding(false));
                File.Replace(tmp, plik, null);
                Log("Claude: folder " + kat + " oznaczony jako zaufany");
            }
            catch (Exception ex) { Log("Claude – zaufanie folderu: " + ex.Message); }
        }

        static void WykonajCodex(Proc w, Dictionary<string, object> rec, string tekst, int limitMin, Wynik wy)
        {
            var katalog = P.S(rec, "katalog");
            string tryb; object polityka;
            if (Podwyzszone(rec)) { tryb = "danger-full-access"; polityka = P.D("type", "dangerFullAccess"); }      // zgoda operatora + UAC, do FIN tego zadania
            else if (P.S(rec, "uprawnienia") == "odczyt") { tryb = "read-only"; polityka = P.D("type", "readOnly"); }
            else { tryb = "workspace-write"; polityka = P.D("type", "workspaceWrite", "writableRoots", Zapis(rec), "networkAccess", false); }
            var parStart = P.D("cwd", katalog, "approvalPolicy", "never", "sandbox", tryb);
            w.BladProjektu = false;
            string projekt;
            try
            {
                if (P.B(rec, "pokaz_sesje"))
                    projekt = CodexProjekt.Zapewnij(w.CodexHome, katalog, (m, p, ms) => Rpc(w, m, p, ms), ZglosProjektCodex, t => Dopisz(w, t));
                else
                {
                    // Praca w tle nie otwiera okien aplikacji. Projekt backendu nadal jest przypisany do katalogu.
                    var r = Rpc(w, "project/create", P.D("name", P.S(rec, "projekt"), "roots", new object[] { P.D("path", Path.GetFullPath(katalog)) },
                        "idempotencyKey", "ecxp:" + CodexProjekt.Normalizuj(katalog).ToLowerInvariant()), 30000);
                    var pr = r != null && r.ContainsKey("project") ? r["project"] as Dictionary<string, object> : null;
                    projekt = pr != null ? P.S(pr, "id") : "";
                    if (projekt.Length == 0) throw new EcxpException("Codex nie zwrócił projektu agenta w tle.");
                }
            }
            catch { w.BladProjektu = true; throw; }
            parStart["projectId"] = projekt;
            var st = Rpc(w, "thread/start", parStart, 60000);
            var th = st != null && st.ContainsKey("thread") ? st["thread"] as Dictionary<string, object> : null;
            var watek = th != null ? P.S(th, "id") : "";
            if (watek.Length == 0) throw new EcxpException("Codex nie zwrócił identyfikatora wątku.");
            // nazwa wątku jak przy sesjach w aplikacji („🔗ECXP …”) – wątek agenta w tle łatwo znaleźć w aplikacji Codex
            var nazwa = NazwaSesji(rec, w.Nazwa);
            try { Rpc(w, "thread/name/set", P.D("threadId", watek, "name", nazwa), 15000); } catch (Exception ex) { Dopisz(w, "nazwa wątku: " + ex.Message); }
            w.Ost = ""; w.StatusTury = null; w.BladTury = null; w.KoniecTury.Reset();
            var par = P.D("threadId", watek, "approvalPolicy", "never", "sandboxPolicy", polityka,
                "input", new List<object> { P.D("type", "text", "text", tekst, "text_elements", new List<object>()) });
            if (P.S(rec, "model").Length > 0) par["model"] = P.S(rec, "model");
            var startTury = Rpc(w, "turn/start", par, 60000);
            w.TuraWystartowala = true;
            bool zakonczona = w.KoniecTury.WaitOne(TimeSpan.FromMinutes(limitMin));
            if (!zakonczona)
            {
                var tu = startTury != null && startTury.ContainsKey("turn") ? startTury["turn"] as Dictionary<string, object> : null;
                Rpc(w, "turn/interrupt", P.D("threadId", watek, "turnId", P.S(tu, "id")), 15000);
                if (!w.KoniecTury.WaitOne(10000)) throw new EcxpException("Codex nie potwierdził przerwania tury.");
            }
            CodexProjekt.OddajWatek(watek, (m, p, ms) => Rpc(w, m, p, ms));
            ZglosPokaz(rec, "codex", watek);
            if (!zakonczona) { wy.Kod = -1; wy.Wyjscie = "przerwano po " + limitMin + " min"; return; }
            if (w.StatusTury == "completed") { wy.Kod = 0; wy.Odpowiedz = (w.Ost ?? "").Trim(); wy.Wyjscie = "wątek " + watek + "\n" + wy.Odpowiedz; }
            else { wy.Kod = 1; wy.Wyjscie = "tura " + (w.StatusTury ?? "przerwana") + ": " + (w.BladTury ?? "") + "\n" + Dziennik(w); }
        }

        static void WykonajClaude(Proc w, Dictionary<string, object> rec, string tekst, int limitMin, Wynik wy)
        {
            Pisz(w, P.D("type", "user", "message", P.D("role", "user", "content", tekst)));
            string linia = null;
            var koniec = DateTime.Now.AddMinutes(limitMin);
            lock (w.Wyniki)
                while (w.Wyniki.Count == 0)
                {
                    var zostalo = (int)(koniec - DateTime.Now).TotalMilliseconds;
                    if (zostalo <= 0 || w.Martwy) break;
                    Monitor.Wait(w.Wyniki, Math.Min(zostalo, 500));
                }
            lock (w.Wyniki) if (w.Wyniki.Count > 0) linia = w.Wyniki.Dequeue();
            if (linia == null) { wy.Kod = -1; wy.Wyjscie = (w.Martwy ? "proces Claude zakończył się bez wyniku\n" : "przerwano po " + limitMin + " min\n") + Dziennik(w); return; }
            Dictionary<string, object> j = null;
            try { j = P.Js().Deserialize<Dictionary<string, object>>(linia); } catch { }
            wy.Wyjscie = linia;
            if (j == null || P.B(j, "is_error")) { wy.Kod = 1; return; }
            wy.Kod = 0; wy.Odpowiedz = P.S(j, "result").Trim();
            ZglosPokaz(rec, "claude", P.S(j, "session_id"));
        }

        static string Dziennik(Proc w) { lock (w.Dziennik) { var s = w.Dziennik.ToString(); return s.Length > 4000 ? s.Substring(s.Length - 4000) : s; } }

        // ---------- sprzątanie ----------
        static void ZamknijProc(Proc w)
        {
            w.Martwy = true;
            var pr = w.Pr;
            new Thread(() =>
            {
                try { if (w.We != null) w.We.Close(); } catch { }
                try
                {
                    if (pr != null && !pr.WaitForExit(3000))
                        Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "taskkill.exe"), "/PID " + pr.Id + " /T /F") { UseShellExecute = false, CreateNoWindow = true }).WaitForExit(10000);
                }
                catch { }
                ZapiszPidy();
            }) { IsBackground = true }.Start();
        }

        public static void Zamknij(Func<string, bool> ktore)
        {
            lock (procesy)
                foreach (var kv in procesy.Where(x => ktore(x.Key)).ToList()) { procesy.Remove(kv.Key); ZamknijProc(kv.Value); }
        }
        public static void ZamknijWszystko() { Zamknij(x => true); }

        // Procesy bezczynne ponad BezczynnoscMin, nieaktywnych agentów albo z inną konfiguracją – zamknięte.
        public static void Sprzataj(Dictionary<string, Dictionary<string, object>> aktywni)
        {
            lock (procesy)
                foreach (var kv in procesy.ToList())
                {
                    var w = kv.Value;
                    if (w.Zajety) continue;
                    Dictionary<string, object> rec;
                    bool zbedny = w.Martwy || !aktywni.TryGetValue(kv.Key, out rec) || Klucz(rec) != w.Klucz || DateTime.Now - w.Ostatnio > TimeSpan.FromMinutes(BezczynnoscMin);
                    if (zbedny) { procesy.Remove(kv.Key); ZamknijProc(w); }
                }
        }

        // Identyfikatory procesów tego stosu (plik na proces ecxpd) – po awarii stosu następny start zamyka sieroty.
        static string PlikPidow { get { return Path.Combine(Dir, "cieple-" + Process.GetCurrentProcess().Id + ".txt"); } }
        static void ZapiszPidy()
        {
            try
            {
                List<string> l;
                lock (procesy) l = procesy.Values.Where(x => x.Pr != null && !x.Martwy).Select(x => { try { return x.Pr.Id.ToString(); } catch { return ""; } }).Where(x => x.Length > 0).ToList();
                Directory.CreateDirectory(Dir);
                File.WriteAllLines(PlikPidow, l);
            }
            catch { }
        }
        public static int SprzatajSieroty()
        {
            int n = 0;
            try
            {
                if (!Directory.Exists(Dir)) return 0;
                foreach (var f in Directory.GetFiles(Dir, "cieple-*.txt"))
                {
                    int wl;
                    if (!int.TryParse(Path.GetFileNameWithoutExtension(f).Substring(7), out wl) || wl == Process.GetCurrentProcess().Id) continue;
                    bool zyje = false;
                    try { zyje = !Process.GetProcessById(wl).HasExited && Process.GetProcessById(wl).ProcessName.StartsWith("ecxpd", StringComparison.OrdinalIgnoreCase); } catch { }
                    if (zyje) continue;
                    foreach (var s in File.ReadAllLines(f))
                    {
                        int pid; if (!int.TryParse(s.Trim(), out pid)) continue;
                        try
                        {
                            // tylko procesy agentów (numer PID mógł zostać nadany innemu programowi)
                            var nazwa = Process.GetProcessById(pid).ProcessName.ToLowerInvariant();
                            if (nazwa != "cmd" && nazwa != "node" && nazwa != "codex" && nazwa != "claude") continue;
                            Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "taskkill.exe"), "/PID " + pid + " /T /F") { UseShellExecute = false, CreateNoWindow = true }).WaitForExit(10000);
                            n++;
                        }
                        catch { }
                    }
                    try { File.Move(f, f + ".zamkniete-" + DateTime.Now.Ticks); } catch { }
                }
            }
            catch { }
            return n;
        }
    }
}
