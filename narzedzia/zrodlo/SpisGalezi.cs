// ECXP-MAILBOX – „Spis gałęzi” na bieżąco: repozytoria Git używane przez projekty ECXP (umowy gałęzi par, katalogi
// agentów i projektów), ich kopie robocze (worktree), gałęzie i niezapisane zmiany. Tylko odczyt: git z
// --no-optional-locks (bez odświeżania indeksu), bez fetch – stan zdalnych gałęzi według ostatniego pobrania.
// Gdy w katalogu skrzynki jest gotowy SPIS-GALEZI-*.json (dawny spis EvidLockV3), konsola pokazuje go jak dotąd.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Ecxp
{
    partial class App
    {
        // git w trybie tylko do odczytu; null, gdy polecenie się nie powiodło
        static string Git(string katalog, string args, int ms = 20000)
        {
            var psi = new ProcessStartInfo("git", "--no-optional-locks -c core.quotepath=off -C \"" + katalog.TrimEnd('\\') + "\" " + args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            psi.EnvironmentVariables["GIT_OPTIONAL_LOCKS"] = "0";
            psi.EnvironmentVariables["GIT_TERMINAL_PROMPT"] = "0";
            using (var p = Process.Start(psi))
            {
                var bl = p.StandardError.ReadToEndAsync();
                var wy = p.StandardOutput.ReadToEndAsync();
                if (!p.WaitForExit(ms)) { try { p.Kill(); } catch { } return null; }
                p.WaitForExit();
                return p.ExitCode == 0 ? wy.Result : null;
            }
        }

        // Katalogi, w których projekty ECXP pracują: umowy gałęzi, katalogi agentów, foldery projektów (i ich podkatalogi z .git).
        static List<string> KandydaciRepozytoriow()
        {
            var k = new List<string>();
            try
            {
                var dg = Path.Combine(P.Root, "stan", "galezie");
                if (Directory.Exists(dg))
                    foreach (var f in Directory.GetFiles(dg, "*.json"))
                    {
                        var d = Pliki.Czytaj(f);
                        var um = d.ContainsKey("umowa") ? d["umowa"] as Dictionary<string, object> : null;
                        if (um != null && P.S(um, "repozytorium").Length > 0) k.Add(P.S(um, "repozytorium"));
                    }
            }
            catch (Exception ex) { Bledy.Wyjatek(ex); }
            try { k.AddRange(Agenci.Lista().Select(a => P.S(a, "katalog")).Where(x => x.Length > 0)); } catch (Exception ex) { Bledy.Wyjatek(ex); }
            try
            {
                var dp = Path.Combine(P.Root, "projekty");
                if (Directory.Exists(dp))
                    foreach (var d in Directory.GetDirectories(dp))
                    {
                        k.Add(d);
                        foreach (var s in Directory.GetDirectories(d)) if (Directory.Exists(Path.Combine(s, ".git")) || File.Exists(Path.Combine(s, ".git"))) k.Add(s);
                    }
            }
            catch (Exception ex) { Bledy.Wyjatek(ex); }
            return k.Select(x => x.Trim().TrimEnd('\\', '/')).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        static int Linie(string s) { return string.IsNullOrEmpty(s) ? 0 : s.Split('\n').Count(l => l.Trim().Length > 0); }

        // Jeden wiersz spisu dla kopii roboczej (repozytorium główne albo worktree).
        static Dictionary<string, object> WierszKopii(string sciezka, string typ, List<object> galezie)
        {
            var w = P.D("katalog", sciezka, "typ", typ, "istnieje", Directory.Exists(sciezka), "przeniesiony", false, "galezie", galezie);
            if (!Directory.Exists(sciezka)) { w["typ"] = "BRAK"; w["grupa"] = "F"; return w; }
            w["galaz"] = (Git(sciezka, "branch --show-current") ?? "").Trim();
            var log = (Git(sciezka, "log -1 --format=%h|%ci") ?? "").Trim().Split('|');
            if (log.Length == 2) { w["head"] = log[0]; w["data"] = log[1].Length >= 16 ? log[1].Substring(0, 16) : log[1]; }
            var st = Git(sciezka, "status --porcelain=v1 --untracked-files=normal") ?? "";
            var ls = st.Split('\n').Where(l => l.Trim().Length > 0).ToList();
            w["zmienione"] = ls.Count(l => !l.StartsWith("??")); w["nowe"] = ls.Count(l => l.StartsWith("??"));
            return w;
        }

        // Dane spisu na bieżąco. opis: skąd pochodzą (pokazywany w nagłówku strony).
        public static List<object> SpisNaZywo(out string opis)
        {
            var wiersze = new List<object>();
            var widziane = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int repo = 0;
            foreach (var kat in KandydaciRepozytoriow())
            {
                if (!Directory.Exists(kat))
                {
                    // repozytorium z umowy gałęzi, którego już nie ma na dysku
                    if (widziane.Add(kat)) wiersze.Add(P.D("katalog", kat, "typ", "BRAK", "istnieje", false, "przeniesiony", false, "grupa", "F", "galezie", new List<object>()));
                    continue;
                }
                var top = (Git(kat, "rev-parse --show-toplevel") ?? "").Trim().Replace('/', '\\');
                if (top.Length == 0 || !widziane.Add(top)) continue;
                repo++;
                // gałęzie: na zdalnym (upstream, bez commitów do wypchnięcia) albo tylko w tym klonie
                var galezie = new List<object>();
                foreach (var l in (Git(top, "for-each-ref refs/heads --format=%(refname:short)|%(objectname:short)|%(upstream:short)|%(upstream:track)") ?? "").Split('\n'))
                {
                    var c = l.Trim().Split('|');
                    if (c.Length < 4 || c[0].Length == 0) continue;
                    var stan = c[2].Length == 0 || c[3].Contains("ahead") ? "TYLKO_W_KLONIE" : "NA_ZDALNYM";
                    galezie.Add(P.D("galaz", c[0], "sha", c[1], "stan", stan));
                }
                var wt = (Git(top, "worktree list --porcelain") ?? "").Split('\n').Where(l => l.StartsWith("worktree ")).Select(l => l.Substring(9).Trim().Replace('/', '\\')).ToList();
                var glowny = WierszKopii(top, "REPO", galezie);
                glowny["stash"] = Linie(Git(top, "stash list"));
                glowny["grupa"] = Grupa(glowny);
                wiersze.Add(glowny);
                foreach (var w in wt.Where(x => !string.Equals(x, top, StringComparison.OrdinalIgnoreCase)))
                {
                    if (!widziane.Add(w)) continue;
                    var wk = WierszKopii(w, "WORKTREE", new List<object>());
                    var g = galezie.Cast<Dictionary<string, object>>().FirstOrDefault(x => P.S(x, "galaz") == P.S(wk, "galaz"));
                    if (g != null) wk["galezie"] = new List<object> { g };
                    if (P.S(wk, "typ") != "BRAK") wk["grupa"] = "E";
                    wiersze.Add(wk);
                }
            }
            opis = "na bieżąco · repozytoria: " + repo + " · stan zdalnych gałęzi według ostatniego git fetch (spis niczego nie pobiera ani nie zmienia)";
            return wiersze;
        }
    }
}
