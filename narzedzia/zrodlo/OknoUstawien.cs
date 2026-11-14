using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;
using System.Collections.Generic;
namespace Ecxp {
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
