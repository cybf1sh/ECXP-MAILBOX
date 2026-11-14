using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
namespace Ecxp {
class TestObserwatorium {
 static int bad;
 static void Check(bool v,string n){Console.WriteLine((v?"PASS ":"FAIL ")+n);if(!v)bad++;}
 static Msg M(string id,string od,string tekst,string parent="",string projekt="demo") {return new Msg{Id=id,Od=od,Do=od=="wlasciciel"?"codex-demo":"wlasciciel",Projekt=projekt,Uwagi=tekst,OdpNa=parent,Czas=DateTime.Now,Typ="informacja",Tytul=id};}
 public static int Main(string[] args){
  Directory.CreateDirectory(args[0]);
  var task=M("t","wlasciciel","Sprawdź usterkę. <script>alert('x')</script>");task.Typ="zadanie";task.Logiczne="lt";
  var mirror=task.Klon();mirror.Id="mirror";mirror.KopiaDla="claude-demo";
  var refusal=M("r","codex-demo","Nie mogę wykonać zadania, ponieważ brak uprawnień.","mirror");
  var alternative=M("a","codex-demo","Proponuję zamiast tego testy jednostkowe. Wybrałem pytest, ponieważ pozwala odtworzyć problem.","r");
  alternative.Czesci.Add(P.D("rodzaj","tekst","tekst","Metoda: kontrola kontraktu. Użyłem danych syntetycznych."));
  var fin=M("f","codex-demo","Kontrole zakończone.","a");fin.Typ="fin";fin.Wynik="pozytywny";fin.Kontrole.Add("[PASS] dane syntetyczne");
  var unrelated=M("z","claude-inny","Nie mogę pomóc.","","inny");
  var question=M("q","wlasciciel","Dlaczego odmawiam?", "t");
  var quoted=M("quoted","codex-demo","> Nie mogę wykonać zadania.\nCzy wybrałem pytest?","t");
  var orphan=M("orphan","codex-demo","Brak danych.","missing");
  var list=new[]{task,mirror,refusal,alternative,fin,unrelated,question,quoted,orphan};
  var scope=AnalizaObserwatorium.Zakres(list,"demo","codex-demo");
  Check(scope.Count==7,"logical mirrors deduplicated and project isolated");
  Check(scope.First(x=>x.Id=="r").OdpNa=="t"&&refusal.OdpNa=="mirror","mirror reply resolves without mutating mailbox objects");
  var signals=AnalizaObserwatorium.Analizuj(scope,"codex-demo");
  Check(signals.Count(s=>s.Rodzaj=="Sygnał odmowy")==1,"refusal detected; operator question and block quote excluded");
  Check(signals.Any(s=>s.Cytat.Contains("ponieważ")),"explicit reason retained verbatim");
  Check(signals.Any(s=>s.Rodzaj=="Proponowana alternatywa"),"recorded alternative detected");
  Check(signals.Any(s=>s.Metody.Contains("Testy automatyczne")),"method extracted with evidence");
  Check(signals.Any(s=>s.Metody.Contains("Deklarowana: kontrola kontraktu")),"custom method in context extracted");
  var html=AnalizaObserwatorium.Raport(list,"demo","codex-demo");File.WriteAllText(Path.Combine(args[0],"syntetyczny.html"),html);
  Check(!html.Contains("<script>alert")&&html.Contains("&lt;script&gt;"),"source HTML escaped, never executed");
  Check(!html.Contains("claude-inny"),"other project absent from report");
  Check(html.Contains("FIN pozytywne: 1")&&html.Contains("Brak rozstrzygnięcia"),"method comparisons include observed outcome and unknown category");
  Check(html.Contains("hipoteza kolejnego kroku")&&html.Contains("Propozycja regułowa, niesprawdzona"),"alternative distinguished from observed action");
  Check(html.Contains("[PASS] dane syntetyczne")&&html.Contains("kontrola kontraktu"),"full controls and message context retained");
  var ids=Regex.Matches(html,"id='([^']+)'").Cast<Match>().Select(m=>m.Groups[1].Value).ToList();
  Check(Regex.Matches(html,"href='#([^']+)'").Cast<Match>().All(m=>ids.Contains(m.Groups[1].Value)),"every internal source and navigation link resolves");
  Check(ids.Count==ids.Distinct().Count(),"source anchors unique");
  Check(AnalizaObserwatorium.Raport(new Msg[0],"empty",null).Contains("Wiadomości: 0"),"empty project rendered without invented findings");
  var blank1=M("b1","codex-demo","one");var blank2=M("b2","claude-other","two");blank2.Do="wlasciciel";
  Check(AnalizaObserwatorium.Zakres(new[]{blank1,blank2},"demo","codex-demo").Count==1,"empty thread IDs do not merge unrelated agents");
  return bad==0?0:1;
 }
}
}

