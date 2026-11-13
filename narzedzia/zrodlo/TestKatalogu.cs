// Test katalogu agentów (logika plików, dane syntetyczne): wyszukiwanie, tworzenie, edycja z kopią, ochrona ścieżek.
// Użycie: TestKatalogu.exe <pusty katalog>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
namespace Ecxp {
class TestKatalogu {
 static int failures;
 static void Check(bool ok,string name){Console.WriteLine((ok?"PASS ":"FAIL ")+name);if(!ok)failures++;}
 static string Blad(Action a){try{a();return null;}catch(EcxpException ex){return ex.Message;}}
 public static int Main(string[] args){
  if(args.Length<1){Console.Error.WriteLine("Użycie: TestKatalogu.exe <pusty katalog>");return 2;}
  var root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);if(Directory.EnumerateFileSystemEntries(root).Any())throw new Exception("Empty fixture required");
  P.Root=Path.Combine(root,"skrzynka");Directory.CreateDirectory(P.Root);
  var kat=Path.Combine(root,"Agents");Directory.CreateDirectory(kat);
  var enc=new UTF8Encoding(false);

  // ---------- wyszukiwanie ----------
  Check(KatalogAgentow.Znajdz(kat).Count==0 && KatalogAgentow.Znajdz(Path.Combine(root,"nie-ma")).Count==0 && KatalogAgentow.Znajdz("").Count==0,"empty or missing working directory has no agents");
  var cx=Path.Combine(kat,"codex-cyber-agent");Directory.CreateDirectory(Path.Combine(cx,"scripts"));Directory.CreateDirectory(Path.Combine(cx,"reports"));
  File.WriteAllText(Path.Combine(cx,"AGENTS.md"),"# Cyber Sentinel — Codex\nKatalog główny agenta: "+cx+".\nProponowana tożsamość ECXP: codex-cyber, moduł: cyber.\n- `AGENTS.md` i `scope.md` są instrukcjami.\n",enc);
  File.WriteAllText(Path.Combine(cx,"scope.md"),"# Zakres\nAktywny katalog projektu agenta: "+cx+".\n",enc);
  File.WriteAllText(Path.Combine(cx,"ECXP-START.txt"),"Agent: Cyber Sentinel — Codex. Odczytaj AGENTS.md i scope.md w "+cx+".\n",enc);
  File.WriteAllText(Path.Combine(cx,"engagement.template.json"),"{\n  \"targets\": []\n}\n",enc);
  File.WriteAllText(Path.Combine(cx,"agent-info.json"),"{\"display_name\":\"Cyber Sentinel — Codex\",\"provider\":\"Codex\",\"proposed_ecxp_identity\":\"codex-cyber\",\"proposed_module\":\"cyber\"}",enc);
  File.WriteAllText(Path.Combine(cx,"scripts","skan.py"),"print('x')\n",enc);
  File.WriteAllText(Path.Combine(cx,"reports","raport.json"),"{}",enc);
  File.WriteAllBytes(Path.Combine(cx,"scripts","obraz.png"),new byte[]{1,2,3});
  Directory.CreateDirectory(Path.Combine(kat,"zwykly-folder"));File.WriteAllText(Path.Combine(kat,"zwykly-folder","README.md"),"x",enc);
  var cl0=Path.Combine(kat,"bez-karty");Directory.CreateDirectory(cl0);File.WriteAllText(Path.Combine(cl0,"CLAUDE.md"),"# Bez karty\n",enc);
  var ag=KatalogAgentow.Znajdz(kat);
  var a=ag.Single(x=>x.NazwaFolderu=="codex-cyber-agent");
  Check(ag.Count==2 && a.Strona=="codex" && a.PlikInstrukcji=="AGENTS.md" && a.Nazwa=="Cyber Sentinel — Codex" && a.Tozsamosc=="codex-cyber" && a.Modul=="cyber","agents are folders with AGENTS.md or CLAUDE.md; name and identity come from agent-info.json");
  var b=ag.Single(x=>x.NazwaFolderu=="bez-karty");
  Check(b.Strona=="claude" && b.Nazwa=="bez-karty" && b.Tozsamosc=="","an agent without agent-info.json is listed by its folder name");

