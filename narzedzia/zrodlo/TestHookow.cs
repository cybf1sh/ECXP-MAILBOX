using System;
using System.IO;
using System.Text;
namespace Ecxp {
class TestHookow {
    static int bad;
    static void Check(bool ok,string title) { Console.WriteLine((ok?"PASS ":"FAIL ")+title); if(!ok)bad++; }
    static string Run(string ev,string json) { var output=new StringWriter(); Check(Hooki.Uruchom(ev,new StringReader(json),output)==0,ev+" exit"); return output.ToString(); }
    public static int Main(string[] args) {
        if(args[0]=="--streams") {for(int i=0;i<2000;i++){Console.Error.WriteLine(new string('x',100));}Console.WriteLine("OUTPUT-END");return 0;}
        if(args[0]=="--failure") return 7;
        if(args[0]=="--timeout") {System.Threading.Thread.Sleep(30000);return 0;}
        P.Root=Path.GetFullPath(args[0]); Directory.CreateDirectory(P.Root);
        var file=Path.Combine(P.Root,"settings.json"); var exe=@"C:\Program Files\ECXP\ecxp-mcp.exe";
        File.WriteAllText(file,"{\"custom\":true,\"hooks\":{\"SessionStart\":[{\"hooks\":[{\"type\":\"command\",\"command\":\"echo custom\"}]}]}}");
        Check(Instalator.ZapiszHookiClaude(file,exe,false),"initial registration");
        var first=File.ReadAllText(file); var stamp=File.GetLastWriteTimeUtc(file);
        Check(first.Contains("echo custom")&&first.Contains("custom"),"unrelated configuration preserved");
        File.SetAttributes(file,FileAttributes.ReadOnly);
        try { Check(!Instalator.ZapiszHookiClaude(file,exe,false)&&File.ReadAllText(file)==first&&File.GetLastWriteTimeUtc(file)==stamp,"read-only correct settings require no write"); }
        finally { File.SetAttributes(file,FileAttributes.Normal); }
        Check(Instalator.ZapiszHookiClaude(file,@"D:\ECXP\ecxp-mcp.exe",false)&&!File.ReadAllText(file).Contains("Program Files"),"installation path migration");
        Check(Instalator.ZapiszHookiClaude(file,exe,true)&&!File.ReadAllText(file).Contains("ecxp-mcp")&&File.ReadAllText(file).Contains("echo custom"),"unregister preserves other hooks");
        File.WriteAllText(file,"{invalid"); bool refused=false; try {Instalator.ZapiszHookiClaude(file,exe,false);}catch {refused=true;}
        Check(refused&&File.ReadAllText(file)=="{invalid","invalid JSON preserved");
        File.WriteAllText(file,"null"); refused=false;try{Instalator.ZapiszHookiClaude(file,exe,false);}catch{refused=true;}
        Check(refused&&File.ReadAllText(file)=="null","JSON null is not overwritten");
        File.WriteAllText(file,P.Js().Serialize(P.D("hooks",P.D("Stop",new object[]{P.D("matcher","", "hooks",new object[]{P.D("type","command","command","echo preserve-me"),P.D("type","command","command","\""+exe+"\" hook Stop")})}))));
        Instalator.ZapiszHookiClaude(file,exe,true);
        Check(File.ReadAllText(file).Contains("preserve-me")&&!File.ReadAllText(file).Contains("ecxp-mcp"),"unrelated hook in same group preserved");
        // Existing user configuration is checked without modifying it.
        if(args.Length>1) Check(!Instalator.ZapiszHookiClaude(args[1],args[2],false),"live settings already correct");
        var cache=Path.Combine(P.Root,"stan","hook-sesje"); Directory.CreateDirectory(cache); File.WriteAllText(Path.Combine(cache,"test-session.txt"),"claude-hook-test");
        var json=P.Js().Serialize(P.D("session_id","test-session","prompt","Kontrola komunikacji testowej"));
        Check(Run("SessionStart",json).Length==0,"no manual watcher instruction");
        Run("UserPromptSubmit",json);
        var inputs=Directory.GetFiles(Path.Combine(P.Root,"stan","wejscia"),"*.json");
        Check(inputs.Length==1&&File.ReadAllText(inputs[0]).Contains("Kontrola komunikacji testowej"),"prompt mirrored exactly to input queue");
        Check(Run("Stop",json).Length==0,"idle stop does not request manual watcher");
        Check(Run("Stop",P.Js().Serialize(P.D("session_id","test-session","stop_hook_active",true))).Length==0,"stop does not loop");
        Check(first.Contains("asyncRewake")&&first.Contains("hook Observe"),"automatic wake hooks registered");
        var queue=Path.Combine(P.Root,P.Folder("claude-hook-test")); Directory.CreateDirectory(queue);
        var msg=Path.Combine(queue,"one.json"); File.WriteAllText(msg,P.Js().Serialize(P.D("do","claude-hook-test","temat","same")));
        var output=new StringWriter();
        Check(Hooki.Obserwuj("claude-hook-test","test-session",output,TimeSpan.Zero)==2&&output.ToString().Contains("ecxp_odbierz"),"pending mail wakes Claude");
        Check(File.Exists(msg),"watcher does not receive or ACK mail");
        Check(Hooki.Obserwuj("claude-hook-test","test-session",new StringWriter(),TimeSpan.Zero)==0,"same batch does not cause wake loop");
        File.WriteAllText(Path.Combine(queue,"two.json"),P.Js().Serialize(P.D("do","claude-hook-test","temat","same")));
        Check(Hooki.Obserwuj("claude-hook-test","test-session",new StringWriter(),TimeSpan.Zero)==2,"new message with same subject wakes again");
        File.WriteAllText(Path.Combine(queue,"other.json"),P.Js().Serialize(P.D("do","claude-other","temat","other")));
        Check(Hooki.Obserwuj("claude-hook-test","test-session",new StringWriter(),TimeSpan.Zero)==0,"other recipient does not wake this session");
        using(var held=new FileStream(Path.Combine(P.Root,"stan","czuwanie","claude-hook-test.lock"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
            Check(Hooki.Obserwuj("claude-hook-test","test-session",new StringWriter(),TimeSpan.Zero)==0,"only one watcher per address");
        File.Delete(msg);
        Check(Hooki.Obserwuj("claude-hook-test","test-session",new StringWriter(),TimeSpan.Zero)==0,"partial receipt does not create false notification");
        var sender=new System.Threading.Thread(()=>{System.Threading.Thread.Sleep(150);File.WriteAllText(Path.Combine(queue,"three.json"),P.Js().Serialize(P.D("do","claude-hook-test","temat","later")));});
        sender.Start(); Check(Hooki.Obserwuj("claude-hook-test","test-session",new StringWriter(),TimeSpan.FromSeconds(3))==2,"idle watcher wakes on later file arrival"); sender.Join();
        var self=System.Reflection.Assembly.GetExecutingAssembly().Location;
        Check(Instalator.Uruchom(self,"--streams").Contains("OUTPUT-END"),"CLI drains both streams without deadlock");
        bool failure=false;try{Instalator.Uruchom(self,"--failure");}catch(IOException){failure=true;}
        Check(failure,"CLI failure cannot report success");
        bool timedout=false;var watch=System.Diagnostics.Stopwatch.StartNew();try{Instalator.Uruchom(self,"--timeout",false,200);}catch(TimeoutException){timedout=true;}
        Check(timedout&&watch.Elapsed.TotalSeconds<10,"CLI timeout bounded");
        return bad==0?0:1;
    }
}
}
