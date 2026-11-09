// Desktop Codex posiada własne identyfikatory projektów. Rejestruje je aplikacja;
// ECXP odczytuje mapowanie i sprawdza korzeń; lokalne wpisy usuwa tylko jawna operacja usunięcia projektu.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Ecxp
{
    static class CodexProjekt
    {
        static readonly object brama = new object();
        public static string Normalizuj(string path) { return Path.GetFullPath(path).TrimEnd('\\', '/'); }
        // Tylko wpisy dotyczące jednego korzenia; wielokorzeniowe projekty wymagają ręcznego rozdzielenia.
        public static List<string> LokalneId(Dictionary<string,object> state,string path)
        {
            object value;var projects=state.TryGetValue("local-projects",out value)?value as Dictionary<string,object>:null;
            var ids=new List<string>();if(projects==null)return ids;
            foreach(var kv in projects){var p=kv.Value as Dictionary<string,object>;var roots=P.L(p,"rootPaths");
                if(!roots.Any(r=>string.Equals(Normalizuj(r),Normalizuj(path),StringComparison.OrdinalIgnoreCase)))continue;
                if(roots.Any(r=>!string.Equals(Normalizuj(r),Normalizuj(path),StringComparison.OrdinalIgnoreCase)))throw new EcxpException("Projekt Codexa obejmuje także inne katalogi. Rozdziel go przed usunięciem.");
                ids.Add(kv.Key);
            }return ids;
        }
        public static void UsunLokalneWpisy(string home,string path)
        {
            if(!Path.IsPathRooted(home))throw new EcxpException("Nieprawidłowy codexHome.");
            var file=Path.Combine(home,".codex-global-state.json");if(!File.Exists(file))return;
            var text=P.Czytaj(file);var state=P.Js().Deserialize<Dictionary<string,object>>(text);var ids=LokalneId(state,path);object value;var legacy=new HashSet<string>(ids);
            if(state.TryGetValue("local-projects",out value)&&value is Dictionary<string,object>){var projects=(Dictionary<string,object>)value;foreach(var id in ids){var mapped=P.S(projects[id] as Dictionary<string,object>,"id");if(mapped.Length>0)legacy.Add(mapped);projects.Remove(id);}}
            if(state.TryGetValue("app-server-project-id-by-legacy-project-id-by-host",out value)&&value is Dictionary<string,object>)
                foreach(var map in ((Dictionary<string,object>)value).Values.OfType<Dictionary<string,object>>())foreach(var id in legacy)map.Remove(id);
            foreach(var key in new[]{"electron-saved-workspace-roots","active-workspace-roots"})if(state.ContainsKey(key))state[key]=P.L(state,key).Where(r=>!string.Equals(Normalizuj(r),Normalizuj(path),StringComparison.OrdinalIgnoreCase)).ToList();
            if(state.TryGetValue("electron-workspace-root-labels",out value)&&value is Dictionary<string,object>){var labels=(Dictionary<string,object>)value;foreach(var key in labels.Keys.Where(r=>string.Equals(Normalizuj(r),Normalizuj(path),StringComparison.OrdinalIgnoreCase)).ToList())labels.Remove(key);}
            if(P.Czytaj(file)!=text)throw new EcxpException("Codex zmienił listę projektów. Zamknij jego okno projektu i ponów operację.");
            Pliki.Zapisz(file,state);
        }
        public static string Link(string katalog) { return "codex://new?path=" + Uri.EscapeDataString(Path.GetFullPath(katalog)); }
        public static void OtworzProjekt(string katalog)
        {
            OtworzLink(Link(katalog));
        }
        public static void OtworzWatek(string id)
        {
            Guid guid;
            if (!Guid.TryParseExact(id, "D", out guid)) throw new EcxpException("Niepoprawny identyfikator wątku Codex.");
            OtworzLink("codex://threads/" + guid.ToString("D"));
        }
        static void OtworzLink(string uri)
        {
            Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
        }
        public static void OddajWatek(string id, Func<string, Dictionary<string, object>, int, Dictionary<string, object>> rpc)
        {
            rpc("thread/unsubscribe", P.D("threadId", id), 15000);
            var czas = Stopwatch.StartNew();
            do
            {
                var r = rpc("thread/loaded/list", P.D(), 10000); object data;
                if (r != null && r.TryGetValue("data", out data) && !Sciezki(data).Contains(id)) return;
                Thread.Sleep(100);
            } while (czas.ElapsedMilliseconds < 10000);
            throw new EcxpException("Codex nie zwolnił własności wątku. Spróbuj otworzyć sesję później: " + id);
        }
        static IEnumerable<string> Sciezki(object value)
        {
            var lista = value as IEnumerable;
            if (lista == null || value is string) yield break;
            foreach (var item in lista) { var s = item as string; if (s != null) yield return s; }
        }
        public static string Znajdz(Dictionary<string, object> stan, string home, string katalog)
        {
            if (stan == null) return "";
            object p, m;
            var projekty = stan.TryGetValue("local-projects", out p) ? p as Dictionary<string, object> : null;
            var hosty = stan.TryGetValue("app-server-project-id-by-legacy-project-id-by-host", out m) ? m as Dictionary<string, object> : null;
            if (projekty == null || hosty == null) return "";
            var host = hosty.FirstOrDefault(x => x.Key.StartsWith("local:", StringComparison.Ordinal) &&
                string.Equals(Normalizuj(x.Key.Substring(6)), Normalizuj(home), StringComparison.OrdinalIgnoreCase)).Value as Dictionary<string, object>;
            if (host == null) return "";
            var ids = new HashSet<string>();
            foreach (var item in projekty)
            {
                var pr = item.Value as Dictionary<string, object>; object roots;
                if (pr == null || !pr.TryGetValue("rootPaths", out roots)) continue;
                if (!Sciezki(roots).Any(x => string.Equals(Normalizuj(x), Normalizuj(katalog), StringComparison.OrdinalIgnoreCase))) continue;
                var legacy = P.S(pr, "id"); if (legacy.Length == 0) legacy = item.Key;
                var id = P.S(host, legacy); if (id.Length > 0) ids.Add(id);
            }
            if (ids.Count > 1) throw new EcxpException("Katalog ma więcej niż jeden projekt w aplikacji Codex. Wybierz właściwy projekt w Codexie.");
            return ids.FirstOrDefault() ?? "";
        }
        static string Czytaj(string home, string katalog)
        {
            string plik = Path.Combine(home, ".codex-global-state.json");
            if (!File.Exists(plik)) return "";
            try
            {
                using (var f = new FileStream(plik, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new StreamReader(f, Encoding.UTF8))
                {
                    var js = P.Js(); js.MaxJsonLength = int.MaxValue;
                    return Znajdz(js.Deserialize<Dictionary<string, object>>(r.ReadToEnd()), home, katalog);
                }
            }
            catch (IOException) { return ""; }
            catch (ArgumentException) { return ""; } // atomowy zapis/zmiana wersji formatu: ponów w granicach limitu
        }
        public static string Zapewnij(string home, string katalog,
            Func<string, Dictionary<string, object>, int, Dictionary<string, object>> rpc,
            Action<string> otworz, Action<string> log)
        {
            if (string.IsNullOrWhiteSpace(home) || !Path.IsPathRooted(home)) throw new EcxpException("Codex nie zwrócił katalogu codexHome.");
            katalog = Path.GetFullPath(katalog);
            if (!Directory.Exists(katalog)) throw new EcxpException("Katalog projektu nie istnieje: " + katalog);
            lock (brama)
            {
                string id = Czytaj(home, katalog);
                if (id.Length == 0)
                {
                    log("Codex: rejestruję projekt w aplikacji: " + katalog);
                    otworz(katalog);
                    var czas = Stopwatch.StartNew();
                    while (id.Length == 0 && czas.ElapsedMilliseconds < 60000) { Thread.Sleep(200); id = Czytaj(home, katalog); }
                }
                if (id.Length == 0) throw new EcxpException("Codex nie zarejestrował projektu. Otwórz aplikację Codex, zaakceptuj folder projektu i spróbuj ponownie.");
                Dictionary<string, object> wynik;
                try { wynik = rpc("project/read", P.D("projectId", id), 30000); }
                catch (Exception) { throw new EcxpException("Nie można sprawdzić projektu desktopowego Codex. Sprawdź zgodność Codex CLI i aplikacji; sesja nie została utworzona."); }
                var pr = wynik != null && wynik.ContainsKey("project") ? wynik["project"] as Dictionary<string, object> : null;
                if (pr == null || P.S(pr, "id") != id || !P.Lista(pr, "roots").Any(x =>
                    string.Equals(Normalizuj(P.S(x, "path")), Normalizuj(katalog), StringComparison.OrdinalIgnoreCase)))
                    throw new EcxpException("Projekt desktopowy Codex ma inny katalog. Sesja nie została utworzona.");
                log("Codex: projekt desktopowy zweryfikowany: " + katalog);
                return id;
            }
        }
        // Nowy trwały wątek pojawia się na dysku po rozpoczęciu pierwszej tury.
        public static void CzekajNaZapis(string id, Func<string, Dictionary<string, object>, int, Dictionary<string, object>> rpc)
        {
            var czas = Stopwatch.StartNew();
            do
            {
                var r = rpc("thread/read", P.D("threadId", id, "includeTurns", false), 10000);
                var th = r != null && r.ContainsKey("thread") ? r["thread"] as Dictionary<string, object> : null;
                if (th != null && File.Exists(P.S(th, "path"))) return;
                Thread.Sleep(100);
            } while (czas.ElapsedMilliseconds < 10000);
            throw new EcxpException("Codex nie utrwalił nowego wątku. Nie można jeszcze otworzyć go w aplikacji.");
        }
    }
}
