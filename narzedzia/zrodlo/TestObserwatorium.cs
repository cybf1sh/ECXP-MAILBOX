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
  var detailed=M("details","codex-demo","Technika: porównanie kontraktu danych.\nUżyłem pytest, ponieważ odtwarza problem offline.\nPolecenie: pytest -q --maxfail=1\nWynik: 17/17 PASS\nSemgrep nie działa.\nProponuję Playwright jako alternatywę.\nRaport: proof.txt\n```text\nprzykładowy fragment, nie komenda\n```");
  var fields=AnalizaObserwatorium.Szczegoly(detailed).Pola;
  Check(fields.Any(x=>x.Pole=="Technika"&&x.Wartosc.Contains("kontraktu")),"custom technique independent of project taxonomy");
  Check(fields.Any(x=>x.Pole=="Parametry / polecenie"&&x.Cytat=="Polecenie: pytest -q --maxfail=1"&&x.Linia==3),"exact command parameters and source line preserved");
  Check(fields.Any(x=>x.Pole=="Powód wyboru / ograniczenie"&&x.Cytat.Contains("ponieważ")),"explicit method reason has source");
  Check(fields.Any(x=>x.Pole=="Narzędzie"&&x.Wartosc=="Semgrep"&&x.Stan.StartsWith("Negacja")),"unavailable tool not classified as successful execution");
  Check(fields.Any(x=>x.Wartosc.Contains("Playwright")&&x.Stan.StartsWith("Plan")),"alternative tool is a plan");
  Check(fields.Any(x=>x.Pole=="Fragment kodu / logu")&&!fields.Any(x=>x.Pole=="Parametry / polecenie"&&x.Cytat.Contains("przykładowy")),"fenced sample is not automatically a command");
  var folder=Path.Combine(args[0],"evidence");Directory.CreateDirectory(folder);
  File.WriteAllText(Path.Combine(folder,"proof.txt"),"Polecenie: pytest -q\nWynik: 3/3 PASS\n<script>notExecutable()</script>");
  var proofs=AnalizaObserwatorium.Dowody(folder,new[]{"proof.txt","missing.log"});
  Check(proofs[0].Tekst.Contains("3/3")&&proofs[0].Sha.Length==64,"referenced text file read with content hash");
  Check(proofs[1].Tekst==null&&proofs[1].Stan.Contains("Nie znaleziono"),"missing artifact explicitly reported");
  var richer=AnalizaObserwatorium.Raport(new[]{detailed},"demo",null,folder);
  Check(richer.Contains("id='techniki'")&&richer.Contains("Plik proof.txt")&&richer.Contains("Plik źródłowy"),"file extraction linked to original artifact");
  Check(richer.Contains("&lt;script&gt;notExecutable")&&!richer.Contains("<script>notExecutable"),"artifact content is escaped");
  Check(!AnalizaObserwatorium.Raport(new[]{detailed},null,null,folder).Contains("3/3 PASS"),"all-project report does not attach a single project evidence directory");
  Directory.CreateDirectory(Path.Combine(folder,"duplicate"));File.Copy(Path.Combine(folder,"proof.txt"),Path.Combine(folder,"duplicate","proof.txt"));
  Check(AnalizaObserwatorium.Dowody(folder,new[]{"proof.txt"})[0].Tekst==null,"ambiguous filenames never silently select another artifact");
  var other=M("other","codex-other","Metoda: walidacja CSV.\nNarzędzie: moj-walidator\nParametry: separator=;\nWynik: 4/4 PASS","","future-project");
  Check(AnalizaObserwatorium.Raport(new[]{other,detailed},"future-project",null).Contains("moj-walidator")&&!AnalizaObserwatorium.Raport(new[]{other,detailed},"future-project",null).Contains("proof.txt"),"future project and custom tools work with project isolation");
  detailed.EtapObiegu="fin_wspolny";
  Check(AnalizaObserwatorium.Szczegoly(detailed).Pola.Where(x=>x.Pole=="Wynik zapisany").All(x=>x.Stan.StartsWith("Relacja")),"joint FIN is not attributed as sender execution");
  var mailbox=Path.Combine(args[0],"mailbox");var profileRoot=Path.Combine(args[0],"profile");Directory.CreateDirectory(Path.Combine(mailbox,"stan","agenci"));Directory.CreateDirectory(Path.Combine(profileRoot,"scripts","nested"));
  File.WriteAllText(Path.Combine(profileRoot,"AGENTS.md"),"Konfiguracja: testy offline. <script>doNotRun()</script>");
  File.WriteAllText(Path.Combine(profileRoot,"scripts","sample.py"),"import argparse\nparser.add_argument('--timeout', default=5)\nif True:\n    raise RuntimeError('not executed')\n");
  File.WriteAllText(Path.Combine(profileRoot,"scripts","nested","sample.py"),"def nested():\n    return 3\n");
  File.WriteAllText(Path.Combine(mailbox,"stan","agenci","codex-demo.json"),P.Js().Serialize(P.D("nazwa","codex-demo","projekt","demo","model","test-model","katalog",profileRoot,"api_key","DO_NOT_EXPORT_SECRET")));
  File.WriteAllText(Path.Combine(mailbox,"stan","agenci","other.json"),P.Js().Serialize(P.D("nazwa","codex-other","projekt","unrelated","katalog",profileRoot)));
  var profileFiles=AnalizaObserwatorium.ProfileZrodla(new List<Msg>{detailed},"demo",null,mailbox,null);
  Check(profileFiles.Count(x=>x.Rodzaj=="Kod scripts"&&x.Tekst!=null)==2,"nested scripts with identical basenames kept distinct");
  Check(profileFiles.All(x=>x.Projekt=="demo")&&profileFiles.Any(x=>x.Tekst!=null&&x.Tekst.Contains("test-model")),"agent settings scoped by project");
  Check(profileFiles.All(x=>x.Tekst==null||!x.Tekst.Contains("DO_NOT_EXPORT_SECRET")),"unselected runtime secrets omitted");
  var profilesHtml=AnalizaObserwatorium.Raport(new[]{detailed},"demo",null,null,mailbox);
  Check(profilesHtml.Contains("Opcje i wartości w kodzie")&&profilesHtml.Contains("--timeout")&&profilesHtml.Contains("&lt;script&gt;doNotRun"),"static code options and instruction text rendered safely");
  var catalog=new[]{new AgentKatalogu{Tozsamosc="codex-profile",Folder=profileRoot}};
  Check(AnalizaObserwatorium.ProfileZrodla(new List<Msg>{detailed},"demo",null,null,catalog).Count==0,"unmentioned catalog profile not assigned to project");
  var mention=M("mention","codex-demo","Sprawdziłem profil codex-profile.");
  Check(AnalizaObserwatorium.ProfileZrodla(new List<Msg>{mention},"demo",null,null,catalog).Any(x=>x.Powiazanie.Contains("wzmianka nie dowodzi")),"mentioned identity links profile without claiming execution");
  if(args.Length>2){
   var actual=new List<Msg>();foreach(var dir in new[]{"przeczytane","archiwum","Odebrane","Wysłane","do-wlasciciela","do-codex","do-claude"}){var location=Path.Combine(args[1],dir);if(Directory.Exists(location))foreach(var path in Directory.GetFiles(location,"*.json",SearchOption.AllDirectories))actual.Add(P.Wczytaj(new FileInfo(path),dir,P.Js()));}
   var actualScope=AnalizaObserwatorium.Zakres(actual,args[2],null);
   var observations=actualScope.Select(m=>AnalizaObserwatorium.Szczegoly(m)).ToList();
   var actualCatalog=args.Length>4?KatalogAgentow.Znajdz(args[4]):new List<AgentKatalogu>();
   File.WriteAllText(Path.Combine(args[0],"projekt-techniki.html"),AnalizaObserwatorium.Raport(actual,args[2],null,args.Length>3?args[3]:null,args[1],actualCatalog));
   File.WriteAllText(Path.Combine(args[0],"wszystkie-techniki.html"),AnalizaObserwatorium.Raport(actual,null,null,null,args[1],actualCatalog));
   var actualProfiles=AnalizaObserwatorium.ProfileZrodla(actualScope,args[2],null,args[1],actualCatalog);
   Console.WriteLine("REAL profileSources="+actualProfiles.Count+" scripts="+actualProfiles.Count(x=>x.Rodzaj=="Kod scripts"&&x.Tekst!=null));
   Console.WriteLine("REAL messages="+actualScope.Count+" fields="+observations.Sum(x=>x.Pola.Count)+" fileReferences="+observations.SelectMany(x=>x.Odwolania).Distinct().Count());
   foreach(var g in observations.SelectMany(x=>x.Pola).GroupBy(x=>x.Pole))Console.WriteLine("REAL "+g.Key+"="+g.Count());
  }
  return bad==0?0:1;
 }
}
}

