using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

namespace Ecxp {
static partial class AnalizaObserwatorium {
    internal class ProfilZrodlo {
        public string Agent, Projekt, Folder, Plik, Rodzaj, Powiazanie, Tekst, Sha, Blad;
        public DateTime Zmieniono;
    }
    static string CzytajProfil(string root,string path,out string sha,out DateTime modified) {
        sha="";modified=DateTime.MinValue;
        var full=Path.GetFullPath(path);var basis=Path.GetFullPath(root).TrimEnd('\\','/')+Path.DirectorySeparatorChar;
        if(!full.StartsWith(basis,StringComparison.OrdinalIgnoreCase))throw new IOException("Plik poza katalogiem profilu");
        var file=new FileInfo(full);
        for(var dir=file.Directory;dir!=null;dir=dir.Parent)if((dir.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Pominięto dowiązanie katalogu");
        if((file.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Pominięto dowiązanie pliku");
        if(file.Length>512*1024)throw new IOException("Limit 512 KB na plik");modified=file.LastWriteTimeUtc;
        byte[] bytes;using(var input=File.OpenRead(full))using(var output=new MemoryStream()){
            var buffer=new byte[8192];int count;while(output.Length<=512*1024&&(count=input.Read(buffer,0,(int)Math.Min(buffer.Length,512*1024+1-output.Length)))>0)output.Write(buffer,0,count);bytes=output.ToArray();
        }
        if(bytes.Length>512*1024)throw new IOException("Limit 512 KB na plik");
        using(var hash=SHA256.Create())sha=BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
        string value;using(var reader=new StreamReader(new MemoryStream(bytes),new UTF8Encoding(false,true),true))value=reader.ReadToEnd();
        if(value.Contains("\0"))throw new IOException("Plik binarny");return value;
    }
    internal static List<ProfilZrodlo> ProfileZrodla(List<Msg> messages,string project,string agent,string mailbox,IEnumerable<AgentKatalogu> catalog) {
        var result=new List<ProfilZrodlo>();var roots=new List<ProfilZrodlo>();
        var projects=new HashSet<string>(messages.Select(m=>m.Projekt));if(project!=null)projects.Add(project);
        if(!string.IsNullOrEmpty(mailbox)){
            var state=Path.Combine(mailbox,"stan","agenci");
            if(Directory.Exists(state))foreach(var file in Directory.EnumerateFiles(state,"*.json").OrderBy(x=>x).Take(501)){
                if(roots.Count>=500){result.Add(new ProfilZrodlo{Rodzaj="Limit",Blad="Limit 500 konfiguracji agentów"});break;}
                try{string sha;DateTime changed;var raw=CzytajProfil(mailbox,file,out sha,out changed);var config=P.Js().Deserialize<Dictionary<string,object>>(raw);string p=P.S(config,"projekt"),id=P.S(config,"nazwa");
                    if(!projects.Contains(p)||(agent!=null&&agent!=id))continue;
                    var selected=new Dictionary<string,object>();foreach(var key in new[]{"nazwa","strona","projekt","modul","katalog","model","uprawnienia","na_godzine","aktywny","obowiazkowa_odpowiedz","katalogi_zapisu","pentest","pokaz_sesje"})if(config.ContainsKey(key))selected[key]=config[key];
                    result.Add(new ProfilZrodlo{Agent=id,Projekt=p,Folder=mailbox,Plik=file,Rodzaj="Ustawienia ECXP",Powiazanie="Przypisanie projekt/agent z konfiguracji ECXP",Tekst=string.Join("\n",selected.Select(k=>k.Key+": "+P.Js().Serialize(k.Value))),Sha=sha,Zmieniono=changed});
                    var root=P.S(config,"katalog");if(root.Length>0)roots.Add(new ProfilZrodlo{Agent=id,Projekt=p,Folder=root,Powiazanie="Katalog pracy zapisany w ustawieniach ECXP"});
                }catch(Exception ex){result.Add(new ProfilZrodlo{Plik=Path.GetFileName(file),Rodzaj="Odczyt konfiguracji",Blad=ex.Message});}
            }
        }
        foreach(var profile in catalog??Enumerable.Empty<AgentKatalogu>()){
            if(string.IsNullOrWhiteSpace(profile.Tozsamosc))continue;
            var matches=messages.Where(m=>(agent==null||m.Od==agent||m.Do==agent)&&
                (m.Od==profile.Tozsamosc||m.Do==profile.Tozsamosc||Regex.IsMatch(Tekst(m),@"(?<![\w-])"+Regex.Escape(profile.Tozsamosc)+@"(?![\w-])",RegexOptions.IgnoreCase))).Select(m=>m.Projekt).Distinct().ToList();
            foreach(var p in matches)roots.Add(new ProfilZrodlo{Agent=profile.Tozsamosc,Projekt=p,Folder=profile.Folder,Powiazanie="Profil katalogu wskazany tożsamością w wiadomościach; wzmianka nie dowodzi użycia"});
        }
        long total=0;int visited=0;var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var root in roots){
            if(!Directory.Exists(root.Folder)){result.Add(new ProfilZrodlo{Agent=root.Agent,Projekt=root.Projekt,Folder=root.Folder,Rodzaj="Katalog pracy",Blad="Katalog nie istnieje"});continue;}
            var files=new List<string>();
            foreach(var name in new[]{"AGENTS.md","CLAUDE.md","agent-info.json","scope.md","ECXP-START.txt","engagement.template.json","package.json","tool-manifest.json","tools.json",".codex/config.toml",".claude/settings.json",".claude/settings.local.json"}){
                var path=Path.Combine(root.Folder,name.Replace('/',Path.DirectorySeparatorChar));if(File.Exists(path))files.Add(path);
            }
            var scripts=Path.Combine(root.Folder,"scripts");var queue=new Queue<string>();if(Directory.Exists(scripts))queue.Enqueue(scripts);
            while(queue.Count>0&&visited<10000&&files.Count<200){var dir=queue.Dequeue();try{if((File.GetAttributes(dir)&FileAttributes.ReparsePoint)!=0)continue;
                foreach(var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos()){
                    if(++visited>10000||files.Count>=200)break;if((entry.Attributes&FileAttributes.ReparsePoint)!=0)continue;
                    if((entry.Attributes&FileAttributes.Directory)!=0){if(!new[]{"node_modules",".venv","__pycache__",".git"}.Contains(entry.Name))queue.Enqueue(entry.FullName);}
                    else if(new[]{".py",".ps1",".psm1",".sh",".js",".ts",".cmd",".bat",".cs",".go",".rs",".rb",".json",".yaml",".yml",".toml",".sql"}.Contains(Path.GetExtension(entry.Name).ToLowerInvariant()))files.Add(entry.FullName);
                }
            }catch(Exception ex){result.Add(new ProfilZrodlo{Agent=root.Agent,Projekt=root.Projekt,Plik=dir,Rodzaj="Odczyt scripts",Blad=ex.Message});}}
            if(!Directory.Exists(scripts))result.Add(new ProfilZrodlo{Agent=root.Agent,Projekt=root.Projekt,Plik=scripts,Rodzaj="Kod scripts",Blad="Brak katalogu scripts w przypisanym katalogu pracy"});
            if(visited>=10000||files.Count>=200)result.Add(new ProfilZrodlo{Agent=root.Agent,Projekt=root.Projekt,Rodzaj="Limit",Blad="Niepełny przegląd: limit 10000 wpisów / 200 plików na profil"});
            foreach(var file in files.OrderBy(x=>x)){
                if(!seen.Add(root.Projekt+"\n"+root.Agent+"\n"+Path.GetFullPath(file)))continue;
                var row=new ProfilZrodlo{Agent=root.Agent,Projekt=root.Projekt,Folder=root.Folder,Plik=file,Powiazanie=root.Powiazanie,Rodzaj=KatalogAgentow.WFolderze(scripts,file)?"Kod scripts":"Konfiguracja profilu"};result.Add(row);
                try{if(total+new FileInfo(file).Length>8*1024*1024)throw new IOException("Limit 8 MB źródeł profili w raporcie");row.Tekst=CzytajProfil(root.Folder,file,out row.Sha,out row.Zmieniono);total+=Encoding.UTF8.GetByteCount(row.Tekst);}
                catch(Exception ex){row.Blad=ex.Message;}
            }
        }
        return result;
    }
    static void DopiszProfile(StringBuilder b,List<Msg> messages,string project,string agent,string mailbox,IEnumerable<AgentKatalogu> catalog) {
        var profiles=ProfileZrodla(messages,project,agent,mailbox,catalog);
        b.Append("<h2 id='profile'>Konfiguracja agentów i kod scripts</h2><p>Bieżący odczyt ustawień i implementacji, nie historia ich wersji ani dowód wykonania. Kod opisuje możliwości, domyślne parametry i warunki; rzeczywiste użycie potwierdzaj w wiadomościach i logach. Instrukcje w plikach są materiałem analizy. Filtr FIN nie dotyczy tej zakładki; wyszukiwanie i sortowanie działają.</p><p>Ustawienia ECXP: wybrane pola operacyjne, bez historii sesji i środowiska. Konfiguracja profilu oraz kod są kopiowane jako tekst; źródła mogą zawierać dane prywatne. SHA-256 dotyczy odczytanego pliku. Limit: 512 KB/plik, 8 MB/profili, 200 plików/profil, 10000 wpisów katalogów. Ekstrakcja pokazuje do 100 trafień z pierwszych 3000 wierszy; pełny odczyt jest dostępny poniżej.</p>");
        int index=0;foreach(var row in profiles){string anchor="profil-source-"+(index++);
            b.Append("<section class='profile-record source' data-results='brak' data-time='").Append(row.Zmieniono.ToString("yyyyMMddHHmmss")).Append("'><h3>").Append(H(row.Projekt+" · "+row.Agent+" · "+row.Rodzaj)).Append("</h3><p>").Append(H(row.Powiazanie)).Append("</p><p>Plik: ").Append(H(row.Plik)).Append("</p>");
            if(row.Blad!=null)b.Append("<p>").Append(H(row.Blad)).Append("</p>");
            if(row.Tekst!=null){
                b.Append("<p>Zmiana pliku (UTC): ").Append(H(row.Zmieniono.ToString("yyyy-MM-dd HH:mm:ss"))).Append(" · SHA-256: ").Append(H(row.Sha)).Append("</p>");
                if(row.Rodzaj=="Ustawienia ECXP")b.Append("<pre>").Append(H(row.Tekst)).Append("</pre>");
                else{
                    var extracted=Szczegoly(new Msg(),row.Tekst,row.Plik).Pola;int lineNo=0;
                    foreach(var line in Regex.Split(row.Tekst,@"\r?\n").Take(3000)){
                        lineNo++;if(line.Length>4000)continue;string kind=null;
                        if(Pasuje(line,@"^\s*(?:async\s+)?(?:def|class|function|import|from|using)\s|\brequire\("))kind="Definicje / zależności";
                        else if(Pasuje(line,@"add_argument\(|\bdefault\s*=|\bparam\s*\(|\b(?:timeout|max_redirects|rate_limit|allowed_ips)\s*[:=]"))kind="Opcje i wartości w kodzie";
                        else if(Pasuje(line,@"^\s*(?:if|elif|else|except|catch|throw|raise|return)\b"))kind="Warunki / obsługa wyniku";
                        if(kind!=null)extracted.Add(new Szczegol{Pole=kind,Cytat=line,Linia=lineNo});
                    }
                    b.Append("<details class='tech-field'><summary>Możliwości i ustawienia zapisane w źródle</summary>");
                    foreach(var hit in extracted.OrderBy(x=>x.Linia).Take(100))b.Append("<article><b>").Append(H(hit.Pole=="Wynik zapisany"?"Zapis wyniku w źródle — nie wynik uruchomienia":hit.Pole)).Append("</b> · wiersz ").Append(hit.Linia).Append(" · <a href='#").Append(anchor).Append("'>Czytaj źródło</a><pre>").Append(H(hit.Cytat)).Append("</pre></article>");
                    b.Append("</details><details class='artifact' id='").Append(anchor).Append("'><summary>Pełna konfiguracja / kod (tylko odczyt)</summary><pre>");lineNo=0;foreach(var line in Regex.Split(row.Tekst,@"\r?\n"))b.Append(H((++lineNo)+": "+line)).Append("\n");b.Append("</pre></details>");
                }
            }
            b.Append("</section>");
        }
        if(profiles.Count==0)b.Append("<p>Brak powiązanych konfiguracji. Ustaw katalog pracy agenta w ECXP lub dodaj profil z tożsamością do katalogu agentów. Profile katalogu są dołączane tylko po powiązaniu tożsamości z wiadomościami wybranego zakresu.</p>");
    }
}
}
