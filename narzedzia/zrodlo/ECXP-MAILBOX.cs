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
        readonly Font fontTresc = new Font("Segoe UI", 11f);
        readonly Font fontTytul = new Font("Segoe UI", 14f, FontStyle.Bold);
        FlowLayoutPanel pnlZal;
        PictureBox logoBox;
        Button bMotyw;
        int wciecie;
        string UstawieniaPlik;

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
        static readonly Color Zolty = Color.FromArgb(255, 222, 0);
        static Color Zielony { get { return Motyw.K(Color.FromArgb(0, 128, 0)); } }
        static Color Czerwony { get { return Motyw.K(Color.FromArgb(200, 0, 0)); } }

        public App()
        {
            Stan = Path.Combine(P.Root, "STAN-MODULOW.md");
            ReadFile = Path.Combine(P.Root, "narzedzia", "przeczytane-wlasciciel.txt");
            UstawieniaPlik = Path.Combine(P.Root, "narzedzia", "ustawienia-wlasciciel.txt");
            var ust = P.Czytaj(UstawieniaPlik);
            Motyw.Ciemny = ust.Contains("motyw=ciemny");
            var mj = Regex.Match(ust, @"jezyk=([a-z]{2})");
            Jezyk.Kod = mj.Success ? mj.Groups[1].Value : "pl";
            widokUslug = ust.Contains("uslugi=karty") ? "karty" : "pasek";
            foreach (var d in P.Foldery) Directory.CreateDirectory(Path.Combine(P.Root, d));
            foreach (var l in P.Czytaj(ReadFile).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)) read.Add(l);

            iconOk = Icon.FromHandle(IconBitmap(false, 32).GetHicon());
            iconNew = Icon.FromHandle(IconBitmap(true, 32).GetHicon());
            BuildForm();
            BuildPop();
            Motyw.Zastosuj(form); Motyw.Zastosuj(pop); Jezyk.Zastosuj(form); Jezyk.Zastosuj(pop);
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
                // status CLI: co 3 min, po uruchomieniu z konsoli co 10 s przez 5 min
                var odstep = DateTime.Now < czujDo ? 10 : 180;
                if ((DateTime.Now - ostatnieCli).TotalSeconds >= odstep) { ostatnieCli = DateTime.Now; SprawdzCliTlo(); }
                if ((DateTime.Now - ostatnieUslugi).TotalSeconds >= 3) { ostatnieUslugi = DateTime.Now; OdswiezUslugi(); }
            };
            timer.Start();

            if (msgs.Any(m => m.Nowa) || !read.Contains(StanKey())) ShowWindow();
            else ShowPop(Jezyk.T("ECXP-MAILBOX działa"), Jezyk.T("Konsola połączona ze stosem ECXP. Brak nowych wiadomości. Ikona ECXP jest przy zegarku."), new List<Msg>(), false);
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
                if (stStos != null) { stStos.Text = Jezyk.T("⚠ ECXP: stos niedostępny – ") + ex.Message; stStos.ForeColor = Czerwony; }
                return;
            }
            StanStosu();
        }

        void StanStosu()
        {
            bool ok = klient != null && klient.Polaczony;
            stStos.Text = ok ? Jezyk.T("ECXP: połączono ze stosem ecxpd ✓") : Jezyk.T("⚠ ECXP: brak połączenia ze stosem");
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
                var t = NazwaW(P.S(d, "do")) + " · " + P.S(d, "temat") + ": " + P.S(d, "opis");
                alarmy.Add(t);
                if (alarmyPokazane.Add(P.S(d, "id"))) ShowPop(Jezyk.T("ECXP: problem z doręczeniem"), t, new List<Msg>(), false);
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
            var s = m.Success ? m.Groups[1].Value.Trim() : Jezyk.T("(brak sekcji „Czeka na Ciebie”)");
            if (alarmy.Count > 0) s = string.Join("\n", alarmy.Select(a => "⚠ " + a)) + "\n" + s;
            return s;
        }

        static string Crlf(string s) { return Regex.Replace(s ?? "", "\r?\n", "\r\n"); }

        static Color Kolor(Msg m)
        {
            if (m.Blad != null) return Motyw.K(Color.FromArgb(170, 0, 0));
            switch (P.Rodzaj(m))
            {
                case "Pytanie": case "Zadanie": case "Blokada": return Motyw.K(Color.FromArgb(200, 0, 0));
                case "Gotowe": case "Meldunek": return Motyw.K(Color.FromArgb(0, 128, 0));
                case "Recenzja": return Motyw.K(Color.FromArgb(128, 0, 170));
                default: return Motyw.K(Color.FromArgb(0, 80, 200));
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
                var s = TOpis(P.Opis(m.Dor, teraz));
                if (m.WymagaOdp && m.Dor.Potwierdzona.HasValue) s += m.Odpowiedziano ? Jezyk.T(", odpowiedziano") : Jezyk.T(", bez odpowiedzi");
                return s;
            }
            if (m.Katalog == "do-wlasciciela") return Jezyk.T("dla Ciebie");
            if (m.Katalog != "przeczytane") return Jezyk.T("czeka u adresata od ") + P.Wiek(teraz - m.Czas);
            var r = Jezyk.T("odebrana");
            if (m.WymagaOdp) r += m.Odpowiedziano ? Jezyk.T(", odpowiedziano") : Jezyk.T(", bez odpowiedzi od ") + P.Wiek(teraz - m.Czas);
            return r;
        }

        static string Zdanie(Msg m)
        {
            var t = (m.Uwagi ?? "").Trim();
            var pierwsza = t.Split('\n')[0].Trim();
            if (pierwsza.Length > 110) pierwsza = pierwsza.Substring(0, 107) + "…";
            return Jezyk.T(P.Rodzaj(m)) + ": " + P.Baza(m.Temat) + (pierwsza.Length > 0 ? " – " + pierwsza : "");
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
            var head = naglowek = new Panel { Dock = DockStyle.Top, Height = 100, BackColor = Color.White };
            var logo = logoBox = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Location = new Point(10, 6), Size = new Size(172, 52) };
            UstawLogo();
            var lblProj = new Label { Text = "Projekt:", AutoSize = true, Location = new Point(200, 22) };
            cbProjekt = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(262, 18), Width = 180 };
            cbProjekt.Items.Add("Wszystkie projekty");
            foreach (var pr in P.Projekty()) cbProjekt.Items.Add(P.S(pr, "nazwa"));
            cbProjekt.SelectedIndex = cbProjekt.Items.Count > 1 ? 1 : 0;
            var bNoweH = Btn("＋  Nowe zadanie / moduł…");
            bNoweH.Font = fontB; bNoweH.BackColor = Zolty; bNoweH.FlatStyle = FlatStyle.Flat; bNoweH.Location = new Point(460, 14);
            bMotyw = Btn(Motyw.Ciemny ? Jezyk.T("☀  Motyw jasny") : Jezyk.T("☾  Motyw ciemny"));
            bMotyw.Location = new Point(700, 14);
            bMotyw.Click += delegate { PrzelaczMotyw(); };
            cbJezyk = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(870, 18), Width = 120 };
            foreach (var j in Jezyk.Dostepne) cbJezyk.Items.Add(j[1]);
            cbJezyk.SelectedIndex = Math.Max(0, Array.FindIndex(Jezyk.Dostepne, j => j[0] == Jezyk.Kod));
            cbJezyk.SelectedIndexChanged += delegate { ZmienJezyk(Jezyk.Dostepne[cbJezyk.SelectedIndex][0]); };
            var bOProgramie = Btn("ⓘ  O programie");
            bOProgramie.Location = new Point(1000, 14);
            bOProgramie.Click += delegate { OProgramie(); };
            head.Controls.AddRange(new Control[] { logo, lblProj, cbProjekt, bNoweH, bMotyw, cbJezyk, bOProgramie });

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
            var bDecyzjaR = Btn("⚖  Decyzja…");
            bDecyzjaR.Click += delegate { Decyzja(); };
            top.Controls.AddRange(new Control[] { bPisz, bOdp, bDecyzjaR, cbWidok, cbModul, lblSz, szukaj });

            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            lv = new ListView { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, HideSelection = false, ShowGroups = true,
                SmallImageList = new ImageList { ImageSize = new Size(1, 26) } };
            foreach (var c in new object[][] { new object[] { "Czas", 100 }, new object[] { "", 26 }, new object[] { "Od → Do", 230 },
                new object[] { "Moduł", 95 }, new object[] { "Treść", 420 }, new object[] { "Doręczenie", 300 } })
                lv.Columns.Add((string)c[0], (int)c[1]);
            det = new RichTextBox { ReadOnly = true, Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.None, DetectUrls = false };
            split.Panel1.Controls.Add(lv);
            split.Panel2.Controls.Add(det);
            pnlZal = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 6, 8, 4), Visible = false };
            split.Panel2.Controls.Add(pnlZal);
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
            var bDecyzja = Btn("⚖  Decyzja…");
            var bTlo = Btn("▶  Agent w tle…");
            var bWznow = Btn("Wznów parę / łańcuch");
            agTop.Controls.AddRange(new Control[] { bNowe, bRola, bDecyzja, bTlo, bWznow, bPakiet, bNazwy });
            bDecyzja.Click += delegate { Decyzja(); };
            bTlo.Click += delegate { UruchomAgenta(); };
            bWznow.Click += delegate { Wznow(); };
            lblAg = new Label { Dock = DockStyle.Top, Height = 44, Padding = new Padding(6), ForeColor = Motyw.Slaby,
                Text = "Zasada 1:1: w każdym module pracuje para – sesja ChatGPT/Codex i sesja Claude – nad tym samym zadaniem i jedną gałęzią. " +
                       "Sesje w parze noszą wspólny znacznik " + "🔗ECXP" + " przed nazwą." };
            lvAg = new ListView { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, HideSelection = false };
            foreach (var c in new object[][] { new object[] { "Projekt", 95 }, new object[] { "Moduł", 110 }, new object[] { "ChatGPT/Codex", 200 },
                new object[] { "Claude", 200 }, new object[] { "Para", 90 }, new object[] { "Role", 260 }, new object[] { "Nazwa sesji (obie strony)", 230 }, new object[] { "Wspólna gałąź", 260 }, new object[] { "Łańcuch ról", 230 } })
                lvAg.Columns.Add((string)c[0], (int)c[1]);
            // usługi Claude i Codex: wariant A – pasek w nagłówku, wariant B – karty pod nagłówkiem
            BudujUslugi(head);
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
            tabStan = new TabPage(Jezyk.T("Stan modułów"));
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
            stCli = new ToolStripStatusLabel { Font = fontB, Text = "CLI: …" };
            strip.Items.AddRange(new ToolStripItem[] { stInfo, stCli, stStos, stSyg });

            form.Controls.Add(tabs);
            form.Controls.Add(czeka);
            form.Controls.Add(lblCzeka);
            form.Controls.Add(pnlKarty);
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
                if (m == null) { MessageBox.Show(form, Jezyk.T("Najpierw zaznacz wiadomość, na którą chcesz odpowiedzieć."), Jezyk.T("Odpowiedz")); return; }
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
            bSpis.Click += delegate { SpisGalezi(); };
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
            det.SelectionFont = f; det.SelectionColor = c; det.SelectionIndent = wciecie;
            det.AppendText(t);
        }

        // Czytelny widok rozmowy: nagłówek wątku, każda wiadomość jako blok (nadawca → adresat, rodzaj, czas),
        // treść większą czcionką z wcięciem, załączniki do kliknięcia nad treścią, dane techniczne drobno na końcu.
        void PokazWatek(Msg sel)
        {
            det.SuspendLayout();
            det.Clear();
            var w = msgs.Where(x => x.Watek == sel.Watek).OrderBy(x => x.Czas).ToList();
            var teraz = DateTime.Now;
            wciecie = 0;
            Dopisz(P.Baza(sel.Temat) + "\n", fontTytul, Motyw.Tekst);
            Dopisz(w.Count + (w.Count == 1 ? Jezyk.T(" wiadomość") : Jezyk.T(" wiadomości")) + (sel.Projekt.Length > 0 ? Jezyk.T("   ·   projekt ") + sel.Projekt : "") +
                (sel.Modul.Length > 0 ? Jezyk.T("   ·   moduł ") + sel.Modul : "") + "\n\n", fontS, Motyw.Slaby);
            int poz = 0;
            var zal = new List<Dictionary<string, object>>();
            foreach (var m in w)
            {
                if (m == sel) poz = det.TextLength;
                wciecie = 0;
                Dopisz(m == sel ? "▶  " : "●  ", fontB, Kolor(m));
                Dopisz(NazwaW(m.Od) + "  →  " + (m.Lustro.Count > 1 ? Jezyk.T("para: ") + string.Join(" + ", m.Lustro.Select(P.Nazwa)) : NazwaW(m.Do)), fontB, Motyw.Tekst);
                Dopisz("     " + Jezyk.T(P.Rodzaj(m)) + (m.Pilne ? Jezyk.T(" · PILNE") : "") + (m.KopiaDla.Length > 0 ? Jezyk.T(" · kopia dla pary (1:1)") : "") + "   ·   " + m.Czas.ToString("dd.MM HH:mm") + "\n", fontS, Kolor(m));
                wciecie = 24;
                if (m.Blad != null) Dopisz(Jezyk.T("Nie da się odczytać wiadomości: ") + m.Blad + "\n", fontN, Czerwony);
                if (m.Uwagi.Trim().Length > 0) Dopisz(TTresc(m.Uwagi.Trim()) + "\n", fontTresc, Motyw.Tekst);
                foreach (var c in m.Czesci)
                {
                    var rodzaj = P.S(c, "rodzaj");
                    if (rodzaj == "plik")
                    {
                        long roz; long.TryParse(P.S(c, "rozmiar"), out roz);
                        Dopisz("📎 " + (P.S(c, "nazwa").Length > 0 ? P.S(c, "nazwa") : P.S(c, "sha256").Substring(0, 12)) + "  (" + Rozmiar(roz) + ")\n", fontN, Motyw.K(Color.FromArgb(0, 80, 200)));
                        zal.Add(c);
                    }
                    else if (rodzaj == "dane") Dopisz("🗂 dane: " + (P.S(c, "schemat").Length > 0 ? P.S(c, "schemat") : "JSON") + "\n", fontN, Motyw.Slaby);
                    else if (rodzaj == "tekst" && P.S(c, "tekst") != m.Uwagi) Dopisz(P.S(c, "tekst").Trim() + "\n", fontTresc, Motyw.Tekst);
                }
                if (m.Kontrole.Count > 0)
                {
                    Dopisz(Jezyk.T("Kontrole:\n"), fontB, Motyw.Tekst);
                    foreach (var k in m.Kontrole)
                        Dopisz("• " + k + "\n", fontN, k.StartsWith("[PASS") ? Zielony : (k.StartsWith("[FAIL") || k.StartsWith("[BLOCKED")) ? Czerwony : Motyw.Tekst);
                }
                if (m.Pliki.Count > 0) { Dopisz(Jezyk.T("Pliki:\n"), fontB, Motyw.Tekst); foreach (var p in m.Pliki) Dopisz("• " + p + "\n", fontN, Motyw.Tekst); }
                if (m.Commit.Length > 0) Dopisz(Jezyk.T("Commit: ") + m.Commit + "\n", fontN, Motyw.Slaby);
                if (m.Ograniczenia.Length > 0) Dopisz(Jezyk.T("Ograniczenia: ") + m.Ograniczenia + "\n", fontS, Motyw.Slaby);
                Dopisz(Jezyk.T("Doręczenie: ") + Doreczenie(m, teraz) + "   ·   id " + m.Id + (m.Seq > 0 ? " · seq " + m.Seq : "") + "\n", fontS, Motyw.Slaby);
                wciecie = 0;
                Dopisz("\n", fontS, Motyw.Slaby);
            }
            det.SelectionStart = poz; det.SelectionLength = 0;
            det.ScrollToCaret();
            det.ResumeLayout();

            pnlZal.SuspendLayout();
            pnlZal.Controls.Clear();
            if (zal.Count > 0)
            {
                pnlZal.Controls.Add(new Label { Text = "Załączniki w tej rozmowie:", AutoSize = true, ForeColor = Motyw.Slaby, Margin = new Padding(0, 6, 8, 0) });
                foreach (var c in zal)
                {
                    var cc = c;
                    var ll = new LinkLabel { Text = "📎 " + (P.S(c, "nazwa").Length > 0 ? P.S(c, "nazwa") : P.S(c, "sha256").Substring(0, 12)), AutoSize = true, Margin = new Padding(4, 6, 8, 0),
                        LinkColor = Motyw.K(Color.FromArgb(0, 80, 200)) };
                    ll.LinkClicked += delegate { OtworzZalacznik(cc); };
                    pnlZal.Controls.Add(ll);
                }
            }
            pnlZal.Visible = zal.Count > 0;
            pnlZal.ResumeLayout();
        }

        static string Rozmiar(long b) { return b >= 1048576 ? (b / 1048576.0).ToString("0.0") + " MB" : b >= 1024 ? (b / 1024) + " kB" : b + " B"; }

        // Otwiera załącznik z magazynu kontekstu (po sprawdzeniu SHA-256). Typy wykonywalne nie są uruchamiane –
        // dla nich otwiera się tylko folder z plikiem.
        void OtworzZalacznik(Dictionary<string, object> c)
        {
            var sha = P.S(c, "sha256");
            var src = sha.Length == 64 ? P.ObjPath(sha) : "";
            if (src.Length == 0 || !File.Exists(src)) { MessageBox.Show(form, Jezyk.T("Nie ma tego załącznika w magazynie kontekstu."), Jezyk.T("Załącznik")); return; }
            if (P.ShaPliku(src) != sha) { MessageBox.Show(form, Jezyk.T("Załącznik jest uszkodzony – skrót SHA-256 się nie zgadza. Nie otwieram go."), Jezyk.T("Załącznik"), MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var nazwa = Path.GetFileName(P.S(c, "nazwa"));
            if (nazwa.Length == 0) nazwa = sha.Substring(0, 12);
            var dir = Path.Combine(Path.GetTempPath(), "ecxp-podglad", sha.Substring(0, 12));
            Directory.CreateDirectory(dir);
            var dst = Path.Combine(dir, nazwa);
            if (!File.Exists(dst)) File.Copy(src, dst);
            var bezpieczne = new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".pdf", ".txt", ".md", ".log", ".json", ".csv", ".xml", ".html", ".htm" };
            if (bezpieczne.Contains(Path.GetExtension(dst).ToLowerInvariant())) System.Diagnostics.Process.Start(dst);
            else System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + dst + "\"");
        }

        // Załączniki → obiekty kontekstu w stosie (SHA-256) → części wiadomości typu plik.
        List<Dictionary<string, object>> WyslijZalaczniki(List<string> pliki)
        {
            var r = new List<Dictionary<string, object>>();
            foreach (var p in pliki)
            {
                var o = (Dictionary<string, object>)Stos("kontekst_zapisz", P.D("sciezka", p, "nazwa", Path.GetFileName(p), "typ", Mime(p)));
                r.Add(P.D("rodzaj", "plik", "sha256", P.S(o, "sha256"), "nazwa", Path.GetFileName(p), "typ", Mime(p)));
            }
            return r;
        }

        static string Mime(string p)
        {
            switch (Path.GetExtension(p).ToLowerInvariant())
            {
                case ".png": return "image/png";
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".gif": return "image/gif";
                case ".pdf": return "application/pdf";
                case ".txt": case ".md": case ".log": return "text/plain";
                case ".json": return "application/json";
                case ".zip": return "application/zip";
                default: return "application/octet-stream";
            }
        }

        // ---------- spis gałęzi jako strona HTML+JS ----------
        // Dane: najnowszy SPIS-GALEZI-*.json, grupy A–F z opisu SPIS-GALEZI-*.md, bieżący stan katalogów
        // (istnieje / przeniesiony do usunięcia) z pól katalog_agentow i katalog_przeniesionych w projekty\*\projekt.json.
        void SpisGalezi()
        {
            try { System.Diagnostics.Process.Start(ZbudujSpis()); }
            catch (Exception ex) { MessageBox.Show(form, Jezyk.T("Nie udało się zbudować spisu: ") + ex.Message, Jezyk.T("Spis gałęzi")); }
        }

        public static string ZbudujSpis()
        {
            var plik = new DirectoryInfo(P.Root).GetFiles("SPIS-GALEZI-*.json").OrderByDescending(f => f.Name).FirstOrDefault();
            if (plik == null) throw new EcxpException("Brak pliku SPIS-GALEZI-*.json w katalogu skrzynki.");
            {
                var d = P.Js().Deserialize<Dictionary<string, object>>(P.Czytaj(plik.FullName));
                var grupy = new Dictionary<string, string>();
                var md = Path.ChangeExtension(plik.FullName, ".md");
                string akt = null;
                foreach (var l in P.Czytaj(md).Split('\n'))
                {
                    var h = Regex.Match(l, @"^## ([A-F])\. ");
                    if (h.Success) { akt = h.Groups[1].Value; continue; }
                    if (l.StartsWith("## ")) { akt = null; continue; }
                    var m = Regex.Match(l, @"^- `([^`]+)`");
                    if (akt != null && m.Success && !grupy.ContainsKey(m.Groups[1].Value)) grupy[m.Groups[1].Value] = akt;
                }
                var pr = P.Projekty().FirstOrDefault(x => P.S(x, "katalog_agentow").Length > 0);
                string kat = pr != null ? P.S(pr, "katalog_agentow") : "", przen = pr != null ? P.S(pr, "katalog_przeniesionych") : "";
                Func<string, string, bool> jest = (k, n) => k.Length > 0 && (Directory.Exists(Path.Combine(k, n)) || File.Exists(Path.Combine(k, n)));
                var pozycje = new List<object>();
                foreach (var o in (System.Collections.IEnumerable)d["agents"])
                {
                    var w = o as Dictionary<string, object>;
                    if (w == null) continue;
                    var nazwa = P.S(w, "katalog");
                    w["istnieje"] = jest(kat, nazwa);
                    w["przeniesiony"] = jest(przen, nazwa);
                    string g;
                    // pozycje opisane w spisie mają jego grupę; nieopisane w istniejącym spisie to grupa A (scalone i czyste)
                    w["grupa"] = grupy.TryGetValue(nazwa, out g) ? g : grupy.Count > 0 ? (P.S(w, "typ") == "WORKTREE" ? "E" : "A") : Grupa(w);
                    pozycje.Add(w);
                }
                var dane = P.D("plik", plik.Name, "wygenerowano", DateTime.Now.ToString("yyyy-MM-dd HH:mm"), "ciemny", Motyw.Ciemny, "pozycje", pozycje,
                    "glowne", d.ContainsKey("main_branches") ? d["main_branches"] : new object[0], "lokalny_elv3", P.S(d, "local_elv3"));
                string szablon;
                using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("spis-galezi.html"))
                using (var r = new StreamReader(st, Encoding.UTF8)) szablon = r.ReadToEnd();
                var html = szablon.Replace("/*__DANE__*/null", P.Js().Serialize(dane));
                var wy = Path.Combine(P.Root, "stan", "spis-galezi.html");
                Directory.CreateDirectory(Path.GetDirectoryName(wy));
                File.WriteAllText(wy, html, new UTF8Encoding(false));
                return wy;
            }
        }

        static string Grupa(Dictionary<string, object> w)
        {
            var typ = P.S(w, "typ");
            if (typ == "WORKTREE") return "E";
            if (typ == "PLIK" || typ == "BEZ_GIT") return "F";
            var gal = P.Lista(w, "galezie");
            if (Regex.IsMatch(P.S(w, "galaz") + " " + P.S(w, "katalog"), "gate-|review|recenz|audit|audyt|raport", RegexOptions.IgnoreCase)) return "C";
            if (gal.Any(g => P.S(g, "stan") != "W_GITHUB_ELV3" && P.S(g, "stan") != "W_LOKALNYM_ELV3")) return "D";
            int zm, nw, st;
            int.TryParse(P.S(w, "zmienione"), out zm); int.TryParse(P.S(w, "nowe"), out nw); int.TryParse(P.S(w, "stash"), out st);
            return zm + nw + st > 0 ? "B" : "A";
        }

        // ---------- decyzje, agenci w tle, wznowienie ----------
        void Decyzja()
        {
            var para = WybranaPara();
            string mod = para != null ? P.S(para, "modul") : "";
            var czekajace = Lancuchy.Wszystkie().Where(l => P.S(l, "stan") == "czeka_na_decyzje").Select(l => P.S(l, "id") + " — " + P.S(l, "tytul")).ToList();
            czekajace.Insert(0, "");
            using (var f = new Formularz("Decyzja operatora – podpisana", fontN, fontB))
            {
                var cbP = f.Combo("Projekt:", P.Projekty().Select(x => P.S(x, "nazwa")).ToList(), para != null ? P.S(para, "projekt") : (cbProjekt.SelectedIndex > 0 ? (string)cbProjekt.SelectedItem : ""));
                var tbM = f.Tekst("Moduł:", mod);
                var cbDo = f.Combo("Do:", new List<string> { mod.Length > 0 ? "para:" + mod : "", mod.Length > 0 ? "rola:integrator@" + mod : "" }.Where(x => x.Length > 0).ToList(), mod.Length > 0 ? "para:" + mod : "");
                var cbR = f.Combo("Rodzaj:", Decyzje.Rodzaje.ToList(), czekajace.Count > 1 ? "lancuch" : "user_accepted");
                var tbC = f.Tekst("Commit (pełny hash, 40 znaków):", "");
                var tbG = f.Tekst("Gałąź:", "");
                var cbL = f.Combo("Łańcuch czekający na decyzję:", czekajace, czekajace.Count > 1 ? czekajace[1] : "");
                var tbO = f.Wielowiersz("Co dokładnie wolno zrobić:", "");
                f.Uwaga("Decyzję podpisuje Twój klucz (RSA, chroniony przez Windows tylko dla Twojego konta). Agent sprawdza podpis (ecxp_sprawdz_decyzje) i wykonuje wyłącznie to, co tu opisano. USER_ACCEPTED, push i scalenie wymagają pełnego hasha commitu.");
                f.PrzyciskOk("⚖  Podpisz i wyślij");
                if (f.ShowDialog(form) != DialogResult.OK) return;
                var podsum = "Rodzaj: " + cbR.Text + (tbC.Text.Length > 0 ? "\nCommit: " + tbC.Text : "") + (tbG.Text.Length > 0 ? "\nGałąź: " + tbG.Text : "") +
                             "\nDo: " + (cbDo.Text.Length > 0 ? cbDo.Text : "(tylko łańcuch)") + (cbL.Text.Length > 0 ? "\nŁańcuch: " + cbL.Text : "") + "\n\n" + tbO.Text;
                if (MessageBox.Show(form, Jezyk.T("Podpisać i wysłać tę decyzję?\n\n") + podsum, Jezyk.T("Decyzja operatora"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                try
                {
                    var r = (Dictionary<string, object>)Stos("decyzja", P.D("projekt", cbP.Text, "modul", tbM.Text, "do", cbDo.Text, "rodzaj", cbR.Text, "commit", tbC.Text,
                        "galaz", tbG.Text, "opis", tbO.Text, "lancuch", cbL.Text.Split(' ')[0]));
                    stInfo.Text = Jezyk.T("Decyzja ") + P.S(r, "id") + Jezyk.T(" podpisana i wysłana.");
                    Skanuj();
                }
                catch (Exception ex) { MessageBox.Show(form, Jezyk.T("Nie wydano decyzji: ") + ex.Message, Jezyk.T("Decyzja"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        void UruchomAgenta() { UruchomAgenta("claude"); }

        void UruchomAgenta(string strona)
        {
            var para = WybranaPara();
            string projekt = para != null ? P.S(para, "projekt") : (cbProjekt.SelectedIndex > 0 ? (string)cbProjekt.SelectedItem : "");
            string mod = para != null ? P.S(para, "modul") : "";
            var pr = P.Projekty().FirstOrDefault(x => P.S(x, "nazwa") == projekt);
            using (var f = new Formularz("Agent w tle (Claude Code CLI / Codex CLI)", fontN, fontB))
            {
                var tbN = f.Tekst("Nazwa:", para != null && P.S(para, strona).Length > 0 ? P.S(para, strona) : strona + "-" + mod);
                var cbP = f.Combo("Projekt:", P.Projekty().Select(x => P.S(x, "nazwa")).ToList(), projekt);
                var tbM = f.Tekst("Moduł:", mod);
                var tbK = f.Tekst("Katalog pracy (klon lub worktree):", pr != null ? P.S(pr, "repozytorium").Replace('/', '\\') : "");
                var cbU = f.Combo("Uprawnienia:", Agenci.Uprawnienia.ToList(), "odczyt");
                var cbMo = f.Combo("Model (puste = domyślny):", new List<string> { "", "opus", "sonnet", "haiku" }, "");
                var tbB = f.Tekst("Najwięcej tur na godzinę:", "6");
                var rl = f.Role("Role:", new[] { "recenzent" });
                var tbO = f.Tekst("Opis od operatora:", "");
                f.Uwaga("Stos uruchamia agenta, gdy w jego kolejce czekają wiadomości – jedna tura naraz, w granicy limitu na godzinę. " +
                        "Claude: Claude Code CLI (" + (Agenci.Program("claude").Length > 0 ? "jest" : "BRAK") + " na tym stanowisku); Codex: Codex CLI (" + (Agenci.Program("codex").Length > 0 ? "jest" : "BRAK") + "). " +
                        "Uprawnienia: odczyt – czyta pliki i rozmawia przez ECXP; edycja – zmienia pliki w katalogu pracy; pelne – także polecenia systemowe. Push, scalenie i tagi tylko po podpisanej decyzji.");
                f.PrzyciskOk("▶  Uruchom agenta");
                if (f.ShowDialog(form) != DialogResult.OK) return;
                try
                {
                    Stos("uruchom_agenta", P.D("nazwa", tbN.Text, "projekt", cbP.Text, "modul", tbM.Text, "katalog", tbK.Text, "uprawnienia", cbU.Text, "model", cbMo.Text,
                        "na_godzine", tbB.Text, "role", Formularz.Zaznaczone(rl), "opis", tbO.Text));
                    stInfo.Text = Jezyk.T("Agent ") + tbN.Text + Jezyk.T(" działa w tle – odbierze wiadomości, gdy przyjdą.");
                    Skanuj();
                }
                catch (Exception ex) { MessageBox.Show(form, Jezyk.T("Nie uruchomiono: ") + ex.Message, Jezyk.T("Agent w tle"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        void Wznow()
        {
            var para = WybranaPara();
            if (para == null) { MessageBox.Show(form, Jezyk.T("Zaznacz parę na liście agentów."), Jezyk.T("Wznów")); return; }
            string pr = P.S(para, "projekt"), mod = P.S(para, "modul");
            try
            {
                if (Galezie.Wstrzymana(pr, mod))
                {
                    if (MessageBox.Show(form, Jezyk.T("Para ") + pr + "/" + mod + Jezyk.T(" jest wstrzymana po rozbieżności wspólnej gałęzi.\nWznowić ją (rozbieżność została rozwiązana)?"), Jezyk.T("Wznów parę"), MessageBoxButtons.YesNo) == DialogResult.Yes)
                        Stos("para_wznow", P.D("projekt", pr, "modul", mod, "uwagi", "wznowiona z konsoli"));
                }
                var lr = Lancuchy.Wszystkie().Where(l => P.S(l, "projekt") == pr && P.S(l, "modul") == mod && P.S(l, "stan") == "blad").FirstOrDefault();
                if (lr != null && MessageBox.Show(form, Jezyk.T("Łańcuch „") + P.S(lr, "tytul") + Jezyk.T("” stoi (") + P.S(lr, "stan") + Jezyk.T("). Wznowić bieżący krok?"), Jezyk.T("Wznów łańcuch"), MessageBoxButtons.YesNo) == DialogResult.Yes)
                    Stos("lancuch_wznow", P.D("id", P.S(lr, "id")));
                Skanuj();
            }
            catch (Exception ex) { MessageBox.Show(form, ex.Message, Jezyk.T("Wznów"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        // ---------- O programie / About ----------
        public const string StronaEvidsec = "https://evidsec.pl/", StronaEcxp = "https://evidsec.pl/ecxp/", RepozytoriumEcxp = "https://github.com/cybf1sh/ECXP-MAILBOX";

        void OProgramie()
        {
            var wersja = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            using (var f = new Form { Text = Jezyk.T("O programie") + " – ECXP-MAILBOX", Size = new Size(720, 600), MinimumSize = new Size(560, 460), StartPosition = FormStartPosition.CenterParent,
                Font = fontN, MinimizeBox = false, MaximizeBox = false, ShowInTaskbar = false })
            {
                var pan = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(22, 16, 22, 10) };
                var lg = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(230, 70), Image = logoBox.Image, Margin = new Padding(0, 0, 0, 8) };
                var szer = 640;
                Func<string, Font, Color, Label> lbl = (t, ft, kol) => new Label { Text = t, Font = ft, ForeColor = kol, AutoSize = true, MaximumSize = new Size(szer, 0), Margin = new Padding(0, 4, 0, 6) };
                pan.Controls.Add(lg);
                pan.Controls.Add(lbl("ECXP-MAILBOX " + wersja + " · ECXP " + P.Protokol, fontH, Motyw.Tekst));
                pan.Controls.Add(lbl("Evidsec Context eXchange Protocol", fontB, Motyw.Slaby));
                pan.Controls.Add(lbl(Jezyk.T("ECXP to uniwersalny, niezawodny i połączeniowy protokół wymiany wiadomości i kontekstu między agentami ChatGPT/Codex i Claude oraz człowiekiem-operatorem – dla dowolnego projektu i na dowolnym stanowisku."), fontN, Motyw.Tekst));
                pan.Controls.Add(lbl(Jezyk.T("Zasada 1:1: para Codex + Claude pracuje nad tym samym zadaniem i jedną wspólną gałęzią; to, co operator napisze jednej stronie – w oknie czatu albo w konsoli – dostaje także druga."), fontN, Motyw.Tekst));
                pan.Controls.Add(lbl(Jezyk.T("Niezawodność jak w TCP: ramki 72 B z SHA-256, uzgadnianie połączeń, potwierdzenia, ponowienia, alarm i dziennik odporny na zmiany. Kontekst: obiekty SHA-256, załączniki, łańcuchy ról, podpisane decyzje operatora."), fontN, Motyw.Tekst));
                pan.Controls.Add(lbl(Jezyk.T("ECXP-MAILBOX to konsola operatora ECXP: rozmowy agentów, pary i role, nowe zadania, agenci w tle (Claude Code CLI, Codex CLI) i decyzje."), fontN, Motyw.Tekst));
                foreach (var l in new[] { new[] { Jezyk.T("Strona Evidsec"), StronaEvidsec }, new[] { Jezyk.T("ECXP i ECXP-MAILBOX – opis"), StronaEcxp }, new[] { Jezyk.T("Kod źródłowy i dokumentacja (GitHub)"), RepozytoriumEcxp } })
                {
                    var adres = l[1];
                    var ll = new LinkLabel { Text = l[0] + ":  " + adres, AutoSize = true, Margin = new Padding(0, 6, 0, 2), LinkColor = Motyw.K(Color.FromArgb(0, 80, 200)) };
                    ll.LinkClicked += delegate { try { System.Diagnostics.Process.Start(adres); } catch { } };
                    pan.Controls.Add(ll);
                }
                pan.Controls.Add(lbl("© Evidsec · " + Jezyk.T("Katalog instalacji") + ": " + P.Root, fontS, Motyw.Slaby));
                var dol = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 48, Padding = new Padding(8) };
                var bz = new Button { Text = Jezyk.T("Zamknij"), AutoSize = true, Height = 32, DialogResult = DialogResult.OK };
                dol.Controls.Add(bz);
                f.Controls.Add(pan); f.Controls.Add(dol);
                f.AcceptButton = bz;
                Motyw.Zastosuj(f);
                f.ShowDialog(form);
            }
        }

        static string NazwaW(string n) { return n == P.Wlasciciel ? Jezyk.T("Ty") : P.Nazwa(n); }
        static readonly string[] FrazyOpisu = { "wysłana, jeszcze nie odebrana", "czeka na potwierdzenie", "potwierdzona", "dostarczona", "ponowienie", "ESKALACJA", "ZAGINIONA",
            "zadanie: ", "zgloszone", "w_toku", "potrzebne_dane", "zakonczone", "nieudane", "anulowane", "odrzucone", "obsluzona", "przyjeta", "(od " };
        static readonly string[] FrazyTresci = { " napisał do operatora:", "Operator napisał w czacie ", "Operator napisał do ", "[Przekazane po przejęciu modułu: wiadomość była do " };
        static string TTresc(string s)
        {
            if (Jezyk.Kod == "pl" || s == null) return s;
            foreach (var f in FrazyTresci) s = s.Replace(f, Jezyk.T(f));
            return s;
        }

        static string TOpis(string s)
        {
            if (Jezyk.Kod == "pl" || s == null) return s;
            foreach (var f in FrazyOpisu) s = s.Replace(f, Jezyk.T(f));
            return s;
        }

        // ---------- status agentów CLI (Claude Code CLI, ChatGPT/Codex CLI) ----------
        class StanCli { public string Nazwa = "", Sciezka = "", Wersja = "", Opis = ""; public bool Jest, Aktywne; }
        readonly Dictionary<string, StanCli> cli = new Dictionary<string, StanCli>();
        Label lblCliClaude;   // znacznik: widok usług zbudowany
        Panel naglowek;
        FlowLayoutPanel pnlPasek;
        TableLayoutPanel pnlKarty;
        string widokUslug = "pasek";
        readonly Dictionary<string, WidokUslugi> pasek = new Dictionary<string, WidokUslugi>(), karty = new Dictionary<string, WidokUslugi>();
        readonly Dictionary<string, Dictionary<string, object>> uslugi = new Dictionary<string, Dictionary<string, object>>();
        bool stosBezUslug;
        DateTime ostatnieUslugi = DateTime.MinValue;

        class WidokUslugi
        {
            public Label Tytul, Stan, Opis;
            public Button Start, Stop, Reset, Agent;
            public Panel Karta;
            public Color Kolor = Color.Gray;
        }
        ToolStripStatusLabel stCli;
        DateTime czujDo = DateTime.MinValue, ostatnieCli = DateTime.MinValue;
        volatile bool sprawdzamCli;
        readonly ToolTip podpowiedzCli = new ToolTip();
        ComboBox cbJezyk;

        static string WykonajCli(string prog, string args, int ms)
        {
            try
            {
                var psi = prog.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? new System.Diagnostics.ProcessStartInfo(prog, args)
                    : new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /s /c \"\"" + prog + "\" " + args + "\"");
                psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true; psi.RedirectStandardInput = true;
                psi.StandardOutputEncoding = Encoding.UTF8;
                using (var pr = System.Diagnostics.Process.Start(psi))
                {
                    pr.StandardInput.Close();
                    var t = pr.StandardOutput.ReadToEndAsync(); var e = pr.StandardError.ReadToEndAsync();
                    if (!pr.WaitForExit(ms)) { try { pr.Kill(); } catch { } return ""; }
                    return t.Result + "\n" + e.Result;
                }
            }
            catch { return ""; }
        }

        static StanCli SprawdzCli(string nazwa)
        {
            var s = new StanCli { Nazwa = nazwa, Sciezka = Agenci.Program(nazwa) };
            s.Jest = s.Sciezka.Length > 0;
            if (!s.Jest) { s.Opis = "nie zainstalowane"; return s; }
            s.Wersja = WykonajCli(s.Sciezka, "--version", 30000).Trim().Split('\n')[0].Trim();
            if (nazwa == "claude") s.Aktywne = Regex.IsMatch(WykonajCli(s.Sciezka, "auth status", 30000), "\"loggedIn\"\\s*:\\s*true");
            else { var o = WykonajCli(s.Sciezka, "login status", 30000); s.Aktywne = o.IndexOf("Logged in", StringComparison.OrdinalIgnoreCase) >= 0 && o.IndexOf("Not logged in", StringComparison.OrdinalIgnoreCase) < 0; }
            s.Opis = s.Aktywne ? "aktywne" : "niezalogowane";
            return s;
        }

        void SprawdzCliTlo()
        {
            if (sprawdzamCli) return;
            sprawdzamCli = true;
            new Thread(() =>
            {
                try
                {
                    var a = SprawdzCli("claude"); var b = SprawdzCli("codex");
                    form.BeginInvoke((Action)(() => { cli["claude"] = a; cli["codex"] = b; PokazCli(); }));
                }
                catch { }
                finally { sprawdzamCli = false; }
            }) { IsBackground = true, Name = "ecxp-status-cli" }.Start();
        }

        void PokazCli()
        {
            if (lblCliClaude == null) return;
            var krotko = new List<string>();
            foreach (var n in new[] { "claude", "codex" })
            {
                StanCli c; cli.TryGetValue(n, out c);
                Dictionary<string, object> u; uslugi.TryGetValue(n, out u);
                Color kol; string stan = StanUslugi(c, u, out kol);
                string szczegoly = SzczegolyUslugi(c, u);
                bool cliOk = c != null && c.Aktywne;
                bool zatrz = u != null && P.B(u, "zatrzymana");
                bool wToku = u != null && P.Lista(u, "w_toku").Count > 0;
                string tStart = c == null ? Jezyk.T("sprawdzam…") : !c.Jest ? Jezyk.T("Zainstaluj…") : !c.Aktywne ? Jezyk.T("Zaloguj (w tle)") : "▶  " + Jezyk.T("Start");
                foreach (var w in new[] { pasek[n], karty[n] })
                {
                    w.Kolor = kol;
                    w.Stan.Text = "● " + stan;
                    w.Stan.ForeColor = kol;
                    w.Start.Text = tStart;
                    w.Start.Enabled = c != null && (!cliOk || zatrz || u == null);
                    w.Stop.Enabled = cliOk && !zatrz && u != null;
                    w.Reset.Enabled = cliOk && u != null;
                    if (w.Agent != null) w.Agent.Enabled = cliOk;
                    if (w.Opis != null) w.Opis.Text = szczegoly;
                    podpowiedzCli.SetToolTip(w.Stan, szczegoly);
                    if (w.Karta != null) w.Karta.Invalidate();
                }
                // w pasku przycisk Start po uruchomieniu usługi otwiera nowego agenta w tle
                if (cliOk && !zatrz && u != null) { pasek[n].Start.Text = Jezyk.T("＋ Agent…"); pasek[n].Start.Enabled = true; }
                krotko.Add((n == "claude" ? "Claude" : "Codex") + " " + (cliOk && !zatrz ? "✓" : "✗"));
            }
            if (stCli == null) return;
            stCli.Text = "CLI: " + string.Join(" · ", krotko);
            stCli.ForeColor = cli.Count == 2 && cli.Values.All(x => x.Aktywne) ? Zielony : Motyw.K(Color.FromArgb(176, 106, 0));
        }

        string StanUslugi(StanCli c, Dictionary<string, object> u, out Color kol)
        {
            kol = Motyw.Slaby;
            if (c == null) return Jezyk.T("sprawdzam…");
            if (!c.Jest) { kol = Czerwony; return Jezyk.T("brak CLI"); }
            if (!c.Aktywne) { kol = Motyw.K(Color.FromArgb(176, 106, 0)); return Jezyk.T("niezalogowane"); }
            if (u == null) { kol = Zielony; return Jezyk.T("aktywne"); }
            if (P.B(u, "zatrzymana")) { kol = Czerwony; return Jezyk.T("zatrzymana"); }
            if (P.Lista(u, "w_toku").Count > 0) { kol = Motyw.K(Color.FromArgb(0, 90, 200)); return Jezyk.T("tura w toku"); }
            int akt; int.TryParse(P.S(u, "aktywni"), out akt);
            kol = Zielony;
            return akt > 0 ? Jezyk.T("działa") : Jezyk.T("gotowa – brak agentów");
        }

        string SzczegolyUslugi(StanCli c, Dictionary<string, object> u)
        {
            var cz = new List<string>();
            if (c != null && c.Wersja.Length > 0) cz.Add(Regex.Replace(c.Wersja, @"\s*\(.*\)$", ""));
            if (c != null && c.Jest) cz.Add(Jezyk.T(c.Aktywne ? "zalogowane" : "niezalogowane"));
            if (u != null)
            {
                cz.Add(Jezyk.T("agenci") + " " + P.S(u, "aktywni") + "/" + P.S(u, "agenci"));
                DateTimeOffset t;
                if (DateTimeOffset.TryParse(P.S(u, "ostatnia_tura"), out t))
                    cz.Add(Jezyk.T("ostatnia tura") + " " + t.LocalDateTime.ToString("HH:mm") + " (" + (P.S(u, "ostatni_kod") == "0" ? "OK" : Jezyk.T("błąd")) + ")");
                else cz.Add(Jezyk.T("bez tur"));
                cz.Add(Jezyk.T("tur/h") + " " + P.S(u, "tury_godzina"));
            }
            else if (stosBezUslug) cz.Add(Jezyk.T("stos bez obsługi usług – uruchom go ponownie"));
            return string.Join("  ·  ", cz);
        }

        void OdswiezUslugi()
        {
            if (klient == null || !klient.Polaczony || stosBezUslug) return;
            try
            {
                var r = (Dictionary<string, object>)Stos("uslugi", null);
                foreach (var n in new[] { "claude", "codex" }) uslugi[n] = (Dictionary<string, object>)r[n];
                PokazCli();
            }
            catch (EcxpException ex) { if (ex.Message.Contains("Nieznane polecenie")) { stosBezUslug = true; PokazCli(); } }
            catch { }
        }

        void AkcjaUslugi(string n, string akcja)
        {
            var nazwa = n == "claude" ? "Claude Code CLI" : "ChatGPT/Codex CLI";
            Dictionary<string, object> u; uslugi.TryGetValue(n, out u);
            if (akcja == "start")
            {
                StanCli c; cli.TryGetValue(n, out c);
                if (c == null || !c.Aktywne) { UruchomCli(n); return; }
            }
            if (akcja != "start" && u != null && P.Lista(u, "w_toku").Count > 0 &&
                MessageBox.Show(form, Jezyk.T("Trwa tura agenta w tle. Przerwać ją?"), nazwa, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            try
            {
                var r = (Dictionary<string, object>)Stos("usluga", P.D("strona", n, "akcja", akcja));
                uslugi[n] = r;
                int ile; int.TryParse(P.S(r, "przerwane_tury"), out ile);
                stInfo.Text = nazwa + ": " + Jezyk.T(akcja == "stop" ? "usługa zatrzymana" : akcja == "start" ? "usługa uruchomiona" : "usługa uruchomiona ponownie") +
                              (ile > 0 ? " – " + Jezyk.T("przerwane tury:") + " " + ile : "") + ".";
                if (akcja == "restart") { ostatnieCli = DateTime.MinValue; czujDo = DateTime.Now.AddMinutes(1); }
                PokazCli();
            }
            catch (Exception ex) { MessageBox.Show(form, ex.Message, nazwa, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        void BudujUslugi(Panel head)
        {
            // wariant A: pasek w nagłówku
            pnlPasek = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Location = new Point(192, 58), Padding = new Padding(0), BackColor = Color.Transparent };
            foreach (var n in new[] { "claude", "codex" })
            {
                var w = new WidokUslugi();
                w.Stan = new Label { AutoSize = true, Font = fontB, Margin = new Padding(n == "claude" ? 6 : 16, 9, 2, 0) };
                w.Tytul = new Label { AutoSize = true, Font = fontB, Margin = new Padding(n == "claude" ? 6 : 16, 9, 0, 0), Text = n == "claude" ? "Claude Code:" : "Codex:" };
                w.Stan.Margin = new Padding(2, 9, 4, 0);
                w.Start = Btn("▶  Start"); w.Stop = Btn("■  Stop"); w.Reset = Btn("↻  Restart");
                foreach (var b in new[] { w.Start, w.Stop, w.Reset }) b.Margin = new Padding(2, 3, 2, 3);
                podpowiedzCli.SetToolTip(w.Stop, Jezyk.T("Zatrzymaj usługę: przerywa trwającą turę i wstrzymuje agentów w tle tej strony."));
                podpowiedzCli.SetToolTip(w.Reset, Jezyk.T("Uruchom ponownie: przerywa turę, wznawia usługę i kasuje przerwę po błędzie."));
                pnlPasek.Controls.AddRange(new Control[] { w.Tytul, w.Stan, w.Start, w.Stop, w.Reset });
                Podepnij(w, n);
                pasek[n] = w;
            }
            head.Controls.Add(pnlPasek);
            lblCliClaude = pasek["claude"].Stan;

            // wariant B: dwie karty usług pod nagłówkiem
            pnlKarty = new TableLayoutPanel { Dock = DockStyle.Top, Height = 112, ColumnCount = 2, RowCount = 1, Padding = new Padding(8, 6, 8, 6) };
            pnlKarty.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            pnlKarty.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            foreach (var n in new[] { "claude", "codex" })
            {
                var w = new WidokUslugi();
                var karta = w.Karta = new Panel { Dock = DockStyle.Fill, Margin = new Padding(4), BackColor = Color.White };
                var ww = w;
                karta.Paint += (o, e) =>
                {
                    var k = (Panel)o;
                    using (var pen = new Pen(Motyw.Ramka)) e.Graphics.DrawRectangle(pen, 0, 0, k.Width - 1, k.Height - 1);
                    using (var br = new SolidBrush(ww.Kolor)) e.Graphics.FillRectangle(br, 0, 0, 6, k.Height);
                };
                w.Tytul = new Label { AutoSize = true, Font = new Font(fontB.FontFamily, fontB.Size + 1.5f, FontStyle.Bold), Location = new Point(18, 8),
                    Text = n == "claude" ? "Claude Code CLI" : "ChatGPT / Codex CLI", BackColor = Color.Transparent };
                w.Stan = new Label { AutoSize = true, Font = new Font(fontB.FontFamily, fontB.Size + 1.5f, FontStyle.Bold), BackColor = Color.Transparent };
                w.Opis = new Label { AutoSize = true, Location = new Point(19, 36), ForeColor = Color.DimGray, BackColor = Color.Transparent };
                var przyc = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Location = new Point(14, 58), BackColor = Color.Transparent };
                w.Start = Btn("▶  Start"); w.Stop = Btn("■  Stop"); w.Reset = Btn("↻  Uruchom ponownie"); w.Agent = Btn("＋  Agent w tle…");
                przyc.Controls.AddRange(new Control[] { w.Start, w.Stop, w.Reset, w.Agent });
                karta.Controls.AddRange(new Control[] { w.Tytul, w.Stan, w.Opis, przyc });
                karta.Layout += delegate { ww.Stan.Location = new Point(ww.Tytul.Right + 14, 8); };
                Podepnij(w, n);
                pnlKarty.Controls.Add(karta, n == "claude" ? 0 : 1, 0);
                karty[n] = w;
            }

            // przełącznik widoku: prawy klawisz na pasku albo na karcie
            var menuWidok = new ContextMenuStrip();
            menuWidok.Opening += delegate
            {
                menuWidok.Items.Clear();
                menuWidok.Items.Add(new ToolStripMenuItem(Jezyk.T("Usługi jako pasek (A)"), null, delegate { UstawWidokUslug("pasek"); }) { Checked = widokUslug == "pasek" });
                menuWidok.Items.Add(new ToolStripMenuItem(Jezyk.T("Usługi jako karty (B)"), null, delegate { UstawWidokUslug("karty"); }) { Checked = widokUslug == "karty" });
                menuWidok.Items.Add(new ToolStripSeparator());
                menuWidok.Items.Add(Jezyk.T("Sprawdź status teraz"), null, delegate { ostatnieCli = DateTime.Now; SprawdzCliTlo(); OdswiezUslugi(); });
            };
            pnlPasek.ContextMenuStrip = menuWidok; pnlKarty.ContextMenuStrip = menuWidok;
            foreach (var w in karty.Values) { w.Karta.ContextMenuStrip = menuWidok; foreach (Control c in w.Karta.Controls) c.ContextMenuStrip = menuWidok; }
            foreach (Control c in pnlPasek.Controls) c.ContextMenuStrip = menuWidok;
            UstawWidokUslug(widokUslug, false);
        }

        void Podepnij(WidokUslugi w, string n)
        {
            w.Start.Click += delegate
            {
                Dictionary<string, object> u; uslugi.TryGetValue(n, out u);
                StanCli c; cli.TryGetValue(n, out c);
                if (w.Agent == null && c != null && c.Aktywne && u != null && !P.B(u, "zatrzymana")) UruchomAgenta(n);
                else AkcjaUslugi(n, "start");
            };
            w.Stop.Click += delegate { AkcjaUslugi(n, "stop"); };
            w.Reset.Click += delegate { AkcjaUslugi(n, "restart"); };
            if (w.Agent != null) w.Agent.Click += delegate { UruchomAgenta(n); };
        }

        void UstawWidokUslug(string widok, bool zapisz = true)
        {
            widokUslug = widok;
            pnlPasek.Visible = widok == "pasek";
            pnlKarty.Visible = widok == "karty";
            naglowek.Height = widok == "pasek" ? 100 : 64;
            if (zapisz) ZapiszUstawienia();
            PokazCli();
        }

        // Usługi CLI działają w tle, bez okien konsoli:
        //  * brak CLI – instalacja przez npm w tle (po potwierdzeniu), postęp w stan\cli-<nazwa>.log;
        //  * CLI bez logowania – logowanie w tle (otwiera się tylko strona logowania w przeglądarce);
        //  * CLI aktywne – agent w tle jako usługa stosu (okno „Agent w tle”).
        void UruchomCli(string n)
        {
            StanCli s; cli.TryGetValue(n, out s);
            var nazwa = n == "claude" ? "Claude Code CLI" : "ChatGPT/Codex CLI";
            try
            {
                if (s != null && s.Aktywne) { UruchomAgenta(n); return; }
                if (s == null || !s.Jest)
                {
                    var pakiet = n == "claude" ? "@anthropic-ai/claude-code" : "@openai/codex";
                    if (MessageBox.Show(form, Jezyk.T("Zainstalować przez npm (Node.js)?") + "\n\n" + nazwa + ": npm install -g " + pakiet + "\n\n" +
                        Jezyk.T("Instalacja przebiegnie w tle, bez okna konsoli. Potem kliknij Zaloguj – otworzy się strona logowania w przeglądarce."),
                        nazwa, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    UkrytyProces(n, Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /c npm install -g " + pakiet);
                    stInfo.Text = nazwa + ": " + Jezyk.T("instalacja w tle – status odświeża się co 10 s.");
                }
                else
                {
                    UkrytyProces(n, s.Sciezka, n == "claude" ? "auth login" : "login");
                    stInfo.Text = nazwa + ": " + Jezyk.T("logowanie w tle – dokończ je na stronie, która otworzyła się w przeglądarce.");
                }
                czujDo = DateTime.Now.AddMinutes(5);
            }
            catch (Exception ex) { MessageBox.Show(form, ex.Message, nazwa, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        void UkrytyProces(string n, string prog, string args)
        {
            var psi = prog.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? new System.Diagnostics.ProcessStartInfo(prog, args)
                : new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /s /c \"\"" + prog + "\" " + args + "\"");
            psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            psi.RedirectStandardOutput = true; psi.RedirectStandardError = true; psi.WorkingDirectory = P.Root;
            var log = Path.Combine(P.Root, "stan", "cli-" + n + ".log");
            Directory.CreateDirectory(Path.GetDirectoryName(log));
            var pr = new System.Diagnostics.Process { StartInfo = psi, EnableRaisingEvents = true };
            System.Diagnostics.DataReceivedEventHandler zapis = (o, e) => { if (e.Data != null) try { File.AppendAllText(log, e.Data + Environment.NewLine, new UTF8Encoding(false)); } catch { } };
            pr.OutputDataReceived += zapis; pr.ErrorDataReceived += zapis;
            pr.Exited += delegate { try { form.BeginInvoke((Action)(() => { ostatnieCli = DateTime.MinValue; })); } catch { } };
            File.AppendAllText(log, "==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + args + Environment.NewLine, new UTF8Encoding(false));
            pr.Start(); pr.BeginOutputReadLine(); pr.BeginErrorReadLine();
        }

        void ZapiszUstawienia()
        {
            try { File.WriteAllText(UstawieniaPlik, "motyw=" + (Motyw.Ciemny ? "ciemny" : "jasny") + Environment.NewLine + "jezyk=" + Jezyk.Kod + Environment.NewLine + "uslugi=" + widokUslug + Environment.NewLine, new UTF8Encoding(false)); } catch { }
        }

        // ---------- motyw ----------
        void UstawLogo()
        {
            var nazwa = Motyw.Ciemny ? "ecxp-logo-dark.png" : "ecxp-logo-light.png";
            var lp = Path.Combine(P.Root, "logo", nazwa);
            if (!File.Exists(lp)) lp = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "..", "logo", nazwa);
            if (File.Exists(lp)) logoBox.Image = Image.FromStream(new MemoryStream(File.ReadAllBytes(lp)));
        }

        void PrzelaczMotyw()
        {
            Motyw.Ciemny = !Motyw.Ciemny;
            ZapiszUstawienia();
            bMotyw.Text = Motyw.Ciemny ? Jezyk.T("☀  Motyw jasny") : Jezyk.T("☾  Motyw ciemny");
            UstawLogo();
            Motyw.Zastosuj(form); Motyw.Zastosuj(pop); Jezyk.Zastosuj(form); Jezyk.Zastosuj(pop);
            RefreshView();
            var m = Wybrana(); if (m != null) PokazWatek(m);
            StanStosu();
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
                var hay = (m.Od + " " + m.Do + " " + m.Modul + " " + m.Temat + " " + m.Uwagi + " " + m.Commit + " " + NazwaW(m.Od) + " " + NazwaW(m.Do));
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
            tabStan.Text = !read.Contains(StanKey()) ? Jezyk.T("Stan modułów (zmieniony)") : Jezyk.T("Stan modułów");

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
                var stanW = w.Any(m => m.Blad != null) ? Jezyk.T("⚠ uszkodzona wiadomość")
                    : w.Any(m => m.Dor != null && (m.Dor.Eskalacja || m.Dor.Zaginiona)) ? Jezyk.T("⚠ brak potwierdzenia – eskalacja")
                    : czekaj > TimeSpan.Zero ? Jezyk.T("⏳ czeka od ") + P.Wiek(czekaj)
                    : Jezyk.T("✓ zamknięta");
                var g = new ListViewGroup(w[0].Watek, P.Baza(w[0].Temat) + "   ·   " + string.Join(", ", w.Select(m => m.Modul).Where(x => x.Length > 0).Distinct()) +
                    "   ·   " + w.Count + Jezyk.T(" wiad.   ·   ") + stanW + (w.Any(m => m.Nowa) ? Jezyk.T("   ·   NOWE") : ""));
                lv.Groups.Add(g);
                foreach (var m in w)
                {
                    var it = new ListViewItem(m.Czas.ToString("dd.MM HH:mm"), g) { Tag = m };
                    it.SubItems.Add(m.Nowa ? "●" : "");
                    it.SubItems.Add(NazwaW(m.Od) + "  →  " + NazwaW(m.Do));
                    it.SubItems.Add(m.Modul);
                    it.SubItems.Add(Zdanie(m));
                    it.SubItems.Add(Doreczenie(m, teraz));
                    it.ForeColor = Kolor(m);
                    if (m.Nowa) it.Font = fontB;
                    var c = Czeka(m, teraz);
                    if (m.Blad != null || (m.Dor != null && (m.Dor.Eskalacja || m.Dor.Zaginiona))) it.BackColor = Motyw.Ostrz(Color.FromArgb(255, 205, 205));
                    else if (c.HasValue && c.Value.TotalHours >= 6) it.BackColor = Motyw.Ostrz(Color.FromArgb(255, 220, 220));
                    else if (c.HasValue && c.Value.TotalHours >= 1) it.BackColor = Motyw.Ostrz(Color.FromArgb(255, 242, 200));
                    lv.Items.Add(it);
                    widoczne.Add(m);
                    if (m.Plik == selPlik) it.Selected = true;
                }
            }
            lv.EndUpdate();

            RefreshPolaczenia(teraz);
            RefreshAgenci();
            Jezyk.Zastosuj(form);
            int nowe = msgs.Count(m => m.Nowa);
            int wKolejce = msgs.Count(m => m.Katalog == "do-codex" || m.Katalog == "do-claude");
            int bezOdp = msgs.Count(m => m.WymagaOdp && !m.Odpowiedziano);
            stInfo.Text = string.Format(Jezyk.T("Nowe dla Ciebie: {0}   ·   w kolejkach adresatów: {1}   ·   pytań bez odpowiedzi: {2}   ·   dziennik: {3}   ·   {4:HH:mm:ss}"),
                nowe, wKolejce, bezOdp, spojnosc.Replace("spójny, wpisów:", Jezyk.T("spójny, wpisów:")).Replace("pusty (brak zdarzeń)", Jezyk.T("pusty (brak zdarzeń)")).Replace("NARUSZONY", Jezyk.T("NARUSZONY")), teraz);
            stInfo.ForeColor = spojnosc.StartsWith("NARUSZONY") ? Motyw.K(Color.FromArgb(200, 0, 0)) : SystemColors.ControlText;
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
                    if (w == null) ocena = Jezyk.T("brak sygnału ECXP – automatyzacja nie używa jeszcze protokołu");
                    else if ((teraz - w.Ostatnio).TotalMinutes > 25) ocena = "⚠ milczy od " + P.Wiek(teraz - w.Ostatnio) + " (powinna co 10 min)";
                    else ocena = Jezyk.T("✓ działa (automatyzacja co 10 min)");
                }
                else if (!kontakt.HasValue) ocena = Jezyk.T("jeszcze się nie odezwał");
                else if (czekaNa > 0) ocena = "ma " + czekaNa + " nieodebranych – czyta między etapami";
                else ocena = Jezyk.T("✓ bez zaległości");
                var it = new ListViewItem(NazwaW(n) + "  (" + n + ")");
                it.SubItems.Add(kontakt.HasValue ? kontakt.Value.ToString("dd.MM HH:mm") + "  (" + P.Wiek(teraz - kontakt.Value) + " temu)" : "—");
                it.SubItems.Add(w != null ? w.Akcja : "");
                it.SubItems.Add(czekaNa.ToString());
                it.SubItems.Add(niepotw.ToString());
                it.SubItems.Add(ocena);
                if (ocena.StartsWith("⚠")) it.ForeColor = Motyw.K(Color.FromArgb(200, 0, 0));
                else if (ocena.StartsWith("✓")) it.ForeColor = Motyw.K(Color.FromArgb(0, 128, 0));
                lvPol.Items.Add(it);
            }
            lvPol.EndUpdate();
            lblPol.Text = "Węzły ECXP. Każde odebranie, wysłanie i potwierdzenie przez narzędzia ECXP zostawia sygnał życia.\r\n" +
                "Dziennik zdarzeń: " + spojnosc + "   (" + Path.Combine(P.Root, "dziennik", "dziennik.jsonl") + ")";

            // pasek: sygnał koordynacji
            Wezel k; wezly.TryGetValue(P.Koordynacja, out k);
            if (k == null) { stSyg.Text = Jezyk.T("Codex · koordynacja: brak sygnału ECXP"); stSyg.ForeColor = Motyw.Slaby; }
            else
            {
                var wiek = teraz - k.Ostatnio;
                if (wiek.TotalMinutes > 25)
                {
                    stSyg.Text = Jezyk.T("⚠ Codex · koordynacja milczy od ") + P.Wiek(wiek); stSyg.ForeColor = Motyw.K(Color.FromArgb(200, 0, 0));
                    if (!ciszaPokazana)
                    {
                        ciszaPokazana = true;
                        ShowPop(Jezyk.T("Codex · koordynacja milczy"), Jezyk.T("Ostatni sygnał ") + k.Ostatnio.ToString(Jezyk.T("HH:mm")) + " (" + P.Wiek(wiek) + Jezyk.T(" temu). Automatyzacja co 10 min mogła stanąć – zajrzyj do czatu koordynacji w Codexie."), new List<Msg>(), false);
                    }
                }
                else { stSyg.Text = Jezyk.T("Codex · koordynacja: ✓ ") + k.Ostatnio.ToString(Jezyk.T("HH:mm")); stSyg.ForeColor = Motyw.K(Color.FromArgb(0, 128, 0)); ciszaPokazana = false; }
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
                    catch (Exception ex) { stInfo.Text = Jezyk.T("Nie udało się potwierdzić ") + m.Plik + ": " + ex.Message; }
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
                        adresaci.Add(n + " — " + (P.S(w, "opis").Length > 0 ? P.S(w, "opis") : NazwaW(n)) + " [" + P.S(pr, "nazwa") + "]");
                }
            foreach (var k in P.Karty())
            {
                var n = P.S(k, "nazwa");
                if (P.PoprawnyNadawca(n) && !adresaci.Any(a => a.StartsWith(n + " "))) adresaci.Add(n + " — " + (P.S(k, "opis").Length > 0 ? P.S(k, "opis") : NazwaW(n)));
            }
            foreach (var n in msgs.SelectMany(m => new[] { m.Od, m.Do }).Concat(P.Wezly().Select(w => w.Nazwa)).Distinct().OrderBy(x => x))
                if (P.PoprawnyNadawca(n) && n != "codex-core" && !adresaci.Any(a => a.StartsWith(n + " ")))
                    adresaci.Add(n + " — " + NazwaW(n) + " (z historii kanału)");
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
                    f.Text = "Odpowiedz – " + NazwaW(komu) + " · " + P.Baza(odp.Temat);
                    f.cbTyp.SelectedIndex = 3;   // odpowiedź
                }
                if (f.ShowDialog(form) != DialogResult.OK) return;
                try
                {
                    var czesci = WyslijZalaczniki(f.zal.Pliki);
                    var r = (Dictionary<string, object>)Stos("wyslij", P.D("czesci", czesci, "do", f.Adresat, "projekt", f.Projekt, "modul", f.Modul, "typ", f.Typ,
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
                    stInfo.Text = Jezyk.T("Wysłano do ") + string.Join(" + ", ((System.Collections.IEnumerable)r["adresaci"]).Cast<object>()) + (ileK > 0 ? Jezyk.T(" (kopia dla pary: ") + ileK + ")" : "") +
                        (termin > 0 ? ", termin potwierdzenia " + termin + " min" : "");
                    ShowPop(Jezyk.T("Wysłano: ") + NazwaW(f.Adresat), Jezyk.T("Temat ") + P.Baza(f.tbTemat.Text.Length > 0 ? f.tbTemat.Text : (string)r["id"]) +
                        (termin > 0 ? ". Jeśli adresat nie potwierdzi w " + termin + " min, kanał ponowi i zaalarmuje." : "."), new List<Msg>(), false);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(form, Jezyk.T("Nie wysłano: ") + ex.Message, Jezyk.T("Napisz"), MessageBoxButtons.OK, MessageBoxIcon.Error);
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
                it.SubItems.Add(cx.Length > 0 ? ZnacznikEcxp + "  " + cx : Jezyk.T("— brak —"));
                it.SubItems.Add(cl.Length > 0 ? ZnacznikEcxp + "  " + cl : Jezyk.T("— brak —"));
                it.SubItems.Add(pelna ? Jezyk.T("✓ para 1:1") : Jezyk.T("⚠ niepełna"));
                it.SubItems.Add(string.Join("; ", role));
                it.SubItems.Add(TytulPary(pr, mod));
                var gal = Galezie.Stan(pr, mod);
                var um = gal.ContainsKey("umowa") ? gal["umowa"] as Dictionary<string, object> : null;
                var rozb = P.S(gal, "para_stan") == "rozbieznosc";
                it.SubItems.Add(rozb ? Jezyk.T("⛔ ROZBIEŻNOŚĆ – para wstrzymana") : um == null ? Jezyk.T("brak umowy") : P.S(um, "galaz") + " (" + P.S(um, "stan") + ")");
                var lr = Lancuchy.Wszystkie().Where(l => P.S(l, "projekt") == pr && P.S(l, "modul") == mod && P.S(l, "stan") != "zakonczony")
                    .OrderByDescending(l => P.S(l, "id")).FirstOrDefault();
                if (lr != null)
                {
                    var kr = P.L(lr, "kroki"); int k = Convert.ToInt32(lr["krok"]);
                    it.SubItems.Add(Jezyk.T("krok ") + (k + 1) + "/" + kr.Count + ": " + (k < kr.Count ? kr[k] : "—") + " · " + P.S(lr, "stan"));
                }
                else it.SubItems.Add("—");
                var wTle = Agenci.Lista().Where(a => P.B(a, "aktywny")).Select(a => P.S(a, "nazwa")).ToList();
                if (wTle.Contains(cx)) it.SubItems[2].Text += Jezyk.T("  (w tle)");
                if (wTle.Contains(cl)) it.SubItems[3].Text += Jezyk.T("  (w tle)");
                it.ForeColor = pelna && !rozb ? Zielony : Czerwony;
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
                var zalN = f.Zalaczniki("Załączniki:");
                var tbL = f.Tekst("Łańcuch ról (puste = bez):", "");
                f.Uwaga("Łańcuch, np.: programista, tester, recenzent, decyzja, integrator – stos sam przekazuje zadanie kolejnym rolom; „decyzja” czeka na Twoją podpisaną decyzję. Puste pole: zadanie trafia od razu do obu stron pary.");
                f.Uwaga("ECXP zarejestruje parę o tych samych nazwach: codex-<moduł> i claude-<moduł>, ze znacznikiem " + ZnacznikEcxp + ", wyśle zadanie do obu i przygotuje pakiety startowe.");
                tbM.TextChanged += delegate { tbT.Text = tbM.Text.Trim(); };
                f.PrzyciskOk("✉  Wyślij do pary (Codex + Claude)");
                if (f.ShowDialog(form) != DialogResult.OK) return;
                try
                {
                    var r = (Dictionary<string, object>)Stos("nowe_zadanie", P.D("projekt", cbP.Text, "modul", tbM.Text, "tytul", tbT.Text, "opis", tbO.Text,
                        "role_codex", Formularz.Zaznaczone(rCx), "role_claude", Formularz.Zaznaczone(rCl), "czesci", WyslijZalaczniki(zalN.Pliki),
                        "lancuch", tbL.Text.Split(new[] { ',', ';', '→', '>' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x.Length > 0).ToList()));
                    Skanuj();
                    var pk = (Dictionary<string, object>)r["pakiety"];
                    PokazPakiety("Para utworzona: " + P.S(r, "tytul_sesji"), (Dictionary<string, object>)pk["codex"], (Dictionary<string, object>)pk["claude"]);
                }
                catch (Exception ex) { MessageBox.Show(form, Jezyk.T("Nie utworzono: ") + ex.Message, Jezyk.T("Nowe zadanie"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
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
                f.PrzyciskOk("✉  Zapisz i wyślij karty ról");
                f.Uwaga("Sesja dostanie kartę każdej roli jako kontekst. Nowa sesja tej samej strony w module przejmuje adres i niepotwierdzone wiadomości poprzedniej.");
                if (f.ShowDialog(form) != DialogResult.OK) return;
                try
                {
                    Stos("rola", P.D("nazwa", cbN.Text.Trim(), "projekt", cbP.Text, "modul", tbM.Text, "role", Formularz.Zaznaczone(rl), "opis", tbO.Text));
                    Skanuj();
                    stInfo.Text = Jezyk.T("Przypisano ") + cbN.Text + Jezyk.T(" do ") + cbP.Text + "/" + tbM.Text;
                }
                catch (Exception ex) { MessageBox.Show(form, Jezyk.T("Nie przypisano: ") + ex.Message, Jezyk.T("Role"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        void PakietyStartowe()
        {
            var para = WybranaPara();
            if (para == null) { MessageBox.Show(form, Jezyk.T("Zaznacz parę na liście agentów."), Jezyk.T("Pakiety startowe")); return; }
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
                var why = new StringBuilder(Jezyk.T("Nic nie przeniesiono."));
                if (mlode > 0) why.Append(Jezyk.T("\n\n• ") + mlode + Jezyk.T(" przeczytanych jest młodszych niż wybrany wiek – wybierz „wszystkie przeczytane”."));
                if (nowe > 0) why.Append(Jezyk.T("\n\n• ") + nowe + Jezyk.T(" nie oznaczyłeś jeszcze jako przeczytane."));
                if (otwarte > 0) why.Append(Jezyk.T("\n\n• ") + otwarte + Jezyk.T(" to pytania lub zadania bez odpowiedzi – zostają do odpowiedzi."));
                if (uAdresata > 0) why.Append(Jezyk.T("\n\n• ") + uAdresata + Jezyk.T(" czeka w kolejkach – zostają, dopóki adresat ich nie potwierdzi."));
                MessageBox.Show(form, why.ToString(), Jezyk.T("Wyczyść"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(form, Jezyk.T("Przenieść ") + cand.Count + Jezyk.T(" przeczytanych wiadomości do archiwum?\n\n") +
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
            catch (Exception ex) { MessageBox.Show(form, Jezyk.T("Nie przeniesiono: ") + ex.Message, Jezyk.T("Wyczyść"), MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            var kept = new HashSet<string>(P.Wszystkie(false).Select(m => m.Plik));
            read.RemoveWhere(r => r.StartsWith("STAN|") ? r != StanKey() : !kept.Contains(r));
            SaveRead();
            Skanuj();
            stInfo.Text = Jezyk.T("Przeniesiono do archiwum: ") + ok + (err > 0 ? Jezyk.T(", błędy: ") + err : "");
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
        // Menu prawego klawisza budowane przy każdym otwarciu, żeby pokazywało bieżący stan
        // stosu, agentów CLI i kolejki operatora.
        void BuildTray()
        {
            tray = new NotifyIcon { Icon = iconOk, Text = "ECXP-MAILBOX", Visible = true };
            var menu = new ContextMenuStrip { ShowImageMargin = false, ShowCheckMargin = true };
            menu.Opening += (o, e) => { e.Cancel = false; MenuZasobnika(menu); };
            menu.Items.Add("…");
            tray.ContextMenuStrip = menu;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };
            tray.DoubleClick += delegate { ShowWindow(); };
        }

        void ZmienJezyk(string kod)
        {
            if (Jezyk.Kod == kod) return;
            Jezyk.Kod = kod;
            ZapiszUstawienia();
            int i = Array.FindIndex(Jezyk.Dostepne, j => j[0] == kod);
            if (cbJezyk != null && i >= 0 && cbJezyk.SelectedIndex != i) cbJezyk.SelectedIndex = i;
            Jezyk.Zastosuj(form); Jezyk.Zastosuj(pop);
            RefreshView(); PokazCli();
        }

        void PokazZakladke(TabPage t) { ShowWindow(); if (t != null) tabs.SelectedTab = t; }

        static ToolStripMenuItem Poz(string tekst, EventHandler klik, bool aktywna = true)
        {
            var it = new ToolStripMenuItem(tekst, null, klik) { Enabled = aktywna };
            return it;
        }

        void MenuZasobnika(ContextMenuStrip menu)
        {
            menu.Items.Clear();
            var fontMenu = menu.Font;
            // nagłówek: projekt i stan stosu
            var proj = cbProjekt != null && cbProjekt.SelectedIndex > 0 ? (string)cbProjekt.SelectedItem : Jezyk.T("wszystkie projekty");
            var naglowek = new ToolStripMenuItem("ECXP-MAILBOX  ·  " + Jezyk.T("Projekt:") + " " + proj) { Enabled = false, Font = new Font(fontMenu, FontStyle.Bold) };
            menu.Items.Add(naglowek);
            bool ok = klient != null && klient.Polaczony;
            var stos = Poz(ok ? Jezyk.T("ECXP: połączono ze stosem ecxpd ✓") : Jezyk.T("⚠ ECXP: brak połączenia ze stosem"), delegate { if (!(klient != null && klient.Polaczony)) Polacz(); ShowWindow(); });
            stos.ForeColor = ok ? Color.FromArgb(0, 128, 0) : Color.FromArgb(190, 0, 0);
            menu.Items.Add(stos);
            int nowe = msgs.Count(m => m.Nowa);
            menu.Items.Add(Poz(string.Format(Jezyk.T("Nowe dla Ciebie: {0}"), nowe), delegate { PokazZakladke(tabMsg); }));
            menu.Items.Add(new ToolStripSeparator());

            var pelne = Poz(Jezyk.T("Otwórz pełne okno ECXP-MAILBOX"), delegate { ShowWindow(); });
            pelne.Font = new Font(fontMenu, FontStyle.Bold);
            menu.Items.Add(pelne);
            menu.Items.Add(Poz(Jezyk.T("Napisz…"), delegate { ShowWindow(); Pisz(null); }));
            menu.Items.Add(Poz(Jezyk.T("＋  Nowe zadanie / moduł…").Replace("＋", "").Trim(), delegate { ShowWindow(); NoweZadanie(); }));
            menu.Items.Add(Poz(Jezyk.T("Decyzja…"), delegate { ShowWindow(); Decyzja(); }));
            menu.Items.Add(new ToolStripSeparator());

            // status agentów CLI z akcją dla każdego z nich
            var mCli = new ToolStripMenuItem(Jezyk.T("Status agentów CLI"));
            var krotko = new List<string>();
            foreach (var n in new[] { "claude", "codex" })
            {
                StanCli st; cli.TryGetValue(n, out st);
                var nazwa = n == "claude" ? "Claude Code CLI" : "ChatGPT/Codex CLI";
                Dictionary<string, object> usl; uslugi.TryGetValue(n, out usl);
                Color kolM; string opis = StanUslugi(st, usl, out kolM) + (st != null && st.Wersja.Length > 0 ? "  (" + st.Wersja + ")" : "");
                var wiersz = new ToolStripMenuItem("● " + nazwa + ": " + opis) { Enabled = false };
                mCli.DropDownItems.Add(wiersz);
                if (st != null)
                {
                    var nn = n;
                    mCli.DropDownItems.Add(Poz("     " + Jezyk.T(!st.Jest ? "Zainstaluj…" : st.Aktywne ? "▶  Agent w tle…" : "Zaloguj (w tle)"), delegate { ShowWindow(); UruchomCli(nn); }));
                    Dictionary<string, object> us; uslugi.TryGetValue(n, out us);
                    if (st.Aktywne && us != null)
                    {
                        if (P.B(us, "zatrzymana")) mCli.DropDownItems.Add(Poz("     ▶  " + Jezyk.T("Start usługi"), delegate { AkcjaUslugi(nn, "start"); }));
                        else mCli.DropDownItems.Add(Poz("     ■  " + Jezyk.T("Zatrzymaj usługę"), delegate { AkcjaUslugi(nn, "stop"); }));
                        mCli.DropDownItems.Add(Poz("     ↻  " + Jezyk.T("Uruchom ponownie"), delegate { AkcjaUslugi(nn, "restart"); }));
                    }
                    krotko.Add((n == "claude" ? "Claude" : "Codex") + " " + (st.Aktywne && !(usl != null && P.B(usl, "zatrzymana")) ? "✓" : "✗"));
                }
            }
            mCli.DropDownItems.Add(new ToolStripSeparator());
            mCli.DropDownItems.Add(Poz(Jezyk.T("Sprawdź status teraz"), delegate { ostatnieCli = DateTime.Now; SprawdzCliTlo(); OdswiezUslugi(); }));
            mCli.DropDownItems.Add(new ToolStripMenuItem(Jezyk.T("Usługi jako pasek (A)"), null, delegate { UstawWidokUslug("pasek"); }) { Checked = widokUslug == "pasek" });
            mCli.DropDownItems.Add(new ToolStripMenuItem(Jezyk.T("Usługi jako karty (B)"), null, delegate { UstawWidokUslug("karty"); }) { Checked = widokUslug == "karty" });
            mCli.DropDownItems.Add(Poz(Jezyk.T("Dziennik usług CLI"), delegate { OpenPath(Path.Combine(P.Root, "stan")); }));
            if (krotko.Count > 0) mCli.Text += "   (" + string.Join(" · ", krotko) + ")";
            menu.Items.Add(mCli);
            menu.Items.Add(Poz(Jezyk.T("Agenci w tle") + " (" + lvAg.Items.Count + ")", delegate { PokazZakladke(tabAg); }));
            menu.Items.Add(Poz(Jezyk.T("Połączenia"), delegate { PokazZakladke(tabPol); }));
            menu.Items.Add(Poz(Jezyk.T("Stan modułów"), delegate { PokazZakladke(tabStan); }));
            menu.Items.Add(Poz(Jezyk.T("Spis gałęzi"), delegate { SpisGalezi(); }));
            menu.Items.Add(new ToolStripSeparator());

            var mJezyk = new ToolStripMenuItem(Jezyk.Kod == "en" ? "Language" : Jezyk.T("Język") + " / Language");
            foreach (var j in Jezyk.Dostepne)
            {
                var kod = j[0];
                mJezyk.DropDownItems.Add(new ToolStripMenuItem(j[1], null, delegate { ZmienJezyk(kod); }) { Checked = Jezyk.Kod == kod });
            }
            menu.Items.Add(mJezyk);
            menu.Items.Add(Poz(Jezyk.T("Motyw jasny / ciemny"), delegate { PrzelaczMotyw(); }));
            menu.Items.Add(Poz(Jezyk.T("O programie"), delegate { ShowWindow(); OProgramie(); }));
            menu.Items.Add(Poz(Jezyk.T("Zainstaluj / napraw rejestrację ECXP"), delegate { MessageBox.Show(form, Instalator.Wykonaj(false), Jezyk.T("Instalacja ECXP")); }));
            menu.Items.Add(Poz(Jezyk.T("Otwórz folder skrzynki"), delegate { OpenPath(P.Root); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Poz(Jezyk.T("Zakończ"), delegate { timer.Stop(); fsw.EnableRaisingEvents = false; if (klient != null) klient.Dispose(); tray.Visible = false; ExitThread(); }));
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
                ShowPop((m.Do == P.Wlasciciel ? Jezyk.T("Do Ciebie od ") : Jezyk.T("Nowa wiadomość: ")) + NazwaW(m.Od) + (m.Do == P.Wlasciciel ? "" : " → " + NazwaW(m.Do)),
                    Zdanie(m), fresh, stanZm);
            }
            else if (fresh.Count > 1)
                ShowPop(Jezyk.T("Nowe wiadomości: ") + fresh.Count, string.Join(Jezyk.T("\n"), fresh.Take(3).Select(m => NazwaW(m.Od) + " → " + NazwaW(m.Do) + ": " + P.Baza(m.Temat))), fresh, stanZm);
            else if (stanZm)
                ShowPop(Jezyk.T("Zmienił się stan modułów"), CzekaNaCiebie().Split('\n')[0], new List<Msg>(), true);

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
            if (args.Length >= 2 && args[0] == "/selftest-agent") return Test.Agent(args[1]);
            if (args.Length >= 3 && args[0] == "/demo") return Test.Demo(args[1], args[2], args.Length > 3 ? args[3] : "pl");
            if (args.Length >= 1 && (args[0] == "/instaluj" || args[0] == "/odinstaluj"))
            {
                var mj = Regex.Match(P.Czytaj(Path.Combine(exeDir, "ustawienia-wlasciciel.txt")), @"jezyk=([a-z]{2})");
                if (mj.Success) Jezyk.Kod = mj.Groups[1].Value;
                for (int i = 0; i + 1 < args.Length; i++) if (args[i] == "/jezyk") Jezyk.Kod = args[i + 1];
                var raport = Instalator.Wykonaj(args[0] == "/odinstaluj");
                try { File.WriteAllText(Path.Combine(P.Root, "stan", "instalacja.txt"), raport, new UTF8Encoding(false)); } catch { }
                if (!args.Contains("/cicho")) MessageBox.Show(raport, Jezyk.T("Instalacja ECXP"));
                return 0;
            }
            if (args.Length >= 1 && args[0] == "/spis-galezi") { Motyw.Ciemny = args.Contains("/ciemny"); ZbudujSpis(); return 0; }

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

    // ---------- języki konsoli: polski (źródłowy), angielski, francuski, czeski, niemiecki ----------
    // Słownik jezyki.json (zasób programu): polski tekst → tłumaczenie. Teksty kontrolek tłumaczy przejście po oknie
    // (z zapamiętaniem oryginału), teksty komunikatów – Jezyk.T(). Treści wiadomości agentów nie są tłumaczone.
    static class Jezyk
    {
        public static string Kod = "pl";
        public static readonly string[][] Dostepne = { new[] { "pl", "Polski" }, new[] { "en", "English" }, new[] { "fr", "Français" }, new[] { "cs", "Čeština" }, new[] { "de", "Deutsch" } };
        static Dictionary<string, Dictionary<string, object>> slowniki;
        static readonly Dictionary<object, string[]> oryg = new Dictionary<object, string[]>();

        static void Wczytaj()
        {
            if (slowniki != null) return;
            slowniki = new Dictionary<string, Dictionary<string, object>>();
            try
            {
                using (var st = Assembly.GetExecutingAssembly().GetManifestResourceStream("jezyki.json"))
                using (var r = new StreamReader(st, Encoding.UTF8))
                {
                    var d = P.Js().Deserialize<Dictionary<string, object>>(r.ReadToEnd());
                    foreach (var kv in d) if (kv.Value is Dictionary<string, object>) slowniki[kv.Key] = (Dictionary<string, object>)kv.Value;
                }
            }
            catch { }
        }

        public static string T(string pl)
        {
            if (Kod == "pl" || string.IsNullOrEmpty(pl)) return pl;
            Wczytaj();
            Dictionary<string, object> d;
            if (!slowniki.TryGetValue(Kod, out d)) return pl;
            object v;
            if (d.TryGetValue(pl, out v)) return (string)v;
            var i = pl.IndexOf(" (");
            if (i > 0 && d.TryGetValue(pl.Substring(0, i), out v)) return v + pl.Substring(i);
            return pl;
        }

        static string Tlumacz(object o, string teraz)
        {
            string[] st;
            if (!oryg.TryGetValue(o, out st) || st[1] != teraz) st = new[] { teraz, teraz };   // tekst zmieniony przez program → nowy oryginał
            var t = T(st[0]);
            st[1] = t; oryg[o] = st;
            return t;
        }

        public static void Zastosuj(ToolStrip ts)
        {
            if (ts == null) return;
            foreach (ToolStripItem it in ts.Items) if (it.Text.Length > 0 && !(it is ToolStripStatusLabel)) it.Text = Tlumacz(it, it.Text);
        }

        static bool trwa;
        public static void Zastosuj(Control c)
        {
            if (c == null || trwa) return;
            trwa = true;
            try { ZastosujW(c); } finally { trwa = false; }
        }

        static void ZastosujW(Control c)
        {
            if ((c is Form || c is Label || c is ButtonBase || c is TabPage) && !(c is LinkLabel) && c.Text.Length > 0) c.Text = Tlumacz(c, c.Text);
            if (c is ListView) foreach (ColumnHeader h in ((ListView)c).Columns) h.Text = Tlumacz(h, h.Text);
            if (c is ComboBox && ((ComboBox)c).DropDownStyle == ComboBoxStyle.DropDownList)
            {
                var cb = (ComboBox)c;
                for (int i = 0; i < cb.Items.Count; i++)
                {
                    var s = cb.Items[i] as string;
                    if (s == null) continue;
                    var klucz = cb.GetHashCode() + "#" + i;
                    string[] st;
                    var org = oryg.TryGetValue(klucz, out st) && st[1] == s ? st[0] : s;
                    var t = T(org);
                    oryg[klucz] = new[] { org, t };
                    if (t != s) { var sel = cb.SelectedIndex; cb.Items[i] = t; cb.SelectedIndex = sel; }
                }
            }
            if (c is ToolStrip) Zastosuj((ToolStrip)c);
            foreach (Control d in c.Controls) ZastosujW(d);
        }
    }

    // ---------- motyw jasny / ciemny ----------
    static class Motyw
    {
        public static bool Ciemny;
        public static Color Tlo { get { return Ciemny ? Color.FromArgb(32, 33, 36) : SystemColors.Control; } }
        public static Color Panel { get { return Ciemny ? Color.FromArgb(40, 42, 46) : SystemColors.Control; } }
        public static Color Pole { get { return Ciemny ? Color.FromArgb(24, 25, 28) : Color.White; } }
        public static Color Tekst { get { return Ciemny ? Color.FromArgb(231, 233, 236) : Color.Black; } }
        public static Color Slaby { get { return Ciemny ? Color.FromArgb(160, 164, 170) : Color.DimGray; } }
        public static Color Ramka { get { return Ciemny ? Color.FromArgb(70, 73, 79) : SystemColors.ControlDark; } }
        public static Color Naglowek { get { return Ciemny ? Color.FromArgb(24, 25, 28) : Color.White; } }
        public static Color Czeka { get { return Ciemny ? Color.FromArgb(58, 52, 30) : Color.FromArgb(255, 248, 220); } }
        static readonly int[] Slabe = { Color.DimGray.ToArgb(), Color.FromArgb(160, 164, 170).ToArgb() };
        static readonly int[] Biale = { Color.White.ToArgb(), Color.FromArgb(24, 25, 28).ToArgb() };
        static readonly int[] Kremowe = { Color.FromArgb(255, 248, 220).ToArgb(), Color.FromArgb(58, 52, 30).ToArgb() };

        // Kolor znaczący (status, rola) – w ciemnym motywie rozjaśniony, żeby był czytelny na ciemnym tle.
        public static Color K(Color c)
        {
            if (!Ciemny) return c;
            return Color.FromArgb(c.R + (255 - c.R) * 55 / 100, c.G + (255 - c.G) * 55 / 100, c.B + (255 - c.B) * 55 / 100);
        }
        // Tło ostrzeżenia (pastel) – w ciemnym motywie przyciemnione.
        public static Color Ostrz(Color c) { return Ciemny ? Color.FromArgb(c.R / 4 + 20, c.G / 5 + 16, c.B / 5 + 16) : c; }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
        static void Pasek(Form f) { try { int v = Ciemny ? 1 : 0; DwmSetWindowAttribute(f.Handle, 20, ref v, 4); } catch { } }

        public static void Zastosuj(Control c)
        {
            if (c is Form)
            {
                var f = (Form)c; f.BackColor = Tlo; f.ForeColor = Tekst;
                if (f.IsHandleCreated) Pasek(f); else f.HandleCreated += delegate { Pasek(f); };
            }
            else if (c is TextBoxBase)
            {
                c.BackColor = Kremowe.Contains(c.BackColor.ToArgb()) ? Czeka : Pole; c.ForeColor = Tekst;
            }
            else if (c is ListView)
            {
                var lv = (ListView)c; lv.BackColor = Pole; lv.ForeColor = Tekst;
                if (!lv.OwnerDraw)
                {
                    lv.OwnerDraw = true;
                    lv.DrawColumnHeader += (s, e) =>
                    {
                        using (var b = new SolidBrush(Ciemny ? Panel : SystemColors.Control)) e.Graphics.FillRectangle(b, e.Bounds);
                        using (var pen = new Pen(Ramka)) e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top + 3, e.Bounds.Right - 1, e.Bounds.Bottom - 3);
                        TextRenderer.DrawText(e.Graphics, e.Header.Text, lv.Font, new Rectangle(e.Bounds.X + 5, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height), Tekst,
                            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                    };
                    lv.DrawItem += (s, e) => { e.DrawDefault = true; };
                    lv.DrawSubItem += (s, e) => { e.DrawDefault = true; };
                }
                lv.Invalidate();
            }
            else if (c is ListBox) { c.BackColor = Pole; c.ForeColor = Tekst; }
            else if (c is ComboBox)
            {
                var cb = (ComboBox)c; cb.FlatStyle = Ciemny ? FlatStyle.Flat : FlatStyle.Standard; cb.BackColor = Pole; cb.ForeColor = Tekst;
                if (cb.DropDownStyle == ComboBoxStyle.DropDownList && cb.DrawMode != DrawMode.OwnerDrawFixed)
                {
                    cb.DrawMode = DrawMode.OwnerDrawFixed;
                    cb.DrawItem += (s, e) =>
                    {
                        bool zazn = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
                        using (var b = new SolidBrush(zazn ? (Ciemny ? Color.FromArgb(60, 64, 72) : SystemColors.Highlight) : Pole)) e.Graphics.FillRectangle(b, e.Bounds);
                        if (e.Index >= 0)
                            TextRenderer.DrawText(e.Graphics, cb.Items[e.Index].ToString(), cb.Font, e.Bounds, zazn && !Ciemny ? SystemColors.HighlightText : Tekst,
                                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
                    };
                }
                cb.Invalidate();
            }
            else if (c is Button)
            {
                var b = (Button)c;
                if (b.BackColor.ToArgb() == Color.FromArgb(255, 222, 0).ToArgb()) b.ForeColor = Color.Black;
                else
                {
                    b.FlatStyle = Ciemny ? FlatStyle.Flat : FlatStyle.Standard;
                    b.BackColor = Ciemny ? Panel : SystemColors.Control; b.ForeColor = Tekst; b.UseVisualStyleBackColor = !Ciemny;
                    b.FlatAppearance.BorderColor = Ramka;
                }
            }
            else if (c is LinkLabel) { c.BackColor = Color.Transparent; }
            else if (c is Label) { c.ForeColor = Slabe.Contains(c.ForeColor.ToArgb()) ? Slaby : Tekst; }
            else if (c is CheckBox) { c.ForeColor = Tekst; }
            else if (c is StatusStrip || c is ToolStrip)
            {
                c.BackColor = Panel;
                foreach (ToolStripItem it in ((ToolStrip)c).Items)
                    if (it.ForeColor.ToArgb() == SystemColors.ControlText.ToArgb() || it.ForeColor.ToArgb() == Color.FromArgb(231, 233, 236).ToArgb()) it.ForeColor = Tekst;
            }
            else if (c is TabControl)
            {
                var t = (TabControl)c;
                if (t.DrawMode != TabDrawMode.OwnerDrawFixed) { t.DrawMode = TabDrawMode.OwnerDrawFixed; t.Padding = new Point(14, 5); t.DrawItem += RysujZakladke; }
                t.Invalidate();
            }
            else if (c is SplitContainer) { c.BackColor = Ramka; }
            else if (c is Panel && !(c is TableLayoutPanel) && !(c is FlowLayoutPanel) && !(c is SplitterPanel) && Biale.Contains(c.BackColor.ToArgb())) { c.BackColor = Naglowek; }
            else if (c is FlowLayoutPanel && c.Parent != null && Biale.Contains(c.Parent.BackColor.ToArgb())) { c.BackColor = c.Parent.BackColor; c.ForeColor = Tekst; }   // pasek w nagłówku
            else if (!(c is PictureBox)) { c.BackColor = Tlo; c.ForeColor = Tekst; }
            foreach (Control d in c.Controls) Zastosuj(d);
        }

        static void RysujZakladke(object s, DrawItemEventArgs e)
        {
            var t = (TabControl)s;
            var r = t.GetTabRect(e.Index);
            bool sel = e.Index == t.SelectedIndex;
            using (var b = new SolidBrush(sel ? (Ciemny ? Pole : Color.White) : (Ciemny ? Panel : SystemColors.Control))) e.Graphics.FillRectangle(b, r);
            TextRenderer.DrawText(e.Graphics, t.TabPages[e.Index].Text, t.Font, r, Tekst, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    // ---------- załączniki: pliki, zrzut ekranu, obraz ze schowka ----------
    class Zalaczniki : FlowLayoutPanel
    {
        public readonly List<string> Pliki = new List<string>();
        readonly FlowLayoutPanel lista;
        public Zalaczniki()
        {
            AutoSize = true; Dock = DockStyle.Fill; WrapContents = true; Margin = new Padding(0);
            var bP = Przycisk("📎  Dodaj plik…"); var bZ = Przycisk("📷  Zrzut ekranu"); var bS = Przycisk("📋  Wklej ze schowka");
            lista = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(6, 0, 0, 0) };
            Controls.AddRange(new Control[] { bP, bZ, bS, lista });
            bP.Click += delegate
            {
                using (var d = new OpenFileDialog { Multiselect = true, Title = "Załącz pliki (bez danych spraw, haseł i kluczy)" })
                    if (d.ShowDialog(FindForm()) == DialogResult.OK) foreach (var p in d.FileNames) Dodaj(p);
            };
            bZ.Click += delegate { Zrzut(); };
            bS.Click += delegate
            {
                if (Clipboard.ContainsImage()) Dodaj(Zapisz(Clipboard.GetImage(), "schowek"));
                else if (Clipboard.ContainsFileDropList()) foreach (var p in Clipboard.GetFileDropList()) Dodaj(p);
                else MessageBox.Show(FindForm(), Jezyk.T("W schowku nie ma obrazu ani plików."), Jezyk.T("Wklej ze schowka"));
            };
        }
        static Button Przycisk(string t) { return new Button { Text = t, AutoSize = true, Height = 30, Margin = new Padding(0, 0, 6, 4) }; }
        static string Katalog() { var d = Path.Combine(Path.GetTempPath(), "ecxp-zalaczniki"); Directory.CreateDirectory(d); return d; }
        static string Zapisz(Image img, string co)
        {
            var p = Path.Combine(Katalog(), co + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
            img.Save(p, System.Drawing.Imaging.ImageFormat.Png);
            return p;
        }
        // Zrzut całego pulpitu (wszystkie monitory) – okna konsoli są na ten czas ukryte.
        void Zrzut()
        {
            var f = FindForm(); var wl = f.Owner;
            f.Opacity = 0; if (wl != null) wl.Opacity = 0;
            Application.DoEvents(); Thread.Sleep(350);
            try
            {
                var r = SystemInformation.VirtualScreen;
                using (var bmp = new Bitmap(r.Width, r.Height))
                {
                    using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(r.Left, r.Top, 0, 0, r.Size);
                    Dodaj(Zapisz(bmp, "zrzut"));
                }
            }
            finally { f.Opacity = 1; if (wl != null) wl.Opacity = 1; }
        }
        public void Dodaj(string p)
        {
            if (!File.Exists(p) || Pliki.Contains(p)) return;
            if (new FileInfo(p).Length > P.MaxObiekt) { MessageBox.Show(FindForm(), Jezyk.T("Plik większy niż 32 MB: ") + p, Jezyk.T("Załącznik")); return; }
            Pliki.Add(p);
            var chip = new Label { Text = "📎 " + Path.GetFileName(p) + "  ✕", AutoSize = true, Cursor = Cursors.Hand, Margin = new Padding(4, 8, 6, 0) };
            new ToolTip().SetToolTip(chip, p + "\nKliknij, aby usunąć z wiadomości.");
            chip.Click += delegate { Pliki.Remove(p); lista.Controls.Remove(chip); };
            lista.Controls.Add(chip);
        }
    }

    // ---------- instalacja na stanowisku (uniwersalna: ścieżki z katalogu instalacji i profilu użytkownika) ----------
    static class Instalator
    {
        static string Uruchom(string plik, string args)
        {
            var psi = plik.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? new System.Diagnostics.ProcessStartInfo(plik, args)
                : new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /s /c \"\"" + plik + "\" " + args + "\"");
            psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            using (var pr = System.Diagnostics.Process.Start(psi))
            {
                var o = pr.StandardOutput.ReadToEnd() + pr.StandardError.ReadToEnd();
                pr.WaitForExit(120000);
                return o.Trim();
            }
        }

        static string Ladnie(string json)
        {
            var sb = new StringBuilder(); int wc = 0; bool str = false, esc = false;
            foreach (var c in json)
            {
                if (str) { sb.Append(c); if (esc) esc = false; else if (c == '\\') esc = true; else if (c == '"') str = false; continue; }
                switch (c)
                {
                    case '"': str = true; sb.Append(c); break;
                    case '{': case '[': sb.Append(c).Append('\n').Append(' ', ++wc * 2); break;
                    case '}': case ']': sb.Append('\n').Append(' ', --wc * 2).Append(c); break;
                    case ',': sb.Append(c).Append('\n').Append(' ', wc * 2); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        static void Kopia(string p) { var b = p + ".przed-ecxp.bak"; if (File.Exists(p) && !File.Exists(b)) File.Copy(p, b); }

        public static string Wykonaj(bool usun)
        {
            var log = new StringBuilder();
            var narz = Path.Combine(P.Root, "narzedzia");
            var mcp = Path.Combine(narz, "ecxp-mcp.exe");
            var hook = Path.Combine(narz, "hook-claude.ps1");
            var dom = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var d in P.Foldery) Directory.CreateDirectory(Path.Combine(P.Root, d));
            Directory.CreateDirectory(Path.Combine(P.Root, "stan")); Directory.CreateDirectory(Path.Combine(P.Root, "projekty"));
            log.AppendLine(Jezyk.T(usun ? "Odinstalowanie" : "Instalacja") + Jezyk.T(" ECXP na stanowisku ") + Environment.MachineName + Jezyk.T(", katalog: ") + P.Root);

            // 1. Claude Code: serwer MCP ecxp (zakres użytkownika)
            var claude = Agenci.Program("claude");
            if (claude.Length == 0) log.AppendLine(Jezyk.T("• Claude Code CLI: brak na tym stanowisku – pomijam rejestrację w Claude."));
            else
            {
                Uruchom(claude, "mcp remove ecxp --scope user");
                log.AppendLine(usun ? Jezyk.T("• Claude: usunięto serwer MCP ecxp.") : "• Claude: " + Uruchom(claude, "mcp add --scope user ecxp -- \"" + mcp + "\"").Split('\n')[0]);
            }

            // 2. Claude Code: hook UserPromptSubmit (lustro wpisów operatora z czatu)
            var sp = Path.Combine(dom, ".claude", "settings.json");
            try
            {
                Dictionary<string, object> st = new Dictionary<string, object>();
                if (File.Exists(sp)) st = P.Js().Deserialize<Dictionary<string, object>>(P.Czytaj(sp)) ?? new Dictionary<string, object>();
                var hooks = Pliki.Slownik(st, "hooks");
                var lista = hooks.ContainsKey("UserPromptSubmit") && hooks["UserPromptSubmit"] is System.Collections.IEnumerable
                    ? ((System.Collections.IEnumerable)hooks["UserPromptSubmit"]).Cast<object>().ToList() : new List<object>();
                lista = lista.Where(x => !P.Js().Serialize(x).Contains("hook-claude.ps1")).ToList();
                if (!usun) lista.Add(P.D("hooks", new List<object> { P.D("type", "command", "command", "powershell -NoProfile -ExecutionPolicy Bypass -File \"" + hook + "\"", "timeout", 15) }));
                if (lista.Count > 0) hooks["UserPromptSubmit"] = lista; else hooks.Remove("UserPromptSubmit");
                if (hooks.Count == 0) st.Remove("hooks");
                Directory.CreateDirectory(Path.GetDirectoryName(sp));
                Kopia(sp);
                File.WriteAllText(sp, Ladnie(P.Js().Serialize(st)), new UTF8Encoding(false));
                log.AppendLine(usun ? Jezyk.T("• Claude: usunięto hook lustra czatu.") : Jezyk.T("• Claude: hook lustra czatu (UserPromptSubmit) w ") + sp + ".");
            }
            catch (Exception ex) { log.AppendLine(Jezyk.T("• Claude: NIE zmieniono ") + sp + " (" + ex.Message + ")."); }

            // 3. Codex: serwer MCP ecxp w config.toml
            var cp = Path.Combine(dom, ".codex", "config.toml");
            if (!File.Exists(cp)) log.AppendLine(Jezyk.T("• Codex: brak ") + cp + Jezyk.T(" – pomijam (zainstaluj aplikację Codex i uruchom instalację ponownie)."));
            else
            {
                var linie = File.ReadAllLines(cp).ToList();
                var wynik = new List<string>(); bool pomin = false;
                foreach (var l in linie)
                {
                    var t = l.Trim();
                    if (t.StartsWith("[")) pomin = t == "[mcp_servers.ecxp]" || t.StartsWith("[mcp_servers.ecxp.");
                    if (!pomin) wynik.Add(l);
                }
                while (wynik.Count > 0 && wynik[wynik.Count - 1].Trim().Length == 0) wynik.RemoveAt(wynik.Count - 1);
                if (!usun) { wynik.Add(""); wynik.Add("[mcp_servers.ecxp]"); wynik.Add("command = '" + mcp + "'"); wynik.Add("args = []"); wynik.Add("startup_timeout_sec = 60"); }
                Kopia(cp);
                File.WriteAllLines(cp, wynik, new UTF8Encoding(false));
                log.AppendLine(usun ? Jezyk.T("• Codex: usunięto [mcp_servers.ecxp].") : Jezyk.T("• Codex: [mcp_servers.ecxp] w ") + cp + Jezyk.T(" – uruchom ponownie aplikację Codex."));
            }

            // 4. Skróty: pulpit i autostart
            foreach (var kat in new[] { Environment.SpecialFolder.Desktop, Environment.SpecialFolder.Startup })
            {
                var lnk = Path.Combine(Environment.GetFolderPath(kat), "ECXP-MAILBOX.lnk");
                try
                {
                    if (usun) { if (File.Exists(lnk)) File.Delete(lnk); continue; }
                    var sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                    var s = sh.GetType().InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
                    var exe = Path.Combine(narz, "ECXP-MAILBOX.exe");
                    foreach (var kv in new[] { new[] { "TargetPath", exe }, new[] { "WorkingDirectory", narz }, new[] { "IconLocation", exe + ",0" }, new[] { "Description", Jezyk.T("ECXP-MAILBOX – konsola operatora ECXP") } })
                        s.GetType().InvokeMember(kv[0], System.Reflection.BindingFlags.SetProperty, null, s, new object[] { kv[1] });
                    s.GetType().InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, s, null);
                }
                catch (Exception ex) { log.AppendLine(Jezyk.T("• Skrót ") + lnk + ": " + ex.Message); }
            }
            log.AppendLine(Jezyk.T(usun ? "• Usunięto skróty." : "• Skróty: pulpit i autostart."));
            log.AppendLine(Jezyk.T("Gotowe. Kopie zmienionych plików konfiguracji: *.przed-ecxp.bak."));
            return log.ToString();
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
        public Zalaczniki Zalaczniki(string e) { var z = new Zalaczniki(); Wiersz(e, z, false); return z; }
        protected override void OnLoad(EventArgs e) { Motyw.Zastosuj(this); Jezyk.Zastosuj(this); base.OnLoad(e); }
        public void PrzyciskOk(string tekst) { ok.Text = tekst; }
    }

    // ---------- okno „Napisz” ----------
    class PiszForm : Form
    {
        public ComboBox cbDo, cbProjekt, cbModul, cbTyp;
        public TextBox tbTemat, tbTresc;
        public CheckBox chPilne;
        public Zalaczniki zal = new Zalaczniki();
        protected override void OnLoad(EventArgs e) { Motyw.Zastosuj(this); Jezyk.Zastosuj(this); base.OnLoad(e); }
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
            Wiersz(t, "Załączniki:", zal, SizeType.AutoSize);
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
            { MessageBox.Show(this, Jezyk.T("Wybierz adresata z listy albo wpisz nazwę w postaci codex-<czat> lub claude-<sesja>."), Jezyk.T("Napisz")); return; }
            if (tbTresc.Text.Trim().Length == 0) { MessageBox.Show(this, Jezyk.T("Wpisz treść wiadomości."), Jezyk.T("Napisz")); tbTresc.Focus(); return; }
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    // ---------- test protokołu na kopii (ECXP-MAILBOX.exe /selftest <pusty katalog>) ----------
    static class Test
    {
        // Test operatora przez prawdziwy stos: konsola łączy się jako operator (stos sprawdza obraz procesu),
        // Przykładowe dane do prezentacji i dokumentacji (wymyślony projekt, bez prawdziwych danych):
        // ECXP-MAILBOX.exe /demo <katalog instalacji przykładowej> 1   – operator tworzy parę i zadanie
        // ECXP-MAILBOX.exe /demo <katalog instalacji przykładowej> 2   – operator odpowiada parze na pytanie Claude
        public static int Demo(string root, string krok, string jezyk)
        {
            bool en = jezyk == "en";
            P.Root = Path.GetFullPath(root);
            using (var k = Klient.Polacz(P.Wlasciciel, "konsola", true))
            {
                if (krok == "1")
                    k.Wywolaj("nowe_zadanie", P.D("projekt", "demo-app", "modul", en ? "reports" : "raporty", "tytul", en ? "PDF report export" : "Eksport raportu PDF",
                        "opis", en ? "Add PDF export for reports. The file needs a SHA-256 hash in the footer, and the export a regression test."
                                   : "Dodajcie eksport raportu do PDF. Plik ma mieć sumę SHA-256 w stopce, a eksport – test regresji.",
                        "role_codex", new List<string> { "programista" }, "role_claude", new List<string> { "recenzent", "tester" }));
                else
                {
                    var mod = en ? "reports" : "raporty";
                    var pyt = P.Wszystkie(false).Where(m => m.Od == "claude-" + mod && m.Typ == "pytanie").OrderByDescending(m => m.Czas).FirstOrDefault();
                    k.Wywolaj("wyslij", P.D("do", "para:" + mod, "projekt", "demo-app", "modul", mod, "typ", "odpowiedz", "temat", en ? "PDF report export" : "Eksport raportu PDF",
                        "odpowiedz_na", pyt != null ? pyt.Id : "", "tresc", en ? "Yes – date and time in the file name, e.g. report-2026-10-31-1405.pdf."
                                                                                  : "Tak – data i godzina w nazwie pliku, np. raport-2026-10-31-1405.pdf."));
                }
            }
            return 0;
        }

        // Test agenta w tle (prawdziwa tura Claude Code CLI, model haiku, tylko odczyt): ECXP-MAILBOX.exe /selftest-agent <katalog instalacji testowej>
        public static int Agent(string root)
        {
            var log = new StringBuilder(); int zle = 0;
            Action<bool, string> ok = (c, t) => { log.AppendLine((c ? "PASS  " : "FAIL  ") + t); if (!c) zle++; };
            Klient k = null;
            try
            {
                P.Root = Path.GetFullPath(root);
                k = Klient.Polacz(P.Wlasciciel, "konsola", true);
                var ag = (Dictionary<string, object>)k.Wywolaj("agenci", null);
                ok(P.S(ag, "claude_cli").Length > 0, "Claude Code CLI znaleziony w PATH: " + P.S(ag, "claude_cli"));
                k.Wywolaj("nowe_zadanie", P.D("projekt", "testowy", "modul", "ta", "tytul", "Agent test", "role_codex", new List<string> { "programista" }, "role_claude", new List<string> { "recenzent" },
                    "opis", "Test agenta w tle: wyślij operatorowi (do=wlasciciel, typ=meldunek) jedno zdanie „Agent w tle działa” i potwierdź to zadanie. Nie zmieniaj żadnych plików."));
                bool zlyKatalog = false;
                try { k.Wywolaj("uruchom_agenta", P.D("nazwa", "claude-ta", "projekt", "testowy", "modul", "ta", "katalog", Path.Combine(P.Root, "nie-ma"), "uprawnienia", "odczyt")); }
                catch (EcxpException) { zlyKatalog = true; }
                ok(zlyKatalog, "agent z nieistniejącym katalogiem odrzucony");
                var rec = (Dictionary<string, object>)k.Wywolaj("uruchom_agenta", P.D("nazwa", "claude-ta", "projekt", "testowy", "modul", "ta", "katalog", P.Root,
                    "uprawnienia", "odczyt", "model", "haiku", "na_godzine", 2, "role", new List<string> { "recenzent" }));
                ok(P.B(rec, "aktywny"), "agent claude-ta uruchomiony w tle (odczyt, haiku)");
                var plik = Path.Combine(P.Root, "stan", "agenci", "claude-ta.json");
                var koniec = DateTime.Now.AddMinutes(6);
                Dictionary<string, object> stan = null;
                while (DateTime.Now < koniec)
                {
                    System.Threading.Thread.Sleep(5000);
                    stan = (Dictionary<string, object>)new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<Dictionary<string, object>>(P.Czytaj(plik));
                    if (P.Lista(stan, "uruchomienia").Count > 0) break;
                }
                var u = stan != null ? P.Lista(stan, "uruchomienia") : new List<Dictionary<string, object>>();
                ok(u.Count > 0 && P.S(u[0], "kod") == "0", "tura agenta zakończona, kod " + (u.Count > 0 ? P.S(u[0], "kod") + ", " + P.S(u[0], "sekund") + " s" : "brak"));
                System.Threading.Thread.Sleep(2000);
                var lista = P.Wszystkie(false);
                ok(lista.Any(m => m.Od == "claude-ta" && m.Do == P.Wlasciciel), "agent w tle wysłał meldunek operatorowi przez ECXP");
                ok(!lista.Any(m => m.Katalog == "do-claude" && m.Do == "claude-ta"), "agent potwierdził odebrane wiadomości (pusta kolejka)");
                k.Wywolaj("zatrzymaj_agenta", P.D("nazwa", "claude-ta"));
            }
            catch (Exception ex) { log.AppendLine("FAIL  wyjątek: " + ex); zle++; }
            finally { if (k != null) k.Dispose(); }
            log.AppendLine(zle == 0 ? "WYNIK: wszystkie testy PASS" : "WYNIK: FAIL " + zle);
            try { File.WriteAllText(Path.Combine(root, "selftest-agent.txt"), log.ToString(), new UTF8Encoding(false)); } catch { }
            return zle;
        }

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

                var plikTest = Path.Combine(P.Root, "zrzut-test.png");
                using (var bmp = new Bitmap(40, 20)) bmp.Save(plikTest, System.Drawing.Imaging.ImageFormat.Png);
                var obj = (Dictionary<string, object>)k.Wywolaj("kontekst_zapisz", P.D("sciezka", plikTest, "nazwa", "zrzut-test.png", "typ", "image/png"));
                var r5 = (Dictionary<string, object>)k.Wywolaj("wyslij", P.D("do", "para:elan", "projekt", "testowy", "typ", "informacja", "tresc", "Zrzut w załączniku.",
                    "czesci", new List<object> { P.D("rodzaj", "plik", "sha256", obj["sha256"], "nazwa", "zrzut-test.png", "typ", "image/png") }));
                var zz = P.Wszystkie(false).Where(m => m.LustroId == P.S(r5, "lustro_id")).ToList();
                ok(zz.Count == 2 && zz.All(m => m.Czesci.Any(c => P.S(c, "rodzaj") == "plik" && P.S(c, "sha256") == P.S(obj, "sha256"))) && File.Exists(P.ObjPath(P.S(obj, "sha256"))),
                    "załącznik (zrzut PNG) jako obiekt SHA-256 dotarł do obu stron pary");

                var tr = (Dictionary<string, object>)k.Wywolaj("trasy", P.D("projekt", "testowy"));
                ok(((System.Collections.IEnumerable)tr["pary"]).Cast<object>().Count() >= 1, "tablica tras czytelna dla klienta");

                // łańcuch ról: nowe zadanie z łańcuchem – pierwszy krok idzie tylko do programisty, nie do całej pary
                var r6 = (Dictionary<string, object>)k.Wywolaj("nowe_zadanie", P.D("projekt", "testowy", "modul", "lr", "tytul", "LR test", "opis", "Zadanie dla łańcucha.",
                    "role_codex", new List<string> { "programista" }, "role_claude", new List<string> { "tester" },
                    "lancuch", new List<string> { "programista", "tester", "decyzja", "integrator" }));
                var lan = (Dictionary<string, object>)r6["lancuch"];
                var zl = P.Lista(lan, "zadania");
                ok(P.S(lan, "stan") == "w_toku" && zl.Count == 1 && P.Wszystkie(false).Any(m => m.Id == P.S(zl[0], "id") && m.Do == "codex-lr"),
                    "łańcuch ról utworzony: krok 1 (programista) trafił tylko do codex-lr");

                // podpisana decyzja operatora
                const string commitTest = "0123456789abcdef0123456789abcdef01234567";
                var r7 = (Dictionary<string, object>)k.Wywolaj("decyzja", P.D("projekt", "testowy", "modul", "lr", "do", "para:lr", "rodzaj", "user_accepted",
                    "commit", commitTest, "opis", "Test: akceptacja commitu."));
                var spr = (Dictionary<string, object>)k.Wywolaj("sprawdz_decyzje", P.D("id", r7["id"]));
                ok(P.B(spr, "wazna"), "decyzja podpisana kluczem operatora (DPAPI) i sprawdzalna");
                bool bezCommitu = false;
                try { k.Wywolaj("decyzja", P.D("projekt", "testowy", "modul", "lr", "do", "para:lr", "rodzaj", "push", "opis", "x")); } catch (EcxpException) { bezCommitu = true; }
                ok(bezCommitu, "decyzja push bez pełnego hasha commitu odrzucona");
                File.WriteAllText(Path.Combine(P.Root, "decyzja-testowa.txt"), P.Js().Serialize(P.D("id", r7["id"], "commit", commitTest)), new UTF8Encoding(false));
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
