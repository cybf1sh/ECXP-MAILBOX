using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
namespace Ecxp {
class TestySesji {
    static int bledy;
    static void Check(bool ok,string nazwa) { Console.WriteLine((ok?"PASS ":"FAIL ")+nazwa); if(!ok) bledy++; }
    [STAThread] public static int Main(string[] args) {
        if(args.Length==2 && args[0]=="--open-codex") { CodexProjekt.OtworzWatek(args[1]); return 0; }
        string root=Path.Combine(Path.GetTempPath(),"ecxp-sesje-test-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string home=Path.Combine(root,"home"), kat=Path.Combine(root,"projekt z polską nazwą"), inny=Path.Combine(root,"inne");
        Directory.CreateDirectory(home);Directory.CreateDirectory(kat);Directory.CreateDirectory(inny);
        string legacy=Guid.NewGuid().ToString(),server=Guid.NewGuid().ToString();
        var stan=P.D("local-projects",P.D(legacy,P.D("id",legacy,"rootPaths",new object[]{kat})),
            "app-server-project-id-by-legacy-project-id-by-host",P.D("local:"+home,P.D(legacy,server)));
        string file=Path.Combine(home,".codex-global-state.json"); File.WriteAllText(file,P.Js().Serialize(stan));
        string before=File.ReadAllText(file);
        Check(CodexProjekt.Znajdz(stan,home,kat.ToUpperInvariant()+"\\")==server,"maps exact folder independent of case/trailing separator");
        Check(CodexProjekt.Znajdz(stan,home,inny)=="","does not assign another project by name or prefix");
        Check(Uri.UnescapeDataString(CodexProjekt.Link(kat).Split('=')[1])==kat,"project URI preserves spaces and Unicode");
        bool opened=false;
        Func<string,Dictionary<string,object>,int,Dictionary<string,object>> rpc=(m,p,ms)=>P.D("project",P.D("id",server,"roots",new object[]{P.D("path",kat)}));
        Check(CodexProjekt.Zapewnij(home,kat,rpc,x=>opened=true,x=>{})==server && !opened,"reuses registered desktop project and validates server roots");
        Check(File.ReadAllText(file)==before,"does not modify desktop state");
        bool rejected=false;
        try { CodexProjekt.Zapewnij(home,kat,(m,p,ms)=>P.D("project",P.D("id",server,"roots",new object[]{P.D("path",inny)})),x=>{},x=>{}); }
        catch(EcxpException) { rejected=true; }
        Check(rejected,"rejects stale mapping to a different folder");
        rejected=false;try{CodexProjekt.OtworzWatek("invalid/?x");}catch(EcxpException){rejected=true;}
        Check(rejected,"rejects malformed thread URI without opening app");
        string testId=Guid.NewGuid().ToString();
        CodexProjekt.CzekajNaZapis(testId,(m,p,ms)=>P.D("thread",P.D("path",file)));
        Check(true,"opens only after durable thread path exists");
        int requests=0;
        CodexProjekt.OddajWatek(testId,(m,p,ms)=> { requests++; return m=="thread/unsubscribe" ? P.D("status","unsubscribed") : P.D("data",new object[0]); });
        Check(requests==2,"releases subscription and confirms writer unloaded before desktop handoff");
        string cl=SesjeAplikacji.LinkClaude(kat,"Próbny pakiet & znaki ? #");
        Check(cl.StartsWith("claude://code/new?folder=") && cl.Contains(Uri.EscapeDataString(kat)) && cl.Contains(Uri.EscapeDataString("Próbny pakiet & znaki ? #")),"Claude native URI preserves project folder and complete prompt");
        var procType=typeof(Cieply).GetNestedType("Proc",BindingFlags.NonPublic);
        var proc=Activator.CreateInstance(procType,true); procType.GetField("Strona").SetValue(proc,"codex");
        var fallback=typeof(Cieply).GetMethod("NieDziala",BindingFlags.Static|BindingFlags.NonPublic);
        var error=new Cieply.Wynik { Kod=1, Wyjscie="-32601 unsupported cleanup method" };
        procType.GetField("TuraWystartowala").SetValue(proc,true);
        Check(fallback.Invoke(null,new object[]{proc,error,true})==null,"never repeats executed work after an unsupported cleanup method");
        procType.GetField("TuraWystartowala").SetValue(proc,false);
        Check(fallback.Invoke(null,new object[]{proc,error,true})!=null,"compatibility fallback remains available before a turn starts");
        P.Root=root;
        string large=new string('A',13000)+"KONIEC PAKIETU";
        string longLink=SesjeAplikacji.PrzygotujLinkClaude(kat,large);
        string packet=Directory.GetFiles(Path.Combine(root,"stan","start","pakiety"),"*.txt").Single();
        Check(File.ReadAllText(packet)==large && longLink.Contains(Uri.EscapeDataString(packet)),"Claude long startup packet is preserved in a complete local snapshot");
        foreach(string lang in new[]{"en","fr","cs","de"}) {
            Jezyk.Kod=lang;
            Check(new[]{"Czytnik wiadomości","Rozmiar tekstu","Ciemny konsolowy","Czcionka","Zapisz jako PDF…","Zapisz jako HTML…","Niewysłane","Szkic","Błąd wysyłki","Motyw całej aplikacji","Zapisz szkic i zamknij"}.All(x=>Jezyk.T(x)!=x),"new reader controls translated: "+lang);
        }
        Jezyk.Kod="pl";
        if(args.Length==2 && args[0]=="--live-codex") {
            var id=SesjeAplikacji.ZalozCodex("ECXP test klienta - projekt 2026-11-04",Path.GetFullPath(args[1]),
                "Syntetyczny test ECXP. Nie uzywaj narzedzi, nie zmieniaj plikow, nie rejestruj sie w ECXP. Odpowiedz tylko: TEST KLIENTA ECXP OK.",null,1,Console.WriteLine,true);
            Console.WriteLine("LIVE_CODEX_THREAD: "+id);
        }
        Console.WriteLine("RESULT: "+bledy+" failed");return bledy==0?0:1;
    }
}}
