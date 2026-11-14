using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Collections.Generic;
namespace Ecxp {
class TestMigracji {
    static int bad;
    static void Check(bool v,string t){Console.WriteLine((v?"PASS ":"FAIL ")+t);if(!v)bad++;}
    static void Zatrzymaj(Klient k,string root){k.Wywolaj("stos_zamknij",null);k.Dispose();var deadline=DateTime.UtcNow.AddSeconds(15);while(DateTime.UtcNow<deadline){Thread.Sleep(200);using(var m=new Mutex(false,@"Local\ECXPD-"+Ramka.NazwaPotoku(root))){bool ok=false;try{ok=m.WaitOne(0);}catch(AbandonedMutexException){ok=true;}if(ok){m.ReleaseMutex();return;}}}throw new Exception("Stos nie zakończył pracy");}
    public static int Main(string[] args){
        P.Root=Path.GetFullPath(args[0]);var source=P.Root;var dest=Path.GetFullPath(args[1]);Directory.CreateDirectory(source);
        using(var k=Klient.Polacz(P.Wlasciciel,"konsola",true)) {
            k.Wywolaj("przypisz",P.D("nazwa","codex-migracja","projekt","test","modul","test","sesja","test"));
            var sent=(Dictionary<string,object>)k.Wywolaj("wyslij",P.D("do","codex-migracja","projekt","test","modul","test","typ","informacja","temat","Migracja testowa","tresc","Syntetyczna treść Żółć","bez_kopii",true));
            Check(P.S(sent,"id").Length>0,"message written through real stack");
            using(var agent=Klient.Polacz("*","mcp",false)) {
                bool refused=false;try{agent.Wywolaj("stos_zamknij",P.D("ja","codex-migracja"));}catch(EcxpException){refused=true;}Check(refused,"agent cannot stop stack");
                refused=false;try{agent.Wywolaj("definiuj_role",P.D("ja","codex-migracja","nazwa","rola-test"));}catch(EcxpException){refused=true;}Check(refused,"agent cannot define operator roles");
            }
            k.Wywolaj("definiuj_role",P.D("nazwa","analityk-migracji","obowiazki","sprawdza kopię","wynik","raport","granice","bez danych spraw"));
            var cfg=P.D("odebrane",Path.Combine(source,"widoki","odb"),"wyslane",Path.Combine(source,"widoki","wys"));Konfiguracja.Zapisz(Konfiguracja.Plik,P.Js().Serialize(cfg),null);Widok.Odswiez();
            Check(Directory.GetFiles(Konfiguracja.Widok("Wysłane"),"*.txt").Length>=2,"custom sent directory is populated");
            Zatrzymaj(k,source);
        }
        int count=Konfiguracja.KopiujSkrzynke(source,dest,true);Check(count>0,"offline migration completed after graceful stop");P.Root=Konfiguracja.RozwiazRoot(source);
        using(var k=Klient.Polacz(P.Wlasciciel,"konsola",true)) {
            Check(P.Root==dest,"stack starts at redirected location with original installation");
            var oldView=Path.Combine(source,"widoki","wys");var before=Directory.GetFiles(oldView,"*.txt").Select(f=>f+"|"+P.ShaPliku(f)).ToArray();
            Widok.Odswiez();Check(before.SequenceEqual(Directory.GetFiles(oldView,"*.txt").Select(f=>f+"|"+P.ShaPliku(f))),"view regeneration preserves source files after migration");
            using(var agent=Klient.Polacz("*","mcp",false)) {
                var received=(Dictionary<string,object>)agent.Wywolaj("odbierz",P.D("ja","codex-migracja"));var messages=P.Lista(received,"wiadomosci");
                Check(messages.Any(m=>P.S(m,"tresc")=="Syntetyczna treść Żółć"&&P.S(m,"integralnosc")=="ok"),"migrated message received with valid integrity");
            }
            var roles=(Dictionary<string,object>)k.Wywolaj("trasy",P.D("projekt","test"));Check(P.L(roles,"role_dostepne").Contains("analityk-migracji"),"custom role survives migration");Zatrzymaj(k,dest);
        }
        if(Agenci.Program("codex").Length>0) {
            OknoUstawien.SprawdzToml("[mcp_servers.ecxp]\ncommand = 'C:\\example\\ecxp-mcp.exe'\n");Check(true,"Codex validates TOML in isolated home");
            bool invalid=false;try{OknoUstawien.SprawdzToml("[broken");}catch(EcxpException){invalid=true;}Check(invalid,"invalid TOML refused before saving");
        }
        Console.WriteLine("RESULT: "+bad+" failed");return bad==0?0:1;
    }
}
}
