// Protokół ECXP (Evidsec Context eXchange Protocol) – warstwa trwałości i niezawodności stosu ecxpd (ecxpd.exe).
// Pisze wyłącznie stos; konsola ECXP-MAILBOX i adapter ecxp-mcp tylko czytają pliki i wysyłają polecenia ramkami przez potok.
//
// Gwarancje, wzorowane na TCP:
//  * każda wiadomość ma unikalny id, numer kolejny w parze nadawca→adresat (seq) i skrót SHA-256 treści;
//  * zapis jest atomowy: plik .tmp zapisany z pominięciem bufora (WriteThrough + Flush), potem zmiana nazwy;
//  * każde zdarzenie (wysłana, dostarczona, potwierdzona, ponowienie, eskalacja) trafia do dziennika
//    łańcuchowego dziennik\dziennik.jsonl – każdy wpis zawiera skrót poprzedniego, więc zmiana lub
//    usunięcie wpisu jest wykrywalne;
//  * adresat musi potwierdzić wiadomość (ecxp_potwierdz albo odpowiedź z odpowiedz_na – potwierdzenie
//    „doklejone” jak w TCP); brak potwierdzenia w terminie → ponowienie, po dwóch ponowieniach eskalacja:
//    alarm u właściciela, a dla czatów modułów Codexa także przekazanie do codex-koordynacja;
//  * wiadomość nie może zniknąć bez śladu: strażnik (okno właściciela) porównuje dziennik z plikami
//    i zgłasza każdą zaginioną.
// Wszystkie zapisy idą pod blokadą pliku dziennik\.blokada (jeden zapisujący naraz, między procesami).
// Kompilacja: buduj.cmd (wbudowany csc .NET Framework 4, C# 5).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace Ecxp
{
    class Msg
    {
        public string Id = "", Plik = "", Katalog = "", Sciezka = "", Od = "", Do = "", Projekt = "", Modul = "", Temat = "", Status = "", EtapObiegu = "", Typ = "",
            Uwagi = "", Ograniczenia = "", Commit = "", OdpNa = "", WatekPole = "", Watek = "", Blad;
        public long Seq;
        public bool Pilne, Nowa, WymagaOdp, Odpowiedziano, NowyFormat;
        public DateTime Czas;
        public List<string> Kontrole = new List<string>(), Pliki = new List<string>();
        public List<Dictionary<string, object>> Czesci = new List<Dictionary<string, object>>();
        public List<string> Lustro = new List<string>();     // zasada 1:1: wszyscy adresaci lustrzanej wiadomości
        public string LustroId = "", KopiaDla = "";
        // ecxp/1.1: wynik FIN jako pole; identyfikator wiadomości wspólny dla wszystkich kopii (para 1:1) i odpowiedź na niego
        public string Wynik = "", Logiczne = "", OdpNaLogiczne = "";
        public string Tytul = "";      // oryginalny tytuł (temat to jego skrót bez polskich znaków i spacji)
        public long Nr;                // numer kolejny wiadomości w całym stosie (kolejność niezależna od zegara; 0 = starsza wiadomość)
        public Msg Rodzic;
        public Stan Dor;   // stan doręczenia z dziennika (null = wiadomość starego formatu)
        // Kopia do wydania z pamięci podręcznej: pola wyliczane przez wywołującego (wątek, rodzic, stan doręczenia) są puste.
        public Msg Klon()
        {
            var k = (Msg)MemberwiseClone();
            k.Kontrole = new List<string>(Kontrole); k.Pliki = new List<string>(Pliki); k.Lustro = new List<string>(Lustro);
            k.Czesci = new List<Dictionary<string, object>>(Czesci);
            k.Rodzic = null; k.Dor = null; k.Watek = ""; k.Nowa = false; k.Odpowiedziano = false;
            return k;
        }
    }

    class Stan
    {
        public string Id = "", Od = "", Do = "", Temat = "", Plik = "", Sha = "", Wynik = "";
        public long Seq;
        public int TerminMin, Dostarczenia, Ponowienia;
        public DateTime Wyslana;
        public DateTime? Dostarczona, Potwierdzona, OstPonowienie;
        public bool Eskalacja, Zaginiona;
        public string ZadanieStan = "";      // cykl życia zadania (A2A TaskState), tylko dla typ = zadanie
        public DateTime? ZadanieCzas;
        public Stan Klon() { return (Stan)MemberwiseClone(); }
    }

    class Wezel { public string Nazwa = "", Akcja = "", Rodzaj = ""; public DateTime Ostatnio; }

    class EcxpException : Exception { public EcxpException(string m) : base(m) { } }

    static class P
    {
        public const string Protokol = "ecxp/1";
        public const string Wlasciciel = "wlasciciel", Koordynacja = "codex-koordynacja", Straznik = "ecxp-straznik";
        public static readonly string[] Kolejki = { "do-wlasciciela", "do-codex", "do-claude" };
        public static readonly string[] Foldery = { "do-wlasciciela", "do-codex", "do-claude", "przeczytane" };
        public static readonly string[] Typy = { "pytanie", "zadanie", "informacja", "meldunek", "odpowiedz", "recenzja", "blokada", "decyzja", "fin", "w_trakcie", "postep" };
        // Wołane po każdej zmianie stanu zadania (łańcuchy ról w stosie): zadanie, stan, kto, uwagi.
        public static Action<string, string, string, string> PoZmianieZadania;
        public static readonly string[] Wyniki = { "obsluzona", "przyjeta", "odrzucona" };
        public static readonly string[] StanyZadan = { "zgloszone", "w_toku", "potrzebne_dane", "zakonczone", "nieudane", "anulowane", "odrzucone" };
        public static readonly string[] RodzajeCzesci = { "tekst", "dane", "plik" };
        static readonly string Zera = new string('0', 64);
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        public static string Root = "";

        // ---------- narzędzia ----------
        public static JavaScriptSerializer Js() { return new JavaScriptSerializer { MaxJsonLength = 64 * 1024 * 1024 }; }
        public static string Teraz() { return DateTimeOffset.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture); }
        public static string Sha(string s)
        {
            using (var h = SHA256.Create()) return Hex(h.ComputeHash(Utf8.GetBytes(s)));
        }
        public static string ShaPliku(string p)
        {
            try { using (var h = SHA256.Create()) using (var f = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)) return Hex(h.ComputeHash(f)); }
            catch { return ""; }
        }
        static string Hex(byte[] b) { return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant(); }
        public static string Czytaj(string p)
        {
            try
            {
                using (var f = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new StreamReader(f, Encoding.UTF8)) return r.ReadToEnd();
            }
            catch { return ""; }
        }
        public static string S(Dictionary<string, object> d, string k)
        {
            object v;
            return d != null && d.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : "";
        }
        public static List<string> L(Dictionary<string, object> d, string k)
        {
            var r = new List<string>(); object v;
            if (d == null || !d.TryGetValue(k, out v) || v == null) return r;
            if (v is string) { if (((string)v).Trim().Length > 0) r.Add((string)v); return r; }
            var e = v as IEnumerable;
            if (e == null) { r.Add(Convert.ToString(v, CultureInfo.InvariantCulture)); return r; }
            foreach (var x in e)
            {
                var dx = x as Dictionary<string, object>;
                if (dx != null)
                {
                    string w = S(dx, "wynik"), pol = S(dx, "polecenie"), sz = S(dx, "szczegoly");
                    r.Add((w.Length > 0 ? "[" + w + "] " : "") + pol + (sz.Length > 0 ? " – " + sz : ""));
                }
                else if (x != null) r.Add(Convert.ToString(x, CultureInfo.InvariantCulture));
            }
            return r;
        }
        public static bool B(Dictionary<string, object> d, string k)
        {
            object v; return d != null && d.TryGetValue(k, out v) && v is bool && (bool)v;
        }
        public static string Slug(string s, int max, bool upper)
        {
            var n = (s ?? "").Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var c0 in n)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c0) == UnicodeCategory.NonSpacingMark) continue;
                char c = c0 == 'ł' ? 'l' : c0 == 'Ł' ? 'L' : c0;
                if (c < 128 && char.IsLetterOrDigit(c)) sb.Append(c);
                else if (sb.Length > 0 && sb[sb.Length - 1] != '-') sb.Append('-');
            }
            var r = sb.ToString().Trim('-');
            if (r.Length > max) r = r.Substring(0, max).Trim('-');
            return upper ? r.ToUpperInvariant() : r.ToLowerInvariant();
        }
        public static string Losowe(int n)
        {
            var b = new byte[n]; using (var g = RandomNumberGenerator.Create()) g.GetBytes(b); return Hex(b);
        }
        static DateTime CzasZ(string czas, string plik, DateTime mtime)
        {
            DateTimeOffset o;
            if (czas.Length > 0 && DateTimeOffset.TryParse(czas, CultureInfo.InvariantCulture, DateTimeStyles.None, out o)) return o.LocalDateTime;
            var m = Regex.Match(plik, @"^(\d{8})-(\d{6})");
            DateTime t;
            if (m.Success && DateTime.TryParseExact(m.Groups[1].Value + m.Groups[2].Value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out t)) return t;
            return mtime;
        }

        // ---------- nazwy i adresowanie ----------
        static readonly Regex ReWezel = new Regex(@"^(codex|claude)(-[a-z0-9]+)+$");
        static readonly Regex ReAdresat = new Regex(@"^(codex|claude)(-[a-z0-9]+)*$");
        public static bool PoprawnyNadawca(string n) { return ReWezel.IsMatch(n ?? ""); }
        public static bool PoprawnyAdresat(string n) { return n == Wlasciciel || ReAdresat.IsMatch(n ?? ""); }
        public static string Folder(string adresat)
        {
            if (adresat == Wlasciciel) return "do-wlasciciela";
            return adresat.StartsWith("claude") ? "do-claude" : "do-codex";
        }
        // Czy wiadomość z polem "do" należy do węzła ja. Ogólne "codex" i "codex-core" obsługuje koordynacja,
        // ogólne "claude" – dowolna sesja Claude.
        public static bool DoMnie(string ja, string adresat)
        {
            if (adresat == ja) return true;
            if (ja == Koordynacja && (adresat == "codex" || adresat == "codex-core")) return true;
            return adresat == "claude" && ja.StartsWith("claude-");
        }
        public static string Nazwa(string n)
        {
            if (n == Wlasciciel) return "Ty";
            if (n == Straznik) return "Strażnik ECXP";
            if (n == "codex") return "Codex (ogólnie)";
            if (n == "claude") return "Claude (dowolna sesja)";
            if (n == "codex-core") n = Koordynacja;
            if (n.StartsWith("codex-")) return "Codex · " + n.Substring(6);
            if (n.StartsWith("claude-")) return "Claude · " + n.Substring(7);
            return n.Length > 0 ? n : "?";
        }
        // ---------- ecxp/1.1: wynik FIN ----------
        // pozytywny / negatywny – zadanie wykonane, z takim wynikiem; niewykonane – zadania nie wykonano (brak danych, błąd);
        // odmowa – dostawca modelu odmówił (zapisuje tylko stos); nieokreslony – agent w tle nie podał wyniku.
        public const string WersjaProtokolu = "1.1";
        // Polecenia stosu (ecxpd) – wysyłane klientom w powitaniu i w „ping”, żeby konsola wiedziała, co stos potrafi.
        public static readonly string[] PoleceniaStosu = { "agenci", "archiwizuj", "decyzja", "galezie", "karta", "kontekst_pobierz", "kontekst_zapisz", "lancuch", "lancuch_wznow",
            "lancuchy", "nowe_zadanie", "nowy_pentest", "obecnosc", "obudz", "odbierz", "para_wznow", "ping", "podwyzszone_wygas", "potwierdz", "projekt_usun_bilet", "przekaz", "przypisz",
            "przywroc", "rola", "sesja", "sprawdz_decyzje", "stan", "stan_galezi", "straz", "sygnal", "trasy", "umowa_galezi", "uruchom_agenta", "usluga", "uslugi", "uslugi_po_starcie",
            "uslugi_zakoncz", "usun", "watek", "wycofaj", "wylacz_agenta", "wyslij", "zadanie", "zapytaj_o_etap", "zarejestruj_sesje", "zatrzymaj_agenta" };
        // Pola przyjmowane przez „wyslij” (ecxp_wyslij). Nieznane pole albo zły typ = błąd od razu, zamiast cichego pominięcia.
        public static readonly string[] PolaWyslij = { "ja", "do", "typ", "projekt", "modul", "temat", "tresc", "odpowiedz_na", "pilne", "commit", "kontrole", "pliki",
            "ograniczenia", "czesci", "bez_kopii", "wynik", "krok", "z_krokow", "klucz" };
        public static void SprawdzPolaWyslij(Dictionary<string, object> a)
        {
            if (a == null) return;
            var nieznane = a.Keys.Where(k => !PolaWyslij.Contains(k)).OrderBy(k => k).ToList();
            if (nieznane.Count > 0)
                throw new EcxpException("Nieznane pole: " + string.Join(", ", nieznane) + ". Dozwolone: " + string.Join(", ", PolaWyslij) + ".");
            foreach (var k in new[] { "pilne", "bez_kopii" })
            {
                object v;
                if (a.TryGetValue(k, out v) && v != null && !(v is bool) && !Regex.IsMatch(Convert.ToString(v, CultureInfo.InvariantCulture), "^(true|false|0|1)$", RegexOptions.IgnoreCase))
                    throw new EcxpException("Pole " + k + " musi być wartością logiczną (true albo false).");
            }
            foreach (var k in new[] { "kontrole", "pliki", "czesci" })
            {
                object v;
                if (a.TryGetValue(k, out v) && v != null && (v is string || !(v is IEnumerable)))
                    throw new EcxpException("Pole " + k + " musi być listą.");
            }
            foreach (var k in new[] { "krok", "z_krokow" })
            {
                object v; int liczba;
                if (a.TryGetValue(k, out v) && v != null && Convert.ToString(v, CultureInfo.InvariantCulture).Length > 0 && (!int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out liczba) || liczba < 1))
                    throw new EcxpException("Pole " + k + " musi być liczbą całkowitą większą od zera.");
            }
            foreach (var k in new[] { "ja", "do", "typ", "projekt", "modul", "temat", "tresc", "odpowiedz_na", "commit", "ograniczenia", "wynik", "klucz" })
            {
                object v;
                if (a.TryGetValue(k, out v) && v != null && !(v is string))
                    throw new EcxpException("Pole " + k + " musi być tekstem.");
            }
        }
        public static Dictionary<string, object> Mozliwosci(string stos)
        {
            return D("stos", stos, "protokol", Protokol, "wersja_protokolu", WersjaProtokolu, "polecenia", PoleceniaStosu.ToList(),
                "typy", Typy.ToList(), "wyniki_fin", WynikiFin.ToList());
        }
        public static readonly string[] WynikiFin = { "pozytywny", "negatywny", "niewykonane" };
        static readonly Regex ReWynik = new Regex(@"^[\s\*#>\-–—:„""]*(?:WYNIK\s*[:\-–—]\s*)?(POZYTYWNY|NEGATYWNY|NIEWYKONANE)\b", RegexOptions.IgnoreCase);
        // Wynik z początku treści (zgodność ze starszymi pakietami: „POZYTYWNY – …”). "" = nie podano.
        public static string WynikZTresci(string tresc)
        {
            var t = tresc ?? "";
            var m = ReWynik.Match(t);
            if (!m.Success) return "";
            var w = m.Groups[1].Value.ToLowerInvariant();
            if (w == "negatywny" && Regex.IsMatch(t.Substring(0, Math.Min(t.Length, 80)), @"NEGATYWNY\s*[—–-]\s*NIEWYKONANE", RegexOptions.IgnoreCase)) return "niewykonane";
            return w;
        }
        // Linia „WYNIK: …” na początku odpowiedzi agenta w tle – po odczytaniu wyniku usuwana z treści.
        public static string BezLiniiWyniku(string tresc)
        {
            return Regex.Replace(tresc ?? "", @"^\s*WYNIK\s*[:\-–—]\s*(POZYTYWNY|NEGATYWNY|NIEWYKONANE)\s*\.?[ \t]*(\r?\n)+", "", RegexOptions.IgnoreCase).Trim();
        }
        public static string WynikOpis(string w)
        {
            switch (w)
            {
                case "pozytywny": return "✓ POZYTYWNY";
                case "negatywny": return "✗ NEGATYWNY";
                case "niewykonane": return "⊘ NIEWYKONANE";
                case "odmowa": return "⛔ NIEWYKONANE – odmowa dostawcy modelu";
                case "nieokreslony": return "? wynik nieokreślony";
            }
            return "";
        }
        // Wynik wiadomości: pole (ecxp/1.1) albo – dla starszych FIN – początek treści.
        public static string WynikWiadomosci(Msg m)
        {
            if (m == null) return "";
            if (m.Wynik.Length > 0) return m.Wynik;
            return m.Typ == "fin" ? WynikZTresci(m.Uwagi) : "";
        }

        public static string StatusZTypu(string typ)
        {
            switch (typ)
            {
                case "pytanie": case "zadanie": return "QUESTION";
                case "blokada": return "BLOCKED";
                case "meldunek": case "fin": return "COMPLETE";
                case "recenzja": return "REVIEW";
                case "decyzja": return "DECISION";
                default: return "PROGRESS";
            }
        }
        public static string Rodzaj(Msg m)
        {
            switch (m.Typ)
            {
                case "pytanie": return "Pytanie";
                case "zadanie": return "Zadanie";
                case "blokada": return "Blokada";
                case "meldunek": return "Meldunek";
                case "recenzja": return "Recenzja";
                case "decyzja": return "Decyzja";
                case "odpowiedz": return "Odpowiedź";
                case "fin": return "FIN";
                case "informacja": return "Informacja";
            }
            switch (m.Status)
            {
                case "QUESTION": return "Pytanie";
                case "BLOCKED": return "Blokada";
                case "COMPLETE": return "Gotowe";
                case "REVIEW": return "Recenzja";
                case "PROGRESS": return "Informacja";
            }
            return "Wiadomość";
        }
        public static int Termin(string adresat, bool pilne)
        {
            if (adresat == Wlasciciel) return 0;              // właściciela pilnuje okno, nie strażnik
            if (pilne) return 15;
            if (DoMnie(Koordynacja, adresat)) return 30;      // automatyzacja co 10 min
            return 240;                                       // czaty modułów i sesje czytają między etapami
        }
        public static string Wiek(TimeSpan t)
        {
            if (t.TotalMinutes < 1) return "chwilę";
            if (t.TotalMinutes < 60) return (int)t.TotalMinutes + " min";
            if (t.TotalHours < 48) return (int)t.TotalHours + " h";
            return (int)t.TotalDays + " dni";
        }

        // ---------- blokada i dziennik ----------
        static string DzDir { get { return Path.Combine(Root, "dziennik"); } }
        static string Dz { get { return Path.Combine(DzDir, "dziennik.jsonl"); } }
        static readonly Regex ReHash = new Regex(",\"hash\":\"([0-9a-f]{64})\"}$");
        [ThreadStatic] static int glebokosc;

        public static T ZBlokada<T>(Func<T> f)
        {
            if (glebokosc > 0) return f();
            Directory.CreateDirectory(DzDir);
            var lp = Path.Combine(DzDir, ".blokada");
            for (int i = 0; ; i++)
            {
                FileStream fs;
                try { fs = new FileStream(lp, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException)
                {
                    if (i > 400) throw new EcxpException("Skrzynka zajęta: blokada dziennika nie zwolniona przez 10 s.");
                    Thread.Sleep(25); continue;
                }
                using (fs)
                {
                    glebokosc++;
                    try { return f(); } finally { glebokosc--; }
                }
            }
        }

        // Końcówka pliku (ostatnie n bajtów) bez czytania całości.
        static string Koncowka(string plik, int n)
        {
            try
            {
                using (var fs = new FileStream(plik, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (fs.Length > n) fs.Seek(-n, SeekOrigin.End);
                    var b = new byte[(int)Math.Min(n, fs.Length)];
                    int ile = 0, r;
                    while (ile < b.Length && (r = fs.Read(b, ile, b.Length - ile)) > 0) ile += r;
                    return Utf8.GetString(b, 0, ile);
                }
            }
            catch (FileNotFoundException) { return ""; }
            catch (DirectoryNotFoundException) { return ""; }
        }

        static string OstatniHash()
        {
            // skrót ostatniego wpisu jest na końcu pliku: wystarczy końcówka (wpis dziennika ma najwyżej kilka KB)
            var t = Koncowka(Dz, 64 * 1024).TrimEnd('\r', '\n', ' ');
            if (t.Length > 0 && t.IndexOf('\n') < 0 && new FileInfo(Dz).Length > 64 * 1024) t = Czytaj(Dz).TrimEnd('\r', '\n', ' ');   // jeden bardzo długi wpis
            if (t.Length == 0) return Zera;
            var i = t.LastIndexOf('\n');
            var m = ReHash.Match((i >= 0 ? t.Substring(i + 1) : t).TrimEnd('\r'));
            return m.Success ? m.Groups[1].Value : Zera;
        }

        // Dopisuje zdarzenie do dziennika łańcuchowego. Tylko pod blokadą.
        public static void Zdarzenie(string zdarzenie, Dictionary<string, object> pola)
        {
            ZBlokada(delegate
            {
                var d = new Dictionary<string, object>();
                d["czas"] = Teraz();
                d["zdarzenie"] = zdarzenie;
                foreach (var kv in pola) d[kv.Key] = kv.Value;
                var poprz = OstatniHash();
                d["poprz"] = poprz;
                var body = Js().Serialize(d);
                var line = body.Substring(0, body.Length - 1) + ",\"hash\":\"" + Sha(poprz + body) + "\"}\n";
                var b = Utf8.GetBytes(line);
                using (var fs = new FileStream(Dz, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough))
                { fs.Write(b, 0, b.Length); fs.Flush(true); }
                return 0;
            });
        }

        public static Dictionary<string, object> D(params object[] kv)
        {
            var d = new Dictionary<string, object>();
            for (int i = 0; i + 1 < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1];
            return d;
        }

        // Odczyt dziennika: stany doręczeń i sprawdzenie łańcucha skrótów.
        // Stan doręczeń trzymany w pamięci procesu: po pierwszym pełnym odczycie dziennika doczytywane są tylko nowe wpisy
        // (dziennik jest tylko dopisywany). Pełny odczyt z kontrolą całego łańcucha skrótów: przy starcie, co 5 minut oraz
        // gdy plik zmalał albo zmienił się bez przyrostu (podmiana / edycja wpisu).
        class PamiecDziennika
        {
            public string Plik = ""; public long Pozycja; public DateTime Zmiana, Pelny;
            public string Prev = Zera, Blad; public int N;
            public Dictionary<string, Stan> R = new Dictionary<string, Stan>(StringComparer.OrdinalIgnoreCase);
        }
        static PamiecDziennika pamiecDz;
        static readonly object blokadaDz = new object();
        public static bool PamiecWylaczona;       // testy porównawcze: zawsze pełny odczyt

        public static Dictionary<string, Stan> Stany(out string spojnosc)
        {
            lock (blokadaDz)
            {
                var plik = Dz;
                var fi = new FileInfo(plik);
                if (!fi.Exists || fi.Length == 0) { pamiecDz = null; spojnosc = "pusty (brak zdarzeń)"; return new Dictionary<string, Stan>(StringComparer.OrdinalIgnoreCase); }
                var p = pamiecDz;
                bool pelny = PamiecWylaczona || p == null || p.Plik != plik || fi.Length < p.Pozycja || DateTime.Now - p.Pelny > TimeSpan.FromMinutes(5)
                    || (fi.Length == p.Pozycja && fi.LastWriteTimeUtc != p.Zmiana);
                if (pelny) p = new PamiecDziennika { Plik = plik, Pelny = DateTime.Now };
                if (fi.Length > p.Pozycja)
                {
                    string tekst;
                    using (var fs = new FileStream(plik, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        fs.Seek(p.Pozycja, SeekOrigin.Begin);
                        var b = new byte[(int)(fs.Length - p.Pozycja)];
                        int ile = 0, rd;
                        while (ile < b.Length && (rd = fs.Read(b, ile, b.Length - ile)) > 0) ile += rd;
                        // tylko pełne wiersze: wpis w trakcie zapisu zostaje na następny odczyt
                        int koniec = ile > 0 ? Array.LastIndexOf(b, (byte)'\n', ile - 1) : -1;
                        tekst = Utf8.GetString(b, 0, koniec + 1);
                        p.Pozycja += koniec + 1;
                    }
                    DopiszDoStanow(p, tekst);
                }
                p.Zmiana = fi.LastWriteTimeUtc;
                pamiecDz = p;
                spojnosc = p.Blad != null ? "NARUSZONY: " + p.Blad : "spójny, wpisów: " + p.N;
                var wynik = new Dictionary<string, Stan>(p.R.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var kv in p.R) wynik[kv.Key] = kv.Value.Klon();
                return wynik;
            }
        }

        static void DopiszDoStanow(PamiecDziennika pd, string tekst)
        {
            var r = pd.R;
            var js = Js();
            string prev = pd.Prev, blad = pd.Blad; int n = pd.N;
            try
            {
            foreach (var raw in tekst.Split('\n'))
            {
                var l = raw.TrimEnd('\r');
                if (l.Trim().Length == 0) continue;
                n++;
                var mh = ReHash.Match(l);
                if (blad == null)
                {
                    if (!mh.Success) blad = "wpis " + n + " bez skrótu";
                    else if (Sha(prev + l.Substring(0, mh.Index) + "}") != mh.Groups[1].Value) blad = "wpis " + n + " zmieniony albo wpis przed nim usunięty";
                }
                if (mh.Success) prev = mh.Groups[1].Value;
                Dictionary<string, object> d;
                try { d = js.Deserialize<Dictionary<string, object>>(l); } catch { continue; }
                var id = S(d, "id");
                if (id.Length == 0) continue;
                DateTime czas = CzasZ(S(d, "czas"), "", DateTime.Now);
                Stan s;
                r.TryGetValue(id, out s);
                switch (S(d, "zdarzenie"))
                {
                    case "wyslana":
                        s = new Stan { Id = id, Od = S(d, "od"), Do = S(d, "do"), Temat = S(d, "temat"), Plik = S(d, "plik"), Sha = S(d, "sha256"), Wyslana = czas };
                        long.TryParse(S(d, "seq"), out s.Seq);
                        int.TryParse(S(d, "termin_min"), out s.TerminMin);
                        r[id] = s;
                        break;
                    case "dostarczona":
                        if (s != null) { s.Dostarczenia++; if (!s.Dostarczona.HasValue) s.Dostarczona = czas; }
                        break;
                    case "potwierdzona":
                        if (s != null) { s.Potwierdzona = czas; s.Wynik = S(d, "wynik"); }
                        break;
                    case "ponowienie":
                        if (s != null) { s.Ponowienia++; s.OstPonowienie = czas; }
                        break;
                    case "eskalacja":
                        if (s != null) s.Eskalacja = true;
                        break;
                    case "zadanie":
                        if (s != null) { s.ZadanieStan = S(d, "stan"); s.ZadanieCzas = czas; }
                        break;
                    case "zaginiona":
                        if (s != null) { s.Zaginiona = true; s.Eskalacja = true; }
                        break;
                    case "projekt_wiadomosc_usunieta":
                        if (s != null) { s.Potwierdzona = czas; s.Wynik = "projekt usunięty"; s.ZadanieStan = "anulowane"; s.Zaginiona = false; }
                        break;
                }
            }
            }
            finally { pd.Prev = prev; pd.Blad = blad; pd.N = n; }
        }

        // ---------- odczyt wiadomości ----------
        public static Msg Wczytaj(FileInfo f, string katalog, JavaScriptSerializer js)
        {
            Dictionary<string, object> d = null; string blad = null;
            try { d = js.Deserialize<Dictionary<string, object>>(Czytaj(f.FullName)); } catch (Exception ex) { blad = "zły JSON: " + ex.Message; }
            if (d == null && blad == null) blad = "pusty plik";
            var m = new Msg
            {
                Plik = f.Name, Katalog = katalog, Sciezka = f.FullName, Blad = blad,
                Od = S(d, "od"), Do = S(d, "do"), Projekt = S(d, "projekt"), Modul = S(d, "modul"), Temat = S(d, "temat"), Status = S(d, "status"),
                Typ = S(d, "typ"), EtapObiegu = S(d, "etap_obiegu"), Uwagi = S(d, "uwagi"), Ograniczenia = S(d, "ograniczenia"), Commit = S(d, "commit"),
                OdpNa = S(d, "odpowiedz_na"), WatekPole = S(d, "watek"), Pilne = B(d, "pilne"),
                NowyFormat = S(d, "protokol").Length > 0,
                Kontrole = L(d, "kontrole"), Pliki = L(d, "pliki")
            };
            if (m.Uwagi.Length == 0) m.Uwagi = S(d, "tresc");
            object cz;
            if (d != null && d.TryGetValue("czesci", out cz) && cz is IEnumerable && !(cz is string))
                foreach (var x in (IEnumerable)cz) { var dx = x as Dictionary<string, object>; if (dx != null) m.Czesci.Add(dx); }
            m.Lustro = L(d, "lustro");
            m.LustroId = S(d, "lustro_id");
            m.KopiaDla = S(d, "kopia_dla_pary");
            m.Wynik = S(d, "wynik"); m.Logiczne = S(d, "wiadomosc"); m.OdpNaLogiczne = S(d, "odpowiedz_na_wiadomosc"); m.Tytul = S(d, "tytul");
            m.Id = S(d, "id");
            if (m.Id.Length == 0) m.Id = Path.GetFileNameWithoutExtension(f.Name);
            long.TryParse(S(d, "seq"), out m.Seq);
            long.TryParse(S(d, "nr"), out m.Nr);
            m.Czas = CzasZ(S(d, "czas"), f.Name, f.LastWriteTime);
            if (blad == null && (m.Od.Length == 0 || m.Do.Length == 0)) m.Blad = "brak pola od albo do";
            if (blad == null && m.NowyFormat)
            {
                var sk = S(d, "skrot");
                if (sk.Length > 0 && sk != SkrotTresci(m.Id, m.Od, m.Do, m.Seq, m.Uwagi)) m.Blad = "skrót treści się nie zgadza (zmieniona po wysłaniu)";
            }
            var t = m.Typ.Length > 0 ? m.Typ : "";
            m.WymagaOdp = m.Blad == null && (t == "pytanie" || t == "zadanie" || t == "blokada" || (t.Length == 0 && (m.Status == "QUESTION" || m.Status == "BLOCKED")));
            return m;
        }

        static string SkrotTresci(string id, string od, string adresat, long seq, string tresc)
        {
            return Sha(id + "|" + od + "|" + adresat + "|" + seq.ToString(CultureInfo.InvariantCulture) + "|" + tresc);
        }

        // Indeks wiadomości w pamięci procesu: plik wiadomości nie zmienia się po zapisie (zmienia tylko folder), więc raz
        // odczytany i sparsowany zostaje w pamięci pod kluczem ścieżka + czas zapisu + rozmiar. Kolejne odczyty to tylko lista
        // plików folderu i kopie obiektów – bez czytania treści z dysku i bez parsowania JSON.
        class WpisPamieci { public long Zapis, Rozmiar; public Msg M; }
        static readonly Dictionary<string, WpisPamieci> pamiecMsg = new Dictionary<string, WpisPamieci>(StringComparer.OrdinalIgnoreCase);
        public static int TrafieniaPamieci, OdczytyZDysku;      // do testów

        static Msg WczytajZPamieci(FileInfo f, string katalog, JavaScriptSerializer js)
        {
            if (PamiecWylaczona) return Wczytaj(f, katalog, js);
            long zapis = f.LastWriteTimeUtc.Ticks, rozmiar = f.Length;
            lock (pamiecMsg)
            {
                WpisPamieci w;
                if (pamiecMsg.TryGetValue(f.FullName, out w) && w.Zapis == zapis && w.Rozmiar == rozmiar) { TrafieniaPamieci++; return w.M.Klon(); }
            }
            var m = Wczytaj(f, katalog, js);
            OdczytyZDysku++;
            lock (pamiecMsg)
            {
                if (pamiecMsg.Count > 50000) pamiecMsg.Clear();      // bezpiecznik: bardzo duża skrzynka
                pamiecMsg[f.FullName] = new WpisPamieci { Zapis = zapis, Rozmiar = rozmiar, M = m };
            }
            return m.Klon();
        }

        public static List<Msg> Wszystkie(bool zArchiwum)
        {
            var list = new List<Msg>();
            var js = Js();
            var widziane = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in Foldery)
            {
                var p = Path.Combine(Root, k);
                if (!Directory.Exists(p)) continue;
                foreach (var f in new DirectoryInfo(p).GetFiles("*.json")) { list.Add(WczytajZPamieci(f, k, js)); widziane.Add(f.FullName); }
            }
            if (zArchiwum)
            {
                var a = Path.Combine(Root, "archiwum");
                if (Directory.Exists(a))
                    foreach (var f in new DirectoryInfo(a).GetFiles("*.json", SearchOption.AllDirectories)) { list.Add(WczytajZPamieci(f, "archiwum", js)); widziane.Add(f.FullName); }
                // pełny przegląd: wpisy plików, których już nie ma (przeniesione, usunięte), wypadają z pamięci
                lock (pamiecMsg)
                    if (pamiecMsg.Count > widziane.Count + 200)
                    {
                        var kosz = Path.Combine(Root, Kosz) + Path.DirectorySeparatorChar;
                        foreach (var klucz in pamiecMsg.Keys.Where(x => !widziane.Contains(x) && !x.StartsWith(kosz, StringComparison.OrdinalIgnoreCase)).ToList()) pamiecMsg.Remove(klucz);
                    }
            }
            return list;
        }

        // Wątki: odpowiedz_na → watek → nazwa pliku wspomniana w treści → temat bez przedrostków ODP-/-ODPOWIEDZ.
        static readonly Regex RePlik = new Regex(@"(\d{8}-\d{6}-[A-Za-z0-9_\-]+?)\.json");
        public static string Baza(string temat)
        {
            var t = (temat ?? "").Trim().ToUpperInvariant();
            foreach (var p in new[] { "ODP-", "ODP_", "ODP ", "RE-", "RE: " }) if (t.StartsWith(p)) t = t.Substring(p.Length);
            foreach (var s in new[] { "-ODPOWIEDZ", "-ODPOWIEDŹ", "-ODP" }) if (t.EndsWith(s)) t = t.Substring(0, t.Length - s.Length);
            return t.Length > 0 ? t : "(bez tematu)";
        }
        // Tytuł do pokazania: oryginalny (ecxp/1.1) albo – starsze wiadomości – temat.
        public static string TytulWiadomosci(Msg m) { return m == null ? "" : m.Tytul.Length > 0 ? m.Tytul : Baza(m.Temat); }

        public static void Powiaz(List<Msg> list)
        {
            var key = new Dictionary<string, Msg>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in list) { key[Path.GetFileNameWithoutExtension(m.Plik)] = m; key[m.Id] = m; }
            foreach (var m in list)
            {
                Msg r = null;
                var o = m.OdpNa.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? m.OdpNa.Substring(0, m.OdpNa.Length - 5) : m.OdpNa;
                if (o.Length > 0) key.TryGetValue(o, out r);
                if (r == null && m.WatekPole.Length > 0 && m.WatekPole != m.Id) key.TryGetValue(m.WatekPole, out r);
                if (r == null)
                    foreach (Match x in RePlik.Matches(m.Uwagi))
                    {
                        Msg c;
                        if (key.TryGetValue(x.Groups[1].Value, out c) && c != m) { r = c; break; }
                    }
                m.Rodzic = r == m ? null : r;
            }
            foreach (var m in list)
            {
                var root = m; int n = 0;
                while (root.Rodzic != null && n++ < 50) root = root.Rodzic;
                // ecxp/1.1: rozmowa to wątek o własnym identyfikatorze (pole watek pierwszej wiadomości), nie „ten sam temat”.
                // Po temacie łączą się tylko wiadomości starego formatu i wpisy z czatu (każdy wpis ma własny wątek).
                bool poWatku = root.NowyFormat && root.WatekPole.Length > 0 && !root.KopiaDla.StartsWith("czat:");
                m.Watek = poWatku ? "W|" + root.WatekPole : "T|" + Baza(root.Temat);
            }
            foreach (var m in list)
            {
                var mm = m;
                m.Odpowiedziano = list.Any(x => x != mm && x.Watek == mm.Watek && x.Od != mm.Od && (x.Rodzic == mm || x.Czas > mm.Czas));
            }
        }

        // Znacznik wiadomości: „#” i 4 ostatnie znaki identyfikatora (jak na końcu nazw plików widoku Odebrane / Wysłane).
        public static string ZnacznikId(string id) { id = id ?? ""; return "#" + (id.Length >= 4 ? id.Substring(id.Length - 4) : id); }
        // Kopia reprezentująca wiadomość wysłaną do pary (lustro): pierwsza według adresata – jej znacznik dotyczy wszystkich kopii.
        public static Msg Kanon(Msg m, IEnumerable<Msg> wszystkie)
        {
            if (m == null) return m;
            if (m.Logiczne.Length > 0 && m.Logiczne != m.Id) return wszystkie.FirstOrDefault(x => x.Id == m.Logiczne) ?? m;
            if (m.Logiczne.Length > 0 || m.LustroId.Length == 0 || m.Lustro.Count <= 1) return m;
            return wszystkie.Where(x => x.LustroId == m.LustroId).OrderBy(x => x.Do).ThenBy(x => x.Id).FirstOrDefault() ?? m;
        }

        // Identyfikator wiadomości wspólny dla wszystkich jej kopii: pole „wiadomosc” (ecxp/1.1) albo – starsze wiadomości –
        // id pierwszej kopii według adresata.
        public static string IdWiadomosci(Msg m, IEnumerable<Msg> wszystkie)
        {
            if (m == null) return "";
            return m.Logiczne.Length > 0 ? m.Logiczne : Kanon(m, wszystkie).Id;
        }

        public static Msg Znajdz(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (id.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) id = id.Substring(0, id.Length - 5);
            return Wszystkie(true).FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetFileNameWithoutExtension(m.Plik), id, StringComparison.OrdinalIgnoreCase));
        }

        // ---------- projekty (ECXP jest uniwersalny; projekt to parametr) ----------
        // projekty\<nazwa>\projekt.json: {"nazwa","opis","repozytorium","galaz","moduly":[...],"wezly":[{"nazwa","opis"}]}
        public static List<Dictionary<string, object>> Projekty()
        {
            var r = new List<Dictionary<string, object>>();
            var dir = Path.Combine(Root, "projekty");
            if (!Directory.Exists(dir)) return r;
            var js = Js();
            foreach (var f in Directory.GetFiles(dir, "projekt.json", SearchOption.AllDirectories))
                try { var d = js.Deserialize<Dictionary<string, object>>(Czytaj(f)); if (d != null && S(d, "nazwa").Length > 0) r.Add(d); } catch { }
            return r.OrderBy(d => S(d, "nazwa")).ToList();
        }

        public static List<Dictionary<string, object>> Lista(Dictionary<string, object> d, string k)
        {
            var r = new List<Dictionary<string, object>>(); object v;
            if (d != null && d.TryGetValue(k, out v) && v is IEnumerable && !(v is string))
                foreach (var x in (IEnumerable)v) { var dx = x as Dictionary<string, object>; if (dx != null) r.Add(dx); }
            return r;
        }

        // ---------- węzły (sygnał życia) ----------
        public static void Sygnal(string ja, string akcja)
        {
            try
            {
                var dir = Path.Combine(Root, "stan", "wezly");
                Directory.CreateDirectory(dir);
                var p = Path.Combine(dir, ja + ".json");
                var tmp = p + "." + Losowe(4) + ".tmp";
                // rodzaj: „tlo” – agent w tle (odzywa się tylko w turach uruchamianych przez stos), „sesja” – sesja w aplikacji
                File.WriteAllText(tmp, Js().Serialize(D("nazwa", ja, "ostatnio", Teraz(), "akcja", akcja, "rodzaj", akcja == "tura" || akcja == "tura_koniec" ? "tlo" : "sesja")), Utf8);
                if (File.Exists(p)) File.Replace(tmp, p, null); else File.Move(tmp, p);
            }
            catch { }
        }
        public static List<Wezel> Wezly()
        {
            var r = new List<Wezel>();
            var dir = Path.Combine(Root, "stan", "wezly");
            if (!Directory.Exists(dir)) return r;
            var js = Js();
            foreach (var f in new DirectoryInfo(dir).GetFiles("*.json"))
            {
                Dictionary<string, object> d = null;
                try { d = js.Deserialize<Dictionary<string, object>>(Czytaj(f.FullName)); } catch { }
                r.Add(new Wezel { Nazwa = Path.GetFileNameWithoutExtension(f.Name), Akcja = S(d, "akcja"), Rodzaj = S(d, "rodzaj"), Ostatnio = CzasZ(S(d, "ostatnio"), "", f.LastWriteTime) });
            }
            return r.OrderBy(w => w.Nazwa).ToList();
        }

        // ---------- wysyłanie ----------
        public class Nowa
        {
            public string Od = "", Do = "", Projekt = "", Modul = "wspolne", Typ = "informacja", Temat = "", Tresc = "", OdpNa = "", Commit = "", Ograniczenia = "", EtapObiegu = "";
            public bool Pilne;
            public bool BezKopii;        // operator wybrał jednego agenta: druga strona pary nie dostaje kopii
            public List<string> Kontrole = new List<string>(), Pliki = new List<string>();
            public List<Dictionary<string, object>> Czesci = new List<Dictionary<string, object>>();
            public string Watek = "", LustroId = "", KopiaDla = "";
            public string Wynik = "", Logiczne = "";   // wynik FIN; identyfikator wiadomości wspólny dla kopii (pusty = własne id)
            public bool ZeStosu;                       // FIN zapisany przez stos za agenta w tle (wynik może być nieokreślony)
            public int Krok, Krokow;                   // typ=postep: krok N z M (0 = nie podano)
            public List<string> Lustro = new List<string>();
            public Nowa Kopia()
            {
                var k = (Nowa)MemberwiseClone();
                k.Kontrole = new List<string>(Kontrole); k.Pliki = new List<string>(Pliki);
                k.Czesci = new List<Dictionary<string, object>>(Czesci); k.Lustro = new List<string>(Lustro);
                return k;
            }
        }

        static long NastepnySeq(string od, string adresat)
        {
            var dir = Path.Combine(Root, "stan", "seq");
            Directory.CreateDirectory(dir);
            var p = Path.Combine(dir, od + "__" + adresat + ".txt");
            long n = 0;
            long.TryParse(Czytaj(p).Trim(), out n);
            n++;
            var tmp = p + ".tmp";
            File.WriteAllText(tmp, n.ToString(CultureInfo.InvariantCulture), Utf8);
            if (File.Exists(p)) File.Replace(tmp, p, null); else File.Move(tmp, p);
            return n;
        }

        // Jeden rosnący numer na cały stos (pod blokadą): kolejność wiadomości nie zależy od zegara ani od tej samej milisekundy.
        static long NastepnyNr()
        {
            var dir = Path.Combine(Root, "stan", "seq");
            Directory.CreateDirectory(dir);
            var p = Path.Combine(dir, "_nr.txt");
            long n = 0;
            long.TryParse(Czytaj(p).Trim(), out n);
            n++;
            var tmp = p + ".tmp";
            File.WriteAllText(tmp, n.ToString(CultureInfo.InvariantCulture), Utf8);
            if (File.Exists(p)) File.Replace(tmp, p, null); else File.Move(tmp, p);
            return n;
        }

        // Wysyła wiadomość. Zwraca id, seq, ścieżkę i termin potwierdzenia.
        public static Dictionary<string, object> Wyslij(Nowa n)
        {
            if (!PoprawnyAdresat(n.Do)) throw new EcxpException("Niepoprawny adresat: '" + n.Do + "'. Dozwolone: wlasciciel, codex[-nazwa], claude[-nazwa].");
            if (n.Od != Wlasciciel && n.Od != Straznik && !PoprawnyNadawca(n.Od)) throw new EcxpException("Niepoprawny nadawca: '" + n.Od + "'. Użyj nazwy codex-<czat> albo claude-<sesja>.");
            if (!Typy.Contains(n.Typ)) throw new EcxpException("Niepoprawny typ: '" + n.Typ + "'. Dozwolone: " + string.Join(", ", Typy));
            if (n.Typ == "w_trakcie" || n.Typ == "postep") throw new EcxpException("Postęp („w trakcie”) dotyczy zadania operatora, które dostałeś – wyślij go przez ecxp_wyslij(typ=postep, odpowiedz_na=<id zadania>).");
            if ((n.Tresc ?? "").Trim().Length == 0) throw new EcxpException("Pusta treść.");
            if (n.Tresc.Length > 50000) throw new EcxpException("Treść dłuższa niż 50 000 znaków – podaj ścieżkę pliku w polu pliki.");
            var modul = Slug(n.Modul, 24, false); if (modul.Length == 0) modul = "wspolne";
            var temat = Slug(n.Temat, 48, true);
            if (temat.Length == 0) temat = Slug(string.Join(" ", n.Tresc.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Take(5)), 40, true);
            if (temat.Length == 0) temat = "WIADOMOSC";
            var czesci = SprawdzCzesci(n.Czesci);

            return ZBlokada(delegate
            {
                Msg org = null;
                if (n.OdpNa.Length > 0)
                {
                    org = Znajdz(n.OdpNa);
                    if (org == null) throw new EcxpException("Nie znaleziono wiadomości odpowiedz_na = '" + n.OdpNa + "'.");
                    // odpowiedz_na może wskazywać identyfikator wiadomości (wspólny dla pary) albo cudzą kopię: liczy się kopia do mnie
                    if (org.LustroId.Length > 0 && !DoMnie(n.Od, org.Do))
                    {
                        var lid = org.LustroId;
                        var moja = Wszystkie(true).FirstOrDefault(x => x.LustroId == lid && DoMnie(n.Od, x.Do));
                        if (moja != null) org = moja;
                    }
                }
                var seq = NastepnySeq(n.Od, n.Do);
                var id = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + modul + "-" + temat + "-" + Losowe(2);
                var termin = Termin(n.Do, n.Pilne);
                var d = new Dictionary<string, object>();
                d["protokol"] = Protokol; d["id"] = id; d["seq"] = seq; d["nr"] = NastepnyNr(); d["czas"] = Teraz();
                d["od"] = n.Od; d["do"] = n.Do;
                var projekt = Slug(n.Projekt.Length > 0 ? n.Projekt : (org != null ? org.Projekt : ""), 32, false);
                if (modul == "wspolne" && org != null && org.Modul.Length > 0) modul = org.Modul;
                if (projekt.Length > 0) d["projekt"] = projekt;
                d["modul"] = modul; d["typ"] = n.Typ; d["status"] = StatusZTypu(n.Typ);
                d["temat"] = temat; d["pilne"] = n.Pilne;
                {
                    var tytul = Regex.Replace((n.Temat ?? "").Trim(), @"\s+", " ");
                    if (tytul.Length > 160) tytul = tytul.Substring(0, 159) + "…";
                    if (tytul.Length > 0 && tytul != temat) d["tytul"] = tytul;
                }
                if (n.EtapObiegu.Length > 0) d["etap_obiegu"] = n.EtapObiegu;
                d["wiadomosc"] = n.Logiczne.Length > 0 ? n.Logiczne : id;
                if (org != null) { d["odpowiedz_na"] = org.Id; d["odpowiedz_na_wiadomosc"] = org.Logiczne.Length > 0 ? org.Logiczne : org.Id; }
                if (n.Wynik.Length > 0 && (n.Typ == "fin" || n.Typ == "odpowiedz")) d["wynik"] = n.Wynik;
                d["watek"] = n.Watek.Length > 0 ? n.Watek : org != null ? (org.WatekPole.Length > 0 ? org.WatekPole : org.Id) : id;
                if (n.Lustro.Count > 1) { d["lustro"] = n.Lustro; d["lustro_id"] = n.LustroId; }
                if (n.KopiaDla.Length > 0) d["kopia_dla_pary"] = n.KopiaDla;
                d["uwagi"] = n.Tresc;
                if (n.Commit.Length > 0) d["commit"] = n.Commit;
                if (n.Kontrole.Count > 0) d["kontrole"] = n.Kontrole;
                if (n.Pliki.Count > 0) d["pliki"] = n.Pliki;
                if (n.Ograniczenia.Length > 0) d["ograniczenia"] = n.Ograniczenia;
                if (czesci.Count > 0) d["czesci"] = czesci;
                d["kontekst_id"] = d["watek"];
                if (n.Typ == "zadanie") d["zadanie"] = D("id", id, "stan", "zgloszone");
                d["termin_potwierdzenia_min"] = termin;
                d["skrot"] = SkrotTresci(id, n.Od, n.Do, seq, n.Tresc);

                var dir = Path.Combine(Root, Folder(n.Do));
                Directory.CreateDirectory(dir);
                var dst = Path.Combine(dir, id + ".json");
                var tmp = Path.Combine(dir, id + ".tmp");
                var bytes = Utf8.GetBytes(Js().Serialize(d));
                using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                { fs.Write(bytes, 0, bytes.Length); fs.Flush(true); }
                File.Move(tmp, dst);
                var sha = ShaPliku(dst);
                Zdarzenie("wyslana", D("id", id, "od", n.Od, "do", n.Do, "seq", seq, "temat", temat, "plik", Folder(n.Do) + "/" + id + ".json", "sha256", sha, "termin_min", termin));
                if (n.Typ == "zadanie") Zdarzenie("zadanie", D("id", id, "stan", "zgloszone", "kto", n.Od));
                Sygnal(n.Od, "wyslij");

                // Potwierdzenie doklejone do odpowiedzi (jak ACK w TCP): odpowiedź na wiadomość do mnie ją potwierdza.
                string ack = "";
                if (org != null && n.EtapObiegu != "odebrane" && DoMnie(n.Od, org.Do) && org.Katalog != "przeczytane" && org.Katalog != "archiwum")
                {
                    PotwierdzPlik(org, n.Od, "obsluzona", "odpowiedź " + id);
                    ack = org.Id;
                }
                return D("id", id, "wiadomosc", d["wiadomosc"], "seq", seq, "plik", dst, "do", n.Do, "termin_potwierdzenia_min", termin, "potwierdzono_przy_okazji", ack);
            });
        }

        // ---------- zamknięcie wiadomości przez operatora ----------
        // Wiadomość z dowolnej kolejki (także niedoręczona albo pytanie bez odpowiedzi) trafia do archiwum\RRRR-MM.
        // Nic nie jest kasowane. W dzienniku: potwierdzona (wynik „wycofana”, kto: wlasciciel) – strażnik przestaje upominać.
        // cel = Kosz („Usuń” w konsoli): plik trafia do kosz\RRRR-MM – znika z konsoli i z archiwum, ale da się go odzyskać ręcznie.
        public const string Kosz = "kosz";
        public static Dictionary<string, object> Wycofaj(List<string> pliki, string powod, string cel = "archiwum")
        {
            if (cel != "archiwum" && cel != Kosz) throw new EcxpException("nieznany cel: " + cel);
            return ZBlokada(delegate
            {
                string spoj;
                var stany = Stany(out spoj);
                var js = Js();
                int ok = 0, brak = 0;
                foreach (var nazwa in pliki.Select(Path.GetFileName).Distinct())
                {
                    FileInfo f = null; string kat = null;
                    foreach (var k in Foldery)
                    {
                        var p = Path.Combine(Root, k, nazwa);
                        if (File.Exists(p)) { f = new FileInfo(p); kat = k; break; }
                    }
                    if (f == null) { brak++; continue; }
                    var m = Wczytaj(f, kat, js);
                    var dir = Path.Combine(Root, cel, m.Czas.ToString("yyyy-MM"));
                    Directory.CreateDirectory(dir);
                    var dst = Path.Combine(dir, m.Plik);
                    if (File.Exists(dst)) dst = Path.Combine(dir, Path.GetFileNameWithoutExtension(m.Plik) + "-" + Losowe(2) + ".json");
                    File.Move(f.FullName, dst);
                    Stan s;
                    if (stany.TryGetValue(m.Id, out s) && !s.Potwierdzona.HasValue)
                        Zdarzenie("potwierdzona", D("id", m.Id, "kto", Wlasciciel, "wynik", "wycofana", "uwagi", string.IsNullOrEmpty(powod) ? "zamknięta przez operatora" : powod));
                    Zdarzenie(cel == Kosz ? "usunieta" : "zarchiwizowana", D("id", m.Id, "plik", m.Plik, "z", kat, "kto", Wlasciciel, "powod", powod ?? ""));
                    ok++;
                }
                return D("przeniesione", ok, "brak", brak);
            });
        }

        // Przywrócenie wiadomości z archiwum albo kosza do bieżących (folder „przeczytane” – nic nie jest doręczane ponownie).
        public static Dictionary<string, object> Przywroc(List<string> pliki)
        {
            return ZBlokada(delegate
            {
                int ok = 0, brak = 0;
                var js = Js();
                foreach (var nazwa in pliki.Select(Path.GetFileName).Where(x => !string.IsNullOrEmpty(x)).Distinct())
                {
                    FileInfo f = null; string kat = null;
                    foreach (var k in new[] { "archiwum", Kosz })
                    {
                        var dir = Path.Combine(Root, k);
                        if (!Directory.Exists(dir)) continue;
                        var p = Directory.GetFiles(dir, nazwa, SearchOption.AllDirectories).FirstOrDefault();
                        if (p != null) { f = new FileInfo(p); kat = k; break; }
                    }
                    if (f == null) { brak++; continue; }
                    var m = Wczytaj(f, kat, js);
                    var cel = Path.Combine(Root, "przeczytane"); Directory.CreateDirectory(cel);
                    var dst = Path.Combine(cel, f.Name);
                    if (File.Exists(dst)) { brak++; continue; }
                    File.Move(f.FullName, dst);
                    Zdarzenie("przywrocona", D("id", m.Id, "plik", f.Name, "z", kat, "kto", Wlasciciel));
                    ok++;
                }
                return D("przywrocone", ok, "brak", brak);
            });
        }

        // Wiadomości z kosza (do folderu Kosz w konsoli).
        public static List<Msg> ZKosza()
        {
            var list = new List<Msg>(); var js = Js();
            var a = Path.Combine(Root, Kosz);
            if (Directory.Exists(a)) foreach (var f in new DirectoryInfo(a).GetFiles("*.json", SearchOption.AllDirectories)) list.Add(WczytajZPamieci(f, Kosz, js));
            return list;
        }

        // ---------- odbiór i potwierdzenia ----------
        public static List<Dictionary<string, object>> Odbierz(string ja, int limit)
        {
            return ZBlokada(delegate
            {
                string spoj;
                var stany = Stany(out spoj);
                var wynik = new List<Dictionary<string, object>>();
                var dir = Path.Combine(Root, Folder(ja));
                Sygnal(ja, "odbierz");
                if (!Directory.Exists(dir)) return wynik;
                var js = Js();
                var lista = new DirectoryInfo(dir).GetFiles("*.json").Select(f => Wczytaj(f, Folder(ja), js))
                    .Where(m => DoMnie(ja, m.Do) || (m.Blad != null)).OrderByDescending(m => m.Pilne).ThenBy(m => m.Czas).ThenBy(m => m.Seq).ToList();
                var wydane = lista.Take(limit <= 0 ? 50 : limit).ToList();
                foreach (var m in wydane)
                {
                    Stan s; stany.TryGetValue(m.Id, out s);
                    string integralnosc = m.Blad != null ? "USZKODZONA: " + m.Blad
                        : s == null ? (m.NowyFormat ? "brak wpisu w dzienniku" : "stary format (bez dziennika)")
                        : (s.Sha.Length > 0 && s.Sha != ShaPliku(m.Sciezka)) ? "USZKODZONA: plik zmieniony po wysłaniu" : "ok";
                    // tylko to, czego agent potrzebuje – każde zbędne pole to tokeny w jego kontekście
                    var d = D("id", m.Id, "od", m.Od, "typ", m.Typ.Length > 0 ? m.Typ : Rodzaj(m).ToLowerInvariant(), "projekt", m.Projekt, "modul", m.Modul,
                        "temat", m.Temat, "tresc", m.Uwagi, "integralnosc", integralnosc);
                    if (m.OdpNa.Length > 0) d["odpowiedz_na"] = m.OdpNa;
                    if (m.Pilne) d["pilne"] = true;
                    if (m.Commit.Length > 0) d["commit"] = m.Commit;
                    if (m.Kontrole.Count > 0) d["kontrole"] = m.Kontrole;
                    if (m.Pliki.Count > 0) d["pliki"] = m.Pliki;
                    if (s != null && s.Dostarczenia > 0) d["proba_dostarczenia"] = s.Dostarczenia + 1;
                    if (m.Czesci.Count > 0) d["czesci"] = CzesciDoOdbioru(m.Czesci);
                    wynik.Add(d);
                    Zdarzenie("dostarczona", D("id", m.Id, "kto", ja, "proba", (s != null ? s.Dostarczenia : 0) + 1));
                }
                // odbiór = ACK (jak w TCP): każda wydana wiadomość jest potwierdzona przez stos, agent nic nie potwierdza;
                // zadanie operatora przechodzi w obiegu w stan „w trakcie”, a jego wynik gwarantuje FIN (Obieg)
                Obieg.Odebrane(ja, wydane.Where(m => m.Blad == null));
                foreach (var m in wydane.Where(x => x.Blad == null && File.Exists(x.Sciezka)))
                    try { PotwierdzPlik(m, ja, "przyjeta", "odbiór (ACK)"); } catch { }
                var tmp = new DirectoryInfo(dir).GetFiles("*.tmp").Where(f => f.LastWriteTime < DateTime.Now.AddMinutes(-2)).ToList();
                if (tmp.Count > 0)
                    wynik.Add(D("ostrzezenie", "Niedokończone zapisy (.tmp starsze niż 2 min): " + string.Join(", ", tmp.Select(f => f.Name)) + ". Nadawca przerwał wysyłanie – poproś o ponowienie."));
                return wynik;
            });
        }

        public static void PotwierdzPlik(Msg m, string ja, string wynik, string uwagi)
        {
            var dir = Path.Combine(Root, "przeczytane");
            Directory.CreateDirectory(dir);
            var dst = Path.Combine(dir, m.Plik);
            if (File.Exists(dst)) dst = Path.Combine(dir, Path.GetFileNameWithoutExtension(m.Plik) + "-" + Losowe(2) + ".json");
            File.Move(m.Sciezka, dst);
            Zdarzenie("potwierdzona", D("id", m.Id, "kto", ja, "wynik", wynik, "uwagi", uwagi ?? ""));
        }

        public static Dictionary<string, object> Potwierdz(string ja, string id, string wynik, string uwagi)
        {
            if (!Wyniki.Contains(wynik)) throw new EcxpException("Niepoprawny wynik: '" + wynik + "'. Dozwolone: " + string.Join(", ", Wyniki));
            return ZBlokada(delegate
            {
                var m = Znajdz(id);
                if (m == null) throw new EcxpException("Nie ma wiadomości o id '" + id + "'.");
                if (m.Katalog == "przeczytane" || m.Katalog == "archiwum")
                    return D("id", m.Id, "stan", "juz_potwierdzona", "uwagi", "Wiadomość była już potwierdzona – powtórne potwierdzenie niczego nie zmienia.");
                if (!DoMnie(ja, m.Do)) throw new EcxpException("Wiadomość '" + m.Id + "' jest do '" + m.Do + "', nie do '" + ja + "'.");
                PotwierdzPlik(m, ja, wynik, uwagi);
                Sygnal(ja, "potwierdz");
                return D("id", m.Id, "stan", "potwierdzona", "wynik", wynik);
            });
        }

        // ---------- strażnik: ponowienia, eskalacje, zaginione ----------
        public class Alarm { public string Id, Do, Temat, Opis; }

        public static List<Alarm> Straz()
        {
            return ZBlokada(delegate
            {
                var alarmy = new List<Alarm>();
                string spoj;
                var stany = Stany(out spoj);
                var pliki = Wszystkie(true).GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                var teraz = DateTime.Now;
                foreach (var s in stany.Values)
                {
                    if (s.Potwierdzona.HasValue || s.Zaginiona) continue;
                    Msg m;
                    if (!pliki.TryGetValue(s.Id, out m))
                    {
                        Zdarzenie("zaginiona", D("id", s.Id, "do", s.Do));
                        alarmy.Add(new Alarm { Id = s.Id, Do = s.Do, Temat = s.Temat, Opis = "Wiadomość zaginęła: jest w dzienniku, nie ma pliku w skrzynce." });
                        continue;
                    }
                    if (m.Katalog == "przeczytane" || m.Katalog == "archiwum")
                    {
                        // adresat przeniósł plik ręcznie (stary sposób) – traktujemy jako potwierdzenie
                        Zdarzenie("potwierdzona", D("id", s.Id, "kto", "?", "wynik", "obsluzona", "uwagi", "plik przeniesiony do " + m.Katalog + " bez ecxp_potwierdz"));
                        continue;
                    }
                    if (s.TerminMin <= 0 || s.Eskalacja) continue;
                    var od = s.OstPonowienie ?? s.Wyslana;
                    if ((teraz - od).TotalMinutes < s.TerminMin) continue;
                    if (s.Ponowienia < 2)
                    {
                        Zdarzenie("ponowienie", D("id", s.Id, "do", s.Do, "nr", s.Ponowienia + 1));
                        continue;
                    }
                    Zdarzenie("eskalacja", D("id", s.Id, "do", s.Do));
                    var opis = "Brak potwierdzenia od " + Nazwa(s.Do) + " od " + Wiek(teraz - s.Wyslana) + " (dwa ponowienia bez odpowiedzi).";
                    if (s.Do.StartsWith("codex-") && s.Do != Koordynacja && s.Do != "codex-core")
                    {
                        Wyslij(new Nowa
                        {
                            Od = Straznik, Do = Koordynacja, Modul = m.Modul, Typ = "zadanie", Temat = "BRAK-POTWIERDZENIA",
                            Tresc = "Adresat " + s.Do + " nie potwierdził wiadomości " + s.Id + " (" + s.Temat + ") od " + Wiek(teraz - s.Wyslana) +
                                ". Sprawdź, czy ten czat działa; przekaż mu sprawę albo zgłoś właścicielowi. Oryginał: " + m.Sciezka,
                            OdpNa = s.Id
                        });
                        opis += " Przekazano do Codex · koordynacja.";
                    }
                    alarmy.Add(new Alarm { Id = s.Id, Do = s.Do, Temat = s.Temat, Opis = opis });
                }
                return alarmy;
            });
        }

        // ---------- warstwa 3: części wiadomości (A2A Part) ----------
        // tekst: {"rodzaj":"tekst","tekst":"..."}; dane: {"rodzaj":"dane","schemat":"...","dane":{...}};
        // plik: {"rodzaj":"plik","sha256":"<obiekt kontekstu>","nazwa":"...","typ":"text/plain"} – rozmiar uzupełnia protokół.
        // Części są objęte skrótem całego pliku zapisanym w dzienniku przy wysłaniu.
        static List<Dictionary<string, object>> SprawdzCzesci(List<Dictionary<string, object>> wej)
        {
            var r = new List<Dictionary<string, object>>();
            if (wej == null) return r;
            if (wej.Count > 50) throw new EcxpException("Najwyżej 50 części w jednej wiadomości.");
            foreach (var c in wej)
            {
                var rodzaj = S(c, "rodzaj");
                if (!RodzajeCzesci.Contains(rodzaj)) throw new EcxpException("Część o nieznanym rodzaju '" + rodzaj + "'. Dozwolone: tekst, dane, plik.");
                if (rodzaj == "tekst")
                {
                    if (S(c, "tekst").Trim().Length == 0) throw new EcxpException("Część tekst jest pusta.");
                    r.Add(D("rodzaj", "tekst", "tekst", S(c, "tekst")));
                }
                else if (rodzaj == "dane")
                {
                    object v; c.TryGetValue("dane", out v);
                    if (!(v is Dictionary<string, object>)) throw new EcxpException("Część dane musi mieć pole dane będące obiektem JSON.");
                    if (Js().Serialize(v).Length > 200000) throw new EcxpException("Część dane większa niż 200 kB – zapisz ją jako obiekt kontekstu.");
                    r.Add(D("rodzaj", "dane", "schemat", S(c, "schemat"), "dane", v));
                }
                else
                {
                    var sha = S(c, "sha256").ToLowerInvariant();
                    if (!ReSha.IsMatch(sha) || !File.Exists(ObjPath(sha))) throw new EcxpException("Część plik wskazuje nieistniejący obiekt kontekstu '" + sha + "'. Najpierw ecxp_kontekst_zapisz.");
                    r.Add(D("rodzaj", "plik", "sha256", sha, "nazwa", S(c, "nazwa"), "typ", S(c, "typ"), "rozmiar", new FileInfo(ObjPath(sha)).Length));
                }
            }
            return r;
        }

        // ---------- warstwa 3: obiekty kontekstu adresowane skrótem ----------
        // kontekst\obiekty\<2 znaki>\<sha256>: niezmienne, tylko do odczytu, sprawdzane przy każdym pobraniu.
        public const long MaxObiekt = 32L * 1024 * 1024;
        static readonly Regex ReSha = new Regex("^[0-9a-f]{64}$");
        public static string ObjPath(string sha) { return Path.Combine(Root, "kontekst", "obiekty", sha.Substring(0, 2), sha); }

        // Części wiadomości dla odbiorcy: plik dostaje pole sciezka – kopię obiektu pod właściwą nazwą
        // (kontekst\zalaczniki\<12 znaków skrótu>\<nazwa>, tylko do odczytu), żeby agent mógł go po prostu otworzyć.
        public static List<Dictionary<string, object>> CzesciDoOdbioru(List<Dictionary<string, object>> czesci)
        {
            var r = new List<Dictionary<string, object>>();
            foreach (var c in czesci)
            {
                var d = new Dictionary<string, object>(c);
                if (S(c, "rodzaj") == "plik")
                {
                    try { d["sciezka"] = KopiaZalacznika(S(c, "sha256"), S(c, "nazwa")); d["integralnosc"] = "ok"; }
                    catch (Exception ex) { d["integralnosc"] = "USZKODZONA: " + ex.Message; }
                }
                r.Add(d);
            }
            return r;
        }

        public static string KopiaZalacznika(string sha, string nazwa)
        {
            sha = (sha ?? "").Trim().ToLowerInvariant();
            if (!ReSha.IsMatch(sha)) throw new EcxpException("niepoprawny skrót");
            var src = ObjPath(sha);
            if (!File.Exists(src)) throw new EcxpException("brak obiektu w magazynie kontekstu");
            var bezp = Regex.Replace(Path.GetFileName(nazwa ?? ""), @"[\\/:*?""<>|\x00-\x1f]", "_").Trim(' ', '.');
            if (bezp.Length == 0) bezp = "zalacznik";
            if (bezp.Length > 120) bezp = bezp.Substring(bezp.Length - 120);
            var dst = Path.Combine(Root, "kontekst", "zalaczniki", sha.Substring(0, 12), bezp);
            if (File.Exists(dst) && ShaPliku(dst) == sha) return dst;
            if (ShaPliku(src) != sha) throw new EcxpException("skrót obiektu się nie zgadza");
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            if (File.Exists(dst)) File.SetAttributes(dst, FileAttributes.Normal);
            var tmp = dst + ".tmp";
            File.Copy(src, tmp, true);
            File.SetAttributes(tmp, FileAttributes.Normal);
            if (File.Exists(dst)) File.Replace(tmp, dst, null); else File.Move(tmp, dst);
            File.SetAttributes(dst, FileAttributes.ReadOnly);
            return dst;
        }
        static readonly string[] Zakazane = { "\\.ssh\\", "\\.gnupg\\", "\\.aws\\", "auth.json", ".credentials", "id_rsa", "id_ed25519", ".pem", ".pfx", ".p12", ".key", "\\.env", "secret", "\\dowody\\" };

        public static Dictionary<string, object> ZapiszObiekt(string ja, byte[] dane, string nazwa, string typ)
        {
            if (dane == null || dane.LongLength == 0) throw new EcxpException("Pusty obiekt.");
            if (dane.LongLength > MaxObiekt) throw new EcxpException("Obiekt większy niż 32 MB.");
            string sha; using (var h = SHA256.Create()) sha = Hex(h.ComputeHash(dane));
            return ZBlokada(delegate
            {
                var p = ObjPath(sha);
                bool nowy = !File.Exists(p);
                if (nowy)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(p));
                    var tmp = p + ".tmp";
                    using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.WriteThrough))
                    { fs.Write(dane, 0, dane.Length); fs.Flush(true); }
                    File.Move(tmp, p);
                    File.SetAttributes(p, FileAttributes.ReadOnly);
                }
                Zdarzenie("obiekt", D("sha256", sha, "rozmiar", dane.LongLength, "nazwa", nazwa ?? "", "typ", typ ?? "", "kto", ja, "nowy", nowy));
                Sygnal(ja, "kontekst_zapisz");
                return D("sha256", sha, "rozmiar", dane.LongLength, "nowy", nowy, "nazwa", nazwa ?? "", "typ", typ ?? "");
            });
        }

        public static Dictionary<string, object> ZapiszPlik(string ja, string sciezka, string nazwa, string typ)
        {
            var pelna = Path.GetFullPath(sciezka);
            var l = pelna.ToLowerInvariant();
            foreach (var z in Zakazane)
                if (l.Contains(z)) throw new EcxpException("Odmowa: ścieżka wygląda na sekret lub dowód ('" + z + "'). Kanał nie przenosi haseł, kluczy ani danych spraw.");
            if (!File.Exists(pelna)) throw new EcxpException("Nie ma pliku " + pelna);
            if (new FileInfo(pelna).Length > MaxObiekt) throw new EcxpException("Plik większy niż 32 MB.");
            return ZapiszObiekt(ja, File.ReadAllBytes(pelna), string.IsNullOrEmpty(nazwa) ? Path.GetFileName(pelna) : nazwa, typ);
        }

        // Pobiera fragment obiektu: tekst UTF-8 w znakach, inne dane w bajtach (base64). Odbiorca sam kontroluje porcje (tokeny).
        public static Dictionary<string, object> PobierzObiekt(string ja, string sha, long od, int ile)
        {
            sha = (sha ?? "").Trim().ToLowerInvariant();
            if (!ReSha.IsMatch(sha)) throw new EcxpException("Niepoprawny skrót sha256.");
            var p = ObjPath(sha);
            if (!File.Exists(p)) throw new EcxpException("Nie ma obiektu kontekstu " + sha + ".");
            var b = File.ReadAllBytes(p);
            using (var h = SHA256.Create())
                if (Hex(h.ComputeHash(b)) != sha) throw new EcxpException("Obiekt " + sha + " jest uszkodzony – skrót się nie zgadza. Nie używaj go i zgłoś właścicielowi.");
            if (od < 0) od = 0;
            if (ile <= 0) ile = 20000;
            if (ile > 100000) ile = 100000;
            Sygnal(ja, "kontekst_pobierz");
            string tekst = null;
            try { tekst = new UTF8Encoding(false, true).GetString(b); } catch (DecoderFallbackException) { }
            if (tekst != null && tekst.IndexOf('\0') < 0)
            {
                var start = (int)Math.Min(od, tekst.Length);
                var n = Math.Min(ile, tekst.Length - start);
                return D("sha256", sha, "kodowanie", "utf-8", "jednostka", "znaki", "calosc", tekst.Length, "od", start, "ile", n,
                    "koniec", start + n >= tekst.Length, "fragment", tekst.Substring(start, n));
            }
            var bs = (int)Math.Min(od, b.LongLength);
            var bn = Math.Min(Math.Min(ile, 48000), b.Length - bs);
            return D("sha256", sha, "kodowanie", "base64", "jednostka", "bajty", "calosc", b.Length, "od", bs, "ile", bn,
                "koniec", bs + bn >= b.Length, "fragment", Convert.ToBase64String(b, bs, bn));
        }

        // ---------- warstwa 3: zadania (A2A Task) ----------
        public static Dictionary<string, object> ZmienZadanie(string ja, string id, string stan, string uwagi, List<string> artefakty)
        {
            if (!StanyZadan.Contains(stan)) throw new EcxpException("Niepoprawny stan zadania '" + stan + "'. Dozwolone: " + string.Join(", ", StanyZadan));
            artefakty = artefakty ?? new List<string>();
            foreach (var a in artefakty)
                if (!ReSha.IsMatch(a) || !File.Exists(ObjPath(a))) throw new EcxpException("Artefakt '" + a + "' nie jest zapisanym obiektem kontekstu.");
            return ZBlokada(delegate
            {
                var m = Znajdz(id);
                if (m == null) throw new EcxpException("Nie ma zadania o id '" + id + "'.");
                if (m.Typ != "zadanie") throw new EcxpException("Wiadomość '" + m.Id + "' nie jest zadaniem (typ " + m.Typ + ").");
                bool wykonawca = DoMnie(ja, m.Do), zleceniodawca = ja == m.Od;
                if (!wykonawca && !zleceniodawca) throw new EcxpException("Stan zadania zmienia jego wykonawca (" + m.Do + ") albo zleceniodawca (" + m.Od + ").");
                if (!wykonawca && stan != "anulowane") throw new EcxpException("Zleceniodawca może zadanie tylko anulować.");
                // wykonawca zamyka zadanie z obiegu operatora = FIN (wcześniej obieg dalej czekał i strażnik przypominał);
                // przed zdarzeniem – błąd obiegu (np. sprawdzający przed wykonawcą) nie zostawia połowicznego stanu
                bool finObiegu = false;
                if (wykonawca && (stan == "zakonczone" || stan == "nieudane" || stan == "odrzucone") && Obieg.CzekaNaFin(m.Id, ja))
                {
                    Obieg.ZAgenta(new Nowa { Od = ja, Do = Wlasciciel, Projekt = m.Projekt, Modul = m.Modul, Typ = "fin", OdpNa = m.Id,
                        Tresc = (stan == "zakonczone" ? "POZYTYWNY" : "NEGATYWNY (" + stan + ")") + (string.IsNullOrEmpty(uwagi) ? "" : " – " + uwagi) });
                    finObiegu = true;
                }
                Zdarzenie("zadanie", D("id", m.Id, "stan", stan, "kto", ja, "uwagi", uwagi ?? "", "artefakty", artefakty));
                if (PoZmianieZadania != null) PoZmianieZadania(m.Id, stan, ja, uwagi ?? "");
                var drugi = zleceniodawca ? m.Do : m.Od;
                string powiadomienie = "";
                if (PoprawnyAdresat(drugi) && drugi != ja && !finObiegu)
                {
                    var czesci = new List<Dictionary<string, object>> { D("rodzaj", "dane", "schemat", "ecxp/stan-zadania", "dane", D("zadanie_id", m.Id, "stan", stan, "artefakty", artefakty)) };
                    foreach (var a in artefakty) czesci.Add(D("rodzaj", "plik", "sha256", a, "nazwa", "artefakt"));
                    var r = Wyslij(new Nowa { Od = ja, Do = drugi, Modul = m.Modul.Length > 0 ? m.Modul : "wspolne", Typ = "informacja", Temat = Baza(m.Temat),
                        Tresc = "Zadanie " + Baza(m.Temat) + ": " + stan + (string.IsNullOrEmpty(uwagi) ? "" : " – " + uwagi), OdpNa = m.Id, Czesci = czesci });
                    powiadomienie = (string)r["id"];
                }
                return D("id", m.Id, "stan", stan, "powiadomienie", powiadomienie);
            });
        }

        // ---------- warstwa 3: karty węzłów (A2A Agent Card) ----------
        public static Dictionary<string, object> Karta(string ja, string opis, List<string> moduly, List<string> umiejetnosci)
        {
            var dir = Path.Combine(Root, "stan", "karty");
            Directory.CreateDirectory(dir);
            var k = D("protokol", Protokol, "nazwa", ja, "opis", opis ?? "", "moduly", moduly ?? new List<string>(),
                "umiejetnosci", umiejetnosci ?? new List<string>(), "dostep", "mcp:ecxp", "aktualizacja", Teraz());
            var p = Path.Combine(dir, ja + ".json");
            var tmp = p + "." + Losowe(4) + ".tmp";
            File.WriteAllText(tmp, Js().Serialize(k), Utf8);
            if (File.Exists(p)) File.Replace(tmp, p, null); else File.Move(tmp, p);
            Zdarzenie("karta", D("kto", ja, "moduly", moduly ?? new List<string>()));
            Sygnal(ja, "karta");
            return k;
        }

        public static List<Dictionary<string, object>> Karty()
        {
            var r = new List<Dictionary<string, object>>();
            var dir = Path.Combine(Root, "stan", "karty");
            if (!Directory.Exists(dir)) return r;
            var js = Js();
            foreach (var f in new DirectoryInfo(dir).GetFiles("*.json"))
                try { var d = js.Deserialize<Dictionary<string, object>>(Czytaj(f.FullName)); if (d != null) r.Add(d); } catch { }
            return r;
        }

        // ---------- stan dla węzła (narzędzie ecxp_stan) ----------
        public static Dictionary<string, object> StanDla(string ja)
        {
            string spoj;
            var stany = Stany(out spoj);
            var teraz = DateTime.Now;
            var moje = stany.Values.Where(s => s.Od == ja && s.Wyslana > teraz.AddDays(-7)).OrderByDescending(s => s.Wyslana).Take(40)
                .Select(s => (object)D("id", s.Id, "do", s.Do, "temat", s.Temat, "wyslana", s.Wyslana.ToString("yyyy-MM-dd HH:mm"),
                    "stan", Opis(s, teraz))).ToList();
            var czeka = 0;
            var dir = Path.Combine(Root, Folder(ja));
            if (Directory.Exists(dir))
            {
                var js = Js();
                czeka = new DirectoryInfo(dir).GetFiles("*.json").Count(f => DoMnie(ja, Wczytaj(f, "", js).Do));
            }
            var wezly = Wezly().Select(w => (object)D("nazwa", w.Nazwa, "ostatnio", w.Ostatnio.ToString("yyyy-MM-dd HH:mm"), "akcja", w.Akcja)).ToList();
            Sygnal(ja, "stan");
            return D("ja", ja, "czeka_na_mnie", czeka, "moje_wyslane", moje, "wezly", wezly, "karty", Karty(), "dziennik", spoj);
        }

        public static string Opis(Stan s, DateTime teraz)
        {
            var z = s.ZadanieStan.Length > 0 ? "zadanie: " + s.ZadanieStan + "; " : "";
            return z + OpisDoreczenia(s, teraz);
        }

        static string OpisDoreczenia(Stan s, DateTime teraz)
        {
            if (s.Zaginiona) return "ZAGINIONA";
            if (s.Potwierdzona.HasValue) return "potwierdzona " + s.Potwierdzona.Value.ToString("dd.MM HH:mm") + (s.Wynik.Length > 0 ? " (" + s.Wynik + ")" : "");
            var r = s.Dostarczona.HasValue ? "dostarczona " + s.Dostarczona.Value.ToString("dd.MM HH:mm") + ", czeka na potwierdzenie" : "wysłana, jeszcze nie odebrana";
            if (s.Eskalacja) r += " – ESKALACJA";
            else if (s.Ponowienia > 0) r += " – ponowienie " + s.Ponowienia;
            return r + " (od " + Wiek(teraz - s.Wyslana) + ")";
        }
    }
}
