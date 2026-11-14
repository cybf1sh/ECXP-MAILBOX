// Ustawienia stanowiska i skrzynki. Zapis atomowy, kopie oraz kontrola konfliktów.
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.Reflection;
namespace Ecxp {
static class Konfiguracja {
    public static string Instalacja { get { return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location); } }
    public static string Plik { get { return Path.Combine(P.Root, "stan", "konfiguracja.json"); } }
    public static Dictionary<string,object> Czytaj(string plik) {
        if (!File.Exists(plik)) return new Dictionary<string,object>();
        var d=P.Js().Deserialize<Dictionary<string,object>>(File.ReadAllText(plik,Encoding.UTF8));
        if(d==null) throw new EcxpException("Pusta konfiguracja: "+plik); return d;
    }
    public static string Pelna(string p) {
        if(string.IsNullOrWhiteSpace(p) || !Path.IsPathRooted(p) || Path.GetPathRoot(p).Length<3 || p.IndexOfAny(new[]{'\r','\n','"'})>=0)
            throw new EcxpException("Podaj pełną ścieżkę, np. D:\\ECXP.");
        var full=Path.GetFullPath(p);return full.Length>Path.GetPathRoot(full).Length?full.TrimEnd('\\','/'):full;
    }
    public static void Zapisz(string plik,string tekst,string oczekiwany) {
        Directory.CreateDirectory(Path.GetDirectoryName(plik));
        using(var l=new FileStream(plik+".edycja",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
            var obecny=File.Exists(plik)?File.ReadAllText(plik,Encoding.UTF8):"";
            if(oczekiwany!=null && obecny!=oczekiwany) throw new EcxpException("Plik zmienił się w innym programie. Otwórz go ponownie.");
            string tmp=plik+"."+Guid.NewGuid().ToString("N")+".tmp";
            File.WriteAllText(tmp,tekst,new UTF8Encoding(false));
            if(File.Exists(plik)) File.Replace(tmp,plik,plik+"."+DateTime.Now.ToString("yyyyMMdd-HHmmss-fffffff")+".bak");
            else File.Move(tmp,plik);
        }
    }
    public static string Sciezka(string klucz,string domyslna) { var v=P.S(Czytaj(Plik),klucz); return v.Length==0?domyslna:Pelna(v); }
    public static int Liczba(string klucz,int domyslna,int min,int max) {
        int n;var s=P.S(Czytaj(Plik),klucz); if(s.Length==0)return domyslna;
        if(!int.TryParse(s,out n)||n<min||n>max)throw new EcxpException("Nieprawidłowe ustawienie: "+klucz); return n;
    }
    public static string Komunikacja { get {
        var wybrany=P.S(Czytaj(Plik),"komunikacja");if(wybrany.Length>0)return Pelna(wybrany);
        var istniejace=P.Projekty().Select(d=>P.S(d,"repozytorium")).Where(p=>p.Length>0&&Path.IsPathRooted(p)).Select(p=>Path.Combine(p,"komunikacja-sesji")).Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return istniejace.Count==1?istniejace[0]:Path.Combine(P.Root,"komunikacja-sesji");
    } }
    public static string StanModulow { get { return Sciezka("stan_modulow",Path.Combine(P.Root,"STAN-MODULOW.md")); } }
    public static string Widok(string folder) { return Sciezka(folder=="Odebrane"?"odebrane":"wyslane",Path.Combine(P.Root,folder)); }
    public static string PlikWidoku(string rel) {
        if(Path.IsPathRooted(rel))return rel;
        var parts=rel.Split(new[]{'\\','/'},2);return parts.Length==2?Path.Combine(Widok(parts[0]),parts[1]):Path.Combine(P.Root,rel);
    }
    public static void PrzygotujKomunikacje(string katalog) {
        katalog=Pelna(katalog);Directory.CreateDirectory(katalog);
        string plik=Path.Combine(katalog,"README.txt");
        if(!File.Exists(plik)) using(var w=new StreamWriter(new FileStream(plik,FileMode.CreateNew,FileAccess.Write),new UTF8Encoding(false)))
            w.Write("KOMUNIKACJA SESJI ECXP\r\nTu zapisuj istotne ustalenia, przekazania i wyniki dotyczące komunikacji.\r\nBez sekretów, danych spraw i pustych kontroli okresowych.\r\nWiadomości i ich stan dostarczenia pozostają w skrzynce ECXP.\r\n");
        plik=Path.Combine(katalog,"HISTORIA-I-USTALENIA-ECXP.txt");
        if(!File.Exists(plik)) using(var w=new StreamWriter(new FileStream(plik,FileMode.CreateNew,FileAccess.Write),new UTF8Encoding(false)))
            w.Write("HISTORIA I USTALENIA KOMUNIKACJI ECXP\r\nUtworzono: "+P.Teraz()+"\r\n");
    }
    public static string RozwiazRoot(string root) {
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for(int i=0;i<16;i++) {
            root=Pelna(root); if(!seen.Add(root.TrimEnd('\\')))throw new EcxpException("Pętla przekierowania skrzynki.");
            var f=Path.Combine(root,"ecxp-przeniesiono.json");if(!File.Exists(f))return root;
            root=P.S(Czytaj(f),"skrzynka");if(!Directory.Exists(root))throw new EcxpException("Nowa skrzynka jest niedostępna: "+root);
        } throw new EcxpException("Zbyt wiele przekierowań skrzynki.");
    }
    static bool Wewnatrz(string p,string root) { return p.TrimEnd('\\').Equals(root.TrimEnd('\\'),StringComparison.OrdinalIgnoreCase)||p.StartsWith(root.TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase); }
    static void BezDowiazan(string p) {
        for(var d=new DirectoryInfo(p);d!=null;d=d.Parent)
            if(d.Exists&&(d.Attributes&FileAttributes.ReparsePoint)!=0)throw new EcxpException("Migracja nie obsługuje dowiązań: "+d.FullName);
    }
    static IEnumerable<string> Pliki(string root) {
        foreach(var p in Directory.GetFileSystemEntries(root)) {
            var a=File.GetAttributes(p);if((a&FileAttributes.ReparsePoint)!=0)throw new EcxpException("Dowiązanie w źródle: "+p);
            if((a&FileAttributes.Directory)!=0) { if(new[]{".git",".worktrees","builds"}.Contains(Path.GetFileName(p)))continue; foreach(var f in Pliki(p))yield return f; }
            else if(!p.EndsWith(".edycja")&&!p.EndsWith(".blokada")&&!p.EndsWith(".tmp")&&Path.GetFileName(p)!="ecxp-przeniesiono.json")yield return p;
        }
    }
    static IEnumerable<string> Katalogi(string root) {
        foreach(var d in Directory.GetDirectories(root)) {
            if((File.GetAttributes(d)&FileAttributes.ReparsePoint)!=0)throw new EcxpException("Dowiązanie w źródle: "+d);
            if(new[]{".git",".worktrees","builds"}.Contains(Path.GetFileName(d)))continue;
            yield return d;foreach(var child in Katalogi(d))yield return child;
        }
    }
    // Kopia/import do pustego katalogu: nigdy nie scala dwóch dzienników ani nie usuwa źródła.
    // Mutexy zapobiegają startowi stosu podczas kopiowania. Czynna skrzynka jest odrzucana.
    public static int KopiujSkrzynke(string z,string doK,bool przelacz) {
        if(Pelna(z)==Path.GetPathRoot(Pelna(z))||Pelna(doK)==Path.GetPathRoot(Pelna(doK)))throw new EcxpException("Wybierz podkatalog, nie katalog główny dysku.");
        z=Pelna(z).TrimEnd('\\');doK=Pelna(doK).TrimEnd('\\');BezDowiazan(z);BezDowiazan(doK);
        if(!Directory.Exists(z)||!Directory.Exists(Path.Combine(z,"dziennik")))throw new EcxpException("Źródło nie jest skrzynką ECXP (brak dziennika).");
        if(Wewnatrz(z,doK)||Wewnatrz(doK,z))throw new EcxpException("Katalogi źródła i celu muszą być rozłączne.");
        if(Directory.Exists(doK)&&Directory.EnumerateFileSystemEntries(doK).Any())throw new EcxpException("Cel musi być pusty. Import nie scala istniejących skrzynek.");
        var locks=new List<Mutex>();
        try {
            foreach(var root in new[]{z,doK}) foreach(var name in new[]{@"Local\ECXPD-"+Ramka.NazwaPotoku(root),@"Local\ECXP-MAILBOX-"+P.Sha(root.ToLowerInvariant()).Substring(0,12)}) {
                var m=new Mutex(false,name);bool ok=false;try {ok=m.WaitOne(0);}catch(AbandonedMutexException){ok=true;}
                if(!ok){m.Dispose();throw new EcxpException("Skrzynka jest używana. Zamknij jej konsolę i stos ecxpd, a następnie ponów w osobnym oknie ustawień.");}locks.Add(m);
            }
            var files=Pliki(z).ToList();var dirs=Katalogi(z).ToList();Directory.CreateDirectory(doK);
            foreach(var dir in dirs)Directory.CreateDirectory(Path.Combine(doK,dir.Substring(z.Length+1)));
            foreach(var f in files) { var target=Path.Combine(doK,f.Substring(z.Length+1));Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(f,target,false);
                if(P.ShaPliku(f)!=P.ShaPliku(target))throw new EcxpException("Niezgodność kopii: "+f); }
            // Nie przenoś absolutnych ścieżek zależnych od starej skrzynki.
            var cfg=Path.Combine(doK,"stan","konfiguracja.json");var d=Czytaj(cfg);
            foreach(var k in new[]{"komunikacja","odebrane","wyslane","stan_modulow"}) {var p=P.S(d,k);if(p.Length>0&&Wewnatrz(p,z))d[k]=doK+p.Substring(z.Length);}
            Zapisz(cfg,P.Js().Serialize(d),null);
            if(przelacz)Zapisz(Path.Combine(z,"ecxp-przeniesiono.json"),P.Js().Serialize(P.D("skrzynka",doK,"czas",P.Teraz())),null);
            return files.Count;
        } finally {foreach(var m in locks.AsEnumerable().Reverse()){m.ReleaseMutex();m.Dispose();}}
    }
}
}
