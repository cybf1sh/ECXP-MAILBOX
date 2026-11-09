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
        public Msg Rodzic;
        public Stan Dor;   // stan doręczenia z dziennika (null = wiadomość starego formatu)
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
    }

    class Wezel { public string Nazwa = "", Akcja = ""; public DateTime Ostatnio; }

    class EcxpException : Exception { public EcxpException(string m) : base(m) { } }

    static class P
    {
        public const string Protokol = "ecxp/1";
        public const string Wlasciciel = "wlasciciel", Koordynacja = "codex-koordynacja", Straznik = "ecxp-straznik";
        public static readonly string[] Kolejki = { "do-wlasciciela", "do-codex", "do-claude" };
        public static readonly string[] Foldery = { "do-wlasciciela", "do-codex", "do-claude", "przeczytane" };
        public static readonly string[] Typy = { "pytanie", "zadanie", "informacja", "meldunek", "odpowiedz", "recenzja", "blokada", "decyzja", "fin", "w_trakcie" };
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

        static string OstatniHash()
        {
            var t = Czytaj(Dz).TrimEnd('\r', '\n', ' ');
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
        public static Dictionary<string, Stan> Stany(out string spojnosc)
        {
            var r = new Dictionary<string, Stan>(StringComparer.OrdinalIgnoreCase);
            var tekst = Czytaj(Dz);
            if (tekst.Length == 0) { spojnosc = "pusty (brak zdarzeń)"; return r; }
            var js = Js();
            string prev = Zera, blad = null; int n = 0;
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
            spojnosc = blad != null ? "NARUSZONY: " + blad : "spójny, wpisów: " + n;
            return r;
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
            m.Id = S(d, "id");
            if (m.Id.Length == 0) m.Id = Path.GetFileNameWithoutExtension(f.Name);
            long.TryParse(S(d, "seq"), out m.Seq);
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

        public static List<Msg> Wszystkie(bool zArchiwum)
        {
            var list = new List<Msg>();
            var js = Js();
            foreach (var k in Foldery)
            {
                var p = Path.Combine(Root, k);
                if (!Directory.Exists(p)) continue;
                foreach (var f in new DirectoryInfo(p).GetFiles("*.json")) list.Add(Wczytaj(f, k, js));
            }
            if (zArchiwum)
            {
                var a = Path.Combine(Root, "archiwum");
                if (Directory.Exists(a))
                    foreach (var f in new DirectoryInfo(a).GetFiles("*.json", SearchOption.AllDirectories)) list.Add(Wczytaj(f, "archiwum", js));
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
                m.Watek = "T|" + Baza(root.Temat);
            }
            foreach (var m in list)
            {
                var mm = m;
                m.Odpowiedziano = list.Any(x => x != mm && x.Watek == mm.Watek && x.Od != mm.Od && (x.Rodzic == mm || x.Czas > mm.Czas));
            }
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
                File.WriteAllText(tmp, Js().Serialize(D("nazwa", ja, "ostatnio", Teraz(), "akcja", akcja)), Utf8);
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
                r.Add(new Wezel { Nazwa = Path.GetFileNameWithoutExtension(f.Name), Akcja = S(d, "akcja"), Ostatnio = CzasZ(S(d, "ostatnio"), "", f.LastWriteTime) });
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

        // Wysyła wiadomość. Zwraca id, seq, ścieżkę i termin potwierdzenia.
        public static Dictionary<string, object> Wyslij(Nowa n)
        {
            if (!PoprawnyAdresat(n.Do)) throw new EcxpException("Niepoprawny adresat: '" + n.Do + "'. Dozwolone: wlasciciel, codex[-nazwa], claude[-nazwa].");
            if (n.Od != Wlasciciel && n.Od != Straznik && !PoprawnyNadawca(n.Od)) throw new EcxpException("Niepoprawny nadawca: '" + n.Od + "'. Użyj nazwy codex-<czat> albo claude-<sesja>.");
            if (!Typy.Contains(n.Typ)) throw new EcxpException("Niepoprawny typ: '" + n.Typ + "'. Dozwolone: " + string.Join(", ", Typy));
            if (n.Typ == "w_trakcie") throw new EcxpException("„w trakcie” to stan zadania w stosie, nie wiadomość – wyślij go przez ecxp_wyslij z odpowiedz_na=<id zadania>.");
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
                }
                var seq = NastepnySeq(n.Od, n.Do);
                var id = DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + modul + "-" + temat + "-" + Losowe(2);
                var termin = Termin(n.Do, n.Pilne);
                var d = new Dictionary<string, object>();
                d["protokol"] = Protokol; d["id"] = id; d["seq"] = seq; d["czas"] = Teraz();
                d["od"] = n.Od; d["do"] = n.Do;
                var projekt = Slug(n.Projekt.Length > 0 ? n.Projekt : (org != null ? org.Projekt : ""), 32, false);
                if (modul == "wspolne" && org != null && org.Modul.Length > 0) modul = org.Modul;
                if (projekt.Length > 0) d["projekt"] = projekt;
                d["modul"] = modul; d["typ"] = n.Typ; d["status"] = StatusZTypu(n.Typ);
                d["temat"] = temat; d["pilne"] = n.Pilne;
                if (n.EtapObiegu.Length > 0) d["etap_obiegu"] = n.EtapObiegu;
                if (org != null) d["odpowiedz_na"] = org.Id;
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
                return D("id", id, "seq", seq, "plik", dst, "do", n.Do, "termin_potwierdzenia_min", termin, "potwierdzono_przy_okazji", ack);
            });
        }

        // ---------- zamknięcie wiadomości przez operatora ----------
        // Wiadomość z dowolnej kolejki (także niedoręczona albo pytanie bez odpowiedzi) trafia do archiwum\RRRR-MM.
        // Nic nie jest kasowane. W dzienniku: potwierdzona (wynik „wycofana”, kto: wlasciciel) – strażnik przestaje upominać.
        public static Dictionary<string, object> Wycofaj(List<string> pliki, string powod)
        {
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
                    var dir = Path.Combine(Root, "archiwum", m.Czas.ToString("yyyy-MM"));
                    Directory.CreateDirectory(dir);
                    var dst = Path.Combine(dir, m.Plik);
                    if (File.Exists(dst)) dst = Path.Combine(dir, Path.GetFileNameWithoutExtension(m.Plik) + "-" + Losowe(2) + ".json");
                    File.Move(f.FullName, dst);
                    Stan s;
                    if (stany.TryGetValue(m.Id, out s) && !s.Potwierdzona.HasValue)
                        Zdarzenie("potwierdzona", D("id", m.Id, "kto", Wlasciciel, "wynik", "wycofana", "uwagi", string.IsNullOrEmpty(powod) ? "zamknięta przez operatora" : powod));
                    Zdarzenie("zarchiwizowana", D("id", m.Id, "plik", m.Plik, "z", kat, "kto", Wlasciciel, "powod", powod ?? ""));
                    ok++;
                }
                return D("przeniesione", ok, "brak", brak);
            });
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
