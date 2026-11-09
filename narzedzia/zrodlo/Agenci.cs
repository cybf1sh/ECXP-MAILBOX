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
        // Które rekordy agentów obsługuje ta pętla. Stos: wszystkie bez podwyższonych uprawnień; proces podwyższony (UAC):
        // tylko agenci swojego zadania (Ecxpd.Podwyzszone).
        public static Func<Dictionary<string, object>, bool> Obsluguje = rec => P.S(rec, "podwyzszone_klucz").Length == 0;

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

        // Adresy, pod którymi pracuje sesja w aplikacji (ecxp_przypisz z identyfikatorem sesji). Agent w tle ma trasę „tlo:<nazwa>”.
        // Pod takim adresem stos nie może odbierać za agenta w tle: odbiór = ACK, więc wiadomości sesji przepadłyby (04.11.2026).
        public static HashSet<string> SesjeInteraktywne()
        {
            return new HashSet<string>(Siec.Trasy().Where(t => t.Aktywna && (t.Sesja ?? "").Length > 0 && !t.Sesja.StartsWith("tlo:")).Select(t => t.Nazwa));
        }

        // Agenci, za których stos naprawdę odbiera wiadomości: aktywni i bez sesji w aplikacji pod tym samym adresem.
        public static HashSet<string> ObslugiwaniWTle()
        {
            var sesje = SesjeInteraktywne();
            return new HashSet<string>(Lista().Where(x => P.B(x, "aktywny") && !sesje.Contains(P.S(x, "nazwa"))).Select(x => P.S(x, "nazwa")));
        }

        public static Dictionary<string, object> Uruchom(string nazwa, string projekt, string modul, string katalog, string model, string uprawnienia, List<string> role, string opis, int naGodzine,
            string pakiet = null, List<string> katalogiZapisu = null, bool obowiazkowaOdpowiedz = false)
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
            // kontrola sesji, trasa „tlo:” i rekord agenta – jedna sekcja krytyczna z ecxp_przypisz (blokada stosu)
            return P.ZBlokada(() => UruchomPodBlokada(nazwa, strona, projekt, modul, katalog, model, uprawnienia, role, opis, naGodzine, pakiet, katalogiZapisu, obowiazkowaOdpowiedz));
        }

        static Dictionary<string, object> UruchomPodBlokada(string nazwa, string strona, string projekt, string modul, string katalog, string model, string uprawnienia, List<string> role,
            string opis, int naGodzine, string pakiet, List<string> katalogiZapisu, bool obowiazkowaOdpowiedz)
        {
            if (SesjeInteraktywne().Contains(nazwa))
                throw new EcxpException("Adres " + nazwa + " ma już sesję w aplikacji – agent w tle nie został założony. Zadanie czeka w skrzynce tej sesji; stos ją obudzi.");
            var r = Siec.Przypisz(nazwa, projekt, modul, "tlo:" + nazwa, role, opis, P.Wlasciciel);
            var rec = P.D("nazwa", nazwa, "strona", strona, "projekt", r["projekt"], "modul", r["modul"], "katalog", Path.GetFullPath(katalog),
                "model", model ?? "", "uprawnienia", uprawnienia, "na_godzine", Math.Max(1, naGodzine), "sesja", Guid.NewGuid().ToString(),
                "sesja_zalozona", false, "aktywny", true, "uruchomienia", new List<object>(), "utworzono", P.Teraz(), "opis", opis ?? "",
                "obowiazkowa_odpowiedz", obowiazkowaOdpowiedz,
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
            if (akcja != "start") Cieply.Zamknij(x => x.StartsWith(strona + "-"));
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
            Cieply.ZamknijWszystko();
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

        static bool MaOczekujace(string nazwa) { return Oczekujace(nazwa).Count > 0; }

        static List<string> Oczekujace(string nazwa)
        {
            var dir = Path.Combine(P.Root, P.Folder(nazwa));
            if (!Directory.Exists(dir)) return new List<string>();
            var js = P.Js();
            return new DirectoryInfo(dir).GetFiles("*.json").Where(f => P.DoMnie(nazwa, P.Wczytaj(f, "", js).Do)).Select(f => f.Name).OrderBy(x => x).ToList();
        }

        // Pętla w stosie: agent z oczekującymi wiadomościami, bez trwającej tury i w granicy budżetu dostaje jedną turę.
        public static void Petla()
        {
            NasluchSieci();
            Cieply.Log = t => Log(t);
            try { var s = Cieply.SprzatajSieroty(); if (s > 0) Log("ciepli agenci: zamknięte procesy osierocone: " + s); } catch { }
            while (true)
            {
                Thread.Sleep(500);      // szybka reakcja: zadanie trafia do agenta w ułamku sekundy
                KrokPetli();
            }
        }

        // Jeden obieg pętli agentów (osobno, żeby test regresyjny mógł go wywołać bez wątku stosu).
        public static void KrokPetli()
        {
                try
                {
                    // adres z sesją w aplikacji obsługuje ta sesja – stos nie odbiera za agenta w tle o tej samej nazwie
                    var sesje = SesjeInteraktywne();
                    // agent, którego adres przejęła sesja: praca odebrana wcześniej nie może zniknąć – jedno zgłoszenie operatorowi
                    foreach (var prz in Lista().Where(x => P.B(x, "aktywny") && Obsluguje(x) && sesje.Contains(P.S(x, "nazwa")))) PracaPrzejeta(prz);
                    var lista = Lista().Where(x => P.B(x, "aktywny") && Obsluguje(x) && !Zatrzymana(P.S(x, "strona")) && !sesje.Contains(P.S(x, "nazwa"))).ToList();
                    Cieply.Sprzataj(lista.GroupBy(x => P.S(x, "nazwa")).ToDictionary(g => g.Key, g => g.First()));
                    Cieply.PokazOczekujace();          // „Pokaż sesje agentów”: okna Codex / Claude
                    // agenci z otwartym zadaniem (bez FIN) – ich procesy są rozgrzane z wyprzedzeniem (np. sprawdzający, zanim skończy wykonawca)
                    var wObiegu = new HashSet<string>();
                    foreach (var r in Obieg.Otwarte())
                    {
                        object ao;
                        if (r.TryGetValue("agenci", out ao) && ao is Dictionary<string, object>)
                            foreach (var kv in (Dictionary<string, object>)ao) if (P.S(kv.Value as Dictionary<string, object>, "stan") != "fin") wObiegu.Add(kv.Key);
                    }
                    foreach (var rec in lista)
                    {
                        var n = P.S(rec, "nazwa");
                        lock (trwa) if (trwa.Contains(n)) continue;
                        // ECXP przezroczysty: stos odbiera za agenta (ACK), agent dostaje samą treść pracy, bez protokołu.
                        // Informacje (np. podziękowania) kończą się na ACK – bez tury i bez tokenów.
                        if (MaOczekujace(n)) Odbierz(n);
                        Praca praca = null;
                        if (wObiegu.Contains(n) || P.L(Pliki.Czytaj(Path.Combine(Dir, n + ".json")), "pytania").Count > 0) praca = NastepnaPraca(n);
                        if (praca == null)
                        {
                            DateTimeOffset tOst;
                            var ost = P.Lista(rec, "uruchomienia").LastOrDefault();
                            bool niedawno = ost != null && DateTimeOffset.TryParse(P.S(ost, "czas"), out tOst) && tOst > DateTimeOffset.Now.AddMinutes(-Cieply.BezczynnoscMin);
                            if (wObiegu.Contains(n) || niedawno) Cieply.Rozgrzej(rec);
                            continue;
                        }
                        if (OdmowionoPracy(rec, praca.Zadanie != null ? praca.Zadanie.Id : praca.Pytanie.Id)) continue;
                        // gwarancja FIN: tury bez wyniku liczone w obiegu; po MaxPrzypomnien+1 jeden alarm u operatora
                        if (praca.Zadanie != null && Obieg.Przypomnienia(n, praca.Zadanie.Id) > MaxPrzypomnien) { AlarmBrakOdpowiedzi(rec, praca.Zadanie); continue; }
                        int naGodzine; int.TryParse(P.S(rec, "na_godzine"), out naGodzine);
                        var ostatnie = P.Lista(rec, "uruchomienia").Count(u => { DateTimeOffset t; return DateTimeOffset.TryParse(P.S(u, "czas"), out t) && t > DateTimeOffset.Now.AddHours(-1); });
                        if (ostatnie >= Math.Max(1, naGodzine)) continue;
                        // po nieudanej turze ponowienie według przyczyny (Ponowienie): bez pętli i bez zbędnego ruchu
                        if (Ponowienie(rec) > DateTimeOffset.Now) continue;
                        // adres mógł przejść na sesję w aplikacji po migawce – wtedy bez tury w tle; odebrana już praca do operatora
                        if (P.ZBlokada(() => SesjeInteraktywne().Contains(n))) { PracaPrzejeta(rec); continue; }
                        lock (trwa) trwa.Add(n);
                        var kopia = rec; var kopiaPraca = praca;
                        new Thread(() => Tura(kopia, kopiaPraca)) { IsBackground = true, Name = "ecxp-agent-" + n }.Start();
                    }
                }
                catch (Exception ex) { Log("agenci: " + ex.Message); }
        }

        public const int MaxPrzypomnien = 2;

        // Zadanie, na które agent jest winien FIN (zapis obiegu zadania – Obieg.Brakujacy).
        public static Msg BezOdpowiedzi(string n) { return Obieg.Brakujacy(n); }

        // ---------- ponawianie po nieudanej turze ----------
        // Przyczyna z wyniku tury: brak sieci, blokada (antywirus, UAC, odmowa dostępu) albo inny błąd.
        static readonly Regex ReSiec = new Regex(@"ENOTFOUND|ECONNRESET|ECONNREFUSED|ETIMEDOUT|EAI_AGAIN|ENETUNREACH|getaddrinfo|fetch failed|socket hang up|network error|stream disconnected|error sending request|" +
            @"could not resolve|connection (was )?(reset|closed|refused)|nie można rozpoznać|brak połączenia", RegexOptions.IgnoreCase);
        static readonly Regex ReBlokada = new Regex(@"access is denied|odmowa dostępu|EPERM|EACCES|requires elevation|wymaga podniesienia|0xc0000022|virus|wirus|blocked by|zablokowan|operation did not complete successfully", RegexOptions.IgnoreCase);
        // Trwały: odpowiedź modelu zablokowana przez filtr treści albo CLI niezalogowane – ponowienie nic nie zmieni.
        static readonly Regex ReTrwaly = new Regex(@"safety_violation|content_policy|regurg|blocked the output|invalid_api_key|not logged in|please run /login|Invalid API key|OAuth token has expired", RegexOptions.IgnoreCase);
        public static string Przyczyna(int kod, string wynik)
        {
            if (kod == 0) return "";
            if (OdmowaDostawcy(kod, wynik)) return "odmowa";
            if (ReTrwaly.IsMatch(wynik ?? "")) return "trwaly";
            if (kod == 5 || kod == 740 || kod == 225 || ReBlokada.IsMatch(wynik ?? "")) return "blokada";
            if (ReSiec.IsMatch(wynik ?? "")) return "siec";
            return "inny";
        }
        public static bool OdmowaDostawcy(int kod, string wynik)
        {
            if (kod == 0) return false;
            var tekst = (wynik ?? "").Replace("\\\"", "\"");
            return Regex.IsMatch(tekst, @"\bcyberPolicy\b|""stop_reason""\s*:\s*""refusal""|safeguards flagged this session", RegexOptions.IgnoreCase);
        }
        public static bool OdmowionoPracy(Dictionary<string, object> rec, string id)
        {
            return id.Length > 0 && P.L(rec, "odmowy_prac").Contains(id);
        }
        // Tylko bieżąca praca kończy się negatywnie; bez przekazania partnerowi i bez blokowania przyszłych zadań.
        public static void ZakonczOdmowe(Dictionary<string, object> d, Msg zadanie, Msg pytanie)
        {
            var n = P.S(d, "nazwa");
            var praca = zadanie ?? pytanie;
            if (praca == null) return;
            var odmowy = P.L(d, "odmowy_prac");
            if (!odmowy.Contains(praca.Id)) odmowy.Add(praca.Id);
            d["odmowy_prac"] = odmowy;
            // Zapis przed doręczeniem FIN: również błąd doręczenia nie może ponowić wywołania modelu.
            Pliki.Zapisz(Path.Combine(Dir, n + ".json"), d);
            var tekst = "NEGATYWNY — NIEWYKONANE: odmowa dostawcy modelu. Zadanie zakończono bez ponawiania, trybu zastępczego i przekazywania partnerowi. " +
                "To nie jest pozytywny wynik testu. Szczegóły: stan/agenci/" + n + ".log";
            if (zadanie != null)
                Obieg.ZAgenta(new P.Nowa { Od = n, Do = P.Wlasciciel, Projekt = praca.Projekt, Modul = praca.Modul, Typ = "fin", OdpNa = praca.Id, Tresc = tekst });
            else
            {
                Siec.Wyslij(new P.Nowa { Od = n, Do = praca.Od.Length > 0 ? praca.Od : P.Wlasciciel, Projekt = praca.Projekt, Modul = praca.Modul,
                    Typ = "odpowiedz", Temat = praca.Temat, OdpNa = praca.Id, Tresc = tekst });
                var pytania = P.L(d, "pytania"); pytania.Remove(praca.Id); d["pytania"] = pytania;
            }
        }
        static readonly int[] OdstepBlokada = { 5, 30, 120, 600 }, OdstepInny = { 30, 120, 600 };

        // Sieć: tylko lokalny stan karty (bez pakietów). Powrót sieci zgłasza Windows (NetworkChange) – wtedy tura od razu.
        static DateTimeOffset siecWrocila = DateTimeOffset.MinValue;
        static bool nasluchSieci;
        static void NasluchSieci()
        {
            if (nasluchSieci) return; nasluchSieci = true;
            try
            {
                System.Net.NetworkInformation.NetworkChange.NetworkAvailabilityChanged += (s, e) => { if (e.IsAvailable) siecWrocila = DateTimeOffset.Now; };
                System.Net.NetworkInformation.NetworkChange.NetworkAddressChanged += (s, e) => { if (SiecLokalna()) siecWrocila = DateTimeOffset.Now; };
            }
            catch { }
        }
        static bool SiecLokalna() { try { return System.Net.NetworkInformation.NetworkInterface.GetIsNetworkAvailable(); } catch { return true; } }

        // Kiedy wolno uruchomić następną turę po nieudanych (MinValue = od razu).
        public static DateTimeOffset Ponowienie(Dictionary<string, object> rec)
        {
            var reset = Reset(P.S(rec, "strona"));
            var u = P.Lista(rec, "uruchomienia");
            int k = 0; Dictionary<string, object> ost = null; var tOst = DateTimeOffset.MinValue;
            for (int i = u.Count - 1; i >= 0; i--)
            {
                DateTimeOffset t;
                if (!DateTimeOffset.TryParse(P.S(u[i], "czas"), out t) || t <= reset) break;
                if (P.S(u[i], "kod") == "0" || P.S(u[i], "wynik").StartsWith("przerwano przez operatora")) break;
                if (ost == null) { ost = u[i]; tOst = t; }
                k++;
            }
            if (k == 0) return DateTimeOffset.MinValue;
            int kod; int.TryParse(P.S(ost, "kod"), out kod);
            var przyczyna = P.S(ost, "przyczyna").Length > 0 ? P.S(ost, "przyczyna") : Przyczyna(kod, P.S(ost, "wynik"));
            if (przyczyna == "odmowa") return DateTimeOffset.MinValue; // zakończony ID pomija OdmowionoPracy; nowa praca jest dozwolona
            if (przyczyna == "siec")
            {
                if (!SiecLokalna()) return DateTimeOffset.MaxValue;          // czeka na zdarzenie Windows, bez prób
                if (siecWrocila > tOst) return DateTimeOffset.MinValue;      // sieć wróciła po nieudanej turze – od razu
                return tOst.AddSeconds(OdstepInny[Math.Min(k - 1, OdstepInny.Length - 1)]);
            }
            if (przyczyna == "trwaly") return tOst.AddSeconds(OdstepInny[OdstepInny.Length - 1]);   // zadanie przejął partner
            var o = przyczyna == "blokada" ? OdstepBlokada : OdstepInny;
            return tOst.AddSeconds(o[Math.Min(k - 1, o.Length - 1)]);
        }

        static int NieudaneZRzedu(List<Dictionary<string, object>> u)
        {
            int k = 0;
            for (int i = u.Count - 1; i >= 0 && P.S(u[i], "kod") != "0" && !P.S(u[i], "wynik").StartsWith("przerwano przez operatora"); i--) k++;
            return k;
        }

        static void AlarmBrakOdpowiedzi(Dictionary<string, object> rec, Msg zad)
        {
            var n = P.S(rec, "nazwa");
            try
            {
                P.ZBlokada(delegate
                {
                    if (Obieg.Alarmowano(n, zad.Id)) return 0;
                    var d = Pliki.Czytaj(Path.Combine(Dir, n + ".json"));
                    var ost = P.Lista(d, "uruchomienia").LastOrDefault();
                    var w = ost == null ? "" : P.S(ost, "wynik");
                    P.Wyslij(new P.Nowa { Od = P.Straznik, Do = P.Wlasciciel, Projekt = zad.Projekt, Modul = zad.Modul, Typ = "blokada", Temat = "BRAK FIN", OdpNa = zad.Id,
                        Tresc = n + " nie dał FIN zadania „" + zad.Temat + "” mimo " + MaxPrzypomnien + " przypomnień." + (w.Length > 0 ? " Ostatnia tura: " + (w.Length > 200 ? w.Substring(0, 200) : w).Trim() : "") +
                                "\nSzczegóły: stan\\agenci\\" + n + ".log" });
                    Log("agenci: " + n + " – brak odpowiedzi na " + zad.Id + ", alarm u operatora");
                    return 0;
                });
            }
            catch (Exception ex) { Log("agenci: alarm braku odpowiedzi " + n + ": " + ex.Message); }
        }

        // Praca agenta w tle: zadanie z obiegu (wynik = FIN) albo pytanie operatora (wynik = odpowiedź).
        class Praca { public Msg Zadanie, Pytanie; public string Tekst = ""; }

        // Stos odbiera wiadomości za agenta (każdy odbiór = ACK). Pytania i odpowiedzi operatora czekają na turę.
        // Pytanie, zadanie, recenzja albo blokada od innego agenta też czeka na turę (odpowiedź do nadawcy) – odbiór
        // nie może jej połknąć. Zadania operatora prowadzi obieg (Obieg), informacje kończą się na ACK.
        public static bool WymagaTury(string od, string typ)
        {
            if (od == P.Wlasciciel) return typ == "pytanie" || typ == "odpowiedz";
            return od != P.Straznik && (typ == "pytanie" || typ == "zadanie" || typ == "recenzja" || typ == "blokada");
        }

        // Praca odebrana przez agenta w tle przed przejęciem adresu przez sesję w aplikacji (pytania, zadanie bez FIN):
        // agent jej już nie wykona (jeden właściciel), więc operator dostaje jedno zgłoszenie na każdą pozycję.
        public static void PracaPrzejeta(Dictionary<string, object> rec)
        {
            var n = P.S(rec, "nazwa");
            P.ZBlokada(delegate
            {
                var p = Path.Combine(Dir, n + ".json");
                var d = Pliki.Czytaj(p);
                var zgloszone = P.L(d, "przejete_zgloszone");
                var ids = P.L(d, "pytania").ToList();
                var zad = Obieg.Brakujacy(n);
                if (zad != null)
                {
                    // zadanie odebrał agent tylko, jeśli doręczono je przed zgłoszeniem sesji (potem odbiera już sama sesja)
                    var odSesji = Siec.Trasy().Where(t => t.Aktywna && t.Nazwa == n && (t.Sesja ?? "").Length > 0 && !t.Sesja.StartsWith("tlo:")).Select(t => t.Od).DefaultIfEmpty(DateTime.MaxValue).Max();
                    string spoj; Stan s;
                    if (P.Stany(out spoj).TryGetValue(zad.Id, out s) && s.Dostarczona.HasValue && s.Dostarczona.Value < odSesji) ids.Add(zad.Id);
                }
                var nowe = ids.Where(x => x.Length > 0 && !zgloszone.Contains(x)).Distinct().ToList();
                if (nowe.Count == 0) return 0;
                var opis = nowe.Select(id => { var m = P.Znajdz(id); return "• " + id + (m != null ? " – " + m.Temat + " (od " + m.Od + ")" : ""); });
                P.Wyslij(new P.Nowa { Od = P.Straznik, Do = P.Wlasciciel, Projekt = P.S(d, "projekt"), Modul = P.S(d, "modul"), Typ = "blokada", Temat = "PRACA-AGENTA-W-TLE-PRZEJETA",
                    Tresc = "Adres " + n + " obsługuje teraz sesja w aplikacji, więc agent w tle nie wykona pracy odebranej wcześniej:\n" + string.Join("\n", opis) +
                            "\nPrzekaż ją sesji (prawy przycisk na wiadomości: ↪ Wyślij ponownie) albo odpowiedz sam. Nic nie zostało skasowane." });
                zgloszone.AddRange(nowe); d["przejete_zgloszone"] = zgloszone; Pliki.Zapisz(p, d);
                P.Zdarzenie("agent_praca_przejeta", P.D("nazwa", n, "ids", nowe.Cast<object>().ToList()));
                return 0;
            });
        }

        static void Odbierz(string n)
        {
            P.ZBlokada(delegate
            {
                // właściciel sprawdzany pod tą samą blokadą co odbiór (ACK): sesja zgłoszona po migawce pętli wygrywa
                if (SesjeInteraktywne().Contains(n)) return 0;
                var odebrane = P.Odbierz(n, 50);
                var pyt = odebrane.Where(x => WymagaTury(P.S(x, "od"), P.S(x, "typ"))).Select(x => P.S(x, "id")).ToList();
                if (pyt.Count > 0)
                {
                    var p = Path.Combine(Dir, n + ".json"); var d = Pliki.Czytaj(p);
                    var l = P.L(d, "pytania"); l.AddRange(pyt.Where(x => !l.Contains(x))); d["pytania"] = l; Pliki.Zapisz(p, d);
                }
                return 0;
            });
        }

        static Praca NastepnaPraca(string n)
        {
            var d = Pliki.Czytaj(Path.Combine(Dir, n + ".json"));
            foreach (var id in P.L(d, "pytania"))
            {
                var q = P.Znajdz(id);
                if (q != null) return new Praca { Pytanie = q, Tekst = q.Od == P.Wlasciciel ? q.Uwagi
                    : "Wiadomość od " + q.Od + " (" + q.Typ + ", temat " + q.Temat + "). Odpowiedz na nią:\n\n" + q.Uwagi };
            }
            var zad = Obieg.Brakujacy(n);
            if (zad == null) return null;
            if (Obieg.Rola(n, zad.Id) == "sprawdzajacy")
                return new Praca { Zadanie = zad, Tekst = "Sprawdź, czy to zadanie zostało wykonane poprawnie i dokładnie. Odpowiedz krótko: POZYTYWNY albo NEGATYWNY i dlaczego.\n\nZadanie:\n" +
                    zad.Uwagi + "\n\nWykonawca zgłosił:\n" + Obieg.FinWykonawcy(zad.Id) };
            return new Praca { Zadanie = zad, Tekst = zad.Uwagi };
        }

        // Ostatnia odpowiedź agenta: Claude – pole result z JSON, Codex – plik z -o.
        static string WynikTury(string strona, string wyjscie, string plikCodex)
        {
            if (strona == "codex") { try { return File.Exists(plikCodex) ? File.ReadAllText(plikCodex, Encoding.UTF8).Trim() : ""; } catch { return ""; } }
            foreach (var linia in (wyjscie ?? "").Split('\n').Reverse())
            {
                var t = linia.Trim();
                if (!t.StartsWith("{") || !t.Contains("\"result\"")) continue;
                try { var j = P.Js().Deserialize<Dictionary<string, object>>(t); if (!P.B(j, "is_error")) return P.S(j, "result").Trim(); } catch { }
            }
            return "";
        }

        static void Tura(Dictionary<string, object> rec, Praca praca)
        {
            var n = P.S(rec, "nazwa");
            var start = DateTime.Now;
            int kod = -1; string wynik = "", odpowiedz = "";
            Msg przypomnij = praca.Zadanie;
            try
            {
                var strona = P.S(rec, "strona");
                var prog = Program(strona);
                if (prog.Length == 0) throw new EcxpException("brak " + strona + " CLI");
                // agent dostaje samą treść pracy – jak od człowieka; ECXP jest dla niego niewidoczny
                var polecenie = praca.Tekst;
                if (P.B(rec, "pentest")) polecenie = "Twój identyfikator testera/recenzenta: " + n +
                    ". Zapisuj własne pliki z tym identyfikatorem w nazwie.\n\n" + polecenie;
                // ciepły proces agenta (Cieply): bez zimnego startu na ścieżce zadania
                var cw = Cieply.Wykonaj(rec, polecenie, 30, pr => { lock (trwa) procesy[n] = pr; });
                kod = cw.Kod; wynik = cw.Wyjscie; odpowiedz = cw.Odpowiedz;
                // tokeny tury w tle (Claude: usage z wyniku JSON); Codex w tle – z jego zapisu rozmowy (lustro)
                if (strona == "claude")
                    try { var tt = Tury.ZWynikuClaude(wynik); if (tt != null) { tt.Nazwa = n; tt.Strona = "claude"; tt.Zrodlo = "tlo"; tt.Czas = DateTime.Now; Tury.Zapisz(tt); } } catch { }
                if (kod == 0 && odpowiedz.Length == 0) { kod = 2; wynik = "bez wyniku: tura zakończona bez odpowiedzi agenta.\n" + wynik; }
                File.AppendAllText(Path.Combine(Dir, n + ".log"), "==== " + start.ToString("yyyy-MM-dd HH:mm:ss") + " kod " + kod + " (" + (int)(DateTime.Now - start).TotalMilliseconds + " ms)\n" + wynik + "\n", new UTF8Encoding(false));
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
                        var przyczyna = Przyczyna(kod, wynik);
                        if (przyczyna == "odmowa")
                        {
                            odpowiedz = "";
                            ZakonczOdmowe(d, praca.Zadanie, praca.Pytanie);
                        }
                        // wynik agenta zamienia stos: zadanie – FIN w obiegu, pytanie – odpowiedź do nadawcy (operatora albo agenta)
                        if (odpowiedz.Length > 0)
                        {
                            try
                            {
                                if (praca.Zadanie != null)
                                    Obieg.ZAgenta(new P.Nowa { Od = n, Do = P.Wlasciciel, Projekt = praca.Zadanie.Projekt, Modul = praca.Zadanie.Modul, Typ = "fin", OdpNa = praca.Zadanie.Id, Tresc = odpowiedz });
                                else if (praca.Pytanie != null)
                                    Siec.Wyslij(new P.Nowa { Od = n, Do = praca.Pytanie.Od.Length > 0 ? praca.Pytanie.Od : P.Wlasciciel, Projekt = praca.Pytanie.Projekt, Modul = praca.Pytanie.Modul, Typ = "odpowiedz", Temat = praca.Pytanie.Temat, OdpNa = praca.Pytanie.Id, Tresc = odpowiedz });
                            }
                            catch (Exception ex) { Log("agenci: " + n + " – wynik nie przekazany: " + ex.Message); }
                            if (praca.Pytanie != null) { var l = P.L(d, "pytania"); l.Remove(praca.Pytanie.Id); d["pytania"] = l; }
                        }
                        else if (kod == 2 && praca.Zadanie != null) Obieg.Przypomniano(n, praca.Zadanie.Id);
                        if (przyczyna == "trwaly")
                        {
                            // komunikat także z JSON zagnieżdżonego w JSON (Codex app-server: \"message\":\"…\")
                            var mt = Regex.Match(wynik, @"message\\*""\s*:\s*\\*""([^""\\]{3,160})");
                            var powod = mt.Success ? mt.Groups[1].Value : "błąd trwały (" + Regex.Match(wynik, ReTrwaly.ToString(), RegexOptions.IgnoreCase).Value + ")";
                            var przek = Obieg.Przekaz(n, powod);
                            if (przek.Count > 0) Log("agenci: " + n + " – błąd trwały, zadanie przekazane partnerowi: " + string.Join(", ", przek));
                        }
                        u.Add(P.D("przyczyna", przyczyna, "czas", start.ToString("yyyy-MM-dd'T'HH:mm:sszzz"), "praca", praca.Zadanie != null ? praca.Zadanie.Id : praca.Pytanie != null ? praca.Pytanie.Id : "", "sekund", (int)(DateTime.Now - start).TotalSeconds, "kod", kod,
                            "wynik", wynik.Length > 400 ? wynik.Substring(0, 400) : wynik));
                        if (u.Count > 50) u = u.Skip(u.Count - 50).ToList();
                        d["uruchomienia"] = u;
                        bool przezOperatora; lock (trwa) przezOperatora = przerwane.Remove(n);
                        if (przezOperatora) ((Dictionary<string, object>)u[u.Count - 1])["wynik"] = "przerwano przez operatora (Stop / Uruchom ponownie)";
                        // alarm dopiero po 3 nieudanych turach z rzędu i najwyżej raz na godzinę – stos ponawia sam
                        if (kod != 0 && !przezOperatora && przyczyna != "trwaly" && przyczyna != "odmowa" && NieudaneZRzedu(u.Cast<Dictionary<string, object>>().ToList()) >= 3)
                        {
                            DateTimeOffset tA;
                            if (!DateTimeOffset.TryParse(P.S(d, "ostatni_alarm"), out tA) || tA < DateTimeOffset.Now.AddHours(-1))
                            {
                                var m = Regex.Match(wynik, "\"result\":\"([^\"]{0,200})");
                                var powod = m.Success ? m.Groups[1].Value : (wynik.Length > 200 ? wynik.Substring(0, 200) : wynik);
                                P.Wyslij(new P.Nowa { Od = P.Straznik, Do = P.Wlasciciel, Projekt = P.S(d, "projekt"), Modul = P.S(d, "modul"), Typ = "blokada", Temat = "AGENT-W-TLE",
                                    Tresc = "Agent w tle " + n + ": 3 nieudane tury z rzędu (" + (przyczyna == "siec" ? "brak sieci" : przyczyna == "blokada" ? "blokada antywirusa, UAC albo uprawnień" : "kod " + kod) + "). " + powod.Trim() +
                                            (powod.Contains("authenticate") || powod.Contains("OAuth") ? "\nCLI nie jest zalogowane na tym stanowisku – uruchom w terminalu polecenie claude i zaloguj się." : "") +
                                            "\nStos ponawia sam" + (przyczyna == "siec" ? " – od razu, gdy Windows zgłosi powrót sieci" : " (co 2–10 min)") + ". Szczegóły: stan\\agenci\\" + n + ".log" });
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
                try { Tury.PrzetworzSygnaly(); } catch (Exception ex) { Log("tury claude: " + ex.Message); }
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
            // jedna kopia na partnera: nazwa w kilku trasach (np. sesja w aplikacji i stara trasa agenta w tle) nie dubluje wpisu;
            // pierwsza wygrywa trasa sesji w aplikacji
            var wyslano = new HashSet<string>();
            foreach (var t in Siec.Przynaleznosc(nazwa).OrderBy(x => (x.Sesja ?? "").StartsWith("tlo:") ? 1 : 0))
            {
                var partner = Siec.Partner(t.Projekt, t.Modul, nazwa);
                if (partner.Length == 0 || !wyslano.Add(partner)) continue;
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
                    if (P.S(d, "type") == "event_msg")
                    {
                        // tury i tokeny Codexa (licznik narastający token_count, początek i koniec tury)
                        var tz = P.S(pl, "type");
                        if (tz == "token_count" || tz == "task_started" || tz == "task_complete")
                        {
                            string nzT; nazwy.TryGetValue(f, out nzT);
                            DateTime czT; if (!DateTime.TryParse(P.S(d, "timestamp"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out czT)) czT = DateTime.Now;
                            try { Tury.ZdarzenieCodex(f, nzT ?? "", pl, czT.Kind == DateTimeKind.Utc ? czT.ToLocalTime() : czT); } catch { }
                        }
                    }
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
