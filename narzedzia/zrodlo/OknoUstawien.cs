using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;
using System.Collections.Generic;
namespace Ecxp {
partial class App
{
    void PokazAnalizeAgenta(Form wlasciciel, string projekt, string agent)
    {
        var rekord = Agenci.Lista().FirstOrDefault(a => P.S(a, "projekt") == projekt && P.S(a, "nazwa") == agent);
        var tury = P.Lista(rekord, "uruchomienia");
        var wiadomosci = P.Wszystkie(true).Where(m => m.Blad == null && m.Projekt == projekt &&
            (m.Do == agent || m.Od == agent)).OrderByDescending(m => m.Czas).ToList();
        string spojnosc;
        var stany = P.Stany(out spojnosc);
        var odpowiedzi = wiadomosci.Where(m => m.Od == agent && m.Typ == "fin").ToLookup(m => m.OdpNa);
        var odpowiedziLogiczne = wiadomosci.Where(m => m.Od == agent && m.Typ == "fin" && m.OdpNaLogiczne.Length > 0)
            .ToLookup(m => m.OdpNaLogiczne);
        var proby = tury.Where(t => P.S(t, "praca").Length > 0).ToLookup(t => P.S(t, "praca"));
        using (var okno = new Form { Text = "Analiza pracy — " + agent + " · " + projekt,
            Width = 1120, Height = 720, StartPosition = FormStartPosition.CenterParent, Font = fontN })
        {
            var zakladki = new TabControl { Dock = DockStyle.Fill };
            var pelna = Btn("Analiza wiadomości, decyzji, metod i alternatyw…");
            pelna.Dock = DockStyle.Top;
            pelna.Click += delegate { try { AnalizaObserwatorium.Pokaz(okno, P.Wszystkie(true), projekt, agent, AgenciZeWszystkichKatalogow()); } catch(Exception ex) { MessageBox.Show(okno, ex.Message, okno.Text); } };
            var przebieg = new TabPage("Graf zadań");
            var graf = new TreeView { Dock = DockStyle.Fill, HideSelection = false };
            var opis = new Label { Dock = DockStyle.Top, Height = 48, Padding = new Padding(8),
                Text = "Graf pokazuje zarejestrowane zdarzenia. Brak wpisu oznacza brak dowodu. Model sieci neuronowej wymaga osobnego podglądu danych modelu." };
            przebieg.Controls.Add(graf);
            przebieg.Controls.Add(opis);
            var os = new TabPage("Wiadomości i kontekst");
            var lista = new ListView { View = View.Details, Dock = DockStyle.Fill, FullRowSelect = true };
            lista.Columns.Add("Czas", 135);
            lista.Columns.Add("Kierunek", 105);
            lista.Columns.Add("Typ", 90);
            lista.Columns.Add("Temat", 320);
            lista.Columns.Add("Elementy kontekstu", 145);
            lista.Columns.Add("Wątek / odpowiedź na", 270);
            foreach (var m in wiadomosci)
            {
                var wiersz = new ListViewItem(m.Czas.ToString("dd.MM.yyyy HH:mm")) { Tag = m };
                wiersz.SubItems.Add(m.Od == agent ? "Wysłana" : "Odebrana do agenta");
                wiersz.SubItems.Add(m.Typ);
                wiersz.SubItems.Add(P.TytulWiadomosci(m));
                wiersz.SubItems.Add(m.Czesci.Count.ToString());
                wiersz.SubItems.Add(m.OdpNa.Length > 0 ? m.OdpNa : m.WatekPole);
                lista.Items.Add(wiersz);
            }
            var szczegoly = new TextBox { Dock = DockStyle.Bottom, Height = 155, Multiline = true,
                ReadOnly = true, ScrollBars = ScrollBars.Vertical };
            lista.SelectedIndexChanged += delegate
            {
                if (lista.SelectedItems.Count == 0) { szczegoly.Clear(); return; }
                var m = (Msg)lista.SelectedItems[0].Tag;
                var rodzaje = m.Czesci.GroupBy(c => P.S(c, "rodzaj")).Select(g => g.Key + " × " + g.Count());
                Stan stan;
                stany.TryGetValue(m.Id, out stan);
                szczegoly.Text = "Identyfikator: " + m.Id + Environment.NewLine + "Nadawca: " + m.Od + " → " + m.Do + Environment.NewLine
                    + "Doręczenie: " + (stan == null ? "brak wpisu" : P.Opis(stan, DateTime.Now)) + Environment.NewLine
                    + "Kontekst: " + (m.Czesci.Count == 0 ? "bez części" : string.Join(", ", rodzaje)) + Environment.NewLine
                    + "Zadeklarowane kontrole: " + m.Kontrole.Count + " · pliki: " + m.Pliki.Count + " · wynik: " + (m.Wynik.Length == 0 ? "—" : m.Wynik);
            };
            os.Controls.Add(lista);
            os.Controls.Add(szczegoly);
            foreach (var m in wiadomosci.Where(m => m.Do == agent && m.Typ == "zadanie"))
            {
                Stan stan;
                stany.TryGetValue(m.Id, out stan);
                var powiazane = proby[m.Id].ToList();
                var wynik = odpowiedzi[m.Id].Concat(m.Logiczne.Length == 0 ? Enumerable.Empty<Msg>() : odpowiedziLogiczne[m.Logiczne])
                    .OrderByDescending(x => x.Czas).FirstOrDefault();
                var zadanie = graf.Nodes.Add(m.Czas.ToString("dd.MM.yyyy HH:mm") + "  " + P.TytulWiadomosci(m));
                var odbior = zadanie.Nodes.Add("Zarejestrowano odbiór? " + (stan != null && stan.Dostarczona.HasValue ? "Tak" : "Nie w dzienniku"));
                var tura = odbior.Nodes.Add("Powiązano turę CLI z zadaniem? " + (powiazane.Count > 0 ? "Tak · " + powiazane.Count : "Nie w historii CLI"));
                foreach (var proba in powiazane)
                    tura.Nodes.Add(P.S(proba, "czas") + " · kod " + P.S(proba, "kod") + " · " + P.S(proba, "sekund") + " s · " + P.S(proba, "przyczyna"));
                var fin = zadanie.Nodes.Add("Zapisano FIN? " + (wynik == null ? "Nie w skrzynce" : "Tak · " + wynik.Wynik));
                if (wynik != null) fin.Nodes.Add("Źródło: " + wynik.Id + " · zadeklarowane kontrole: " + wynik.Kontrole.Count);
            }
            if (graf.Nodes.Count == 0) graf.Nodes.Add("Brak zarejestrowanych zadań adresowanych do tego agenta.");
            else graf.Nodes[0].ExpandAll();
            var historia = new TabPage("Historia tur CLI");
            var tekst = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Text = string.Join(Environment.NewLine, tury.AsEnumerable().Reverse().Select(t => P.S(t, "czas") + " · kod "
                    + P.S(t, "kod") + " · " + P.S(t, "sekund") + " s · " + P.S(t, "przyczyna") + " · zadanie " + P.S(t, "praca"))) };
            if (tekst.Text.Length == 0) tekst.Text = "Brak zapisanych tur CLI.";
            historia.Controls.Add(tekst);
            zakladki.TabPages.AddRange(new[] { przebieg, os, historia });
            okno.Controls.Add(zakladki);
            okno.Controls.Add(pelna);
            Motyw.Zastosuj(okno);
            okno.ShowDialog(wlasciciel);
        }
    }

