// Test zmian protokołu ecxp/1.1 (rdzeń stosu, dane syntetyczne).
// Użycie: TestProtokolu.exe <pusty katalog>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace Ecxp {
class TestProtokolu {
 static int failures;
 static void Check(bool ok,string name){Console.WriteLine((ok?"PASS ":"FAIL ")+name);if(!ok)failures++;}
 static List<Msg> Do(string kto){return P.Wszystkie(false).Where(m=>m.Do==kto).ToList();}
 static string Blad(Action a){try{a();return null;}catch(EcxpException ex){return ex.Message;}}
 static Dictionary<string,object> Zadanie(string temat){
  Siec.Wyslij(new P.Nowa{Od=P.Wlasciciel,Do="para:test",Projekt="test",Modul="test",Typ="zadanie",Temat=temat,Tresc="Syntetyczne zadanie: "+temat});
  var z=P.Wszystkie(false).Where(m=>m.Typ=="zadanie" && m.Uwagi.EndsWith(temat)).ToList();
  P.Odbierz("codex-p",50);P.Odbierz("claude-p",50);
  return P.D("codex",z.Single(m=>m.Do=="codex-p"),"claude",z.Single(m=>m.Do=="claude-p"));
 }
 public static int Main(string[] args){
  if(args.Length<1){Console.Error.WriteLine("Użycie: TestProtokolu.exe <pusty katalog>");return 2;}
  P.Root=Path.GetFullPath(args[0]);Directory.CreateDirectory(P.Root);if(Directory.EnumerateFileSystemEntries(P.Root).Any())throw new Exception("Empty fixture required");
  foreach(var d in P.Foldery)Directory.CreateDirectory(Path.Combine(P.Root,d));
  var config=Path.Combine(P.Root,"projekty","test");Directory.CreateDirectory(config);
  File.WriteAllText(Path.Combine(config,"projekt.json"),P.Js().Serialize(P.D("pary",P.D("test",P.D("codex","codex-p","claude","claude-p","wykonawca","codex-p")))));
  Siec.Przypisz("codex-p","test","test","c-session",new List<string>(),"","codex-p");
  Siec.Przypisz("claude-p","test","test","cl-session",new List<string>(),"","claude-p");

  // ---------- pkt 2: jeden identyfikator wiadomości dla pary ----------
  var z1=Zadanie("Pierwsze");var c1=(Msg)z1["codex"];var s1=(Msg)z1["claude"];
  Check(c1.Logiczne.Length>0 && c1.Logiczne==s1.Logiczne && (c1.Logiczne==c1.Id || c1.Logiczne==s1.Id),"both copies of a pair message carry one message id");
  Check(c1.Logiczne==s1.Id,"message id is the id of the first recipient by name (claude-p before codex-p)");
  var wszystkie=P.Wszystkie(false);
  Check(P.IdWiadomosci(c1,wszystkie)==P.IdWiadomosci(s1,wszystkie) && P.ZnacznikId(P.IdWiadomosci(c1,wszystkie))=="#"+s1.Id.Substring(s1.Id.Length-4),"both copies resolve to the same code");
  var solo=Siec.Wyslij(new P.Nowa{Od=P.Wlasciciel,Do="codex-p",Projekt="test",Modul="test",Typ="pytanie",Temat="Pytanie do jednego",Tresc="Jedno pytanie.",BezKopii=true});
  var soloM=P.Znajdz(P.S(solo,"id"));
  Check(soloM.Logiczne==soloM.Id && P.S(solo,"wiadomosc")==soloM.Id,"single-recipient message id equals its own id");
  // odpowiedź z identyfikatorem wiadomości (kopia innego adresata) trafia na kopię nadawcy
  var odp=Siec.Wyslij(new P.Nowa{Od="codex-p",Do=P.Wlasciciel,Projekt="test",Modul="test",Typ="odpowiedz",OdpNa=soloM.Id,Tresc="Odpowiedź na pytanie."});
  var odpM=P.Znajdz(P.S(odp,"id"));
  Check(odpM.OdpNaLogiczne==soloM.Id,"reply records the message id it answers");

  // ---------- pkt 1: wynik FIN jako pole ----------
  Check(P.WynikZTresci("POZYTYWNY – gotowe")=="pozytywny" && P.WynikZTresci("**NEGATYWNY**: test nie przeszedł")=="negatywny" && P.WynikZTresci("WYNIK: NIEWYKONANE\nbrak dostępu")=="niewykonane"
    && P.WynikZTresci("NEGATYWNY — NIEWYKONANE: odmowa")=="niewykonane" && P.WynikZTresci("Odliczam 2 minuty w tle.")=="","result is parsed only from the start of the text");
  Check(P.BezLiniiWyniku("WYNIK: POZYTYWNY\n\nZrobione, testy przechodzą.")=="Zrobione, testy przechodzą.","result line is stripped from a background agent answer");
  var bezWyniku=Blad(()=>Siec.Wyslij(new P.Nowa{Od="codex-p",Do=P.Wlasciciel,Projekt="test",Modul="test",Typ="fin",OdpNa=c1.Id,Tresc="Odliczam 2 minuty w tle, odpowiem później."}));
  Check(bezWyniku!=null && bezWyniku.Contains("wynik") && Obieg.CzekaNaFin(c1.Id,"codex-p"),"FIN without a result is refused and the task stays open");
  Check(Blad(()=>Siec.Wyslij(new P.Nowa{Od="codex-p",Do=P.Wlasciciel,Projekt="test",Modul="test",Typ="fin",Wynik="super",OdpNa=c1.Id,Tresc="x"}))!=null,"unknown result value is refused");
  Check(Blad(()=>Siec.Wyslij(new P.Nowa{Od="codex-p",Do=P.Wlasciciel,Projekt="test",Modul="test",Typ="fin",Wynik="odmowa",OdpNa=c1.Id,Tresc="x"}))!=null,"provider-refusal result can be written only by the stack");
  // FIN z identyfikatorem wiadomości zamiast id własnej kopii
  Siec.Wyslij(new P.Nowa{Od="codex-p",Do=P.Wlasciciel,Projekt="test",Modul="test",Typ="fin",Wynik="pozytywny",OdpNa=c1.Logiczne,Tresc="Zrobione."});
  Check(!Obieg.CzekaNaFin(c1.Id,"codex-p"),"FIN addressed by the shared message id closes the sender's own copy");
  var czesc=Do(P.Wlasciciel).Single(m=>m.EtapObiegu=="fin_czesciowy");
  Check(czesc.Wynik=="pozytywny" && czesc.OdpNaLogiczne==c1.Logiczne,"partial FIN carries the result field and the answered message id");
  Siec.Wyslij(new P.Nowa{Od="claude-p",Do=P.Wlasciciel,Projekt="test",Modul="test",Typ="fin",OdpNa=s1.Id,Tresc="NEGATYWNY – test B nie przechodzi."});
  var wsp=Do(P.Wlasciciel).Single(m=>m.EtapObiegu=="fin_wspolny");
  Check(wsp.Wynik=="negatywny" && wsp.Uwagi.Contains("✓ POZYTYWNY") && wsp.Uwagi.Contains("✗ NEGATYWNY"),"joint FIN is negative when any side is negative; legacy text prefix still accepted");
  Check(File.ReadLines(Path.Combine(P.Root,"dziennik","dziennik.jsonl")).Any(l=>l.Contains("obieg_fin") && l.Contains("\"wynik\":\"negatywny\"")),"result is recorded in the audit log");

  // FIN zapisany przez stos za agenta w tle: wynik może być nieokreślony albo odmowa
  var z2=Zadanie("Drugie");var c2=(Msg)z2["codex"];var s2=(Msg)z2["claude"];
  Obieg.ZAgenta(new P.Nowa{Od="codex-p",Do=P.Wlasciciel,Projekt="test",Modul="test",Typ="fin",ZeStosu=true,OdpNa=c2.Id,Tresc="Odliczam 2 minuty."});
  Obieg.ZAgenta(new P.Nowa{Od="claude-p",Do=P.Wlasciciel,Projekt="test",Modul="test",Typ="fin",ZeStosu=true,Wynik="odmowa",OdpNa=s2.Id,Tresc="NIEWYKONANE: odmowa dostawcy modelu."});
  var wsp2=Do(P.Wlasciciel).Where(m=>m.EtapObiegu=="fin_wspolny").OrderBy(m=>m.Czas).ThenBy(m=>m.Seq).Last();
  Check(wsp2.Wynik=="niewykonane" && P.WynikOpis("odmowa").Contains("odmowa dostawcy") && P.WynikOpis("nieokreslony").Length>0,"stack-written FIN may be undetermined or a provider refusal; joint result is not-done");
  var z3=Zadanie("Trzecie");var c3=(Msg)z3["codex"];var s3=(Msg)z3["claude"];
  Siec.Wyslij(new P.Nowa{Od="codex-p",Do=P.Wlasciciel,Projekt="test",Modul="test",Typ="fin",Wynik="pozytywny",OdpNa=c3.Id,Tresc="ok"});
  Siec.Wyslij(new P.Nowa{Od="claude-p",Do=P.Wlasciciel,Projekt="test",Modul="test",Typ="fin",Wynik="pozytywny",OdpNa=s3.Id,Tresc="sprawdzone"});
  Check(Do(P.Wlasciciel).Where(m=>m.EtapObiegu=="fin_wspolny").OrderBy(m=>m.Czas).ThenBy(m=>m.Seq).Last().Wynik=="pozytywny","joint FIN is positive only when every side is positive");
  Check(P.WynikWiadomosci(new Msg{Typ="fin",Uwagi="POZYTYWNY – stary FIN bez pola"})=="pozytywny" && P.WynikWiadomosci(new Msg{Typ="informacja",Uwagi="POZYTYWNY"})=="","old FIN without the field is read from its text; other types have no result");

  // ---------- pkt 3: wątek z własnym identyfikatorem, oryginalny tytuł ----------
  var t1=Siec.Wyslij(new P.Nowa{Od=P.Wlasciciel,Do="codex-p",Projekt="test",Modul="test",Typ="pytanie",Temat="Stan testów – żółć i spacje",Tresc="Pierwsze pytanie.",BezKopii=true});
  var t2=Siec.Wyslij(new P.Nowa{Od=P.Wlasciciel,Do="codex-p",Projekt="test",Modul="test",Typ="pytanie",Temat="Stan testów – żółć i spacje",Tresc="Drugie pytanie, ten sam temat.",BezKopii=true});
  var o2=Siec.Wyslij(new P.Nowa{Od="codex-p",Do=P.Wlasciciel,Projekt="test",Modul="test",Typ="odpowiedz",OdpNa=P.S(t2,"id"),Tresc="Odpowiedź na drugie."});
  var lista=P.Wszystkie(false);P.Powiaz(lista);
  Func<Dictionary<string,object>,Msg> zl=x=>lista.Single(m=>m.Id==P.S(x,"id"));
  Check(zl(t1).Watek!=zl(t2).Watek,"two messages with the same subject are two conversations");
  Check(zl(o2).Watek==zl(t2).Watek && zl(o2).Watek!=zl(t1).Watek,"a reply stays in the conversation of the message it answers");
  Check(zl(t1).Tytul=="Stan testów – żółć i spacje" && P.TytulWiadomosci(zl(t1))=="Stan testów – żółć i spacje" && zl(t1).Temat=="STAN-TESTOW-ZOLC-I-SPACJE","original title is kept next to the slug subject");
  var c1p=lista.Single(m=>m.Id==c1.Id);var s1p=lista.Single(m=>m.Id==s1.Id);
  Check(c1p.Watek==s1p.Watek && lista.Single(m=>m.Id==wsp.Id).Watek==c1p.Watek,"both pair copies and the joint FIN share one conversation");
  var stary1=new Msg{Id="s1",Plik="s1.json",Od="codex-p",Do=P.Wlasciciel,Temat="STARY-TEMAT",Czas=DateTime.Now.AddDays(-30)};
  var stary2=new Msg{Id="s2",Plik="s2.json",Od=P.Wlasciciel,Do="codex-p",Temat="ODP-STARY-TEMAT",Czas=DateTime.Now.AddDays(-29)};
  var stare=new List<Msg>{stary1,stary2};P.Powiaz(stare);
  Check(stary1.Watek==stary2.Watek && P.TytulWiadomosci(stary1)=="STARY-TEMAT","old-format messages are still grouped by subject");

  // ---------- pkt 4: postęp jako osobny typ ----------
  var z4=Zadanie("Czwarte");var c4=(Msg)z4["codex"];var s4=(Msg)z4["claude"];
  int finyPrzed=Do(P.Wlasciciel).Count(m=>m.Typ=="fin");
  Siec.Wyslij(new P.Nowa{Od="codex-p",Do=P.Wlasciciel,Projekt="test",Modul="test",Typ="postep",OdpNa=c4.Id,Tresc="piszę testy",Krok=2,Krokow=5});
  DateTime kiedy;
  Check(Obieg.EtapZgloszony(c4.Id,"codex-p",out kiedy)=="krok 2/5: piszę testy" && Obieg.CzekaNaFin(c4.Id,"codex-p") && Do(P.Wlasciciel).Count(m=>m.Typ=="fin")==finyPrzed,"progress message stores the step and never closes the task");
  Check(Blad(()=>Siec.Wyslij(new P.Nowa{Od="codex-p",Do=P.Wlasciciel,Projekt="test",Modul="test",Typ="postep",OdpNa=P.S(t1,"id"),Tresc="x"}))!=null,"progress outside an operator task is refused");
  Check(!P.Wszystkie(false).Any(m=>m.Typ=="postep"),"progress is state of the task, not a stored message");

  // ---------- pkt 5: możliwości stosu ----------
  var mz=P.Mozliwosci("ecxpd test");
  Check(P.S(mz,"wersja_protokolu")=="1.1" && P.L(mz,"polecenia").Contains("przywroc") && P.L(mz,"polecenia").Contains("zapytaj_o_etap") && P.L(mz,"typy").Contains("postep") && P.L(mz,"wyniki_fin").Count==3,"stack advertises protocol version, commands, types and FIN results");
  if(File.Exists("Ecxpd.cs")){
   var wKodzie=System.Text.RegularExpressions.Regex.Matches(File.ReadAllText("Ecxpd.cs"),"case \"([a-z_]+)\":").Cast<System.Text.RegularExpressions.Match>().Select(x=>x.Groups[1].Value).Distinct().OrderBy(x=>x).ToList();
   var brak=wKodzie.Except(P.PoleceniaStosu).ToList();var nadmiar=P.PoleceniaStosu.Except(wKodzie).ToList();
   Check(brak.Count==0 && nadmiar.Count==0,"advertised command list matches the stack source ("+string.Join(",",brak)+" / "+string.Join(",",nadmiar)+")");
  }

  // ---------- pkt 6: archiwum, kosz, przywracanie ----------
  var doArch=P.Znajdz(P.S(o2,"id"));
  P.Wycofaj(new List<string>{doArch.Plik},"test");
  Check(!P.Wszystkie(false).Any(m=>m.Id==doArch.Id) && P.Wszystkie(true).Any(m=>m.Id==doArch.Id && m.Katalog=="archiwum"),"closed message moves to the archive");
  var rp=P.Przywroc(new List<string>{doArch.Plik});
  Check(P.S(rp,"przywrocone")=="1" && P.Wszystkie(false).Any(m=>m.Id==doArch.Id && m.Katalog=="przeczytane"),"archived message can be restored to current messages");
  P.Wycofaj(new List<string>{doArch.Plik},"test",P.Kosz);
  Check(P.ZKosza().Any(m=>m.Id==doArch.Id && m.Katalog==P.Kosz) && !P.Wszystkie(true).Any(m=>m.Id==doArch.Id),"deleted message is listed in the trash only");
  P.Przywroc(new List<string>{doArch.Plik});
  Check(P.Wszystkie(false).Any(m=>m.Id==doArch.Id) && P.ZKosza().Count==0 && P.S(P.Przywroc(new List<string>{"nie-ma.json"}),"brak")=="1","deleted message can be restored; unknown file is reported");
  Check(File.ReadLines(Path.Combine(P.Root,"dziennik","dziennik.jsonl")).Count(l=>l.Contains("\"przywrocona\""))==2,"restores are recorded in the audit log");

  // ---------- pkt 7: obecność adresata z rodzajem ----------
  Siec.Przypisz("claude-nowa","test","inny","sesja-x",new List<string>(),"","claude-nowa");
  File.Delete(Path.Combine(P.Root,"stan","wezly","claude-nowa.json"));
  var ob1=Siec.Obecnosci("claude-nowa").Single();
  Check(P.S(ob1,"rodzaj")=="sesja" && !P.B(ob1,"dostepny") && P.S(ob1,"opis").Contains("nigdy"),"app session that never spoke is reported unavailable");
  P.Sygnal("claude-nowa","odbierz");
  Check(P.B(Siec.Obecnosci("claude-nowa").Single(),"dostepny"),"app session with a recent signal is available");
  Directory.CreateDirectory(Path.Combine(P.Root,"stan","agenci"));
  File.WriteAllText(Path.Combine(P.Root,"stan","agenci","codex-tlo.json"),P.Js().Serialize(P.D("nazwa","codex-tlo","strona","codex","aktywny",true,"katalog",P.Root)));
  var ob2=Siec.Obecnosci("codex-tlo").Single();
  Check(P.S(ob2,"rodzaj")=="tlo" && P.B(ob2,"dostepny") && P.S(ob2,"opis").Contains("agent w tle"),"background agent without any signal is available while its service runs");
  File.WriteAllText(Path.Combine(P.Root,"stan","uslugi.json"),P.Js().Serialize(P.D("codex_zatrzymana",true)));
  Check(!P.B(Siec.Obecnosci("codex-tlo").Single(),"dostepny"),"background agent is unavailable when its service is stopped");
  Console.WriteLine("RESULT: "+failures+" failed");
  return failures==0?0:1;
 }
}}
