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
  Console.WriteLine("RESULT: "+failures+" failed");
  return failures==0?0:1;
 }
}}
