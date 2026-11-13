// ECXP-MAILBOX – okno „🗂 Katalog agentów” (zakładka Agenci): katalog roboczy z agentami Codex i Claude, dodawanie agenta,
// uruchomienie go w ECXP z jego katalogiem oraz edycja plików: instrukcji (AGENTS.md / CLAUDE.md), zakresu i zadań
// (scope.md, ECXP-START.txt, szablon zlecenia) i skryptów (scripts\). Logika plików: KatalogAgentow.cs.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Ecxp
{
    partial class App
    {
        string katalogAgentow = "";
        Form oknoKatalogu;

        void WczytajKatalogAgentow(string ust)
        {
            var m = System.Text.RegularExpressions.Regex.Match(ust ?? "", @"(?m)^katalog_agentow=(.*?)\r?$");
            katalogAgentow = m.Success ? m.Groups[1].Value.Trim() : "";
            if (katalogAgentow.Length == 0 && Directory.Exists(@"E:\Agents")) katalogAgentow = @"E:\Agents";
        }
        string UstawieniaKataloguAgentow() { return katalogAgentow.Length > 0 ? "katalog_agentow=" + katalogAgentow + Environment.NewLine : ""; }

        // Stan agenta w ECXP: sesja w aplikacji, agent w tle (z jakim katalogiem) albo brak.
        string StanWEcxp(AgentKatalogu a)
        {
            try
            {
                if (a.Tozsamosc.Length > 0 && Wylaczeni.Jest(a.Tozsamosc)) return "⏸ " + Jezyk.T("wyłączony przez operatora");
                var tlo = Agenci.Lista().Where(x => P.B(x, "aktywny") && string.Equals(P.S(x, "katalog").TrimEnd('\\'), a.Folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)).ToList();
                if (tlo.Count > 0) return Jezyk.T("agent w tle: ") + string.Join(", ", tlo.Select(x => P.S(x, "nazwa") + " (" + P.S(x, "projekt") + "/" + P.S(x, "modul") + ")"));
                if (a.Tozsamosc.Length == 0) return "—";
                var tr = Siec.Trasy().Where(x => x.Aktywna && x.Nazwa == a.Tozsamosc).OrderByDescending(x => x.Od).FirstOrDefault();
                if (tr == null) return Jezyk.T("nie zarejestrowany");
                return ((tr.Sesja ?? "").StartsWith("tlo:") ? Jezyk.T("agent w tle") : Jezyk.T("sesja w aplikacji")) + " (" + tr.Projekt + "/" + tr.Modul + ")";
            }
            catch (Exception ex) { Bledy.Wyjatek(ex); return "?"; }
        }

        Action<string> wybierzWKatalogu;      // zaznacza agenta o podanym folderze w otwartym oknie katalogu

        void PokazKatalogAgentow() { PokazKatalogAgentow(null); }

        // folderAgenta != null: okno otwiera się na tym agencie (wejście z „Skonfiguruj agenta”).
        void PokazKatalogAgentow(string folderAgenta)
        {
            if (!string.IsNullOrEmpty(folderAgenta))
            {
                folderAgenta = folderAgenta.TrimEnd('\\', '/');
                bool maInstrukcje = File.Exists(Path.Combine(folderAgenta, "AGENTS.md")) || File.Exists(Path.Combine(folderAgenta, "CLAUDE.md"));
                if (!maInstrukcje)
                {
                    MessageBox.Show(form, string.Format(Jezyk.T("Folder {0} nie zawiera pliku instrukcji agenta (AGENTS.md albo CLAUDE.md), więc nie ma tu czego konfigurować.\n\nUtwórz agenta w katalogu agentów i wskaż jego folder jako katalog pracy."), folderAgenta),
                        Jezyk.T("Katalog agentów"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                    folderAgenta = null;
                }
                else
                {
                    var rodzic = Path.GetDirectoryName(folderAgenta) ?? "";
                    if (!string.Equals(rodzic.TrimEnd('\\'), katalogAgentow.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    {
                        if (katalogAgentow.Length > 0 && MessageBox.Show(form, string.Format(Jezyk.T("Ten agent leży w {0}, poza bieżącym katalogiem roboczym agentów ({1}).\n\nPrzełączyć katalog roboczy na {0}?"), rodzic, katalogAgentow),
                                Jezyk.T("Katalog agentów"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                        if (oknoKatalogu != null && !oknoKatalogu.IsDisposed) oknoKatalogu.Close();
                        if (oknoKatalogu != null && !oknoKatalogu.IsDisposed) return;      // niezapisane zmiany – zamknięcie anulowane
                        UstawKatalogAgentow(rodzic);
                    }
                }
            }
            if (oknoKatalogu != null && !oknoKatalogu.IsDisposed)
            {
                if (oknoKatalogu.WindowState == FormWindowState.Minimized) oknoKatalogu.WindowState = FormWindowState.Normal;
                oknoKatalogu.Activate();
                if (folderAgenta != null && wybierzWKatalogu != null) wybierzWKatalogu(folderAgenta);
                return;
            }
            var f = oknoKatalogu = new Form { Name = "oknoKatalogu", Text = Jezyk.T("🗂 Katalog agentów") + " – ECXP-MAILBOX", Size = new Size(1180, 760), MinimumSize = new Size(820, 520),
                StartPosition = FormStartPosition.CenterScreen, Icon = form.Icon, Font = form.Font, KeyPreview = true };

            // --- góra: katalog roboczy ---
            var gora = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, WrapContents = false, Padding = new Padding(8, 8, 8, 0) };
            var lblKat = new Label { Text = "Katalog roboczy agentów:", AutoSize = true, Margin = new Padding(0, 7, 6, 0), Font = fontB };
            var tbKat = new TextBox { Name = "katalogAgentow", ReadOnly = true, Width = 420, Text = katalogAgentow, Margin = new Padding(0, 3, 6, 0) };
            var bZmien = Btn("Zmień…"); var bEksplorator = Btn("📂  Otwórz"); var bOdswiez = Btn("Odśwież");
            gora.Controls.AddRange(new Control[] { lblKat, tbKat, bZmien, bEksplorator, bOdswiez });

            var podzial = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, Size = new Size(1160, 660), SplitterDistance = 430, Panel1MinSize = 300, Panel2MinSize = 380 };

            // --- lewa strona: agenci i ich pliki ---
            var lista = new ListView { Name = "listaAgentowKatalogu", View = View.Details, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Top, Height = 190, MultiSelect = false };
            lista.Columns.Add("Agent", 150); lista.Columns.Add("Model", 60); lista.Columns.Add("Tożsamość ECXP", 110); lista.Columns.Add("W ECXP", 200);
            var pasekAg = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, WrapContents = false, Padding = new Padding(4, 4, 4, 0) };
            var bDodaj = Btn("＋  Dodaj agenta…"); bDodaj.Font = fontB; bDodaj.BackColor = Zolty; bDodaj.FlatStyle = FlatStyle.Flat;
            var bUruchom = Btn("▶  Uruchom w ECXP…"); var bFolder = Btn("📂  Folder");
            pasekAg.Controls.AddRange(new Control[] { bDodaj, bUruchom, bFolder });
            var drzewo = new TreeView { Name = "plikiAgenta", Dock = DockStyle.Fill, HideSelection = false, ShowLines = false, FullRowSelect = true, BorderStyle = BorderStyle.None, ItemHeight = Math.Max(22, fontN.Height + 6) };
            var pasekPl = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, WrapContents = false, Padding = new Padding(4, 4, 4, 0) };
            var bNowy = Btn("＋  Nowy skrypt…"); var bUsun = Btn("🗑  Usuń skrypt…");
            pasekPl.Controls.AddRange(new Control[] { bNowy, bUsun });
            podzial.Panel1.Controls.Add(drzewo); podzial.Panel1.Controls.Add(Naglowek("PLIKI AGENTA")); podzial.Panel1.Controls.Add(pasekPl);
            podzial.Panel1.Controls.Add(pasekAg); podzial.Panel1.Controls.Add(lista); podzial.Panel1.Controls.Add(Naglowek("AGENCI"));

            // --- prawa strona: edytor ---
            var lblPlik = new Label { Name = "edytowanyPlik", Dock = DockStyle.Top, Height = 30, Padding = new Padding(8, 7, 8, 0), Font = fontB, AutoEllipsis = true, Text = Jezyk.T("Wybierz plik agenta z listy po lewej.") };
            var edytor = new TextBox { Name = "edytorPliku", Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, AcceptsReturn = true, AcceptsTab = true,
                Font = new Font("Consolas", 10.5f), MaxLength = 0, ReadOnly = true, BorderStyle = BorderStyle.None };
            var pasekEd = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            var bZapisz = Btn("💾  Zapisz (Ctrl+S)"); bZapisz.Font = fontB; bZapisz.Enabled = false; bZapisz.Name = "zapiszPlik";
            var bCofnij = Btn("Cofnij zmiany"); bCofnij.Enabled = false;
            var chZawijaj = new CheckBox { Text = "Zawijaj wiersze", AutoSize = true, Margin = new Padding(12, 9, 6, 0) };
            var lblStan = new Label { AutoSize = true, Margin = new Padding(12, 10, 6, 0), ForeColor = Motyw.Slaby };
            pasekEd.Controls.AddRange(new Control[] { bZapisz, bCofnij, chZawijaj, lblStan });
            podzial.Panel2.Controls.Add(edytor); podzial.Panel2.Controls.Add(lblPlik); podzial.Panel2.Controls.Add(pasekEd);
            f.Controls.Add(podzial); f.Controls.Add(gora);

            List<AgentKatalogu> agenci = new List<AgentKatalogu>();
            AgentKatalogu wybrany = null; PlikAgenta otwarty = null; KatalogAgentow.Tresc wzor = null; string oryginal = ""; bool ladowanie = false;

            Func<bool> zmieniony = () => otwarty != null && !otwarty.TylkoOdczyt && edytor.Text != oryginal;
            Action stan = () =>
            {
                bool zm = zmieniony();
                bZapisz.Enabled = zm; bCofnij.Enabled = zm;
                lblPlik.Text = otwarty == null ? Jezyk.T("Wybierz plik agenta z listy po lewej.") : (zm ? "● " : "") + otwarty.Wzgledna + (otwarty.TylkoOdczyt ? "   " + Jezyk.T("(tylko odczyt)") : "");
                bUsun.Enabled = otwarty != null && otwarty.Grupa == KatalogAgentow.GrupaSkrypty;
                bNowy.Enabled = bUruchom.Enabled = bFolder.Enabled = wybrany != null;
            };
            // true = można opuścić bieżący plik (zapisano, odrzucono zmiany albo nie było zmian)
            Func<bool> zapisz = null;
            Func<bool> opuscPlik = () =>
            {
                if (!zmieniony()) return true;
                var r = MessageBox.Show(f, string.Format(Jezyk.T("Plik „{0}” ma niezapisane zmiany. Zapisać je?"), otwarty.Wzgledna), Jezyk.T("Katalog agentów"), MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) return false;
                return r == DialogResult.No || zapisz();
            };
            Action<PlikAgenta> otworz = pl =>
            {
                ladowanie = true;
                try
                {
                    if (pl == null) { otwarty = null; wzor = null; oryginal = ""; edytor.Text = ""; edytor.ReadOnly = true; return; }
                    try
                    {
                        wzor = KatalogAgentow.Czytaj(wybrany, pl.Sciezka);
                        otwarty = pl; oryginal = wzor.Tekst; edytor.Text = wzor.Tekst; edytor.ReadOnly = pl.TylkoOdczyt; edytor.Select(0, 0);
                        lblStan.Text = (pl.Rozmiar / 1024.0).ToString("0.0") + " KB · " + (wzor.KoniecWiersza == "\n" ? "LF" : "CRLF") + (wzor.Bom ? " · BOM" : "");
                    }
                    catch (Exception ex) { otwarty = null; wzor = null; oryginal = ""; edytor.Text = ""; edytor.ReadOnly = true; lblStan.Text = ""; MessageBox.Show(f, ex.Message, Jezyk.T("Katalog agentów"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }
                finally { ladowanie = false; stan(); }
            };
            Action<string> pokazPliki = zaznacz =>
            {
                drzewo.BeginUpdate();
                try
                {
                    drzewo.Nodes.Clear();
                    if (wybrany == null) return;
                    foreach (var g in KatalogAgentow.Pliki(wybrany).GroupBy(x => x.Grupa))
                    {
                        var wezel = new TreeNode(Jezyk.T(g.Key)) { NodeFont = fontB };
                        foreach (var pl in g) wezel.Nodes.Add(new TreeNode(pl.Grupa == KatalogAgentow.GrupaSkrypty || pl.Grupa == KatalogAgentow.GrupaRaporty ? pl.Wzgledna.Substring(pl.Wzgledna.IndexOf(Path.DirectorySeparatorChar) + 1) : pl.Wzgledna) { Tag = pl, Name = pl.Wzgledna });
                        drzewo.Nodes.Add(wezel);
                        if (g.Key != KatalogAgentow.GrupaRaporty) wezel.Expand();
                    }
                    if (!drzewo.Nodes.Cast<TreeNode>().Any(x => x.Text == Jezyk.T(KatalogAgentow.GrupaSkrypty)))
                        drzewo.Nodes.Add(new TreeNode(Jezyk.T(KatalogAgentow.GrupaSkrypty) + " – " + Jezyk.T("brak (dodaj przyciskiem „Nowy skrypt…”)")) { NodeFont = fontB });
                    var doZazn = zaznacz != null ? drzewo.Nodes.Find(zaznacz, true).FirstOrDefault() : null;
                    if (doZazn != null) drzewo.SelectedNode = doZazn;
                }
                finally { drzewo.EndUpdate(); }
            };
            Action<string> odswiez = zaznaczFolder =>
            {
                tbKat.Text = katalogAgentow.Length > 0 ? katalogAgentow : Jezyk.T("(nie wybrano)");
                agenci = KatalogAgentow.Znajdz(katalogAgentow);
                ladowanie = true;
                lista.BeginUpdate();
                try
                {
                    lista.Items.Clear();
                    foreach (var a in agenci)
                    {
                        var it = new ListViewItem(a.Nazwa) { Tag = a, ToolTipText = a.Folder };
                        it.SubItems.Add(a.Strona == "codex" ? "Codex" : "Claude"); it.SubItems.Add(a.Tozsamosc.Length > 0 ? a.Tozsamosc : "—"); it.SubItems.Add(StanWEcxp(a));
                        lista.Items.Add(it);
                        if (zaznaczFolder != null && string.Equals(a.Folder, zaznaczFolder, StringComparison.OrdinalIgnoreCase)) it.Selected = true;
                    }
                }
                finally { lista.EndUpdate(); ladowanie = false; }
                if (lista.SelectedItems.Count == 0)
                {
                    wybrany = null; pokazPliki(null); otworz(null);
                    if (katalogAgentow.Length == 0) lblPlik.Text = Jezyk.T("Wybierz katalog roboczy agentów przyciskiem „Zmień…”.");
                    else if (agenci.Count == 0) lblPlik.Text = Jezyk.T("W tym katalogu nie ma agentów (folderów z AGENTS.md albo CLAUDE.md). Dodaj pierwszego przyciskiem „Dodaj agenta…”.");
                }
                bDodaj.Enabled = katalogAgentow.Length > 0 && Directory.Exists(katalogAgentow);
                bEksplorator.Enabled = bDodaj.Enabled;
            };
            zapisz = () =>
            {
                if (!zmieniony()) return true;
                try
                {
                    KatalogAgentow.Zapisz(wybrany, otwarty.Sciezka, edytor.Text, wzor);
                    oryginal = edytor.Text; otwarty.Rozmiar = new FileInfo(otwarty.Sciezka).Length;
                    lblStan.Text = Jezyk.T("Zapisano ") + DateTime.Now.ToString("HH:mm:ss") + " · " + Jezyk.T("poprzednia wersja w folderze .kopie");
                    stan();
                    if (otwarty.Wzgledna.Equals("agent-info.json", StringComparison.OrdinalIgnoreCase)) odswiez(wybrany.Folder);
                    return true;
                }
                catch (Exception ex) { MessageBox.Show(f, Jezyk.T("Nie zapisano: ") + ex.Message, Jezyk.T("Katalog agentów"), MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
            };

            lista.SelectedIndexChanged += delegate
            {
                if (ladowanie) return;
                var nowy = lista.SelectedItems.Count > 0 ? (AgentKatalogu)lista.SelectedItems[0].Tag : null;
                if (nowy == wybrany) return;
                if (!opuscPlik())
                {
                    ladowanie = true;
                    try { foreach (ListViewItem it in lista.Items) it.Selected = it.Tag == wybrany; } finally { ladowanie = false; }
                    return;
                }
                wybrany = nowy; otworz(null);
                pokazPliki(wybrany != null ? wybrany.PlikInstrukcji : null);
            };
            drzewo.BeforeSelect += (s, e) => { if (!ladowanie && e.Node != null && e.Node.Tag is PlikAgenta && e.Node.Tag != otwarty && !opuscPlik()) e.Cancel = true; };
            drzewo.AfterSelect += (s, e) => { var pl = e.Node != null ? e.Node.Tag as PlikAgenta : null; if (pl != null && pl != otwarty) otworz(pl); };
            edytor.TextChanged += delegate { if (!ladowanie) stan(); };
            chZawijaj.CheckedChanged += delegate { edytor.WordWrap = chZawijaj.Checked; edytor.ScrollBars = chZawijaj.Checked ? ScrollBars.Vertical : ScrollBars.Both; };
            bZapisz.Click += delegate { zapisz(); };
            bCofnij.Click += delegate { if (zmieniony()) { ladowanie = true; edytor.Text = oryginal; ladowanie = false; stan(); } };
            f.KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.S) { e.Handled = true; e.SuppressKeyPress = true; zapisz(); } };
            bOdswiez.Click += delegate { if (opuscPlik()) odswiez(wybrany != null ? wybrany.Folder : null); };
            bEksplorator.Click += delegate { if (Directory.Exists(katalogAgentow)) OpenPath(katalogAgentow); };
            bFolder.Click += delegate { if (wybrany != null && Directory.Exists(wybrany.Folder)) OpenPath(wybrany.Folder); };
            bZmien.Click += delegate
            {
                if (!opuscPlik()) return;
                using (var d = new FolderBrowserDialog { Description = Jezyk.T("Katalog roboczy z folderami agentów Codex i Claude"), SelectedPath = Directory.Exists(katalogAgentow) ? katalogAgentow : "", ShowNewFolderButton = true })
                    if (d.ShowDialog(f) == DialogResult.OK) { UstawKatalogAgentow(d.SelectedPath); odswiez(null); }
            };
            bDodaj.Click += delegate
            {
                if (!opuscPlik()) return;
                var nowy = DodajAgentaKatalogu(f, agenci);
                if (nowy != null) odswiez(nowy.Folder);
            };
            bUruchom.Click += delegate { if (wybrany != null) { UruchomAgentaZKatalogu(f, wybrany); odswiez(wybrany.Folder); } };
            bNowy.Click += delegate
            {
                if (wybrany == null || !opuscPlik()) return;
                using (var d = new Formularz(Jezyk.T("Nowy skrypt") + " – " + wybrany.Nazwa, fontN, fontB))
                {
                    d.Size = new Size(620, 260);
                    var tb = d.Tekst("Nazwa pliku w scripts\\:", "nowy_skrypt.py");
                    d.Uwaga("Litery, cyfry, kropka, myślnik i podkreślenie. Rozszerzenie decyduje o szablonie początku pliku (.py, .ps1, .sh).");
                    d.PrzyciskOk("Utwórz");
                    if (d.ShowDialog(f) != DialogResult.OK) return;
                    try { var p = KatalogAgentow.NowySkrypt(wybrany, tb.Text); pokazPliki(Path.Combine("scripts", Path.GetFileName(p))); }
                    catch (Exception ex) { MessageBox.Show(f, ex.Message, Jezyk.T("Nowy skrypt"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }
            };
            bUsun.Click += delegate
            {
                if (wybrany == null || otwarty == null || otwarty.Grupa != KatalogAgentow.GrupaSkrypty) return;
                if (MessageBox.Show(f, string.Format(Jezyk.T("Usunąć skrypt „{0}”?\n\nPlik trafi do folderu .kopie tego agenta, skąd można go odzyskać."), otwarty.Wzgledna), Jezyk.T("Usuń skrypt"),
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                try { KatalogAgentow.UsunSkrypt(wybrany, otwarty.Sciezka); otworz(null); pokazPliki(null); }
                catch (Exception ex) { MessageBox.Show(f, ex.Message, Jezyk.T("Usuń skrypt"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };
            f.FormClosing += (s, e) => { if (!opuscPlik()) e.Cancel = true; };
            f.FormClosed += delegate { oknoKatalogu = null; wybierzWKatalogu = null; };
            wybierzWKatalogu = folder =>
            {
                if (!opuscPlik()) return;
                foreach (ListViewItem it in lista.Items) it.Selected = string.Equals(((AgentKatalogu)it.Tag).Folder.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            };

            Motyw.Zastosuj(f); Jezyk.Zastosuj(f);
            edytor.BackColor = Motyw.Pole; edytor.ForeColor = Motyw.Tekst; drzewo.BackColor = Motyw.Pole; drzewo.ForeColor = Motyw.Tekst;
            odswiez(null);
            if (folderAgenta != null) wybierzWKatalogu(folderAgenta);
            if (lista.SelectedItems.Count == 0 && lista.Items.Count > 0) lista.Items[0].Selected = true;
            stan();
            f.Show();
        }

        void UstawKatalogAgentow(string sciezka)
        {
            katalogAgentow = (sciezka ?? "").Trim().TrimEnd('\\', '/');
            if (katalogAgentow.Length == 2 && katalogAgentow[1] == ':') katalogAgentow += "\\";
            ZapiszUstawienia();
        }

        // Formularz nowego agenta. Zwraca utworzonego agenta albo null.
        AgentKatalogu DodajAgentaKatalogu(Form rodzic, List<AgentKatalogu> istniejacy)
        {
            using (var d = new Formularz(Jezyk.T("Dodaj agenta") + " – " + katalogAgentow, fontN, fontB))
            {
                d.Size = new Size(760, 520);
                var cbStrona = d.Combo("Model (LLM):", new List<string> { "Codex", "Claude" }, "Claude");
                var tbModul = d.Tekst("Moduł (specjalizacja):", "");
                var tbFolder = d.Tekst("Nazwa folderu agenta:", "");
                var tbNazwa = d.Tekst("Nazwa wyświetlana:", "");
                var wzory = new List<string> { Jezyk.T("(pusty szkielet instrukcji)") }.Concat(istniejacy.Select(x => x.Nazwa + "  [" + x.NazwaFolderu + "]")).ToList();
                var cbWzor = d.Combo("Skopiuj konfigurację z:", wzory, wzory[0]);
                d.Uwaga("Powstanie folder agenta z plikiem instrukcji (AGENTS.md dla Codex, CLAUDE.md dla Claude), scope.md, ECXP-START.txt, agent-info.json oraz podfolderami input, scripts, tests i reports. " +
                        "Tożsamość ECXP to <model>-<moduł>, np. claude-cyber. Kopiowanie konfiguracji zamienia ścieżki, nazwę i tożsamość na nowe. Samo utworzenie nie uruchamia agenta – zrób to przyciskiem „Uruchom w ECXP…”.");
                bool recznyFolder = false, recznaNazwa = false, auto = false;
                Action podpowiedz = () =>
                {
                    auto = true;
                    var st = cbStrona.Text.ToLowerInvariant(); var mo = tbModul.Text.Trim().ToLowerInvariant();
                    if (!recznyFolder) tbFolder.Text = mo.Length > 0 ? st + "-" + mo + "-agent" : "";
                    if (!recznaNazwa) tbNazwa.Text = mo.Length > 0 ? char.ToUpper(mo[0]) + mo.Substring(1) + " — " + cbStrona.Text : "";
                    auto = false;
                };
                tbModul.TextChanged += delegate { podpowiedz(); }; cbStrona.TextChanged += delegate { podpowiedz(); };
                tbFolder.TextChanged += delegate { if (!auto) recznyFolder = tbFolder.Text.Length > 0; };
                tbNazwa.TextChanged += delegate { if (!auto) recznaNazwa = tbNazwa.Text.Length > 0; };
                d.PrzyciskOk("＋  Utwórz agenta");
                while (d.ShowDialog(rodzic) == DialogResult.OK)
                {
                    try
                    {
                        var wzor = cbWzor.SelectedIndex > 0 && cbWzor.SelectedIndex - 1 < istniejacy.Count ? istniejacy[cbWzor.SelectedIndex - 1] : null;
                        var a = KatalogAgentow.Utworz(katalogAgentow, tbFolder.Text, cbStrona.Text.ToLowerInvariant(), tbNazwa.Text, tbModul.Text, wzor);
                        stInfo.Text = Jezyk.T("Utworzono agenta: ") + a.Folder;
                        return a;
                    }
                    catch (Exception ex) { MessageBox.Show(rodzic, ex.Message, Jezyk.T("Dodaj agenta"), MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }
                return null;
            }
        }

        // Agent z katalogu jako agent w tle ECXP: katalog pracy = folder agenta (CLI czyta z niego AGENTS.md / CLAUDE.md).
        void UruchomAgentaZKatalogu(Form rodzic, AgentKatalogu a)
        {
            using (var d = new Formularz(Jezyk.T("Uruchom w ECXP") + " – " + a.Nazwa, fontN, fontB))
            {
                var projekty = P.Projekty().Select(x => P.S(x, "nazwa")).ToList();
                var domyslnyProjekt = Siec.Trasy().Where(x => x.Aktywna && x.Nazwa == a.Tozsamosc).Select(x => x.Projekt).FirstOrDefault() ?? (a.Modul.Length > 0 ? a.Modul : "");
                var tbN = d.Tekst("Nazwa (tożsamość ECXP):", a.Tozsamosc.Length > 0 ? a.Tozsamosc : a.Strona + "-" + (a.Modul.Length > 0 ? a.Modul : a.NazwaFolderu));
                var cbP = d.Combo("Projekt:", projekty, domyslnyProjekt);
                var tbM = d.Tekst("Moduł:", a.Modul);
                var tbK = d.Tekst("Katalog pracy:", a.Folder); tbK.ReadOnly = true;
                var cbU = d.Combo("Uprawnienia:", Agenci.Uprawnienia.ToList(), "edycja");
                var cbMo = d.Combo("Model (puste = domyślny):", a.Strona == "claude" ? new List<string> { "", "opus", "sonnet", "haiku" } : new List<string> { "" }, "");
                var tbB = d.Tekst("Najwięcej tur na godzinę:", "6");
                var tbO = d.Tekst("Opis od operatora:", a.Nazwa + ", katalog " + a.Folder);
                d.Uwaga("Agent zostanie zarejestrowany w ECXP jako agent w tle z katalogiem pracy " + a.Folder + " – " + (a.Strona == "codex" ? "Codex CLI" : "Claude Code CLI") + " czyta stamtąd " + a.PlikInstrukcji + ". " +
                        "Stos uruchamia go, gdy w jego kolejce czekają wiadomości. Uprawnienia: odczyt – tylko czyta; edycja – zmienia pliki w katalogu pracy; pelne – także polecenia systemowe " +
                        "(Claude w tle uruchamia skrypty tylko przy „pelne”). Jeśli ten adres ma już sesję w aplikacji, stos odmówi i poda powód.");
                d.PrzyciskOk("▶  Uruchom agenta");
                if (d.ShowDialog(rodzic) != DialogResult.OK) return;
                try
                {
                    Stos("uruchom_agenta", P.D("nazwa", tbN.Text, "projekt", cbP.Text, "modul", tbM.Text, "katalog", a.Folder, "uprawnienia", cbU.Text, "model", cbMo.Text,
                        "na_godzine", tbB.Text, "role", new List<string>(), "opis", tbO.Text));
                    stInfo.Text = Jezyk.T("Agent ") + tbN.Text + Jezyk.T(" działa w tle – odbierze wiadomości, gdy przyjdą.");
                    Skanuj();
                }
                catch (Exception ex) { Bledy.Wyjatek(ex); MessageBox.Show(rodzic, Jezyk.T("Nie uruchomiono: ") + ex.Message, Jezyk.T("Uruchom w ECXP"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }
    }
}
