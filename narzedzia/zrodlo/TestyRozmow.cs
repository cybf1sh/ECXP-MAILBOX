using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Collections.Generic;
namespace Ecxp {
class TestyRozmow {
    static readonly BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    static object Field(App a, string n) { return typeof(App).GetField(n, Flags).GetValue(a); }
    static object Call(App a, string n, params object[] args) { return typeof(App).GetMethod(n, Flags).Invoke(a, args); }
    [DllImport("user32.dll", EntryPoint="SendMessage")] static extern IntPtr PointMsg(IntPtr h, int msg, IntPtr wp, ref Point p);
    static IEnumerable<Control> Descendants(Control parent) { foreach (Control c in parent.Controls) { yield return c; foreach (var d in Descendants(c)) yield return d; } }
    static int failed;
    static void Check(bool ok, string name) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failed++; }
    [STAThread] public static int Main(string[] args) {
      if (args.Length < 1) return 2;
      string root=Path.GetFullPath(args[0]);
      if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new Exception("Test root must be empty");
      P.Root=root; foreach(string d in P.Foldery) Directory.CreateDirectory(Path.Combine(root,d));
      string body=string.Join("\r\n",Enumerable.Range(1,180).Select(i=>"Syntetyczny akapit "+i+". Spokojne czytanie wiadomości bez przeskakiwania tekstu."));
      P.Wyslij(new P.Nowa { Od="claude-demo", Do="wlasciciel", Projekt="demo", Modul="rozmowy", Temat="Czytanie długiej wiadomości", Tresc=body, Watek="test-reading" });
      P.Wyslij(new P.Nowa { Od="codex-demo", Do="wlasciciel", Projekt="demo", Modul="rozmowy", Temat="Drugi wątek", Tresc="Wyłącznie dane syntetyczne.", Watek="test-other" });
      Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
      var app=new App();
      ((System.Windows.Forms.Timer)Field(app,"timer")).Stop();
      var form=(Form)Field(app,"form"); form.Text="ECXP — TESTY SYNTETYCZNE"; form.Show();
      var lv=(ListView)Field(app,"lv"); var det=(RichTextBox)Field(app,"det");
      Action run = delegate {
        try {
      Application.DoEvents();
      var row=lv.Items.Cast<ListViewItem>().Single(x=>((Msg)x.Tag).Uwagi==body); row.Selected=true;
      Application.DoEvents();
      Check(lv.Items.Count==2,"one row per conversation");
      det.Select(500,35); var scroll=new Point(0,300); PointMsg(det.Handle,0x04DE,IntPtr.Zero,ref scroll); PointMsg(det.Handle,0x04DD,IntPtr.Zero,ref scroll);
      string initial=det.Text;
      for(int i=0;i<5;i++) Call(app,"RefreshView");
      var after=new Point(); PointMsg(det.Handle,0x04DD,IntPtr.Zero,ref after);
      Check(det.Text==initial && det.SelectionStart==500 && det.SelectionLength==35 && after==scroll,"unchanged refresh preserves text selection and scroll");
      P.Wyslij(new P.Nowa { Od="codex-demo", Do="wlasciciel", Projekt="demo", Modul="rozmowy", Temat="Drugi wątek", Tresc="Nowa wiadomość w innym wątku.", Watek="test-other" });
      Call(app,"Skanuj"); Application.DoEvents(); PointMsg(det.Handle,0x04DD,IntPtr.Zero,ref after);
      Check(det.Text==initial && det.SelectionStart==500 && after==scroll,"other conversation arrival preserves reading position");
      P.Wyslij(new P.Nowa { Od="codex-demo", Do="wlasciciel", Projekt="demo", Modul="rozmowy", Temat="Czytanie długiej wiadomości", Tresc="NOWA-SYNTETYCZNA", Watek="test-reading" });
      Call(app,"Skanuj"); Application.DoEvents();
      Check(det.Text==initial && ((Button)Field(app,"noweWiadomosci")).Visible,"same conversation arrival waits behind explicit refresh");
      var selected=(Msg)Call(app,"Wybrana"); Call(app,"PokazWatek",selected,true); PointMsg(det.Handle,0x04DD,IntPtr.Zero,ref after);
      Check(det.Text.Contains("NOWA-SYNTETYCZNA") && det.SelectionStart==500 && after==scroll,"explicit refresh preserves position");
      var longMessage=((List<Msg>)Field(app,"msgs")).First(x=>x.Uwagi==body);
      Call(app,"OtworzWiadomosc",longMessage); Call(app,"OtworzWiadomosc",longMessage);
      var windows=(Dictionary<string,Form>)Field(app,"oknaWiadomosci");
      Check(windows.Count==1,"same message reuses its window");
      var readerForm=windows.Values.Single();
      var reader=Descendants(readerForm).OfType<RichTextBox>().Single(); string snapshot=reader.Text;
      reader.Select(400,20); Call(app,"Skanuj");
      Check(reader.Text==snapshot && reader.SelectionStart==400 && reader.SelectionLength==20,"message window stable during mailbox scan");
      var zoom=Descendants(readerForm).OfType<TrackBar>().Single(x=>x.Name=="rozmiarTekstu");
      reader.Select(450,30); reader.ScrollToCaret(); Application.DoEvents();
      int anchor=reader.GetCharIndexFromPosition(new Point(1,1)); int anchorY=reader.GetPositionFromCharIndex(anchor).Y;
      zoom.Value=150; Application.DoEvents();
      Check(Math.Abs(reader.ZoomFactor-1.5f)<.01f && reader.Text==snapshot && reader.SelectionStart==450 && reader.SelectionLength==30,"reader slider scales without changing text or selection");
      Check(Math.Abs(reader.GetPositionFromCharIndex(anchor).Y-anchorY)<=Math.Ceiling(reader.Font.GetHeight()*reader.ZoomFactor)+3,"zoom retains visible reading anchor");
      Descendants(readerForm).OfType<Button>().Single(x=>x.Text=="Odśwież").PerformClick();
      Check(Math.Abs(reader.ZoomFactor-1.5f)<.01f,"explicit reader refresh retains zoom");
      var theme=Descendants(readerForm).OfType<ComboBox>().Single(x=>x.Name=="motywCzytania");
      string stable=reader.Text; theme.SelectedIndex=2;
      reader.Select(450,1);
      Check(reader.BackColor==Color.Black && reader.SelectionColor==Color.White && reader.Text==stable,"console theme has black background and white text");
      var family=Descendants(readerForm).OfType<ComboBox>().Single(x=>x.Name=="czcionkaCzytania"); family.SelectedIndex=1;
      reader.Select(450,1);
      Check(reader.SelectionFont.Name=="Consolas" && Math.Abs(reader.ZoomFactor-1.5f)<.01f,"console font preserves current zoom");
      theme.SelectedIndex=0;
      Check(reader.BackColor==Color.White && reader.Text==stable,"reader can restore light background");
      var previewZoom=Descendants(form).OfType<TrackBar>().Single(x=>x.Name=="rozmiarTekstu");
      string previewText=det.Text; previewZoom.Value=125;
      Check(Math.Abs(det.ZoomFactor-1.25f)<.01f && det.Text==previewText,"preview has working independent text slider");
      var mainTheme=Descendants(form).OfType<ComboBox>().Single(x=>x.Name=="motywCzytania");
      mainTheme.SelectedIndex=2; Application.DoEvents();
      Check(form.BackColor==Color.Black && readerForm.BackColor==Color.Black && det.BackColor==Color.Black && reader.BackColor==Color.Black && theme.SelectedIndex==2,"theme is global across main window and existing reader");
      string appearance=Path.Combine(root,"narzedzia","ustawienia-wygladu.json");
      var restartPrefs=new Czytnik.Preferencje(appearance);
      Check(restartPrefs.Motyw==2 && restartPrefs.Czcionka==1,"theme and font survive reloading preferences");
      readerForm.Close(); Call(app,"OtworzWiadomosc",longMessage);
      readerForm=windows.Values.Single(); reader=Descendants(readerForm).OfType<RichTextBox>().Single();
      Check(readerForm.BackColor==Color.Black && reader.BackColor==Color.Black && Descendants(readerForm).OfType<ComboBox>().Single(x=>x.Name=="motywCzytania").SelectedIndex==2,"new reader inherits saved global console theme");
      Call(app,"RefreshView");
      Check(form.BackColor==Color.Black && det.BackColor==Color.Black && new Czytnik.Preferencje(appearance).Motyw==2,"mailbox refresh does not reset console theme");
      using(var compose=new PiszForm(new List<string>{"codex-demo"},new List<string>{"demo"},new List<string>{"rozmowy"},form.Font,form.Font)) {
        compose.Show(form); Application.DoEvents();
        Check(compose.BackColor==Color.Black && compose.tbTresc.BackColor==Color.Black && compose.tbTresc.ForeColor==Color.White,"new compose window inherits global console theme");
        compose.Close();
      }
      mainTheme.SelectedIndex=0; Application.DoEvents();
      Check(det.BackColor==Color.White && reader.BackColor==Color.White && new Czytnik.Preferencje(appearance).Motyw==0,"explicit light selection applies to all windows and persists");
      string evil="<script>alert(1)</script>\nZażółć gęślą jaźń & test";
      string html=Czytnik.Html("Tytuł <test>",evil);
      Check(html.Contains("&lt;script&gt;") && !html.Contains("<script>") && html.Contains("Zażółć") && html.Contains("Content-Security-Policy"),"HTML escapes message content and preserves Unicode");
      string htmlPath=Path.Combine(root,"message.html"); Czytnik.Zapisz(htmlPath,"Tytuł",evil,false);
      Czytnik.Zapisz(htmlPath,"Nowy tytuł",evil+" KONIEC",false);
      Check(File.ReadAllText(htmlPath).Contains("KONIEC") && !Directory.GetFiles(root,".ecxp-*").Any(),"HTML export atomically replaces chosen file without temporary leftovers");
      Czytnik.ProgramPdf=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"narzedzia","ECXP-MAILBOX.exe");
      string pdfPath=Path.Combine(root,"message.pdf"); Czytnik.Zapisz(pdfPath,"Syntetyczny PDF",body+"\nOSTATNI ZNAK ŻÓŁĆ",true);
      Check(File.Exists(pdfPath) && new FileInfo(pdfPath).Length>1000,"multi-page PDF export creates a nonempty verified PDF");
      Console.WriteLine("PDF_PATH: "+pdfPath);
      foreach(var w in windows.Values.ToList()) w.Close();
      var store=(MagazynSzkicow)Field(app,"magazynSzkicow");
      var quick=(TextBox)Field(app,"szybkaTresc"); quick.Text="NIEWYSŁANY SZKIC Żółć — pełna treść";
      Call(app,"ZapiszSzybkiSzkic");
      string draftsPath=Path.Combine(root,"narzedzia","niewyslane-wlasciciela.json");
      var reload=new MagazynSzkicow(draftsPath);
      Check(reload.Lista.Count==1 && reload.Lista[0].Tresc==quick.Text,"quick reply persists unsent Unicode text across store reload");
      string attachment=Path.Combine(root,"syntetyczny-zalacznik.txt");File.WriteAllText(attachment,"syntetyczny zalacznik");
      string formId;
      using(var compose=new PiszForm(new List<string>{"codex/rozmowy"},new List<string>{"demo"},new List<string>{"rozmowy"},form.Font,form.Font)) {
        compose.UstawAdresata("codex/rozmowy");compose.chCodex.Checked=true;compose.chClaude.Checked=false;
        compose.cbProjekt.Text="demo";compose.cbModul.Text="rozmowy";compose.tbTemat.Text="Niewysłana wiadomość";
        compose.tbTresc.Text="Zachowaj całość bez wysyłania.";compose.OdpNa=longMessage.Id;compose.zal.UstawPliki(new[]{attachment});
        formId=compose.SzkicId;Call(app,"PodlaczZapisSzkicu",compose);compose.Show(form);Application.DoEvents();compose.Close();
      }
      var saved=store.Znajdz(formId);
      Check(saved!=null && saved.Tresc=="Zachowaj całość bez wysyłania." && saved.Pliki.Single()==attachment,"closing composer saves complete unsent content and attachments");
      saved.Blad="Syntetyczna odmowa transportu";store.Zapisz(saved);
      var failedDraft=new MagazynSzkicow(draftsPath).Znajdz(formId);
      Check(failedDraft!=null && failedDraft.Blad==saved.Blad && failedDraft.Tresc==saved.Tresc,"failed-send draft retains error and content across reload");
      using(var resume=new PiszForm(new List<string>{"codex/rozmowy"},new List<string>{"demo"},new List<string>{"rozmowy"},form.Font,form.Font)) {
        MagazynSzkicow.DoFormularza(resume,failedDraft);
        Check(resume.AdresatDoWyslania=="codex/rozmowy" && resume.chCodex.Checked && !resume.chClaude.Checked && resume.OdpNa==longMessage.Id && resume.tbTresc.Text==saved.Tresc && resume.zal.Pliki.Single()==attachment,"resumed draft restores routing, one-agent choice, reply link and attachment");
      }
      var wire=P.Wyslij(new P.Nowa{Od=P.Wlasciciel,Do="codex-demo",Projekt="demo",Modul="rozmowy",Temat="Syntetyczny błąd doręczenia",Tresc="Próba testowa",Watek="test-failed-delivery"});
      Call(app,"Skanuj");
      var failedWire=((List<Msg>)Field(app,"msgs")).Single(x=>x.Id==P.S(wire,"id"));
      failedWire.Dor=new Stan{Id=P.S(wire,"id"),Eskalacja=true,Ponowienia=1,Dostarczona=DateTime.Now};
      ((RadioButton)Field(app,"rbNiewyslane")).Checked=true;Application.DoEvents();
      var unsent=(ListView)Field(app,"lvNiewyslane");
      Check(unsent.Visible && !lv.Visible && !((Panel)Field(app,"szybkiPanel")).Visible && unsent.Items.Count==3,"unsent filter lists drafts and failed deliveries together");
      int readCount=((HashSet<string>)Field(app,"read")).Count;
      unsent.Items.Cast<ListViewItem>().Single(x=>x.Tag is SzkicWiadomosci && ((SzkicWiadomosci)x.Tag).Id==formId).Selected=true;Application.DoEvents();
      Check(det.Text.Contains(saved.Tresc) && det.Text.Contains(saved.Blad) && ((HashSet<string>)Field(app,"read")).Count==readCount,"unsent preview reads local draft without acknowledging a wire message");
      string draftSnapshot=det.Text;int draftSelection=det.SelectionStart;
      Call(app,"RefreshView");
      Check(det.Text==draftSnapshot && det.SelectionStart==draftSelection,"unchanged unsent refresh preserves reading snapshot");
      failedWire.Dor.Potwierdzona=DateTime.Now;Call(app,"OdswiezNiewyslane");
      Check(unsent.Items.Count==2 && !unsent.Items.Cast<ListViewItem>().Any(x=>x.Tag is Msg),"acknowledged delivery disappears from unsent filter");
      store.Usun(formId);Call(app,"OdswiezNiewyslane");
      Check(unsent.Items.Count==1 && new MagazynSzkicow(draftsPath).Znajdz(formId)==null,"draft removal is durable and preserves another unsent draft");
      ((RadioButton)Field(app,"rbWsz")).Checked=true;Application.DoEvents();
      Check(lv.Visible && !unsent.Visible,"normal conversation list returns after leaving unsent filter");
      var quickId="quick-"+(string)Field(app,"draftWatek");
      ((HashSet<string>)Field(app,"edytowaneSzkice")).Add(quickId);
      var edited=store.Znajdz(quickId);edited.Tresc="Treść poprawiona w osobnym edytorze";store.Zapisz(edited);
      Call(app,"ZapiszSzybkiSzkic");
      Check(store.Znajdz(quickId).Tresc==edited.Tresc,"background quick reply cannot overwrite a draft being edited in its own window");
      ((HashSet<string>)Field(app,"edytowaneSzkice")).Remove(quickId);Call(app,"SynchronizujSzybkiSzkic",quickId);
      Check(quick.Text==edited.Tresc,"closing draft editor synchronizes quick reply with edited content");
      using(var sent=new PiszForm(new List<string>{"codex/rozmowy"},new List<string>{"demo"},new List<string>{"rozmowy"},form.Font,form.Font)) {
        MagazynSzkicow.DoFormularza(sent,edited);Call(app,"PodlaczZapisSzkicu",sent);sent.Show(form);Application.DoEvents();
        Call(app,"OznaczSzkicWyslany",sent);sent.Close();
      }
      Call(app,"ZapiszSzybkiSzkic");
      Check(quick.Text.Length==0 && store.Znajdz(quickId)==null && new MagazynSzkicow(draftsPath).Znajdz(quickId)==null,"accepted send stays removed after composer closes and quick autosave runs");
      // menu prawego klawisza (3.9.0): Niewysłane – Ponów / Usuń; odebrane – Odpowiedz / Archiwizuj / Usuń
      Check(unsent.ContextMenuStrip!=null && unsent.ContextMenuStrip.Items.Cast<ToolStripItem>().Select(x=>x.Text).SequenceEqual(new[]{"↻  Ponów wysyłkę…","🗑  Usuń…"}),"unsent list right-click offers Retry and Delete");
      foreach(ListViewItem x in lv.Items) x.Selected=false;
      lv.Items.Cast<ListViewItem>().First(x=>((Msg)x.Tag).Od!=P.Wlasciciel).Selected=true; Application.DoEvents();
      var lvMenu=lv.ContextMenuStrip; lvMenu.Show(lv,new Point(5,5)); Application.DoEvents();
      var widoczneMenu=lvMenu.Items.Cast<ToolStripItem>().Where(x=>x.Available && !(x is ToolStripSeparator)).Select(x=>x.Text).ToList();
      lvMenu.Close(ToolStripDropDownCloseReason.Keyboard); Application.DoEvents();
      Check(widoczneMenu.Take(3).SequenceEqual(new[]{"↩  Odpowiedz…","🗄  Archiwizuj","🗑  Usuń…"}) && !widoczneMenu.Contains("⊘  Zamknij wątek…"),"received conversation right-click starts with Reply, Archive, Delete");
      det.ContextMenuStrip.Show(det,new Point(5,5)); Application.DoEvents();
      var menuPodgladu=det.ContextMenuStrip.Items.Cast<ToolStripItem>().Where(x=>x.Available && !(x is ToolStripSeparator)).Select(x=>x.Text).ToList();
      det.ContextMenuStrip.Close(ToolStripDropDownCloseReason.Keyboard); Application.DoEvents();
      Check(menuPodgladu.SequenceEqual(new[]{"📋  Kopiuj","↩  Odpowiedz…","🗄  Archiwizuj","🗑  Usuń…","👁  Co teraz robi agent?"}),"received message preview right-click offers Copy, Reply, Archive, Delete, agent activity");
      Check(widoczneMenu.Contains("👁  Co teraz robi agent?"),"conversation right-click offers agent activity window");
      // okno „Co robi agent” dla agenta w tle (dane syntetyczne)
      Directory.CreateDirectory(Path.Combine(root,"stan","agenci"));
      Pliki.Zapisz(Path.Combine(root,"stan","agenci","claude-demo.json"),P.D("nazwa","claude-demo","strona","claude","katalog",root,"tura_od",P.Teraz(),"tura_praca",""));
      Aktywnosc.Zapisz("claude-demo","praca","📥 zadanie: DEMO"); Aktywnosc.Zapisz("claude-demo","narzedzie","▶ uruchamia pytest -q");
      Call(app,"PokazAktywnosc","claude-demo");
      Form oknoAkt=null; for(int i=0;i<40 && (oknoAkt==null || ((ListView)Descendants(oknoAkt).OfType<ListView>().Single()).Items.Count<2);i++){ Application.DoEvents(); System.Threading.Thread.Sleep(50); oknoAkt=Application.OpenForms.Cast<Form>().FirstOrDefault(x=>x.Name=="oknoAktywnosci"); }
      var listaAkt=oknoAkt==null?null:Descendants(oknoAkt).OfType<ListView>().Single();
      Check(oknoAkt!=null && listaAkt.Items.Count==2 && listaAkt.Items[1].SubItems[1].Text=="▶ uruchamia pytest -q" && Descendants(oknoAkt).OfType<Label>().Any(x=>x.Name=="stanAgenta" && x.Text.StartsWith("● pracuje")),"agent activity window lists live actions and working state");
      Call(app,"PokazAktywnosc","claude-demo");
      Check(Application.OpenForms.Cast<Form>().Count(x=>x.Name=="oknoAktywnosci")==1,"activity window is reused per agent");
      if(oknoAkt!=null) oknoAkt.Close();
      var doKosza=P.Wyslij(new P.Nowa{Od="codex-demo",Do=P.Wlasciciel,Projekt="demo",Modul="rozmowy",Temat="Do usunięcia",Tresc="Syntetyczna",Watek="test-kosz"});
      string plikKosza=Path.GetFileName(P.S(doKosza,"plik"));
      var wynikKosza=P.Wycofaj(new List<string>{plikKosza},"test usuwania",P.Kosz);
      Check(Convert.ToInt32(wynikKosza["przeniesione"])==1 && Directory.GetFiles(Path.Combine(root,P.Kosz),plikKosza,SearchOption.AllDirectories).Length==1
        && !P.Foldery.Any(f=>File.Exists(Path.Combine(root,f,plikKosza))) && !P.Wszystkie(true).Any(x=>x.Plik==plikKosza),"delete moves message to recoverable trash outside queues and archive");
      var settings=Descendants(form).OfType<Button>().Single(x=>x.Text=="Ustawienia…");
      settings.PerformClick(); Application.DoEvents();
      settings.ContextMenuStrip.Close(ToolStripDropDownCloseReason.AppClicked); Application.DoEvents();
      Check(!form.IsDisposed && !settings.ContextMenuStrip.IsDisposed,"settings menu survives dismissal by outside click");
      settings.PerformClick(); Application.DoEvents();
      settings.ContextMenuStrip.Close(ToolStripDropDownCloseReason.Keyboard); Application.DoEvents();
      Check(!form.IsDisposed && !settings.ContextMenuStrip.IsDisposed,"settings menu can reopen and close by keyboard");
      var help=Descendants(form).OfType<Button>().Single(x=>x.Text=="ⓘ  Pomoc");
      Check(help.Visible && help.Parent==settings.Parent,"help button sits in the header next to settings");
      help.PerformClick(); Application.DoEvents();
      var helpItems=help.ContextMenuStrip.Items.Cast<ToolStripItem>().ToList();
      Check(helpItems.Count==5 && helpItems[0].Text=="O programie" && helpItems[1] is ToolStripSeparator
        && helpItems.Skip(2).All(x=>x.Text.EndsWith("↗")) && helpItems.Any(x=>x.Text.StartsWith("ECXP i ECXP-MAILBOX")) && helpItems.Any(x=>x.Text.Contains("GitHub")),"help menu offers About and ECXP links");
      help.ContextMenuStrip.Close(ToolStripDropDownCloseReason.Keyboard); Application.DoEvents();
      form.Size=new Size(900,560); Application.DoEvents();
      var hb=form.RectangleToClient(help.RectangleToScreen(help.ClientRectangle));
      Check(hb.Right<=form.ClientSize.Width && hb.Bottom<=form.ClientSize.Height,"help button stays visible at minimum window size");
      form.Size=new Size(1240,820); Application.DoEvents();
      det.Select(0,0);
      typeof(Control).GetMethod("OnMouseUp",Flags).Invoke(det,new object[]{new MouseEventArgs(MouseButtons.Left,1,30,30,0)});
      Check(windows.Count==0,"click in preview does not open a message window");
      form.Size=new Size(900,560); Application.DoEvents();
      Check(det.Width>200 && det.Height>20,"minimum window retains readable content area");
      form.Size=new Size(1240,820); Application.DoEvents();
      // strażnik zadań (zakładka Agenci) i stan wysłanej wiadomości na liście TYTUŁ
      var tabAgenci=(TabPage)Field(app,"tabAg");
      var slider=Descendants(tabAgenci).OfType<TrackBar>().SingleOrDefault(x=>x.Name=="suwakStraznika");
      Check(slider!=null && slider.Minimum==10 && slider.Maximum==60 && slider.Value==60,"watcher slider spans 10 s to 1 min and defaults to 1 min");
      Check(Descendants(tabAgenci).OfType<Button>().Any(x=>x.Name=="przyciskStraznika") && Descendants(tabAgenci).OfType<ListView>().Any(x=>x.Name=="listaStraznika"),"watcher can be started from the Agents tab");
      var watcherTimer=(System.Windows.Forms.Timer)Field(app,"timerStraznika");
      Check(!watcherTimer.Enabled,"watcher is off until the operator starts it");
      slider.Value=10; Application.DoEvents();
      Check(watcherTimer.Interval==10000 && File.ReadAllText(Path.Combine(root,"narzedzia","ustawienia-wlasciciel.txt")).Contains("straznik=0|10|"),"slider changes the check interval and persists it");
      P.Wyslij(new P.Nowa { Od=P.Wlasciciel, Do="codex-demo", Projekt="demo", Modul="rozmowy", Typ="pytanie", Temat="Pytanie operatora o postęp", Tresc="Na jakim etapie jest praca?", Watek="test-other" });
      Call(app,"Skanuj"); Application.DoEvents();
      var etapRow=lv.Items.Cast<ListViewItem>().Select(x=>(string)x.GetType().GetField("Etap").GetValue(x)).FirstOrDefault(x=>x.Length>0);
      Check(etapRow!=null && etapRow.Contains("w kolejce"),"sent message shows its current delivery state in the TITLE list");
      Call(app,"UstawStraznika",true,true); Application.DoEvents();
      var watcherList=Descendants(tabAgenci).OfType<ListView>().Single(x=>x.Name=="listaStraznika");
      Check(watcherTimer.Enabled && watcherList.Items.Cast<ListViewItem>().Any(x=>((Msg)x.Tag).Uwagi.Contains("Na jakim etapie jest praca?") && x.SubItems[3].Text.Contains("w kolejce")),"started watcher tracks the operator question with its stage");
      Call(app,"UstawStraznika",false,true); Application.DoEvents();
      Check(!watcherTimer.Enabled,"watcher can be stopped");
      // znaczniki (3.11.0): kod #xxxx, „Wątek do zadania”, „↩ odpowiedź na zadanie #xxxx”
      ((TabControl)Field(app,"tabs")).SelectedTab=(TabPage)Field(app,"tabMsg");
      var zadZn=P.Wyslij(new P.Nowa{Od=P.Wlasciciel,Do="codex-demo",Projekt="demo",Modul="rozmowy",Typ="zadanie",Temat="Zadanie ze znacznikiem",Tresc="Zrób A.",Watek="test-znaczniki"});
      string idZad=P.S(zadZn,"id"), tagZad="#"+idZad.Substring(idZad.Length-4);
      var odpZn=P.Wyslij(new P.Nowa{Od="codex-demo",Do=P.Wlasciciel,Projekt="demo",Modul="rozmowy",Typ="odpowiedz",Temat="Zadanie ze znacznikiem",Tresc="Zrobione.",Watek="test-znaczniki",OdpNa=idZad});
      string idOdp=P.S(odpZn,"id"), tagOdp="#"+idOdp.Substring(idOdp.Length-4);
      Call(app,"Skanuj"); Application.DoEvents();
      var wierszZn=lv.Items.Cast<ListViewItem>().Single(x=>((Msg)x.Tag).Temat.ToUpperInvariant().Contains("ZNACZNIK"));
      string chipZn=(string)wierszZn.GetType().GetField("Chip").GetValue(wierszZn), etapZn=(string)wierszZn.GetType().GetField("Etap").GetValue(wierszZn);
      Check(chipZn=="Wątek do zadania "+tagZad,"conversation row carries a coloured thread marker naming the task code");
      Check(etapZn.StartsWith("↩ odpowiedź na zadanie "+tagZad+" z ") && etapZn.Contains("Twoja wiadomość "+tagZad+":"),"received reply row names the task it answers by code");
      foreach(ListViewItem x in lv.Items) x.Selected=false; wierszZn.Selected=true; Application.DoEvents();
      Check(det.Text.Contains("Wątek do zadania "+tagZad) && det.Text.Contains("↩ odpowiedź na zadanie "+tagZad+" · „") && det.Text.Contains("” · wysłane przez Ciebie ")
        && det.Text.Contains("↪ odpowiedzi na tę wiadomość: "+tagOdp),"conversation preview links reply and task by their codes in both directions");
      int pozTag=det.Text.IndexOf("↩ odpowiedź na zadanie "+tagZad); det.Select(pozTag,5); var kolorOdp=det.SelectionColor;
      det.Select(det.Text.IndexOf(tagZad+"   ·   "),4); var kolorZad=det.SelectionColor;
      Check(kolorOdp==kolorZad && kolorOdp.ToArgb()!=det.ForeColor.ToArgb(),"reply marker uses the same colour as the code of the task it answers");
      var odpMsg=((List<Msg>)Field(app,"msgs")).Single(x=>x.Id==idOdp);
      string schowek=(string)Call(app,"TekstDoSchowka",odpMsg);
      Check(schowek.Contains("Znacznik: "+tagOdp) && schowek.Contains("↩ odpowiedź na zadanie "+tagZad),"copied message text contains its code and the code it answers");
      // drzewko folderów (3.11.0): Wszystkie / Odebrane / Wysłane / Niewysłane / Zarchiwizowane
      var drzewo=(TreeView)Field(app,"drzewoFolderow");
      Check(drzewo!=null && drzewo.Nodes.Count==5 && drzewo.Nodes[1].Text.Contains("Odebrane") && drzewo.Nodes[2].Text.Contains("Wysłane") && drzewo.Nodes[3].Text.Contains("Niewysłane") && drzewo.Nodes[4].Text.Contains("Zarchiwizowane"),"folder tree lists inbox, sent, unsent and archived");
      Check(System.Text.RegularExpressions.Regex.IsMatch(drzewo.Nodes[1].Text,@"\(\d+") && System.Text.RegularExpressions.Regex.IsMatch(drzewo.Nodes[2].Text,@"\(\d+\)"),"folders show conversation counts");
      int wszystkichRozmow=lv.Items.Count;
      drzewo.SelectedNode=drzewo.Nodes[2]; Application.DoEvents();
      Check((int)Field(app,"kierunek")==2 && lv.Items.Count>0 && lv.Items.Count<wszystkichRozmow && lv.Items.Cast<ListViewItem>().All(x=>((Msg)x.Tag).Od==P.Wlasciciel),"selecting Sent folder lists only conversations with sent messages");
      drzewo.SelectedNode=drzewo.Nodes[3]; Application.DoEvents();
      Check((int)Field(app,"kierunek")==3 && ((ListView)Field(app,"lvNiewyslane")).Visible && !lv.Visible,"selecting Unsent folder shows the unsent list");
      P.Wycofaj(new List<string>{Path.GetFileName(P.S(odpZn,"plik")),Path.GetFileName(P.S(zadZn,"plik"))},"test archiwum");
      Call(app,"Skanuj"); Application.DoEvents();
      drzewo.SelectedNode=drzewo.Nodes[4]; Application.DoEvents();
      Check((int)Field(app,"kierunek")==4 && lv.Visible && lv.Items.Count==1 && ((Msg)lv.Items[0].Tag).Temat.ToUpperInvariant().Contains("ZNACZNIK") && drzewo.Nodes[4].Text.Contains("(1)"),"Archived folder lists the archived conversation");
      lv.Items[0].Selected=true; Application.DoEvents();
      Check(det.Text.Contains("Zrób A.") && det.Text.Contains("Zrobione.") && !((Panel)Field(app,"szybkiPanel")).Visible,"archived conversation opens read-only in the preview");
      lv.ContextMenuStrip.Show(lv,new Point(5,5)); Application.DoEvents();
      var menuArch=lv.ContextMenuStrip.Items.Cast<ToolStripItem>().Where(x=>x.Available && !(x is ToolStripSeparator)).Select(x=>x.Text).ToList();
      lv.ContextMenuStrip.Close(ToolStripDropDownCloseReason.Keyboard); Application.DoEvents();
      Check(!menuArch.Any(x=>x.Contains("Usuń")||x.Contains("Archiwizuj")||x.Contains("Zamknij")) && menuArch.Contains("Otwórz w oknie"),"archived folder menu has no delete, archive or close actions");
      drzewo.SelectedNode=drzewo.Nodes[0]; Application.DoEvents();
      Check((int)Field(app,"kierunek")==0 && !lv.Items.Cast<ListViewItem>().Any(x=>((Msg)x.Tag).Temat.ToUpperInvariant().Contains("ZNACZNIK")),"archived conversation is not shown among current conversations");
      Console.WriteLine("RESULT: "+failed+" failed");
      File.WriteAllText(Path.Combine(root,"gui-test-result.txt"),"Failures: "+failed);
        }
        catch(Exception ex) { Check(false,"unhandled GUI test exception: "+ex); }
        finally {
          ((FileSystemWatcher)Field(app,"fsw")).EnableRaisingEvents=false;
          if(!args.Contains("--stay")) app.ExitThread();
        }
      };
      form.BeginInvoke(run);
      Application.Run(app); // pełna pętla WinForms i prawidłowe zamknięcie dostawców okien/COM
      ((System.Windows.Forms.Timer)Field(app,"timer")).Dispose();
      ((FileSystemWatcher)Field(app,"fsw")).Dispose();
      var connection=Field(app,"klient") as IDisposable; if(connection!=null) connection.Dispose();
      foreach(var open in Application.OpenForms.Cast<Form>().ToList()) open.Dispose();
      app.Dispose();
      return failed==0?0:1;
    }
}}
