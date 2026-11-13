// ECXP-MAILBOX – drzewko folderów zakładki Rozmowy (układ jak w programie pocztowym): po lewej foldery
// Wszystkie / Odebrane / Wysłane / Niewysłane / Zarchiwizowane / Kosz z podfolderami projektów, w środku lista, po prawej podgląd.
// Folder to dotychczasowy „kierunek” (0–3) oraz 4 = Zarchiwizowane (archiwum\RRRR-MM) i 5 = Kosz (kosz\RRRR-MM).
// Foldery 4 i 5 są tylko do odczytu; wiadomości wracają z nich do bieżących poleceniem stosu „przywroc”.
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
        public const int FolderArchiwum = 4, FolderKosz = 5, MaksRozmowArchiwum = 400;
        TreeView drzewoFolderow;
        RadioButton rbArch, rbKosz;
        FlowLayoutPanel pasekKierunkow;
        string projektDrzewa;                        // podfolder projektu wybrany w drzewku (null = cały folder)
        bool ustawianieDrzewa, wyborZDrzewa;
        // foldery tylko do odczytu: wiadomości folderu razem z bieżącymi wiadomościami tych samych rozmów
        readonly Dictionary<int, List<Msg>> odczyt = new Dictionary<int, List<Msg>> { { FolderArchiwum, new List<Msg>() }, { FolderKosz, new List<Msg>() } };
        readonly Dictionary<int, string> podpisOdczytu = new Dictionary<int, string>();
        readonly Dictionary<int, KeyValuePair<DateTime, int>> liczbyOdczytu = new Dictionary<int, KeyValuePair<DateTime, int>>();
        static readonly string[] IkonyFolderow = { "📬", "📥", "📤", "📝", "🗄", "🗑" };
        static readonly string[] NazwyFolderow = { "Wszystkie", "Odebrane", "Wysłane", "Niewysłane", "Zarchiwizowane", "Kosz" };

        bool FolderOdczytu { get { return kierunek >= FolderArchiwum; } }
        static string KatalogFolderu(int k) { return k == FolderKosz ? P.Kosz : "archiwum"; }

        // Wiadomości pokazywanego folderu: bieżące albo (Zarchiwizowane / Kosz) wiadomości folderu z resztą tych rozmów.
        List<Msg> Zrodlo { get { return FolderOdczytu ? odczyt[kierunek] : msgs; } }

        int LiczbaPlikow(int k)
        {
            KeyValuePair<DateTime, int> kv;
            if (liczbyOdczytu.TryGetValue(k, out kv) && DateTime.Now - kv.Key < TimeSpan.FromSeconds(30)) return kv.Value;
            int n = 0;
            try { var d = Path.Combine(P.Root, KatalogFolderu(k)); n = Directory.Exists(d) ? Directory.GetFiles(d, "*.json", SearchOption.AllDirectories).Length : 0; } catch { }
            liczbyOdczytu[k] = new KeyValuePair<DateTime, int>(DateTime.Now, n);
            return n;
        }

        // Folder czytany tylko po wejściu do niego i tylko gdy zmieniła się liczba plików albo bieżące wiadomości.
        void WczytajFolderOdczytu(int k, bool wymus = false)
        {
            try
            {
                liczbyOdczytu.Remove(k);
                var podpis = LiczbaPlikow(k) + "|" + msgs.Count + "|" + (msgs.Count > 0 ? msgs.Max(m => m.Czas).Ticks : 0);
                string stary;
                if (!wymus && podpisOdczytu.TryGetValue(k, out stary) && stary == podpis) return;
                var kat = KatalogFolderu(k);
                var wszystkie = k == FolderKosz ? P.Wszystkie(false).Concat(P.ZKosza()).ToList() : P.Wszystkie(true);
                P.Powiaz(wszystkie);
                var watki = new HashSet<string>(wszystkie.Where(m => m.Katalog == kat).Select(m => m.Watek));
                odczyt[k] = wszystkie.Where(m => watki.Contains(m.Watek)).ToList();
                podpisOdczytu[k] = podpis;
            }
            catch (Exception ex) { Bledy.Wyjatek(ex); }
        }

        void BudujDrzewoFolderow(Control panel)
        {
            drzewoFolderow = new TreeView { Name = "drzewoFolderow", Dock = DockStyle.Fill, HideSelection = false, ShowLines = false, ShowRootLines = true, FullRowSelect = true,
                BorderStyle = BorderStyle.None, ItemHeight = Math.Max(26, fontB.Height + 10), Font = fontB, Indent = 18 };   // czcionka drzewka pogrubiona: węzły z NodeFont nie są wtedy przycinane
            for (int k = 0; k < NazwyFolderow.Length; k++)
                drzewoFolderow.Nodes.Add(new TreeNode(IkonyFolderow[k] + "  " + Jezyk.T(NazwyFolderow[k])) { Name = k.ToString(), Tag = k, NodeFont = fontN });
            drzewoFolderow.AfterSelect += (s, e) =>
            {
                if (ustawianieDrzewa || e.Node == null) return;
                int k = e.Node.Parent != null ? (int)e.Node.Parent.Tag : (int)e.Node.Tag;
                WybierzFolder(k, e.Node.Parent != null ? (string)e.Node.Tag : null);
            };
            panel.Controls.Add(drzewoFolderow);
            drzewoFolderow.SelectedNode = drzewoFolderow.Nodes[0];
        }

        RadioButton PrzyciskFolderu(int k) { return k == 0 ? rbWsz : k == 1 ? rbOdeb : k == 2 ? rbWysl : k == 3 ? rbNiewyslane : k == FolderKosz ? rbKosz : rbArch; }

        // Wybór folderu (z drzewka albo z paska przycisków przy wąskim oknie).
        void WybierzFolder(int k, string projekt)
        {
            bool zmianaProjektu = projektDrzewa != projekt;
            projektDrzewa = projekt;
            if (k >= FolderArchiwum) WczytajFolderOdczytu(k);
            var rb = PrzyciskFolderu(k);
            if (rb != null && !rb.Checked) { wyborZDrzewa = true; try { rb.Checked = true; } finally { wyborZDrzewa = false; } }   // zmiana kierunku odświeża listę (Kier)
            else if (zmianaProjektu)
            {
                podpisListy = ""; podpisNiewyslanych = "";
                RefreshView();
                if (kierunek == 3) PokazNiewyslana(true);
            }
            ZaznaczFolderWDrzewie();
        }

        void ZaznaczFolderWDrzewie()
        {
            if (drzewoFolderow == null) return;
            var n = drzewoFolderow.Nodes[kierunek.ToString()];
            if (n == null) return;
            if (projektDrzewa != null) n = n.Nodes.Cast<TreeNode>().FirstOrDefault(x => (string)x.Tag == projektDrzewa) ?? n;
            if (drzewoFolderow.SelectedNode == n) return;
            ustawianieDrzewa = true;
            try { drzewoFolderow.SelectedNode = n; } finally { ustawianieDrzewa = false; }
        }

        static void UstawWezel(TreeNode n, string tekst, Font f)
        {
            if (n.Text != tekst) n.Text = tekst;
            if (n.NodeFont != f) n.NodeFont = f;
        }

        // Rozmowy folderu tylko do odczytu (po filtrach, bez podfolderu projektu).
        List<List<Msg>> RozmowyOdczytu(int k)
        {
            var kat = KatalogFolderu(k);
            return odczyt[k].Where(PasujeBezFolderu).GroupBy(m => m.Watek).Select(g => g.OrderBy(m => m.Czas).ToList()).Where(w => w.Any(m => m.Katalog == kat)).ToList();
        }

        // Liczniki i podfoldery projektów. rozmowy: bieżące rozmowy po filtrach (bez podfolderu projektu), każda jako lista wiadomości.
        void OdswiezDrzewoFolderow(List<List<Msg>> rozmowy, int niewyslane, Dictionary<string, int> niewyslaneProjekty)
        {
            if (drzewoFolderow == null) return;
            var foldery = new Dictionary<int, List<List<Msg>>> {
                { 0, rozmowy }, { 1, rozmowy.Where(w => w.Any(m => !Wyslana(m))).ToList() }, { 2, rozmowy.Where(w => w.Any(Wyslana)).ToList() } };
            if (FolderOdczytu) foldery[kierunek] = RozmowyOdczytu(kierunek);
            ustawianieDrzewa = true;
            drzewoFolderow.BeginUpdate();
            try
            {
                for (int k = 0; k < NazwyFolderow.Length; k++)
                {
                    var n = drzewoFolderow.Nodes[k.ToString()];
                    string licznik; bool pogrub = false;
                    Dictionary<string, int> projekty = null; Dictionary<string, int> noweP = null;
                    bool nieotwarty = k >= FolderArchiwum && !foldery.ContainsKey(k);
                    if (k == 3) { licznik = niewyslane.ToString(); projekty = niewyslaneProjekty; pogrub = niewyslane > 0; }
                    else if (nieotwarty) licznik = LiczbaPlikow(k).ToString();     // przed otwarciem: liczba plików
                    else
                    {
                        var f = foldery[k];
                        int nowe = k <= 1 ? f.Count(w => w.Any(m => m.Nowa)) : 0;
                        licznik = f.Count + (nowe > 0 ? ", " + Jezyk.T("nowe: ") + nowe : "");
                        pogrub = nowe > 0;
                        if (k != 0)
                        {
                            projekty = f.GroupBy(w => w[0].Projekt.Length > 0 ? w[0].Projekt : "").ToDictionary(g => g.Key, g => g.Count());
                            noweP = k == 1 ? f.GroupBy(w => w[0].Projekt.Length > 0 ? w[0].Projekt : "").ToDictionary(g => g.Key, g => g.Count(w => w.Any(m => m.Nowa))) : null;
                        }
                    }
                    UstawWezel(n, IkonyFolderow[k] + "  " + Jezyk.T(NazwyFolderow[k]) + "  (" + licznik + ")", pogrub ? fontB : fontN);
                    if (projekty == null) { if (nieotwarty) continue; n.Nodes.Clear(); continue; }
                    // podfoldery projektów – tylko gdy jest więcej niż jeden projekt
                    var nazwy = projekty.Keys.Where(x => projekty[x] > 0).OrderBy(x => x).ToList();
                    if (nazwy.Count <= 1 && !(projektDrzewa != null && kierunek == k)) nazwy.Clear();
                    if (projektDrzewa != null && kierunek == k && !nazwy.Contains(projektDrzewa)) nazwy.Add(projektDrzewa);
                    foreach (var stary in n.Nodes.Cast<TreeNode>().Where(x => !nazwy.Contains((string)x.Tag)).ToList()) n.Nodes.Remove(stary);
                    foreach (var p in nazwy)
                    {
                        var c = n.Nodes.Cast<TreeNode>().FirstOrDefault(x => (string)x.Tag == p);
                        if (c == null) { c = new TreeNode { Tag = p }; n.Nodes.Insert(nazwy.IndexOf(p), c); }
                        int ile; projekty.TryGetValue(p, out ile);
                        int nw = 0; if (noweP != null) noweP.TryGetValue(p, out nw);
                        UstawWezel(c, (p.Length > 0 ? IkP(p) + p : Jezyk.T("(bez projektu)")) + "  (" + ile + (nw > 0 ? ", " + Jezyk.T("nowe: ") + nw : "") + ")", nw > 0 ? fontB : fontN);
                    }
                    if (kierunek == k && n.Nodes.Count > 0 && !n.IsExpanded) n.Expand();
                }
            }
            finally { drzewoFolderow.EndUpdate(); ustawianieDrzewa = false; }
            ZaznaczFolderWDrzewie();
            drzewoFolderow.BackColor = Motyw.Pole; drzewoFolderow.ForeColor = Motyw.Tekst;
        }

        // Przywrócenie zaznaczonych rozmów z archiwum albo kosza do bieżących (polecenie stosu „przywroc”).
        void PrzywrocWatki()
        {
            if (!FolderOdczytu) return;
            var kat = KatalogFolderu(kierunek);
            var watki = new HashSet<string>(lv.SelectedItems.Cast<ListViewItem>().Select(it => ((Msg)it.Tag).Watek));
            var lista = odczyt[kierunek].Where(x => watki.Contains(x.Watek) && x.Katalog == kat).ToList();
            if (lista.Count == 0) return;
            if (MessageBox.Show(form, string.Format(Jezyk.T("Przywrócić do bieżących rozmowy ({0}) – wiadomości: {1}?\n\nWrócą do listy jako przeczytane; nic nie zostanie wysłane ponownie."),
                    watki.Count, lista.Count), Jezyk.T("Przywróć"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                var r = (Dictionary<string, object>)Stos("przywroc", P.D("pliki", lista.Select(x => x.Plik).ToList()));
                foreach (var x in lista) read.Add(x.Plik);
                SaveRead();
                det.Clear(); pnlZal.Controls.Clear(); pnlZal.Visible = false;
                Skanuj();
                WczytajFolderOdczytu(kierunek, true); podpisListy = ""; RefreshView();
                stInfo.Text = Jezyk.T("Przywrócono do bieżących: ") + P.S(r, "przywrocone");
            }
            catch (Exception ex) { Bledy.Wyjatek(ex); MessageBox.Show(form, Jezyk.T("Nie przywrócono: ") + ex.Message, Jezyk.T("Przywróć"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
    }
}