    void PokazObserwatorium()
    {
        using (var okno = new Form { Text = "Obserwatorium agentów", Width = 1120, Height = 700,
            StartPosition = FormStartPosition.CenterParent, Font = fontN })
        {
            var projekty = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 245 };
            var tabela = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false };
            var wykres = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 125, FlowDirection = FlowDirection.TopDown,
                WrapContents = false, AutoScroll = true, Padding = new Padding(8) };
            var pasek = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
            var odswiez = Btn("Odśwież");
            var eksport = Btn("Zapisz raport HTML…");
            var analizuj = Btn("Analizuj agenta…");
            var analizaProjektu = Btn("Wiadomości, decyzje i metody…");
            analizaProjektu.Click += delegate { try { AnalizaObserwatorium.Pokaz(okno, P.Wszystkie(true), projekty.SelectedIndex <= 0 ? null : Convert.ToString(projekty.SelectedItem), null, AgenciZeWszystkichKatalogow()); } catch(Exception ex) { MessageBox.Show(okno, ex.Message, okno.Text); } };
            analizuj.Click += delegate
            {
                if (tabela.CurrentRow == null) return;
                PokazAnalizeAgenta(okno, Convert.ToString(tabela.CurrentRow.Cells[0].Value),
                    Convert.ToString(tabela.CurrentRow.Cells[1].Value));
            };
            var opis = new Label { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8),
                Text = "Wyniki FIN i tury CLI są liczone osobno. Sesje i role pochodzą z tras ECXP. Raport zawiera metadane i liczniki." };
            pasek.Controls.Add(new Label { Text = "Projekt", AutoSize = true, Padding = new Padding(0, 5, 0, 0) });
            pasek.Controls.AddRange(new Control[] { projekty, odswiez, analizuj, analizaProjektu, eksport });
            okno.Controls.AddRange(new Control[] { tabela, wykres, pasek, opis });
            var naglowki = new[] { "Projekt", "Agent", "Sesje ECXP", "Role", "Stan", "Ostatnia aktywność", "FIN pozytywne", "FIN negatywne", "FIN niewykonane", "Tury OK", "Tury z błędem" };
            foreach (var naglowek in naglowki) tabela.Columns.Add(naglowek, naglowek);
            var wiersze = new List<object[]>();
            var nazwy = new[] { "FIN pozytywne", "FIN negatywne", "FIN niewykonane", "Tury OK", "Tury z błędem" };
            var kolory = new[] { "#26865e", "#bd4a4a", "#93732a", "#346ba4", "#a4547c" };
            Action rysuj = delegate
            {
                tabela.Rows.Clear();
                wykres.SuspendLayout();
                try
                {
                    while (wykres.Controls.Count > 0) wykres.Controls[0].Dispose();
                    var wybrane = wiersze.Where(w => projekty.SelectedIndex <= 0 || Equals(w[0], projekty.SelectedItem)).ToList();
                    foreach (var w in wybrane) tabela.Rows.Add(w);
                    var sumy = Enumerable.Range(6, 5).Select(i => wybrane.Sum(w => (int)w[i])).ToArray();
                    int maksimum = Math.Max(1, sumy.Max());
                    for (int i = 0; i < sumy.Length; i++)
                    {
                        var rzad = new FlowLayoutPanel { Width = 600, Height = 19, Margin = Padding.Empty, WrapContents = false };
                        rzad.Controls.Add(new Label { Text = nazwy[i] + "  " + sumy[i], Width = 200, Height = 19, Margin = Padding.Empty });
                        rzad.Controls.Add(new Panel { Width = (int)(300.0 * sumy[i] / maksimum), Height = 12, BackColor = ColorTranslator.FromHtml(kolory[i]) });
                        wykres.Controls.Add(rzad);
                    }
                }
                finally { wykres.ResumeLayout(); }
            };
            Action pobierz = delegate
            {
                var poprzedni = projekty.SelectedItem;
                var wiadomosci = P.Wszystkie(true).Where(m => m.Blad == null && m.KopiaDla.Length == 0).ToList();
                var trasy = Siec.Trasy();
                var agenci = Agenci.Lista();
                var fin = wiadomosci.Where(m => m.Typ == "fin" && P.PoprawnyNadawca(m.Od))
                    .ToLookup(m => Tuple.Create(m.Projekt, m.Od));
                var grupyTras = trasy.ToLookup(t => Tuple.Create(t.Projekt, t.Nazwa));
                var grupyAgentow = agenci.ToLookup(a => Tuple.Create(P.S(a, "projekt"), P.S(a, "nazwa")));
                var klucze = grupyTras.Select(g => g.Key).Concat(grupyAgentow.Select(g => g.Key))
                    .Concat(fin.Select(g => g.Key)).Distinct().OrderBy(k => k.Item1).ThenBy(k => k.Item2);
                wiersze.Clear();
                foreach (var klucz in klucze)
                {
                    var t = grupyTras[klucz];
                    var a = grupyAgentow[klucz].FirstOrDefault();
                    var tury = P.Lista(a, "uruchomienia").Where(u => P.S(u, "kod").Length > 0).ToList();
                    var ostatnia = fin[klucz].Select(m => m.Czas).Concat(tury.Select(u => {
                        DateTime czas;
                        return DateTime.TryParse(P.S(u, "czas"), out czas) ? czas : DateTime.MinValue;
                    })).DefaultIfEmpty(DateTime.MinValue).Max();
                    string stan = Wylaczeni.Jest(klucz.Item2) ? "Wyłączony" : a != null && Agenci.TuraZyje(a) ? "Pracuje" :
                        BiezaceZadanie(klucz.Item2) != null ? "Otwarte zadanie" : t.Any(x => x.Aktywna) ? "Zarejestrowany" : "Historia";
                    wiersze.Add(new object[] { klucz.Item1, klucz.Item2,
                        string.Join(", ", t.Select(x => x.Sesja).Where(x => x.Length > 0).Distinct()),
                        string.Join(", ", t.SelectMany(x => x.Role).Distinct()), stan,
                        ostatnia == DateTime.MinValue ? "—" : ostatnia.ToString("yyyy-MM-dd HH:mm"),
                        fin[klucz].Count(m => m.Wynik == "pozytywny"), fin[klucz].Count(m => m.Wynik == "negatywny"),
                        fin[klucz].Count(m => m.Wynik == "niewykonane"),
                        tury.Count(u => P.S(u, "kod") == "0"), tury.Count(u => P.S(u, "kod") != "0") });
                }
                projekty.Items.Clear();
                projekty.Items.Add("Wszystkie projekty");
                projekty.Items.AddRange(wiersze.Select(w => w[0]).Distinct().ToArray());
                int indeks = poprzedni == null ? -1 : projekty.Items.IndexOf(poprzedni);
                projekty.SelectedIndex = Math.Max(0, indeks);
            };
            projekty.SelectedIndexChanged += delegate { rysuj(); };
            odswiez.Click += delegate { try { pobierz(); } catch (Exception ex) { MessageBox.Show(okno, ex.Message, okno.Text); } };
            tabela.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0) return;
                var w = tabela.Rows[e.RowIndex];
                var projekt = Convert.ToString(w.Cells[0].Value);
                var agent = Convert.ToString(w.Cells[1].Value);
                PokazAnalizeAgenta(okno, projekt, agent);
            };
            eksport.Click += delegate
            {
                using (var dialog = new SaveFileDialog { Filter = "Raport HTML|*.html", FileName = "obserwatorium-ecxp.html" })
                {
                    if (dialog.ShowDialog(okno) != DialogResult.OK) return;
                    try
                    {
                        Func<object, string> koduj = v => System.Net.WebUtility.HtmlEncode(Convert.ToString(v));
                        var raport = new StringBuilder("<!doctype html><html lang='pl'><meta charset='utf-8'><title>Obserwatorium agentów</title>");
                        raport.Append("<style>body{font:16px system-ui;margin:2rem;color:#172238}table{border-collapse:collapse}td,th{border:1px solid #b9c3ce;padding:.5rem;text-align:left}meter{width:16rem}</style>");
                        raport.Append("<h1>Obserwatorium agentów</h1><p>").Append(koduj(projekty.SelectedItem)).Append("</p>");
                        var widoczne = tabela.Rows.Cast<DataGridViewRow>().ToList();
                        var sumy = Enumerable.Range(6, 5).Select(i => widoczne.Sum(r => (int)r.Cells[i].Value)).ToArray();
                        for (int i = 0; i < sumy.Length; i++)
                            raport.AppendFormat("<p>{0} <meter min='0' max='{1}' value='{2}'></meter> {2}</p>", koduj(nazwy[i]), Math.Max(1, sumy.Max()), sumy[i]);
                        raport.Append("<table><thead><tr>");
                        foreach (string n in naglowki) raport.Append("<th>").Append(koduj(n)).Append("</th>");
                        raport.Append("</tr></thead><tbody>");
                        foreach (var w in widoczne)
                        {
                            raport.Append("<tr>");
                            foreach (DataGridViewCell c in w.Cells) raport.Append("<td>").Append(koduj(c.Value)).Append("</td>");
                            raport.Append("</tr>");
                        }
                        File.WriteAllText(dialog.FileName, raport.Append("</tbody></table></html>").ToString(), new UTF8Encoding(false));
                    }
                    catch (Exception ex) { MessageBox.Show(okno, ex.Message, okno.Text); }
                }
            };
            okno.Shown += delegate { odswiez.PerformClick(); };
            Motyw.Zastosuj(okno);
            tabela.EnableHeadersVisualStyles = false;
            tabela.BackgroundColor = okno.BackColor;
            tabela.DefaultCellStyle.BackColor = okno.BackColor;
            tabela.DefaultCellStyle.ForeColor = okno.ForeColor;
            tabela.ColumnHeadersDefaultCellStyle.BackColor = okno.BackColor;
            tabela.ColumnHeadersDefaultCellStyle.ForeColor = okno.ForeColor;
            tabela.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.True;
            tabela.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            okno.ShowDialog(form);
        }
    }
}

