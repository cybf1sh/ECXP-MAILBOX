// Test strażnika zadań (rdzeń stosu): pytanie o etap, odpowiedź w_trakcie z etapem, brak pomyłki z FIN.
// Użycie: TestStraznika.exe <pusty katalog>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace Ecxp {
class TestStraznika {
 static int failures;
 static void Check(bool ok,string name){Console.WriteLine((ok?"PASS ":"FAIL ")+name);if(!ok)failures++;}
 static List<Msg> Do(string kto){return P.Wszystkie(false).Where(m=>m.Do==kto).ToList();}
 static bool Rzuca(Action a){try{a();return false;}catch(EcxpException){return true;}}
 public static int Main(string[] args){
  if(args.Length<1){Console.Error.WriteLine("Użycie: TestStraznika.exe <pusty katalog>");return 2;}
  P.Root=Path.GetFullPath(args[0]);Directory.CreateDirectory(P.Root);if(Directory.EnumerateFileSystemEntries(P.Root).Any())throw new Exception("Empty fixture required");
  foreach(var d in P.Foldery)Directory.CreateDirectory(Path.Combine(P.Root,d));
  var config=Path.Combine(P.Root,"projekty","test");Directory.CreateDirectory(config);
  File.WriteAllText(Path.Combine(config,"projekt.json"),P.Js().Serialize(P.D("pary",P.D("test",P.D("codex","codex-p","claude","claude-p","wykonawca","codex-p")))));
  // codex-p: sesja w aplikacji (z MCP); claude-p: agent w tle (bez MCP)
  Siec.Przypisz("codex-p","test","test","c-session",new List<string>(),"","codex-p");
  Siec.Przypisz("claude-p","test","test","tlo:claude-p",new List<string>(),"",P.Wlasciciel);
  Siec.Wyslij(new P.Nowa{Od=P.Wlasciciel,Do="para:test",Projekt="test",Modul="test",Typ="zadanie",Temat="Zadanie śledzone",Tresc="Syntetyczne zadanie."});
  var zad=P.Wszystkie(false).Where(m=>m.Typ=="zadanie").ToList();
  var c=zad.Single(m=>m.Do=="codex-p");var s=zad.Single(m=>m.Do=="claude-p");
  P.Odbierz("codex-p",50);
  DateTime kiedy;
  Check(Obieg.EtapZgloszony(c.Id,"codex-p",out kiedy)=="" && Obieg.OstatniePytanieOEtap(c.Id,"codex-p")==DateTime.MinValue,"no stage and no question before asking");

  var r=P.ZBlokada(()=>Obieg.ZapytajOEtap(c.Id)).Cast<Dictionary<string,object>>().ToList();
  Check(r.Any(x=>P.S(x,"agent")=="codex-p" && P.S(x,"sposob")=="zapytano"),"app session is asked for its stage");
  Check(r.Any(x=>P.S(x,"agent")=="claude-p" && P.S(x,"sposob")=="w_tle"),"background agent is not asked (no MCP), reported as background");
  var q=Do("codex-p").Where(m=>m.Od==P.Straznik && m.Temat.StartsWith(Obieg.TematPytaniaOEtap)).ToList();
  Check(q.Count==1 && q[0].Typ=="informacja" && q[0].Uwagi.Contains("typ=postep") && q[0].Uwagi.Contains("odpowiedz_na="+c.Id),"question is one informacja from the watcher with exact reply instructions");
  Check(!Do("claude-p").Any(m=>m.Od==P.Straznik),"background agent receives no question");
  Check(Obieg.Znajdz(q[0].Id)==null,"question is not attached to the task flow (cannot become FIN)");
  Check(!Agenci.WymagaTury(P.Straznik,"informacja"),"watcher message never triggers a background turn");
  Check(Obieg.OstatniePytanieOEtap(c.Id,"codex-p")>DateTime.Now.AddMinutes(-1),"question time recorded in task flow");

  var r2=P.ZBlokada(()=>Obieg.ZapytajOEtap(c.Id)).Cast<Dictionary<string,object>>().ToList();
  Check(r2.Any(x=>P.S(x,"agent")=="codex-p" && P.S(x,"sposob")=="niedawno") && Do("codex-p").Count(m=>m.Od==P.Straznik)==1,"repeated question within a minute is suppressed");

  int doOperatora=Do(P.Wlasciciel).Count;
  var ack=Siec.Wyslij(new P.Nowa{Od="codex-p",Do=P.Wlasciciel,Projekt="test",Modul="test",Typ="w_trakcie",OdpNa=c.Id,Tresc="Etap 2 z 3: piszę testy."});
  Check(Obieg.EtapZgloszony(c.Id,"codex-p",out kiedy)=="Etap 2 z 3: piszę testy." && kiedy>DateTime.Now.AddMinutes(-1),"in-progress reply stores the reported stage");
  Check(Obieg.Otwarte().Count==1 && Obieg.CzekaNaFin(c.Id,"codex-p"),"stage reply keeps the task open and still waiting for FIN");
  Check(!Do(P.Wlasciciel).Skip(doOperatora).Any(m=>m.Typ=="fin"),"stage reply produces no FIN for the operator");
  Check(File.ReadLines(Path.Combine(P.Root,"dziennik","dziennik.jsonl")).Any(l=>l.Contains("obieg_etap") && l.Contains("codex-p")),"stage is recorded in the audit log");
  Check(File.ReadLines(Path.Combine(P.Root,"dziennik","dziennik.jsonl")).Any(l=>l.Contains("obieg_pytanie_o_etap")),"question is recorded in the audit log");

  Siec.Wyslij(new P.Nowa{Od="codex-p",Do=P.Wlasciciel,Projekt="test",Modul="test",Typ="w_trakcie",OdpNa=c.Id,Tresc=new string('x',900)});
  Check(Obieg.EtapZgloszony(c.Id,"codex-p",out kiedy).Length==Obieg.EtapMaks+1,"long stage text is truncated");
  Siec.Wyslij(new P.Nowa{Od="codex-p",Do=P.Wlasciciel,Projekt="test",Modul="test",Typ="w_trakcie",OdpNa=c.Id});
  Check(Obieg.EtapZgloszony(c.Id,"codex-p",out kiedy).Length==Obieg.EtapMaks+1,"empty in-progress keeps the last reported stage");

  // agent omyłkowo odpowiada na samo pytanie zwykłą odpowiedzią – to nie może zamknąć zadania
  Siec.Wyslij(new P.Nowa{Od="codex-p",Do=P.Wlasciciel,Projekt="test",Modul="test",Typ="odpowiedz",OdpNa=q[0].Id,Tresc="Jestem w połowie."});
  Check(Obieg.Otwarte().Count==1 && Obieg.CzekaNaFin(c.Id,"codex-p"),"reply to the watcher question is not taken as FIN");

  Check(Rzuca(()=>Obieg.ZapytajOEtap("nie-ma-takiego-id")),"asking about a message outside a task flow is refused");
  Siec.Wyslij(new P.Nowa{Od="codex-p",Do=P.Wlasciciel,Projekt="test",Modul="test",Typ="fin",OdpNa=c.Id,Tresc="POZYTYWNY - gotowe"});
  P.Odbierz("claude-p",50);
  Siec.Wyslij(new P.Nowa{Od="claude-p",Do=P.Wlasciciel,Projekt="test",Modul="test",Typ="fin",OdpNa=s.Id,Tresc="POZYTYWNY - sprawdzone"});
  Check(Obieg.Otwarte().Count==0 && Do(P.Wlasciciel).Count(m=>m.EtapObiegu=="fin_wspolny")==1,"normal FIN still closes the task after stage reports");
  Check(Rzuca(()=>Obieg.ZapytajOEtap(c.Id)),"asking about a finished task is refused");
  Check(Rzuca(()=>Siec.Wyslij(new P.Nowa{Od="codex-p",Do=P.Wlasciciel,Projekt="test",Modul="test",Typ="w_trakcie",OdpNa=c.Id,Tresc="po FIN"})),"stage after FIN is refused");
  Console.WriteLine("RESULT: "+failures+" failed");
  return failures==0?0:1;
 }
}}
