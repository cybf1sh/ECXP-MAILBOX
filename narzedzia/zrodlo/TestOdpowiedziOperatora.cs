using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;

namespace Ecxp {
class TestOdpowiedziOperatora {
 static int errors;
 static System.Diagnostics.Process server;
 static void Check(bool ok,string name){Console.WriteLine((ok?"PASS ":"FAIL ")+name);if(!ok)errors++;}
 static Dictionary<string,object> Agent(string name){return Pliki.Czytaj(Path.Combine(P.Root,"stan","agenci",name+".json"));}
 static void Step(){if(server==null)Agenci.KrokPetli();Thread.Sleep(100);}
 static void WaitFor(Func<bool> done){var end=DateTime.Now.AddSeconds(20);while(!done()&&DateTime.Now<end)Step();}
 static void Run(string root,string stack) {
  P.Root=Path.GetFullPath(root);if(Directory.Exists(P.Root))throw new Exception("Wymagany nowy katalog");
  foreach(var d in P.Foldery)Directory.CreateDirectory(Path.Combine(P.Root,d));
  var mock=Path.Combine(P.Root,"mock");Directory.CreateDirectory(mock);
  var calls=Path.Combine(P.Root,"calls.jsonl");var fixture=Path.Combine(mock,"provider.py");
  File.WriteAllText(fixture,@"import sys,json,uuid
sys.stdin.reconfigure(encoding='utf-8')
sys.stdout.reconfigure(encoding='utf-8')
kind=sys.argv[1]
def send(v):print(json.dumps(v),flush=True)
def answer(text):
 with open(sys.argv[2],'a',encoding='utf-8') as f:f.write(json.dumps({'provider':kind,'text':text})+'\n')
 return 'ODEBRANO: '+text.split('\n\n',1)[0]
for line in sys.stdin:
 try:r=json.loads(line)
 except ValueError:continue
 if kind=='claude':
  send({'type':'result','is_error':False,'stop_reason':'end_turn','result':answer(r['message']['content'])})
  break
 if 'id' not in r:continue
 m=r.get('method');out={}
 if m=='initialize':out={'codexHome':sys.argv[2]+'.home'}
 if m=='project/create':out={'project':{'id':'fixture-project'}}
 if m=='thread/start':out={'thread':{'id':str(uuid.uuid4())}}
 if m=='thread/loaded/list':out={'data':[]}
 if m=='turn/start':
  text='\n'.join(i.get('text','') for i in r['params']['input'])
  send({'id':r['id'],'result':{'turn':{'id':'fixture-turn'}}})
  send({'method':'item/completed','params':{'item':{'type':'agentMessage','text':answer(text)}}})
  send({'method':'turn/completed','params':{'turn':{'status':'completed'}}})
  continue
 send({'id':r['id'],'result':out})
");
  foreach(var side in new[]{"codex","claude"})File.WriteAllText(Path.Combine(mock,side+".cmd"),"@echo off\r\n\"C:\\Python314\\python.exe\" \""+fixture+"\" "+side+" \""+calls+"\"\r\n");
  var old=Environment.GetEnvironmentVariable("PATH");Environment.SetEnvironmentVariable("PATH",mock+";"+old);
  try {
   var role=new List<string>{"programista"};
   Siec.NoweZadanie("test-odpowiedzi","rozmowa","test","syntetyczne",role,role,"ECXP",null,false,null,null,Siec.Ikony[1],false,false);
   var ids=new Dictionary<string,string>();
   foreach(var side in new[]{"codex","claude"}) {
    var name=side+"-rozmowa";
    Agenci.Uruchom(name,"test-odpowiedzi","rozmowa",P.Root,"fixture","edycja",role,"test",12,"");
    // Wcześniej odrzucone ID nie może blokować nowej wiadomości ani zostać ponowione.
    var rec=Agent(name);rec["odmowy_prac"]=new List<string>{"poprzednia-odmowa"};Pliki.Zapisz(Path.Combine(P.Root,"stan","agenci",name+".json"),rec);
    var sent=Siec.Wyslij(new P.Nowa{Od=P.Wlasciciel,Do=name,Projekt="test-odpowiedzi",Modul="rozmowa",Typ="odpowiedz",Temat="ODPOWIEDZ-OPERATORA",Tresc="ODP-operatora-"+side,BezKopii=true});
    ids[name]=P.S(sent,"id");
   }
   if(stack.Length>0)server=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(stack,"/root \""+P.Root+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden});
   WaitFor(()=>ids.All(kv=>P.Wszystkie(false).Any(m=>m.Od==kv.Key&&m.OdpNa==kv.Value)));
   foreach(var kv in ids) {
    var replies=P.Wszystkie(false).Where(m=>m.Od==kv.Key&&m.Do==P.Wlasciciel&&m.OdpNa==kv.Value).ToList();
    Check(replies.Count==1&&replies[0].Uwagi=="ODEBRANO: ODP-operatora-"+kv.Key.Split('-')[0],kv.Key+": odpowiedź operatora uruchamia turę i dostaje wynik");
    var rec=Agent(kv.Key);
    Check(P.L(rec,"pytania").Count==0&&P.Lista(rec,"uruchomienia").Count==1,kv.Key+": kolejka opróżniona, jedna tura");
    Check(Agenci.OdmowionoPracy(rec,"poprzednia-odmowa")&&!Agenci.OdmowionoPracy(rec,kv.Value),kv.Key+": poprzednia odmowa zachowana, nowe ID dozwolone");
   }
   // Przycisk GUI Odpowiedz ustawia również odpowiedz_na poprzedniego raportu.
   // W trybie osobnego stosu wiadomości są przygotowane przed jego startem:
   // nie dokładamy drugiego procesu piszącego do działającej skrzynki.
   if(server==null) {
   var linked=new Dictionary<string,string>();
   foreach(var kv in ids) {
    var previous=P.Wszystkie(false).Single(m=>m.Od==kv.Key&&m.OdpNa==kv.Value);
    var sent=Siec.Wyslij(new P.Nowa{Od=P.Wlasciciel,Do=kv.Key,Projekt="test-odpowiedzi",Modul="rozmowa",Typ="odpowiedz",OdpNa=previous.Id,Temat="DALSZA-ROZMOWA",Tresc="DALSZA-ODP-"+kv.Key,BezKopii=true});
    linked[kv.Key]=P.S(sent,"id");
   }
   WaitFor(()=>linked.All(kv=>P.Wszystkie(false).Any(m=>m.Od==kv.Key&&m.OdpNa==kv.Value)));
   foreach(var kv in linked) {
    var replies=P.Wszystkie(false).Where(m=>m.Od==kv.Key&&m.Do==P.Wlasciciel&&m.OdpNa==kv.Value).ToList();
    Check(replies.Count==1&&replies[0].Uwagi=="ODEBRANO: DALSZA-ODP-"+kv.Key,kv.Key+": odpowiedź na raport także otrzymuje odpowiedź");
    Check(P.L(Agent(kv.Key),"pytania").Count==0&&P.Lista(Agent(kv.Key),"uruchomienia").Count==2,kv.Key+": po dalszej rozmowie dwie zakończone tury");
   }
   }
   for(int i=0;i<10;i++)Step();
   Check(File.Exists(calls)&&File.ReadAllLines(calls).Length==(server==null?4:2),"jedno wywołanie na wiadomość, bez duplikatów i pętli odpowiedzi");
   Check(!Agenci.WymagaTury("claude-rozmowa","odpowiedz")&&!Agenci.WymagaTury(P.Wlasciciel,"informacja"),"odpowiedzi agentów i informacje nie tworzą pętli");
   string state;P.Stany(out state);Check(state.StartsWith("spójny"),"dziennik pozostaje spójny");
  } finally {
   Cieply.ZamknijWszystko();
   if(server!=null&&!server.HasExited){
    // Wyłącznie drzewo procesu uruchomionego przez ten test; bez podszywania się pod konsolę operatora.
    using(var stop=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("taskkill.exe","/PID "+server.Id+" /T /F"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=System.Diagnostics.ProcessWindowStyle.Hidden})){stop.WaitForExit();}
    server.WaitForExit();
   }
   foreach(var side in new[]{"codex","claude"})Agenci.Zatrzymaj(side+"-rozmowa");
   Environment.SetEnvironmentVariable("PATH",old);
  }
 }
 public static int Main(string[] args){if(args.Length<1||args.Length>2)return 2;try{Run(args[0],args.Length==2?Path.GetFullPath(args[1]):"");}catch(Exception ex){Console.WriteLine(ex);return 1;}return errors==0?0:1;}
}}
