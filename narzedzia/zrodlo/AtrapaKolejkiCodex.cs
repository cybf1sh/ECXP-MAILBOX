// Atrapa używana wyłącznie przez TestPotwierdzen. Nie wykonuje poleceń ani nie uruchamia agenta.
using System; using System.Text;
class AtrapaKolejkiCodex
{
    static int Main(string[] a)
    {
        string id="",text="";
        for(int i=0;i+1<a.Length;i++) {if(a[i]=="--thread")id=a[i+1];if(a[i]=="--message")text=a[i+1];}
        if(text=="ERROR"){Console.WriteLine("Warning: nothing queued");return 0;}
        if(text=="NO_RESUME") Console.Error.WriteLine("Warning: message was queued, but could not resume thread: start a local daemon");
        Console.WriteLine("TEXT_BASE64:"+Convert.ToBase64String(Encoding.UTF8.GetBytes(text)));
        Console.WriteLine("Queued message aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee for thread "+(text=="WRONG_THREAD"?"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee":id)+".");
        return 0;
    }
}