  // ---------- pliki ----------
  var pliki=KatalogAgentow.Pliki(a);
  Check(pliki.Where(x=>x.Grupa==KatalogAgentow.GrupaKonfiguracja).Select(x=>x.Wzgledna).SequenceEqual(new[]{"AGENTS.md","agent-info.json"})
    && pliki.Where(x=>x.Grupa==KatalogAgentow.GrupaZakres).Select(x=>x.Wzgledna).SequenceEqual(new[]{"scope.md","ECXP-START.txt","engagement.template.json"}),"configuration and scope files are grouped");
  Check(pliki.Single(x=>x.Grupa==KatalogAgentow.GrupaSkrypty).Wzgledna==Path.Combine("scripts","skan.py") && pliki.Single(x=>x.Grupa==KatalogAgentow.GrupaRaporty).TylkoOdczyt,"scripts are listed (text files only); reports are read-only");

  // ---------- odczyt i zapis ----------
  var instr=Path.Combine(cx,"AGENTS.md");
  var t=KatalogAgentow.Czytaj(a,instr);
  Check(t.KoniecWiersza=="\n" && !t.Bom && t.Tekst.Contains("\r\n") && !t.Tekst.Replace("\r\n","").Contains("\n"),"file with LF line endings is shown with CRLF for the editor");
  var kopia=KatalogAgentow.Zapisz(a,instr,t.Tekst+"## Nowa sekcja\r\nZażółć gęślą jaźń\r\n",t);
  var poZapisie=File.ReadAllBytes(instr);
  Check(!Encoding.UTF8.GetString(poZapisie).Contains("\r") && Encoding.UTF8.GetString(poZapisie).Contains("Zażółć gęślą jaźń") && poZapisie[0]!=0xEF,"saving keeps the original LF endings and no BOM; Unicode is preserved");
  Check(File.Exists(kopia) && kopia.StartsWith(Path.Combine(cx,KatalogAgentow.KatalogKopii)) && !File.ReadAllText(kopia).Contains("Nowa sekcja"),"the previous version is kept in the agent's .kopie folder");
  var zbom=Path.Combine(cx,"scripts","z-bom.ps1");File.WriteAllText(zbom,"Write-Host 1\r\n",new UTF8Encoding(true));
  var tb=KatalogAgentow.Czytaj(a,zbom);KatalogAgentow.Zapisz(a,zbom,tb.Tekst+"Write-Host 2\r\n",tb);
  var bb=File.ReadAllBytes(zbom);
  Check(tb.Bom && bb[0]==0xEF && bb[1]==0xBB && Encoding.UTF8.GetString(bb).Contains("Write-Host 2\r\n"),"a file with BOM and CRLF keeps both");
  Check(Blad(()=>KatalogAgentow.Zapisz(a,Path.Combine(cx,"agent-info.json"),"{ to nie json",null))!=null && File.ReadAllText(Path.Combine(cx,"agent-info.json")).Contains("display_name"),"invalid JSON is refused and the file stays unchanged");
  Check(Blad(()=>KatalogAgentow.Zapisz(a,Path.Combine(cx,"reports","raport.json"),"{}",null))!=null,"reports cannot be edited");

  // ---------- ochrona ścieżek ----------
  var poza=Path.Combine(root,"poza.txt");File.WriteAllText(poza,"tajne",enc);
  Check(Blad(()=>KatalogAgentow.Czytaj(a,poza))!=null && Blad(()=>KatalogAgentow.Czytaj(a,Path.Combine(cx,"..","..","poza.txt")))!=null,"reading outside the agent folder is refused");
  Check(Blad(()=>KatalogAgentow.Zapisz(a,Path.Combine(cx,"scripts","..","..","..","poza.txt"),"x",null))!=null && File.ReadAllText(poza)=="tajne"
    && Blad(()=>KatalogAgentow.Zapisz(a,Path.Combine(cl0,"CLAUDE.md"),"x",null))!=null,"writing outside the agent folder (also into another agent) is refused");
  Check(!KatalogAgentow.WFolderze(cx,cx+"-inny\\plik.txt") && KatalogAgentow.WFolderze(cx,Path.Combine(cx,"scripts","a.py")),"a sibling folder with a similar name is not inside the agent folder");
  File.WriteAllBytes(Path.Combine(cx,"scripts","binarny.txt"),new byte[]{65,0,66});
  Check(Blad(()=>KatalogAgentow.Czytaj(a,Path.Combine(cx,"scripts","binarny.txt")))!=null,"a binary file is not opened in the editor");

