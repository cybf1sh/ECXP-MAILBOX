using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
namespace Ecxp {
class TestPotwierdzen {
 static int failures;
 static void Check(bool ok,string name){Console.WriteLine((ok?"PASS ":"FAIL ")+name);if(!ok)failures++;}
 static List<Msg> Operator(){return P.Wszystkie(false).Where(m=>m.Do==P.Wlasciciel).ToList();}
 public static int Main(string[] args){
  P.Root=Path.GetFullPath(args[0]);Directory.CreateDirectory(P.Root);if(Directory.EnumerateFileSystemEntries(P.Root).Any())throw new Exception("Empty fixture required");
  foreach(var d in P.Foldery)Directory.CreateDirectory(Path.Combine(P.Root,d));
  var config=Path.Combine(P.Root,"projekty","test");Directory.CreateDirectory(config);
  File.WriteAllText(Path.Combine(config,"projekt.json"),P.Js().Serialize(P.D("pary",P.D("test",P.D("codex","codex-p","claude","claude-p","wykonawca","codex-p")))));
  Siec.Przypisz("codex-p","test","test","c-session",new List<string>(),"","codex-p");
  Siec.Przypisz("claude-p","test","test","s-session",new List<string>(),"","claude-p");
  Siec.Wyslij(new P.Nowa{Od=P.Wlasciciel,Do="para:test",Projekt="test",Modul="test",Typ="zadanie",Temat="Test widocznego postępu",Tresc="Syntetyczne zadanie."});
  var requests=P.Wszystkie(false).Where(m=>m.Typ=="zadanie").ToList();
  var c=requests.Single(m=>m.Do=="codex-p");var s=requests.Single(m=>m.Do=="claude-p");
  P.Odbierz("codex-p",50);
  var receipt=Operator().SingleOrDefault(m=>m.EtapObiegu=="odebrane");
  Check(receipt!=null && receipt.Od=="codex-p" && receipt.OdpNa==c.Id,"receipt immediately visible with original request ID");
  Check(!Obieg.WynikAdresata(receipt,"codex-p"),"receipt does not mark task completed");
  P.Odbierz("codex-p",50);Check(Operator().Count==1,"repeated receive does not duplicate receipt");
  var n=new P.Nowa{Od="codex-p",Do=P.Wlasciciel,Projekt="test",Modul="test",Typ="fin",OdpNa=c.Id,Tresc="POZYTYWNY - wynik Codexa"};
  Siec.Wyslij(n);
  var part=Operator().SingleOrDefault(m=>m.EtapObiegu=="fin_czesciowy");
  Check(part!=null && part.OdpNa==c.Id && part.Uwagi.Contains("wynik Codexa"),"first FIN visible while partner pending");
  Check(Obieg.Otwarte().Count==1,"partial FIN preserves open pair");
  Check(Obieg.WynikAdresata(part,"codex-p") && !Obieg.WynikAdresata(part,"claude-p"),"partial FIN completes only sender");
  int before=Operator().Count;Siec.Wyslij(n);Check(Operator().Count==before,"repeated partial FIN is idempotent");
  P.Odbierz("claude-p",50);
  Siec.Wyslij(new P.Nowa{Od="claude-p",Do=P.Wlasciciel,Projekt="test",Modul="test",Typ="fin",OdpNa=s.Id,Tresc="POZYTYWNY - sprawdzone przez Claude"});
  var common=Operator().Where(m=>m.EtapObiegu=="fin_wspolny").ToList();
  Check(common.Count==1 && common[0].Uwagi.Contains("wynik Codexa") && common[0].Uwagi.Contains("sprawdzone przez Claude"),"one shared FIN contains both results");
  Check(Obieg.Otwarte().Count==0 && Obieg.WynikAdresata(common[0],"codex-p") && Obieg.WynikAdresata(common[0],"claude-p"),"shared FIN closes both tracking entries");
  Check(Operator().Count(m=>m.EtapObiegu=="odebrane")==2,"each side has receipt");
  Check(Obieg.WynikAdresata(new Msg{Od="claude-p",Typ="fin"},"codex-p"),"legacy FIN without metadata keeps previous common-result behavior");
  Check(Obieg.WynikAdresata(new Msg{Od="codex-p",Typ="odpowiedz"},"codex-p"),"legacy answer without metadata remains visible");
  var solo=Siec.Wyslij(new P.Nowa{Od=P.Wlasciciel,Do="codex/test",BezKopii=true,Projekt="test",Modul="test",Typ="zadanie",Tresc="Tylko jeden agent."});
  string soloId=P.S(solo,"id");
  Siec.Wyslij(new P.Nowa{Od="codex-p",Do=P.Wlasciciel,Projekt="test",Modul="test",Typ="w_trakcie",OdpNa=soloId});
  Check(Operator().Count(m=>m.OdpNa==soloId && m.EtapObiegu=="odebrane")==1,"explicit in-progress notification also reports receipt");
  P.Odbierz("codex-p",50);
  Check(Operator().Count(m=>m.OdpNa==soloId && m.EtapObiegu=="odebrane")==1,"later receive does not duplicate explicit receipt");
  Siec.Wyslij(new P.Nowa{Od="codex-p",Do=P.Wlasciciel,Projekt="test",Modul="test",Typ="fin",OdpNa=soloId,Tresc="POZYTYWNY - solo"});
  Check(Operator().Count(m=>m.OdpNa==soloId && m.Typ=="fin")==1 && Obieg.Otwarte().Count==0,"single-agent task has one final result and closes");
  bool rejected=false;try{CodexBudzenie.Przygotuj(args[1],"wrong-id","test",P.Root);}catch(EcxpException){rejected=true;}
  Check(rejected,"invalid thread ID rejected before process start");
  string id="11111111-2222-3333-4444-555555555555";
  string text="Unicode Żółć: \"quoted\" & | %TEMP% trailing\\";
  var psi=CodexBudzenie.Przygotuj(args[1],id,text,P.Root);
  Check(!psi.UseShellExecute && psi.FileName.EndsWith(".exe") && !psi.Arguments.Contains("approval") && !psi.Arguments.Contains("sandbox"),"native executable, no shell or permission overrides");
  using(var process=Process.Start(psi)){
   string output=process.StandardOutput.ReadToEnd();process.WaitForExit();
   Check(process.ExitCode==0 && output.Contains("TEXT_BASE64:"+Convert.ToBase64String(Encoding.UTF8.GetBytes(text))),"argument quoting preserves Unicode, quotes, metacharacters and trailing slash");
  }
  Check(CodexBudzenie.Wyslij(args[1],id,"test",P.Root).Contains("kolejce"),"queue confirmation is not reported as ACK or completion");
  foreach(var bad in new[]{"ERROR","WRONG_THREAD"}){rejected=false;try{CodexBudzenie.Wyslij(args[1],id,bad,P.Root);}catch(EcxpException){rejected=true;}Check(rejected,"zero exit insufficient without matching confirmation: "+bad);}
  Console.WriteLine("RESULT: "+failures+" failed");return failures;
 }
}}