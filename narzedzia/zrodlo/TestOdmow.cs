using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
namespace Ecxp {
class TestOdmow {
 static int errors;
 static void Check(bool ok,string n){Console.WriteLine((ok?"PASS ":"FAIL ")+n);if(!ok)errors++;}
 static void MainTest(string root) {
  P.Root=Path.GetFullPath(root);if(Directory.Exists(P.Root))throw new Exception("Root must be new");
  foreach(var f in P.Foldery)Directory.CreateDirectory(Path.Combine(P.Root,f));
  foreach(var value in new[]{"{\"codexErrorInfo\":\"cyberPolicy\"}","{\"stop_reason\":\"refusal\",\"is_error\":true,\"result\":\"stream-json disabled\"}","Opus 5.5's safeguards flagged this session", "{\\\"stop_reason\\\":\\\"refusal\\\"}"})
   Check(Agenci.Przyczyna(1,value)=="odmowa","odmowa: "+value);
  Check(Agenci.Przyczyna(0,"explained stop_reason refusal in a successful report")=="","sukces nie jest odmową");
  Check(Agenci.Przyczyna(1,"Error: getaddrinfo ENOTFOUND api.anthropic.com")=="siec","błąd sieci zachowuje własną kategorię");
  Check(Agenci.Przyczyna(1,"unknown option stream-json")=="inny","brak opcji nie jest odmową");
  var roles=new List<string>{"tester"};
  Siec.NoweZadanie("odmowy-test","r1","test","syntetyczny",roles,roles,"ECXP",null,false,null,null,Siec.Ikony[1],false,false);
  Siec.Wyslij(new P.Nowa{Od=P.Wlasciciel,Do="para:r1",Projekt="odmowy-test",Modul="r1",Typ="zadanie",Temat="test",Tresc="syntetyczny"});
  var ob=Obieg.Otwarte().Single();var entries=(Dictionary<string,object>)ob["agenci"];
  var id=P.S((Dictionary<string,object>)entries["codex-r1"],"zadanie");var msg=P.Znajdz(id);
  var rec=P.D("nazwa","codex-r1","projekt","odmowy-test","modul","r1","strona","codex","aktywny",true);
  Directory.CreateDirectory(Path.Combine(P.Root,"stan","agenci"));
  P.ZBlokada(()=>{Agenci.ZakonczOdmowe(rec,msg,null);return 0;});
  var after=Obieg.Znajdz(id);var ae=(Dictionary<string,object>)after["agenci"];
  var own=(Dictionary<string,object>)ae["codex-r1"];var other=(Dictionary<string,object>)ae["claude-r1"];
  Check(P.S(own,"stan")=="fin"&&P.S(own,"fin").StartsWith("NIEWYKONANE"),"odmowa kończy się FIN niewykonane, nie negatywnym wynikiem pracy");
  Check(P.S(other,"rola")=="sam"&&P.S(other,"stan")!="fin","bez przekazania partnerowi także poza pentestem");
  var saved=Pliki.Czytaj(Path.Combine(P.Root,"stan","agenci","codex-r1.json"));
  Check(Agenci.OdmowionoPracy(saved,id),"odmowa zapisana przed FIN i przetrwa restart");
  Check(!Agenci.OdmowionoPracy(saved,"inne-zadanie"),"kolejna praca nie jest blokowana");
  // Prawdziwa pętla, transport Claude stream-json i FIN; fixture nie łączy się z dostawcą.
  var mock=Path.Combine(P.Root,"mock");Directory.CreateDirectory(mock);
  var fixture=Path.Combine(mock,"fake.py");var calls=Path.Combine(P.Root,"calls.txt");
  File.WriteAllText(fixture,"import sys,json\nfor line in sys.stdin:\n if not line.strip(): continue\n with open("+P.Js().Serialize(calls)+",'a') as f: f.write('request\\n')\n print(json.dumps({'type':'result','is_error':('stream-json' in sys.argv),'stop_reason':'refusal','result':'safeguards flagged this session; stream-json disabled'}),flush=True)\n break\n");
  File.WriteAllText(Path.Combine(mock,"claude.cmd"),"@echo off\r\n\"C:\\Python314\\python.exe\" \""+fixture+"\"\r\n");
  var old=Environment.GetEnvironmentVariable("PATH");Environment.SetEnvironmentVariable("PATH",mock+";"+old);
  try {
   Agenci.Uruchom("claude-r1","odmowy-test","r1",P.Root,"test-model","edycja",roles,"test",12,"");
   var deadline=DateTime.Now.AddSeconds(15);
   while(DateTime.Now<deadline) {
    Agenci.KrokPetli();Thread.Sleep(100);
    var r=Obieg.Znajdz(id);var es=(Dictionary<string,object>)r["agenci"];
    if(P.S((Dictionary<string,object>)es["claude-r1"],"stan")=="fin")break;
   }
   for(int i=0;i<8;i++){Agenci.KrokPetli();Thread.Sleep(100);}
   Check(File.Exists(calls)&&File.ReadAllLines(calls).Length==1,"jedno wywołanie modelu, bez fallbacku i powtórzeń");
   var final=Pliki.Czytaj(Path.Combine(P.Root,"stan","agenci","claude-r1.json"));
   Check(P.Lista(final,"uruchomienia").Count==1&&P.S(P.Lista(final,"uruchomienia")[0],"przyczyna")=="odmowa","jedna tura z kategorią odmowa");
   var end=(Dictionary<string,object>)Obieg.Znajdz(id)["agenci"];
   Check(P.S((Dictionary<string,object>)end["claude-r1"],"stan")=="fin","pętla sama kończy odmowę FIN");
   Check(!Cieply.Zastepczy("claude"),"odmowa nie przełącza całej strony na tryb zastępczy");
   Agenci.Zatrzymaj("claude-r1");Cieply.ZamknijWszystko();Thread.Sleep(3500);
   var mode=Environment.GetEnvironmentVariable("ECXP_BEZ_CIEPLYCH");
   try {
    Environment.SetEnvironmentVariable("ECXP_BEZ_CIEPLYCH","1");
    var cold=Cieply.Wykonaj(P.D("nazwa","claude-cold","strona","claude","projekt","odmowy-test","modul","cold","katalog",P.Root,"uprawnienia","odczyt"),"syntetyczne",1,null);
    Check(cold.Kod!=0&&cold.Odpowiedz.Length==0,"zimny CLI z exit 0 i metadanymi odmowy nie jest sukcesem");
    Check(File.ReadAllLines(calls).Length==2,"zimna ścieżka wywołuje model tylko raz");
   } finally {Environment.SetEnvironmentVariable("ECXP_BEZ_CIEPLYCH",mode);}
   var question=Siec.Wyslij(new P.Nowa{Od=P.Wlasciciel,Do="claude-r1",Projekt="odmowy-test",Modul="r1",Typ="pytanie",Temat="nowe pytanie",Tresc="syntetyczne"});
   var qid=P.S(question,"id");var d=Pliki.Czytaj(Path.Combine(P.Root,"stan","agenci","claude-r1.json"));d["pytania"]=new List<string>{qid};
   P.ZBlokada(()=>{Agenci.ZakonczOdmowe(d,null,P.Znajdz(qid));Pliki.Zapisz(Path.Combine(P.Root,"stan","agenci","claude-r1.json"),d);return 0;});
   Check(P.L(d,"pytania").Count==0&&Agenci.OdmowionoPracy(d,qid),"odmowa pytania usuwa je z kolejki i zapamiętuje jego ID");
   Cieply.ZamknijWszystko();
   File.WriteAllText(fixture,"import sys\nfor line in sys.stdin:\n with open("+P.Js().Serialize(calls)+",'a') as f: f.write('crash\\n')\n sys.exit(1)\n");
   int beforeCrash=File.ReadAllLines(calls).Length;
   var crashed=Cieply.Wykonaj(P.D("nazwa","claude-crash","strona","claude","projekt","odmowy-test","katalog",P.Root,"uprawnienia","odczyt"),"syntetyczne",1,null);
   Check(crashed.Kod!=0&&File.ReadAllLines(calls).Length==beforeCrash+1&&!Cieply.Zastepczy("claude"),"awaria Claude po doręczeniu nie ponawia pracy w zimnym transporcie");
   File.WriteAllText(fixture,"import sys,time\nfor line in sys.stdin:\n with open("+P.Js().Serialize(calls)+",'a') as f: f.write('stop\\n')\n time.sleep(30)\n");
   int beforeStop=File.ReadAllLines(calls).Length;
   var stopper=new Thread(()=>{var stopAt=DateTime.UtcNow.AddSeconds(10);while(File.ReadAllLines(calls).Length==beforeStop&&DateTime.UtcNow<stopAt)Thread.Sleep(50);Cieply.Zamknij(n=>n=="claude-stop");});stopper.IsBackground=true;stopper.Start();
   var stopped=Cieply.Wykonaj(P.D("nazwa","claude-stop","strona","claude","projekt","odmowy-test","katalog",P.Root,"uprawnienia","odczyt"),"syntetyczne",1,null);stopper.Join(11000);
   Check(stopped.Kod!=0&&File.ReadAllLines(calls).Length==beforeStop+1&&!Cieply.Zastepczy("claude"),"Stop Claude po doręczeniu nie uruchamia zadania ponownie");
   var legacy=Path.Combine(mock,"legacy.py");File.WriteAllText(legacy,"import sys,json\nif '--input-format' in sys.argv:\n print(\"error: unknown option '--input-format'\",file=sys.stderr,flush=True)\n sys.exit(1)\nsys.stdin.read()\nprint(json.dumps({'type':'result','is_error':False,'result':'LEGACY_OK'}),flush=True)\n");
   File.WriteAllText(Path.Combine(mock,"claude.cmd"),"@echo off\r\n\"C:\\Python314\\python.exe\" \""+legacy+"\" %*\r\n");
   var compatible=Cieply.Wykonaj(P.D("nazwa","claude-legacy","strona","claude","projekt","odmowy-test","katalog",P.Root,"uprawnienia","odczyt"),"syntetyczne",1,null);
   Check(compatible.Kod==0&&compatible.Odpowiedz=="LEGACY_OK"&&Cieply.Zastepczy("claude"),"jawny błąd parsera opcji przed pracą nadal pozwala na zgodny transport jednorazowy");
   var codexFixture=Path.Combine(mock,"codex.py");File.WriteAllText(codexFixture,@"import sys,json,uuid,pathlib
def log(text):
 with open("+P.Js().Serialize(calls)+@",'a') as f: f.write(text+'\n')
def send(value): print(json.dumps(value),flush=True)
if 'exec' in sys.argv:
 log('codex-cold-duplicate');pathlib.Path(sys.argv[sys.argv.index('-o')+1]).write_text('duplicate');sys.exit(0)
for line in sys.stdin:
 r=json.loads(line)
 if 'id' not in r: continue
 m=r.get('method');out={}
 if m=='initialize': out={'codexHome':"+P.Js().Serialize(mock)+@"}
 if m=='project/create': out={'project':{'id':'fixture-project'}}
 if m=='thread/start': out={'thread':{'id':str(uuid.uuid4())}}
 if m=='turn/start': log('codex-accepted-no-ack');sys.exit(1)
 send({'id':r['id'],'result':out})
");
   File.WriteAllText(Path.Combine(mock,"codex.cmd"),"@echo off\r\n\"C:\\Python314\\python.exe\" \""+codexFixture+"\" %*\r\n");
   var uncertain=Cieply.Wykonaj(P.D("nazwa","codex-no-ack","strona","codex","projekt","odmowy-test","katalog",P.Root,"uprawnienia","odczyt","model","fixture"),"syntetyczne",1,null);
   Check(uncertain.Kod!=0&&File.ReadAllLines(calls).Count(x=>x=="codex-accepted-no-ack")==1&&!File.ReadAllLines(calls).Contains("codex-cold-duplicate")&&!Cieply.Zastepczy("codex"),"Codex po utracie ACK turn/start nie powtarza potencjalnie wykonanej pracy");
  } finally {Environment.SetEnvironmentVariable("PATH",old);}
 }
 public static int Main(string[] args){if(args.Length!=1)return 2;try{MainTest(args[0]);}catch(Exception ex){Console.WriteLine(ex);return 1;}return errors==0?0:1;}
}}
