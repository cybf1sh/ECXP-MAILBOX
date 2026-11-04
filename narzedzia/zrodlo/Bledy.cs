// Bledy – moduł obsługi błędów konsoli ECXP-MAILBOX.
// Zasada: zapis TYLKO wtedy, gdy coś pójdzie źle (błąd, ostrzeżenie, nieobsłużony wyjątek). Przy normalnej pracy log jest pusty.
// Każdy wpis jest szczegółowy: wyjątek z pełnym stosem wywołań i wyjątkami wewnętrznymi, miejsce w kodzie, kontekst operacji
// (np. operacja stosu ECXP i jej argumenty), ostatnie kroki programu przed błędem (ślad) oraz stan procesu.
// Pliki: <skrzynka>\stan\logi\mailbox-RRRR-MM-DD.jsonl – jeden wpis JSON w wierszu. Nic nie jest kasowane.
// Ten sam błąd powtarzany w krótkim czasie zapisuje się raz na 10 minut (z liczbą pominiętych powtórzeń).
// Przeglądarka: przycisk „Logi” w oknie konsoli (Bledy.Pokaz).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Ecxp
{
    static class Bledy
    {
        public const string Blad = "BŁĄD", Krytyczny = "BŁĄD KRYTYCZNY", Ostrzezenie = "OSTRZEŻENIE";
        public static string Katalog { get { return Path.Combine(P.Root, "stan", "logi"); } }
        static string PlikObejrzane { get { return Path.Combine(Katalog, "obejrzane.txt"); } }

        static readonly object blok = new object();
        static readonly Queue<string> slad = new Queue<string>();
        static readonly Dictionary<string, DateTime> ostatnio = new Dictionary<string, DateTime>();
        static readonly Dictionary<string, int> pominiete = new Dictionary<string, int>();
        static readonly HashSet<string> raz = new HashSet<string>();
        static int nieobejrzane = -1;
        static Form okno;

        /// <summary>Wywoływane po każdym nowym wpisie (z dowolnego wątku).</summary>
        public static event Action Zmiana;
        /// <summary>Zmiana licznika nieobejrzanych wpisów (np. po obejrzeniu) – tylko dla przycisku; przeglądarka go nie słucha.</summary>
        public static event Action Licznik;

        // ---------- przechwytywanie ----------

        /// <summary>Zakładane w Main przed utworzeniem okien: łapie wszystko, czego nie złapał kod konsoli.</summary>
        public static void Zainstaluj()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                int zapis = Zapisz(Krytyczny, "Nieobsłużony błąd w oknie konsoli: " + e.Exception.Message, e.Exception, null, "okno (wątek interfejsu)");
                if (zapis == 0) return;                 // ten sam błąd przed chwilą – okno już było pokazane
                if (zapis < 0)
                {
                    try { MessageBox.Show(Jezyk.T("Wystąpił nieoczekiwany błąd:") + "\n\n" + e.Exception, "ECXP-MAILBOX", MessageBoxButtons.OK, MessageBoxIcon.Error); } catch { }
                    return;
                }
                try
                {
                    if (MessageBox.Show(Jezyk.T("Wystąpił nieoczekiwany błąd:") + "\n\n" + e.Exception.Message + "\n\n" +
                            Jezyk.T("Konsola działa dalej. Szczegóły zapisano w logach. Pokazać logi teraz?"),
                            "ECXP-MAILBOX", MessageBoxButtons.YesNo, MessageBoxIcon.Error) == DialogResult.Yes)
                        Pokaz(null, null);
                }
                catch { }
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                Zapisz(Krytyczny, "Nieobsłużony wyjątek w procesie" + (e.IsTerminating ? " – program kończy pracę" : ""), e.ExceptionObject as Exception,
                    P.D("program_konczy_prace", e.IsTerminating, "obiekt", e.ExceptionObject == null ? "" : e.ExceptionObject.ToString()), "proces");
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                Zapisz(Blad, "Nieobsłużony wyjątek w zadaniu w tle", e.Exception, null, "zadanie w tle");
                e.SetObserved();
            };
        }

        /// <summary>Krok programu do śladu (pamięć, ostatnie 50). Trafia do pliku dopiero razem z błędem.</summary>
        public static void Slad(string krok)
        {
            lock (blok)
            {
                slad.Enqueue(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + krok);
                while (slad.Count > 50) slad.Dequeue();
            }
        }

        /// <summary>Wyjątek złapany w kodzie konsoli. Miejsce (metoda, wiersz) uzupełnia kompilator.</summary>
        public static void Wyjatek(Exception ex,
            [System.Runtime.CompilerServices.CallerMemberName] string metoda = "",
            [System.Runtime.CompilerServices.CallerLineNumber] int wiersz = 0)
        {
            if (ex == null || ex.Data.Contains("ecxp-log")) return;     // już zapisany głębiej (np. w operacji stosu)
            Zapisz(Blad, ex.Message, ex, null, metoda + ", wiersz " + wiersz);
        }

        public static void ZapiszBlad(string opis, Exception ex = null, object kontekst = null,
            [System.Runtime.CompilerServices.CallerMemberName] string metoda = "",
            [System.Runtime.CompilerServices.CallerLineNumber] int wiersz = 0)
        {
            Zapisz(Blad, opis, ex, kontekst, metoda + ", wiersz " + wiersz);
        }

        public static void ZapiszOstrzezenie(string opis, object kontekst = null,
            [System.Runtime.CompilerServices.CallerMemberName] string metoda = "",
            [System.Runtime.CompilerServices.CallerLineNumber] int wiersz = 0)
        {
            Zapisz(Ostrzezenie, opis, null, kontekst, metoda + ", wiersz " + wiersz);
        }

        /// <summary>Problem stały (np. uszkodzony plik wiadomości): zapis tylko raz na uruchomienie konsoli.</summary>
        public static void Raz(string klucz, string poziom, string opis, object kontekst = null,
            [System.Runtime.CompilerServices.CallerMemberName] string metoda = "",
            [System.Runtime.CompilerServices.CallerLineNumber] int wiersz = 0)
        {
            lock (blok) { if (!raz.Add(klucz)) return; }
            Zapisz(poziom, opis, null, kontekst, metoda + ", wiersz " + wiersz);
        }

        // 1 = wpis zapisany, 0 = powtórka w ciągu 10 min (pominięta), -1 = zapis niemożliwy.
        static int Zapisz(string poziom, string opis, Exception ex, object kontekst, string zrodlo)
        {
            try
            {
                if (ex != null) try { ex.Data["ecxp-log"] = true; } catch { }
                string klucz = poziom + "|" + zrodlo + "|" + opis;
                int pomin = 0;
                string[] kroki;
                lock (blok)
                {
                    DateTime t;
                    if (ostatnio.TryGetValue(klucz, out t) && (DateTime.Now - t).TotalMinutes < 10)
                    {
                        int n; pominiete.TryGetValue(klucz, out n); pominiete[klucz] = n + 1;
                        return 0;
                    }
                    ostatnio[klucz] = DateTime.Now;
                    if (pominiete.TryGetValue(klucz, out pomin)) pominiete.Remove(klucz);
                    kroki = slad.ToArray();
                }
                var proc = Process.GetCurrentProcess();
                var exe = Application.ExecutablePath;
                var wpis = new Dictionary<string, object>
                {
                    { "czas", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffzzz") },
                    { "poziom", poziom },
                    { "gdzie", zrodlo ?? "" },
                    { "opis", opis ?? "" },
                    { "wyjatek", ex == null ? "" : ex.GetType().FullName },
                    { "szczegoly", ex == null ? "" : ex.ToString() },
                    { "wewnetrzne", Wewnetrzne(ex) },
                    { "dane_wyjatku", DaneWyjatku(ex) },
                    { "kontekst", Przytnij(kontekst, 0) },
                    { "pominiete_powtorzenia", pomin },
                    { "slad", kroki },
                    { "program", P.D("plik", exe, "zmieniony", File.Exists(exe) ? File.GetLastWriteTime(exe).ToString("yyyy-MM-dd HH:mm:ss") : "",
                        "pid", proc.Id, "dziala_od", proc.StartTime.ToString("yyyy-MM-dd HH:mm:ss"), "watek", Thread.CurrentThread.ManagedThreadId + (Thread.CurrentThread.Name != null ? " " + Thread.CurrentThread.Name : ""),
                        "pamiec_mb", proc.WorkingSet64 / 1048576, "uchwyty", proc.HandleCount, "skrzynka", P.Root,
                        "system", Environment.OSVersion.VersionString + (Environment.Is64BitProcess ? " (64-bit)" : " (32-bit)"), "clr", Environment.Version.ToString(),
                        "jezyk", Jezyk.Kod) }
                };
                Directory.CreateDirectory(Katalog);
                var plik = Path.Combine(Katalog, "mailbox-" + DateTime.Now.ToString("yyyy-MM-dd") + ".jsonl");
                var linia = P.Js().Serialize(wpis) + "\n";
                lock (blok)
                {
                    using (var fs = new FileStream(plik, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    {
                        var b = new UTF8Encoding(false).GetBytes(linia);
                        fs.Write(b, 0, b.Length);
                    }
                    if (nieobejrzane >= 0) nieobejrzane++;
                }
                var z = Zmiana; if (z != null) z();
                return 1;
            }
            catch { return -1; }   // moduł błędów nigdy sam nie może zatrzymać konsoli
        }

        static List<object> Wewnetrzne(Exception ex)
        {
            var l = new List<object>();
            for (var w = ex == null ? null : ex.InnerException; w != null && l.Count < 10; w = w.InnerException)
                l.Add(P.D("typ", w.GetType().FullName, "opis", w.Message, "stos", w.StackTrace ?? ""));
            var ag = ex as AggregateException;
            if (ag != null) foreach (var w in ag.InnerExceptions.Take(10)) l.Add(P.D("typ", w.GetType().FullName, "opis", w.Message, "stos", w.StackTrace ?? ""));
            return l;
        }

        static Dictionary<string, object> DaneWyjatku(Exception ex)
        {
            var d = new Dictionary<string, object>();
            if (ex == null) return d;
            d["HResult"] = "0x" + ex.HResult.ToString("X8");
            if (ex.Source != null) d["zrodlo"] = ex.Source;
            if (ex.TargetSite != null) d["metoda"] = ex.TargetSite.DeclaringType + "." + ex.TargetSite.Name;
            var w32 = ex as System.ComponentModel.Win32Exception;
            if (w32 != null) d["kod_windows"] = w32.NativeErrorCode;
            var io = ex as IOException;
            if (io != null) d["io"] = true;
            foreach (DictionaryEntry e in ex.Data)
                if (!"ecxp-log".Equals(e.Key)) d["data:" + e.Key] = e.Value == null ? "" : e.Value.ToString();
            return d;
        }

        // Kontekst do zapisu: długie teksty skrócone, pola wyglądające na sekrety zamaskowane.
        static object Przytnij(object o, int glebokosc)
        {
            if (o == null) return null;
            if (glebokosc > 6) return "…";
            var s = o as string;
            if (s != null) return s.Length > 4000 ? s.Substring(0, 4000) + "… (skrócono, " + s.Length + " znaków)" : s;
            var d = o as IDictionary;
            if (d != null)
            {
                var r = new Dictionary<string, object>();
                foreach (DictionaryEntry e in d)
                {
                    var k = e.Key == null ? "" : e.Key.ToString();
                    var kl = k.ToLowerInvariant();
                    r[k] = kl.Contains("haslo") || kl.Contains("hasło") || kl.Contains("password") || kl.Contains("token") || kl.Contains("secret") || kl.Contains("klucz_prywatny")
                        ? "*** (ukryte)" : Przytnij(e.Value, glebokosc + 1);
                }
                return r;
            }
            var l = o as IEnumerable;
            if (l != null) { var r = new List<object>(); foreach (var x in l) { if (r.Count >= 100) { r.Add("…"); break; } r.Add(Przytnij(x, glebokosc + 1)); } return r; }
            if (o is bool || o is int || o is long || o is double || o is decimal) return o;
            return o.ToString();
        }

        // ---------- odczyt ----------

        public static List<string> Pliki()
        {
            try { return Directory.Exists(Katalog) ? Directory.GetFiles(Katalog, "mailbox-*.jsonl").OrderByDescending(x => x).ToList() : new List<string>(); }
            catch { return new List<string>(); }
        }

        public static List<Dictionary<string, object>> Wczytaj(string plik)
        {
            var l = new List<Dictionary<string, object>>();
            try
            {
                string tekst;
                using (var fs = new FileStream(plik, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var sr = new StreamReader(fs, Encoding.UTF8)) tekst = sr.ReadToEnd();
                foreach (var w in tekst.Split('\n'))
                {
                    if (w.Trim().Length == 0) continue;
                    try { l.Add(P.Js().Deserialize<Dictionary<string, object>>(w)); }
                    catch { l.Add(P.D("czas", "", "poziom", Blad, "gdzie", Path.GetFileName(plik), "opis", "Nieczytelny wiersz logu", "szczegoly", w)); }
                }
            }
            catch (Exception ex) { l.Add(P.D("czas", "", "poziom", Blad, "gdzie", Path.GetFileName(plik), "opis", "Nie można odczytać pliku logu: " + ex.Message, "szczegoly", ex.ToString())); }
            return l;
        }

        static DateTime Czas(Dictionary<string, object> w)
        {
            DateTime t; return DateTime.TryParse(P.S(w, "czas"), null, System.Globalization.DateTimeStyles.RoundtripKind, out t) ? t : DateTime.MinValue;
        }

        /// <summary>Liczba wpisów z ostatnich dwóch dni, których operator jeszcze nie obejrzał w przeglądarce logów.</summary>
        public static int Nieobejrzane
        {
            get
            {
                lock (blok) { if (nieobejrzane >= 0) return nieobejrzane; }
                DateTime od; DateTime.TryParse(P.Czytaj(PlikObejrzane).Trim(), null, System.Globalization.DateTimeStyles.RoundtripKind, out od);
                int n = Pliki().Take(2).Sum(p => Wczytaj(p).Count(w => Czas(w) > od));
                lock (blok) { nieobejrzane = n; }
                return n;
            }
        }

        // Operator obejrzał logi: licznik na zero. Nie wywołuje Zmiana (słucha jej przeglądarka – inaczej pętla odświeżania).
        static void Obejrzano()
        {
            bool bylo; lock (blok) { bylo = nieobejrzane != 0; nieobejrzane = 0; }
            if (!bylo && File.Exists(PlikObejrzane)) return;
            try { Directory.CreateDirectory(Katalog); File.WriteAllText(PlikObejrzane, DateTime.Now.ToString("o"), new UTF8Encoding(false)); } catch { }
            var l = Licznik; if (l != null) l();
        }

        // ---------- przeglądarka ----------

        /// <summary>Okno logów: lista wpisów (najnowsze u góry), filtr dnia, poziomu i tekstu, pełne szczegóły wybranego wpisu.</summary>
        public static void Pokaz(Form rodzic, Font font)
        {
            if (okno != null && !okno.IsDisposed) { okno.Show(); okno.WindowState = FormWindowState.Normal; okno.Activate(); Obejrzano(); return; }
            font = font ?? new Font("Segoe UI", 10);
            var f = okno = new OknoEcxp { Text = "Logi ECXP-MAILBOX – błędy i ostrzeżenia", Size = new Size(1150, 760), MinimumSize = new Size(700, 450),
                StartPosition = rodzic != null ? FormStartPosition.CenterParent : FormStartPosition.CenterScreen, Font = font, ShowIcon = false };
            var gora = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 6, 8, 2), WrapContents = true };
            var cbDzien = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Margin = new Padding(3, 6, 10, 3) };
            var cbPoziom = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Margin = new Padding(3, 6, 10, 3) };
            cbPoziom.Items.AddRange(new object[] { "Wszystkie poziomy", "Tylko błędy", "Tylko ostrzeżenia" }); cbPoziom.SelectedIndex = 0;
            var tbSzukaj = new TextBox { Width = 200, Margin = new Padding(3, 6, 10, 3) };
            Func<string, Button> btn = t => new Button { Text = t, AutoSize = true, Margin = new Padding(3, 4, 3, 3), Padding = new Padding(6, 2, 6, 2) };
            var bOdswiez = btn("Odśwież"); var bKopiuj = btn("Kopiuj szczegóły"); var bFolder = btn("Otwórz folder logów");
            gora.Controls.AddRange(new Control[] {
                new Label { Text = "Dzień:", AutoSize = true, Margin = new Padding(0, 10, 0, 0) }, cbDzien,
                new Label { Text = "Poziom:", AutoSize = true, Margin = new Padding(0, 10, 0, 0) }, cbPoziom,
                new Label { Text = "Szukaj:", AutoSize = true, Margin = new Padding(0, 10, 0, 0) }, tbSzukaj, bOdswiez, bKopiuj, bFolder });
            var opis = new Label { Dock = DockStyle.Top, AutoSize = false, Height = 40, Padding = new Padding(12, 4, 8, 4), ForeColor = Color.FromArgb(90, 90, 90),
                Text = Jezyk.T("Konsola zapisuje tu tylko problemy: błędy, ostrzeżenia i nieobsłużone wyjątki – ze stosem wywołań, kontekstem operacji i ostatnimi krokami przed błędem. Przy normalnej pracy lista jest pusta. Pliki: ") + Katalog };
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, Size = new Size(1100, 600), SplitterDistance = 260, Panel1MinSize = 120, Panel2MinSize = 150 };
            var lista = new ListView { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, HideSelection = false, MultiSelect = false };
            lista.Columns.Add("Czas", 160); lista.Columns.Add("Poziom", 130); lista.Columns.Add("Gdzie", 240); lista.Columns.Add("Opis", 560);
            var szcz = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, Font = new Font("Consolas", 9.5f), WordWrap = true, DetectUrls = false, BorderStyle = BorderStyle.None };
            split.Panel1.Controls.Add(lista); split.Panel2.Controls.Add(szcz);
            var pasek = new StatusStrip(); var stan = new ToolStripStatusLabel(); pasek.Items.Add(stan);
            f.Controls.Add(split); f.Controls.Add(opis); f.Controls.Add(gora); f.Controls.Add(pasek);

            Action pokazWybrany = () =>
            {
                var w = lista.SelectedItems.Count > 0 ? lista.SelectedItems[0].Tag as Dictionary<string, object> : null;
                szcz.Text = w == null ? (lista.Items.Count == 0 ? Jezyk.T("Brak wpisów – konsola nie zanotowała tego dnia żadnego problemu.") : Jezyk.T("Wybierz wpis, aby zobaczyć szczegóły.")) : Opis(w);
            };
            Action wczytaj = () =>
            {
                var pliki = Pliki();
                var wybrany = cbDzien.SelectedItem as string;
                var nazwy = pliki.Select(p => Path.GetFileNameWithoutExtension(p).Replace("mailbox-", "")).ToList();
                if (nazwy.Count == 0) nazwy.Add(DateTime.Now.ToString("yyyy-MM-dd"));
                if (!nazwy.SequenceEqual(cbDzien.Items.Cast<string>()))
                {
                    cbDzien.Items.Clear(); foreach (var n in nazwy) cbDzien.Items.Add(n);
                    cbDzien.SelectedIndex = wybrany != null && nazwy.Contains(wybrany) ? nazwy.IndexOf(wybrany) : 0;
                    return;   // zmiana wyboru wywoła wczytanie jeszcze raz
                }
                var plik = Path.Combine(Katalog, "mailbox-" + cbDzien.SelectedItem + ".jsonl");
                var wpisy = File.Exists(plik) ? Wczytaj(plik) : new List<Dictionary<string, object>>();
                var q = tbSzukaj.Text.Trim();
                var widoczne = wpisy.Where(w =>
                        (cbPoziom.SelectedIndex == 0 || (cbPoziom.SelectedIndex == 1 ? P.S(w, "poziom").StartsWith(Blad) : P.S(w, "poziom") == Ostrzezenie)) &&
                        (q.Length == 0 || P.Js().Serialize(w).IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0))
                    .OrderByDescending(Czas).ToList();
                lista.BeginUpdate(); lista.Items.Clear();
                foreach (var w in widoczne)
                {
                    var t = Czas(w);
                    var it = new ListViewItem(t == DateTime.MinValue ? "?" : t.ToString("dd.MM.yyyy HH:mm:ss")) { Tag = w };
                    it.SubItems.Add(P.S(w, "poziom")); it.SubItems.Add(P.S(w, "gdzie")); it.SubItems.Add(P.S(w, "opis").Replace("\r", " ").Replace("\n", " "));
                    var poz = P.S(w, "poziom");
                    it.ForeColor = poz.StartsWith(Blad) ? Motyw.K(Color.FromArgb(190, 0, 0)) : Motyw.K(Color.FromArgb(160, 95, 0));
                    if (poz == Krytyczny) it.Font = new Font(lista.Font, FontStyle.Bold);
                    lista.Items.Add(it);
                }
                lista.EndUpdate();
                if (lista.Items.Count > 0) lista.Items[0].Selected = true;
                pokazWybrany();
                stan.Text = string.Format(Jezyk.T("Wpisy: {0} (błędy: {1}, ostrzeżenia: {2})   ·   plik: {3}"), wpisy.Count,
                    wpisy.Count(w => P.S(w, "poziom").StartsWith(Blad)), wpisy.Count(w => P.S(w, "poziom") == Ostrzezenie), File.Exists(plik) ? plik : Jezyk.T("brak – dziś bez problemów"));
            };
            cbDzien.SelectedIndexChanged += delegate { wczytaj(); };
            cbPoziom.SelectedIndexChanged += delegate { wczytaj(); };
            tbSzukaj.TextChanged += delegate { wczytaj(); };
            lista.SelectedIndexChanged += delegate { pokazWybrany(); };
            bOdswiez.Click += delegate { wczytaj(); Obejrzano(); };
            bKopiuj.Click += delegate { if (szcz.TextLength > 0) try { Clipboard.SetText(szcz.Text); stan.Text = Jezyk.T("Szczegóły skopiowane do schowka."); } catch { } };
            bFolder.Click += delegate { try { Directory.CreateDirectory(Katalog); Process.Start("explorer.exe", "\"" + Katalog + "\""); } catch (Exception ex) { Wyjatek(ex); } };
            Action odswiezGdyNowy = () => { try { if (!f.IsDisposed) f.BeginInvoke((Action)(() => { if (!f.IsDisposed) { wczytaj(); Obejrzano(); } })); } catch { } };
            Zmiana += odswiezGdyNowy;
            f.FormClosed += delegate { Zmiana -= odswiezGdyNowy; okno = null; };
            Motyw.Zastosuj(f); Jezyk.Zastosuj(f);
            wczytaj();
            if (rodzic != null) f.Show(rodzic); else f.Show();
            Obejrzano();
        }

        // Szczegóły jednego wpisu w czytelnej postaci tekstowej.
        public static string Opis(Dictionary<string, object> w)
        {
            var sb = new StringBuilder();
            sb.AppendLine(P.S(w, "poziom") + "   " + P.S(w, "czas"));
            sb.AppendLine("Gdzie:  " + P.S(w, "gdzie"));
            sb.AppendLine("Opis:   " + P.S(w, "opis"));
            object v;
            if (w.TryGetValue("pominiete_powtorzenia", out v) && v != null && v.ToString() != "0")
                sb.AppendLine("Ten sam problem wystąpił wcześniej jeszcze " + v + " raz(y) w ciągu 10 minut (bez osobnych wpisów).");
            if (P.S(w, "wyjatek").Length > 0) { sb.AppendLine(); sb.AppendLine("=== Wyjątek: " + P.S(w, "wyjatek")); }
            if (P.S(w, "szczegoly").Length > 0) sb.AppendLine(P.S(w, "szczegoly"));
            if (w.TryGetValue("wewnetrzne", out v) && v is IList && ((IList)v).Count > 0) { sb.AppendLine(); sb.AppendLine("=== Wyjątki wewnętrzne"); sb.AppendLine(Ladnie(v, 0)); }
            if (w.TryGetValue("dane_wyjatku", out v) && v is IDictionary && ((IDictionary)v).Count > 0) { sb.AppendLine(); sb.AppendLine("=== Dane wyjątku"); sb.AppendLine(Ladnie(v, 0)); }
            if (w.TryGetValue("kontekst", out v) && v != null) { sb.AppendLine(); sb.AppendLine("=== Kontekst operacji"); sb.AppendLine(Ladnie(v, 0)); }
            if (w.TryGetValue("slad", out v) && v is IList && ((IList)v).Count > 0)
            {
                sb.AppendLine(); sb.AppendLine("=== Ostatnie kroki przed problemem (najstarsze u góry)");
                foreach (var k in (IList)v) sb.AppendLine("  " + k);
            }
            if (w.TryGetValue("program", out v) && v != null) { sb.AppendLine(); sb.AppendLine("=== Program"); sb.AppendLine(Ladnie(v, 0)); }
            return sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n");
        }

        static string Ladnie(object o, int wc)
        {
            var pad = new string(' ', wc * 2);
            var d = o as IDictionary;
            if (d != null)
            {
                if (d.Count == 0) return "{}";
                var sb = new StringBuilder();
                foreach (DictionaryEntry e in d)
                {
                    var war = e.Value;
                    bool zlozone = (war is IDictionary && ((IDictionary)war).Count > 0) || (war is IList && ((IList)war).Count > 0);
                    sb.Append(pad + e.Key + ":" + (zlozone ? "\n" + Ladnie(war, wc + 1) : " " + Ladnie(war, wc + 1)) + "\n");
                }
                return sb.ToString().TrimEnd('\n');
            }
            var l = o as IList;
            if (l != null && !(o is string))
            {
                if (l.Count == 0) return "[]";
                var sb = new StringBuilder();
                int i = 0;
                foreach (var x in l)
                {
                    bool zlozone = x is IDictionary || (x is IList && !(x is string));
                    sb.Append(pad + "[" + (i++) + "]" + (zlozone ? "\n" + Ladnie(x, wc + 1) : " " + Ladnie(x, wc + 1)) + "\n");
                }
                return sb.ToString().TrimEnd('\n');
            }
            if (o == null) return "(brak)";
            var s = o.ToString();
            return s.Contains("\n") ? "\n" + string.Join("\n", s.Replace("\r", "").Split('\n').Select(x => pad + "  " + x)) : s;
        }
    }
}
