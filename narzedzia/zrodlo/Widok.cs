// ECXP – widok skrzynki dla człowieka: foldery <skrzynka>\Odebrane i <skrzynka>\Wysłane.
//  * Jeden plik .txt na wiadomość, nazwa: „RRRR-MM-DD GG-MM · Rodzaj · TEMAT · od/do … · id.txt” – Eksplorator sortuje po dacie.
//  * Wysłane = wiadomości operatora (także kopie wpisów z czatu), Odebrane = wszystkie pozostałe (od agentów i strażnika).
//  * Wiadomość do pary (lustro 1:1) to jeden plik z obiema stronami; kopie dla pary nie mają osobnych plików.
//  * W pliku: nagłówek, treść, załączniki, kontrole, stan doręczenia u każdego adresata i miejsce oryginału.
//  * Widok obejmuje też archiwum (pełna historia). Tworzy go wyłącznie stos; pliki są tylko do odczytu, nic nie jest kasowane.
//    Kolejki protokołu (do-*, przeczytane, archiwum) zostają bez zmian – widok niczego nie wysyła ani nie zmienia.
//  * Indeks: stan\widok.json (klucz wiadomości → plik widoku, skrót treści, identyfikatory kopii) – plik przepisywany tylko po zmianie.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Ecxp
{
    static class Widok
    {
        public const string FolderOdebrane = "Odebrane", FolderWyslane = "Wysłane";
        public static Action<string> Log = s => { };
        public static object Blokada = new object();
        public static volatile int Zmiana = 1;
        static string Indeks { get { return Path.Combine(P.Root, "stan", "widok.json"); } }
        static readonly UTF8Encoding Bom = new UTF8Encoding(true);

        // Ścieżka należy do widoku (zapisy widoku nie mogą wywoływać kolejnych odświeżeń).
        public static bool JestWidokiem(string sciezka)
        {
            var l = (sciezka ?? "").ToLowerInvariant();
            return l.Contains("\\" + FolderOdebrane.ToLowerInvariant() + "\\") || l.EndsWith("\\" + FolderOdebrane.ToLowerInvariant())
                || l.Contains("\\" + FolderWyslane.ToLowerInvariant() + "\\") || l.EndsWith("\\" + FolderWyslane.ToLowerInvariant())
                || l.EndsWith("\\stan\\widok.json");
        }

        public static void Petla()
        {
            Thread.Sleep(5000);
            var ostPelny = DateTime.MinValue;
            while (true)
            {
                try
                {
                    if (Zmiana == 1 || (DateTime.Now - ostPelny).TotalSeconds >= 60)
                    {
                        Thread.Sleep(1500);            // seria zapisów stosu (wiadomość, kopie, dziennik) – jedno odświeżenie
                        Zmiana = 0;
                        Odswiez();
                        ostPelny = DateTime.Now;
                    }
                }
                catch (Exception ex) { Log("widok: " + ex.Message); }
                Thread.Sleep(2000);
            }
        }

        class Pozycja { public string Klucz = "", Folder = "", Nazwa = "", Tresc = ""; public List<string> Ids = new List<string>(); }

        public static Dictionary<string, object> Odswiez()
        {
            lock (Blokada) return OdswiezWidok();
        }
        // Globalna blokada stosu jest trzymana tylko na czas odczytu wiadomości i stanu doręczeń. Budowa i zapis plików widoku
        // (nawet kilkaset plików) dzieją się poza nią – wysyłka i odbiór nie czekają na widok. Pliki widoku pisze tylko ten wątek.
        static Dictionary<string, object> OdswiezWidok()
        {
            List<Msg> msgs = null; Dictionary<string, Stan> stany = null;
            P.ZBlokada(delegate
            {
                msgs = P.Wszystkie(true);
                string spoj;
                stany = P.Stany(out spoj);
                return 0;
            });
            foreach (var m in msgs) { Stan s; if (stany.TryGetValue(m.Id, out s)) m.Dor = s; }
            P.Powiaz(msgs);
            var pozycje = Zbuduj(msgs);

            var idx = Pliki.Czytaj(Indeks);
            var wpisy = idx.ContainsKey("wiadomosci") && idx["wiadomosci"] is Dictionary<string, object> ? (Dictionary<string, object>)idx["wiadomosci"] : new Dictionary<string, object>();
            int zapisane = 0, bez = 0, bledy = 0;
            bool zmianaIdx = false;
            foreach (var poz in pozycje)
            {
                try
                {
                    var rel = Path.Combine(poz.Folder, poz.Nazwa);
                    var pelna = Path.Combine(P.Root, rel);
                    var skrot = P.Sha(poz.Tresc);
                    object ov; var stary = wpisy.TryGetValue(poz.Klucz, out ov) ? ov as Dictionary<string, object> : null;
                    if (stary != null && P.S(stary, "plik") == rel && P.S(stary, "skrot") == skrot && File.Exists(pelna)) { bez++; continue; }
                    Zapisz(pelna, poz.Tresc);
                    // zmieniona nazwa (np. nowy format) – stary plik widoku zostaje przeniesiony do podfolderu „poprzednie”, nie skasowany
                    if (stary != null && P.S(stary, "plik").Length > 0 && P.S(stary, "plik") != rel) Odloz(Path.Combine(P.Root, P.S(stary, "plik")));
                    wpisy[poz.Klucz] = P.D("plik", rel, "skrot", skrot, "ids", poz.Ids);
                    zapisane++; zmianaIdx = true;
                }
                catch (Exception ex) { bledy++; Log("widok: " + poz.Nazwa + ": " + ex.Message); }
            }
            Info(FolderOdebrane, "Wiadomości odebrane przez Ciebie oraz wymienione między agentami ECXP (Codex, Claude, strażnik).");
            Info(FolderWyslane, "Wiadomości wysłane przez Ciebie (operatora), także wpisy z czatu przekazane drugiej stronie pary.");
            if (zmianaIdx)
            {
                idx["wiadomosci"] = wpisy;
                idx["opis"] = "Indeks widoku skrzynki (foldery Odebrane i Wysłane). Tworzy go stos ECXP.";
                idx["aktualizacja"] = P.Teraz();
                Pliki.Zapisz(Indeks, idx);
            }
            if (zapisane > 0 || bledy > 0) Log("widok: zapisane " + zapisane + ", bez zmian " + bez + (bledy > 0 ? ", błędy " + bledy : ""));
            return P.D("zapisane", zapisane, "bez_zmian", bez, "bledy", bledy);
        }

        // Pozycje widoku: jedna na wiadomość logiczną (lustro = jeden plik), bez kopii dla pary.
        static List<Pozycja> Zbuduj(List<Msg> msgs)
        {
            var wynik = new List<Pozycja>();
            var poId = msgs.GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());
            foreach (var g in msgs.Where(m => !(m.KopiaDla.Length > 0 && !m.KopiaDla.StartsWith("czat:")))
                                  .GroupBy(m => m.Lustro.Count > 1 && m.LustroId.Length > 0 ? "L|" + m.LustroId : "M|" + m.Id))
            {
                var kopie = g.OrderBy(m => m.Do).ToList();
                var m0 = kopie.OrderBy(m => m.Czas).First();
                var wyslana = m0.Od == P.Wlasciciel;
                var dlaPary = msgs.Where(x => x.KopiaDla.Length > 0 && kopie.Any(k => k.Id == x.KopiaDla)).ToList();
                var poz = new Pozycja
                {
                    Klucz = g.Key,
                    Folder = wyslana ? FolderWyslane : FolderOdebrane,
                    Ids = kopie.Select(k => k.Id).Concat(dlaPary.Select(k => k.Id)).Distinct().ToList()
                };
                poz.Nazwa = Nazwa(m0, kopie, wyslana);
                poz.Tresc = Tresc(m0, kopie, dlaPary, wyslana, poId);
                wynik.Add(poz);
            }
            // limit ścieżki Windows (260 znaków z plikiem .tmp): środek nazwy (temat, nadawca/adresat) skracany, data i identyfikator zostają
            foreach (var p in wynik)
            {
                int max = 255 - (Path.Combine(P.Root, p.Folder).Length + 1) - 4 - 6;
                p.Nazwa = Dopasuj(p.Nazwa, max);
            }
            // nazwy unikalne w folderze (te same minuty, rodzaj i temat – rozróżnia je końcówka identyfikatora)
            foreach (var kol in wynik.GroupBy(p => (p.Folder + "\\" + p.Nazwa).ToLowerInvariant()).Where(x => x.Count() > 1))
            {
                int i = 1;
                foreach (var p in kol.Skip(1)) p.Nazwa = Path.GetFileNameWithoutExtension(p.Nazwa) + " (" + (++i) + ").txt";
            }
            return wynik;
        }

        static string Dopasuj(string nazwa, int max)
        {
            if (nazwa.Length <= max) return nazwa;
            var i1 = nazwa.IndexOf(" · ", StringComparison.Ordinal);
            var i2 = nazwa.LastIndexOf(" · ", StringComparison.Ordinal);
            if (i1 < 0 || i2 <= i1 || max < 40) return nazwa.Substring(0, Math.Max(1, Math.Min(nazwa.Length, max - 4))).TrimEnd('.', ' ') + ".txt";
            var poczatek = nazwa.Substring(0, i1);
            var koniec = nazwa.Substring(i2);
            var srodek = nazwa.Substring(i1, i2 - i1);
            var zmiesci = max - poczatek.Length - koniec.Length - 1;
            return poczatek + (zmiesci > 3 ? srodek.Substring(0, zmiesci).TrimEnd('.', ' ') + "…" : "") + koniec;
        }

        static string Krotko(string n)
        {
            if (n == P.Wlasciciel) return "Ty";
            if (n == P.Straznik) return "strażnik";
            if (n.StartsWith("codex-")) return "Codex " + n.Substring(6);
            if (n.StartsWith("claude-")) return "Claude " + n.Substring(7);
            return n;
        }

        static string Bezpieczna(string s, int max)
        {
            s = Regex.Replace(s ?? "", @"[\x00-\x1F<>:""/\\|?*]", "-");
            s = Regex.Replace(s, @"\s+", " ").Trim();
            if (s.Length > max) s = s.Substring(0, max).TrimEnd() + "…";
            return s.TrimEnd('.', ' ');
        }

        static string Nazwa(Msg m, List<Msg> kopie, bool wyslana)
        {
            var adresaci = kopie.Select(k => k.Do).Distinct().ToList();
            string kto = wyslana
                ? "do " + string.Join(" + ", adresaci.Select(Krotko))
                : "od " + Krotko(m.Od) + (adresaci.All(a => a == P.Wlasciciel) ? "" : " do " + string.Join(" + ", adresaci.Select(Krotko)));
            var id = m.Id.Length >= 4 ? m.Id.Substring(m.Id.Length - 4) : m.Id;
            var rodzaj = m.KopiaDla.StartsWith("czat:") ? "Czat" : P.Rodzaj(m);
            var n = m.Czas.ToString("yyyy-MM-dd HH-mm", CultureInfo.InvariantCulture) + " · " + Bezpieczna(rodzaj, 20) + " · " + Bezpieczna(P.Baza(m.Temat), 70) +
                    " · " + Bezpieczna(kto, 70) + " · " + Bezpieczna(id, 8);
            return Bezpieczna(n, 190) + ".txt";
        }

        static string OpisStanu(Msg k)
        {
            if (k.Blad != null) return "USZKODZONA: " + k.Blad;
            var s = k.Dor;
            if (s == null) return k.Katalog == "przeczytane" || k.Katalog == "archiwum" ? "odebrana (stary format, bez dziennika)" : "w kolejce adresata";
            var t = new List<string>();
            if (s.Potwierdzona.HasValue) t.Add("potwierdzona " + s.Potwierdzona.Value.ToString("dd.MM.yyyy HH:mm") + (s.Wynik.Length > 0 ? " (" + s.Wynik + ")" : ""));
            else if (s.Dostarczona.HasValue) t.Add("odebrana " + s.Dostarczona.Value.ToString("dd.MM.yyyy HH:mm") + ", bez potwierdzenia");
            else t.Add("nieodebrana – czeka w kolejce adresata");
            if (s.Ponowienia > 0) t.Add("ponowienia: " + s.Ponowienia);
            if (s.Eskalacja) t.Add(s.Zaginiona ? "ZAGINIONA" : "eskalacja");
            if (s.ZadanieStan.Length > 0) t.Add("zadanie: " + s.ZadanieStan);
            return string.Join(", ", t);
        }

        static string Tresc(Msg m, List<Msg> kopie, List<Msg> dlaPary, bool wyslana, Dictionary<string, Msg> poId)
        {
            var sb = new StringBuilder();
            Action<string> w = x => sb.Append(x).Append("\r\n");
            Func<string, string> nz = n => P.Nazwa(n) + (n == P.Wlasciciel || P.Nazwa(n) == n ? "" : " (" + n + ")");
            w("ECXP · wiadomość " + (wyslana ? "wysłana" : "odebrana"));
            w(new string('─', 60));
            w("Data:      " + m.Czas.ToString("dd.MM.yyyy HH:mm:ss"));
            w("Od:        " + nz(m.Od));
            w("Do:        " + string.Join(", ", kopie.Select(k => nz(k.Do)).Distinct()) + (kopie.Count > 1 ? "   (para 1:1 – każda strona dostała tę samą wiadomość)" : ""));
            w("Projekt:   " + (m.Projekt.Length > 0 ? m.Projekt : "—") + "      Moduł: " + (m.Modul.Length > 0 ? m.Modul : "—"));
            w("Rodzaj:    " + (m.KopiaDla.StartsWith("czat:") ? "Czat (wpis z okna czatu drugiej strony)" : P.Rodzaj(m)) + (m.Pilne ? "      PILNE" : ""));
            w("Temat:     " + P.Baza(m.Temat));
            w("Znacznik:  " + P.ZnacznikId(m.Logiczne.Length > 0 ? m.Logiczne : m.Id));
            if (P.WynikWiadomosci(m).Length > 0) w("Wynik:     " + P.WynikOpis(P.WynikWiadomosci(m)));
            if (m.OdpNa.Length > 0)
            {
                Msg r; w("Odpowiedź na: " + (poId.TryGetValue(m.OdpNa, out r) ? P.ZnacznikId(P.IdWiadomosci(r, poId.Values)) + " · " + P.Rodzaj(r) + " „" + P.Baza(r.Temat) + "” od " + P.Nazwa(r.Od) + " (" + r.Czas.ToString("dd.MM.yyyy HH:mm") + ")" : P.ZnacznikId(m.OdpNa) + " · " + m.OdpNa));
            }
            w("");
            w("STAN DORĘCZENIA");
            foreach (var k in kopie) w("  " + P.Nazwa(k.Do) + ": " + OpisStanu(k));
            foreach (var k in dlaPary) w("  kopia dla pary – " + P.Nazwa(k.Do) + ": " + OpisStanu(k));
            w("");
            w("TREŚĆ");
            w(new string('─', 60));
            if (m.Blad != null) w("[Nie da się odczytać wiadomości: " + m.Blad + "]");
            w((m.Uwagi ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n").TrimEnd());
            foreach (var c in m.Czesci.Where(c => P.S(c, "rodzaj") == "tekst" && P.S(c, "tekst") != m.Uwagi)) { w(""); w(P.S(c, "tekst").Replace("\r\n", "\n").Replace("\n", "\r\n").TrimEnd()); }
            var pliki = m.Czesci.Where(c => P.S(c, "rodzaj") == "plik").ToList();
            if (pliki.Count > 0)
            {
                w(""); w("ZAŁĄCZNIKI");
                foreach (var c in pliki)
                {
                    long roz; long.TryParse(P.S(c, "rozmiar"), out roz);
                    var sha = P.S(c, "sha256");
                    w("  • " + (P.S(c, "nazwa").Length > 0 ? P.S(c, "nazwa") : sha) + (P.S(c, "typ").Length > 0 ? "  (" + P.S(c, "typ") + (roz > 0 ? ", " + roz + " B" : "") + ")" : "") +
                      (sha.Length >= 12 ? "\r\n    obiekt kontekstu: kontekst\\…\\" + sha + "   (otwórz w ECXP-MAILBOX – plik jest sprawdzany skrótem)" : ""));
                }
            }
            foreach (var c in m.Czesci.Where(c => P.S(c, "rodzaj") == "dane")) { w(""); w("DANE: " + (P.S(c, "schemat").Length > 0 ? P.S(c, "schemat") : "JSON")); }
            if (m.Kontrole.Count > 0) { w(""); w("KONTROLE"); foreach (var k in m.Kontrole) w("  • " + k); }
            if (m.Pliki.Count > 0) { w(""); w("PLIKI"); foreach (var p in m.Pliki) w("  • " + p); }
            if (m.Commit.Length > 0) { w(""); w("Commit: " + m.Commit); }
            if (m.Ograniczenia.Length > 0) { w(""); w("Ograniczenia: " + m.Ograniczenia); }
            w("");
            w(new string('─', 60));
            w("Id:        " + string.Join(", ", kopie.Select(k => k.Id)));
            w("Wątek:     " + (m.WatekPole.Length > 0 ? m.WatekPole : m.Watek.Length > 0 ? m.Watek : m.Id));
            w("Oryginał:  " + string.Join(", ", kopie.Select(k => k.Katalog + "\\" + (k.Katalog == "archiwum" ? Path.GetFileName(Path.GetDirectoryName(k.Sciezka)) + "\\" : "") + k.Plik)));
            w("");
            w("Ten plik to widok tylko do odczytu, tworzony automatycznie przez stos ECXP. Zmiana albo usunięcie go");
            w("niczego nie wysyła i nie zmienia w skrzynce. Oryginał (plik protokołu .json) nie jest edytowany.");
            return sb.ToString();
        }

        static void Zapisz(string pelna, string tresc)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(pelna));
            var tmp = pelna + ".tmp";
            File.WriteAllText(tmp, tresc, Bom);
            if (File.Exists(pelna))
            {
                File.SetAttributes(pelna, File.GetAttributes(pelna) & ~FileAttributes.ReadOnly);
                File.Replace(tmp, pelna, null);
            }
            else File.Move(tmp, pelna);
            File.SetAttributes(pelna, File.GetAttributes(pelna) | FileAttributes.ReadOnly);
        }

        static void Odloz(string pelna)
        {
            try
            {
                if (!File.Exists(pelna)) return;
                var dir = Path.Combine(Path.GetDirectoryName(pelna), "poprzednie");
                Directory.CreateDirectory(dir);
                var cel = Path.Combine(dir, Path.GetFileName(pelna));
                if (File.Exists(cel)) cel = Path.Combine(dir, Path.GetFileNameWithoutExtension(pelna) + " " + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                File.Move(pelna, cel);
            }
            catch (Exception ex) { Log("widok: odłożenie " + pelna + ": " + ex.Message); }
        }

        static void Info(string folder, string opis)
        {
            try
            {
                var p = Path.Combine(P.Root, folder, "_O TYM FOLDERZE.txt");
                var t = "ECXP · folder „" + folder + "”\r\n\r\n" + opis + "\r\n\r\n" +
                        "Jeden plik na wiadomość. Nazwa: data i godzina · rodzaj · temat · nadawca/adresat · końcówka identyfikatora,\r\n" +
                        "więc pliki układają się od najstarszej do najnowszej. W pliku jest treść, załączniki i aktualny stan doręczenia.\r\n\r\n" +
                        "To widok tylko do odczytu, tworzony automatycznie przez stos ECXP (także z archiwum). Zmiana albo usunięcie\r\n" +
                        "pliku niczego nie wysyła i nie zmienia w skrzynce. Wiadomości wysyłasz i obsługujesz w ECXP-MAILBOX.\r\n";
                if (File.Exists(p) && File.ReadAllText(p) == t) return;
                Zapisz(p, t);
            }
            catch (Exception ex) { Log("widok: opis folderu " + folder + ": " + ex.Message); }
        }
    }
}
