using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Diagnostics;
namespace Ecxp {
static class UsuwanieProjektu {
 static readonly Dictionary<string,Dictionary<string,object>> bilety=new Dictionary<string,Dictionary<string,object>>();
 static string Dir {get{return Path.Combine(P.Root,"stan","usuwanie-projektow");}}
 public static bool Trwa(string projekt){projekt=P.Slug(projekt??"",32,false);return projekt.Length>0&&File.Exists(Path.Combine(Dir,projekt+".blokada.json"));}
 public static string Epoka(string projekt){projekt=P.Slug(projekt??"",32,false);var file=Path.Combine(Dir,projekt+".epoka.json");return projekt.Length>0&&File.Exists(file)?P.S(Pliki.Czytaj(file),"token"):"";}
 public static void SprawdzOperacje(string op,string projekt,string epoka){
  if(new[]{"ping","stan","watek","trasy","kontekst_pobierz","sprawdz_decyzje","lancuchy","galezie","agenci","sesja","uslugi","straz","projekt_usun_bilet","zatrzymaj_agenta"}.Contains(op))return;
  if(projekt.Length>0){if(Trwa(projekt)||Epoka(projekt)!=epoka)throw new EcxpException("Projekt jest usuwany lub zmienił się od rozpoczęcia operacji. Zleć pracę ponownie po zakończeniu usuwania.");}
  else if(Directory.Exists(Dir)&&Directory.GetFiles(Dir,"*.blokada.json").Length>0)throw new EcxpException("Operacja bez jednoznacznego projektu jest wstrzymana na czas usuwania.");
 }
 public static bool Wewnatrz(string path,string parent){return Path.GetFullPath(path).StartsWith(Path.GetFullPath(parent).TrimEnd('\\','/')+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}
 public static void BezLinkow(string path,string parent){
  if(!Wewnatrz(path,parent))throw new EcxpException("Ścieżka poza zakresem usuwania: "+path);
  var p=Path.GetFullPath(path);
  while(p!=null){if((File.Exists(p)||Directory.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new EcxpException("Nie usuwam dowiązania/junction: "+p);p=Path.GetDirectoryName(p);}
 }
 static void SprawdzDrzewo(string path,string parent){BezLinkow(path,parent);if(Directory.Exists(path))foreach(var child in Directory.GetFileSystemEntries(path))SprawdzDrzewo(child,parent);}
 static void UsunPlik(string path,string parent){BezLinkow(path,parent);if(File.Exists(path)){File.SetAttributes(path,File.GetAttributes(path)&~FileAttributes.ReadOnly);File.Delete(path);}}
 static void UsunDrzewo(string path,string parent){BezLinkow(path,parent);if(!Directory.Exists(path))return;foreach(var child in Directory.GetFileSystemEntries(path)){if(Directory.Exists(child))UsunDrzewo(child,parent);else UsunPlik(child,parent);}Directory.Delete(path);}
 public static string Katalog(string projekt){if(!Regex.IsMatch(projekt??"",@"^[a-z0-9][a-z0-9-]{0,31}$"))throw new EcxpException("Wybierz konkretny projekt.");var p=Path.Combine(P.Root,"projekty",projekt);BezLinkow(p,Path.Combine(P.Root,"projekty"));return p;}
 static List<string> Watki(string path){return Cieply.KonserwacjaCodex(rpc=>{
  var found=new Dictionary<string,string>();var edges=new Dictionary<string,HashSet<string>>();var parents=new Queue<string>();var scanned=new HashSet<string>();parents.Enqueue("");
  while(parents.Count>0){var parent=parents.Dequeue();if(!scanned.Add(parent))continue;
   foreach(bool archived in new[]{false,true}){string cursor="";var cursors=new HashSet<string>();do{
    var a=P.D("limit",100,"archived",archived,"sourceKinds",new[]{"cli","vscode","exec","appServer","subAgent","subAgentReview","subAgentCompact","subAgentThreadSpawn","subAgentOther","unknown"},"modelProviders",new string[0]);
    if(parent.Length==0)a["cwd"]=path;else a["parentThreadId"]=parent;if(cursor.Length>0)a["cursor"]=cursor;
    var r=rpc("thread/list",a,30000);foreach(var thread in P.Lista(r,"data")){
     var id=P.S(thread,"id");Guid parsed;if(!Guid.TryParse(id,out parsed))throw new EcxpException("Niepoprawne ID wątku Codex.");
     var cwd=P.S(thread,"cwd");if(cwd.Length==0||(!string.Equals(Path.GetFullPath(cwd),Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase)&&!Wewnatrz(cwd,path)))throw new EcxpException("Wątek potomny Codexa należy do innego katalogu; przerwano plan.");
     if(!found.ContainsKey(id)){found[id]=cwd;parents.Enqueue(id);edges[id]=new HashSet<string>();}if(parent.Length>0)edges[parent].Add(id);if(found.Count>10000)throw new EcxpException("Zbyt wiele wątków w pojedynczej operacji.");
    }cursor=P.S(r,"nextCursor");if(cursor.Length>0&&!cursors.Add(cursor))throw new EcxpException("Powtórzona strona listy wątków.");
   }while(cursor.Length>0);}
  }var ordered=new List<string>();var visiting=new HashSet<string>();var done=new HashSet<string>();Action<string,int> visit=null;visit=(id,depth)=>{if(done.Contains(id))return;if(depth>128||!visiting.Add(id))throw new EcxpException("Nieprawidłowe drzewo wątków Codexa.");foreach(var child in edges[id].OrderBy(x=>x))visit(child,depth+1);visiting.Remove(id);done.Add(id);ordered.Add(id);};foreach(var id in found.Keys.OrderBy(x=>x))visit(id,0);return ordered;
 });}
 static string Run(string program,string arguments,string cwd,bool emptyAllowed=false){
  if(program.Length==0)throw new EcxpException("Nie znaleziono CLI dostawcy.");
  var psi=program.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)?new ProcessStartInfo(program,arguments):new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"cmd.exe"),"/d /s /c \"\""+program+"\" "+arguments+"\"");
  psi.WorkingDirectory=cwd;psi.UseShellExecute=false;psi.CreateNoWindow=true;psi.RedirectStandardOutput=psi.RedirectStandardError=true;psi.EnvironmentVariables["ECXP_DORECZANIE"]="1";
  using(var p=Process.Start(psi)){var output=new StringBuilder();var gate=new object();DataReceivedEventHandler h=(s,e)=>{if(e.Data!=null)lock(gate){if(output.Length<131072)output.AppendLine(e.Data);}};p.OutputDataReceived+=h;p.ErrorDataReceived+=h;p.BeginOutputReadLine();p.BeginErrorReadLine();if(!p.WaitForExit(120000)){using(var stop=Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"taskkill.exe"),"/PID "+p.Id+" /T /F"){UseShellExecute=false,CreateNoWindow=true})){stop.WaitForExit(10000);}throw new EcxpException("CLI nie zakończyło operacji w czasie.");}p.WaitForExit();var text=output.ToString();if(p.ExitCode!=0&&!(emptyAllowed&&p.ExitCode==1&&text.Contains("No Claude Code project state found for "+cwd)))throw new EcxpException("CLI zakończyło operację błędem: "+text);return text;}
 }
 static string ClaudePrefix(){var p=Agenci.Program("claude");string h="";try{h=Run(p,"project purge --help",P.Root);}catch(EcxpException){}if(h.Contains("--dry-run")&&h.Contains("--yes"))return "project purge";h=Run(p,"purge --help",P.Root);if(h.Contains("--dry-run")&&h.Contains("--yes"))return "purge";throw new EcxpException("Claude CLI nie obsługuje usuwania stanu projektu. Zaktualizuj CLI.");}
 static string ClaudePlan(string path,string prefix){var text=Run(Agenci.Program("claude"),prefix+" \""+path+"\" --dry-run",path,true);if(!text.Contains(path))throw new EcxpException("Claude nie potwierdził dokładnego katalogu projektu.");return text;}
 static string ClaudeHome(){var h=Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");return Path.GetFullPath(string.IsNullOrEmpty(h)?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),".claude"):h);}
 static string ClaudeTemp(){var h=Environment.GetEnvironmentVariable("CLAUDE_CODE_TMPDIR");return Path.GetFullPath(string.IsNullOrEmpty(h)?Path.Combine(Path.GetTempPath(),"claude"):h);}
 static List<string> ScratchClaude(string path,string preview){
  var ids=new HashSet<string>();var projects=Path.Combine(ClaudeHome(),"projects");
  foreach(Match match in Regex.Matches(preview,@"(?m)^\s*dir:\s*(.+)$")){
   var dir=match.Groups[1].Value.Trim();if(!Path.IsPathRooted(dir)||!Wewnatrz(dir,projects)||!Directory.Exists(dir))continue;BezLinkow(dir,projects);
   foreach(var file in Directory.GetFiles(dir,"*.jsonl",SearchOption.TopDirectoryOnly)){
    BezLinkow(file,projects);Guid id;if(!Guid.TryParse(Path.GetFileNameWithoutExtension(file),out id))continue;ids.Add(id.ToString("D"));
    using(var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new StreamReader(input)){
     for(int i=0;i<50&&!reader.EndOfStream;i++){var line=reader.ReadLine();if(line.Length>1048576)throw new EcxpException("Zbyt duży nagłówek sesji Claude.");Dictionary<string,object> record;try{record=P.Js().Deserialize<Dictionary<string,object>>(line);}catch{continue;}var cwd=P.S(record,"cwd");if(cwd.Length==0)continue;if(!string.Equals(Path.GetFullPath(cwd),Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase)&&!Wewnatrz(cwd,path))throw new EcxpException("Stan Claude jest współdzielony z innym katalogiem. Nie usunięto danych.");break;}
    }
   }
  }
  var result=new List<string>();var temp=ClaudeTemp();if(Directory.Exists(temp)){if((File.GetAttributes(temp)&FileAttributes.ReparsePoint)!=0)throw new EcxpException("Katalog tymczasowy Claude jest dowiązaniem.");foreach(var group in Directory.GetDirectories(temp)){BezLinkow(group,temp);foreach(var id in ids){var dir=Path.Combine(group,id);if(Directory.Exists(dir)){SprawdzDrzewo(dir,temp);result.Add(dir);}}}}
  return result.OrderBy(x=>x).ToList();
 }
 static List<string> PlikiProjektu(string project,List<string> names){
  var all=P.Wszystkie(true);var own=all.Where(m=>m.Projekt==project).ToList();var paths=own.Select(m=>m.Sciezka).ToList();
  var shared=new HashSet<string>(all.Where(m=>m.Projekt!=project).SelectMany(m=>m.Czesci).Select(c=>P.S(c,"sha256")));
  var drafts=Path.Combine(P.Root,"narzedzia","niewyslane-wlasciciela.json");if(File.Exists(drafts)){var list=P.Js().Deserialize<object[]>(P.Czytaj(drafts));foreach(var draft in list.Cast<Dictionary<string,object>>().Where(d=>P.S(d,"Projekt")!=project))foreach(var part in P.Lista(draft,"Czesci"))shared.Add(P.S(part,"sha256"));}
  foreach(var sha in own.SelectMany(m=>m.Czesci).Select(c=>P.S(c,"sha256")).Distinct())if(Regex.IsMatch(sha,@"^[0-9a-f]{64}$")&&!shared.Contains(sha)){
   var obj=P.ObjPath(sha);if(File.Exists(obj))paths.Add(obj);var copies=Path.Combine(P.Root,"kontekst","zalaczniki",sha.Substring(0,12));if(Directory.Exists(copies))foreach(var file in Directory.GetFiles(copies))if(P.ShaPliku(file)==sha)paths.Add(file);
  }
  foreach(var folder in new[]{"Odebrane","Wysłane"}){var view=Path.Combine(P.Root,folder);if(!Directory.Exists(view))continue;SprawdzDrzewo(view,P.Root);foreach(var file in Directory.GetFiles(view,"*.txt",SearchOption.AllDirectories)){
   BezLinkow(file,P.Root);var headers=Regex.Matches(P.Czytaj(file),@"(?m)^Projekt:\s+([a-z0-9-]+)\s+Moduł:").Cast<Match>().Select(m=>m.Groups[1].Value).Distinct().ToList();
   if(headers.Contains(project)){if(headers.Count>1)throw new EcxpException("Widok wiadomości obejmuje kilka projektów; rozdziel wątek przed usunięciem.");paths.Add(file);}
  }}
  var state=Path.Combine(P.Root,"stan");if(Directory.Exists(state))foreach(var dir in Directory.GetDirectories(state)){
   if(string.Equals(dir,Dir,StringComparison.OrdinalIgnoreCase))continue;
   foreach(var file in Directory.GetFiles(dir,"*",SearchOption.TopDirectoryOnly)){
    var stem=Path.GetFileName(file);bool belongs=names.Any(n=>stem.StartsWith(n+".",StringComparison.OrdinalIgnoreCase));
    if(!belongs&&file.EndsWith(".json",StringComparison.OrdinalIgnoreCase))try{belongs=P.S(Pliki.Czytaj(file),"projekt")==project;}catch{}
    if(belongs)paths.Add(file);
   }
  }return paths.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x).ToList();
 }
 public static Dictionary<string,object> Plan(string project,bool codex,bool claude){
  if(Trwa(project))throw new EcxpException("Projekt jest już w trakcie usuwania.");
  var path=Katalog(project);if(!Directory.Exists(path))throw new EcxpException("Projekt nie istnieje.");SprawdzDrzewo(path,Path.Combine(P.Root,"projekty"));
  var names=Siec.Trasy().Where(t=>t.Projekt==project).Select(t=>t.Nazwa).Concat(Agenci.Lista().Where(r=>P.S(r,"projekt")==project).Select(r=>P.S(r,"nazwa"))).Distinct().OrderBy(x=>x).ToList();
  var files=PlikiProjektu(project,names);foreach(var f in files)BezLinkow(f,P.Root);
  var threads=codex?Watki(path):new List<string>();var prefix=claude?ClaudePrefix():"";var cp=claude?ClaudePlan(path,prefix):"";
  string home=codex?Cieply.KonserwacjaCodex((rpc,h)=>h):"";
  if(codex&&(!Path.IsPathRooted(home)||!Directory.Exists(home)))throw new EcxpException("Codex zwrócił nieprawidłowy katalog profilu.");
  if(codex&&File.Exists(Path.Combine(home,".codex-global-state.json")))BezLinkow(Path.Combine(home,".codex-global-state.json"),home);
  if(codex&&File.Exists(Path.Combine(home,".codex-global-state.json")))CodexProjekt.LokalneId(Pliki.Czytaj(Path.Combine(home,".codex-global-state.json")),path);
  var reports=files.Where(f=>string.Equals(Path.GetDirectoryName(f),Path.Combine(P.Root,"stan","pentesty"),StringComparison.OrdinalIgnoreCase)&&f.EndsWith(".json")).Select(f=>Path.Combine(Path.GetDirectoryName(f),Path.GetFileNameWithoutExtension(f))).Where(Directory.Exists).Distinct().ToList();foreach(var dir in reports)SprawdzDrzewo(dir,Path.Combine(P.Root,"stan","pentesty"));
  var p=P.D("projekt",project,"katalog",path,"codex",codex,"claude",claude,"agenci",names,"pliki",files,"katalogi_dodatkowe",reports,"watki",threads,"codex_home",home,"claude_home",claude?ClaudeHome():"","claude_tmp",claude?ClaudeTemp():"","claude_scratch",claude?ScratchClaude(path,cp):new List<string>(),"claude_polecenie",prefix,"claude_plan",cp);
  p["skrot"]=P.Sha(P.Js().Serialize(p));return p;
 }
 public static Dictionary<string,object> Przygotuj(string project,bool codex,bool claude){var p=Plan(project,codex,claude);var token=Guid.NewGuid().ToString("N");p["token"]=token;p["wazny_do"]=DateTime.UtcNow.AddMinutes(15).ToString("o");lock(bilety){foreach(var old in bilety.Keys.Where(k=>DateTime.Parse(P.S(bilety[k],"wazny_do")).ToUniversalTime()<DateTime.UtcNow).ToList())bilety.Remove(old);bilety[token]=p;}return p;}
 public static Dictionary<string,object> OdbierzBilet(string token){
  Dictionary<string,object> p;lock(bilety){if(!bilety.TryGetValue(token,out p)||DateTime.Parse(P.S(p,"wazny_do")).ToUniversalTime()<DateTime.UtcNow)throw new EcxpException("Plan wygasł; wyświetl go ponownie.");bilety.Remove(token);}
  var current=Plan(P.S(p,"projekt"),P.B(p,"codex"),P.B(p,"claude"));if(P.S(current,"skrot")!=P.S(p,"skrot"))throw new EcxpException("Zakres zmienił się od podglądu. Ponownie sprawdź plan.");
  var marker=Path.Combine(Dir,P.S(p,"projekt")+".blokada.json");
  P.ZBlokada(()=>{Directory.CreateDirectory(Dir);Pliki.Zapisz(marker,P.D("token",token));Pliki.Zapisz(Path.Combine(Dir,P.S(p,"projekt")+".epoka.json"),P.D("token",token));foreach(var n in P.L(p,"agenci")){var record=Path.Combine(P.Root,"stan","agenci",n+".json");if(File.Exists(record))Agenci.Zatrzymaj(n);}return 0;});
  Cieply.Zamknij(n=>P.L(p,"agenci").Contains(n));
  var limit=DateTime.UtcNow.AddSeconds(15);while(Agenci.TrwaKtorys(P.L(p,"agenci"))&&DateTime.UtcNow<limit)System.Threading.Thread.Sleep(50);
  if(Agenci.TrwaKtorys(P.L(p,"agenci"))){File.Delete(marker);throw new EcxpException("Tura agenta nadal kończy pracę. Poczekaj i ponów usuwanie.");}
  return p;
 }
 public static string Wynik(string token){Guid id;if(!Guid.TryParseExact(token,"N",out id))throw new EcxpException("Niepoprawny bilet usuwania.");return Path.Combine(Dir,token+".wynik.json");}
 public static ProcessStartInfo Pomocnik(string exe,string token,bool uac){Wynik(token);var psi=new ProcessStartInfo(exe,"/root \""+Path.GetFullPath(P.Root)+"\" /usun-projekt "+token){UseShellExecute=uac,CreateNoWindow=!uac,WindowStyle=ProcessWindowStyle.Hidden};if(uac)psi.Verb="runas";return psi;}
 public static Dictionary<string,object> Wykonaj(Dictionary<string,object> p){
  var project=P.S(p,"projekt");var path=Katalog(project);if(!string.Equals(path,P.S(p,"katalog"),StringComparison.OrdinalIgnoreCase))throw new EcxpException("Niepoprawny katalog planu.");
  var token=P.S(p,"token");var result=P.D("projekt",project,"sukces",false);var completed=new List<string>();
  try{
   SprawdzDrzewo(path,Path.Combine(P.Root,"projekty"));
   if(P.B(p,"codex")){
    var current=Watki(path);if(!current.SequenceEqual(P.L(p,"watki")))throw new EcxpException("Zmieniła się lista wątków Codexa; utwórz nowy plan.");
    Cieply.KonserwacjaCodex((rpc,home)=>{if(!string.Equals(home,P.S(p,"codex_home"),StringComparison.OrdinalIgnoreCase))throw new EcxpException("Zmieniony profil Codexa; przerwano usuwanie.");foreach(var id in current)rpc("thread/delete",P.D("threadId",id),30000);return 0;});
    CodexProjekt.UsunLokalneWpisy(P.S(p,"codex_home"),path);completed.Add("Codex: trwale usunięto wątki, metadane i lokalne wpisy projektu ("+current.Count+")");
   }
   if(P.B(p,"claude")){
    if(ClaudeHome()!=P.S(p,"claude_home")||ClaudeTemp()!=P.S(p,"claude_tmp"))throw new EcxpException("Zmieniony profil lub katalog tymczasowy Claude.");
    var preview=ClaudePlan(path,P.S(p,"claude_polecenie"));if(!ScratchClaude(path,preview).SequenceEqual(P.L(p,"claude_scratch")))throw new EcxpException("Zmienił się zakres danych tymczasowych Claude.");
    if(!preview.Contains("No Claude Code project state found"))Run(Agenci.Program("claude"),P.S(p,"claude_polecenie")+" \""+path+"\" --yes",path);
    foreach(var scratch in P.L(p,"claude_scratch"))UsunDrzewo(scratch,ClaudeTemp());completed.Add("Claude: usunięto lokalny stan projektu i jego dane tymczasowe");
   }
   P.ZBlokada(()=>{
    var messages=P.Wszystkie(true).Where(m=>m.Projekt==project).ToList();
    foreach(var m in messages)if(m.Id.Length>0)P.Zdarzenie("projekt_wiadomosc_usunieta",P.D("id",m.Id));
    // Trasy wspólne są filtrowane, a nie kasowane razem z innymi projektami.
    var routes=Path.Combine(P.Root,"stan","trasy.json");if(File.Exists(routes)){var data=P.Js().Deserialize<object[]>(P.Czytaj(routes));File.WriteAllText(routes,P.Js().Serialize(data.Cast<Dictionary<string,object>>().Where(d=>P.S(d,"projekt")!=project).ToArray()),new UTF8Encoding(false));}
    var labels=Path.Combine(P.Root,"stan","znaczniki.json");if(File.Exists(labels)){var data=Pliki.Czytaj(labels);data["sesje"]=P.Lista(data,"sesje").Where(d=>P.S(d,"projekt")!=project).ToList();Pliki.Zapisz(labels,data);}
    foreach(var file in P.L(p,"pliki").Concat(messages.Select(m=>m.Sciezka)).Distinct(StringComparer.OrdinalIgnoreCase))UsunPlik(file,P.Root);
    foreach(var dir in P.L(p,"katalogi_dodatkowe"))UsunDrzewo(dir,Path.Combine(P.Root,"stan","pentesty"));
    var viewIndex=Path.Combine(P.Root,"stan","widok.json");if(File.Exists(viewIndex)){var index=Pliki.Czytaj(viewIndex);object value;if(index.TryGetValue("wiadomosci",out value)&&value is Dictionary<string,object>){var entries=(Dictionary<string,object>)value;var ids=new HashSet<string>(messages.Select(m=>m.Id));foreach(var key in entries.Keys.Where(k=>P.L(entries[k] as Dictionary<string,object>,"ids").Any(ids.Contains)).ToList())entries.Remove(key);Pliki.Zapisz(viewIndex,index);}}
    var drafts=Path.Combine(P.Root,"narzedzia","niewyslane-wlasciciela.json");if(File.Exists(drafts)){var data=P.Js().Deserialize<object[]>(P.Czytaj(drafts));File.WriteAllText(drafts,P.Js().Serialize(data.Cast<Dictionary<string,object>>().Where(d=>P.S(d,"Projekt")!=project).ToArray()),new UTF8Encoding(false));}
    UsunDrzewo(path,Path.Combine(P.Root,"projekty"));return 0;
   });completed.Add("Mailbox: usunięto katalog i dane projektu");result["sukces"]=true;
  }catch(Exception ex){result["blad"]=ex.Message;}
  finally{result["wykonano"]=completed;Directory.CreateDirectory(Dir);Pliki.Zapisz(Wynik(token),result);var marker=Path.Combine(Dir,project+".blokada.json");if(File.Exists(marker))File.Delete(marker);}
  return result;
 }
}
}