  // ---------- skrypty ----------
  var ns=KatalogAgentow.NowySkrypt(a,"sprawdz_naglowki.py");
  Check(File.Exists(ns) && File.ReadAllText(ns).Contains("Użycie: python sprawdz_naglowki.py") && Blad(()=>KatalogAgentow.NowySkrypt(a,"sprawdz_naglowki.py"))!=null,"a new script is created from a template; an existing name is refused");
  Check(Blad(()=>KatalogAgentow.NowySkrypt(a,"..\\zly.py"))!=null && Blad(()=>KatalogAgentow.NowySkrypt(a,"bez-rozszerzenia"))!=null && Blad(()=>KatalogAgentow.NowySkrypt(a,"program.exe"))!=null && Blad(()=>KatalogAgentow.NowySkrypt(a,""))!=null,"bad script names are refused");
  var us=KatalogAgentow.UsunSkrypt(a,ns);
  Check(!File.Exists(ns) && File.Exists(us) && us.Contains(KatalogAgentow.KatalogKopii) && Blad(()=>KatalogAgentow.UsunSkrypt(a,instr))!=null,"deleting a script moves it to .kopie; only scripts can be deleted");

  // ---------- tworzenie agenta ----------
  var nowy=KatalogAgentow.Utworz(kat,"claude-web-agent","claude","Web Sentinel — Claude","web",null);
  Check(nowy.Strona=="claude" && nowy.Tozsamosc=="claude-web" && nowy.Modul=="web" && File.Exists(Path.Combine(nowy.Folder,"CLAUDE.md")) && !File.Exists(Path.Combine(nowy.Folder,"AGENTS.md"))
    && new[]{"input","scripts","tests","reports"}.All(x=>Directory.Exists(Path.Combine(nowy.Folder,x))) && File.Exists(Path.Combine(nowy.Folder,"scope.md")) && File.Exists(Path.Combine(nowy.Folder,"ECXP-START.txt")),"a new agent gets its instruction file, scope, start file and working folders");
  var ni=File.ReadAllText(Path.Combine(nowy.Folder,"CLAUDE.md"));
  Check(ni.Contains("wynik=pozytywny|negatywny|niewykonane") && ni.Contains("typ=postep") && ni.Contains(nowy.Folder) && ni.Contains("claude-web"),"the skeleton instruction carries ECXP 1.1 rules, the folder and the identity");
  var kopiaAg=KatalogAgentow.Utworz(kat,"claude-cyber-agent","claude","Cyber Sentinel — Claude","cyber",a);
  var ki=File.ReadAllText(Path.Combine(kopiaAg.Folder,"CLAUDE.md"));var ks=File.ReadAllText(Path.Combine(kopiaAg.Folder,"scope.md"));var kst=File.ReadAllText(Path.Combine(kopiaAg.Folder,"ECXP-START.txt"));
  Check(ki.Contains(kopiaAg.Folder) && !ki.Contains(cx+".") && ki.Contains("claude-cyber") && !ki.Contains("codex-cyber") && ki.Contains("Cyber Sentinel — Claude") && ki.Contains("`CLAUDE.md`") && ki.Contains("Nowa sekcja"),"copying a configuration replaces folder, identity, name and instruction file name");
  Check(ks.Contains(kopiaAg.Folder) && !ks.Contains(cx+".") && kst.Contains("CLAUDE.md") && !kst.Contains("AGENTS.md") && File.Exists(Path.Combine(kopiaAg.Folder,"engagement.template.json")) && ki.Contains("Uwaga po skopiowaniu konfiguracji"),"scope, start file and engagement template are copied and adjusted; a cross-model copy is flagged");
  Check(!File.Exists(Path.Combine(kopiaAg.Folder,"scripts","skan.py")) && File.ReadAllText(instr).Contains("codex-cyber"),"scripts are not copied and the source agent stays unchanged");
  Check(Blad(()=>KatalogAgentow.Utworz(kat,"claude-web-agent","claude","x","web",null))!=null && Blad(()=>KatalogAgentow.Utworz(kat,"..\\ucieczka","claude","x","web",null))!=null
    && Blad(()=>KatalogAgentow.Utworz(kat,"Duze Litery","claude","x","web",null))!=null && Blad(()=>KatalogAgentow.Utworz(kat,"ok-agent","gemini","x","web",null))!=null
    && Blad(()=>KatalogAgentow.Utworz(kat,"ok-agent","claude","x","zły moduł",null))!=null && Blad(()=>KatalogAgentow.Utworz(Path.Combine(root,"nie-ma"),"ok-agent","claude","x","web",null))!=null
    && !Directory.Exists(Path.Combine(kat,"ok-agent")),"existing folder, path escape, bad names, unknown model and missing directory are refused without leftovers");
  Check(KatalogAgentow.Znajdz(kat).Count==4,"all agents are listed after creation");

