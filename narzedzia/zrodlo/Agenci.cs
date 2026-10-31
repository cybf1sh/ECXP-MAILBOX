// ECXP – agenci w tle (Claude Code CLI; Codex CLI, jeśli jest na stanowisku) i lustro wpisów operatora z okien czatów.
// Nic nie jest przypięte do jednego komputera: programy CLI wyszukuje się w PATH, rozmowy Codexa w profilu użytkownika,
// katalog skrzynki to P.Root (katalog instalacji).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Ecxp
{
    static class Agenci
    {
        public static readonly string[] Uprawnienia = { "odczyt", "edycja", "pelne" };
        static string Dir { get { return Path.Combine(P.Root, "stan", "agenci"); } }
        static readonly HashSet<string> trwa = new HashSet<string>();
        static readonly Dictionary<string, Process> procesy = new Dictionary<string, Process>();
        static readonly HashSet<string> przerwane = new HashSet<string>();
        public static Action<string> Log = s => { };

        // Program CLI z PATH (Windows: .cmd/.exe/.bat). Pusty, gdy go nie ma na tym stanowisku.
        public static string Program(string nazwa)
        {
            var sciezki = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';').Where(x => x.Trim().Length > 0).ToList();
            sciezki.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"));
            foreach (var d in sciezki)
                foreach (var ext in new[] { ".cmd", ".exe", ".bat" })
                {
                    try { var p = Path.Combine(d.Trim(), nazwa + ext); if (File.Exists(p)) return p; } catch { }
                }
            return "";
        }

        public static List<Dictionary<string, object>> Lista()
        {
            if (!Directory.Exists(Dir)) return new List<Dictionary<string, object>>();
            return Directory.GetFiles(Dir, "*.json").Select(Pliki.Czytaj).Where(d => P.S(d, "nazwa").Length > 0).ToList();
        }

        public static Dictionary<string, object> Uruchom(string nazwa, string projekt, string modul, string katalog, string model, string uprawnienia, List<string> role, string opis, int naGodzine)
        {
            nazwa = (nazwa ?? "").Trim().ToLowerInvariant();
            if (!P.PoprawnyNadawca(nazwa)) throw new EcxpException("Nazwa agenta: codex-<nazwa> albo claude-<nazwa>.");
            var strona = nazwa.StartsWith("codex") ? "codex" : "claude";
            if (!Uprawnienia.Contains(uprawnienia)) throw new EcxpException("Uprawnienia: odczyt, edycja albo pelne.");
            if ((katalog ?? "").Trim().Length == 0 || !Directory.Exists(katalog)) throw new EcxpException("Katalog pracy agenta nie istnieje: " + katalog);
            var prog = Program(strona);
            if (prog.Length == 0)
                throw new EcxpException(strona == "codex"
                    ? "Na tym stanowisku nie ma Codex CLI (polecenie codex). Zainstaluj je albo otwórz czat Codexa i wklej pakiet startowy."
                    : "Na tym stanowisku nie ma Claude Code CLI (polecenie claude).");
            var r = Siec.Przypisz(nazwa, projekt, modul, "tlo:" + nazwa, role, opis, P.Wlasciciel);
            var rec = P.D("nazwa", nazwa, "strona", strona, "projekt", r["projekt"], "modul", r["modul"], "katalog", Path.GetFullPath(katalog),
                "model", model ?? "", "uprawnienia", uprawnienia, "na_godzine", Math.Max(1, naGodzine), "sesja", Guid.NewGuid().ToString(),
                "sesja_zalozona", false, "aktywny", true, "uruchomienia", new List<object>(), "utworzono", P.Teraz(), "opis", opis ?? "");
            Pliki.Zapisz(Path.Combine(Dir, nazwa + ".json"), rec);
            var pakiet = Pakiet(rec, role);
            File.WriteAllText(Path.Combine(Dir, nazwa + ".system.txt"), pakiet, new UTF8Encoding(false));
            P.Zdarzenie("agent_w_tle", P.D("nazwa", nazwa, "projekt", rec["projekt"], "modul", rec["modul"], "uprawnienia", uprawnienia));
            return rec;
        }

        static string Pakiet(Dictionary<string, object> rec, List<string> role)
        {
            var n = P.S(rec, "nazwa");
            var partner = Siec.Partner(P.S(rec, "projekt"), P.S(rec, "modul"), n);
            return "Jesteś agentem w tle w parze ECXP (zasada 1:1) projektu " + P.S(rec, "projekt") + ", moduł " + P.S(rec, "modul") + ".\n" +
                   "Twoja nazwa w ECXP: " + n + ". Druga strona pary: " + (partner.Length > 0 ? partner : "(jeszcze nieprzypisana)") +
                   ". Role: " + (role != null && role.Count > 0 ? string.Join(", ", role) : "według kart ról, które odbierzesz") + ".\n" +
                   "Pracujesz w katalogu " + P.S(rec, "katalog") + ". Uprawnienia: " + P.S(rec, "uprawnienia") + ".\n" +
                   "Każdą turę zaczynasz od ecxp_odbierz(ja=" + n + "); każdą wiadomość potwierdzasz (odpowiedź ecxp_wyslij z odpowiedz_na albo ecxp_potwierdz). " +
                   "Zadania łańcucha kończysz przez ecxp_zadanie(stan=zakonczone | nieudane | odrzucone). Z drugą stroną pracujecie na jednej uzgodnionej gałęzi " +
                   "(ecxp_umowa_galezi, po każdym commicie ecxp_stan_galezi). Bez push, scalenia, tagów i usuwania bez podpisanej decyzji operatora " +
                   "(sprawdź ją: ecxp_sprawdz_decyzje). Bez danych spraw, haseł i kluczy." +
                   (P.S(rec, "opis").Length > 0 ? "\nOd operatora: " + P.S(rec, "opis") : "");
        }

        public static Dictionary<string, object> Zatrzymaj(string nazwa)
        {
            var p = Path.Combine(Dir, Path.GetFileName(nazwa) + ".json");
            if (!File.Exists(p)) throw new EcxpException("Nie ma agenta w tle '" + nazwa + "'.");
            var d = Pliki.Czytaj(p); d["aktywny"] = false; Pliki.Zapisz(p, d);
            P.Zdarzenie("agent_zatrzymany", P.D("nazwa", nazwa));
            return d;
        }

        // ---------- usługa agentów w tle jednej strony (Claude albo Codex) ----------
        // Stop: przerywa trwające tury tej strony i wstrzymuje kolejne (rekordy agentów zostają).
        // Start: wznawia tury. Uruchom ponownie (restart): przerywa tury, wznawia i kasuje 10-minutową przerwę po błędzie.
        static string UslugiPlik { get { return Path.Combine(P.Root, "stan", "uslugi.json"); } }
        static Dictionary<string, object> Uslugi() { try { return File.Exists(UslugiPlik) ? Pliki.Czytaj(UslugiPlik) : new Dictionary<string, object>(); } catch { return new Dictionary<string, object>(); } }
        public static bool Zatrzymana(string strona) { return P.B(Uslugi(), strona + "_zatrzymana"); }
        static DateTimeOffset Reset(string strona) { DateTimeOffset t; return DateTimeOffset.TryParse(P.S(Uslugi(), strona + "_reset"), out t) ? t : DateTimeOffset.MinValue; }

        public static Dictionary<string, object> Usluga(string strona, string akcja)
        {
            if (strona != "claude" && strona != "codex") throw new EcxpException("Strona usługi: claude albo codex.");
            if (akcja != "start" && akcja != "stop" && akcja != "restart") throw new EcxpException("Akcja usługi: start, stop albo restart.");
            int ile = akcja == "start" ? 0 : PrzerwijTury(strona);
            var u = Uslugi();
            u[strona + "_zatrzymana"] = akcja == "stop";
            u[strona + "_zmiana"] = P.Teraz();
            if (akcja != "stop") u[strona + "_reset"] = P.Teraz();
            Pliki.Zapisz(UslugiPlik, u);
            P.Zdarzenie("usluga_" + akcja, P.D("strona", strona, "przerwane_tury", ile));
            Log("usługa " + strona + ": " + akcja + (ile > 0 ? ", przerwane tury: " + ile : ""));
            var st = UslugaStan(strona);
            st["przerwane_tury"] = ile;
            return st;
        }

        static int PrzerwijTury(string strona)
        {
            List<KeyValuePair<string, Process>> l;
            lock (trwa) { l = procesy.Where(x => x.Key.StartsWith(strona + "-")).ToList(); foreach (var x in l) przerwane.Add(x.Key); }
            foreach (var x in l)
            {
                try
                {
                    var k = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "taskkill.exe"), "/PID " + x.Value.Id + " /T /F") { UseShellExecute = false, CreateNoWindow = true });
                    k.WaitForExit(10000);
                }
                catch { }
                try { if (!x.Value.HasExited) x.Value.Kill(); } catch { }
            }
            return l.Count;
        }

        public static Dictionary<string, object> UslugaStan(string strona)
        {
            var ag = Lista().Where(x => P.S(x, "strona") == strona).ToList();
            List<string> wToku; lock (trwa) wToku = trwa.Where(x => x.StartsWith(strona + "-")).ToList();
            Dictionary<string, object> ost = null; string ostN = ""; var tOst = DateTimeOffset.MinValue; int godz = 0;
            foreach (var a in ag)
                foreach (var u in P.Lista(a, "uruchomienia"))
                {
                    DateTimeOffset t;
                    if (!DateTimeOffset.TryParse(P.S(u, "czas"), out t)) continue;
                    if (t > DateTimeOffset.Now.AddHours(-1)) godz++;
                    if (t > tOst) { tOst = t; ost = u; ostN = P.S(a, "nazwa"); }
                }
            var us = Uslugi();
            return P.D("strona", strona, "zatrzymana", P.B(us, strona + "_zatrzymana"), "zmiana", P.S(us, strona + "_zmiana"),
                "agenci", ag.Count, "aktywni", ag.Count(x => P.B(x, "aktywny")), "nazwy", ag.Select(x => (object)P.S(x, "nazwa")).ToList(),
                "w_toku", wToku.Cast<object>().ToList(), "tury_godzina", godz,
                "ostatnia_tura", ost == null ? "" : P.S(ost, "czas"), "ostatni_kod", ost == null ? "" : P.S(ost, "kod"), "ostatni_agent", ostN,
                "ostatni_wynik", ost == null ? "" : P.S(ost, "wynik"));
        }

        static bool MaOczekujace(string nazwa)
        {
            var dir = Path.Combine(P.Root, P.Folder(nazwa));
            if (!Directory.Exists(dir)) return false;
            var js = P.Js();
            return new DirectoryInfo(dir).GetFiles("*.json").Any(f => P.DoMnie(nazwa, P.Wczytaj(f, "", js).Do));
        }

        // Pętla w stosie: agent z oczekującymi wiadomościami, bez trwającej tury i w granicy budżetu dostaje jedną turę.
        public static void Petla()
        {
            while (true)
            {
                Thread.Sleep(5000);
                try
                {
                    foreach (var rec in Lista().Where(x => P.B(x, "aktywny")))
                    {
                        var n = P.S(rec, "nazwa");
                        if (Zatrzymana(P.S(rec, "strona"))) continue;
                        lock (trwa) if (trwa.Contains(n)) continue;
                        if (!MaOczekujace(n)) continue;
                        int naGodzine; int.TryParse(P.S(rec, "na_godzine"), out naGodzine);
                        var ostatnie = P.Lista(rec, "uruchomienia").Count(u => { DateTimeOffset t; return DateTimeOffset.TryParse(P.S(u, "czas"), out t) && t > DateTimeOffset.Now.AddHours(-1); });
                        if (ostatnie >= Math.Max(1, naGodzine)) continue;
                        // po nieudanej turze 10 minut przerwy – bez pętli ponowień
                        var ost = P.Lista(rec, "uruchomienia").LastOrDefault();
                        DateTimeOffset tOst;
                        if (ost != null && P.S(ost, "kod") != "0" && DateTimeOffset.TryParse(P.S(ost, "czas"), out tOst) && tOst > DateTimeOffset.Now.AddMinutes(-10)
                            && tOst > Reset(P.S(rec, "strona"))) continue;
                        lock (trwa) trwa.Add(n);
                        var kopia = rec;
                        new Thread(() => Tura(kopia)) { IsBackground = true, Name = "ecxp-agent-" + n }.Start();
                    }
                }
                catch (Exception ex) { Log("agenci: " + ex.Message); }
            }
        }

        static void Tura(Dictionary<string, object> rec)
        {
            var n = P.S(rec, "nazwa");
            var start = DateTime.Now;
            int kod = -1; string wynik = "";
            try
            {
                var strona = P.S(rec, "strona");
                var prog = Program(strona);
                if (prog.Length == 0) throw new EcxpException("brak " + strona + " CLI");
                var polecenie = "Masz nowe wiadomości w ECXP. Wywołaj ecxp_odbierz(ja=" + n + "), obsłuż je zgodnie ze swoją rolą i potwierdź każdą. " +
                                "Na koniec krótko zamelduj operatorowi (ecxp_wyslij do=wlasciciel, typ=meldunek), co zrobiłeś i co zostało.";
                var sys = Path.Combine(Dir, n + ".system.txt");
                string args;
                if (strona == "claude")
                {
                    var narz = new List<string> { "mcp__ecxp", "Read", "Grep", "Glob" };
                    if (P.S(rec, "uprawnienia") != "odczyt") { narz.Add("Edit"); narz.Add("Write"); }
                    if (P.S(rec, "uprawnienia") == "pelne") narz.Add("Bash");
                    // własna konfiguracja MCP: agent rozmawia z tym samym stosem (ten sam katalog instalacji), na każdym stanowisku
                    var mcpCfg = Path.Combine(Dir, n + ".mcp.json");
                    var mcpExe = Path.Combine(P.Root, "narzedzia", "ecxp-mcp.exe");
                    File.WriteAllText(mcpCfg, P.Js().Serialize(P.D("mcpServers", P.D("ecxp", P.D("command", mcpExe, "args", new List<string> { "--root", P.Root })))), new UTF8Encoding(false));
                    args = "-p --output-format json --mcp-config \"" + mcpCfg + "\" --strict-mcp-config --append-system-prompt-file \"" + sys + "\" --allowedTools " + string.Join(" ", narz) +
                           (P.S(rec, "uprawnienia") != "odczyt" ? " --permission-mode acceptEdits" : "") +
                           (P.S(rec, "model").Length > 0 ? " --model " + P.S(rec, "model") : "") +
                           (P.B(rec, "sesja_zalozona") ? " --resume " : " --session-id ") + P.S(rec, "sesja");
                }
                else
                {
                    polecenie = P.Czytaj(sys) + "\n\n" + polecenie;
                    args = "exec --skip-git-repo-check -C \"" + P.S(rec, "katalog") + "\"" + (P.S(rec, "model").Length > 0 ? " -m " + P.S(rec, "model") : "") + " -";
                }
                var psi = prog.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? new ProcessStartInfo(prog, args)
                    : new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /s /c \"\"" + prog + "\" " + args + "\"");
                psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.WorkingDirectory = P.S(rec, "katalog");
                psi.RedirectStandardInput = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8; psi.StandardErrorEncoding = Encoding.UTF8;
                using (var pr = Process.Start(psi))
                {
                    lock (trwa) procesy[n] = pr;
                    var wy = new StringBuilder();
                    pr.OutputDataReceived += (s, e) => { if (e.Data != null) lock (wy) wy.AppendLine(e.Data); };
                    pr.ErrorDataReceived += (s, e) => { if (e.Data != null) lock (wy) wy.AppendLine(e.Data); };
                    pr.BeginOutputReadLine(); pr.BeginErrorReadLine();
                    var we = new StreamWriter(pr.StandardInput.BaseStream, new UTF8Encoding(false));
                    we.Write(polecenie); we.Close();
                    if (!pr.WaitForExit(30 * 60 * 1000)) { try { pr.Kill(); } catch { } wynik = "przerwano po 30 min"; }
                    else kod = pr.ExitCode;
                    lock (wy) wynik = (wynik.Length > 0 ? wynik + "\n" : "") + wy.ToString();
                }
                File.AppendAllText(Path.Combine(Dir, n + ".log"), "==== " + start.ToString("yyyy-MM-dd HH:mm:ss") + " kod " + kod + "\n" + wynik + "\n", new UTF8Encoding(false));
            }
            catch (Exception ex) { wynik = "błąd: " + ex.Message; }
            finally
            {
                try
                {
                    P.ZBlokada(delegate
                    {
                        var p = Path.Combine(Dir, n + ".json");
                        var d = Pliki.Czytaj(p);
                        var u = P.Lista(d, "uruchomienia").Cast<object>().ToList();
                        u.Add(P.D("czas", start.ToString("yyyy-MM-dd'T'HH:mm:sszzz"), "sekund", (int)(DateTime.Now - start).TotalSeconds, "kod", kod,
                            "wynik", wynik.Length > 400 ? wynik.Substring(0, 400) : wynik));
                        if (u.Count > 50) u = u.Skip(u.Count - 50).ToList();
                        d["uruchomienia"] = u;
                        // identyfikator sesji jest zajęty już po pierwszej próbie (także nieudanej) – dalej tylko --resume
                        if (kod == 0 || wynik.Contains("already in use") || wynik.Contains("\"session_id\"")) d["sesja_zalozona"] = true;
                        bool przezOperatora; lock (trwa) przezOperatora = przerwane.Remove(n);
                        if (przezOperatora) ((Dictionary<string, object>)u[u.Count - 1])["wynik"] = "przerwano przez operatora (Stop / Uruchom ponownie)";
                        if (kod != 0 && !przezOperatora)
                        {
                            DateTimeOffset tA;
                            if (!DateTimeOffset.TryParse(P.S(d, "ostatni_alarm"), out tA) || tA < DateTimeOffset.Now.AddHours(-1))
                            {
                                var m = Regex.Match(wynik, "\"result\":\"([^\"]{0,200})");
                                var powod = m.Success ? m.Groups[1].Value : (wynik.Length > 200 ? wynik.Substring(0, 200) : wynik);
                                P.Wyslij(new P.Nowa { Od = P.Straznik, Do = P.Wlasciciel, Projekt = P.S(d, "projekt"), Modul = P.S(d, "modul"), Typ = "blokada", Temat = "AGENT-W-TLE",
                                    Tresc = "Agent w tle " + n + ": tura nieudana (kod " + kod + "). " + powod.Trim() +
                                            (powod.Contains("authenticate") || powod.Contains("OAuth") ? "\nCLI nie jest zalogowane na tym stanowisku – uruchom w terminalu polecenie claude i zaloguj się." : "") +
                                            "\nKolejna próba najwcześniej za 10 minut. Szczegóły: stan\\agenci\\" + n + ".log" });
                                d["ostatni_alarm"] = P.Teraz();
                            }
                        }
                        Pliki.Zapisz(p, d);
                        P.Zdarzenie("tura_agenta", P.D("nazwa", n, "kod", kod, "sekund", (int)(DateTime.Now - start).TotalSeconds));
                        return 0;
                    });
                }
                catch (Exception ex) { Log("tura " + n + ": " + ex.Message); }
                lock (trwa) { trwa.Remove(n); procesy.Remove(n); }
            }
        }
    }

    // Lustro wpisów operatora z okien czatów (zasada 1:1): wpis w czacie jednej strony pary trafia dosłownie do drugiej.
    //  * Claude: hook UserPromptSubmit (narzedzia\hook-claude.ps1) odkłada wpis do stan\wejscia\, stos go przekazuje.
    //  * Codex: stos czyta (tylko odczyt) zapis rozmów %USERPROFILE%\.codex\sessions\**\rollout-*.jsonl; wpis operatora to
    //    wiadomość użytkownika w turze wywołanej z okna (turn_trigger = composer albo odpowiedź na pytanie), nie z automatyzacji.
    static class Lustro
    {
        public static Action<string> Log = s => { };
        static readonly Regex RePakiet = new Regex(@"Twoja nazwa w ECXP: ((?:codex|claude)-[a-z0-9-]+)");
        static readonly Regex RePrzypisz = new Regex(@"ecxp_przypisz[\s\S]{0,600}?\\*""ja\\*""\s*:\s*\\*""((?:codex|claude)-[a-z0-9-]+)");
        static readonly HashSet<string> Wyzwalacze = new HashSet<string> { "composer", "send_user_message_async_question" };
        static Dictionary<string, long> offsety = new Dictionary<string, long>();
        static readonly Dictionary<string, string> nazwy = new Dictionary<string, string>();
        static readonly Dictionary<string, string> wyzwalacz = new Dictionary<string, string>();
        static readonly DateTime startStosu = DateTime.Now;

        public static void Petla()
        {
            var po = Path.Combine(P.Root, "stan", "lustro-codex.json");
            try { foreach (var kv in Pliki.Czytaj(po)) offsety[kv.Key] = Convert.ToInt64(kv.Value); } catch { }
            int obrot = 0;
            while (true)
            {
                Thread.Sleep(3000);
                try { Wejscia(); } catch (Exception ex) { Log("lustro claude: " + ex.Message); }
                if (++obrot % 2 == 0)
                    try { if (Codex()) Pliki.Zapisz(po, offsety.ToDictionary(k => k.Key, k => (object)k.Value)); } catch (Exception ex) { Log("lustro codex: " + ex.Message); }
            }
        }

        static void Wejscia()
        {
            var dir = Path.Combine(P.Root, "stan", "wejscia");
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir, "*.json").OrderBy(x => x))
            {
                var d = Pliki.Czytaj(f);
                Przekaz(P.S(d, "strona"), P.S(d, "nazwa"), P.S(d, "sesja"), P.S(d, "tekst"));
                var ob = Path.Combine(dir, "obsluzone");
                Directory.CreateDirectory(ob);
                File.Move(f, Path.Combine(ob, Path.GetFileName(f)));
            }
        }

        public static int Przekaz(string strona, string nazwa, string sesja, string tekst)
        {
            nazwa = (nazwa ?? "").Trim().ToLowerInvariant();
            tekst = (tekst ?? "").Trim();
            if (!P.PoprawnyNadawca(nazwa) || tekst.Length == 0 || RePakiet.IsMatch(tekst)) return 0;
            if (tekst.Length > 20000) tekst = tekst.Substring(0, 20000) + "\n[…ucięte]";
            int ile = 0;
            foreach (var t in Siec.Przynaleznosc(nazwa))
            {
                var partner = Siec.Partner(t.Projekt, t.Modul, nazwa);
                if (partner.Length == 0) continue;
                P.Wyslij(new P.Nowa
                {
                    Od = P.Wlasciciel, Do = partner, Projekt = t.Projekt, Modul = t.Modul, Typ = "informacja", Temat = "CZAT-" + strona.ToUpperInvariant(),
                    Tresc = "Operator napisał w czacie " + (strona == "codex" ? "ChatGPT/Codex" : "Claude") + " (" + nazwa + "):\n\n" + tekst,
                    KopiaDla = "czat:" + (sesja ?? ""), Ograniczenia = "Kopia wpisu operatora z okna czatu drugiej strony pary (lustro 1:1)."
                });
                P.Zdarzenie("lustro_czatu", P.D("strona", strona, "nazwa", nazwa, "partner", partner, "sesja", sesja ?? "", "znaki", tekst.Length));
                ile++;
            }
            return ile;
        }

        static bool Codex()
        {
            var baza = Environment.GetEnvironmentVariable("ECXP_CODEX_SESSIONS");
            if (string.IsNullOrEmpty(baza)) baza = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions");
            if (!Directory.Exists(baza)) return false;
            bool zmiana = false;
            var granica = DateTime.Now.AddDays(-2);
            foreach (var f in Directory.EnumerateFiles(baza, "rollout-*.jsonl", SearchOption.AllDirectories))
            {
                FileInfo fi;
                try { fi = new FileInfo(f); } catch { continue; }
                if (fi.LastWriteTime < granica) continue;
                long o;
                if (!offsety.TryGetValue(f, out o))
                {
                    // nowy plik od startu stosu – czytamy od początku; starszy – tylko rozpoznajemy nazwę, bez przekazywania historii
                    o = fi.CreationTime > startStosu ? 0 : fi.Length;
                    if (o > 0) { var m = Nazwa(Czytaj(f, 0, fi.Length)); if (m.Length > 0) nazwy[f] = m; }
                    offsety[f] = o; zmiana = true;
                }
                if (fi.Length <= o) continue;
                var tekst = Czytaj(f, o, fi.Length);
                var koniec = tekst.LastIndexOf('\n');
                if (koniec < 0) continue;
                var porcja = tekst.Substring(0, koniec + 1);
                offsety[f] = o + new UTF8Encoding(false).GetByteCount(porcja); zmiana = true;
                foreach (var linia in porcja.Split('\n'))
                {
                    if (linia.Trim().Length == 0) continue;
                    var n = Nazwa(linia);
                    if (n.Length > 0 && n.StartsWith("codex-")) nazwy[f] = n;
                    Dictionary<string, object> d;
                    try { d = P.Js().Deserialize<Dictionary<string, object>>(linia); } catch { continue; }
                    var pl = d != null && d.ContainsKey("payload") ? d["payload"] as Dictionary<string, object> : null;
                    if (pl == null) continue;
                    if (P.S(d, "type") == "event_msg" && P.S(pl, "type") == "task_started")
                    {
                        var ta = pl.ContainsKey("turn_attribution") ? pl["turn_attribution"] as Dictionary<string, object> : null;
                        wyzwalacz[f] = P.S(ta, "turn_trigger");
                    }
                    else if (P.S(d, "type") == "response_item" && P.S(pl, "type") == "message" && P.S(pl, "role") == "user")
                    {
                        string w, nz;
                        if (!wyzwalacz.TryGetValue(f, out w) || !Wyzwalacze.Contains(w) || !nazwy.TryGetValue(f, out nz)) continue;
                        var txt = string.Concat(P.Lista(pl, "content").Select(c => P.S(c, "text")));
                        if (txt.StartsWith("<environment_context") || txt.StartsWith("<heartbeat")) continue;
                        Przekaz("codex", nz, Path.GetFileNameWithoutExtension(f), txt);
                        wyzwalacz[f] = "";
                    }
                }
            }
            return zmiana;
        }

        static string Nazwa(string tekst)
        {
            var m = RePakiet.Match(tekst);
            if (!m.Success) m = RePrzypisz.Match(tekst);
            return m.Success ? m.Groups[1].Value : "";
        }

        static string Czytaj(string f, long od, long doBajtu)
        {
            using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                fs.Seek(od, SeekOrigin.Begin);
                var n = (int)Math.Min(doBajtu - od, 64L * 1024 * 1024);
                var b = new byte[n];
                int r = 0; while (r < n) { int k = fs.Read(b, r, n - r); if (k <= 0) break; r += k; }
                return new UTF8Encoding(false).GetString(b, 0, r);
            }
        }
    }
}
