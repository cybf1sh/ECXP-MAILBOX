using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
namespace Ecxp {
class TestKonfiguracji {
    static int bad;static void Check(bool ok,string name){Console.WriteLine((ok?"PASS ":"FAIL ")+name);if(!ok)bad++;}
    static bool Refused(Action a){try{a();return false;}catch{return true;}}
    static IEnumerable<Control> Controls(Control p){foreach(Control c in p.Controls){yield return c;foreach(var d in Controls(c))yield return d;}}
    static void Okno(Action open,string file,Action<Form> check){using(var t=new System.Windows.Forms.Timer{Interval=250}){t.Tick+=(s,e)=>{t.Stop();var f=Application.OpenForms.Cast<Form>().Last();try{check(f);if(!f.IsDisposed)using(var b=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(b,new Rectangle(0,0,b.Width,b.Height));b.Save(file);}}catch(Exception ex){Check(false,ex.ToString());}finally{f.Close();}};t.Start();open();}}
    [STAThread] public static int Main(string[] args){
        string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);P.Root=Path.Combine(root,"source");Directory.CreateDirectory(P.Root);Directory.CreateDirectory(Path.Combine(P.Root,"dziennik"));
        Check(P.Termin("codex-test",false)==240&&P.Termin("codex-test",true)==15&&P.Termin(P.Wlasciciel,true)==0,"default deadlines and operator exemption");
        var cfg=P.D("termin_zwykly",80,"termin_pilny",5,"polaczenie_sekundy",20,"komunikacja",Path.Combine(P.Root,"notes"));Konfiguracja.Zapisz(Konfiguracja.Plik,P.Js().Serialize(cfg),"");
        Check(P.Termin("codex-test",false)==80&&P.Termin("codex-test",true)==5,"custom protocol deadlines used");
        var original=File.ReadAllText(Konfiguracja.Plik);Check(Refused(()=>Konfiguracja.Zapisz(Konfiguracja.Plik,"{}","stale"))&&File.ReadAllText(Konfiguracja.Plik)==original,"concurrent edit rejected without data loss");
        Konfiguracja.Zapisz(Konfiguracja.Plik,original,original);Check(Directory.GetFiles(Path.GetDirectoryName(Konfiguracja.Plik),"*.bak").Length==1,"previous config backed up");
        Check(Refused(()=>Konfiguracja.Pelna("relative"))&&Refused(()=>Konfiguracja.Pelna("D:\\bad\npath")),"relative paths and control characters rejected");
        Konfiguracja.PrzygotujKomunikacje(Konfiguracja.Komunikacja);var notes=Path.Combine(Konfiguracja.Komunikacja,"HISTORIA-I-USTALENIA-ECXP.txt");File.AppendAllText(notes,"Ważne ustalenie");Konfiguracja.PrzygotujKomunikacje(Konfiguracja.Komunikacja);
        Check(File.ReadAllText(notes).Contains("Ważne ustalenie"),"existing notes preserved");
        var source=P.Root;var target=Path.Combine(root,"destination");
        Check(Refused(()=>Konfiguracja.KopiujSkrzynke(source,Path.Combine(source,"child"),true)),"nested migration rejected");
        Directory.CreateDirectory(target);File.WriteAllText(Path.Combine(target,"existing.txt"),"existing");Check(Refused(()=>Konfiguracja.KopiujSkrzynke(source,target,true)),"nonempty destination rejected");
        target=Path.Combine(root,"migrated");var ready=new ManualResetEvent(false);var release=new ManualResetEvent(false);
        var thread=new Thread(()=>{using(var m=new Mutex(true,@"Local\ECXPD-"+Ramka.NazwaPotoku(source))){ready.Set();release.WaitOne();m.ReleaseMutex();}});thread.Start();ready.WaitOne();
        Check(Refused(()=>Konfiguracja.KopiujSkrzynke(source,target,true))&&!Directory.Exists(target),"active stack rejected before copying");release.Set();thread.Join();ready.Dispose();release.Dispose();
        var count=Konfiguracja.KopiujSkrzynke(source,target,true);Check(count>0&&File.Exists(notes)&&File.ReadAllText(Path.Combine(target,"notes",Path.GetFileName(notes))).Contains("Ważne ustalenie"),"migration copies history and preserves source");
        Check(Konfiguracja.RozwiazRoot(source)==target,"old startup path resolves migrated mailbox");P.Root=target;Check(Konfiguracja.Komunikacja==Path.Combine(target,"notes"),"internal configured paths rebased");
        var import=Path.Combine(root,"imported");Konfiguracja.KopiujSkrzynke(target,import,false);Check(!File.Exists(Path.Combine(target,"ecxp-przeniesiono.json")),"import does not switch source");
        Konfiguracja.Zapisz(Path.Combine(target,"ecxp-przeniesiono.json"),P.Js().Serialize(P.D("skrzynka",source)),null);Check(Refused(()=>Konfiguracja.RozwiazRoot(source)),"redirect cycle rejected");
        P.Root=import;Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        Okno(()=>OknoUstawien.Lokalizacje(null),Path.Combine(root,"lokalizacje.png"),f=>Check(Controls(f).OfType<TextBox>().Count()==4,"location editor exposes four editable paths"));
        Okno(()=>OknoUstawien.Protokol(null),Path.Combine(root,"protokol.png"),f=>Check(Controls(f).OfType<NumericUpDown>().Count()==4,"protocol editor exposes four bounded settings"));
        Okno(()=>OknoUstawien.Podglad(null),Path.Combine(root,"podglad.png"),f=>Check(Controls(f).OfType<TreeView>().Single().Nodes.Count>=9,"inventory includes installation, mailbox and MCP"));
        Okno(()=>OknoUstawien.Mcp(null),Path.Combine(root,"mcp.png"),f=>Check(Controls(f).OfType<ListBox>().Single().Items.Count==4,"MCP editor lists user and project configurations"));
        Okno(()=>OknoUstawien.Migracja(null),Path.Combine(root,"migracja.png"),f=>Check(Controls(f).OfType<CheckBox>().Single().Checked,"migration provides explicit switch choice"));
        var chosen=Path.Combine(root,"wybrana komunikacja Żółć");
        Okno(()=>OknoUstawien.Lokalizacje(null),Path.Combine(root,"saved.png"),f=>{Controls(f).OfType<TextBox>().Single(t=>t.Name=="katalogKomunikacji").Text=chosen;Controls(f).OfType<Button>().Single(b=>b.Text=="Zapisz").PerformClick();});
        Check(Konfiguracja.Komunikacja==chosen&&File.Exists(Path.Combine(chosen,"HISTORIA-I-USTALENIA-ECXP.txt")),"location dialog saves selected directory and creates history");
        Okno(()=>OknoUstawien.Protokol(null),Path.Combine(root,"protocol-saved.png"),f=>{foreach(var n in Controls(f).OfType<NumericUpDown>())n.Value=12;Controls(f).OfType<Button>().Single(b=>b.Text=="Zapisz").PerformClick();});
        Check(P.Termin("codex-test",false)==12&&Konfiguracja.Liczba("polaczenie_sekundy",45,1,300)==12,"protocol dialog saves values used by stack");
        var json=Path.Combine(root,"mcp-test.json");File.WriteAllText(json,"{}");
        Okno(()=>OknoUstawien.Edytuj(null,json),Path.Combine(root,"editor.png"),f=>{Controls(f).OfType<TextBox>().Single().Text="{\"mcpServers\":{}}";Controls(f).OfType<Button>().Single(b=>b.Text=="Zapisz z kopią").PerformClick();});
        Check(File.ReadAllText(json).Contains("mcpServers")&&Directory.GetFiles(root,"mcp-test.json.*.bak").Length==1,"configuration editor saves JSON with backup");
        Console.WriteLine("RESULT: "+bad+" failed");return bad==0?0:1;
    }
}
}
