using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.IO;
using System.Windows.Forms;

namespace Ecxp {
// Local, evidence-based extraction. Patterns identify candidates, never hidden reasoning or causality.
static class AnalizaObserwatorium {
    internal class Slad {
        public Msg Zrodlo;
        public string Rodzaj, Cytat;
        public List<string> Metody = new List<string>();
    }
    static readonly string[,] Metody = {
        {"Testy automatyczne", @"\b(testy? jednostkowe|unit tests?|pytest|selftest|testy? automatyczne)\b"},
        {"Testy integracyjne", @"\b(testy? integracyjne|integration tests?)\b"},
        {"Analiza kodu", @"\b(przegląd kodu|analiza kodu|code review|static analysis|analiza statyczna)\b"},
        {"Analiza logów", @"\b(analiza logów|analizę logów|log analysis|przejrzałem logi|sprawdziłem logi)\b"},
        {"Reprodukcja błędu", @"\b(reprodukcja|odtworzyłem błąd|odtworzenie błędu|reproduce|reproduced)\b"},
        {"Test ręczny / UI", @"\b(test ręczny|testy ręczne|test UI|testy UI|manual test|zrzut ekranu)\b"},
        {"Dokumentacja", @"\b(dokumentacja|dokumentację|documentation)\b"},
        {"Eksperyment porównawczy", @"\b(benchmark|porównanie wariantów|test A/B)\b"},
        {"Test bezpieczeństwa", @"\b(test bezpieczeństwa|testy bezpieczeństwa|pentest|skan podatności)\b"}
    };
    static bool Pasuje(string s, string p) { return Regex.IsMatch(s, p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
    public static string Tekst(Msg m) {
        return string.Join("\n", new[] {m.Uwagi, m.Ograniczenia}
            .Concat(m.Czesci.Where(c => P.S(c,"rodzaj")=="tekst").Select(c => P.S(c,"tekst")))
            .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
    }
    public static List<Msg> Zakres(IEnumerable<Msg> dane, string projekt, string agent) {
        var wejscie = dane.Where(m => m.Blad == null && (projekt == null || m.Projekt == projekt))
            .OrderBy(m => m.Czas).ThenBy(m => m.Nr).ToList();
        var grupy = wejscie.GroupBy(m => m.Projekt + "\n" + (m.Logiczne.Length > 0 ? m.Logiczne : m.Id)).ToList();
        var aliasy = new Dictionary<string,string>();
        foreach(var g in grupy) {var pierwszy=g.FirstOrDefault(m=>m.KopiaDla.Length==0)??g.First();foreach(var m in g)aliasy[m.Projekt+"\n"+m.Id]=pierwszy.Id;}
        var wszystkie = grupy.Select(g => {var m=(g.FirstOrDefault(x=>x.KopiaDla.Length==0)??g.First()).Klon();string id;if(aliasy.TryGetValue(m.Projekt+"\n"+m.OdpNa,out id))m.OdpNa=id;return m;}).ToList();
        if (agent == null) return wszystkie;
        var wybrane = new HashSet<Msg>(wszystkie.Where(m => m.Od == agent || m.Do == agent || m.Lustro.Contains(agent)));
        // Include the complete linked context, but never cross a project boundary.
        bool zmiana;
        do {
            zmiana = false;
            foreach (var m in wszystkie.Where(m => !wybrane.Contains(m)).ToList())
                if (wybrane.Any(w => Powiazane(m,w))) { wybrane.Add(m); zmiana = true; }
        } while(zmiana);
        return wszystkie.Where(wybrane.Contains).ToList();
    }
    internal static bool Powiazane(Msg a, Msg b) {
        return a.Projekt == b.Projekt && (Odpowiada(a,b) || Odpowiada(b,a) ||
            (a.WatekPole.Length > 0 && a.WatekPole == b.WatekPole));
    }
    static bool Odpowiada(Msg a, Msg b) {
        return (a.OdpNa.Length > 0 && a.OdpNa == b.Id) ||
            (a.OdpNaLogiczne.Length > 0 && b.Logiczne.Length > 0 && a.OdpNaLogiczne == b.Logiczne);
    }
    public static List<Slad> Analizuj(IEnumerable<Msg> wiadomosci, string agent) {
        var wynik = new List<Slad>();
        foreach (var m in wiadomosci.Where(m => agent == null ? P.PoprawnyNadawca(m.Od) : m.Od == agent)) {
            foreach (var zdanie in Regex.Split(Tekst(m), @"(?<=[.!?])\s+|[\r\n]+").Where(s=>s.Trim().Length>0)) {
                var s = zdanie.Trim();
                // Quoted lines and questions are context, not declarations by the author.
                if (s.StartsWith(">") || s.EndsWith("?")) continue;
                var slad = new Slad { Zrodlo=m, Cytat=s, Rodzaj="Wzmianka o metodzie" };
                bool odmowa = Pasuje(s,@"\b(odmawiam|nie mogę (wykonać|pomóc|zrealizować)|nie wykonam|I cannot (help|assist|comply)|I refuse)\b");
                bool powod = Pasuje(s,@"\b(ponieważ|dlatego|ze względu na|powodem|przyczyną|because|due to)\b");
                bool decyzja = Pasuje(s,@"\b(wybrałem|wybrałam|zdecydowałem|zdecydowałam|zastosowałem|zastosowałam|użyłem|użyłam|I chose|I used)\b");
                bool alternatywa = Pasuje(s,@"\b(zamiast|alternatyw|alternatywnie|można spróbować|proponuję|instead|alternative)\b");
                bool blokada = m.Typ=="blokada" || Pasuje(s,@"\b(brak uprawnień|brak dostępu|brakuje danych|brak danych|permission denied|timed out|timeout)\b");
                for(int i=0;i<Metody.GetLength(0);i++) if(Pasuje(s,Metody[i,1])) slad.Metody.Add(Metody[i,0]);
                var nazwana=Regex.Match(s,@"(?:^|\s)(?:metoda|technika|method):\s*([^.;\r\n]{3,100})",RegexOptions.IgnoreCase);
                if(nazwana.Success) slad.Metody.Add("Deklarowana: "+nazwana.Groups[1].Value.Trim());
                if(odmowa) slad.Rodzaj="Sygnał odmowy";
                else if(blokada) slad.Rodzaj="Sygnał przeszkody";
                else if(alternatywa) slad.Rodzaj="Proponowana alternatywa";
                else if(decyzja) slad.Rodzaj="Deklaracja decyzji / działania";
                else if(powod) slad.Rodzaj="Podane uzasadnienie — sprawdź kontekst";
                if(odmowa || blokada || alternatywa || decyzja || powod || slad.Metody.Count>0) wynik.Add(slad);
            }
        }
        return wynik;
    }
    static string H(object s) { return System.Net.WebUtility.HtmlEncode(Convert.ToString(s)); }
    static string K(Msg m) { return "m" + BitConverter.ToString(Encoding.UTF8.GetBytes(m.Projekt+"\n"+m.Id)).Replace("-",""); }
    static string Link(Msg m) {return "<a href='#"+K(m)+"'>"+H(m.Id)+"</a>";}
    static string Box(string title,string body,string cls) {return "<div class='node "+cls+"'><b>"+H(title)+"</b><div>"+body+"</div></div>";}
    static List<List<Msg>> Watki(List<Msg> dane) {
        var wynik=new List<List<Msg>>(); var reszta=new HashSet<Msg>(dane);
        while(reszta.Count>0) {
            var w=new List<Msg>{reszta.First()};reszta.Remove(w[0]);bool zmiana;
            do {zmiana=false;foreach(var m in reszta.ToList()) if(w.Any(x=>Powiazane(x,m))) {w.Add(m);reszta.Remove(m);zmiana=true;}} while(zmiana);
            wynik.Add(w.OrderBy(m=>m.Czas).ThenBy(m=>m.Nr).ToList());
        }
        return wynik;
    }
    public static string Raport(IEnumerable<Msg> dane,string projekt,string agent) {
        var lista=Zakres(dane,projekt,agent);var slady=Analizuj(lista,agent);var watki=Watki(lista);
        var b=new StringBuilder("<!doctype html><html lang='pl'><head><meta charset='utf-8'><meta http-equiv='X-UA-Compatible' content='IE=edge'><title>Obserwatorium — analiza pracy</title><style>");
        b.Append("body{font:15px Segoe UI,Arial;margin:24px;background:#f3f6fa;color:#172238}a{color:#165a9f;overflow-wrap:break-word;word-wrap:break-word}h1{font-size:25px}h2{margin-top:32px}table{border-collapse:collapse;width:100%;background:white;table-layout:fixed}td,th{border:1px solid #c9d4e0;padding:10px;text-align:left;vertical-align:top;word-wrap:break-word}th{background:#e6edf5}pre{white-space:pre-wrap;word-wrap:break-word;font:14px Consolas}blockquote{border-left:4px solid #6f8eaf;margin:8px 0;padding:10px;background:#fff}.node{border:2px solid #7594b5;border-radius:8px;padding:12px;margin:8px 0;background:white}.node div{margin-top:8px}.hyp{border-style:dashed;background:#fff7e7}.decision{background:#e3edf9}.arrow{text-align:center;font-weight:bold}.source{background:white;border:1px solid #ccd6e0;margin:16px 0;padding:16px}.muted{color:#526276}.badge{display:inline-block;padding:8px;margin:4px;background:#e0eaf5}input{padding:10px;width:65%}button{padding:10px} .branch td{width:50%} @media print{input,button{display:none} .source{page-break-inside:avoid}}</style></head><body>");
        b.Append("<h1>Obserwatorium — analiza pracy agentów</h1><p>").Append(H(projekt??"Wszystkie projekty")).Append(" · ").Append(H(agent??"Wszyscy agenci")).Append(" · ").Append(H(DateTime.Now.ToString("yyyy-MM-dd HH:mm"))).Append("</p>");
        b.Append("<p>Analiza lokalna, regułowa (PL/EN), bez wysyłania treści do modelu. Wykrywa sygnały w tekście; cytaty mogą zawierać relacje cudzych wypowiedzi, plany lub negacje. Nie stanowi rekonstrukcji ukrytych myśli. Przyczyna decyzji jest ustalona tylko w zakresie jawnego uzasadnienia. Brak trafienia reguły nie oznacza braku decyzji.</p><p><a href='#decyzje'>Decyzje i odmowy</a> · <a href='#metody'>Porównanie metod</a> · <a href='#grafy'>Grafy i alternatywy</a> · <a href='#zrodla'>Pełne wiadomości i kontekst</a></p>");
        b.Append("<div class='badge'>Wiadomości: ").Append(lista.Count).Append("</div><div class='badge'>Wątki: ").Append(watki.Count).Append("</div><div class='badge'>Sygnały: ").Append(slady.Count).Append("</div><div class='badge'>Sygnały odmowy: ").Append(slady.Count(s=>s.Rodzaj=="Sygnał odmowy")).Append("</div>");
        b.Append("<h2 id='decyzje'>Decyzje, powody i odmowy</h2><p>Cytaty stanowią dowody tekstowe. Powiązanie kolejnych wypowiedzi jest kontekstem, nie dowodem przyczynowości. Jeśli uzasadnienia nie ma w źródłach, przyczyna pozostaje nieustalona.</p>");
        foreach(var s in slady.Where(s=>s.Rodzaj!="Wzmianka o metodzie")) b.Append("<div class='source'><b>").Append(H(s.Rodzaj)).Append(" · ").Append(H(s.Zrodlo.Od)).Append("</b><blockquote>").Append(H(s.Cytat)).Append("</blockquote>Źródło: ").Append(Link(s.Zrodlo)).Append("</div>");
        if(!slady.Any(s=>s.Rodzaj!="Wzmianka o metodzie")) b.Append("<p>Nie wykryto pasujących deklaracji. Sprawdź pełne wiadomości poniżej.</p>");
        b.Append("<h2 id='metody'>Porównanie metod</h2><p>Liczymy wątki ze wzmianką o metodzie, nie potwierdzone użycia. FIN dotyczy całego wątku i autora wzmianki; wynik nie dowodzi skuteczności konkretnej metody. Różne zadania mogą być nieporównywalne. Kilka metod może współwystępować.</p><table><tr><th>Metoda / agent / projekt</th><th>Wątki i wyniki FIN</th><th>Dowody i uzasadnienia</th></tr>");
        foreach(var g in slady.SelectMany(s=>s.Metody.Select(m=>new {Metoda=m,Slad=s})).GroupBy(x=>new{x.Metoda,x.Slad.Zrodlo.Od,x.Slad.Zrodlo.Projekt})) {
            var zrodla=g.Select(x=>x.Slad.Zrodlo).Distinct().ToList();var powiazane=watki.Where(w=>w.Any(zrodla.Contains)).ToList();
            var wyniki=powiazane.Select(w=>w.LastOrDefault(m=>m.Typ=="fin" && m.Od==g.Key.Od)).ToList();
            int pozytywne=wyniki.Count(m=>m!=null&&m.Wynik=="pozytywny");
            b.Append("<tr><td>").Append(H(g.Key.Metoda+" / "+g.Key.Od+" / "+g.Key.Projekt)).Append("</td><td>Wątki: ").Append(powiazane.Count).Append("<br>FIN pozytywne: ").Append(pozytywne).Append("<br>FIN negatywne: ").Append(wyniki.Count(m=>m!=null&&m.Wynik=="negatywny")).Append("<br>Niewykonane: ").Append(wyniki.Count(m=>m!=null&&m.Wynik=="niewykonane")).Append("<br>Brak rozstrzygnięcia: ").Append(wyniki.Count(m=>m==null||!(new[]{"pozytywny","negatywny","niewykonane"}).Contains(m.Wynik))).Append("<div style='background:#dce5ef'><div style='background:#368261;height:12px;width:").Append(100*pozytywne/Math.Max(1,powiazane.Count)).Append("%'></div></div></td><td>");
            foreach(var s in g.Select(x=>x.Slad)) b.Append("<blockquote>").Append(H(s.Cytat)).Append("</blockquote>").Append(Link(s.Zrodlo));
            b.Append("</td></tr>");
        }
        b.Append("</table><h2 id='grafy'>Grafy przebiegu i alternatyw</h2><p>Linia ciągła: zapisany przebieg. Linia przerywana: propozycja do sprawdzenia. Tak / nie opisuje obecność dowodu, nie wewnętrzną logikę modelu. Kolejność według czasu wiadomości.</p>");
        foreach(var w in watki) {
            var ss=slady.Where(s=>w.Contains(s.Zrodlo)).ToList();var odmowy=ss.Where(s=>s.Rodzaj=="Sygnał odmowy"||s.Rodzaj=="Sygnał przeszkody").ToList();
            b.Append("<h3>").Append(H(w[0].Projekt+" · "+P.TytulWiadomosci(w[0]))).Append("</h3>");
            b.Append(Box("Cel / początek dostępnego kontekstu",Link(w[0])+"<blockquote>"+H(Tekst(w[0]))+"</blockquote>",""));
            foreach(var m in w.Skip(1)) {
                var sygnaly=ss.Where(s=>s.Zrodlo==m).ToList();
                b.Append("<div class='arrow'>↓ następna wiadomość</div>").Append(Box(m.Od+" · "+m.Typ+" · "+m.Czas.ToString("dd.MM HH:mm"),Link(m)+"<br>"+H(P.TytulWiadomosci(m))+string.Join("",sygnaly.Select(s=>"<blockquote>"+H(s.Rodzaj+": "+s.Cytat)+"</blockquote>")),""));
            }
            b.Append("<div class='arrow'>↓ ocena dostępnych dowodów</div>").Append(Box("Czy zapis zawiera sygnał odmowy lub przeszkody?",odmowy.Count>0?"TAK — "+string.Join(", ",odmowy.Select(s=>Link(s.Zrodlo)).Distinct()):"NIE WYKRYTO — przyczyna może nie być zapisana lub rozpoznana.","decision"));
            var fin=w.Where(m=>m.Typ=="fin"&&(agent==null||m.Od==agent)).ToList();
            b.Append(Box("Czy zapisano wynik FIN?",fin.Count==0?"NIE — wynik nieustalony": "TAK — "+string.Join("<br>",fin.Select(m=>H(m.Od+": "+m.Wynik)+" · "+Link(m))),"decision"));
            bool wyjasnienie=ss.Any(s=>s.Rodzaj=="Proponowana alternatywa"||s.Rodzaj.StartsWith("Podane uzasadnienie")||Regex.IsMatch(s.Cytat,@"\b(ponieważ|because|ze względu na)\b",RegexOptions.IgnoreCase));
            b.Append(Box("Czy wykryto uzasadnienie lub zapisaną alternatywę?",wyjasnienie?"TAK — sprawdź cytaty i kontekst poniżej.":"NIE WYKRYTO — przyczyna pozostaje nieustalona.","decision"));
            b.Append("<div class='arrow'>↙ TAK &nbsp;&nbsp;&nbsp;&nbsp; NIE ↘</div><table class='branch'><tr><td>").Append(Box("Ścieżka TAK — zapisane wyjaśnienia i alternatywy",string.Join("",ss.Where(s=>s.Rodzaj=="Proponowana alternatywa"||s.Rodzaj.StartsWith("Podane uzasadnienie")||Regex.IsMatch(s.Cytat,@"\b(ponieważ|because|ze względu na)\b",RegexOptions.IgnoreCase)).Select(s=>"<blockquote>"+H(s.Cytat)+"</blockquote>"+Link(s.Zrodlo)))+"<p>Sprawdź zgodność deklaracji z wynikiem i kontrolami w źródłach.</p>","hyp")).Append("</td><td>");
            string propozycja=odmowy.Count>0?"Ustal brakujący warunek na podstawie wskazanej odmowy. Doprecyzuj dane, zakres lub uprawnienia; porównaj dostępne, dopuszczalne metody na tym samym zadaniu.":"Poproś o krótkie uzasadnienie wyboru metody i kryterium sukcesu. Porównaj alternatywną metodę na tym samym zadaniu z tymi samymi kontrolami.";
            b.Append(Box("Ścieżka NIE — hipoteza kolejnego kroku",H(propozycja)+"<p>Propozycja regułowa, niesprawdzona. Nie oznacza, że będzie skuteczniejsza. Podstawa: "+string.Join(", ",(odmowy.Count>0?odmowy.Select(s=>s.Zrodlo):new[]{w[0]}).Distinct().Select(Link))+"</p>","hyp")).Append("</td></tr></table>");
        }
        b.Append("<h2 id='zrodla'>Pełne wiadomości i kontekst</h2><p>Źródła obejmują tekst, ograniczenia, kontrole i zapisane części kontekstu. Załączniki są opisane, nie są automatycznie otwierane ani wykonywane.</p><input id='q' aria-label='Szukaj w wiadomościach' placeholder='Szukaj tekstu, agenta, projektu lub identyfikatora'><button onclick='filtruj()'>Szukaj</button><button onclick=\"document.getElementById('q').value='';filtruj()\">Pokaż wszystkie</button><p id='licznik'></p>");
        foreach(var m in lista) {
            b.Append("<div class='source message' id='").Append(K(m)).Append("'><h3>").Append(H(P.TytulWiadomosci(m))).Append("</h3><p>").Append(H(m.Projekt+" · "+m.Czas.ToString("yyyy-MM-dd HH:mm:ss")+" · "+m.Od+" → "+m.Do+" · "+m.Typ)).Append("</p><p>ID: ").Append(H(m.Id)).Append(" · logiczny: ").Append(H(m.Logiczne)).Append(" · wątek: ").Append(H(m.WatekPole)).Append("</p><p>Powiązane: ").Append(string.Join(" · ",lista.Where(x=>x!=m&&Powiazane(x,m)).Select(Link))).Append("</p><pre>").Append(H(Tekst(m))).Append("</pre><p>Wynik FIN: ").Append(H(m.Wynik)).Append("</p><pre>Kontrole:\n").Append(H(string.Join("\n",m.Kontrole))).Append("\nPliki:\n").Append(H(string.Join("\n",m.Pliki))).Append("</pre>");
            foreach(var c in m.Czesci.Where(c=>P.S(c,"rodzaj")!="tekst")) b.Append("<pre>Kontekst: ").Append(H(P.Js().Serialize(c))).Append("</pre>");
            b.Append("<a href='#grafy'>Wróć do grafów</a></div>");
        }
        b.Append("<script>function filtruj(){var q=document.getElementById('q').value.toLowerCase(),ds=document.getElementsByTagName('div'),n=0;for(var i=0;i<ds.length;i++){if((' '+ds[i].className+' ').indexOf(' message ')>=0){var ok=(ds[i].textContent||ds[i].innerText||'').toLowerCase().indexOf(q)>=0;ds[i].style.display=ok?'':'none';if(ok)n++;}}document.getElementById('licznik').innerText='Widoczne wiadomości: '+n;}document.onclick=function(e){var a=(e||window.event).target||(e||window.event).srcElement;while(a&&a.tagName!='A')a=a.parentNode;if(a){var h=a.getAttribute('href');if(h&&h.indexOf('#m')==0){var el=document.getElementById(h.substring(1));if(el)el.style.display='';}}};</script></body></html>");
        return b.ToString();
    }
    public static void Pokaz(IWin32Window owner, IEnumerable<Msg> dane,string projekt,string agent) {
        var html=Raport(dane,projekt,agent);
        using(var f=new Form{Text="Obserwatorium — wiadomości, decyzje i metody",Width=1200,Height=800,StartPosition=FormStartPosition.CenterParent}) {
            var browser=new WebBrowser{Dock=DockStyle.Fill,AllowWebBrowserDrop=false,IsWebBrowserContextMenuEnabled=false,ScriptErrorsSuppressed=false,WebBrowserShortcutsEnabled=false};
            browser.Navigating+=(s,e)=>{if(e.Url!=null&&e.Url.Scheme!="about")e.Cancel=true;};
            browser.NewWindow+=(s,e)=>{e.Cancel=true;};
            var save=new Button{Text="Eksportuj pełną analizę HTML…",AutoSize=true,Dock=DockStyle.Top};
            save.Click+=(s,e)=>{using(var d=new SaveFileDialog{Filter="Raport HTML|*.html",FileName="obserwatorium-analiza.html"}) if(d.ShowDialog(f)==DialogResult.OK) try{File.WriteAllText(d.FileName,html,new UTF8Encoding(false));}catch(Exception ex){MessageBox.Show(f,ex.Message,"Eksport analizy");}};
            f.Controls.Add(browser);f.Controls.Add(save);browser.DocumentText=html;f.ShowDialog(owner);
        }
    }
}
}
