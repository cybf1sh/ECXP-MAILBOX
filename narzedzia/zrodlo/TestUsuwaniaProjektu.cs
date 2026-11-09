using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
namespace Ecxp {
class TestUsuwaniaProjektu {
 static int errors;static string state,other;
 static void Check(bool ok,string name){Console.WriteLine((ok?"PASS ":"FAIL ")+name);if(!ok)errors++;}
 static void Reject(Action a,string name){try{a();Check(false,name);}catch(EcxpException){Check(true,name);}}
 static void Setup(string root,string fixture){
  P.Root=Path.GetFullPath(root);foreach(var d in P.Foldery)Directory.CreateDirectory(Path.Combine(P.Root,d));Directory.CreateDirectory(Path.Combine(P.Root,"narzedzia"));
  var roles=new List<string>{"tester"};foreach(var p in new[]{"test-kasuj","test-zostaw"}){Siec.NoweZadanie(p,p,"test","syntetyczny",roles,roles,"ECXP",null,false,null,null,Siec.Ikony[1],false,false);File.WriteAllText(Path.Combine(P.Root,"projekty",p,"dane.txt"),p);Siec.Wyslij(new P.Nowa{Od=P.Wlasciciel,Do="para:"+p,Projekt=p,Modul=p,Typ="pytanie",Temat="TEST",Tresc="Dane tylko "+p});}
  var target=UsuwanieProjektu.Katalog("test-kasuj");other=UsuwanieProjektu.Katalog("test-zostaw");
  Pliki.Zapisz(Path.Combine(P.Root,"stan","znaczniki.json"),P.D("znacznik","ECXP","sesje",new[]{P.D("projekt","test-kasuj","nazwa","codex-test-kasuj"),P.D("projekt","test-zostaw","nazwa","codex-test-zostaw")}));
  var home=Path.Combine(P.Root,"profile","codex");var claude=Path.Combine(P.Root,"profile","claude");var cdir=Path.Combine(claude,"projects","target");Directory.CreateDirectory(home);Directory.CreateDirectory(cdir);Directory.CreateDirectory(Path.Combine(claude,"projects","other"));
  var sid=Guid.NewGuid().ToString("D");File.WriteAllText(Path.Combine(cdir,sid+".jsonl"),P.Js().Serialize(P.D("cwd",target)));Directory.CreateDirectory(Path.Combine(cdir,"memory"));File.WriteAllText(Path.Combine(cdir,"memory","MEMORY.md"),"Project memory");
  var tmp=Path.Combine(P.Root,"temp-claude");Directory.CreateDirectory(Path.Combine(tmp,"hash",sid));File.WriteAllText(Path.Combine(tmp,"hash",sid,"paste.txt"),"Project scratch");Directory.CreateDirectory(Path.Combine(tmp,"hash","keep"));
  Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR",claude);Environment.SetEnvironmentVariable("CLAUDE_CODE_TMPDIR",tmp);
  var rootId=Guid.NewGuid().ToString("D");var childId=Guid.NewGuid().ToString("D");var otherId=Guid.NewGuid().ToString("D");
  state=Path.Combine(P.Root,"fixture-state.json");Pliki.Zapisz(state,P.D("project",target,"codex_home",home,"claude_dir",cdir,"threads",new object[]{P.D("id",rootId,"cwd",target),P.D("id",childId,"cwd",target,"parent",rootId,"archived",true),P.D("id",otherId,"cwd",other)}));
  Pliki.Zapisz(Path.Combine(home,".codex-global-state.json"),P.D("local-projects",P.D("target",P.D("id","legacy-target","rootPaths",new[]{target}),"other",P.D("rootPaths",new[]{other})),"app-server-project-id-by-legacy-project-id-by-host",P.D("local:fixture",P.D("legacy-target","app-target","other","app-other")),"electron-saved-workspace-roots",new[]{target,other},"unrelated","preserve"));
  var mock=Path.Combine(P.Root,"mock");Directory.CreateDirectory(mock);foreach(var side in new[]{"claude","codex"})File.WriteAllText(Path.Combine(mock,side+".cmd"),"@echo off\r\n\"C:\\Python314\\python.exe\" \""+fixture+"\" "+side+" \""+state+"\" %*\r\n");
  Environment.SetEnvironmentVariable("PATH",mock+";"+Environment.GetEnvironmentVariable("PATH"));
 }
 public static int Main(string[] args){if(args.Length!=2||Directory.Exists(args[0]))return 2;var old=Environment.GetEnvironmentVariable("PATH");var oldClaude=Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");var oldTmp=Environment.GetEnvironmentVariable("CLAUDE_CODE_TMPDIR");
  try{
   Setup(args[0],Path.GetFullPath(args[1]));
   Reject(()=>UsuwanieProjektu.Katalog("../escape"),"odrzucono wyjście poza katalog projektu");
   Reject(()=>UsuwanieProjektu.BezLinkow(other,UsuwanieProjektu.Katalog("test-kasuj")),"podobna nazwa sąsiada nie jest podkatalogiem celu");
   var junction=Path.Combine(P.Root,"junction-test");Directory.CreateDirectory(Path.Combine(other,"sub"));File.WriteAllText(Path.Combine(other,"sub","canary.txt"),"keep");
   using(var mk=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"cmd.exe"),"/d /c mklink /J \""+junction+"\" \""+other+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true})){mk.WaitForExit();if(mk.ExitCode!=0)throw new Exception("Nie utworzono syntetycznej junction.");}
   try{Reject(()=>UsuwanieProjektu.BezLinkow(Path.Combine(junction,"sub","canary.txt"),Path.Combine(junction,"sub")),"junction nad korzeniem zakresu jest także odrzucana");Check(File.ReadAllText(Path.Combine(other,"sub","canary.txt"))=="keep","odrzucenie junction zachowuje jej cel");}finally{Directory.Delete(junction);}
   var basic=UsuwanieProjektu.Przygotuj("test-kasuj",false,false);Check(P.L(basic,"watki").Count==0&&P.S(basic,"claude_plan")=="","bez checkboxów nie planuje się usuwania danych dostawców");
   Check(Directory.Exists(UsuwanieProjektu.Katalog("test-kasuj")),"samo przygotowanie planu niczego nie usuwa");
   var full=UsuwanieProjektu.Przygotuj("test-kasuj",true,true);Check(P.L(full,"watki").Count==2,"plan obejmuje aktywny i archiwalny wątek celu");Check(P.L(full,"claude_scratch").Count==1,"plan obejmuje tylko scratchpad sesji celu");
   var data=Pliki.Czytaj(state);var threads=P.Lista(data,"threads");var child=threads.Single(t=>P.S(t,"parent").Length>0);var original=P.S(child,"cwd");child["cwd"]=other;data["threads"]=threads;Pliki.Zapisz(state,data);
   Reject(()=>UsuwanieProjektu.Plan("test-kasuj",true,false),"wątek potomny innego projektu blokuje cały plan");child["cwd"]=original;Pliki.Zapisz(state,data);
   var beforeEpoch=UsuwanieProjektu.Epoka("test-kasuj");
   var token=P.S(full,"token");var claimed=UsuwanieProjektu.OdbierzBilet(token);Check(UsuwanieProjektu.Trwa("test-kasuj"),"odebrany bilet blokuje nowe prace projektu");Reject(()=>UsuwanieProjektu.OdbierzBilet(token),"bilet jest jednorazowy");
   foreach(var op in new[]{"uruchom_agenta","przekaz","lancuch","lancuch_wznow","para_wznow","rola","decyzja","zadanie"})Reject(()=>UsuwanieProjektu.SprawdzOperacje(op,"test-kasuj",UsuwanieProjektu.Epoka("test-kasuj")),"blokada obejmuje operację "+op);
   Reject(()=>Siec.Wyslij(new P.Nowa{Od=P.Wlasciciel,Do="para:test-kasuj",Projekt="test-kasuj",Modul="test-kasuj",Typ="zadanie",Temat="test",Tresc="syntetyczny"}),"wewnętrzna wysyłka także respektuje blokadę");
   var result=UsuwanieProjektu.Wykonaj(claimed);Check(P.B(result,"sukces"),"pełne usunięcie zakończyło się sukcesem: "+P.S(result,"blad"));
   data=Pliki.Czytaj(state);Check(P.L(data,"deleted").Count==2&&P.Lista(data,"threads").Count==1&&P.S(P.Lista(data,"threads")[0],"cwd")==other,"Codex: usunięto właściwe wątki, zachowano sąsiada");
   var global=Pliki.Czytaj(Path.Combine(P.S(full,"codex_home"),".codex-global-state.json"));Check(!((Dictionary<string,object>)global["local-projects"]).ContainsKey("target")&&((Dictionary<string,object>)global["local-projects"]).ContainsKey("other")&&P.S(global,"unrelated")=="preserve","Codex: usunięto tylko lokalny wpis celu");
   var mapping=(Dictionary<string,object>)((Dictionary<string,object>)global["app-server-project-id-by-legacy-project-id-by-host"])["local:fixture"];Check(!mapping.ContainsKey("legacy-target")&&P.S(mapping,"other")=="app-other","Codex: usunięto także odmienny identyfikator legacy, zachowano inne mapowania");
   Check(!Directory.Exists(P.S(full,"katalog"))&&Directory.Exists(other)&&File.ReadAllText(Path.Combine(other,"dane.txt"))=="test-zostaw","Mailbox: folder celu usunięty, inne dane nienaruszone");
   Reject(()=>UsuwanieProjektu.SprawdzOperacje("nowy_pentest","test-kasuj",beforeEpoch),"preflight rozpoczęty przed usunięciem nie odtwarza projektu po zdjęciu blokady");
   UsuwanieProjektu.SprawdzOperacje("nowe_zadanie","test-kasuj",UsuwanieProjektu.Epoka("test-kasuj"));Check(true,"nowa jawna operacja po zakończeniu może ponownie użyć nazwy projektu");
   Check(!P.Wszystkie(true).Any(m=>m.Projekt=="test-kasuj")&&P.Wszystkie(true).Count(m=>m.Projekt=="test-zostaw")==2,"wiadomości celu usunięte, wiadomości sąsiada zachowane");
   var labels=P.Lista(Pliki.Czytaj(Path.Combine(P.Root,"stan","znaczniki.json")),"sesje");Check(labels.Count==1&&P.S(labels[0],"projekt")=="test-zostaw","znaczniki usuniętych sesji znikają bez naruszania innych projektów");
   Check(P.L(full,"claude_scratch").All(d=>!Directory.Exists(d))&&!Directory.Exists(P.S(data,"claude_dir")),"Claude: usunięto sesje, pamięć i scratchpad celu");
   Check(Directory.Exists(Path.Combine(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR"),"projects","other"))&&Directory.Exists(Path.Combine(Environment.GetEnvironmentVariable("CLAUDE_CODE_TMPDIR"),"hash","keep")),"Claude: stan innych projektów i scratchpady zachowane");
   string integrity;P.Stany(out integrity);Check(integrity.StartsWith("spójny")&&P.Straz().Count==0,"dziennik jest spójny, brak alarmów o świadomie usuniętych wiadomościach");Check(!UsuwanieProjektu.Trwa("test-kasuj"),"po zakończeniu zdjęto blokadę projektu");
   var psi=UsuwanieProjektu.Pomocnik("ECXP-MAILBOX.exe",Guid.NewGuid().ToString("N"),true);Check(psi.UseShellExecute&&psi.Verb=="runas","checkbox UAC wybiera systemowe uruchomienie runas");
   psi=UsuwanieProjektu.Pomocnik("ECXP-MAILBOX.exe",Guid.NewGuid().ToString("N"),false);Check(!psi.UseShellExecute&&psi.CreateNoWindow,"bez checkboxa UAC nie ma żądania podniesienia praw");
   Setup(args[0]+"-basic",Path.GetFullPath(args[1]));var before=P.Czytaj(state);basic=UsuwanieProjektu.Przygotuj("test-kasuj",false,false);result=UsuwanieProjektu.Wykonaj(UsuwanieProjektu.OdbierzBilet(P.S(basic,"token")));
   Check(P.B(result,"sukces")&&P.Czytaj(state)==before,"odznaczone checkboxy dostawców pozostawiają ich dane bez zmian");
   Setup(args[0]+"-partial",Path.GetFullPath(args[1]));data=Pliki.Czytaj(state);data["fail_claude"]=true;Pliki.Zapisz(state,data);full=UsuwanieProjektu.Przygotuj("test-kasuj",true,true);result=UsuwanieProjektu.Wykonaj(UsuwanieProjektu.OdbierzBilet(P.S(full,"token")));
   Check(!P.B(result,"sukces")&&P.S(result,"blad").Contains("Synthetic purge failure")&&P.L(result,"wykonano").Any(x=>x.StartsWith("Codex")),"częściowy błąd dostawcy jest jawny i raportuje wykonane kroki");
   Check(Directory.Exists(UsuwanieProjektu.Katalog("test-kasuj"))&&Directory.Exists(other)&&P.Wszystkie(true).Any(m=>m.Projekt=="test-kasuj"),"błąd dostawcy zachowuje projekt mailboxa i inne dane");
   Check(!UsuwanieProjektu.Trwa("test-kasuj"),"błąd nie pozostawia blokady operacji");
  }catch(Exception ex){Console.WriteLine(ex);return 1;}
  finally{Cieply.ZamknijWszystko();Environment.SetEnvironmentVariable("PATH",old);Environment.SetEnvironmentVariable("CLAUDE_CONFIG_DIR",oldClaude);Environment.SetEnvironmentVariable("CLAUDE_CODE_TMPDIR",oldTmp);}
  return errors==0?0:1;
 }
}}