static class OknoUstawien {
    static void Blad(IWin32Window f,Action a) {try{a();}catch(Exception ex){MessageBox.Show(f,ex.Message,"Ustawienia ECXP",MessageBoxButtons.OK,MessageBoxIcon.Error);}}
    static Button Przycisk(string t,Action a) { var b=new Button{Text=t,AutoSize=true,Margin=new Padding(6)}; b.Click+=(s,e)=>Blad(b.FindForm(),a);return b; }
    static Form Okno(string t) {return new Form{Text=t,Width=1000,Height=700,MinimumSize=new Size(720,480),StartPosition=FormStartPosition.CenterScreen,Font=new Font("Segoe UI",10),AutoScaleMode=AutoScaleMode.Dpi};}
    static TextBox Pole(TableLayoutPanel p,string opis,string value,bool plik) {
        int row=p.RowCount++;p.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        p.Controls.Add(new Label{Text=opis,AutoSize=true,Margin=new Padding(6,12,6,6)},0,row);
        var tb=new TextBox{Text=value,Dock=DockStyle.Fill,Margin=new Padding(6,10,6,6)};p.Controls.Add(tb,1,row);
        p.Controls.Add(Przycisk("Wybierz…",()=>{
            if(plik) {using(var d=new SaveFileDialog{FileName=tb.Text,Filter="Plik tekstowy|*.md;*.txt|Wszystkie pliki|*.*",OverwritePrompt=false})if(d.ShowDialog(p.FindForm())==DialogResult.OK)tb.Text=d.FileName;}
            else using(var d=new FolderBrowserDialog{SelectedPath=Directory.Exists(tb.Text)?tb.Text:"",ShowNewFolderButton=true})if(d.ShowDialog(p.FindForm())==DialogResult.OK)tb.Text=d.SelectedPath;
        }),2,row);return tb;
    }
    static TableLayoutPanel Tabela(Control f) {var p=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=3,Padding=new Padding(12)};p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,220));p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,115));f.Controls.Add(p);return p;}
    public static void Lokalizacje(IWin32Window owner) {
        using(var f=Okno("Lokalizacje plików i komunikacji")) {
            var opis=new Label{Dock=DockStyle.Top,Height=80,Padding=new Padding(18),Text="Wybierz folder na dowolnym dysku. Istniejące ustalenia pozostają zachowane. Odebrane i Wysłane to czytelne kopie wiadomości; całą skrzynkę przenosi osobny kreator."};
            var p=Tabela(f);var stary=File.Exists(Konfiguracja.Plik)?File.ReadAllText(Konfiguracja.Plik,Encoding.UTF8):"";var cfg=Konfiguracja.Czytaj(Konfiguracja.Plik);
            var kom=Pole(p,"Komunikacja sesji",Konfiguracja.Komunikacja,false);kom.Name="katalogKomunikacji";
            var od=Pole(p,"Odebrane",Konfiguracja.Widok("Odebrane"),false);var wy=Pole(p,"Wysłane",Konfiguracja.Widok("Wysłane"),false);
            var stan=Pole(p,"Plik stanu modułów",Konfiguracja.StanModulow,true);
            var dol=new FlowLayoutPanel{Dock=DockStyle.Bottom,AutoSize=true};f.Controls.Add(dol);f.Controls.Add(opis);
            dol.Controls.Add(Przycisk("Zapisz",()=>{
                var kp=Konfiguracja.Pelna(kom.Text);var op=Konfiguracja.Pelna(od.Text);var wp=Konfiguracja.Pelna(wy.Text);var sp=Konfiguracja.Pelna(stan.Text);
                if(op.TrimEnd('\\').Equals(wp.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))throw new EcxpException("Odebrane i Wysłane muszą mieć różne katalogi.");
                Konfiguracja.PrzygotujKomunikacje(kp);Directory.CreateDirectory(op);Directory.CreateDirectory(wp);
                cfg["komunikacja"]=kp;cfg["odebrane"]=op;cfg["wyslane"]=wp;cfg["stan_modulow"]=sp;
                Konfiguracja.Zapisz(Konfiguracja.Plik,P.Js().Serialize(cfg),stary);f.DialogResult=DialogResult.OK;f.Close();
            }));
            dol.Controls.Add(Przycisk("Kopiuj istniejące ustalenia…",()=>KopiujNotatki(f,Konfiguracja.Komunikacja,Konfiguracja.Pelna(kom.Text))));
            dol.Controls.Add(Przycisk("Gdzie są wszystkie pliki?",()=>Podglad(f)));
            dol.Controls.Add(Przycisk("Anuluj",()=>f.Close()));Motyw.Zastosuj(f);f.ShowDialog(owner);
        }
    }
    static void KopiujNotatki(IWin32Window f,string z,string cel) {
        if(z.TrimEnd('\\').Equals(cel.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase))throw new EcxpException("Wybierz nowy katalog komunikacji.");
        if(!Directory.Exists(z))throw new EcxpException("Brak katalogu źródłowego: "+z);
        var files=Directory.GetFiles(z,"*",SearchOption.TopDirectoryOnly);Directory.CreateDirectory(cel);
        foreach(var p in files)if(File.Exists(Path.Combine(cel,Path.GetFileName(p))))throw new EcxpException("Cel zawiera plik o tej samej nazwie: "+Path.GetFileName(p));
        foreach(var p in files)File.Copy(p,Path.Combine(cel,Path.GetFileName(p)),false);
        MessageBox.Show(f,"Skopiowano pliki ustaleń: "+files.Length+". Źródło pozostaje zachowane. Zapisz wybór katalogu.");
    }
    public static void Protokol(IWin32Window owner) {
        using(var f=Okno("Ustawienia protokołu ECXP")) {
            var p=Tabela(f);var raw=File.Exists(Konfiguracja.Plik)?File.ReadAllText(Konfiguracja.Plik,Encoding.UTF8):"";var cfg=Konfiguracja.Czytaj(Konfiguracja.Plik);
            var pola=new Dictionary<string,NumericUpDown>();
            string[] keys={"termin_pilny","termin_koordynacja","termin_zwykly","polaczenie_sekundy"};
            string[] names={"Pilne: potwierdzenie (min)","Koordynacja: potwierdzenie (min)","Zwykłe: potwierdzenie (min)","Czas łączenia (sekundy)"};int[] defaults={15,30,240,45};
            for(int i=0;i<keys.Length;i++){int row=p.RowCount++;p.Controls.Add(new Label{Text=names[i],AutoSize=true,Margin=new Padding(8)},0,row);var n=new NumericUpDown{Minimum=1,Maximum=i==3?300:10080,Value=Konfiguracja.Liczba(keys[i],defaults[i],1,i==3?300:10080),Dock=DockStyle.Fill};p.Controls.Add(n,1,row);pola.Add(keys[i],n);}
            var bottom=new FlowLayoutPanel{Dock=DockStyle.Bottom,AutoSize=true};f.Controls.Add(bottom);
            bottom.Controls.Add(Przycisk("Zapisz",()=>{foreach(var kv in pola)cfg[kv.Key]=(int)kv.Value.Value;Konfiguracja.Zapisz(Konfiguracja.Plik,P.Js().Serialize(cfg),raw);f.Close();}));
            bottom.Controls.Add(Przycisk("Przywróć wartości domyślne",()=>{for(int i=0;i<keys.Length;i++)pola[keys[i]].Value=defaults[i];}));
            f.Controls.Add(new Label{Dock=DockStyle.Bottom,Height=80,Text="Terminy dotyczą nowych wiadomości. Czas łączenia obowiązuje przy kolejnym połączeniu. Potwierdzenia, integralność i kontrola uprawnień pozostają obowiązkowe.",Padding=new Padding(16)});
            Motyw.Zastosuj(f);f.ShowDialog(owner);
        }
    }
    static string DomCodex {get {var d=Environment.GetEnvironmentVariable("CODEX_HOME");return string.IsNullOrEmpty(d)?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".codex"):d;}}
    public static void Mcp(IWin32Window owner) {
        using(var f=Okno("Konfiguracja MCP — Codex i Claude")) {
            var home=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var list=new ListBox{Dock=DockStyle.Fill};list.Items.AddRange(new object[]{Path.Combine(DomCodex,"config.toml"),Path.Combine(home,".claude.json"),Path.Combine(home,".claude","settings.json"),Path.Combine(Environment.CurrentDirectory,".mcp.json")});list.SelectedIndex=0;f.Controls.Add(list);
            var b=new FlowLayoutPanel{Dock=DockStyle.Bottom,AutoSize=true};f.Controls.Add(b);
            b.Controls.Add(Przycisk("Edytuj wybrany plik…",()=>Edytuj(f,(string)list.SelectedItem)));
            b.Controls.Add(Przycisk("Wskaż inny plik…",()=>{using(var d=new OpenFileDialog{Filter="Konfiguracja|*.toml;*.json|Wszystkie|*.*"})if(d.ShowDialog(f)==DialogResult.OK)Edytuj(f,d.FileName);}));
            f.Controls.Add(new Label{Dock=DockStyle.Top,Height=85,Padding=new Padding(16),Text="Edytor zapisuje kopię poprzedniej wersji i wykrywa równoległe zmiany. Po edycji uruchom ponownie odpowiedniego klienta. Plik projektu można wskazać ręcznie."});Motyw.Zastosuj(f);f.ShowDialog(owner);
        }
    }
    public static void Edytuj(IWin32Window owner,string path) {
        using(var f=Okno("Edytuj — "+path)) {
            var original=File.Exists(path)?File.ReadAllText(path,Encoding.UTF8):"";
            var t=new TextBox{Multiline=true,AcceptsTab=true,ScrollBars=ScrollBars.Both,WordWrap=false,Dock=DockStyle.Fill,Font=new Font("Consolas",10),Text=original,MaxLength=16*1024*1024};f.Controls.Add(t);
            var b=new FlowLayoutPanel{Dock=DockStyle.Bottom,AutoSize=true};f.Controls.Add(b);
            b.Controls.Add(Przycisk("Zapisz z kopią",()=>{
                if(Path.GetExtension(path).Equals(".json",StringComparison.OrdinalIgnoreCase)&&!(P.Js().DeserializeObject(t.Text) is Dictionary<string,object>))throw new EcxpException("Konfiguracja musi być obiektem JSON.");
                if(Path.GetExtension(path).Equals(".toml",StringComparison.OrdinalIgnoreCase))SprawdzToml(t.Text);
                Konfiguracja.Zapisz(path,t.Text,original);f.Close();
            }));b.Controls.Add(Przycisk("Anuluj",()=>f.Close()));Motyw.Zastosuj(f);f.ShowDialog(owner);
        }
    }
    public static void SprawdzToml(string tekst) {
        // Parser dostarczony z Codexem; odizolowany CODEX_HOME, bez zmiany aktywnej konfiguracji.
        var exe=Agenci.Program("codex");if(exe.Length==0)throw new EcxpException("Walidacja TOML wymaga zainstalowanego Codex CLI.");
        var temp=Path.Combine(Path.GetTempPath(),"ecxp-toml-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try {
            File.WriteAllText(Path.Combine(temp,"config.toml"),tekst,new UTF8Encoding(false));
            var si=new ProcessStartInfo(exe,"mcp list --json"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=temp};if (exe.EndsWith(".cmd",StringComparison.OrdinalIgnoreCase)||exe.EndsWith(".bat",StringComparison.OrdinalIgnoreCase)) { si.FileName=Environment.GetEnvironmentVariable("COMSPEC")??"cmd.exe";si.Arguments="/d /s /c \"\""+exe+"\" mcp list --json\""; }
            si.EnvironmentVariables["CODEX_HOME"]=temp;
            using(var proc=new Process{StartInfo=si}) {proc.OutputDataReceived+=(s,e)=>{};proc.ErrorDataReceived+=(s,e)=>{};proc.Start();proc.BeginOutputReadLine();proc.BeginErrorReadLine();if(!proc.WaitForExit(20000)){proc.Kill();throw new EcxpException("Walidacja Codex przekroczyła czas. Pliku nie zapisano.");}if(proc.ExitCode!=0)throw new EcxpException("Codex odrzucił konfigurację TOML. Popraw składnię i wartości; pliku nie zapisano.");}
        } finally {try{Directory.Delete(temp,true);}catch{}}
    }
    public static void Podglad(IWin32Window owner) {
        using(var f=Okno("Gdzie znajdują się pliki ECXP-MAILBOX?")) {
            var tree=new TreeView{Dock=DockStyle.Fill,ShowNodeToolTips=true};f.Controls.Add(tree);
            Action<TreeNode> expand=n=>{n.Nodes.Clear();var p=(string)n.Tag;if(!Directory.Exists(p))return;try{foreach(var d in Directory.GetDirectories(p).OrderBy(x=>x)){var child=n.Nodes.Add(Path.GetFileName(d));child.Tag=d;child.ToolTipText=d;if((File.GetAttributes(d)&FileAttributes.ReparsePoint)==0)child.Nodes.Add("…");}foreach(var file in Directory.GetFiles(p).OrderBy(x=>x)){var child=n.Nodes.Add(Path.GetFileName(file));child.Tag=file;child.ToolTipText=file;}}catch(Exception ex){n.Nodes.Add(ex.Message);}};
            var roots=new Dictionary<string,string>{{"Instalacja (programy)",Konfiguracja.Instalacja},{"Skrzynka (kolejki, dziennik, stan, załączniki)",P.Root},{"Komunikacja sesji",Konfiguracja.Komunikacja},{"Odebrane",Konfiguracja.Widok("Odebrane")},{"Wysłane",Konfiguracja.Widok("Wysłane")},{"Stan modułów",Konfiguracja.StanModulow},{"Konfiguracja lokalizacji i protokołu",Konfiguracja.Plik},{"Codex MCP",Path.Combine(DomCodex,"config.toml")},{"Claude MCP",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".claude.json")}};
            foreach(var kv in roots){var n=tree.Nodes.Add(kv.Key+" — "+kv.Value);n.Tag=kv.Value;n.ToolTipText=kv.Value;if(Directory.Exists(kv.Value))n.Nodes.Add("…");else if(!File.Exists(kv.Value))n.Text+=" (jeszcze nie istnieje)";}
            tree.BeforeExpand+=(s,e)=>expand(e.Node);
            var b=new FlowLayoutPanel{Dock=DockStyle.Bottom,AutoSize=true};f.Controls.Add(b);
            b.Controls.Add(Przycisk("Otwórz lokalizację",()=>{if(tree.SelectedNode!=null){var path=(string)tree.SelectedNode.Tag;if(File.Exists(path))Process.Start("explorer.exe","/select,\""+path+"\"");else if(Directory.Exists(path))Process.Start("explorer.exe","\""+path+"\"");}}));
            b.Controls.Add(Przycisk("Kopiuj ścieżkę",()=>{if(tree.SelectedNode!=null&&tree.SelectedNode.Tag!=null)Clipboard.SetText((string)tree.SelectedNode.Tag);}));Motyw.Zastosuj(f);f.ShowDialog(owner);
        }
    }
    public static void Migracja(IWin32Window owner) {
        using(var f=Okno("Przenieś lub importuj skrzynkę ECXP")) {
            var p=Tabela(f);var z=Pole(p,"Źródłowa skrzynka",P.Root,false);var cel=Pole(p,"Nowa skrzynka (pusta)","",false);
            var prze=new CheckBox{Text="Przełącz źródło na nową lokalizację po sprawdzeniu kopii",Checked=true,Dock=DockStyle.Bottom,Height=45};f.Controls.Add(prze);
            var info=new Label{Dock=DockStyle.Bottom,Height=140,Padding=new Padding(16),Text="Import kopiuje pełną skrzynkę do pustego folderu, także na innym dysku. Oryginał pozostaje zachowany. Nie łączy dwóch skrzynek.\r\nNa czas kopiowania zamknij konsolę i stos źródłowej skrzynki. To niezależne okno może pozostać otwarte. Po przełączeniu uruchom ponownie konsolę i klientów MCP. Katalog instalacji programów pozostaje oddzielny."};f.Controls.Add(info);
            var b=new FlowLayoutPanel{Dock=DockStyle.Bottom,AutoSize=true};f.Controls.Add(b);
            b.Controls.Add(Przycisk("Kopiuj i sprawdź",()=>{if(MessageBox.Show(f,"Skopiować skrzynkę z:\r\n"+z.Text+"\r\ndo:\r\n"+cel.Text+"\r\n"+(prze.Checked?"Po sprawdzeniu nowe uruchomienia będą używać celu.":"Źródło pozostanie aktywną lokalizacją."),"Migracja",MessageBoxButtons.OKCancel)!=DialogResult.OK)return;var count=Konfiguracja.KopiujSkrzynke(z.Text,cel.Text,prze.Checked);MessageBox.Show(f,"Sprawdzono i skopiowano pliki: "+count+". Źródło zachowane. Uruchom ponownie klientów.");}));
            Motyw.Zastosuj(f);f.ShowDialog(owner);
        }
    }
}
}
