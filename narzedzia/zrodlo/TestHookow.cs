using System;
using System.IO;
using System.Text;
namespace Ecxp {
class TestHookow {
    static int bad;
    static void Check(bool ok,string title) { Console.WriteLine((ok?"PASS ":"FAIL ")+title); if(!ok)bad++; }
    static string Run(string ev,string json) { var output=new StringWriter(); Check(Hooki.Uruchom(ev,new StringReader(json),output)==0,ev+" exit"); return output.ToString(); }
    public static int Main(string[] args) {
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
        // Existing user configuration is checked without modifying it.
        if(args.Length>1) Check(!Instalator.ZapiszHookiClaude(args[1],args[2],false),"live settings already correct");
        var cache=Path.Combine(P.Root,"stan","hook-sesje"); Directory.CreateDirectory(cache); File.WriteAllText(Path.Combine(cache,"test-session.txt"),"claude-hook-test");
        var json=P.Js().Serialize(P.D("session_id","test-session","prompt","Kontrola komunikacji testowej"));
        Check(Run("SessionStart",json).Contains("ECXP"),"session reminder");
        Run("UserPromptSubmit",json);
        var inputs=Directory.GetFiles(Path.Combine(P.Root,"stan","wejscia"),"*.json");
        Check(inputs.Length==1&&File.ReadAllText(inputs[0]).Contains("Kontrola komunikacji testowej"),"prompt mirrored exactly to input queue");
        Check(Run("Stop",json).Contains("block"),"stop reminds to watch mailbox");
        Check(Run("Stop",P.Js().Serialize(P.D("session_id","test-session","stop_hook_active",true))).Length==0,"stop does not loop");
        return bad==0?0:1;
    }
}
}
