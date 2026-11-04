// ECXP – dziennik tur agentów i zużycia tokenów (bez modelu AI, tylko odczyt zapisów rozmów).
//  * Claude: hook Stop (ecxp-mcp.exe hook Stop) odkłada sygnał do stan\tury\sygnaly\; stos czyta przyrost zapisu rozmowy
//    (~\.claude\projects\…\<sesja>.jsonl) od początku tury i sumuje message.usage (każda wiadomość raz – po id).
//  * Codex: pętla lustra czyta rollout-*.jsonl: event_msg token_count (licznik narastający), task_started, task_complete.
//  * Agenci w tle: wynik tury Claude (usage z -p --output-format json).
// Tokeny zadania = suma tur adresata od doręczenia zadania do pierwszej tury zakończonej po jego odpowiedzi.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Ecxp
{
    class Tura
    {
        public string Nazwa = "", Strona = "", Sesja = "", Id = "", Zrodlo = "";
        public DateTime Czas;
        public long Wejscie, CacheOdczyt, CacheZapis, Wyjscie;
        public long Razem { get { return Wejscie + CacheOdczyt + CacheZapis + Wyjscie; } }
    }

    static class Tury
    {
        static readonly object brama = new object();
        public static string Dir { get { return Path.Combine(P.Root, "stan", "tury"); } }
        public static string DirSygnaly { get { return Path.Combine(Dir, "sygnaly"); } }
        static string PlikOffsetow { get { return Path.Combine(Dir, "offsety-claude.json"); } }
        static string Plik(string nazwa) { return Path.Combine(Dir, Path.GetFileName(nazwa) + ".jsonl"); }

        public static void Zapisz(Tura t)
        {
            if (!P.PoprawnyNadawca(t.Nazwa) || t.Razem <= 0) return;
            var d = P.D("czas", t.Czas.ToString("o", CultureInfo.InvariantCulture), "strona", t.Strona, "sesja", t.Sesja, "tura", t.Id, "zrodlo", t.Zrodlo,
                "wejscie", t.Wejscie, "cache_odczyt", t.CacheOdczyt, "cache_zapis", t.CacheZapis, "wyjscie", t.Wyjscie);
            lock (brama)
            {
                Directory.CreateDirectory(Dir);
                using (var fs = new FileStream(Plik(t.Nazwa), FileMode.Append, FileAccess.Write, FileShare.Read))
                using (var w = new StreamWriter(fs, new UTF8Encoding(false)))
                    w.WriteLine(P.Js().Serialize(d));
            }
        }

        public static List<Tura> Lista(string nazwa)
        {
            var wynik = new List<Tura>();
            var p = Plik(nazwa);
            if (!File.Exists(p)) return wynik;
            var js = P.Js();
            try
            {
                using (var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new StreamReader(fs, Encoding.UTF8))
                {
                    string l;
                    while ((l = r.ReadLine()) != null)
                    {
                        Dictionary<string, object> d;
                        try { d = js.Deserialize<Dictionary<string, object>>(l); } catch { continue; }
                        DateTime cz;
                        if (d == null || !DateTime.TryParse(P.S(d, "czas"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out cz)) continue;
                        wynik.Add(new Tura { Nazwa = nazwa, Czas = cz.ToLocalTime(), Strona = P.S(d, "strona"), Sesja = P.S(d, "sesja"), Id = P.S(d, "tura"), Zrodlo = P.S(d, "zrodlo"),
                            Wejscie = Liczba(d, "wejscie"), CacheOdczyt = Liczba(d, "cache_odczyt"), CacheZapis = Liczba(d, "cache_zapis"), Wyjscie = Liczba(d, "wyjscie") });
                    }
                }
            }
            catch (IOException) { }
            return wynik;
        }

        static long Liczba(Dictionary<string, object> d, string k)
        {
            object o; if (d == null || !d.TryGetValue(k, out o) || o == null) return 0;
            try { return Convert.ToInt64(o, CultureInfo.InvariantCulture); } catch { return 0; }
        }

        // Tokeny zadania: tury adresata od doręczenia (albo wysłania) zadania do pierwszej tury, która skończyła się
        // w chwili odpowiedzi albo po niej. null = brak danych (wtedy konsola pokazuje „—”, nie 0).
        public static Tura DlaOdpowiedzi(string nazwa, DateTime od, DateTime odpowiedz)
        {
            if (!P.PoprawnyNadawca(nazwa)) return null;
            var tury = Lista(nazwa).Where(t => t.Czas > od).OrderBy(t => t.Czas).ToList();
            var koniec = tury.FirstOrDefault(t => t.Czas >= odpowiedz);   // tura, w której padła odpowiedź, kończy się po niej
            if (koniec == null) return null;
            var w = tury.Where(t => t.Czas <= koniec.Czas).ToList();
            return new Tura { Nazwa = nazwa, Czas = koniec.Czas, Id = w.Count.ToString(CultureInfo.InvariantCulture), Zrodlo = string.Join(",", w.Select(t => t.Zrodlo).Distinct()),
                Wejscie = w.Sum(t => t.Wejscie), CacheOdczyt = w.Sum(t => t.CacheOdczyt), CacheZapis = w.Sum(t => t.CacheZapis), Wyjscie = w.Sum(t => t.Wyjscie) };
        }

        // ---------- Claude: sygnały hooka i przyrost zapisu rozmowy ----------
        // Początek tury (UserPromptSubmit) zapisuje długość pliku rozmowy; koniec tury (Stop) liczy przyrost od tego miejsca.
        public static void Sygnal(string rodzaj, string nazwa, string sesja, string zapis)
        {
            Directory.CreateDirectory(DirSygnaly);
            long dl = 0; try { if (File.Exists(zapis)) dl = new FileInfo(zapis).Length; } catch { }
            var o = P.D("rodzaj", rodzaj, "strona", "claude", "nazwa", nazwa, "sesja", sesja, "zapis", zapis, "dlugosc", dl, "czas", DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
            var plik = Path.Combine(DirSygnaly, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + rodzaj + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".json");
            File.WriteAllText(plik + ".tmp", P.Js().Serialize(o), new UTF8Encoding(false));
            File.Move(plik + ".tmp", plik);
        }

        // Obsługa sygnałów przez stos (pętla lustra). Zwraca liczbę zapisanych tur.
        public static int PrzetworzSygnaly()
        {
            if (!Directory.Exists(DirSygnaly)) return 0;
            var pliki = Directory.GetFiles(DirSygnaly, "*.json").OrderBy(x => x).ToList();
            if (pliki.Count == 0) return 0;
            int ile = 0;
            lock (brama)
            {
                var off = File.Exists(PlikOffsetow) ? Pliki.Czytaj(PlikOffsetow) : new Dictionary<string, object>();
                foreach (var f in pliki)
                {
                    try
                    {
                        var s = Pliki.Czytaj(f);
                        var sesja = P.S(s, "sesja"); var zapis = P.S(s, "zapis");
                        if (sesja.Length > 0 && P.S(s, "rodzaj") == "start") { if (!off.ContainsKey(sesja)) off[sesja] = Liczba(s, "dlugosc"); }
                        else if (sesja.Length > 0 && P.S(s, "rodzaj") == "stop" && File.Exists(zapis))
                        {
                            long od = off.ContainsKey(sesja) ? Liczba(off, sesja) : -1;
                            long nowy;
                            // tylko do granicy zapisanej w chwili końca tury – opóźniony sygnał nie dolicza następnej tury
                            var t = PrzyrostClaude(zapis, od, Liczba(s, "dlugosc"), out nowy);
                            off[sesja] = nowy;
                            if (t != null)
                            {
                                DateTime cz; DateTime.TryParse(P.S(s, "czas"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out cz);
                                t.Nazwa = P.S(s, "nazwa"); t.Strona = "claude"; t.Sesja = sesja; t.Zrodlo = "hook";
                                t.Czas = cz == DateTime.MinValue ? DateTime.Now : cz.ToLocalTime();
                                Zapisz(t); ile++;
                            }
                        }
                    }
                    catch { }
                    try { File.Delete(f); } catch { }
                }
                Directory.CreateDirectory(Dir);
                Pliki.Zapisz(PlikOffsetow, off);
            }
            return ile;
        }

        // Suma message.usage wiadomości asystenta zapisanych od offsetu (każda wiadomość raz: ostatni wpis o danym id).
        // offset < 0 (sesja bez znanego początku tury): tylko część po ostatnim wpisie operatora w ostatnich 8 MB.
        // granica > 0: czytamy najwyżej do niej (długość pliku w chwili sygnału Stop); pusty przyrost – offset bez zmian.
        public static Tura PrzyrostClaude(string zapis, long offset, long granica, out long nowyOffset)
        {
            nowyOffset = offset < 0 ? 0 : offset;
            string porcja;
            using (var fs = new FileStream(zapis, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                long dl = granica > 0 ? Math.Min(fs.Length, granica) : fs.Length;
                long start = offset >= 0 ? Math.Min(offset, dl) : Math.Max(0, dl - 8L * 1024 * 1024);
                nowyOffset = start;
                if (dl <= start) return null;
                fs.Seek(start, SeekOrigin.Begin);
                var bufor = new byte[dl - start];
                int przeczytano = 0;
                while (przeczytano < bufor.Length) { int n = fs.Read(bufor, przeczytano, bufor.Length - przeczytano); if (n <= 0) break; przeczytano += n; }
                int kon = Array.LastIndexOf(bufor, (byte)'\n', Math.Max(0, przeczytano - 1));
                if (kon < 0) return null;
                porcja = Encoding.UTF8.GetString(bufor, 0, kon + 1);
                nowyOffset = start + kon + 1;
            }
            var linie = porcja.Split('\n');
            int odLinii = 0;
            if (offset < 0)
                for (int i = linie.Length - 1; i >= 0; i--)
                    if (WpisOperatora(linie[i])) { odLinii = i + 1; break; }
            var js = P.Js();
            var wiad = new Dictionary<string, Dictionary<string, object>>();
            for (int i = odLinii; i < linie.Length; i++)
            {
                var l = linie[i];
                if (l.IndexOf("\"usage\"", StringComparison.Ordinal) < 0 || l.IndexOf("\"assistant\"", StringComparison.Ordinal) < 0) continue;
                Dictionary<string, object> d;
                try { d = js.Deserialize<Dictionary<string, object>>(l); } catch { continue; }
                if (P.S(d, "type") != "assistant") continue;
                var m = d.ContainsKey("message") ? d["message"] as Dictionary<string, object> : null;
                var u = m != null && m.ContainsKey("usage") ? m["usage"] as Dictionary<string, object> : null;
                if (u == null) continue;
                var id = P.S(m, "id"); if (id.Length == 0) id = "linia-" + i;
                wiad[id] = u;
            }
            if (wiad.Count == 0) return null;
            return new Tura
            {
                Wejscie = wiad.Values.Sum(u => Liczba(u, "input_tokens")),
                CacheOdczyt = wiad.Values.Sum(u => Liczba(u, "cache_read_input_tokens")),
                CacheZapis = wiad.Values.Sum(u => Liczba(u, "cache_creation_input_tokens")),
                Wyjscie = wiad.Values.Sum(u => Liczba(u, "output_tokens"))
            };
        }

        // Wpis operatora w zapisie Claude: wiadomość użytkownika, która nie jest wynikiem narzędzia.
        static bool WpisOperatora(string l)
        {
            return l.IndexOf("\"type\":\"user\"", StringComparison.Ordinal) >= 0 && l.IndexOf("\"tool_use_id\"", StringComparison.Ordinal) < 0
                && l.IndexOf("\"isMeta\":true", StringComparison.Ordinal) < 0;
        }

        // ---------- Codex: liczniki z rollout ----------
        static readonly Dictionary<string, Dictionary<string, object>> licznik = new Dictionary<string, Dictionary<string, object>>();
        static readonly Dictionary<string, Dictionary<string, object>> poczatek = new Dictionary<string, Dictionary<string, object>>();

        // Jedno zdarzenie event_msg z pliku rollout (wywołuje pętla lustra). nazwa = sesja ECXP wątku ("" = wątek spoza par).
        public static void ZdarzenieCodex(string plik, string nazwa, Dictionary<string, object> payload, DateTime czas)
        {
            var typ = P.S(payload, "type");
            lock (licznik)
            {
                if (typ == "token_count")
                {
                    var info = payload.ContainsKey("info") ? payload["info"] as Dictionary<string, object> : null;
                    var tot = info != null && info.ContainsKey("total_token_usage") ? info["total_token_usage"] as Dictionary<string, object> : null;
                    if (tot != null) licznik[plik] = tot;
                }
                else if (typ == "task_started")
                {
                    // bez znanego licznika sprzed tury (np. po starcie stosu) nie liczymy od zera – brak danych dla tej tury
                    Dictionary<string, object> l;
                    if (licznik.TryGetValue(plik, out l)) poczatek[plik] = l; else poczatek.Remove(plik);
                }
                else if (typ == "task_complete")
                {
                    Dictionary<string, object> a, b;
                    if (!poczatek.TryGetValue(plik, out a) || !licznik.TryGetValue(plik, out b)) return;   // początek tury sprzed startu stosu – bez danych
                    poczatek.Remove(plik);
                    if (string.IsNullOrEmpty(nazwa)) return;
                    long we = Liczba(b, "input_tokens") - Liczba(a, "input_tokens"), ca = Liczba(b, "cached_input_tokens") - Liczba(a, "cached_input_tokens");
                    long cz = Liczba(b, "cache_write_input_tokens") - Liczba(a, "cache_write_input_tokens"), wy = Liczba(b, "output_tokens") - Liczba(a, "output_tokens");
                    if (we < 0 || ca < 0 || wy < 0) return;
                    var m = Regex.Match(Path.GetFileNameWithoutExtension(plik), @"([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})$");
                    Zapisz(new Tura { Nazwa = nazwa, Strona = "codex", Sesja = m.Success ? m.Groups[1].Value : "", Id = P.S(payload, "turn_id"), Zrodlo = "rollout", Czas = czas,
                        Wejscie = Math.Max(0, we - ca), CacheOdczyt = ca, CacheZapis = Math.Max(0, cz), Wyjscie = wy });
                }
            }
        }

        // ---------- agenci w tle: wynik Claude -p --output-format json ----------
        public static Tura ZWynikuClaude(string wyjscie)
        {
            foreach (var linia in (wyjscie ?? "").Split('\n').Reverse())
            {
                var t = linia.Trim();
                if (!t.StartsWith("{") || t.IndexOf("\"usage\"", StringComparison.Ordinal) < 0) continue;
                try
                {
                    var j = P.Js().Deserialize<Dictionary<string, object>>(t);
                    var u = j != null && j.ContainsKey("usage") ? j["usage"] as Dictionary<string, object> : null;
                    if (u == null) continue;
                    return new Tura { Wejscie = Liczba(u, "input_tokens"), CacheOdczyt = Liczba(u, "cache_read_input_tokens"), CacheZapis = Liczba(u, "cache_creation_input_tokens"),
                        Wyjscie = Liczba(u, "output_tokens"), Sesja = P.S(j, "session_id") };
                }
                catch { }
            }
            return null;
        }
    }
}
