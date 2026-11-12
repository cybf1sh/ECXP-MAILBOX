// ECXP-MAILBOX – drzewko folderów zakładki Rozmowy (układ jak w programie pocztowym): po lewej foldery
// Wszystkie / Odebrane / Wysłane / Niewysłane / Zarchiwizowane z podfolderami projektów, w środku lista, po prawej podgląd.
// Folder to dotychczasowy „kierunek” (0–3) oraz 4 = Zarchiwizowane (wiadomości z archiwum\RRRR-MM, tylko do odczytu).
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
        public const int FolderArchiwum = 4, MaksRozmowArchiwum = 400;
        TreeView drzewoFolderow;
        RadioButton rbArch;
        FlowLayoutPanel pasekKierunkow;
        string projektDrzewa;                        // podfolder projektu wybrany w drzewku (null = cały folder)
        bool ustawianieDrzewa, wyborZDrzewa;
        List<Msg> archiwum = new List<Msg>();        // archiwum razem z bieżącymi wiadomościami tych samych rozmów
        string podpisArchiwum = "";
        int liczbaArchiwum; DateTime liczbaArchiwumCzas = DateTime.MinValue;
        static readonly string[] IkonyFolderow = { "📬", "📥", "📤", "📝", "🗄" };
        static readonly string[] NazwyFolderow = { "Wszystkie", "Odebrane", "Wysłane", "Niewysłane", "Zarchiwizowane" };

        // Wiadomości pokazywanego folderu: bieżące albo (folder Zarchiwizowane) archiwum z resztą tych rozmów.
        List<Msg> Zrodlo { get { return kierunek == FolderArchiwum ? archiwum : msgs; } }

        static string DirArchiwum { get { return Path.Combine(P.Root, "archiwum"); } }

        int LiczbaArchiwum()
        {
            if (DateTime.Now - liczbaArchiwumCzas < TimeSpan.FromSeconds(30)) return liczbaArchiwum;
            liczbaArchiwumCzas = DateTime.Now;
            try { liczbaArchiwum = Directory.Exists(DirArchiwum) ? Directory.GetFiles(DirArchiwum, "*.json", SearchOption.AllDirectories).Length : 0; } catch { }
            return liczbaArchiwum;
        }

        // Archiwum czytane tylko dla folderu Zarchiwizowane i tylko gdy zmieniła się liczba plików albo bieżące wiadomości.
        void WczytajArchiwum(bool wymus = false)
        {
            try
            {
                liczbaArchiwumCzas = DateTime.MinValue;
                var podpis = LiczbaArchiwum() + "|" + msgs.Count + "|" + (msgs.Count > 0 ? msgs.Max(m => m.Czas).Ticks : 0);
                if (!wymus && podpis == podpisArchiwum) return;
                var wszystkie = P.Wszystkie(true);
                P.Powiaz(wszystkie);
                var watki = new HashSet<string>(wszystkie.Where(m => m.Katalog == "archiwum").Select(m => m.Watek));
                archiwum = wszystkie.Where(m => watki.Contains(m.Watek)).ToList();
                podpisArchiwum = podpis;
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

        RadioButton PrzyciskFolderu(int k) { return k == 0 ? rbWsz : k == 1 ? rbOdeb : k == 2 ? rbWysl : k == 3 ? rbNiewyslane : rbArch; }

        // Wybór folderu (z drzewka albo z paska przycisków przy wąskim oknie).
        void WybierzFolder(int k, string projekt)
        {
            bool zmianaProjektu = projektDrzewa != projekt;
            projektDrzewa = projekt;
            if (k == FolderArchiwum) WczytajArchiwum();
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

        // Liczniki i podfoldery projektów. rozmowy: bieżące rozmowy po filtrach (bez podfolderu projektu), każda jako lista wiadomości.
        void OdswiezDrzewoFolderow(List<List<Msg>> rozmowy, int niewyslane, Dictionary<string, int> niewyslaneProjekty)
        {
            if (drzewoFolderow == null) return;
            var foldery = new Dictionary<int, List<List<Msg>>> {
                { 0, rozmowy }, { 1, rozmowy.Where(w => w.Any(m => !Wyslana(m))).ToList() }, { 2, rozmowy.Where(w => w.Any(Wyslana)).ToList() } };
            if (kierunek == FolderArchiwum)
                foldery[FolderArchiwum] = archiwum.Where(PasujeBezFolderu).GroupBy(m => m.Watek).Select(g => g.ToList()).Where(w => w.Any(m => m.Katalog == "archiwum")).ToList();
            ustawianieDrzewa = true;
            drzewoFolderow.BeginUpdate();
            try
            {
                for (int k = 0; k < NazwyFolderow.Length; k++)
                {
                    var n = drzewoFolderow.Nodes[k.ToString()];
                    string licznik; bool pogrub = false;
                    Dictionary<string, int> projekty = null; Dictionary<string, int> noweP = null;
                    if (k == 3) { licznik = niewyslane.ToString(); projekty = niewyslaneProjekty; pogrub = niewyslane > 0; }
                    else if (k == FolderArchiwum && !foldery.ContainsKey(k)) licznik = LiczbaArchiwum().ToString();     // przed otwarciem: liczba plików
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
                    if (projekty == null) { if (k == FolderArchiwum && !foldery.ContainsKey(k)) continue; n.Nodes.Clear(); continue; }
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
    }
}
