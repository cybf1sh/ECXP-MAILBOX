using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Security.Cryptography;

namespace Ecxp {
static partial class AnalizaObserwatorium {
    // Every extracted field retains its literal line and provenance. No commands or URLs are executed.
    internal class Szczegol {
        public string Pole, Wartosc, Cytat, Pochodzenie, Stan, Artefakt;
        public int Linia;
    }
    internal class Dzialanie {
        public Msg Zrodlo;
        public List<Szczegol> Pola = new List<Szczegol>();
        public List<string> Odwolania = new List<string>();
    }
    internal class DowodLokalny {
        public string Nazwa, Sciezka, Tekst, Sha, Stan;
    }
    static readonly string[,] Techniki = {
        {"Normalizacja i kodowanie wejścia", @"normalizac|canonical\(|kodowani|encoding|percent.encod|url.encod"},
        {"Kontrola zakresu i przekierowań", @"przekierowa|redirect|scope.guard|scoped_http|walidator.*zakres|allowlist"},
        {"Uwierzytelnianie i uprawnienia", @"\bmTLS\b|uwierzyteln|autoryzac|authentication|authorization|\bIDOR\b|access.control"},
        {"Limity, równoległość i zatrzymanie", @"rate.limit|req/s|równoległo|współbież|concurren|mechanizm.*STOP|znacznik.*STOP|KeyboardInterrupt|BaseException"},
        {"Testy HTTP / API", @"żąda[nń].*HTTP|HTTP.*żąda|\bHTTP/[12]|\b(GET|POST|PUT|DELETE|PATCH)\s+/|test.*\bAPI\b"},
        {"Analiza statyczna", @"\b(Semgrep|Bandit|SAST)\b|analiz[ayę] statyczn"},
        {"Analiza zależności / sekretów", @"\b(Trivy|Gitleaks|pip-audit|dependency audit)\b"},
        {"Testy przeglądarkowe", @"\b(Playwright|Selenium|Cypress)\b|test.*\bUI\b"},
        {"Testy automatyczne i regresja", @"\b(pytest|unittest|selftest|regresj\w*|regression)\b|test.*jednostkow"},
        {"Testy różnicowe i porównawcze", @"różnicow|differential|porówn.*wariant|benchmark|test A/B|WHATWG"},
        {"Reprodukcja usterki", @"odtworz|reprodukc|reproduktor|reproduc"},
        {"Analiza logów", @"analiz.*log|przejrza.*log|log analysis"}
    };
    static readonly string Narzedzia = @"\b(?:Semgrep|Bandit|Trivy|Gitleaks|pip-audit|Nmap|Nuclei|ZAP|Burp(?: Suite)?|Playwright|Selenium|Cypress|pytest|unittest|curl|wget|Docker|Chromium|git|dotnet|npm|node|python(?:3)?|powershell|pwsh|csc|msbuild)\b|[\w.-]+\.(?:py|ps1|sh|cmd|exe)\b";
    static string StatusLinii(string s, Msg m) {
        if(m.EtapObiegu=="fin_wspolny"){var copy=m.Klon();copy.EtapObiegu="";return "Relacja zbiorcza — sprawdź wykonawcę · "+StatusLinii(s,copy);}
        if(s.TrimStart().StartsWith(">"))return "Cytat / kontekst";
        if(Pasuje(s,@"nie (wykona|uruch|testowa|potwierdz)|nie działa|niewykon|not (run|executed|confirmed)|did not|odmow|odmaw|cannot|zablokowan"))return "Negacja / przeszkoda — nie traktuj jako sukces";
        if(m.Typ=="zadanie"||m.Od=="wlasciciel"||s.TrimEnd().EndsWith("?")||Pasuje(s,@"\b(proponuję|można|należy|planuję|planowane|zamierzam|should|plan to|could|przykład)\b"))return "Plan / propozycja / pytanie";
        if(m.EtapObiegu=="fin_wspolny"||Pasuje(s,@"według|z raport|na podstawie.*raport|ostatni.*rund|poprzedni|reported|according to"))return "Relacja wyników — wykonawca wymaga sprawdzenia";
        return "Zapis autora — wykonanie niezweryfikowane";
    }
    static IEnumerable<string> Referencje(string s) {
        // Filename inventory only; never use message text as an arbitrary filesystem path.
        return Regex.Matches(s??"",@"[\p{L}\p{N}_][\p{L}\p{N}_.-]*\.(?:txt|md|jsonl|json|log)\b",RegexOptions.IgnoreCase).Cast<Match>().Select(x=>x.Value).Distinct(StringComparer.OrdinalIgnoreCase);
    }
    internal static Dzialanie Szczegoly(Msg m, string tekstPliku = null, string nazwaPliku = null) {
        var a=new Dzialanie{Zrodlo=m};
        Action<string,string> scan=(content,origin)=>{
            bool code=false;int number=0;
            foreach(var raw in Regex.Split(content??"",@"\r?\n")) {
                number++;if(tekstPliku!=null&&(number>3000||a.Pola.Count>=120))break;var line=raw.Trim();if(tekstPliku!=null&&line.Length>4000)continue;if(line.Length==0)continue;
                if(line.StartsWith("```")){code=!code;continue;}
                Action<string,string> add=(field,value)=>{if(!a.Pola.Any(x=>x.Pole==field&&x.Cytat==raw&&x.Pochodzenie==origin))a.Pola.Add(new Szczegol{Pole=field,Wartosc=value,Cytat=raw,Linia=number,Pochodzenie=origin,Stan=(tekstPliku==null?"":"Treść pliku, nie niezależna weryfikacja · ")+StatusLinii(line,m),Artefakt=nazwaPliku});};
                var methods=new List<string>();for(int i=0;i<Techniki.GetLength(0);i++)if(Pasuje(line,Techniki[i,1]))methods.Add(Techniki[i,0]);
                var custom=Regex.Match(line,@"(?:^|\s)(?:technika|metoda|technique|method)\s*:\s*(.+)",RegexOptions.IgnoreCase);if(custom.Success)methods.Add(custom.Groups[1].Value);
                if(methods.Count>0)add("Technika",string.Join(" · ",methods));
                var tools=Regex.Matches(line,Narzedzia,RegexOptions.IgnoreCase).Cast<Match>().Select(x=>x.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var named=Regex.Match(line,@"(?:^|\s)(?:narzędzie|narzedzie|tool)\s*:\s*(.+)",RegexOptions.IgnoreCase);if(named.Success)tools.Add(named.Groups[1].Value);
                if(tools.Count>0)add("Narzędzie",string.Join(" · ",tools));
                if(code)add("Fragment kodu / logu",line);
                if(Pasuje(line,@"(?:^|\s)(?:polecenie|command|komenda|parametry|parameters|argumenty|args)\s*:|(?:^|\s)--?[a-zA-Z][\w-]*(?:[=\s]|$)|\d+(?:\.\d+)?\s*(?:req/s|rps|ms|sekund|na sekund|sek\b)|timeout\s*[=:]|\b(?:GET|POST|PUT|PATCH|DELETE)\s+/"))add("Parametry / polecenie",line);
                if(Pasuje(line,@"ponieważ|dlatego|ze względu|powodem|przyczyn|because|due to|wybrał\w*.*(?:aby|żeby)|wybrał\w*.*bo\b|uzasadnienie\s*:|reason\s*:"))add("Powód wyboru / ograniczenie",line);
                if(Pasuje(line,@"\b(?:wynik|result|PASS|FAIL|FAILED|PASSED|exit.code|regresja)\b|\d+\s*/\s*\d+|\d+.*test.*przesz|\b[2345]\d\d\b|nie potwierdz|nie działa|odrzucon|timeout|niewykon"))add("Wynik zapisany",line);
                if(Pasuje(line,@"\b(?:dow[oó]d|dowody|evidence|stdout|stderr|sha-?256|artefakt\w*|raport\w*)\b"))add("Wskazanie dowodu",line);
            }
        };
        if(tekstPliku!=null){scan(tekstPliku,"Plik "+nazwaPliku);return a;}
        scan(m.Uwagi,"Treść wiadomości");
        foreach(var c in m.Czesci.Where(x=>P.S(x,"rodzaj")=="tekst"))scan(P.S(c,"tekst"),"Część tekstowa "+(m.Czesci.IndexOf(c)+1));
        foreach(var c in m.Kontrole)a.Pola.Add(new Szczegol{Pole="Kontrola zadeklarowana",Wartosc=c,Cytat=c,Linia=m.Kontrole.IndexOf(c)+1,Pochodzenie="Pole kontrole",Stan="Zapis autora — nie jest niezależnym logiem wykonania"});
        foreach(var c in m.Czesci.Where(x=>P.S(x,"rodzaj")!="tekst"))a.Pola.Add(new Szczegol{Pole="Kontekst / załącznik",Wartosc=P.S(c,"rodzaj"),Cytat=P.Js().Serialize(c),Linia=m.Czesci.IndexOf(c)+1,Pochodzenie="Pole czesci",Stan="Metadane — treść nie została odczytana"});
        a.Odwolania=Referencje(Tekst(m)+"\n"+string.Join("\n",m.Pliki)+"\n"+string.Join("\n",m.Czesci.Select(c=>P.Js().Serialize(c)))).ToList();
        return a;
    }
    internal static List<DowodLokalny> Dowody(string root,IEnumerable<string> names) {
        var wanted=new HashSet<string>(names,StringComparer.OrdinalIgnoreCase);var result=new List<DowodLokalny>();
        if(string.IsNullOrEmpty(root))return wanted.Select(n=>new DowodLokalny{Nazwa=n,Stan="Tylko odwołanie — nie odczytano pliku"}).ToList();
        var baseDir=new DirectoryInfo(Path.GetFullPath(root));
        if(!baseDir.Exists||(baseDir.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Nieprawidłowy katalog dowodów.");
        var found=new Dictionary<string,List<string>>(StringComparer.OrdinalIgnoreCase);var queue=new Queue<DirectoryInfo>();queue.Enqueue(baseDir);int entries=0;bool limited=false;var problems=new List<string>();
        while(queue.Count>0&&!limited){var dir=queue.Dequeue();try{foreach(var entry in dir.EnumerateFileSystemInfos()){
            if(++entries>20000){limited=true;break;}if((entry.Attributes&FileAttributes.ReparsePoint)!=0)continue;
            var sub=entry as DirectoryInfo;if(sub!=null){if(sub.Name!=".git"&&sub.Name!="node_modules"&&sub.Name!=".venv")queue.Enqueue(sub);continue;}
            if(!wanted.Contains(entry.Name))continue;if(!found.ContainsKey(entry.Name))found[entry.Name]=new List<string>();found[entry.Name].Add(entry.FullName);
        }}catch(IOException){problems.Add(dir.FullName);}catch(UnauthorizedAccessException){problems.Add(dir.FullName);}}
        long total=0;
        foreach(var name in wanted){var item=new DowodLokalny{Nazwa=name};result.Add(item);List<string> paths;
            if(!found.TryGetValue(name,out paths)){item.Stan="Nie znaleziono w wybranym katalogu";continue;}
            if(paths.Count!=1||limited||problems.Count>0){item.Stan="Nie odczytano: niejednoznaczna nazwa lub niepełny przegląd katalogu";continue;}
            item.Sciezka=paths[0].Substring(baseDir.FullName.TrimEnd('\\').Length).TrimStart('\\');
            try{var info=new FileInfo(paths[0]);if(info.Length>512*1024||total+info.Length>8*1024*1024){item.Stan="Nie odczytano: limit 512 KB/pliki lub 8 MB/raport";continue;}
                // Recheck directory links immediately before reading; never follow referenced paths outside the chosen root.
                for(var dir=info.Directory;dir!=null;dir=dir.Parent){if((dir.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Dowiązanie w ścieżce");if(dir.FullName.Equals(baseDir.FullName,StringComparison.OrdinalIgnoreCase))break;}
                if((info.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Dowiązanie pliku");
                byte[] bytes;using(var input=new FileStream(info.FullName,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))using(var output=new MemoryStream()){
                    var buffer=new byte[8192];int read;while(output.Length<=512*1024&&(read=input.Read(buffer,0,(int)Math.Min(buffer.Length,512*1024+1-output.Length)))>0)output.Write(buffer,0,read);bytes=output.ToArray();
                }
                if(bytes.Length>512*1024){item.Stan="Nie odczytano: plik urósł ponad limit";continue;}total+=bytes.Length;
                using(var sha=SHA256.Create())item.Sha=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
                using(var reader=new StreamReader(new MemoryStream(bytes),new UTF8Encoding(false,true),true))item.Tekst=reader.ReadToEnd();
                if(item.Tekst.IndexOf('\0')>=0){item.Tekst=null;item.Stan="Nie odczytano: zawartość binarna";continue;}
                item.Stan="Odczytano plik lokalny; pochodzenie i wykonanie niezweryfikowane";
            }catch(Exception ex){item.Tekst=null;item.Stan="Nie odczytano: "+ex.Message;}
        }
        if(limited||problems.Count>0)result.Add(new DowodLokalny{Nazwa="Zakres odczytu",Stan="Przegląd niepełny: limit 20000 wpisów lub brak dostępu do części katalogów."});
        return result;
    }
    static void DopiszTechniki(StringBuilder b,List<Msg> list,string agent,string root) {
        var records=list.Where(m=>agent==null||m.Od==agent).Select(m=>Szczegoly(m)).Where(a=>a.Pola.Count>0||a.Odwolania.Count>0).ToList();
        var files=Dowody(root,records.SelectMany(a=>a.Odwolania));
        b.Append("<h2 id='techniki'>Techniki, narzędzia i dowody</h2><p>Każde pole ma cytat i źródło. Nazwa techniki jest etykietą regułową; polecenia, parametry i powody pochodzą wyłącznie z zapisu. Sąsiedztwo pól nie dowodzi, że wynik dotyczy każdej wymienionej metody. FIN opisuje wiadomość lub zadanie, nie skuteczność techniki. Nadawca zbiorczego FIN nie musi być wykonawcą opisywanych działań.</p>");
        b.Append("<p>Brak logu lub pliku oznacza brak dowodu wykonania, nawet gdy agent opisuje sukces. Lokalne dowody można dołączyć przyciskiem nad raportem po wybraniu projektu. Nie uruchamiamy narzędzi ani nie pobieramy odnośników z sieci.</p>");
        foreach(var a in records){var m=a.Zrodlo;
            b.Append("<section class='observation source' data-results='").Append(m.Typ=="fin"&&(new[]{"pozytywny","negatywny","niewykonane"}).Contains(m.Wynik)?m.Wynik:"brak").Append("' data-time='").Append(m.Czas.ToString("yyyyMMddHHmmss")).Append("'><h3>").Append(H(m.Projekt+" · "+P.TytulWiadomosci(m))).Append("</h3><p>Nadawca: ").Append(H(m.Od)).Append(" · ").Append(H(m.Czas)).Append(" · ").Append(H(m.EtapObiegu=="fin_wspolny"?"Zbiorcza relacja kilku stron":m.Typ)).Append(" · Źródło: ").Append(Link(m)).Append("</p>");
            foreach(var file in files.Where(f=>f.Tekst!=null&&a.Odwolania.Contains(f.Nazwa,StringComparer.OrdinalIgnoreCase)))a.Pola.AddRange(Szczegoly(m,file.Tekst,file.Nazwa).Pola);
            b.Append("<p>Wykryte etykiety technik: ").Append(H(string.Join(" · ",a.Pola.Where(x=>x.Pole=="Technika").Select(x=>x.Wartosc).Distinct()))).Append("</p>");
            foreach(var field in new[]{"Technika","Narzędzie","Parametry / polecenie","Fragment kodu / logu","Powód wyboru / ograniczenie","Wynik zapisany","Wskazanie dowodu","Kontrola zadeklarowana","Kontekst / załącznik"}){
                var values=a.Pola.Where(x=>x.Pole==field).ToList();b.Append("<details class='tech-field'><summary>").Append(H(field)).Append(" · ").Append(values.Count).Append("</summary>");
                if(values.Count==0)b.Append("<p>Brak zapisanej informacji w tej wiadomości.</p>");
                foreach(var s in values)b.Append("<article><b>").Append(H(s.Wartosc)).Append("</b><p>").Append(H(s.Stan)).Append(" · ").Append(H(s.Pochodzenie)).Append(", wiersz ").Append(s.Linia).Append(" · ").Append(s.Artefakt==null?Link(m):"<a href='#dowod-"+files.FindIndex(f=>f.Nazwa==s.Artefakt)+"'>Plik źródłowy</a>").Append("</p><pre>").Append(H(s.Cytat)).Append("</pre></article>");
                b.Append("</details>");
            }
            b.Append("<details class='tech-field'><summary>Pliki i dowody lokalne · ").Append(a.Odwolania.Count).Append("</summary>");
            if(a.Odwolania.Count==0)b.Append("<p>Brak wskazanych plików z dowodami.</p>");
            foreach(var name in a.Odwolania){var f=files.First(x=>string.Equals(x.Nazwa,name,StringComparison.OrdinalIgnoreCase));b.Append("<p><b>").Append(H(name)).Append("</b> — ").Append(H(f.Stan)).Append("</p>");if(f.Tekst!=null)b.Append("<a href='#dowod-").Append(files.IndexOf(f)).Append("'>Czytaj plik i jego SHA-256</a>");}
            b.Append("</details></section>");
        }
        if(records.Count==0)b.Append("<p>Brak szczegółów pasujących do reguł. Pełne wiadomości pozostają dostępne w zakładce źródeł.</p>");
        b.Append("<h3>Odczytane artefakty</h3><p>Ekstrakcja pól z pliku obejmuje pierwsze 3000 wierszy, do 120 trafień; pomija wiersze dłuższe niż 4000 znaków. Pełna odczytana treść jest poniżej. Treść pliku to materiał do analizy; sam fakt istnienia pliku nie potwierdza prawdziwości zawartych w nim deklaracji.</p>");
        foreach(var f in files.Where(x=>x.Tekst!=null)){b.Append("<details class='artifact' id='dowod-").Append(files.IndexOf(f)).Append("'><summary>").Append(H(f.Nazwa)).Append("</summary><p>Ścieżka względem wybranego katalogu: ").Append(H(f.Sciezka)).Append("<br>SHA-256: ").Append(H(f.Sha)).Append("</p><pre>");int n=0;foreach(var line in Regex.Split(f.Tekst,@"\r?\n"))b.Append(H((++n)+": "+line)).Append("\n");b.Append("</pre></details>");}
        foreach(var f in files.Where(x=>x.Nazwa=="Zakres odczytu"))b.Append("<p>").Append(H(f.Stan)).Append("</p>");
    }
}
}
