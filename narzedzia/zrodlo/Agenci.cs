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

        public static Dictionary<string, object> Uruchom(string nazwa, string projekt, string modul, string katalog, string model, string uprawnienia, List<string> role, string opis, int naGodzine,
            string pakiet = null, List<string> katalogiZapisu = null)
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
                "sesja_zalozona", false, "aktywny", true, "uruchomienia", new List<object>(), "utworzono", P.Teraz(), "opis", opis ?? "",
                "katalogi_zapisu", (katalogiZapisu ?? new List<string>()).Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList());
            Pliki.Zapisz(Path.Combine(Dir, nazwa + ".json"), rec);
            if (pakiet == null) pakiet = Pakiet(rec, role);
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

        // Całkowite zakończenie usług Claude i Codex: obie strony zatrzymane, trwające tury przerwane, zamknięte też
        // procesy agentów w tle pozostałe po poprzednim uruchomieniu stosu. Okna czatu operatora (interaktywne sesje
        // Claude Code i Codex) nie są ruszane – rozpoznajemy wyłącznie procesy uruchomione przez ECXP.
        // przyZamknieciu: konsola się zamyka – usługi, które działały, wznowi następny start konsoli (WznowPoZamknieciu).
        public static Dictionary<string, object> Zakoncz(bool przyZamknieciu)
        {
            int ile = PrzerwijTury("claude") + PrzerwijTury("codex");
            int sieroty = 0;
            try { sieroty = ZamknijOsierocone(); } catch (Exception ex) { Log("zakończenie usług – procesy osierocone: " + ex.Message); }
            var u = Uslugi();
            foreach (var s in new[] { "claude", "codex" })
            {
                bool dzialala = !P.B(u, s + "_zatrzymana");
                u[s + "_zatrzymana"] = true;
                u[s + "_zmiana"] = P.Teraz();
                if (przyZamknieciu) { if (dzialala) u[s + "_wznow_przy_starcie"] = true; }
                else u.Remove(s + "_wznow_przy_starcie");
            }
            Pliki.Zapisz(UslugiPlik, u);
            P.Zdarzenie("uslugi_zakonczone", P.D("przerwane_tury", ile, "osierocone", sieroty, "przy_zamknieciu", przyZamknieciu));
            Log("usługi Claude i Codex zakończone" + (przyZamknieciu ? " (zamknięcie konsoli)" : "") + ": przerwane tury " + ile + ", procesy osierocone " + sieroty);
            return P.D("przerwane_tury", ile, "osierocone", sieroty, "claude", UslugaStan("claude"), "codex", UslugaStan("codex"));
        }

        public static Dictionary<string, object> WznowPoZamknieciu()
        {
            var u = Uslugi();
            var wzn = new List<object>();
            foreach (var s in new[] { "claude", "codex" })
                if (P.B(u, s + "_wznow_przy_starcie"))
                {
                    u[s + "_zatrzymana"] = false; u.Remove(s + "_wznow_przy_starcie");
                    u[s + "_reset"] = P.Teraz(); u[s + "_zmiana"] = P.Teraz();
                    wzn.Add(s);
                }
            if (wzn.Count > 0) { Pliki.Zapisz(UslugiPlik, u); Log("usługi wznowione po starcie konsoli: " + string.Join(", ", wzn)); }
            return P.D("wznowione", wzn);
        }

        // Procesy agentów w tle, których stos nie śledzi (np. tura sprzed restartu stosu):
        // Claude – linia poleceń z plikami z katalogu stan\agenci (--mcp-config / --append-system-prompt-file);
        // Codex – "exec --skip-git-repo-check" w katalogu pracy jednego z agentów Codex.
        static int ZamknijOsierocone()
        {
            var dir = Dir.TrimEnd('\\');
            var katalogi = Lista().Where(x => P.S(x, "strona") == "codex").Select(x => P.S(x, "katalog").TrimEnd('\\')).Where(x => x.Length > 0).ToList();
            var ja = Process.GetCurrentProcess().Id;
            var znalezione = new Dictionary<int, int>();   // pid -> pid rodzica
            using (var q = new System.Management.ManagementObjectSearcher("SELECT ProcessId, ParentProcessId, CommandLine FROM Win32_Process"))
                foreach (System.Management.ManagementObject o in q.Get())
                {
                    var cl = o["CommandLine"] as string;
                    if (string.IsNullOrEmpty(cl)) continue;
                    int pid = Convert.ToInt32(o["ProcessId"]), rodzic = Convert.ToInt32(o["ParentProcessId"]);
                    if (pid == ja) continue;
                    bool agent = cl.IndexOf(dir, StringComparison.OrdinalIgnoreCase) >= 0 &&
                                 (cl.Contains("--mcp-config") || cl.Contains("--append-system-prompt-file"));
                    if (!agent && cl.Contains("exec --skip-git-repo-check"))
                        agent = katalogi.Any(k => cl.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);
                    if (agent) znalezione[pid] = rodzic;
                }
            int n = 0;
            foreach (var kv in znalezione)
            {
                if (znalezione.ContainsKey(kv.Value)) continue;   // dziecko już zamykanego procesu – zniknie z drzewem
                try
                {
                    var k = Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "taskkill.exe"), "/PID " + kv.Key + " /T /F") { UseShellExecute = false, CreateNoWindow = true });
                    k.WaitForExit(10000);
                    n++;
                }
                catch { }
            }
            return n;
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
                var polecenie = "Masz nowe wiadomości w ECXP. Wywołaj ecxp_odbierz(ja=" + n + ") i obsłuż je według zasad z pakietu startowego: " +
                                "na wiadomość, która tego wymaga, odpowiadasz typ=odpowiedz z odpowiedz_na=<jej id>, pozostałe potwierdzasz ecxp_potwierdz. " +
                                "Nie wysyłaj meldunków pośrednich – gdy czekasz na kogoś, po prostu zakończ turę (stos obudzi Cię, gdy przyjdzie odpowiedź).";
                var zapis = P.L(rec, "katalogi_zapisu").Where(Directory.Exists).ToList();
                var pisze = P.S(rec, "uprawnienia") != "odczyt";
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
                           (pisze ? " --permission-mode acceptEdits" : "") +
                           (pisze ? string.Concat(zapis.Select(z => " --add-dir \"" + z.TrimEnd(Path.DirectorySeparatorChar) + "\"")) : "") +
                           (P.S(rec, "model").Length > 0 ? " --model " + P.S(rec, "model") : "") +
                           (P.B(rec, "sesja_zalozona") ? " --resume " : " --session-id ") + P.S(rec, "sesja");
                }
                else
                {
                    polecenie = P.Czytaj(sys) + "\n\n" + polecenie;
                    args = "exec --skip-git-repo-check -C \"" + P.S(rec, "katalog") + "\"" + (pisze ? " -s workspace-write" : " -s read-only") +
                           (pisze ? string.Concat(zapis.Select(z => " --add-dir \"" + z.TrimEnd(Path.DirectorySeparatorChar) + "\"")) : "") +
                           (P.S(rec, "model").Length > 0 ? " -m " + P.S(rec, "model") : "") + " -";
                }
                var psi = prog.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? new ProcessStartInfo(prog, args)
                    : new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /s /c \"\"" + prog + "\" " + args + "\"");
                psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.WorkingDirectory = P.S(rec, "katalog");
                psi.RedirectStandardInput = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = Encoding.UTF8; psi.StandardErrorEncoding = Encoding.UTF8;
                psi.EnvironmentVariables["ECXP_DORECZANIE"] = "1";   // hook-claude.ps1: tura agenta w tle nie jest wpisem operatora (bez lustra)
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
        // nazwa sesji wątku Codexa: ustalona raz (plik → nazwa albo "" = wątek spoza par); dla wątków bez wpisu operatora – długość pliku przy ostatnim sprawdzeniu
        static readonly Dictionary<string, string> glowy = new Dictionary<string, string>();
        static readonly Dictionary<string, long> bezWpisu = new Dictionary<string, long>();
        // własne wywołania ecxp_przypisz w wątku (ja=codex-…) i wszystkie nazwy z pakietów wspomniane w wątku – drugie źródło nazwy
        static readonly Dictionary<string, HashSet<string>> przypiszy = new Dictionary<string, HashSet<string>>();
        static readonly Dictionary<string, HashSet<string>> wspomniane = new Dictionary<string, HashSet<string>>();
        static readonly HashSet<string> przeskanowane = new HashSet<string>();
        static readonly Regex ReWywolanie = new Regex(@"ecxp_przypisz[\s\S]{0,400}?\bja\\*""?\s*[:=]\s*\\*""(codex-[a-z0-9-]+)");

        // Wywołania narzędzia przez asystenta (nie wyniki narzędzi i nie tekst wiadomości): function_call albo custom_tool_call.
        static void SkanujWywolania(string f, string tekst)
        {
            HashSet<string> pz, ws;
            lock (glowy)
            {
                if (!przypiszy.TryGetValue(f, out pz)) przypiszy[f] = pz = new HashSet<string>();
                if (!wspomniane.TryGetValue(f, out ws)) wspomniane[f] = ws = new HashSet<string>();
            }
            foreach (var l in tekst.Split('\n'))
            {
                if (l.IndexOf("Twoja nazwa w ECXP:", StringComparison.Ordinal) >= 0)
                    foreach (Match m in RePakiet.Matches(l)) if (m.Groups[1].Value.StartsWith("codex-")) lock (glowy) ws.Add(m.Groups[1].Value);
                if (l.IndexOf("ecxp_przypisz", StringComparison.Ordinal) < 0) continue;
                if (l.IndexOf("\"type\":\"custom_tool_call\"", StringComparison.Ordinal) < 0 && l.IndexOf("\"type\":\"function_call\"", StringComparison.Ordinal) < 0) continue;
                foreach (Match m in ReWywolanie.Matches(l)) lock (glowy) pz.Add(m.Groups[1].Value);
            }
        }

        // Nazwa wynikająca z własnych wywołań ecxp_przypisz – tylko gdy jednoznaczna i wątek nie pokazuje pakietów innych sesji.
        static string NazwaZPrzypisz(string f)
        {
            lock (glowy)
            {
                HashSet<string> pz, ws;
                if (!przypiszy.TryGetValue(f, out pz) || pz.Count != 1) return "";
                var n = pz.First();
                if (wspomniane.TryGetValue(f, out ws) && ws.Any(x => x != n)) return "";
                return n;
            }
        }

        public static bool ZPakietu(string f, string nazwa) { lock (glowy) { string z; return glowy.TryGetValue(f, out z) && z == nazwa; } }
        static readonly Dictionary<string, string> wyzwalacz = new Dictionary<string, string>();
        static readonly DateTime startStosu = DateTime.Now;

        // Plik rozmowy Codexa rozpoznany jako sesja o tej nazwie ECXP (najnowszy) – dla usługi doręczania.
        public static string PlikRozmowy(string nazwa)
        {
            for (int proba = 0; proba < 3; proba++)
                try
                {
                    var kl = nazwy.ToArray().Where(kv => kv.Value == nazwa).Select(kv => kv.Key).ToList();
                    return kl.Where(File.Exists).OrderByDescending(x => ZPakietu(x, nazwa)).ThenByDescending(File.GetLastWriteTime).FirstOrDefault();
                }
                catch (InvalidOperationException) { Thread.Sleep(50); }   // słownik zmieniany równolegle przez pętlę lustra
            return null;
        }

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

        static readonly Regex ReSystemowe = new Regex(@"^\s*<(task-notification|system-reminder|local-command|command-name|command-message|bash-input|bash-stdout|user-prompt-submit-hook)");

        public static int Przekaz(string strona, string nazwa, string sesja, string tekst)
        {
            nazwa = (nazwa ?? "").Trim().ToLowerInvariant();
            tekst = (tekst ?? "").Trim();
            if (!P.PoprawnyNadawca(nazwa) || tekst.Length == 0 || RePakiet.IsMatch(tekst)) return 0;
            if (ReSystemowe.IsMatch(tekst)) return 0;   // powiadomienia i przypomnienia systemowe to nie wpisy operatora
            if (tekst.StartsWith("[ECXP – doręczenie]")) return 0;   // polecenie tury budzenia ze stosu, nie wpis operatora
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
                    // nowy plik od startu stosu – czytamy od początku; starszy – bez przekazywania historii
                    o = fi.CreationTime > startStosu ? 0 : fi.Length;
                    offsety[f] = o; zmiana = true;
                }
                bool nowyWStosie; lock (glowy) nowyWStosie = przeskanowane.Add(f);
                if (nowyWStosie && o > 0) SkanujWywolania(f, Czytaj(f, 0, o));   // historia sprzed startu stosu – raz
                if (!nazwy.ContainsKey(f)) { var nw = NazwaWatku(f); if (!string.IsNullOrEmpty(nw)) nazwy[f] = nw; }
                if (fi.Length <= o) continue;
                var tekst = Czytaj(f, o, fi.Length);
                var koniec = tekst.LastIndexOf('\n');
                if (koniec < 0) continue;
                var porcja = tekst.Substring(0, koniec + 1);
                offsety[f] = o + new UTF8Encoding(false).GetByteCount(porcja); zmiana = true;
                SkanujWywolania(f, porcja);
                foreach (var linia in porcja.Split('\n'))
                {
                    if (linia.Trim().Length == 0) continue;
                    if (!nazwy.ContainsKey(f)) { var nw = NazwaWatku(f); if (!string.IsNullOrEmpty(nw)) nazwy[f] = nw; }
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
                        txt = BezBlokowSystemowych(txt);
                        if (txt.Length == 0) continue;   // sam blok aplikacji (kontekst, strona, heartbeat) – wpis operatora przyjdzie dalej
                        Przekaz("codex", nz, Path.GetFileNameWithoutExtension(f), txt);
                        wyzwalacz[f] = "";
                    }
                }
            }
            return zmiana;
        }

        // Aplikacja Codex dokleja do tury własne bloki (<environment_context>, <external_codex_apps_…>, <heartbeat> …)
        // jako osobne wiadomości "user". To nie są wpisy operatora – usuwamy je, zostaje tylko jego tekst.
        static readonly System.Text.RegularExpressions.Regex BlokSystemowy = new System.Text.RegularExpressions.Regex(
            @"<(environment_context|heartbeat|external_[a-z_]+|user_instructions|turn_aborted|system[a-z_]*)\b[^>]*?(/>|>.*?</\1\s*>)",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        public static string BezBlokowSystemowych(string txt)
        {
            if (string.IsNullOrEmpty(txt)) return "";
            var t = BlokSystemowy.Replace(txt, "").Trim();
            if (t.StartsWith("<environment_context") || t.StartsWith("<heartbeat")) return "";   // blok bez zamknięcia
            return t;
        }

        // Czyj jest wątek Codexa: sesja pary zaczyna się pakietem startowym, więc liczy się tylko PIERWSZA wiadomość operatora
        // (pomijane są bloki systemowe: kontekst środowiska, AGENTS.md). Wątek, który tylko wyświetla cudze pakiety
        // (np. koordynacja), nie staje się przez to stroną pary. null = brak jeszcze wpisu operatora, "" = wątek spoza par.
        public static string NazwaWatku(string f)
        {
            var n = NazwaWatkuZPakietu(f);
            if (!string.IsNullOrEmpty(n)) return n;
            var zp = NazwaZPrzypisz(f);
            return zp.Length > 0 ? zp : n;
        }

        static string NazwaWatkuZPakietu(string f)
        {
            lock (glowy)
            {
                string z; if (glowy.TryGetValue(f, out z)) return z;
                long dl; long teraz = 0;
                try { teraz = new FileInfo(f).Length; } catch { return null; }
                if (bezWpisu.TryGetValue(f, out dl) && dl == teraz) return null;
                try
                {
                    using (var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var sr = new StreamReader(fs, Encoding.UTF8))
                    {
                        string l; int n = 0;
                        while ((l = sr.ReadLine()) != null && n++ < 400)
                        {
                            if (l.IndexOf("\"role\":\"user\"", StringComparison.Ordinal) < 0) continue;
                            Dictionary<string, object> d;
                            try { d = P.Js().Deserialize<Dictionary<string, object>>(l); } catch { continue; }
                            var pl = d != null && d.ContainsKey("payload") ? d["payload"] as Dictionary<string, object> : null;
                            if (pl == null || P.S(d, "type") != "response_item" || P.S(pl, "type") != "message" || P.S(pl, "role") != "user") continue;
                            var txt = string.Concat(P.Lista(pl, "content").Select(c => P.S(c, "text"))).TrimStart();
                            if (txt.Length == 0 || txt.StartsWith("<") || txt.StartsWith("# AGENTS.md")) continue;
                            var m = RePakiet.Match(txt);
                            var wynik = m.Success && m.Groups[1].Value.StartsWith("codex-") ? m.Groups[1].Value : "";
                            glowy[f] = wynik; bezWpisu.Remove(f);
                            return wynik;
                        }
                    }
                }
                catch { return null; }
                bezWpisu[f] = teraz;
                return null;
            }
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
