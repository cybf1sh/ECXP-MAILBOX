// ECXP-MAILBOX – konsola operatora uniwersalnego protokołu ECXP (dowolne projekty i moduły, agenci Codex i Claude).
// Klient stosu ecxpd (rola operatora potwierdzana przez stos po obrazie procesu). Czyta pliki skrzynki tylko do podglądu;
// każdą zmianę (wysłanie, potwierdzenie, archiwum, role, nowe zadanie) wysyła ramkami ECXP do stosu. Kompilacja: buduj.cmd.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("ECXP-MAILBOX")]
[assembly: AssemblyDescription("ECXP-MAILBOX – konsola operatora ECXP")]
[assembly: AssemblyProduct("ECXP-MAILBOX")]
[assembly: AssemblyCompany("Evidsec")]
[assembly: AssemblyVersion("3.0.0.0")]
[assembly: AssemblyFileVersion("3.0.0.0")]

namespace Ecxp
{
    class App : ApplicationContext
    {
        readonly string Stan, ReadFile;
        readonly HashSet<string> read = new HashSet<string>();
        readonly HashSet<string> seen = new HashSet<string>();
        readonly HashSet<string> alarmyPokazane = new HashSet<string>();
        string stanSeen, spojnosc = "";
        List<Msg> msgs = new List<Msg>();
        List<Msg> widoczne = new List<Msg>();
        List<Msg> popItems = new List<Msg>();
        List<string> alarmy = new List<string>();
        bool popStan, ciszaPokazana;
        volatile int brudne;

        readonly Icon iconOk, iconNew;
        readonly Font fontB = new Font("Segoe UI", 10, FontStyle.Bold);
        readonly Font fontN = new Font("Segoe UI", 10);
        readonly Font fontS = new Font("Segoe UI", 8.5f);
        readonly Font fontH = new Font("Segoe UI", 12, FontStyle.Bold);

        Form form, pop;
        TextBox czeka, txtStan, szukaj;
        RichTextBox det;
        TabControl tabs;
        TabPage tabMsg, tabStan, tabPol;
        ListView lv, lvPol;
        ComboBox cbWidok, cbModul;
        Label popTitle, popText, lblPol;
        ToolStripStatusLabel stInfo, stSyg;
        NotifyIcon tray;
        System.Windows.Forms.Timer timer, blink;
        FileSystemWatcher fsw;
        DateTime ostatniPelny = DateTime.MinValue;
        bool alarm, blinkOn;
        Klient klient;
        DateTime ostatniaProba = DateTime.MinValue;
        ComboBox cbProjekt;
        ToolStripStatusLabel stStos;
        TabPage tabAg;
        ListView lvAg;
        Label lblAg;
        static readonly Color Zolty = Color.FromArgb(255, 222, 0), Zielony = Color.FromArgb(0, 128, 0), Czerwony = Color.FromArgb(200, 0, 0);

        public App()
        {
            Stan = Path.Combine(P.Root, "STAN-MODULOW.md");
            ReadFile = Path.Combine(P.Root, "narzedzia", "przeczytane-wlasciciel.txt");
            foreach (var d in P.Foldery) Directory.CreateDirectory(Path.Combine(P.Root, d));
            foreach (var l in P.Czytaj(ReadFile).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) read.Add(l);

            iconOk = Icon.FromHandle(IconBitmap(false, 32).GetHicon());
            iconNew = Icon.FromHandle(IconBitmap(true, 32).GetHicon());
            BuildForm();
            BuildPop();
            BuildTray();
            var uchwyt = form.Handle;   // uchwyt okna potrzebny do przekazywania zdarzeń stosu do wątku okna
            Polacz();

            msgs = Wczytaj();
            foreach (var m in msgs) seen.Add(m.Plik);
            stanSeen = StanKey();

            fsw = new FileSystemWatcher(P.Root) { IncludeSubdirectories = true, InternalBufferSize = 65536,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName };
            FileSystemEventHandler h = (s, e) => { if (Istotne(e.FullPath)) brudne = 1; };
            fsw.Created += h; fsw.Changed += h; fsw.Deleted += h;
            fsw.Renamed += (s, e) => { if (Istotne(e.FullPath) || Istotne(e.OldFullPath)) brudne = 1; };
            fsw.Error += (s, e) => { brudne = 1; };
            fsw.EnableRaisingEvents = true;

            timer = new System.Windows.Forms.Timer { Interval = 400 };
            timer.Tick += delegate
            {
                if ((klient == null || !klient.Polaczony) && (DateTime.Now - ostatniaProba).TotalSeconds >= 5) Polacz();
                if (brudne == 1 || (DateTime.Now - ostatniPelny).TotalSeconds >= 15) { brudne = 0; Skanuj(); }
            };
            timer.Start();

            if (msgs.Any(m => m.Nowa) || !read.Contains(StanKey())) ShowWindow();
            else ShowPop("ECXP-MAILBOX działa", "Konsola połączona ze stosem ECXP. Brak nowych wiadomości. Ikona ECXP jest przy zegarku.", new List<Msg>(), false);
            UpdateTray();
        }

        // ---------- połączenie ze stosem ECXP ----------
        void Polacz()
        {
            ostatniaProba = DateTime.Now;
            try
            {
                if (klient != null) klient.Dispose();
                klient = Klient.Polacz(P.Wlasciciel, "konsola", true);
                klient.Dane += d => { try { form.BeginInvoke((Action)(() => DaneStosu(d))); } catch { } };
                klient.Rozlaczono += () => { try { form.BeginInvoke((Action)StanStosu); } catch { } };
            }
            catch (Exception ex)
            {
                klient = null;
                if (stStos != null) { stStos.Text = "⚠ ECXP: stos niedostępny – " + ex.Message; stStos.ForeColor = Czerwony; }
                return;
            }
            StanStosu();
        }

        void StanStosu()
        {
            bool ok = klient != null && klient.Polaczony;
            stStos.Text = ok ? "ECXP: połączono ze stosem ecxpd ✓" : "⚠ ECXP: brak połączenia ze stosem";
            stStos.ForeColor = ok ? Zielony : Czerwony;
        }

        object Stos(string op, Dictionary<string, object> a)
        {
            if (klient == null || !klient.Polaczony) Polacz();
            if (klient == null) throw new EcxpException("Brak połączenia ze stosem ECXP (ecxpd).");
            return klient.Wywolaj(op, a);
        }

        void DaneStosu(Dictionary<string, object> d)
        {
            if (P.S(d, "zdarzenie") == "alarm")
            {
                var t = P.Nazwa(P.S(d, "do")) + " · " + P.S(d, "temat") + ": " + P.S(d, "opis");
                alarmy.Add(t);
                if (alarmyPokazane.Add(P.S(d, "id"))) ShowPop("ECXP: problem z doręczeniem", t, new List<Msg>(), false);
            }
            brudne = 1;
        }

        static bool Istotne(string p)
        {
            if (p == null || p.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) return false;
            var l = p.ToLowerInvariant();
            if (l.Contains("\\narzedzia\\")) return false;
            if (l.Contains("\\dziennik\\") || l.Contains("\\stan\\seq\\")) return false;   // własne zapisy – sprawdza je pełny skan
            return true;
        }

        // ---------- dane ----------
        string StanKey() { return File.Exists(Stan) ? "STAN|" + File.GetLastWriteTime(Stan).ToString("o") : ""; }

        void SaveRead()
        {
            try
            {
                var tmp = ReadFile + ".tmp";
                File.WriteAllLines(tmp, read.ToArray(), new UTF8Encoding(false));
                if (File.Exists(ReadFile)) File.Replace(tmp, ReadFile, null); else File.Move(tmp, ReadFile);
            }
            catch { }
        }

        List<Msg> Wczytaj()
        {
            var list = P.Wszystkie(false);
            P.Powiaz(list);
            var stany = P.Stany(out spojnosc);
            foreach (var m in list)
            {
                Stan s; if (stany.TryGetValue(m.Id, out s)) m.Dor = s;
                m.Nowa = !read.Contains(m.Plik) && m.Od != P.Wlasciciel;
            }
            return list;
        }

        string CzekaNaCiebie()
        {
            var m = Regex.Match(P.Czytaj(Stan), @"^## Czeka na Ciebie\s*(.*?)(?=^---|^## )", RegexOptions.Multiline | RegexOptions.Singleline);
            var s = m.Success ? m.Groups[1].Value.Trim() : "(brak sekcji „Czeka na Ciebie”)";
            if (alarmy.Count > 0) s = string.Join("\n", alarmy.Select(a => "⚠ " + a)) + "\n" + s;
            return s;
        }

        static string Crlf(string s) { return Regex.Replace(s ?? "", "\r?\n", "\r\n"); }

        static Color Kolor(Msg m)
        {
            if (m.Blad != null) return Color.FromArgb(170, 0, 0);
            switch (P.Rodzaj(m))
            {
                case "Pytanie": case "Zadanie": case "Blokada": return Color.FromArgb(200, 0, 0);
                case "Gotowe": case "Meldunek": return Color.FromArgb(0, 128, 0);
                case "Recenzja": return Color.FromArgb(128, 0, 170);
                default: return Color.FromArgb(0, 80, 200);
            }
        }

        // Ile czeka wiadomość: na odbiór (w kolejce) albo na odpowiedź (pytanie/zadanie bez odpowiedzi).
        static TimeSpan? Czeka(Msg m, DateTime teraz)
        {
            if (m.Blad != null || m.Katalog == "do-wlasciciela") return null;
            if (m.Katalog == "do-codex" || m.Katalog == "do-claude") return teraz - m.Czas;
            if (m.WymagaOdp && !m.Odpowiedziano) return teraz - m.Czas;
            return null;
        }

        static string Doreczenie(Msg m, DateTime teraz)
        {
            if (m.Blad != null) return "USZKODZONA: " + m.Blad;
            if (m.Dor != null)
            {
                var s = P.Opis(m.Dor, teraz);
                if (m.WymagaOdp && m.Dor.Potwierdzona.HasValue) s += m.Odpowiedziano ? ", odpowiedziano" : ", bez odpowiedzi";
                return s;
            }
            if (m.Katalog == "do-wlasciciela") return "dla Ciebie";
            if (m.Katalog != "przeczytane") return "czeka u adresata od " + P.Wiek(teraz - m.Czas);
            var r = "odebrana";
            if (m.WymagaOdp) r += m.Odpowiedziano ? ", odpowiedziano" : ", bez odpowiedzi od " + P.Wiek(teraz - m.Czas);
            return r;
        }

        static string Zdanie(Msg m)
        {
            var t = (m.Uwagi ?? "").Trim();
            var pierwsza = t.Split('\n')[0].Trim();
            if (pierwsza.Length > 110) pierwsza = pierwsza.Substring(0, 107) + "…";
            return P.Rodzaj(m) + ": " + P.Baza(m.Temat) + (pierwsza.Length > 0 ? " – " + pierwsza : "");
        }

