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
      var reader=windows.Values.Single().Controls.OfType<RichTextBox>().Single(); string snapshot=reader.Text;
      reader.Select(400,20); Call(app,"Skanuj");
      Check(reader.Text==snapshot && reader.SelectionStart==400 && reader.SelectionLength==20,"message window stable during mailbox scan");
      foreach(var w in windows.Values.ToList()) w.Close();
      var settings=Descendants(form).OfType<Button>().Single(x=>x.Text=="Ustawienia…");
      settings.PerformClick(); Application.DoEvents();
      settings.ContextMenuStrip.Close(ToolStripDropDownCloseReason.AppClicked); Application.DoEvents();
      Check(!form.IsDisposed && !settings.ContextMenuStrip.IsDisposed,"settings menu survives dismissal by outside click");
      settings.PerformClick(); Application.DoEvents();
      settings.ContextMenuStrip.Close(ToolStripDropDownCloseReason.Keyboard); Application.DoEvents();
      Check(!form.IsDisposed && !settings.ContextMenuStrip.IsDisposed,"settings menu can reopen and close by keyboard");
      det.Select(0,0);
      typeof(Control).GetMethod("OnMouseUp",Flags).Invoke(det,new object[]{new MouseEventArgs(MouseButtons.Left,1,30,30,0)});
      Check(windows.Count==0,"click in preview does not open a message window");
      form.Size=new Size(900,560); Application.DoEvents();
      Check(det.Width>200 && det.Height>20,"minimum window retains readable content area");
      form.Size=new Size(1240,820); Application.DoEvents();
      Console.WriteLine("RESULT: "+failed+" failed");
      File.WriteAllText(Path.Combine(root,"gui-test-result.txt"),"Failures: "+failed);
      if(args.Contains("--stay")) Application.Run(form);
      ((NotifyIcon)Field(app,"tray")).Visible=false;
      return failed==0?0:1;
    }
}}
