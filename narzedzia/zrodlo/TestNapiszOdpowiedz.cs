using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
namespace Ecxp {
class TestNapiszOdpowiedz {
 static int errors;static readonly BindingFlags Flags=BindingFlags.NonPublic|BindingFlags.Instance;
 static void Check(bool ok,string name){Console.WriteLine((ok?"PASS ":"FAIL ")+name);if(!ok)errors++;}
 class Server:IDisposable {
  public readonly ManualResetEvent Ready=new ManualResetEvent(false);NamedPipeServerStream pipe;public Exception Error;
  public Server(){new Thread(()=>{try{using(pipe=new NamedPipeServerStream(Ramka.NazwaPotoku(P.Root),PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.None)){
   Ready.Set();pipe.WaitForConnection();var h=Ramka.Czytaj(pipe);Send(new Rama{Typ=Ramka.WITAJ,Flagi=Ramka.F_SYN|Ramka.F_ACK,Polaczenie=1,Seq=100,Ack=h.Seq+1}.Z(P.D("ja",P.Wlasciciel)));Ramka.Czytaj(pipe);ulong seq=101;
   while(true){var req=Ramka.Czytaj(pipe);if(req.Typ==Ramka.FIN)break;if(req.Typ!=Ramka.ZAPYTANIE)continue;
    var j=req.Json();var op=P.S(j,"op");var a=(Dictionary<string,object>)j["args"];object result=P.D();
    if(op=="wyslij")result=Siec.Wyslij(new P.Nowa{Od=P.Wlasciciel,Do=P.S(a,"do"),Projekt=P.S(a,"projekt"),Modul=P.S(a,"modul"),Typ=P.S(a,"typ"),Temat=P.S(a,"temat"),Tresc=P.S(a,"tresc"),OdpNa=P.S(a,"odpowiedz_na"),BezKopii=P.B(a,"bez_kopii")});
    if(op=="uslugi")result=P.D("codex",P.D(),"claude",P.D());
    Send(new Rama{Typ=Ramka.ODPOWIEDZ,Polaczenie=1,Seq=seq++,Ack=req.Seq+1}.Z(P.D("ok",true,"wynik",result)));
   }
  }}catch(Exception ex){if(!(ex is IOException)&&!(ex is ObjectDisposedException)){Error=ex;Console.WriteLine("SERWER TESTOWY: "+ex);}}}){IsBackground=true}.Start();}
  void Send(Rama r){var b=Ramka.Koduj(r);pipe.Write(b,0,b.Length);pipe.Flush();}
  public void Dispose(){if(pipe!=null)pipe.Dispose();}
 }
 static void WaitFor(Func<bool> done){var end=DateTime.Now.AddSeconds(20);while(!done()&&DateTime.Now<end){Agenci.KrokPetli();Application.DoEvents();Thread.Sleep(100);}}
 static void Compose(App app,Msg anchor,string text){
  bool filled=false;var timer=new System.Windows.Forms.Timer{Interval=80};
  timer.Tick+=(s,e)=>{var f=Application.OpenForms.OfType<PiszForm>().FirstOrDefault();if(f==null)return;timer.Stop();filled=true;
   if(anchor==null){Check(f.Typ=="pytanie","Napisz: domyślny typ to pytanie");f.UstawAdresata("para:rozmowa");f.cbProjekt.Text="test-formularzy";f.cbModul.Text="rozmowa";f.tbTemat.Text="NAPISZ";}
   else Check(f.Typ=="odpowiedz"&&f.OdpNa==anchor.Id&&f.AdresatDoWyslania==anchor.Od.Split('-')[0]+"/rozmowa","Odpowiedz: typ, rodzic i pojedynczy adresat "+anchor.Od);
   f.tbTresc.Text=text;typeof(PiszForm).GetMethod("Wyslij",Flags).Invoke(f,null);
  };
  try{timer.Start();typeof(App).GetMethod("Pisz",Flags,null,new[]{typeof(Msg)},null).Invoke(app,new object[]{anchor});Check(filled,"otwarto i wysłano formularz "+(anchor==null?"Napisz":"Odpowiedz"));}
  finally{timer.Dispose();}
 }
 [STAThread] public static int Main(string[] args){
  if(args.Length!=2||Directory.Exists(args[0]))return 2;string old=Environment.GetEnvironmentVariable("PATH");App app=null;Form form=null;
  try{
   P.Root=Path.GetFullPath(args[0]);foreach(var d in P.Foldery)Directory.CreateDirectory(Path.Combine(P.Root,d));Directory.CreateDirectory(Path.Combine(P.Root,"narzedzia"));
   var mock=Path.Combine(P.Root,"mock");Directory.CreateDirectory(mock);var fixture=Path.Combine(mock,"provider.py");File.Copy(args[1],fixture);
   var calls=Path.Combine(P.Root,"calls.jsonl");foreach(var side in new[]{"codex","claude"})File.WriteAllText(Path.Combine(mock,side+".cmd"),"@echo off\r\n\"C:\\Python314\\python.exe\" \""+fixture+"\" "+side+" \""+calls+"\"\r\n");
   Environment.SetEnvironmentVariable("PATH",mock+";"+old);var roles=new List<string>{"programista"};
   Siec.NoweZadanie("test-formularzy","rozmowa","test","syntetyczny",roles,roles,"ECXP",null,false,null,null,Siec.Ikony[1],false,false);
   foreach(var side in new[]{"codex","claude"})Agenci.Uruchom(side+"-rozmowa","test-formularzy","rozmowa",P.Root,"fixture","edycja",roles,"test",12,"");
   using(var server=new Server()){
    server.Ready.WaitOne();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);app=new App();
    ((System.Windows.Forms.Timer)typeof(App).GetField("timer",Flags).GetValue(app)).Stop();form=(Form)typeof(App).GetField("form",Flags).GetValue(app);form.Text="TEST formularzy wiadomości";
    Compose(app,null,"NAPISZ-MARKER");
    var sent=P.Wszystkie(false).Where(m=>m.Od==P.Wlasciciel&&m.Uwagi=="NAPISZ-MARKER").ToList();Check(sent.Count==2,"Napisz: obie strony pary dostały wiadomość");
    WaitFor(()=>sent.All(m=>P.Wszystkie(false).Any(r=>r.OdpNa==m.Id&&r.Od==m.Do)));
    foreach(var m in sent){var replies=P.Wszystkie(false).Where(r=>r.OdpNa==m.Id&&r.Od==m.Do).ToList();Check(replies.Count==1&&replies[0].Uwagi=="ODEBRANO: NAPISZ-MARKER","Napisz: odbiór i odpowiedź "+m.Do);}
    var anchors=sent.Select(m=>P.Wszystkie(false).Single(r=>r.OdpNa==m.Id&&r.Od==m.Do)).ToList();
    foreach(var anchor in anchors){Compose(app,anchor,"ODPOWIEDZ-MARKER-"+anchor.Od);var follow=P.Wszystkie(false).Where(m=>m.Od==P.Wlasciciel&&m.Uwagi=="ODPOWIEDZ-MARKER-"+anchor.Od).ToList();Check(follow.Count==1&&follow[0].Do==anchor.Od,"Odpowiedz: brak kopii do drugiej strony");WaitFor(()=>follow.All(m=>P.Wszystkie(false).Any(r=>r.OdpNa==m.Id&&r.Od==m.Do)));Check(follow.Count==1&&P.Wszystkie(false).Count(r=>r.OdpNa==follow[0].Id&&r.Od==anchor.Od&&r.Uwagi=="ODEBRANO: ODPOWIEDZ-MARKER-"+anchor.Od)==1,"Odpowiedz: odbiór i wynik "+anchor.Od);}
    Check(File.ReadAllLines(calls).Length==4,"cztery wiadomości, cztery tury, bez duplikatów");
    Check(server.Error==null,"transport formularzy bez błędu");
    form.Close();form=null;
   }
  }catch(Exception ex){Console.WriteLine(ex);return 1;}
  finally{Cieply.ZamknijWszystko();if(form!=null)form.Close();Environment.SetEnvironmentVariable("PATH",old);}
  return errors==0?0:1;
 }
}}