        // ---------- ikona ----------
        // Jaskrawa ikona: żółte tło ze znakiem ECXP (dwa węzły – Codex i Claude – połączone przez karmazynowy stos).
        // Alarm: czerwone tło, białe węzły (miga na zmianę z żółtym).
        public static Bitmap IconBitmap(bool alert, int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);
                var bg = alert ? Color.FromArgb(255, 30, 30) : Color.FromArgb(255, 222, 0);
                using (var b = new SolidBrush(bg)) g.FillRectangle(b, 0, 0, size, size);
                using (var pen = new Pen(alert ? Color.White : Color.Black, Math.Max(1, size / 16))) g.DrawRectangle(pen, 0, 0, size - 1, size - 1);
                float k = size / 32f;
                var wezel = alert ? Color.White : Color.FromArgb(35, 37, 40);
                var srodek = alert ? Color.FromArgb(255, 222, 0) : Color.FromArgb(155, 25, 42);
                PointF a = new PointF(7 * k, 21 * k), c = new PointF(16 * k, 11 * k), b2 = new PointF(25 * k, 21 * k);
                using (var pen = new Pen(wezel, 2.6f * k)) { g.DrawLine(pen, a, c); g.DrawLine(pen, c, b2); }
                using (var bw = new SolidBrush(wezel))
                {
                    g.FillEllipse(bw, a.X - 3.6f * k, a.Y - 3.6f * k, 7.2f * k, 7.2f * k);
                    g.FillEllipse(bw, b2.X - 3.6f * k, b2.Y - 3.6f * k, 7.2f * k, 7.2f * k);
                }
                using (var bs = new SolidBrush(srodek)) g.FillEllipse(bs, c.X - 4.8f * k, c.Y - 4.8f * k, 9.6f * k, 9.6f * k);
            }
            return bmp;
        }

        // ---------- główne okno ----------
        Button Btn(string t)
        {
            return new Button { Text = t, AutoSize = true, Height = 32, Font = fontN, Margin = new Padding(3, 4, 3, 4) };
        }

        void BuildForm()
        {
            form = new Form { Text = "ECXP-MAILBOX – konsola operatora ECXP", Size = new Size(1240, 820), MinimumSize = new Size(900, 560),
                StartPosition = FormStartPosition.CenterScreen, Font = fontN, Icon = iconOk, KeyPreview = true };

            // --- nagłówek: logo ECXP, projekt, nowe zadanie ---
            var head = new Panel { Dock = DockStyle.Top, Height = 64, BackColor = Color.White };
            var logo = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Location = new Point(10, 6), Size = new Size(172, 52) };
            var lp = Path.Combine(P.Root, "logo", "ecxp-logo-light.png");
            if (!File.Exists(lp)) lp = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "..", "logo", "ecxp-logo-light.png");
            if (File.Exists(lp)) logo.Image = Image.FromStream(new MemoryStream(File.ReadAllBytes(lp)));
            var lblProj = new Label { Text = "Projekt:", AutoSize = true, Location = new Point(200, 22) };
            cbProjekt = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(262, 18), Width = 180 };
            cbProjekt.Items.Add("Wszystkie projekty");
            foreach (var pr in P.Projekty()) cbProjekt.Items.Add(P.S(pr, "nazwa"));
            cbProjekt.SelectedIndex = cbProjekt.Items.Count > 1 ? 1 : 0;
            var bNoweH = Btn("＋  Nowe zadanie / moduł…");
            bNoweH.Font = fontB; bNoweH.BackColor = Zolty; bNoweH.FlatStyle = FlatStyle.Flat; bNoweH.Location = new Point(460, 14);
            var lblMotto = new Label { Text = "Evidsec Context eXchange Protocol · para 1:1 ChatGPT/Codex ↔ Claude", AutoSize = true, ForeColor = Color.DimGray, Location = new Point(700, 23) };
            head.Controls.AddRange(new Control[] { logo, lblProj, cbProjekt, bNoweH, lblMotto });

            var lblCzeka = new Label { Text = "Czeka na Ciebie", Dock = DockStyle.Top, Height = 24, Font = fontB };
            czeka = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Top, Height = 90,
                BackColor = Color.FromArgb(255, 248, 220), ScrollBars = ScrollBars.Vertical };

            tabs = new TabControl { Dock = DockStyle.Fill };

            // --- Skrzynka ---
            tabMsg = new TabPage("Rozmowy");
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, WrapContents = false };
            var bPisz = Btn("✉  Napisz…");
            bPisz.Font = fontB; bPisz.BackColor = Color.FromArgb(255, 222, 0); bPisz.FlatStyle = FlatStyle.Flat;
            var bOdp = Btn("↩  Odpowiedz…");
            bOdp.Font = fontB;
            cbWidok = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210, Margin = new Padding(14, 8, 3, 3) };
            cbWidok.Items.AddRange(new object[] { "Wszystkie rozmowy", "Do mnie i ode mnie", "Czeka na odpowiedź", "Pytania, zadania, blokady", "Nowe dla mnie", "Problemy z doręczeniem" });
            cbWidok.SelectedIndex = 0;
            cbModul = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Margin = new Padding(3, 8, 3, 3) };
            cbModul.Items.Add("Wszystkie moduły"); cbModul.SelectedIndex = 0;
            var lblSz = new Label { Text = "Szukaj:", AutoSize = true, Margin = new Padding(10, 12, 0, 0) };
            szukaj = new TextBox { Width = 200, Margin = new Padding(3, 8, 3, 3) };
            top.Controls.AddRange(new Control[] { bPisz, bOdp, cbWidok, cbModul, lblSz, szukaj });

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            lv = new ListView { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, HideSelection = false, ShowGroups = true };
            foreach (var c in new object[][] { new object[] { "Czas", 100 }, new object[] { "", 26 }, new object[] { "Od → Do", 230 },
                new object[] { "Moduł", 95 }, new object[] { "Treść", 420 }, new object[] { "Doręczenie", 300 } })
                lv.Columns.Add((string)c[0], (int)c[1]);
            det = new RichTextBox { ReadOnly = true, Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.None, DetectUrls = false };
            split.Panel1.Controls.Add(lv);
            split.Panel2.Controls.Add(det);
            tabMsg.Controls.Add(split);
            tabMsg.Controls.Add(top);

            // --- Agenci (pary 1:1, role, znaczniki) ---
            tabAg = new TabPage("Agenci");
            var agTop = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, WrapContents = false };
            var bNowe = Btn("＋  Nowe zadanie / moduł…");
            bNowe.Font = fontB; bNowe.BackColor = Zolty; bNowe.FlatStyle = FlatStyle.Flat;
            var bRola = Btn("Nadaj role / przypisz…");
            var bPakiet = Btn("Pakiety startowe…");
            var bNazwy = Btn("Nazwy sesji wg ECXP…");
            agTop.Controls.AddRange(new Control[] { bNowe, bRola, bPakiet, bNazwy });
            lblAg = new Label { Dock = DockStyle.Top, Height = 44, Padding = new Padding(6), ForeColor = Color.DimGray,
                Text = "Zasada 1:1: w każdym module pracuje para – sesja ChatGPT/Codex i sesja Claude – nad tym samym zadaniem i jedną gałęzią. " +
                       "Sesje w parze noszą wspólny znacznik " + "🔗ECXP" + " przed nazwą." };
            lvAg = new ListView { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, HideSelection = false };
            foreach (var c in new object[][] { new object[] { "Projekt", 95 }, new object[] { "Moduł", 110 }, new object[] { "ChatGPT/Codex", 200 },
                new object[] { "Claude", 200 }, new object[] { "Para", 90 }, new object[] { "Role", 260 }, new object[] { "Nazwa sesji (obie strony)", 260 } })
                lvAg.Columns.Add((string)c[0], (int)c[1]);
            tabAg.Controls.Add(lvAg);
            tabAg.Controls.Add(lblAg);
            tabAg.Controls.Add(agTop);
            bNowe.Click += delegate { NoweZadanie(); };
            bNoweH.Click += delegate { NoweZadanie(); };
            bRola.Click += delegate { NadajRole(); };
            bPakiet.Click += delegate { PakietyStartowe(); };
            bNazwy.Click += delegate { NazwySesji(); };
            cbProjekt.SelectedIndexChanged += delegate { RefreshView(); };

            // --- Połączenia ---
            tabPol = new TabPage("Połączenia");
            lblPol = new Label { Dock = DockStyle.Top, Height = 48, Padding = new Padding(6), Font = fontN };
            lvPol = new ListView { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill };
            foreach (var c in new object[][] { new object[] { "Węzeł", 230 }, new object[] { "Ostatni kontakt", 170 }, new object[] { "Ostatnia czynność", 140 },
                new object[] { "Czeka na niego", 120 }, new object[] { "Niepotwierdzone od niego", 180 }, new object[] { "Ocena", 300 } })
                lvPol.Columns.Add((string)c[0], (int)c[1]);
            tabPol.Controls.Add(lvPol);
            tabPol.Controls.Add(lblPol);

            // --- Stan modułów ---
            tabStan = new TabPage("Stan modułów");
            txtStan = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Both,
                WordWrap = false, Font = new Font("Consolas", 10), BackColor = Color.White };
            tabStan.Controls.Add(txtStan);

            tabs.TabPages.Add(tabMsg);
            tabs.TabPages.Add(tabAg);
            tabs.TabPages.Add(tabPol);
            tabs.TabPages.Add(tabStan);

            var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42 };
            var bRead = Btn("Przeczytane");
            var bAll = Btn("Wszystkie przeczytane");
            var bClean = Btn("Wyczyść");
            var cbAge = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170, Font = fontN, Margin = new Padding(3, 8, 3, 3) };
            cbAge.Items.AddRange(new object[] { "wszystkie przeczytane", "starsze niż 1 dzień", "starsze niż 3 dni", "starsze niż 7 dni" });
            cbAge.SelectedIndex = 0;
            var bRef = Btn("Odśwież");
            var bDir = Btn("Folder skrzynki");
            var bSpis = Btn("Spis gałęzi");
            bar.Controls.AddRange(new Control[] { bRead, bAll, bClean, cbAge, bRef, bDir, bSpis });

            var strip = new StatusStrip { SizingGrip = true };
            stInfo = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
            stSyg = new ToolStripStatusLabel { Font = fontB };
            stStos = new ToolStripStatusLabel { Font = fontB, Text = "ECXP: łączenie…" };
            strip.Items.AddRange(new ToolStripItem[] { stInfo, stStos, stSyg });

            form.Controls.Add(tabs);
            form.Controls.Add(czeka);
            form.Controls.Add(lblCzeka);
            form.Controls.Add(head);
            form.Controls.Add(bar);
            form.Controls.Add(strip);
            form.Shown += delegate { split.SplitterDistance = (int)(split.Height * 0.5); };

            lv.SelectedIndexChanged += delegate { var m = Wybrana(); if (m != null) PokazWatek(m); };
            lv.DoubleClick += delegate { var m = Wybrana(); if (m != null) MarkRead(new List<Msg> { m }); };
            bPisz.Click += delegate { Pisz(null); };
            bOdp.Click += delegate
            {
                var m = Wybrana();
                if (m == null) { MessageBox.Show(form, "Najpierw zaznacz wiadomość, na którą chcesz odpowiedzieć.", "Odpowiedz"); return; }
                Pisz(m);
            };
            cbWidok.SelectedIndexChanged += delegate { RefreshView(); };
            cbModul.SelectedIndexChanged += delegate { RefreshView(); };
            szukaj.TextChanged += delegate { RefreshView(); };
            bRead.Click += delegate
            {
                if (tabs.SelectedTab == tabStan) { read.Add(StanKey()); SaveRead(); RefreshView(); return; }
                var sel = new List<Msg>();
                foreach (ListViewItem it in lv.SelectedItems) sel.Add((Msg)it.Tag);
                if (sel.Count > 0) MarkRead(sel);
            };
            bAll.Click += delegate { read.Add(StanKey()); MarkRead(msgs); };
            bClean.Click += delegate { Clean(new[] { 0, 1, 3, 7 }[cbAge.SelectedIndex]); };
            bRef.Click += delegate { Skanuj(); };
            bDir.Click += delegate { OpenPath(P.Root); };
            bSpis.Click += delegate
            {
                var s = new DirectoryInfo(P.Root).GetFiles("SPIS-GALEZI-*.md").OrderByDescending(f => f.Name).FirstOrDefault();
                if (s != null) System.Diagnostics.Process.Start("notepad.exe", "\"" + s.FullName + "\"");
            };
            form.KeyDown += (s, e) =>
            {
                if (e.Control && e.KeyCode == Keys.N) { e.Handled = true; Pisz(null); }
                else if (e.Control && e.KeyCode == Keys.R) { e.Handled = true; var m = Wybrana(); if (m != null) Pisz(m); }
                else if (e.KeyCode == Keys.F5) { e.Handled = true; Skanuj(); }
            };
            form.FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; form.Hide(); } };
        }

        Msg Wybrana() { return lv.SelectedItems.Count > 0 ? (Msg)lv.SelectedItems[0].Tag : null; }

        static void OpenPath(string p) { System.Diagnostics.Process.Start("explorer.exe", "\"" + p + "\""); }

        void Dopisz(string t, Font f, Color c)
        {
            det.SelectionStart = det.TextLength; det.SelectionLength = 0;
            det.SelectionFont = f; det.SelectionColor = c;
            det.AppendText(t);
        }

        void PokazWatek(Msg sel)
        {
            det.SuspendLayout();
            det.Clear();
            var w = msgs.Where(x => x.Watek == sel.Watek).OrderBy(x => x.Czas).ToList();
            var teraz = DateTime.Now;
            Dopisz("Rozmowa: " + P.Baza(sel.Temat) + "   ·   wiadomości: " + w.Count + "\n\n", fontH, Color.Black);
            int poz = 0;
            foreach (var m in w)
            {
                if (m == sel) poz = det.TextLength;
                Dopisz((m == sel ? "▶ " : "") + m.Czas.ToString("dd.MM.yyyy HH:mm") + "   " + P.Nazwa(m.Od) + "  →  " + P.Nazwa(m.Do) +
                    "   ·   " + P.Rodzaj(m) + (m.Pilne ? " · PILNE" : "") + (m.Modul.Length > 0 ? "   ·   moduł " + m.Modul : "") + "\n", fontB, Kolor(m));
                if (m.Blad != null) Dopisz("Nie da się odczytać wiadomości: " + m.Blad + "\n", fontN, Color.FromArgb(170, 0, 0));
                if (m.Uwagi.Trim().Length > 0) Dopisz(m.Uwagi.Trim() + "\n", fontN, Color.Black);
                if (m.Kontrole.Count > 0)
                {
                    Dopisz("Kontrole:\n", fontB, Color.Black);
                    foreach (var k in m.Kontrole)
                        Dopisz("   • " + k + "\n", fontN, k.StartsWith("[PASS") ? Color.FromArgb(0, 128, 0) : (k.StartsWith("[FAIL") || k.StartsWith("[BLOCKED")) ? Color.FromArgb(200, 0, 0) : Color.Black);
                }
                if (m.Pliki.Count > 0) { Dopisz("Pliki:\n", fontB, Color.Black); foreach (var p in m.Pliki) Dopisz("   • " + p + "\n", fontN, Color.Black); }
                if (m.Commit.Length > 0) Dopisz("Commit: " + m.Commit + "\n", fontN, Color.DimGray);
                if (m.Ograniczenia.Length > 0) Dopisz("Ograniczenia: " + m.Ograniczenia + "\n", fontN, Color.DimGray);
                Dopisz("Doręczenie: " + Doreczenie(m, teraz) + "\n", fontS, Color.DimGray);
                Dopisz("id " + m.Id + (m.Seq > 0 ? "  ·  seq " + m.Seq : "") + "  ·  " + m.Sciezka + "\n\n", fontS, Color.Gray);
            }
            det.SelectionStart = poz; det.SelectionLength = 0;
            det.ScrollToCaret();
            det.ResumeLayout();
        }

        bool Pasuje(Msg m)
        {
            var pr = cbProjekt != null && cbProjekt.SelectedIndex > 0 ? (string)cbProjekt.SelectedItem : null;
            if (pr != null && m.Projekt != pr) return false;
            var mod = cbModul.SelectedIndex > 0 ? (string)cbModul.SelectedItem : null;
            if (mod != null && m.Modul != mod) return false;
            var q = szukaj.Text.Trim();
            if (q.Length > 0)
            {
                var hay = (m.Od + " " + m.Do + " " + m.Modul + " " + m.Temat + " " + m.Uwagi + " " + m.Commit + " " + P.Nazwa(m.Od) + " " + P.Nazwa(m.Do));
                if (hay.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) < 0) return false;
            }
            return true;
        }

        bool PasujeWatek(List<Msg> w, DateTime teraz)
        {
            switch (cbWidok.SelectedIndex)
            {
                case 1: return w.Any(m => m.Od == P.Wlasciciel || m.Do == P.Wlasciciel);
                case 2: return w.Any(m => Czeka(m, teraz).HasValue);
                case 3: return w.Any(m => m.WymagaOdp);
                case 4: return w.Any(m => m.Nowa);
                case 5: return w.Any(m => m.Blad != null || (m.Dor != null && (m.Dor.Eskalacja || m.Dor.Ponowienia > 0 || m.Dor.Zaginiona)));
            }
            return true;
        }

        void RefreshView()
        {
            var teraz = DateTime.Now;
            txtStan.Text = Crlf(P.Czytaj(Stan));
            czeka.Text = Crlf(CzekaNaCiebie());
            tabStan.Text = !read.Contains(StanKey()) ? "Stan modułów (zmieniony)" : "Stan modułów";

            // lista modułów do filtra
            var moduly = msgs.Select(m => m.Modul).Where(m => m.Length > 0).Distinct().OrderBy(m => m).ToList();
            var akt = cbModul.SelectedIndex > 0 ? (string)cbModul.SelectedItem : null;
            if (cbModul.Items.Count - 1 != moduly.Count || moduly.Where((m, i) => (string)cbModul.Items[i + 1] != m).Any())
            {
                cbModul.BeginUpdate();
                cbModul.Items.Clear(); cbModul.Items.Add("Wszystkie moduły");
                foreach (var m in moduly) cbModul.Items.Add(m);
                cbModul.SelectedIndex = akt != null && moduly.Contains(akt) ? moduly.IndexOf(akt) + 1 : 0;
                cbModul.EndUpdate();
            }

            var wybrana = Wybrana();
            string selPlik = wybrana != null ? wybrana.Plik : null;
            var watki = msgs.GroupBy(m => m.Watek).Select(g => g.OrderBy(m => m.Czas).ToList())
                .Where(w => PasujeWatek(w, teraz) && w.Any(Pasuje))
                .OrderByDescending(w => w.Any(m => m.Nowa)).ThenByDescending(w => w.Max(m => m.Czas)).ToList();

            lv.BeginUpdate();
            lv.Items.Clear();
            lv.Groups.Clear();
            widoczne = new List<Msg>();
            foreach (var w in watki)
            {
                var czekaj = w.Select(m => Czeka(m, teraz)).Where(t => t.HasValue).Select(t => t.Value).DefaultIfEmpty(TimeSpan.Zero).Max();
                var stanW = w.Any(m => m.Blad != null) ? "⚠ uszkodzona wiadomość"
                    : w.Any(m => m.Dor != null && (m.Dor.Eskalacja || m.Dor.Zaginiona)) ? "⚠ brak potwierdzenia – eskalacja"
                    : czekaj > TimeSpan.Zero ? "⏳ czeka od " + P.Wiek(czekaj)
                    : "✓ zamknięta";
                var g = new ListViewGroup(w[0].Watek, P.Baza(w[0].Temat) + "   ·   " + string.Join(", ", w.Select(m => m.Modul).Where(x => x.Length > 0).Distinct()) +
                    "   ·   " + w.Count + " wiad.   ·   " + stanW + (w.Any(m => m.Nowa) ? "   ·   NOWE" : ""));
                lv.Groups.Add(g);
                foreach (var m in w)
                {
                    var it = new ListViewItem(m.Czas.ToString("dd.MM HH:mm"), g) { Tag = m };
                    it.SubItems.Add(m.Nowa ? "●" : "");
                    it.SubItems.Add(P.Nazwa(m.Od) + "  →  " + P.Nazwa(m.Do));
                    it.SubItems.Add(m.Modul);
                    it.SubItems.Add(Zdanie(m));
                    it.SubItems.Add(Doreczenie(m, teraz));
                    it.ForeColor = Kolor(m);
                    if (m.Nowa) it.Font = fontB;
                    var c = Czeka(m, teraz);
                    if (m.Blad != null || (m.Dor != null && (m.Dor.Eskalacja || m.Dor.Zaginiona))) it.BackColor = Color.FromArgb(255, 205, 205);
                    else if (c.HasValue && c.Value.TotalHours >= 6) it.BackColor = Color.FromArgb(255, 220, 220);
                    else if (c.HasValue && c.Value.TotalHours >= 1) it.BackColor = Color.FromArgb(255, 242, 200);
                    lv.Items.Add(it);
                    widoczne.Add(m);
                    if (m.Plik == selPlik) it.Selected = true;
                }
            }
            lv.EndUpdate();

            RefreshPolaczenia(teraz);
            RefreshAgenci();
            int nowe = msgs.Count(m => m.Nowa);
            int wKolejce = msgs.Count(m => m.Katalog == "do-codex" || m.Katalog == "do-claude");
            int bezOdp = msgs.Count(m => m.WymagaOdp && !m.Odpowiedziano);
            stInfo.Text = string.Format("Nowe dla Ciebie: {0}   ·   w kolejkach adresatów: {1}   ·   pytań bez odpowiedzi: {2}   ·   dziennik: {3}   ·   {4:HH:mm:ss}",
                nowe, wKolejce, bezOdp, spojnosc, teraz);
            stInfo.ForeColor = spojnosc.StartsWith("NARUSZONY") ? Color.FromArgb(200, 0, 0) : SystemColors.ControlText;
            UpdateTray();
        }

        void RefreshPolaczenia(DateTime teraz)
        {
            var wezly = P.Wezly().ToDictionary(w => w.Nazwa, w => w);
            var nazwy = new SortedSet<string>(wezly.Keys);
            foreach (var m in msgs)
            {
                if (P.PoprawnyNadawca(m.Od)) nazwy.Add(m.Od);
                if (P.PoprawnyNadawca(m.Do)) nazwy.Add(m.Do);
            }
            nazwy.Add(P.Koordynacja);
            nazwy.Remove("codex-core");
            lvPol.BeginUpdate();
            lvPol.Items.Clear();
            foreach (var n in nazwy)
            {
                Wezel w; wezly.TryGetValue(n, out w);
                var ostatniaWiad = msgs.Where(m => m.Od == n).Select(m => (DateTime?)m.Czas).DefaultIfEmpty(null).Max();
                DateTime? kontakt = w != null ? (DateTime?)w.Ostatnio : null;
                if (ostatniaWiad.HasValue && (!kontakt.HasValue || ostatniaWiad > kontakt)) kontakt = ostatniaWiad;
                int czekaNa = msgs.Count(m => (m.Katalog == "do-codex" || m.Katalog == "do-claude") && P.DoMnie(n, m.Do));
                int niepotw = msgs.Count(m => m.Od == n && m.Dor != null && !m.Dor.Potwierdzona.HasValue);
                string ocena;
                if (n == P.Koordynacja)
                {
                    if (w == null) ocena = "brak sygnału ECXP – automatyzacja nie używa jeszcze protokołu";
                    else if ((teraz - w.Ostatnio).TotalMinutes > 25) ocena = "⚠ milczy od " + P.Wiek(teraz - w.Ostatnio) + " (powinna co 10 min)";
                    else ocena = "✓ działa (automatyzacja co 10 min)";
                }
                else if (!kontakt.HasValue) ocena = "jeszcze się nie odezwał";
                else if (czekaNa > 0) ocena = "ma " + czekaNa + " nieodebranych – czyta między etapami";
                else ocena = "✓ bez zaległości";
                var it = new ListViewItem(P.Nazwa(n) + "  (" + n + ")");
                it.SubItems.Add(kontakt.HasValue ? kontakt.Value.ToString("dd.MM HH:mm") + "  (" + P.Wiek(teraz - kontakt.Value) + " temu)" : "—");
                it.SubItems.Add(w != null ? w.Akcja : "");
                it.SubItems.Add(czekaNa.ToString());
                it.SubItems.Add(niepotw.ToString());
                it.SubItems.Add(ocena);
                if (ocena.StartsWith("⚠")) it.ForeColor = Color.FromArgb(200, 0, 0);
                else if (ocena.StartsWith("✓")) it.ForeColor = Color.FromArgb(0, 128, 0);
                lvPol.Items.Add(it);
            }
            lvPol.EndUpdate();
            lblPol.Text = "Węzły ECXP. Każde odebranie, wysłanie i potwierdzenie przez narzędzia ECXP zostawia sygnał życia.\r\n" +
                "Dziennik zdarzeń: " + spojnosc + "   (" + Path.Combine(P.Root, "dziennik", "dziennik.jsonl") + ")";

            // pasek: sygnał koordynacji
            Wezel k; wezly.TryGetValue(P.Koordynacja, out k);
            if (k == null) { stSyg.Text = "Codex · koordynacja: brak sygnału ECXP"; stSyg.ForeColor = Color.DimGray; }
            else
            {
                var wiek = teraz - k.Ostatnio;
                if (wiek.TotalMinutes > 25)
                {
                    stSyg.Text = "⚠ Codex · koordynacja milczy od " + P.Wiek(wiek); stSyg.ForeColor = Color.FromArgb(200, 0, 0);
                    if (!ciszaPokazana)
                    {
                        ciszaPokazana = true;
                        ShowPop("Codex · koordynacja milczy", "Ostatni sygnał " + k.Ostatnio.ToString("HH:mm") + " (" + P.Wiek(wiek) + " temu). Automatyzacja co 10 min mogła stanąć – zajrzyj do czatu ELV3-CORE w Codexie.", new List<Msg>(), false);
                    }
                }
                else { stSyg.Text = "Codex · koordynacja: ✓ " + k.Ostatnio.ToString("HH:mm"); stSyg.ForeColor = Color.FromArgb(0, 128, 0); ciszaPokazana = false; }
            }
        }

        void UpdateTray()
        {
            int nowe = msgs.Count(m => m.Nowa);
            bool stanNowy = !read.Contains(StanKey());
            var t = "ECXP-MAILBOX – nowe: " + nowe + (stanNowy ? ", stan zmieniony" : "") + (alarmy.Count > 0 ? ", alarmy: " + alarmy.Count : "");
            tray.Text = t.Length > 63 ? t.Substring(0, 63) : t;
            alarm = nowe > 0 || stanNowy || alarmy.Count > 0;
            if (blink == null)
            {
                blink = new System.Windows.Forms.Timer { Interval = 600 };
                blink.Tick += delegate { blinkOn = !blinkOn; tray.Icon = blinkOn ? iconNew : iconOk; };
            }
            if (alarm && !blink.Enabled) blink.Start();
            if (!alarm) { blink.Stop(); tray.Icon = iconOk; }
        }

        void MarkRead(List<Msg> items)
        {
            foreach (var m in items)
            {
                read.Add(m.Plik);
                // wiadomość do właściciela: przeczytanie = potwierdzenie protokołu (przeniesienie do przeczytane)
                if (m.Katalog == "do-wlasciciela" && m.Blad == null && File.Exists(m.Sciezka))
                {
                    try { Stos("potwierdz", P.D("id", m.Id, "wynik", "obsluzona", "uwagi", "przeczytane przez operatora")); }
                    catch (Exception ex) { stInfo.Text = "Nie udało się potwierdzić " + m.Plik + ": " + ex.Message; }
                }
            }
            SaveRead();
            Skanuj();
        }

        // ---------- pisanie ----------
        void Pisz(Msg odp)
        {
            // Adresaci, projekty i moduły pochodzą z konfiguracji projektów (projekty\*\projekt.json), kart węzłów i historii – nic nie jest wpisane na sztywno.
            var projekty = P.Projekty();
            var adresaci = new List<string>();
            var projAkt = cbProjekt.SelectedIndex > 0 ? (string)cbProjekt.SelectedItem : "";
            foreach (var pa in Siec.Opis(projAkt)["pary"] as List<object> ?? new List<object>())
            {
                var d = (Dictionary<string, object>)pa;
                var mod = P.S(d, "modul");
                adresaci.Add("para:" + mod + " — PARA 1:1 " + mod + ": " + (P.S(d, "codex").Length > 0 ? P.S(d, "codex") : "brak Codexa") + " + " + (P.S(d, "claude").Length > 0 ? P.S(d, "claude") : "brak Claude") + " [" + P.S(d, "projekt") + "]");
            }
            foreach (var pa in Siec.Opis(projAkt)["pary"] as List<object> ?? new List<object>())
            {
                var mod = P.S((Dictionary<string, object>)pa, "modul");
                adresaci.Add("codex/" + mod + " — tylko strona ChatGPT/Codex modułu " + mod + " (Claude dostanie kopię – zasada 1:1)");
                adresaci.Add("claude/" + mod + " — tylko strona Claude modułu " + mod + " (Codex dostanie kopię – zasada 1:1)");
            }
            adresaci.Add(P.Koordynacja + " — Codex · koordynacja: odbiera co 10 min, odpowiada albo przekazuje sprawę właściwej sesji");
            foreach (var pr in projekty)
                foreach (var w in P.Lista(pr, "wezly"))
                {
                    var n = P.S(w, "nazwa");
                    if (P.PoprawnyNadawca(n) && !adresaci.Any(a => a.StartsWith(n + " ")))
                        adresaci.Add(n + " — " + (P.S(w, "opis").Length > 0 ? P.S(w, "opis") : P.Nazwa(n)) + " [" + P.S(pr, "nazwa") + "]");
                }
            foreach (var k in P.Karty())
            {
                var n = P.S(k, "nazwa");
                if (P.PoprawnyNadawca(n) && !adresaci.Any(a => a.StartsWith(n + " "))) adresaci.Add(n + " — " + (P.S(k, "opis").Length > 0 ? P.S(k, "opis") : P.Nazwa(n)));
            }
            foreach (var n in msgs.SelectMany(m => new[] { m.Od, m.Do }).Concat(P.Wezly().Select(w => w.Nazwa)).Distinct().OrderBy(x => x))
                if (P.PoprawnyNadawca(n) && n != "codex-core" && !adresaci.Any(a => a.StartsWith(n + " ")))
                    adresaci.Add(n + " — " + P.Nazwa(n) + " (z historii kanału)");
            var nazwyProjektow = projekty.Select(pr => P.S(pr, "nazwa")).Concat(msgs.Select(m => m.Projekt)).Where(x => x.Length > 0).Distinct().OrderBy(x => x).ToList();
            var moduly = new List<string> { "wspolne" };
            foreach (var pr in projekty) foreach (var m in P.L(pr, "moduly")) if (!moduly.Contains(m)) moduly.Add(m);
            foreach (var m in msgs.Select(x => x.Modul).Where(x => x.Length > 0)) if (!moduly.Contains(m)) moduly.Add(m);

            using (var f = new PiszForm(adresaci, nazwyProjektow, moduly, fontN, fontB))
            {
                if (projAkt.Length > 0) f.cbProjekt.Text = projAkt;
                if (odp != null)
                {
                    var komu = odp.Od == P.Wlasciciel ? odp.Do : odp.Od;
                    if (komu == "codex-core" || komu == "codex") komu = P.Koordynacja;
                    if (komu == P.Straznik) komu = P.Koordynacja;
                    f.UstawAdresata(adresaci.FirstOrDefault(a => a.StartsWith(komu + " ")) ?? komu);
                    f.cbModul.Text = odp.Modul.Length > 0 ? odp.Modul : "wspolne";
                    if (odp.Projekt.Length > 0) f.cbProjekt.Text = odp.Projekt;
                    f.tbTemat.Text = P.Baza(odp.Temat);
                    f.OdpNa = odp.Id;
                    f.Text = "Odpowiedz – " + P.Nazwa(komu) + " · " + P.Baza(odp.Temat);
                    f.cbTyp.SelectedIndex = 3;   // odpowiedź
                }
                if (f.ShowDialog(form) != DialogResult.OK) return;
                try
                {
                    var r = (Dictionary<string, object>)Stos("wyslij", P.D("do", f.Adresat, "projekt", f.Projekt, "modul", f.Modul, "typ", f.Typ,
                        "temat", f.tbTemat.Text, "tresc", f.tbTresc.Text, "odpowiedz_na", f.OdpNa, "pilne", f.chPilne.Checked,
                        "ograniczenia", "Wiadomość operatora przez ECXP. Push, merge, USER_ACCEPTED, usuwanie, zmiany rezerwacji i ustawień bezpieczeństwa wymagają zgody operatora w czacie."));
                    foreach (var w in (r["wyslane"] as System.Collections.IEnumerable) ?? new object[0])
                        read.Add(Path.GetFileName(P.S((Dictionary<string, object>)w, "plik")));
                    foreach (var w in (r["kopie_dla_pary"] as System.Collections.IEnumerable) ?? new object[0])
                        read.Add(P.S((Dictionary<string, object>)w, "id") + ".json");
                    SaveRead();
                    Skanuj();
                    var termin = Convert.ToInt32(r["termin_potwierdzenia_min"]);
                    var ileK = ((r["kopie_dla_pary"] as System.Collections.ICollection) ?? new object[0]).Count;
                    stInfo.Text = "Wysłano do " + string.Join(" + ", ((System.Collections.IEnumerable)r["adresaci"]).Cast<object>()) + (ileK > 0 ? " (kopia dla pary: " + ileK + ")" : "") +
                        (termin > 0 ? ", termin potwierdzenia " + termin + " min" : "");
                    ShowPop("Wysłano: " + P.Nazwa(f.Adresat), "Temat " + P.Baza(f.tbTemat.Text.Length > 0 ? f.tbTemat.Text : (string)r["id"]) +
                        (termin > 0 ? ". Jeśli adresat nie potwierdzi w " + termin + " min, kanał ponowi i zaalarmuje." : "."), new List<Msg>(), false);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(form, "Nie wysłano: " + ex.Message, "Napisz", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // ---------- agenci: pary 1:1, role, znaczniki ----------
        const string ZnacznikEcxp = "🔗ECXP";

        string TytulPary(string projekt, string modul)
        {
            var pr = P.Projekty().FirstOrDefault(x => P.S(x, "nazwa") == projekt);
            object pv, mv;
            if (pr != null && pr.TryGetValue("pary", out pv) && pv is Dictionary<string, object> && ((Dictionary<string, object>)pv).TryGetValue(modul, out mv) && mv is Dictionary<string, object>)
            {
                var t = P.S((Dictionary<string, object>)mv, "tytul");
                if (t.Length > 0) return t;
            }
            return ZnacznikEcxp + " " + projekt + " · " + modul;
        }

        void RefreshAgenci()
        {
            if (lvAg == null) return;
            var projAkt = cbProjekt.SelectedIndex > 0 ? (string)cbProjekt.SelectedItem : "";
            Dictionary<string, object> o;
            try { o = Siec.Opis(projAkt); } catch { return; }
            var trasy = Siec.Trasy().Where(x => x.Aktywna).ToList();
            lvAg.BeginUpdate();
            lvAg.Items.Clear();
            foreach (Dictionary<string, object> d in (List<object>)o["pary"])
            {
                string pr = P.S(d, "projekt"), mod = P.S(d, "modul"), cx = P.S(d, "codex"), cl = P.S(d, "claude");
                var role = new List<string>();
                var rcx = trasy.FirstOrDefault(x => x.Projekt == pr && x.Modul == mod && x.Nazwa == cx);
                var rcl = trasy.FirstOrDefault(x => x.Projekt == pr && x.Modul == mod && x.Nazwa == cl);
                if (rcx != null && rcx.Role.Count > 0) role.Add("Codex: " + string.Join(", ", rcx.Role));
                if (rcl != null && rcl.Role.Count > 0) role.Add("Claude: " + string.Join(", ", rcl.Role));
                bool pelna = cx.Length > 0 && cl.Length > 0;
                var it = new ListViewItem(pr);
                it.SubItems.Add(mod);
                it.SubItems.Add(cx.Length > 0 ? ZnacznikEcxp + "  " + cx : "— brak —");
                it.SubItems.Add(cl.Length > 0 ? ZnacznikEcxp + "  " + cl : "— brak —");
                it.SubItems.Add(pelna ? "✓ para 1:1" : "⚠ niepełna");
                it.SubItems.Add(string.Join("; ", role));
                it.SubItems.Add(TytulPary(pr, mod));
                it.ForeColor = pelna ? Zielony : Czerwony;
                it.Tag = d;
                lvAg.Items.Add(it);
            }
            lvAg.EndUpdate();
            tabAg.Text = "Agenci (" + lvAg.Items.Count + ")";
        }

        Dictionary<string, object> WybranaPara()
        {
            return lvAg.SelectedItems.Count > 0 ? (Dictionary<string, object>)lvAg.SelectedItems[0].Tag : null;
        }

        void NoweZadanie()
        {
            var projekty = P.Projekty().Select(x => P.S(x, "nazwa")).ToList();
            using (var f = new Formularz("Nowe zadanie / moduł – para 1:1 ECXP", fontN, fontB))
            {
                var cbP = f.Combo("Projekt:", projekty, cbProjekt.SelectedIndex > 0 ? (string)cbProjekt.SelectedItem : (projekty.Count > 0 ? projekty[0] : "evidlockv3"));
                var tbM = f.Tekst("Moduł / zadanie (krótka nazwa):", "");
                var tbT = f.Tekst("Nazwa sesji po obu stronach:", "");
                var rCx = f.Role("Role sesji ChatGPT/Codex:", new[] { "programista" });
                var rCl = f.Role("Role sesji Claude:", new[] { "recenzent", "tester" });
                var tbO = f.Wielowiersz("Zadanie (trafi do obu stron pary):", "");
                f.Uwaga("ECXP zarejestruje parę o tych samych nazwach: codex-<moduł> i claude-<moduł>, ze znacznikiem " + ZnacznikEcxp + ", wyśle zadanie do obu i przygotuje pakiety startowe.");
                tbM.TextChanged += delegate { tbT.Text = tbM.Text.Trim(); };
                if (f.ShowDialog(form) != DialogResult.OK) return;
                try
                {
                    var r = (Dictionary<string, object>)Stos("nowe_zadanie", P.D("projekt", cbP.Text, "modul", tbM.Text, "tytul", tbT.Text, "opis", tbO.Text,
                        "role_codex", Formularz.Zaznaczone(rCx), "role_claude", Formularz.Zaznaczone(rCl)));
                    Skanuj();
                    var pk = (Dictionary<string, object>)r["pakiety"];
                    PokazPakiety("Para utworzona: " + P.S(r, "tytul_sesji"), (Dictionary<string, object>)pk["codex"], (Dictionary<string, object>)pk["claude"]);
                }
                catch (Exception ex) { MessageBox.Show(form, "Nie utworzono: " + ex.Message, "Nowe zadanie", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        void NadajRole()
        {
            var para = WybranaPara();
            var nazwy = Siec.Trasy().Where(x => x.Aktywna).Select(x => x.Nazwa).Concat(msgs.SelectMany(m => new[] { m.Od, m.Do })).Where(P.PoprawnyNadawca).Distinct().OrderBy(x => x).ToList();
            using (var f = new Formularz("Nadaj role / przypisz sesję do modułu", fontN, fontB))
            {
                var cbN = f.Combo("Sesja (codex-… / claude-…):", nazwy, para != null ? P.S(para, "codex") : "");
                var cbP = f.Combo("Projekt:", P.Projekty().Select(x => P.S(x, "nazwa")).ToList(), para != null ? P.S(para, "projekt") : (cbProjekt.SelectedIndex > 0 ? (string)cbProjekt.SelectedItem : ""));
                var tbM = f.Tekst("Moduł:", para != null ? P.S(para, "modul") : "");
                var rl = f.Role("Role:", new string[0]);
                var tbO = f.Tekst("Opis roli od operatora:", "");
                f.Uwaga("Sesja dostanie kartę każdej roli jako kontekst. Nowa sesja tej samej strony w module przejmuje adres i niepotwierdzone wiadomości poprzedniej.");
                if (f.ShowDialog(form) != DialogResult.OK) return;
                try
                {
                    Stos("rola", P.D("nazwa", cbN.Text.Trim(), "projekt", cbP.Text, "modul", tbM.Text, "role", Formularz.Zaznaczone(rl), "opis", tbO.Text));
                    Skanuj();
                    stInfo.Text = "Przypisano " + cbN.Text + " do " + cbP.Text + "/" + tbM.Text;
                }
                catch (Exception ex) { MessageBox.Show(form, "Nie przypisano: " + ex.Message, "Role", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        void PakietyStartowe()
        {
            var para = WybranaPara();
            if (para == null) { MessageBox.Show(form, "Zaznacz parę na liście agentów.", "Pakiety startowe"); return; }
            Func<string, Dictionary<string, object>> pak = n =>
            {
                var plik = Path.Combine(P.Root, "stan", "start", n + ".txt");
                return P.D("nazwa", n, "tytul_sesji", TytulPary(P.S(para, "projekt"), P.S(para, "modul")), "plik", plik,
                    "tekst", File.Exists(plik) ? P.Czytaj(plik) : "Brak pakietu – parę utworzono poza „Nowe zadanie”. Nazwa sesji: " + TytulPary(P.S(para, "projekt"), P.S(para, "modul")) +
                    "\nNazwa w ECXP: " + n + ". Na początku: ecxp_przypisz (ja=" + n + ", projekt=" + P.S(para, "projekt") + ", modul=" + P.S(para, "modul") + "), potem ecxp_odbierz.");
            };
            PokazPakiety("Pakiety startowe: " + P.S(para, "modul"), pak(P.S(para, "codex")), pak(P.S(para, "claude")));
        }

        void PokazPakiety(string tytul, Dictionary<string, object> cx, Dictionary<string, object> cl)
        {
            using (var f = new Formularz(tytul, fontN, fontB))
            {
                f.Uwaga("Otwórz nowy czat ChatGPT/Codex i nową sesję Claude, nadaj obu nazwę „" + P.S(cx, "tytul_sesji") + "” i wklej każdej jej pakiet. " +
                        "Sesja zgłosi się do ECXP (ecxp_przypisz) i odbierze zadanie. Pakiety leżą też w " + Path.Combine(P.Root, "stan", "start") + ".");
                f.Kopiuj("ChatGPT/Codex – " + P.S(cx, "nazwa"), P.S(cx, "tekst"));
                f.Kopiuj("Claude – " + P.S(cl, "nazwa"), P.S(cl, "tekst"));
                f.TylkoZamknij();
                f.ShowDialog(form);
            }
        }

        void NazwySesji()
        {
            var sb = new StringBuilder();
            var projAkt = cbProjekt.SelectedIndex > 0 ? (string)cbProjekt.SelectedItem : "";
            foreach (Dictionary<string, object> d in (List<object>)Siec.Opis(projAkt)["pary"])
            {
                var t = TytulPary(P.S(d, "projekt"), P.S(d, "modul"));
                sb.AppendLine(t);
                sb.AppendLine("   ChatGPT/Codex: " + (P.S(d, "codex").Length > 0 ? P.S(d, "codex") : "brak sesji") + "   →   nazwa czatu: " + t);
                sb.AppendLine("   Claude:        " + (P.S(d, "claude").Length > 0 ? P.S(d, "claude") : "brak sesji") + "   →   nazwa sesji: " + t);
                sb.AppendLine();
            }
            using (var f = new Formularz("Nazwy sesji według standardu ECXP", fontN, fontB))
            {
                f.Uwaga("Standard: obie sesje pary noszą tę samą nazwę ze znacznikiem " + ZnacznikEcxp + " na początku; wcześniejsze znaczki (np. 🔴) zastępuje znacznik ECXP. " +
                        "Nazwy sesji Claude zmienia sesja Claude z narzędziami aplikacji (zlecenie poniżej). Nazwy czatów Codexa zmień ręcznie w aplikacji Codex – ECXP nie zapisuje w bazie Codexa.");
                f.Kopiuj("Nazwy do nadania", sb.Length > 0 ? sb.ToString() : "Brak par w tym projekcie.");
                f.TylkoZamknij();
                f.ShowDialog(form);
            }
        }

        // ---------- czyszczenie ----------
        // Przenosi z przeczytane\ do archiwum\RRRR-MM\ wiadomości obsłużone przez adresata i przeczytane przez
        // właściciela, starsze niż podana liczba dni (0 = wszystkie). Nic nie kasuje. Pytania bez odpowiedzi zostają.
        void Clean(int days)
        {
            var limit = DateTime.Now.AddDays(-days);
            var all = Wczytaj();
            Func<Msg, bool> otwarta = m => m.WymagaOdp && !m.Odpowiedziano;
            var cand = all.Where(m => m.Katalog == "przeczytane" && !m.Nowa && !otwarta(m) && (days == 0 || m.Czas < limit)).ToList();
            int nowe = all.Count(m => m.Katalog == "przeczytane" && m.Nowa);
            int mlode = all.Count(m => m.Katalog == "przeczytane" && !m.Nowa && !otwarta(m) && days > 0 && m.Czas >= limit);
            int otwarte = all.Count(m => m.Katalog == "przeczytane" && otwarta(m));
            int uAdresata = all.Count(m => m.Katalog != "przeczytane");
            if (cand.Count == 0)
            {
                var why = new StringBuilder("Nic nie przeniesiono.");
                if (mlode > 0) why.Append("\n\n• " + mlode + " przeczytanych jest młodszych niż wybrany wiek – wybierz „wszystkie przeczytane”.");
                if (nowe > 0) why.Append("\n\n• " + nowe + " nie oznaczyłeś jeszcze jako przeczytane.");
                if (otwarte > 0) why.Append("\n\n• " + otwarte + " to pytania lub zadania bez odpowiedzi – zostają do odpowiedzi.");
                if (uAdresata > 0) why.Append("\n\n• " + uAdresata + " czeka w kolejkach – zostają, dopóki adresat ich nie potwierdzi.");
                MessageBox.Show(form, why.ToString(), "Wyczyść", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(form, "Przenieść " + cand.Count + " przeczytanych wiadomości do archiwum?\n\n" +
                "Nic nie zostanie skasowane: pliki trafią do " + Path.Combine(P.Root, "archiwum") + "\\RRRR-MM.\n" +
                "Wiadomości w kolejkach, nieprzeczytane i pytania bez odpowiedzi zostają.", "Wyczyść",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int ok = 0, err = 0;
            try
            {
                var r = (Dictionary<string, object>)Stos("archiwizuj", P.D("pliki", cand.Select(m => m.Plik).ToList()));
                ok = Convert.ToInt32(r["przeniesione"]); err = Convert.ToInt32(r["bledy"]);
                foreach (var m in cand) read.Remove(m.Plik);
            }
            catch (Exception ex) { MessageBox.Show(form, "Nie przeniesiono: " + ex.Message, "Wyczyść", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            var kept = new HashSet<string>(P.Wszystkie(false).Select(m => m.Plik));
            read.RemoveWhere(r => r.StartsWith("STAN|") ? r != StanKey() : !kept.Contains(r));
            SaveRead();
            Skanuj();
            stInfo.Text = "Przeniesiono do archiwum: " + ok + (err > 0 ? ", błędy: " + err : "");
        }

        // ---------- okienko w rogu ----------
        void BuildPop()
        {
            pop = new Form { FormBorderStyle = FormBorderStyle.FixedToolWindow, Text = "ECXP-MAILBOX", TopMost = true,
                ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Size = new Size(460, 190), Font = fontN,
                BackColor = Color.FromArgb(255, 248, 220) };
            popTitle = new Label { Font = fontB, Location = new Point(12, 10), Size = new Size(430, 24) };
            popText = new Label { Location = new Point(12, 38), Size = new Size(430, 70) };
            var bOpen = Btn("Pokaż"); bOpen.Location = new Point(12, 112);
            var bRead = Btn("Przeczytane"); bRead.Location = new Point(110, 112);
            var bLater = Btn("Później"); bLater.Location = new Point(240, 112);
            pop.Controls.AddRange(new Control[] { popTitle, popText, bOpen, bRead, bLater });
            bOpen.Click += delegate { pop.Hide(); ShowWindow(); };
            bRead.Click += delegate { pop.Hide(); if (popStan) read.Add(StanKey()); MarkRead(popItems); };
            bLater.Click += delegate { pop.Hide(); };
            pop.FormClosing += (s, e) => { if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; pop.Hide(); } };
        }

        void ShowPop(string title, string text, List<Msg> items, bool stan)
        {
            popItems = items; popStan = stan;
            popTitle.Text = title; popText.Text = text;
            var wa = Screen.PrimaryScreen.WorkingArea;
            pop.Location = new Point(wa.Right - pop.Width - 12, wa.Bottom - pop.Height - 12);
            if (!pop.Visible) pop.Show();
            pop.TopMost = true;
            pop.BringToFront();
            System.Media.SystemSounds.Asterisk.Play();
        }

        // ---------- zasobnik ----------
        void BuildTray()
        {
            tray = new NotifyIcon { Icon = iconOk, Text = "ECXP-MAILBOX", Visible = true };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Napisz…", null, delegate { ShowWindow(); Pisz(null); });
            menu.Items.Add("Pokaż konsolę ECXP", null, delegate { ShowWindow(); });
            menu.Items.Add("Otwórz folder skrzynki", null, delegate { OpenPath(P.Root); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Zakończ", null, delegate { timer.Stop(); fsw.EnableRaisingEvents = false; if (klient != null) klient.Dispose(); tray.Visible = false; ExitThread(); });
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };
        }

        void ShowWindow()
        {
            pop.Hide();
            RefreshView();
            form.Show();
            form.WindowState = FormWindowState.Normal;
            form.TopMost = true;
            form.Activate();
            form.TopMost = false;
        }

        // ---------- obserwacja i strażnik ----------
        void Skanuj()
        {
            ostatniPelny = DateTime.Now;
            // Strażnik terminów działa w stosie ecxpd; alarmy przychodzą jako wypchnięte ramki DANE.
            if (alarmy.Count > 20) alarmy = alarmy.Skip(alarmy.Count - 20).ToList();

            msgs = Wczytaj();
            var fresh = msgs.Where(m => m.Nowa && !seen.Contains(m.Plik)).ToList();
            foreach (var m in msgs) seen.Add(m.Plik);
            var k = StanKey();
            bool stanZm = k != stanSeen && !read.Contains(k);
            stanSeen = k;

            if (fresh.Count == 1)
            {
                var m = fresh[0];
                ShowPop((m.Do == P.Wlasciciel ? "Do Ciebie od " : "Nowa wiadomość: ") + P.Nazwa(m.Od) + (m.Do == P.Wlasciciel ? "" : " → " + P.Nazwa(m.Do)),
                    Zdanie(m), fresh, stanZm);
            }
            else if (fresh.Count > 1)
                ShowPop("Nowe wiadomości: " + fresh.Count, string.Join("\n", fresh.Take(3).Select(m => P.Nazwa(m.Od) + " → " + P.Nazwa(m.Do) + ": " + P.Baza(m.Temat))), fresh, stanZm);
            else if (stanZm)
                ShowPop("Zmienił się stan modułów", CzekaNaCiebie().Split('\n')[0], new List<Msg>(), true);

            if (form.Visible) RefreshView(); else { RefreshPolaczenia(DateTime.Now); UpdateTray(); }
        }

        protected override void ExitThreadCore()
        {
            tray.Visible = false;
            tray.Dispose();
            base.ExitThreadCore();
        }

        // Zapisuje plik .ico (PNG 32 i 256 px) dla skrótów i budowania: ECXP-MAILBOX.exe /ico <plik>
        static void WriteIco(string path)
        {
            var imgs = new List<byte[]>();
            foreach (var s in new[] { 32, 256 })
                using (var b = IconBitmap(false, s)) using (var ms = new MemoryStream()) { b.Save(ms, System.Drawing.Imaging.ImageFormat.Png); imgs.Add(ms.ToArray()); }
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)imgs.Count);
                int off = 6 + 16 * imgs.Count; int[] sz = { 32, 256 };
                for (int i = 0; i < imgs.Count; i++)
                {
                    w.Write((byte)(sz[i] % 256)); w.Write((byte)(sz[i] % 256)); w.Write((byte)0); w.Write((byte)0);
                    w.Write((ushort)1); w.Write((ushort)32); w.Write((uint)imgs[i].Length); w.Write((uint)off);
                    off += imgs[i].Length;
                }
                foreach (var d in imgs) w.Write(d);
            }
        }

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length == 2 && args[0] == "/ico") { WriteIco(args[1]); return 0; }
            var exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            P.Root = Path.GetDirectoryName(exeDir);   // program leży w <skrzynka>\narzedzia
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "/root") P.Root = Path.GetFullPath(args[i + 1]);
            if (args.Length >= 1 && args[0] == "/selftest") return Test.Uruchom(args.Length > 1 ? args[1] : P.Root);
            if (args.Length >= 2 && args[0] == "/selftest-stos") return Test.Stos(args[1]);

            bool created;
            using (var mutex = new Mutex(true, @"Local\ECXP-MAILBOX-" + P.Sha(P.Root.ToLowerInvariant()).Substring(0, 12), out created))
            {
                if (!created) return 0;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new App());
            }
            return 0;
        }
    }

    // ---------- proste okna dialogowe konsoli ----------
    class Formularz : Form
    {
        readonly TableLayoutPanel t;
        readonly Font fb;
        readonly Button ok;
        public Formularz(string tytul, Font f, Font fb)
        {
            Text = tytul; Font = f; this.fb = fb; Size = new Size(760, 640); MinimumSize = new Size(560, 400);
            StartPosition = FormStartPosition.CenterParent; MinimizeBox = false;
            t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12), AutoScroll = true };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Controls.Add(t);
            var dol = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 48, Padding = new Padding(8) };
            ok = new Button { Text = "OK", AutoSize = true, Height = 32, Font = fb, BackColor = Color.FromArgb(255, 222, 0), FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.OK };
            var an = new Button { Text = "Anuluj", AutoSize = true, Height = 32, DialogResult = DialogResult.Cancel };
            dol.Controls.Add(ok); dol.Controls.Add(an);
            Controls.Add(dol);
            AcceptButton = null; CancelButton = an;
        }
        void Wiersz(string e, Control c, bool rosnie)
        {
            int r = t.RowCount++;
            t.RowStyles.Add(rosnie ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
            t.Controls.Add(new Label { Text = e, AutoSize = true, Margin = new Padding(3, 7, 8, 3) }, 0, r);
            t.Controls.Add(c, 1, r);
        }
        public ComboBox Combo(string e, List<string> lista, string wart)
        {
            var c = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
            c.Items.AddRange(lista.Cast<object>().ToArray()); c.Text = wart; Wiersz(e, c, false); return c;
        }
        public TextBox Tekst(string e, string wart) { var c = new TextBox { Dock = DockStyle.Fill, Text = wart }; Wiersz(e, c, false); return c; }
        public TextBox Wielowiersz(string e, string wart)
        {
            var c = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, Text = wart, MinimumSize = new Size(0, 140) };
            Wiersz(e, c, true); return c;
        }
        public CheckedListBox Role(string e, string[] zazn)
        {
            var c = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, Height = 116, MultiColumn = true, ColumnWidth = 160 };
            foreach (var r in Siec.RoleDomyslne) c.Items.Add(r, zazn.Contains(r));
            Wiersz(e, c, false); return c;
        }
        public static List<string> Zaznaczone(CheckedListBox c) { return c.CheckedItems.Cast<object>().Select(x => x.ToString()).ToList(); }
        public void Uwaga(string tekst)
        {
            int r = t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var l = new Label { Text = tekst, AutoSize = true, MaximumSize = new Size(680, 0), ForeColor = Color.DimGray, Margin = new Padding(3, 8, 3, 8) };
            t.Controls.Add(l, 0, r); t.SetColumnSpan(l, 2);
        }
        public void Kopiuj(string e, string tekst)
        {
            var p = new Panel { Dock = DockStyle.Fill, Height = 220, MinimumSize = new Size(0, 200) };
            var tb = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Text = System.Text.RegularExpressions.Regex.Replace(tekst ?? "", "\r?\n", "\r\n"), BackColor = Color.White };
            var b = new Button { Text = "Kopiuj", Dock = DockStyle.Right, Width = 90 };
            b.Click += delegate { try { Clipboard.SetText(tb.Text); b.Text = "Skopiowano"; } catch { } };
            p.Controls.Add(tb); p.Controls.Add(b);
            Wiersz(e, p, true);
        }
        public void TylkoZamknij() { ok.Text = "Zamknij"; }
    }

    // ---------- okno „Napisz” ----------
    class PiszForm : Form
    {
        public ComboBox cbDo, cbProjekt, cbModul, cbTyp;
        public TextBox tbTemat, tbTresc;
        public CheckBox chPilne;
        public string OdpNa = "";
        readonly Label hint;
        static readonly string[] TypyPl = { "Pytanie", "Zadanie", "Informacja", "Odpowiedź", "Blokada (potrzebna decyzja)" };
        static readonly string[] TypyKod = { "pytanie", "zadanie", "informacja", "odpowiedz", "blokada" };

        public PiszForm(List<string> adresaci, List<string> projekty, List<string> moduly, Font f, Font fb)
        {
            Text = "Napisz – ECXP";
            Font = f; Size = new Size(820, 640); MinimumSize = new Size(640, 480);
            StartPosition = FormStartPosition.CenterParent; MinimizeBox = false; KeyPreview = true;
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12) };
            t.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            cbDo = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };
            cbDo.Items.AddRange(adresaci.ToArray()); cbDo.SelectedIndex = 0;
            hint = new Label { Dock = DockStyle.Fill, AutoSize = false, Height = 40, ForeColor = Color.DimGray };
            cbProjekt = new ComboBox { Width = 220, DropDownStyle = ComboBoxStyle.DropDown };
            cbProjekt.Items.AddRange(projekty.ToArray());
            if (projekty.Count > 0) cbProjekt.SelectedIndex = 0;
            cbModul = new ComboBox { Width = 220, DropDownStyle = ComboBoxStyle.DropDown };
            cbModul.Items.AddRange(moduly.ToArray()); cbModul.Text = "wspolne";
            cbTyp = new ComboBox { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
            cbTyp.Items.AddRange(TypyPl); cbTyp.SelectedIndex = 0;
            chPilne = new CheckBox { Text = "Pilne (potwierdzenie w 15 min)", AutoSize = true, Margin = new Padding(16, 6, 3, 3) };
            var rodz = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            rodz.Controls.Add(cbTyp); rodz.Controls.Add(chPilne);
            tbTemat = new TextBox { Dock = DockStyle.Fill };
            tbTresc = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, Font = new Font("Segoe UI", 10.5f) };
            var uwaga = new Label
            {
                Dock = DockStyle.Fill, AutoSize = false, Height = 36, ForeColor = Color.DimGray,
                Text = "Bez danych spraw, haseł i kluczy. Decyzje typu push, scalenie, USER_ACCEPTED czy usuwanie wydawaj w czacie – kanał tylko o nie pyta."
            };
            var przyciski = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
            var bOk = new Button { Text = "Wyślij  (Ctrl+Enter)", AutoSize = true, Height = 34, Font = fb, BackColor = Color.FromArgb(255, 222, 0), FlatStyle = FlatStyle.Flat };
            var bAn = new Button { Text = "Anuluj", AutoSize = true, Height = 34, DialogResult = DialogResult.Cancel };
            przyciski.Controls.Add(bOk); przyciski.Controls.Add(bAn);
            CancelButton = bAn;

            Wiersz(t, "Do:", cbDo, SizeType.AutoSize);
            Wiersz(t, "", hint, SizeType.AutoSize);
            Wiersz(t, "Projekt:", cbProjekt, SizeType.AutoSize);
            Wiersz(t, "Moduł:", cbModul, SizeType.AutoSize);
            Wiersz(t, "Rodzaj:", rodz, SizeType.AutoSize);
            Wiersz(t, "Temat:", tbTemat, SizeType.AutoSize);
            Wiersz(t, "Treść:", tbTresc, SizeType.Percent);
            Wiersz(t, "", uwaga, SizeType.AutoSize);
            Wiersz(t, "", przyciski, SizeType.AutoSize);
            Controls.Add(t);

            cbDo.TextChanged += delegate { Podpowiedz(); };
            Podpowiedz();
            bOk.Click += delegate { Wyslij(); };
            KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; Wyslij(); } };
            Shown += delegate { tbTresc.Focus(); };
        }

        static void Wiersz(TableLayoutPanel t, string etykieta, Control c, SizeType st)
        {
            int r = t.RowCount++;
            t.RowStyles.Add(st == SizeType.Percent ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.AutoSize));
            t.Controls.Add(new Label { Text = etykieta, AutoSize = true, Margin = new Padding(3, 7, 8, 3) }, 0, r);
            t.Controls.Add(c, 1, r);
        }

        public void UstawAdresata(string s) { cbDo.Text = s; Podpowiedz(); }

        public string Adresat
        {
            get { var s = cbDo.Text; int i = s.IndexOf(" — "); return (i >= 0 ? s.Substring(0, i) : s).Trim().ToLowerInvariant(); }
        }
        public string Projekt { get { return P.Slug(cbProjekt.Text, 32, false); } }
        public string Modul { get { var m = P.Slug(cbModul.Text, 24, false); return m.Length > 0 ? m : "wspolne"; } }
        public string Typ { get { return TypyKod[Math.Max(0, cbTyp.SelectedIndex)]; } }

        void Podpowiedz()
        {
            var a = Adresat;
            if (a.StartsWith("para:")) hint.Text = "Zasada 1:1: obie strony pary (Codex i Claude) dostają to samo zadanie w jednej sprawie. Doręczone, gdy obie potwierdzą.";
            else if (a.StartsWith("codex/") || a.StartsWith("claude/")) hint.Text = "Jedna strona pary; druga dostanie dosłowną kopię (zasada 1:1).";
            else if (a == P.Koordynacja) hint.Text = "Odbiór co 10 min. Odpowiada sam albo przekazuje sprawę czatowi lub sesji właściwego modułu i mówi Ci, komu. Najlepszy adres, gdy nie wiesz, kto prowadzi moduł.";
            else if (a.StartsWith("codex-")) hint.Text = "Czat Codexa modułu czyta kanał między etapami pracy. Brak potwierdzenia w 4 h → ponowienie, potem przekazanie do koordynacji i alarm u Ciebie.";
            else if (a.StartsWith("claude-")) hint.Text = "Sesja Claude odbiera przy najbliższej pracy (narzędzie ecxp_odbierz albo przypomnienie przy starcie). Brak potwierdzenia w 4 h → alarm u Ciebie.";
            else hint.Text = "Adres: codex-<czat> albo claude-<sesja>.";
        }

        void Wyslij()
        {
            if (Adresat == P.Wlasciciel || !(P.PoprawnyAdresat(Adresat) || System.Text.RegularExpressions.Regex.IsMatch(Adresat, @"^(para:|\*/|codex/|claude/)[a-z0-9-]+$|^rola:[a-z0-9-]+@([a-z0-9-]+|\*)$")))
            { MessageBox.Show(this, "Wybierz adresata z listy albo wpisz nazwę w postaci codex-<czat> lub claude-<sesja>.", "Napisz"); return; }
            if (tbTresc.Text.Trim().Length == 0) { MessageBox.Show(this, "Wpisz treść wiadomości.", "Napisz"); tbTresc.Focus(); return; }
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    // ---------- test protokołu na kopii (ECXP-MAILBOX.exe /selftest <pusty katalog>) ----------
    static class Test
    {
        // Test operatora przez prawdziwy stos: konsola łączy się jako operator (stos sprawdza obraz procesu),
        // tworzy parę 1:1, sprawdza lustro, kopię dla pary, przejęcie adresu i role. ECXP-MAILBOX.exe /selftest-stos <pusty katalog>
        public static int Stos(string root)
        {
            var log = new StringBuilder(); int zle = 0;
            Action<bool, string> ok = (c, t) => { log.AppendLine((c ? "PASS  " : "FAIL  ") + t); if (!c) zle++; };
            Klient k = null;
            try
            {
                P.Root = Path.GetFullPath(root);
                Directory.CreateDirectory(P.Root);
                var t0 = DateTime.Now;
                k = Klient.Polacz(P.Wlasciciel, "konsola", true);
                ok(k.Przyznane == P.Wlasciciel, "połączenie operatora: SYN / SYN+ACK / ACK, rola przyznana przez stos (" + (int)(DateTime.Now - t0).TotalMilliseconds + " ms razem z uruchomieniem stosu)");
                var t1 = DateTime.Now;
                var ping = (Dictionary<string, object>)k.Wywolaj("ping", null);
                ok(P.S(ping, "protokol") == P.Protokol, "ping przez potok: " + (DateTime.Now - t1).TotalMilliseconds.ToString("0.0") + " ms");

                var r = (Dictionary<string, object>)k.Wywolaj("nowe_zadanie", P.D("projekt", "testowy", "modul", "elan", "tytul", "EL_an test", "opis", "Zadanie testowe dla pary. Zażółć gęślą jaźń.",
                    "role_codex", new List<string> { "programista" }, "role_claude", new List<string> { "recenzent", "tester" }));
                var para = (Dictionary<string, object>)r["para"];
                ok(P.S(para, "codex") == "codex-elan" && P.S(para, "claude") == "claude-elan" && P.B(para, "pelna"), "nowe zadanie: para codex-elan + claude-elan, te same nazwy po obu stronach");
                ok(P.S(r, "tytul_sesji") == "🔗ECXP EL_an test", "wspólna nazwa sesji ze znacznikiem: " + P.S(r, "tytul_sesji"));
                var zad = (Dictionary<string, object>)r["zadanie"];
                var adr = ((System.Collections.IEnumerable)zad["adresaci"]).Cast<object>().Select(x => x.ToString()).ToList();
                ok(adr.Count == 2 && P.S(zad, "lustro_id").StartsWith("L-"), "zadanie w lustrze do obu stron (" + string.Join(", ", adr) + ")");
                var lista = P.Wszystkie(false);
                var lustrzane = lista.Where(m => m.LustroId == P.S(zad, "lustro_id")).ToList();
                ok(lustrzane.Count == 2 && lustrzane.Select(m => m.WatekPole).Distinct().Count() == 1 && lustrzane.All(m => m.Uwagi.Contains("gęślą")), "obie kopie: ten sam wątek i dosłowna treść");
                ok(File.Exists(Path.Combine(P.Root, "stan", "start", "codex-elan.txt")) && File.Exists(Path.Combine(P.Root, "stan", "start", "claude-elan.txt")), "pakiety startowe dla obu stron");
                var zn = P.Czytaj(Path.Combine(P.Root, "stan", "znaczniki.json"));
                ok(zn.Contains("codex-elan") && zn.Contains("claude-elan"), "znaczniki ECXP zapisane dla obu sesji");

                var r2 = (Dictionary<string, object>)k.Wywolaj("wyslij", P.D("do", "codex/elan", "projekt", "testowy", "modul", "elan", "typ", "informacja", "tresc", "Uwaga tylko do Codexa."));
                var kopie = ((System.Collections.IEnumerable)r2["kopie_dla_pary"]).Cast<Dictionary<string, object>>().ToList();
                ok(kopie.Count == 1 && P.S(kopie[0], "do") == "claude-elan", "zasada 1:1: wiadomość operatora do Codexa ma kopię u Claude");

                bool niepelna = false;
                try { k.Wywolaj("wyslij", P.D("do", "para:brak", "projekt", "testowy", "typ", "zadanie", "tresc", "x")); } catch (EcxpException) { niepelna = true; }
                ok(niepelna, "niepełna para odrzucona (zasada 1:1 wymaga obu stron)");

                var r3 = (Dictionary<string, object>)k.Wywolaj("przypisz", P.D("nazwa", "claude-elan2", "projekt", "testowy", "modul", "elan", "role", new List<string> { "recenzent" }));
                var przek = ((System.Collections.IEnumerable)r3["przekazane"]).Cast<object>().Count();
                ok(P.S((Dictionary<string, object>)r3["para"], "claude") == "claude-elan2" && przek >= 2, "przejęcie adresu: nowa sesja Claude dostała " + przek + " niepotwierdzone wiadomości poprzedniej");

                var r4 = (Dictionary<string, object>)k.Wywolaj("rola", P.D("nazwa", "codex-elan", "projekt", "testowy", "modul", "elan", "role", new List<string> { "programista", "tester" }, "opis", "test"));
                ok(((System.Collections.IEnumerable)r4["karty_rol"]).Cast<object>().Count() == 2, "nadanie ról: sesja dostała 2 karty ról");

                var tr = (Dictionary<string, object>)k.Wywolaj("trasy", P.D("projekt", "testowy"));
                ok(((System.Collections.IEnumerable)tr["pary"]).Cast<object>().Count() >= 1, "tablica tras czytelna dla klienta");
                string spoj; P.Stany(out spoj);
                ok(spoj.StartsWith("spójny"), "dziennik po wszystkich operacjach: " + spoj);
            }
            catch (Exception ex) { log.AppendLine("FAIL  wyjątek: " + ex); zle++; }
            finally { if (k != null) k.Dispose(); }
            log.AppendLine(zle == 0 ? "WYNIK: wszystkie testy PASS" : "WYNIK: FAIL " + zle);
            try { File.WriteAllText(Path.Combine(root, "selftest-stos.txt"), log.ToString(), new UTF8Encoding(false)); } catch { }
            return zle;
        }

        public static int Uruchom(string root)
        {
            var log = new StringBuilder(); int zle = 0;
            Action<bool, string> ok = (c, t) => { log.AppendLine((c ? "PASS  " : "FAIL  ") + t); if (!c) zle++; };
            try
            {
                Directory.CreateDirectory(root);
                if (Directory.EnumerateFileSystemEntries(root).Any(x => !x.EndsWith("selftest.txt"))) throw new Exception("katalog testu musi być pusty: " + root);
                P.Root = root;
                foreach (var d in P.Foldery) Directory.CreateDirectory(Path.Combine(root, d));

                ok(P.Baza("ODP-SPIS-GALEZI") == "SPIS-GALEZI" && P.Baza("PYTANIA-OPIEKUNOW-ODPOWIEDZ") == "PYTANIA-OPIEKUNOW", "tematy odpowiedzi łączą się z pytaniem");
                ok(P.DoMnie(P.Koordynacja, "codex") && P.DoMnie(P.Koordynacja, "codex-core") && !P.DoMnie("codex-elan", "codex"), "adres ogólny codex trafia tylko do koordynacji");

                // 1. właściciel pyta koordynację
                var r1 = P.Wyslij(new P.Nowa { Od = P.Wlasciciel, Do = P.Koordynacja, Modul = "ElCarver", Typ = "pytanie", Temat = "Stan ElCarver", Tresc = "Jaki jest stan modułu? Zażółć gęślą jaźń." });
                var p1 = (string)r1["plik"];
                ok(p1.Contains("\\do-codex\\") && File.Exists(p1), "wysłanie do kolejki do-codex: " + Path.GetFileName(p1));
                ok(!Directory.GetFiles(Path.Combine(root, "do-codex"), "*.tmp").Any(), "po zapisie atomowym nie zostaje .tmp");
                ok(Convert.ToInt32(r1["termin_potwierdzenia_min"]) == 30, "termin potwierdzenia koordynacji 30 min");

                // 2. odbiór przez koordynację
                var odb = P.Odbierz(P.Koordynacja, 10);
                ok(odb.Count == 1 && (string)odb[0]["integralnosc"] == "ok" && ((string)odb[0]["tresc"]).Contains("gęślą"), "odbiór, integralność i polskie znaki");
                ok(P.Odbierz("codex-elan", 10).Count == 0, "inny czat nie widzi cudzej wiadomości");
                string spoj; var st = P.Stany(out spoj);
                ok(st[(string)r1["id"]].Dostarczona.HasValue && spoj.StartsWith("spójny"), "dziennik: dostarczona, łańcuch spójny (" + spoj + ")");

                // 3. przekazanie do modułu i odpowiedź z doklejonym potwierdzeniem
                var r2 = P.Wyslij(new P.Nowa { Od = P.Koordynacja, Do = "codex-elcarver", Modul = "elcarver", Typ = "zadanie", Temat = "STAN-ELCARVER", Tresc = "Właściciel pyta o stan.", OdpNa = (string)r1["id"] });
                ok((string)r2["potwierdzono_przy_okazji"] == (string)r1["id"], "przekazanie potwierdza oryginał (ACK doklejony)");
                ok(File.Exists(Path.Combine(root, "przeczytane", Path.GetFileName(p1))), "oryginał przeniesiony do przeczytane");
                var r3 = P.Wyslij(new P.Nowa { Od = "codex-elcarver", Do = P.Wlasciciel, Modul = "elcarver", Typ = "odpowiedz", Temat = "STAN-ELCARVER", Tresc = "Czeka na scalenie.", OdpNa = (string)r2["id"] });
                ok(((string)r3["plik"]).Contains("\\do-wlasciciela\\"), "odpowiedź dla właściciela w do-wlasciciela");

                var lista = P.Wszystkie(false); P.Powiaz(lista);
                var q = lista.First(m => m.Id == (string)r1["id"]);
                ok(lista.Select(m => m.Watek).Distinct().Count() == 1, "trzy wiadomości w jednym wątku");
                ok(q.Odpowiedziano, "pytanie oznaczone jako odpowiedziane");

                // 4. potwierdzenie powtórne (idempotentne) i cudze
                var pp = P.Potwierdz("codex-elcarver", (string)r2["id"], "obsluzona", "");
                ok((string)P.Potwierdz("codex-elcarver", (string)r2["id"], "obsluzona", "")["stan"] == "juz_potwierdzona" && (string)pp["stan"] == "juz_potwierdzona", "potwierdzenie powtórne niczego nie psuje");
                bool cudze = false;
                var r4 = P.Wyslij(new P.Nowa { Od = P.Wlasciciel, Do = "codex-elan", Typ = "informacja", Tresc = "Test." });
                try { P.Potwierdz("codex-elview", (string)r4["id"], "obsluzona", ""); } catch (EcxpException) { cudze = true; }
                ok(cudze, "nie da się potwierdzić cudzej wiadomości");
                bool podszycie = false;
                try { P.Wyslij(new P.Nowa { Od = "Wlasciciel X", Do = "codex", Typ = "pytanie", Tresc = "x" }); } catch (EcxpException) { podszycie = true; }
                ok(podszycie, "niepoprawny nadawca odrzucony");

                // 5. zmiana pliku po wysłaniu jest wykrywana
                var plik4 = (string)r4["plik"];
                File.WriteAllText(plik4, P.Czytaj(plik4).Replace("Test.", "Zmienione."), new UTF8Encoding(false));
                var o4 = P.Odbierz("codex-elan", 10);
                ok(o4.Count == 1 && ((string)o4[0]["integralnosc"]).StartsWith("USZKODZONA"), "zmiana treści po wysłaniu wykryta");

                // 6. zaginięcie pliku → strażnik alarmuje
                File.Move(plik4, Path.Combine(root, "zgubione.bin"));
                var al = P.Straz();
                ok(al.Any(a => a.Id == (string)r4["id"] && a.Opis.Contains("zaginęła")), "strażnik wykrywa zaginioną wiadomość");

                // 7. naruszenie dziennika
                var dz = Path.Combine(root, "dziennik", "dziennik.jsonl");
                var linie = File.ReadAllLines(dz, Encoding.UTF8).ToList();
                P.Stany(out spoj); ok(spoj.StartsWith("spójny"), "dziennik spójny przed ingerencją: " + spoj);
                linie.RemoveAt(1); File.WriteAllLines(dz, linie, new UTF8Encoding(false));
                P.Stany(out spoj); ok(spoj.StartsWith("NARUSZONY"), "usunięcie wpisu wykryte: " + spoj);
            }
            catch (Exception ex) { log.AppendLine("FAIL  wyjątek: " + ex); zle++; }
            log.AppendLine(zle == 0 ? "WYNIK: wszystkie testy PASS" : "WYNIK: FAIL " + zle);
            try { File.WriteAllText(Path.Combine(root, "selftest.txt"), log.ToString(), new UTF8Encoding(false)); } catch { }
            return zle;
        }
    }
}