  // ---------- zmiana konfiguracji wiadomością ----------
  var fk=Path.Combine(root,"msg-agent");Directory.CreateDirectory(fk);File.WriteAllText(Path.Combine(fk,"CLAUDE.md"),"stara instrukcja");File.WriteAllText(Path.Combine(fk,"scope.md"),"stary zakres");
  var kopieK=KatalogAgentow.KopiaKonfiguracji(fk);
  Check(kopieK.Count==2 && kopieK.All(File.Exists) && kopieK.All(x=>Path.GetDirectoryName(x)==Path.Combine(fk,".kopie")) && File.ReadAllText(kopieK[0])=="stara instrukcja" && File.ReadAllText(Path.Combine(fk,"CLAUDE.md"))=="stara instrukcja","configuration files are backed up before a change by message and the originals stay untouched");
  Check(KatalogAgentow.KopiaKonfiguracji(Path.Combine(root,"nie-ma-folderu")).Count==0 && KatalogAgentow.KopiaKonfiguracji("").Count==0,"backup of a missing folder does nothing");
  var tz=KatalogAgentow.TrescZmianyKonfiguracji("claude-x","claude",fk,"  testuj tylko host A  ");
  Check(tz.Contains("claude-x") && tz.Contains("testuj tylko host A") && tz.Contains("CLAUDE.md") && tz.Contains(fk) && tz.Contains("niewykonane") && !tz.Contains("AGENTS.md"),"change-by-message task names the agent, the change, its own instruction file and folder");
  Check(KatalogAgentow.TrescZmianyKonfiguracji("codex-x","codex","","zmiana").Contains("AGENTS.md") && Blad(()=>KatalogAgentow.TrescZmianyKonfiguracji("codex-x","codex",fk,"   "))!=null,"Codex gets AGENTS.md and an empty change is refused");

  // ---------- domyślna struktura nowego katalogu ----------
  var nowyKat=Path.Combine(root,"NowyKatalog");
  var dom=KatalogAgentow.UtworzDomyslne(nowyKat," Web ","Tester WWW",true,true);
  Check(dom.Count==2 && Directory.Exists(Path.Combine(nowyKat,"codex-web-agent","scripts")) && Directory.Exists(Path.Combine(nowyKat,"claude-web-agent","reports"))
    && File.Exists(Path.Combine(nowyKat,"codex-web-agent","AGENTS.md")) && File.Exists(Path.Combine(nowyKat,"claude-web-agent","CLAUDE.md")) && File.Exists(Path.Combine(nowyKat,"claude-web-agent","scope.md"))
    && KatalogAgentow.Znajdz(nowyKat).Count==2 && dom[0].Tozsamosc=="codex-web" && dom[1].Nazwa.StartsWith("Tester WWW"),"default structure creates a Codex and a Claude agent with instruction, scope and folders in a new directory");
  Check(Blad(()=>KatalogAgentow.UtworzDomyslne(nowyKat,"web","",true,false))!=null && Blad(()=>KatalogAgentow.UtworzDomyslne(nowyKat,"zły moduł","",true,true))!=null && Blad(()=>KatalogAgentow.UtworzDomyslne(nowyKat,"api","",false,false))!=null
    && KatalogAgentow.Znajdz(nowyKat).Count==2,"default structure never overwrites an existing agent and refuses bad input without leftovers");
  Check(KatalogAgentow.UtworzDomyslne(nowyKat,"api","",false,true).Count==1 && KatalogAgentow.Znajdz(nowyKat).Count==3 && !Directory.Exists(Path.Combine(nowyKat,"codex-api-agent")),"default structure can be created for one side only");
  Console.WriteLine("RESULT: "+failures+" failed");
  return failures==0?0:1;
 }
}}
