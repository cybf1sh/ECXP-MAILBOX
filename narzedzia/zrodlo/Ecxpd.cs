// ecxpd.exe – stos ECXP. Jedyny proces, który pisze w skrzynce. Serwer potoku nazwanego (tylko bieżący użytkownik),
// uzgadnianie połączeń, wykonywanie poleceń klientów (konsola ECXP-MAILBOX, adaptery ecxp-mcp), wypychanie zmian
// do konsoli bez odpytywania, strażnik terminów potwierdzeń. Uruchamia go konsola albo adapter, gdy go nie ma.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;

[assembly: AssemblyTitle("ecxpd")]
[assembly: AssemblyDescription("Stos ECXP – Evidsec Context eXchange Protocol")]
[assembly: AssemblyProduct("ECXP")]
[assembly: AssemblyCompany("Evidsec")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace Ecxp
{
    static class Stos
    {
        public const string Znacznik = "🔗ECXP";   // wspólny znacznik sesji Codexa i Claude połączonych przez ECXP
        static readonly object stos = new object();
        static readonly List<Sesja> sesje = new List<Sesja>();
        static string katalog, obrazKonsoli, nazwaPotoku;
        static long licznik;
        static volatile int zmiana;

        class Sesja
        {
            public NamedPipeServerStream Pipe;
            public string Ja = "", Rodzaj = "", Obraz = "";
            public bool Operator;
            public ulong Id, Seq, AckOd;
            public readonly object W = new object();
        }

        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint pid);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

        static string ObrazProcesu(NamedPipeServerStream p)
        {
            uint pid;
            if (!GetNamedPipeClientProcessId(p.SafePipeHandle.DangerousGetHandle(), out pid)) return "";
            var h = OpenProcess(0x1000, false, pid);
            if (h == IntPtr.Zero) return "";
            try { var sb = new StringBuilder(1024); int n = sb.Capacity; return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString() : ""; }
            finally { CloseHandle(h); }
        }

        static void Log(string t)
        {
            try { File.AppendAllText(Path.Combine(P.Root, "stan", "ecxpd.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + t + Environment.NewLine, new UTF8Encoding(false)); } catch { }
        }

        [STAThread]
        static int Main(string[] args)
        {
            katalog = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            P.Root = Path.GetDirectoryName(katalog);
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "/root") P.Root = Path.GetFullPath(args[i + 1]);
            obrazKonsoli = Path.Combine(katalog, "ECXP-MAILBOX.exe");
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "/podwyzszone") return Podwyzszone(args[i + 1]);
            nazwaPotoku = Ramka.NazwaPotoku(P.Root);
            bool nowy;
            using (var mutex = new Mutex(true, @"Local\ECXPD-" + nazwaPotoku, out nowy))
            {
                if (!nowy) return 0;
                foreach (var d in P.Foldery) Directory.CreateDirectory(Path.Combine(P.Root, d));
                Directory.CreateDirectory(Path.Combine(P.Root, "stan"));
                Siec.RoleDomyslnePliki();
                Znaczniki();
                P.PoZmianieZadania = (id, stan, kto, uwagi) => { try { Lancuchy.PoZmianieZadania(id, stan, kto, uwagi); } catch (Exception ex) { Log("łańcuch: " + ex.Message); } };
                Agenci.Log = Log; Lustro.Log = Log;
                new Thread(Agenci.Petla) { IsBackground = true, Name = "ecxp-agenci" }.Start();
                new Thread(Lustro.Petla) { IsBackground = true, Name = "ecxp-lustro" }.Start();
                Log("start, potok " + nazwaPotoku + ", katalog " + P.Root);
                for (int i = 0; i < 4; i++) new Thread(Akceptuj) { IsBackground = true, Name = "ecxp-akceptuj" }.Start();
                new Thread(Straznik) { IsBackground = true, Name = "ecxp-straznik" }.Start();
                Doreczanie.Log = Log; Doreczanie.Blokada = stos;
                Doreczanie.Alarm = (id, kto, temat, opis) => { Wypchnij(P.D("zdarzenie", "alarm", "id", id, "do", kto, "temat", temat, "opis", opis, "rodzaj", "niedoreczona")); Wypchnij(P.D("zdarzenie", "zmiana")); };
                new Thread(Doreczanie.Petla) { IsBackground = true, Name = "ecxp-doreczanie" }.Start();
                Widok.Log = Log; Widok.Blokada = stos;
                new Thread(Widok.Petla) { IsBackground = true, Name = "ecxp-widok" }.Start();
                var fsw = new FileSystemWatcher(P.Root) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite, InternalBufferSize = 65536 };
                FileSystemEventHandler h = (s, e) => { if (Istotne(e.FullPath)) { zmiana = 1; if (DlaWidoku(e.FullPath)) Widok.Zmiana = 1; } };
                fsw.Created += h; fsw.Changed += h; fsw.Deleted += h;
                fsw.Renamed += (s, e) => { if (Istotne(e.FullPath)) { zmiana = 1; if (DlaWidoku(e.FullPath)) Widok.Zmiana = 1; } };
                fsw.EnableRaisingEvents = true;
                while (true)
                {
                    Thread.Sleep(150);
                    if (zmiana == 1) { zmiana = 0; Wypchnij(P.D("zdarzenie", "zmiana")); }
                }
            }
        }

        // Widok (Odebrane / Wysłane) zależy tylko od wiadomości i dziennika – zapisy w stan\ go nie odświeżają (co 60 s i tak jest pełny przegląd).
        static bool DlaWidoku(string p)
        {
            var l = (p ?? "").ToLowerInvariant();
            return l.Contains("\\do-") || l.Contains("\\przeczytane\\") || l.Contains("\\archiwum\\") || l.EndsWith("\\dziennik\\dziennik.jsonl");
        }

        static bool Istotne(string p)
        {
            var l = (p ?? "").ToLowerInvariant();
            return !l.EndsWith(".tmp") && !l.Contains("\\narzedzia\\") && !l.EndsWith("ecxpd.log") && !l.Contains("\\.blokada") && !Widok.JestWidokiem(p) && !l.Contains("\\stan\\logi\\");
        }

        static NamedPipeServerStream NowyPotok()
        {
            var sec = new PipeSecurity();
            sec.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
            return new NamedPipeServerStream(nazwaPotoku, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 65536, 65536, sec);
        }

        static void Akceptuj()
        {
            while (true)
            {
                NamedPipeServerStream p = null;
                try
                {
                    p = NowyPotok();
                    p.WaitForConnection();
                    var s = new Sesja { Pipe = p };
                    new Thread(() => Obsluz(s)) { IsBackground = true, Name = "ecxp-sesja" }.Start();
                }
                catch (Exception ex) { Log("akceptuj: " + ex.Message); if (p != null) p.Dispose(); Thread.Sleep(200); }
            }
        }

        static void Wyslij(Sesja s, Rama r)
        {
            r.Polaczenie = s.Id;
            var b = Ramka.Koduj(r);
            lock (s.W) { s.Pipe.Write(b, 0, b.Length); s.Pipe.Flush(); }
        }

        static void Obsluz(Sesja s)
        {
            try
            {
                s.Pipe.ReadMode = PipeTransmissionMode.Byte;
                var h = Ramka.Czytaj(s.Pipe);
                var hj = h.Json();
                if (h.Typ != Ramka.HELLO || (h.Flagi & Ramka.F_SYN) == 0) throw new EcxpException("Oczekiwano HELLO z SYN.");
                s.Rodzaj = P.S(hj, "rodzaj");
                s.Obraz = ObrazProcesu(s.Pipe);
                s.Id = (ulong)Interlocked.Increment(ref licznik) | ((ulong)Ramka.Isn() << 20);
                if (s.Rodzaj == "konsola")
                {
                    if (!string.Equals(s.Obraz, obrazKonsoli, StringComparison.OrdinalIgnoreCase))
                    {
                        Wyslij(s, new Rama { Typ = Ramka.RST, Flagi = Ramka.F_RST, Ack = h.Seq + 1 }.Z(P.D("blad", "Rola operatora przysługuje tylko konsoli " + obrazKonsoli + ". Proces: " + s.Obraz)));
                        Log("odmowa operatora dla " + s.Obraz);
                        return;
                    }
                    s.Operator = true; s.Ja = P.Wlasciciel;
                }
                else if (s.Rodzaj == "mcp" || s.Rodzaj == "cli") s.Ja = "*";
                else throw new EcxpException("Nieznany rodzaj klienta '" + s.Rodzaj + "'.");
                s.Seq = Ramka.Isn();
                s.AckOd = h.Seq + 1;
                Wyslij(s, new Rama { Typ = Ramka.WITAJ, Flagi = (ushort)(Ramka.F_SYN | Ramka.F_ACK), Seq = s.Seq, Ack = s.AckOd }.Z(P.D("ja", s.Ja, "protokol", P.Protokol, "stos", "ecxpd 1.0")));
                var a = Ramka.Czytaj(s.Pipe);
                if (a.Typ != Ramka.ACK || a.Ack != s.Seq + 1) throw new EcxpException("Brak ACK uzgodnienia.");
                s.Seq++; s.AckOd = a.Seq;
                lock (sesje) sesje.Add(s);
                Log("połączenie " + s.Rodzaj + " " + s.Ja + " (" + s.Obraz + ")");
                while (true)
                {
                    var r = Ramka.Czytaj(s.Pipe);
                    if (r.Seq < s.AckOd && r.Typ == Ramka.ZAPYTANIE) continue;                 // duplikat
                    if (r.Seq >= s.AckOd) s.AckOd = r.Seq + 1;
                    if (r.Typ == Ramka.ZAPYTANIE)
                    {
                        Dictionary<string, object> odp;
                        try
                        {
                            var j = r.Json();
                            var args = j.ContainsKey("args") ? j["args"] as Dictionary<string, object> : null;
                            odp = P.D("ok", true, "wynik", Wykonaj(s, P.S(j, "op"), args ?? new Dictionary<string, object>()));
                        }
                        catch (Exception ex) { odp = P.D("ok", false, "blad", ex is EcxpException ? ex.Message : ex.GetType().Name + ": " + ex.Message); }
                        Wyslij(s, new Rama { Typ = Ramka.ODPOWIEDZ, Flagi = Ramka.F_ACK, Seq = ++s.Seq, Ack = r.Seq + 1 }.Z(odp));
                    }
                    else if (r.Typ == Ramka.PING) Wyslij(s, new Rama { Typ = Ramka.PONG, Flagi = Ramka.F_ACK, Seq = ++s.Seq, Ack = r.Seq + 1 });
                    else if (r.Typ == Ramka.FIN) { Wyslij(s, new Rama { Typ = Ramka.FIN, Flagi = (ushort)(Ramka.F_FIN | Ramka.F_ACK), Seq = ++s.Seq, Ack = r.Seq + 1 }); break; }
                    else if (r.Typ == Ramka.RST) break;
                }
            }
            catch (EndOfStreamException) { if (s.Rodzaj.Length == 0) Log("połączenie zamknięte przed HELLO (klient ponowi uzgodnienie)"); }
            catch (Exception ex)
            {
                Log("sesja " + s.Rodzaj + ": " + ex.Message);
                try { Wyslij(s, new Rama { Typ = Ramka.BLAD, Flagi = Ramka.F_RST, Seq = ++s.Seq }.Z(P.D("ok", false, "blad", ex.Message))); } catch { }
            }
            finally
            {
                lock (sesje) sesje.Remove(s);
                try { s.Pipe.Dispose(); } catch { }
            }
        }

        static void Wypchnij(Dictionary<string, object> d)
        {
            List<Sesja> kopia;
            lock (sesje) kopia = sesje.Where(x => x.Operator).ToList();
            foreach (var s in kopia)
                try { Wyslij(s, new Rama { Typ = Ramka.DANE, Flagi = Ramka.F_PSH, Seq = ++s.Seq, Ack = s.AckOd }.Z(d)); }
                catch { }
        }

        static void Straznik()
        {
            while (true)
            {
                Thread.Sleep(15000);
                try
                {
                    List<P.Alarm> al;
                    lock (stos)
                    {
                        al = P.Straz();
                        // nadzór obiegu zadań w sesjach aplikacji (agentów w tle pilnuje Agenci.Petla)
                        var wTle = Agenci.ObslugiwaniWTle();
                        al.AddRange(P.ZBlokada(() => Obieg.Nadzor(wTle)));
                        foreach (var k in Agenci.Lista().Select(x => P.S(x, "podwyzszone_klucz")).Where(x => x.Length > 0).Distinct().ToList())
                        {
                            var po = Path.Combine(P.Root, "stan", "obieg", k + ".json");
                            bool zak = true; try { zak = !File.Exists(po) || P.B(Pliki.Czytaj(po), "zakonczone"); } catch { }
                            if (zak && !System.Diagnostics.Process.GetProcessesByName("ecxpd").Any(pr => { try { return pr.Id != System.Diagnostics.Process.GetCurrentProcess().Id && pr.StartTime > DateTime.Now.AddHours(-3.1); } catch { return true; } }))
                                WygasUprawnienia(k, "zadanie zakończone");
                        }
                    }
                    foreach (var a in al) Wypchnij(P.D("zdarzenie", "alarm", "id", a.Id, "do", a.Do, "temat", a.Temat, "opis", a.Opis));
                    if (al.Count > 0) Wypchnij(P.D("zdarzenie", "zmiana"));
                }
                catch (Exception ex) { Log("strażnik: " + ex.Message); }
            }
        }

        // Znaczniki 🔗ECXP: sesje w parach (zasada 1:1). Okno i sesje Claude stosują je do nazw sesji.
        static void Znaczniki()
        {
            try
            {
                var lista = new List<object>();
                foreach (var t in Siec.Trasy().Where(x => x.Aktywna))
                {
                    var partner = Siec.Partner(t.Projekt, t.Modul, t.Nazwa);
                    lista.Add(P.D("znacznik", Znacznik, "strona", t.Strona, "nazwa", t.Nazwa, "sesja", t.Sesja, "projekt", t.Projekt, "modul", t.Modul,
                        "partner", partner, "para_pelna", partner.Length > 0, "role", t.Role));
                }
                var p = Path.Combine(P.Root, "stan", "znaczniki.json");
                var tmp = p + ".tmp";
                File.WriteAllText(tmp, P.Js().Serialize(P.D("znacznik", Znacznik, "sesje", lista, "aktualizacja", P.Teraz())), new UTF8Encoding(false));
                if (File.Exists(p)) File.Replace(tmp, p, null); else File.Move(tmp, p);
            }
            catch (Exception ex) { Log("znaczniki: " + ex.Message); }
        }

        // ---------- polecenia ----------
        static string Agent(Sesja s, Dictionary<string, object> a)
        {
            if (s.Operator) return P.Wlasciciel;
            var ja = P.S(a, "ja").Trim().ToLowerInvariant();
            if (!P.PoprawnyNadawca(ja)) throw new EcxpException("Pole ja = '" + ja + "' jest niepoprawne. Użyj codex-<nazwa> albo claude-<nazwa>. Nazwy 'wlasciciel' i 'ecxp-straznik' są zastrzeżone.");
            return ja;
        }

        // Nowe zadanie w trybie „w tle”: każda strona dostaje agenta, którego tury uruchamia stos (Agenci.Petla), gdy w kolejce
        // czeka wiadomość – bez okien aplikacji i bez budzenia przez operatora. Katalog pracy: projekty\<projekt>; zapis także
        // w istniejących katalogach ścieżek, które operator podał w treści zadania (np. plik na pulpicie).
        // claudeWAplikacji: strona Claude nie pracuje w tle – konsola otwiera dla niej sesję w aplikacji Claude (widoczną na pasku),
        // która sama odbiera zadanie i daje FIN przez MCP ECXP.
        static List<object> AgenciWTle(Dictionary<string, object> r, string opis, List<string> roleCodex, List<string> roleClaude, string podwyzszone = "", bool pokazSesje = false, bool claudeWAplikacji = false)
        {
            var projekt = P.S(r, "projekt"); var modul = P.S(r, "modul");
            var katalog = Path.Combine(P.Root, "projekty", projekt);
            Directory.CreateDirectory(katalog);
            var zapis = KatalogiZTresci(opis);
            var wynik = new List<object>();
            var pk = (Dictionary<string, object>)r["pakiety"];
            foreach (var strona in pk.Keys.ToList())
            {
                var p = (Dictionary<string, object>)pk[strona];
                if (strona == "claude" && claudeWAplikacji)
                {
                    Cieply.ZaufajClaude(katalog);
                    wynik.Add(P.D("nazwa", P.S(p, "nazwa"), "strona", strona, "katalog", katalog, "zapis", zapis, "w_aplikacji", true));
                    continue;
                }
                try
                {
                    var rec = Agenci.Uruchom(P.S(p, "nazwa"), projekt, modul, katalog, "", podwyzszone.Length > 0 ? "pelne" : "edycja", strona == "codex" ? roleCodex : roleClaude, P.S(r, "tytul_sesji"), 12,
                        P.S(p, "tekst") + (zapis.Count > 0 ? "\n\nMożesz zapisywać w: " + katalog + " oraz " + string.Join(", ", zapis) + "." : ""), zapis,
                        P.S(p, "rola").Length > 0);      // jawne pole: agent zadania (wykonawca, sprawdzający, sam) musi dać FIN
                    P.ZBlokada(delegate
                    {
                        var pa = Path.Combine(P.Root, "stan", "agenci", P.S(rec, "nazwa") + ".json");
                        var d = Pliki.Czytaj(pa);
                        d["pokaz_sesje"] = pokazSesje;        // „Pokaż sesje agentów” – okno rozmowy otwiera się samo
                        if (podwyzszone.Length > 0) { d["podwyzszone_klucz"] = podwyzszone; d["uprawnienia_po_zadaniu"] = "edycja"; }
                        Pliki.Zapisz(pa, d);
                        return 0;
                    });
                    // projekt widoczny w Claude od razu: folder projektu ECXP zaufany w Claude Code (bez pytania przy otwarciu sesji)
                    if (strona == "claude") Cieply.ZaufajClaude(katalog);
                    wynik.Add(P.D("nazwa", P.S(rec, "nazwa"), "strona", strona, "katalog", katalog, "zapis", zapis, "podwyzszone", podwyzszone.Length > 0));
                }
                catch (Exception ex) { wynik.Add(P.D("nazwa", P.S(p, "nazwa"), "strona", strona, "blad", ex.Message)); }
            }
            return wynik;
        }

        // ---------- podwyższone uprawnienia (UAC) jednego zadania ----------
        // Proces uruchomiony przez konsolę z „runas” (okno UAC Windows): wykonuje tury tylko agentów tego zadania, z prawami
        // administratora. Kończy się, gdy zadanie ma FIN (obieg zakończony) albo po 3 h – uprawnienia agentów wracają do „edycja”.
        static int Podwyzszone(string klucz)
        {
            Agenci.Log = t => Log("[UAC " + klucz + "] " + t);
            Agenci.Obsluguje = rec => P.S(rec, "podwyzszone_klucz") == klucz;
            Cieply.WProcesieUac = true;          // okna sesji otwiera zwykły proces stosu, nie proces administratora
            Log("[UAC " + klucz + "] start, administrator: " + new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent())
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator));
            new Thread(Agenci.Petla) { IsBackground = true, Name = "ecxp-agenci-uac" }.Start();
            var start = DateTime.Now;
            var plik = Path.Combine(P.Root, "stan", "obieg", klucz + ".json");
            while (true)
            {
                Thread.Sleep(5000);
                bool koniec = DateTime.Now - start > TimeSpan.FromHours(3);
                try { if (!File.Exists(plik) || P.B(Pliki.Czytaj(plik), "zakonczone")) koniec = true; } catch { }
                if (!koniec) continue;
                Agenci.Obsluguje = rec => false;                                    // bez nowych tur
                for (int i = 0; i < 360 && (Agenci.UslugaStan("claude")["w_toku"] as List<object>).Count + (Agenci.UslugaStan("codex")["w_toku"] as List<object>).Count > 0; i++) Thread.Sleep(5000);
                WygasUprawnienia(klucz, DateTime.Now - start > TimeSpan.FromHours(3) ? "limit 3 h" : "zadanie zakończone");
                Cieply.ZamknijWszystko(); Thread.Sleep(4000);       // procesy agentów z prawami administratora nie zostają
                return 0;
            }
        }

        static int WygasUprawnienia(string klucz, string powod)
        {
            if ((klucz ?? "").Length == 0) return 0;
            return P.ZBlokada(delegate
            {
                int n = 0;
                var dir = Path.Combine(P.Root, "stan", "agenci");
                if (!Directory.Exists(dir)) return 0;
                foreach (var pa in Directory.GetFiles(dir, "*.json"))
                {
                    Dictionary<string, object> d;
                    try { d = Pliki.Czytaj(pa); } catch { continue; }
                    if (P.S(d, "podwyzszone_klucz") != klucz) continue;
                    d["uprawnienia"] = P.S(d, "uprawnienia_po_zadaniu").Length > 0 ? P.S(d, "uprawnienia_po_zadaniu") : "edycja";
                    d.Remove("podwyzszone_klucz"); d.Remove("uprawnienia_po_zadaniu");
                    Pliki.Zapisz(pa, d);
                    P.Zdarzenie("uprawnienia_wygasly", P.D("nazwa", P.S(d, "nazwa"), "klucz", klucz, "powod", powod ?? ""));
                    n++;
                }
                return n;
            });
        }

        static readonly System.Text.RegularExpressions.Regex ReSciezka = new System.Text.RegularExpressions.Regex(@"[A-Za-z]:\\[^\r\n""„”<>|*?:]+");   // bez dwukropka: kolejna ścieżka (X:\) zaczyna nowe dopasowanie
        public static List<string> KatalogiZTresci(string opis)
        {
            var l = new List<string>();
            foreach (System.Text.RegularExpressions.Match m in ReSciezka.Matches(opis ?? ""))
            {
                var s = m.Value.Trim().TrimEnd('.', ',', ')', ';', ':');
                try
                {
                    string d = Directory.Exists(s) ? s : Path.GetDirectoryName(s);
                    while (!string.IsNullOrEmpty(d) && !Directory.Exists(d)) d = Path.GetDirectoryName(d);
                    if (string.IsNullOrEmpty(d) || Path.GetPathRoot(d) == d) continue;          // nigdy cały dysk
                    if (d.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase)) continue;
                    if (!l.Contains(d, StringComparer.OrdinalIgnoreCase)) l.Add(d);
                }
                catch { }
            }
            return l;
        }

        static void TylkoOperator(Sesja s) { if (!s.Operator) throw new EcxpException("To polecenie wydaje tylko operator z konsoli ECXP-MAILBOX."); }

        static List<Dictionary<string, object>> Czesci(Dictionary<string, object> a)
        {
            return P.Lista(a, "czesci");
        }

        static object Wykonaj(Sesja s, string op, Dictionary<string, object> a)
        {
            lock (stos)
            {
                object w = WykonajW(s, op, a);
                if (op != "odbierz" && op != "stan" && op != "watek" && op != "trasy" && op != "kontekst_pobierz" && op != "ping" && op != "sprawdz_decyzje" && op != "lancuchy" && op != "galezie" && op != "agenci" && op != "sesja")
                {
                    if (op == "przypisz" || op == "rola" || op == "nowe_zadanie") Znaczniki();
                    Wypchnij(P.D("zdarzenie", "zmiana", "op", op));
                }
                return w;
            }
        }

        static object WykonajW(Sesja s, string op, Dictionary<string, object> a)
        {
            switch (op)
            {
                case "ping": return P.D("stos", "ecxpd 1.0", "protokol", P.Protokol, "ja", s.Ja, "czas", P.Teraz());
                case "wyslij":
                    {
                        int dummy;
                        var n = new P.Nowa
                        {
                            Od = Agent(s, a), Do = P.S(a, "do").Trim(), Typ = P.S(a, "typ").Trim().ToLowerInvariant(), Projekt = P.S(a, "projekt").Trim(),
                            Modul = P.S(a, "modul").Length > 0 ? P.S(a, "modul") : "wspolne", Temat = P.S(a, "temat"), Tresc = P.S(a, "tresc"),
                            OdpNa = P.S(a, "odpowiedz_na").Trim(), Pilne = P.B(a, "pilne"), Commit = P.S(a, "commit").Trim(),
                            Kontrole = P.L(a, "kontrole"), Pliki = P.L(a, "pliki"), Ograniczenia = P.S(a, "ograniczenia"), Czesci = Czesci(a),
                            BezKopii = s.Operator && P.B(a, "bez_kopii")      // tylko operator może pominąć kopię dla drugiej strony pary
                        };
                        int.TryParse("0", out dummy);
                        if (n.Typ.Length == 0) n.Typ = "informacja";
                        if (s.Operator && (n.Typ == "fin" || n.Typ == "w_trakcie")) throw new EcxpException("FIN i „w trakcie” wysyłają agenci – operator wysyła zadanie, pytanie albo informację.");
                        if (n.Typ == "decyzja") throw new EcxpException("Decyzje wydaje wyłącznie operator, podpisane – w konsoli (Decyzja…), nie zwykłą wiadomością.");
                        if (s.Operator && n.Ograniczenia.Length == 0)
                            n.Ograniczenia = "Wiadomość operatora przez ECXP (konsola ECXP-MAILBOX, tożsamość potwierdzona przez stos).";
                        return Siec.Wyslij(n);
                    }
                case "odbierz":
                    {
                        int limit; int.TryParse(P.S(a, "limit"), out limit);
                        var ja = Agent(s, a);
                        var lst = P.Odbierz(ja, limit);
                        return P.D("liczba", lst.Count(x => x.ContainsKey("id")), "wiadomosci", lst,
                            "przypomnienie", lst.Count > 0 ? "Zadanie: wykonaj i wyślij jeden FIN (typ=fin, odpowiedz_na=<id zadania>) – odbiór stos już zapisał. Inne wiadomości: ecxp_potwierdz." : "Brak wiadomości dla Ciebie.");
                    }
                case "potwierdz": return P.Potwierdz(Agent(s, a), P.S(a, "id").Trim(), P.S(a, "wynik").Trim().ToLowerInvariant(), P.S(a, "uwagi"));
                case "stan":
                    {
                        var ja = Agent(s, a);
                        var st = P.StanDla(ja);
                        st["moje_pary"] = Siec.Przynaleznosc(ja).Select(t => (object)Siec.Para(t.Projekt, t.Modul)).ToList();
                        st["znacznik"] = Znacznik;
                        return st;
                    }
                case "watek":
                    {
                        var m0 = P.Znajdz(P.S(a, "id").Trim());
                        if (m0 == null) throw new EcxpException("Nie ma wiadomości o id '" + P.S(a, "id") + "'.");
                        var lista = P.Wszystkie(true); P.Powiaz(lista);
                        var m = lista.First(x => x.Sciezka == m0.Sciezka);
                        return P.D("watek", P.Baza(m.Temat), "wiadomosci", lista.Where(x => x.Watek == m.Watek).OrderBy(x => x.Czas).Select(x => (object)P.D(
                            "id", x.Id, "czas", x.Czas.ToString("yyyy-MM-dd HH:mm:ss"), "od", x.Od, "do", x.Do, "typ", x.Typ.Length > 0 ? x.Typ : P.Rodzaj(x).ToLowerInvariant(),
                            "projekt", x.Projekt, "modul", x.Modul, "temat", x.Temat, "tresc", x.Uwagi, "gdzie", x.Katalog)).ToList());
                    }
                case "sygnal": { var ja = Agent(s, a); P.Sygnal(ja, "sygnal"); return P.D("ja", ja, "czas", P.Teraz()); }
                case "kontekst_zapisz":
                    {
                        var ja = Agent(s, a);
                        if (P.S(a, "sciezka").Length > 0) return P.ZapiszPlik(ja, P.S(a, "sciezka"), P.S(a, "nazwa"), P.S(a, "typ"));
                        return P.ZapiszObiekt(ja, new UTF8Encoding(false).GetBytes(P.S(a, "tresc")), P.S(a, "nazwa"), P.S(a, "typ").Length > 0 ? P.S(a, "typ") : "text/plain");
                    }
                case "kontekst_pobierz":
                    {
                        long od; int ile; long.TryParse(P.S(a, "od"), out od); int.TryParse(P.S(a, "ile"), out ile);
                        return P.PobierzObiekt(Agent(s, a), P.S(a, "sha256"), od, ile);
                    }
                case "zadanie": return P.ZmienZadanie(Agent(s, a), P.S(a, "id").Trim(), P.S(a, "stan").Trim().ToLowerInvariant(), P.S(a, "uwagi"), P.L(a, "artefakty"));
                case "karta": return P.Karta(Agent(s, a), P.S(a, "opis"), P.L(a, "moduly"), P.L(a, "umiejetnosci"));
                case "przypisz":
                    {
                        var nazwa = s.Operator ? P.S(a, "nazwa").Trim().ToLowerInvariant() : Agent(s, a);
                        return Siec.Przypisz(nazwa, P.S(a, "projekt"), P.S(a, "modul"), P.S(a, "sesja"), P.L(a, "role"), P.S(a, "opis"), s.Operator ? P.Wlasciciel : nazwa);
                    }
                case "trasy": return Siec.Opis(P.S(a, "projekt"));
                case "rola":
                    TylkoOperator(s);
                    return Siec.NadajRole(P.S(a, "nazwa").Trim().ToLowerInvariant(), P.S(a, "projekt"), P.S(a, "modul"), P.L(a, "role"), P.S(a, "opis"));
                case "przekaz":
                    {
                        var ja = Agent(s, a);
                        var dane = P.D("cel", P.S(a, "cel"), "stan", P.S(a, "stan"), "repozytorium", P.S(a, "repozytorium"), "galaz", P.S(a, "galaz"),
                            "commit", P.S(a, "commit"), "pliki", P.L(a, "pliki"), "decyzje", P.L(a, "decyzje"), "ograniczenia", P.L(a, "ograniczenia"),
                            "otwarte_pytania", P.L(a, "otwarte_pytania"), "kontrole", P.L(a, "kontrole"));
                        var obj = P.ZapiszObiekt(ja, new UTF8Encoding(false).GetBytes(P.Js().Serialize(dane)), "przekazanie.json", "application/json");
                        return Siec.Wyslij(new P.Nowa
                        {
                            Od = ja, Do = P.S(a, "do"), Projekt = P.S(a, "projekt"), Modul = P.S(a, "modul").Length > 0 ? P.S(a, "modul") : "wspolne",
                            Typ = "zadanie", Temat = P.S(a, "temat").Length > 0 ? P.S(a, "temat") : "PRZEKAZANIE", Commit = P.S(a, "commit"),
                            Tresc = "Pakiet przekazania pracy. Cel: " + P.S(a, "cel") + "\nStan: " + P.S(a, "stan") + "\nGałąź: " + P.S(a, "galaz") + " @ " + P.S(a, "commit"),
                            Czesci = new List<Dictionary<string, object>> {
                                P.D("rodzaj", "dane", "schemat", "ecxp/przekazanie", "dane", dane),
                                P.D("rodzaj", "plik", "sha256", obj["sha256"], "nazwa", "przekazanie.json", "typ", "application/json") }
                        });
                    }
                case "archiwizuj":
                    {
                        TylkoOperator(s);
                        int ok = 0, err = 0;
                        foreach (var plik in P.L(a, "pliki"))
                        {
                            try
                            {
                                var src = Path.Combine(P.Root, "przeczytane", Path.GetFileName(plik));
                                if (!File.Exists(src)) { err++; continue; }
                                var m = P.Wczytaj(new FileInfo(src), "przeczytane", P.Js());
                                var dir = Path.Combine(P.Root, "archiwum", m.Czas.ToString("yyyy-MM"));
                                Directory.CreateDirectory(dir);
                                var dst = Path.Combine(dir, m.Plik);
                                if (File.Exists(dst)) dst = Path.Combine(dir, Path.GetFileNameWithoutExtension(m.Plik) + "-" + P.Losowe(2) + ".json");
                                File.Move(src, dst);
                                P.Zdarzenie("zarchiwizowana", P.D("id", m.Id, "plik", m.Plik, "kto", P.Wlasciciel));
                                ok++;
                            }
                            catch { err++; }
                        }
                        return P.D("przeniesione", ok, "bledy", err);
                    }
                case "obudz": TylkoOperator(s); return Doreczanie.ObudzTeraz(P.S(a, "nazwa"));
                case "sesja": TylkoOperator(s); return Doreczanie.Opisz(P.S(a, "nazwa"));
                case "zarejestruj_sesje": TylkoOperator(s); return Siec.ZarejestrujZnalezione(P.Lista(a, "sesje"));
                case "wycofaj": TylkoOperator(s); return P.Wycofaj(P.L(a, "pliki"), P.S(a, "powod"));
                case "nowe_zadanie":
                    {
                        TylkoOperator(s);
                        var kroki = P.L(a, "lancuch");
                        var strony = Siec.Strony(P.L(a, "strony"));
                        if (kroki.Count > 0 && strony.Count < 2) throw new EcxpException("Łańcuch ról wymaga obu stron pary – zaznacz Codex i Claude albo wyczyść pole łańcucha.");
                        var r = Siec.NoweZadanie(P.S(a, "projekt"), P.S(a, "modul"), P.S(a, "tytul"), P.S(a, "opis"), P.L(a, "role_codex"), P.L(a, "role_claude"), Znacznik, Czesci(a), kroki.Count == 0, strony,
                            a.ContainsKey("ikona_projektu") ? P.S(a, "ikona_projektu") : null, a.ContainsKey("ikona_modulu") ? P.S(a, "ikona_modulu") : null, P.B(a, "repozytorium"),
                            a.ContainsKey("sprawdzanie") ? P.B(a, "sprawdzanie") : true);
                        // podwyższone uprawnienia: zgoda operatora tylko dla tego zadania, klucz = obieg zadania (wygasają z jego FIN)
                        string klucz = "";
                        if (P.B(a, "podwyzszone") && P.B(a, "w_tle") && r["zadanie"] is Dictionary<string, object>)
                        {
                            var zd = (Dictionary<string, object>)r["zadanie"];
                            klucz = P.S(zd, "lustro_id").Length > 0 ? P.S(zd, "lustro_id") : P.S(zd, "id");
                        }
                        if (P.B(a, "w_tle")) r["agenci_w_tle"] = AgenciWTle(r, P.S(a, "opis"), P.L(a, "role_codex"), P.L(a, "role_claude"), klucz, P.B(a, "pokaz_sesje"), P.B(a, "claude_w_aplikacji"));
                        if (klucz.Length > 0)
                        {
                            r["podwyzszone_klucz"] = klucz;
                            P.Zdarzenie("zgoda_podwyzszone", P.D("klucz", klucz, "kto", P.Wlasciciel, "projekt", P.S(r, "projekt"), "modul", P.S(r, "modul"),
                                "zakres", "tylko agenci tego zadania, do jego zakończenia"));
                        }
                        if (kroki.Count > 0) r["lancuch"] = Lancuchy.Utworz(P.S(a, "projekt"), P.S(a, "modul"), P.S(a, "tytul"), P.S(a, "opis"), kroki, Czesci(a));
                        return r;
                    }
                case "podwyzszone_wygas": TylkoOperator(s); return P.D("wygasly", WygasUprawnienia(P.S(a, "klucz"), P.S(a, "powod")));
                case "decyzja":
                    TylkoOperator(s);
                    return Decyzje.Wydaj(P.S(a, "projekt"), P.S(a, "modul"), P.S(a, "do"), P.S(a, "rodzaj"), P.S(a, "commit"), P.S(a, "galaz"), P.S(a, "opis"), P.S(a, "odpowiedz_na"), P.S(a, "lancuch"));
                case "sprawdz_decyzje": return Decyzje.Sprawdz(P.S(a, "id"));
                case "lancuch":
                    TylkoOperator(s);
                    return Lancuchy.Utworz(P.S(a, "projekt"), P.S(a, "modul"), P.S(a, "tytul"), P.S(a, "opis"), P.L(a, "kroki"), Czesci(a));
                case "lancuch_wznow": TylkoOperator(s); return Lancuchy.Wznow(P.S(a, "id"));
                case "lancuchy":
                    {
                        var pr = P.Slug(P.S(a, "projekt"), 32, false);
                        return P.D("lancuchy", Lancuchy.Wszystkie().Where(l => pr.Length == 0 || P.S(l, "projekt") == pr).Cast<object>().ToList());
                    }
                case "umowa_galezi": return Galezie.Umowa(Agent(s, a), P.S(a, "projekt"), P.S(a, "modul"), P.S(a, "repozytorium"), P.S(a, "galaz"), P.S(a, "baza"));
                case "stan_galezi": return Galezie.StanGalezi(Agent(s, a), P.S(a, "projekt"), P.S(a, "modul"), P.S(a, "galaz"), P.S(a, "head"));
                case "galezie": return Galezie.Stan(P.S(a, "projekt"), P.S(a, "modul"));
                case "para_wznow": TylkoOperator(s); return Galezie.Wznow(P.S(a, "projekt"), P.S(a, "modul"), P.S(a, "uwagi"));
                case "uruchom_agenta":
                    {
                        TylkoOperator(s);
                        int ng; int.TryParse(P.S(a, "na_godzine"), out ng);
                        return Agenci.Uruchom(P.S(a, "nazwa"), P.S(a, "projekt"), P.S(a, "modul"), P.S(a, "katalog"), P.S(a, "model"),
                            P.S(a, "uprawnienia").Length > 0 ? P.S(a, "uprawnienia") : "odczyt", P.L(a, "role"), P.S(a, "opis"), ng > 0 ? ng : 6);
                    }
                case "zatrzymaj_agenta": TylkoOperator(s); return Agenci.Zatrzymaj(P.S(a, "nazwa"));
                case "usluga": TylkoOperator(s); return Agenci.Usluga(P.S(a, "strona"), P.S(a, "akcja"));
                case "uslugi": return P.D("claude", Agenci.UslugaStan("claude"), "codex", Agenci.UslugaStan("codex"));
                case "uslugi_zakoncz": TylkoOperator(s); return Agenci.Zakoncz(P.B(a, "przy_zamknieciu"));
                case "uslugi_po_starcie": TylkoOperator(s); return Agenci.WznowPoZamknieciu();
                case "agenci":
                    return P.D("agenci", Agenci.Lista().Cast<object>().ToList(), "claude_cli", Agenci.Program("claude"), "codex_cli", Agenci.Program("codex"));
                case "straz":
                    {
                        var al = P.Straz();
                        return P.D("alarmy", al.Select(x => (object)P.D("id", x.Id, "do", x.Do, "temat", x.Temat, "opis", x.Opis)).ToList());
                    }
            }
            throw new EcxpException("Nieznane polecenie stosu: '" + op + "'.");
        }
    }
}
