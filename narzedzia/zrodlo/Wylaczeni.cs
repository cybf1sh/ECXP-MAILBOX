// ECXP – agenci wyłączeni przez operatora (stan\wylaczeni.json). O tym, który agent pracuje, decyduje operator:
// wyłączony agent nie dostaje nowych wiadomości (adres pary pomija go, adres bezpośredni jest odrzucany), stos nie uruchamia
// jego tur w tle ani nie budzi jego sesji, a jego własne wywołania odbioru i wysyłki są odrzucane. Nic nie jest kasowane:
// trasy, rekord agenta i wiadomości zostają – po włączeniu agent pracuje dalej.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ecxp
{
    static class Wylaczeni
    {
        static string Plik { get { return Path.Combine(P.Root, "stan", "wylaczeni.json"); } }
        static Dictionary<string, object> pamiec; static DateTime pamiecZapis; static string pamiecPlik;
        static readonly object blokada = new object();

        static Dictionary<string, object> Agenci()
        {
            lock (blokada)
            {
                var p = Plik;
                var zapis = File.Exists(p) ? File.GetLastWriteTimeUtc(p) : DateTime.MinValue;
                if (pamiec == null || pamiecPlik != p || zapis != pamiecZapis)
                {
                    var d = Pliki.Czytaj(p);
                    pamiec = d.ContainsKey("agenci") && d["agenci"] is Dictionary<string, object> ? (Dictionary<string, object>)d["agenci"] : new Dictionary<string, object>();
                    pamiecZapis = zapis; pamiecPlik = p;
                }
                return pamiec;
            }
        }

        public static bool Jest(string nazwa) { return !string.IsNullOrEmpty(nazwa) && Agenci().ContainsKey(nazwa); }
        public static List<string> Lista() { return Agenci().Keys.OrderBy(x => x).ToList(); }

        // Wyłącza albo włącza agenta. Zwraca stan po zmianie.
        public static Dictionary<string, object> Ustaw(string nazwa, bool wylacz, string uwagi)
        {
            nazwa = (nazwa ?? "").Trim().ToLowerInvariant();
            if (!P.PoprawnyNadawca(nazwa)) throw new EcxpException("Nazwa agenta: codex-<nazwa> albo claude-<nazwa>.");
            return P.ZBlokada(delegate
            {
                var d = Pliki.Czytaj(Plik);
                var ag = d.ContainsKey("agenci") && d["agenci"] is Dictionary<string, object> ? (Dictionary<string, object>)d["agenci"] : new Dictionary<string, object>();
                bool byl = ag.ContainsKey(nazwa);
                if (wylacz) ag[nazwa] = P.D("czas", P.Teraz(), "uwagi", uwagi ?? "");
                else ag.Remove(nazwa);
                d["agenci"] = ag;
                d["opis"] = "Agenci wyłączeni przez operatora w ECXP-MAILBOX. Zapisuje stos ECXP.";
                Directory.CreateDirectory(Path.GetDirectoryName(Plik));
                Pliki.Zapisz(Plik, d);
                lock (blokada) pamiec = null;
                if (byl != wylacz) P.Zdarzenie(wylacz ? "agent_wylaczony" : "agent_wlaczony", P.D("kto", P.Wlasciciel, "agent", nazwa, "uwagi", uwagi ?? ""));
                return P.D("agent", nazwa, "wylaczony", wylacz, "zmiana", byl != wylacz);
            });
        }
    }
}
