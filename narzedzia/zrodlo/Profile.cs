// ECXP – profile agentów (stan\profile-agentow.json): własna nazwa i opis nadane agentowi przez operatora.
// Profil niczego nie zmienia w adresowaniu – tożsamość ECXP (codex-…, claude-…) zostaje ta sama; nazwa własna służy
// operatorowi do rozpoznawania agentów w konsoli. Zapisuje wyłącznie stos (polecenie profil_agenta).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ecxp
{
    static class Profile
    {
        public const int MaksImie = 40, MaksOpis = 300;
        static string Plik { get { return Path.Combine(P.Root, "stan", "profile-agentow.json"); } }
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

        static string Pole(string nazwa, string pole)
        {
            if (string.IsNullOrEmpty(nazwa)) return "";
            object o; var ag = Agenci();
            return ag.TryGetValue(nazwa, out o) && o is Dictionary<string, object> ? P.S((Dictionary<string, object>)o, pole) : "";
        }
        public static string Imie(string nazwa) { return Pole(nazwa, "imie"); }
        public static string Opis(string nazwa) { return Pole(nazwa, "opis"); }

        static string Czysty(string s) { return new string((s ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim(); }

        // Ustawia nazwę własną i opis agenta. Puste oba pola usuwają profil.
        public static Dictionary<string, object> Ustaw(string nazwa, string imie, string opis)
        {
            nazwa = (nazwa ?? "").Trim().ToLowerInvariant();
            if (!P.PoprawnyNadawca(nazwa)) throw new EcxpException("Nazwa agenta: codex-<nazwa> albo claude-<nazwa>.");
            imie = Czysty(imie); opis = Czysty(opis);
            if (imie.Length > MaksImie) throw new EcxpException("Nazwa własna agenta: najwyżej " + MaksImie + " znaków.");
            if (opis.Length > MaksOpis) throw new EcxpException("Opis agenta: najwyżej " + MaksOpis + " znaków.");
            return P.ZBlokada(delegate
            {
                var d = Pliki.Czytaj(Plik);
                var ag = d.ContainsKey("agenci") && d["agenci"] is Dictionary<string, object> ? (Dictionary<string, object>)d["agenci"] : new Dictionary<string, object>();
                var stary = ag.ContainsKey(nazwa) && ag[nazwa] is Dictionary<string, object> ? (Dictionary<string, object>)ag[nazwa] : new Dictionary<string, object>();
                bool zmiana = P.S(stary, "imie") != imie || P.S(stary, "opis") != opis;
                if (imie.Length == 0 && opis.Length == 0) ag.Remove(nazwa);
                else ag[nazwa] = P.D("imie", imie, "opis", opis, "czas", P.Teraz());
                if (zmiana)
                {
                    d["agenci"] = ag;
                    d["opis"] = "Nazwy własne i opisy agentów nadane przez operatora w ECXP-MAILBOX. Zapisuje stos ECXP.";
                    Directory.CreateDirectory(Path.GetDirectoryName(Plik));
                    Pliki.Zapisz(Plik, d);
                    lock (blokada) pamiec = null;
                    P.Zdarzenie("agent_profil", P.D("kto", P.Wlasciciel, "agent", nazwa, "imie", imie));
                }
                return P.D("agent", nazwa, "imie", imie, "opis", opis, "zmiana", zmiana);
            });
        }
    }
}
