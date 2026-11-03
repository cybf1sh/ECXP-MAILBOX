// ECXP – obieg zadania operatora: zadanie → (w trakcie) → FIN.
//  * Zadanie operatora zakłada zapis stan\obieg\<klucz>.json: kto wykonuje, kto sprawdza, stan każdego agenta.
//  * „W trakcie” to stan w stosie, nie wiadomość: zapisuje go odbiór zadania (albo typ=w_trakcie). Operator nic nie dostaje.
//  * FIN agenta też nie trafia od razu do operatora: w parze FIN wykonawcy idzie do sprawdzającego, a operator dostaje
//    jeden wspólny FIN, gdy skończą wszyscy. Zadanie dla jednego agenta: jego FIN od razu do operatora.
//  * Nadzór: agent bez FIN dostaje przypomnienie (agent w tle – turę, sesja w aplikacji – wiadomość), po MaxPrzypomnien alarm.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ecxp
{
    static class Obieg
    {
        public const int MaxPrzypomnien = 2;
        public const int MinutyDoPrzypomnienia = 20;      // sesja w aplikacji: tyle ciszy bez FIN, zanim stos przypomni
        static string Dir { get { return Path.Combine(P.Root, "stan", "obieg"); } }

        static List<Dictionary<string, object>> Wszystkie()
        {
            if (!Directory.Exists(Dir)) return new List<Dictionary<string, object>>();
            var l = new List<Dictionary<string, object>>();
            foreach (var f in Directory.GetFiles(Dir, "*.json")) { try { l.Add(Pliki.Czytaj(f)); } catch { } }
            return l;
        }
        public static List<Dictionary<string, object>> Otwarte() { return Wszystkie().Where(r => !P.B(r, "zakonczone")).OrderBy(r => P.S(r, "czas")).ToList(); }
        static void Zapisz(Dictionary<string, object> r) { Directory.CreateDirectory(Dir); Pliki.Zapisz(Path.Combine(Dir, P.S(r, "klucz") + ".json"), r); }
        static Dictionary<string, object> Agenci(Dictionary<string, object> r) { object v; return r.TryGetValue("agenci", out v) && v is Dictionary<string, object> ? (Dictionary<string, object>)v : new Dictionary<string, object>(); }
        static Dictionary<string, object> Wpis(Dictionary<string, object> r, string n) { object v; return Agenci(r).TryGetValue(n, out v) ? v as Dictionary<string, object> : null; }

        // Zadanie, do którego należy wiadomość (zadanie agenta albo wiadomość przekazana mu przez stos).
        public static Dictionary<string, object> Znajdz(string idWiadomosci)
        {
            if (string.IsNullOrEmpty(idWiadomosci)) return null;
            foreach (var r in Wszystkie())
            {
                if (P.L(r, "przekazane").Contains(idWiadomosci)) return r;
                foreach (var kv in Agenci(r)) if (P.S(kv.Value as Dictionary<string, object>, "zadanie") == idWiadomosci) return r;
            }
            return null;
        }

        // ---------- zadanie operatora: założenie obiegu ----------
        public static void Zalozone(string projekt, string modul, string temat, List<object> wyslane, string lustroId)
        {
            var cele = wyslane.Cast<Dictionary<string, object>>().Where(w => P.PoprawnyNadawca(P.S(w, "do"))).ToList();
            if (cele.Count == 0) return;
            // wykonawca pary zapisany przy nowym zadaniu (projekt.json → pary.<moduł>.wykonawca)
            string wykonawca = "";
            try
            {
                var pr = Pliki.Czytaj(Path.Combine(P.Root, "projekty", projekt, "projekt.json"));
                object pv, mv;
                if (pr.TryGetValue("pary", out pv) && pv is Dictionary<string, object> && ((Dictionary<string, object>)pv).TryGetValue(modul, out mv) && mv is Dictionary<string, object>)
                    wykonawca = P.S((Dictionary<string, object>)mv, "wykonawca");
            }
            catch { }
            bool para = cele.Count == 2 && cele.Any(c => P.S(c, "do") == wykonawca);
            var agenci = new Dictionary<string, object>();
            foreach (var c in cele)
            {
                var n = P.S(c, "do");
                agenci[n] = P.D("zadanie", P.S(c, "id"), "rola", !para ? "sam" : n == wykonawca ? "wykonawca" : "sprawdzajacy",
                    "obowiazkowa_odpowiedz", true, "stan", "wyslane", "zmiana", P.Teraz(), "przypomnienia", 0);
            }
            var r = P.D("klucz", lustroId.Length > 0 ? lustroId : P.S(cele[0], "id"), "projekt", projekt, "modul", modul, "temat", temat ?? "", "czas", P.Teraz(),
                "zakonczone", false, "agenci", agenci, "przekazane", new List<string>());
            Zapisz(r);
            P.Zdarzenie("obieg_zalozony", P.D("klucz", r["klucz"], "agenci", agenci.Keys.ToList()));
        }

        // ---------- odbiór: zadanie przyjęte = „w trakcie”, wiadomość potwierdzona (bez meldunku do operatora) ----------
        public static void Odebrane(string ja, IEnumerable<Msg> odebrane)
        {
            foreach (var m in odebrane.ToList())
            {
                var r = Znajdz(m.Id);
                if (r == null || P.B(r, "zakonczone")) continue;
                var w = Wpis(r, ja);
                if (w == null) continue;
                if (P.S(w, "stan") == "wyslane") { w["stan"] = "w_trakcie"; w["zmiana"] = P.Teraz(); Zapisz(r); }
                try { if (File.Exists(m.Sciezka) && m.Katalog != "przeczytane") P.PotwierdzPlik(m, ja, "przyjeta", "w trakcie (ECXP)"); } catch { }
            }
        }

        static void PotwierdzZadania(Dictionary<string, object> r, string ja)
        {
            var ids = new List<string> { P.S(Wpis(r, ja), "zadanie") };
            ids.AddRange(P.L(r, "przekazane"));
            foreach (var id in ids)
            {
                var m = P.Znajdz(id);
                if (m != null && P.DoMnie(ja, m.Do) && m.Katalog != "przeczytane" && m.Katalog != "archiwum") P.PotwierdzPlik(m, ja, "obsluzona", "FIN");
            }
        }

        // ---------- wiadomość agenta: w_trakcie i FIN obsługuje obieg (null = zwykła wiadomość) ----------
        public static Dictionary<string, object> ZAgenta(P.Nowa n)
        {
            var typ = (n.Typ ?? "").Trim().ToLowerInvariant();
            bool fin = typ == "fin";
            if (!fin && typ != "w_trakcie")
            {
                // odpowiedź do operatora na zadanie w obiegu = FIN (zgodność ze starszymi pakietami)
                if (typ != "odpowiedz" || n.Do != P.Wlasciciel || n.OdpNa.Length == 0) return null;
                var o = Znajdz(n.OdpNa);
                if (o == null || Wpis(o, n.Od) == null) return null;
                fin = true;
            }
            Dictionary<string, object> r = n.OdpNa.Length > 0 ? Znajdz(n.OdpNa) : null;
            if (r == null && n.OdpNa.Length == 0)
                r = Otwarte().FirstOrDefault(x => Wpis(x, n.Od) != null && P.S(Wpis(x, n.Od), "stan") != "fin" && (n.Projekt.Length == 0 || P.S(x, "projekt") == n.Projekt));
            if (r == null || Wpis(r, n.Od) == null)
                throw new EcxpException((fin ? "FIN" : "W trakcie") + " dotyczy zadania operatora, które dostałeś: podaj odpowiedz_na=<id tego zadania>.");
            if (P.B(r, "zakonczone"))
                throw new EcxpException("Zadanie „" + P.S(r, "temat") + "” jest już zakończone – operator dostał FIN. Zakończ turę.");
            var w = Wpis(r, n.Od);
            if (!fin)
            {
                if (P.S(w, "stan") == "wyslane") { w["stan"] = "w_trakcie"; w["zmiana"] = P.Teraz(); }
                Zapisz(r);
                var m = P.Znajdz(P.S(w, "zadanie"));
                if (m != null && m.Katalog != "przeczytane" && m.Katalog != "archiwum") P.PotwierdzPlik(m, n.Od, "przyjeta", "w trakcie (ECXP)");
                return P.D("ack", true, "stan", "w_trakcie", "zadanie", r["klucz"], "uwagi", "Zapisane w stosie – operator nic nie dostaje. Po wykonaniu wyślij typ=fin.");
            }
            if ((n.Tresc ?? "").Trim().Length == 0) throw new EcxpException("FIN bez treści: napisz wynik POZYTYWNY albo NEGATYWNY i krótko, co zrobiłeś.");
            var ag = Agenci(r);
            var wyk = ag.Where(kv => P.S(kv.Value as Dictionary<string, object>, "rola") == "wykonawca").Select(kv => kv.Key).FirstOrDefault() ?? "";
            if (P.S(w, "rola") == "sprawdzajacy" && wyk.Length > 0 && P.S(Wpis(r, wyk), "stan") != "fin")
                throw new EcxpException("Zadanie wykonuje " + wyk + ". Poczekaj na jego FIN (stos Ci go przekaże), wtedy sprawdź wynik i wyślij swój FIN. Teraz zakończ turę.");
            w["stan"] = "fin"; w["fin"] = n.Tresc.Trim(); w["zmiana"] = P.Teraz();
            Zapisz(r);
            PotwierdzZadania(r, n.Od);
            P.Zdarzenie("obieg_fin", P.D("klucz", r["klucz"], "kto", n.Od));
            P.Sygnal(n.Od, "wyslij");

            // wykonawca skończył, sprawdzający jeszcze nie: wynik idzie do sprawdzającego, operator czeka na wspólny FIN
            var spr = ag.Where(kv => P.S(kv.Value as Dictionary<string, object>, "rola") == "sprawdzajacy" && P.S(kv.Value as Dictionary<string, object>, "stan") != "fin").Select(kv => kv.Key).FirstOrDefault();
            if (P.S(w, "rola") == "wykonawca" && spr != null)
            {
                var ws = Wpis(r, spr);
                var p = P.Wyslij(new P.Nowa
                {
                    Od = n.Od, Do = spr, Projekt = P.S(r, "projekt"), Modul = P.S(r, "modul"), Typ = "informacja", Temat = "FIN " + P.S(r, "temat"), Watek = P.S(r, "klucz"),
                    Tresc = n.Od + " zakończył zadanie „" + P.S(r, "temat") + "”:\n\n" + n.Tresc.Trim() + "\n\nSprawdź wynik i wyślij jeden FIN: ecxp_wyslij(do=wlasciciel, typ=fin, odpowiedz_na=" +
                            P.S(ws, "zadanie") + ") – w treści POZYTYWNY albo NEGATYWNY i co sprawdziłeś.",
                    Pliki = n.Pliki, Czesci = n.Czesci, Commit = n.Commit
                });
                var prz = P.L(r, "przekazane"); prz.Add((string)p["id"]); r["przekazane"] = prz;
                ws["zmiana"] = P.Teraz();
                Zapisz(r);
                return P.D("ack", true, "stan", "fin", "zadanie", r["klucz"], "przekazano", spr, "uwagi", "FIN zapisany. Stos przekazał wynik " + spr + " do sprawdzenia; operator dostanie wspólny FIN. Zakończ turę.");
            }
            if (ag.Values.Cast<Dictionary<string, object>>().Any(x => P.S(x, "stan") != "fin"))
                return P.D("ack", true, "stan", "fin", "zadanie", r["klucz"], "uwagi", "FIN zapisany. Operator dostanie wspólny FIN, gdy skończy druga strona. Zakończ turę.");

            // wszyscy skończyli: jeden FIN do operatora
            string tresc;
            if (ag.Count == 1) tresc = n.Tresc.Trim();
            else
                tresc = string.Join("\n\n", ag.OrderBy(kv => P.S(kv.Value as Dictionary<string, object>, "rola") == "wykonawca" ? 0 : 1).Select(kv =>
                {
                    var x = (Dictionary<string, object>)kv.Value;
                    var rola = P.S(x, "rola") == "wykonawca" ? "wykonał" : P.S(x, "rola") == "sprawdzajacy" ? "sprawdził" : "";
                    return P.Nazwa(kv.Key) + " (" + kv.Key + ")" + (rola.Length > 0 ? " – " + rola : "") + ":\n" + P.S(x, "fin");
                }));
            var f = P.Wyslij(new P.Nowa
            {
                Od = n.Od, Do = P.Wlasciciel, Projekt = P.S(r, "projekt"), Modul = P.S(r, "modul"), Typ = "fin", Temat = "FIN " + P.S(r, "temat"),
                OdpNa = P.S(w, "zadanie"), Tresc = tresc, Pliki = n.Pliki, Czesci = n.Czesci, Commit = n.Commit
            });
            r["zakonczone"] = true; r["zakonczone_czas"] = P.Teraz(); r["fin_do_operatora"] = f["id"];
            Zapisz(r);
            P.Zdarzenie("obieg_zakonczony", P.D("klucz", r["klucz"], "fin", f["id"]));
            return P.D("ack", true, "stan", "zakonczone", "zadanie", r["klucz"], "fin_do_operatora", f["id"], "uwagi", "FIN dotarł do operatora. Zadanie zakończone – zakończ turę.");
        }

        // Agent ma otwarte zadanie bez FIN? (wtedy do operatora nie wysyła informacji ani meldunków – tylko FIN albo pytanie)
        public static Dictionary<string, object> OtwarteDla(string n, string projekt)
        {
            return Otwarte().FirstOrDefault(r => Wpis(r, n) != null && (projekt.Length == 0 || P.S(r, "projekt") == projekt));
        }

        // ---------- gwarancja FIN ----------
        // Zadanie, na które agent jest teraz winien FIN: nie dał go, a nie czeka zasadnie (sprawdzający przed FIN wykonawcy,
        // albo agent zadał pytanie, na które nikt jeszcze nie odpowiedział).
        public static Msg Brakujacy(string n)
        {
            var w = P.Wszystkie(false);
            var odp = new HashSet<string>(w.Where(x => x.OdpNa.Length > 0).Select(x => x.OdpNa), StringComparer.OrdinalIgnoreCase);
            foreach (var r in Otwarte())
            {
                var e = Wpis(r, n);
                if (e == null || P.S(e, "stan") == "fin" || !P.B(e, "obowiazkowa_odpowiedz")) continue;
                if (P.S(e, "rola") == "sprawdzajacy")
                {
                    var wyk = Agenci(r).Where(kv => P.S(kv.Value as Dictionary<string, object>, "rola") == "wykonawca").Select(kv => kv.Value as Dictionary<string, object>).FirstOrDefault();
                    if (wyk != null && P.S(wyk, "stan") != "fin") continue;
                }
                DateTimeOffset od; DateTimeOffset.TryParse(P.S(r, "czas"), out od);
                if (w.Any(x => x.Od == n && x.Typ == "pytanie" && x.Czas > od.LocalDateTime && !odp.Contains(x.Id))) continue;
                var m = P.Znajdz(P.S(e, "zadanie"));
                if (m != null) return m;
            }
            return null;
        }

        // Nadzór sesji w aplikacjach (agentów w tle pilnuje Agenci.Petla): po ciszy bez FIN przypomnienie do agenta,
        // po MaxPrzypomnien jeden alarm u operatora. Nic nie idzie do operatora, dopóki agent może jeszcze skończyć.
        public static List<P.Alarm> Nadzor(HashSet<string> wTle)
        {
            var al = new List<P.Alarm>();
            foreach (var r in Otwarte())
            {
                bool zmiana = false;
                foreach (var kv in Agenci(r))
                {
                    var n = kv.Key; var e = (Dictionary<string, object>)kv.Value;
                    if (wTle.Contains(n)) continue;
                    var zad = Brakujacy(n);
                    if (zad == null || P.S(e, "zadanie") != zad.Id) continue;
                    // czeka jeszcze nieodebrana wiadomość – doręczanie samo budzi sesję
                    if (P.Wszystkie(false).Any(m => P.DoMnie(n, m.Do) && (m.Katalog == "do-codex" || m.Katalog == "do-claude"))) continue;
                    DateTimeOffset t; DateTimeOffset.TryParse(P.S(e, "zmiana"), out t);
                    if (t > DateTimeOffset.Now.AddMinutes(-MinutyDoPrzypomnienia)) continue;
                    int ile; int.TryParse(P.S(e, "przypomnienia"), out ile);
                    if (ile < MaxPrzypomnien)
                    {
                        var p = P.Wyslij(new P.Nowa
                        {
                            Od = P.Straznik, Do = n, Projekt = P.S(r, "projekt"), Modul = P.S(r, "modul"), Typ = "informacja", Temat = "PRZYPOMNIENIE FIN", Watek = P.S(r, "klucz"),
                            Tresc = "Zadanie „" + P.S(r, "temat") + "” nie ma jeszcze Twojego FIN. Dokończ je i wyślij: ecxp_wyslij(ja=" + n + ", do=wlasciciel, typ=fin, odpowiedz_na=" + zad.Id +
                                    ") – w treści POZYTYWNY albo NEGATYWNY i krótko dlaczego. Jeśli nie da się go wykonać: NEGATYWNY z powodem."
                        });
                        var prz = P.L(r, "przekazane"); prz.Add((string)p["id"]); r["przekazane"] = prz;
                        e["przypomnienia"] = ile + 1; e["zmiana"] = P.Teraz(); zmiana = true;
                    }
                    else if (!P.B(e, "alarm"))
                    {
                        var a = P.Wyslij(new P.Nowa
                        {
                            Od = P.Straznik, Do = P.Wlasciciel, Projekt = P.S(r, "projekt"), Modul = P.S(r, "modul"), Typ = "blokada", Temat = "BRAK FIN", OdpNa = zad.Id,
                            Tresc = n + " nie dał FIN zadania „" + P.S(r, "temat") + "” mimo " + MaxPrzypomnien + " przypomnień. Prawy przycisk: ⏰ Obudź adresata albo ⊘ Zamknij wątek."
                        });
                        e["alarm"] = true; zmiana = true;
                        al.Add(new P.Alarm { Id = (string)a["id"], Do = P.Wlasciciel, Temat = "BRAK FIN", Opis = n + ": brak FIN" });
                    }
                }
                if (zmiana) Zapisz(r);
            }
            return al;
        }

        public static void Przypomniano(string n, string idZadania)
        {
            var r = Znajdz(idZadania); if (r == null) return;
            var e = Wpis(r, n); if (e == null) return;
            int ile; int.TryParse(P.S(e, "przypomnienia"), out ile);
            e["przypomnienia"] = ile + 1; e["zmiana"] = P.Teraz();
            Zapisz(r);
        }
        public static int Przypomnienia(string n, string idZadania)
        {
            var r = Znajdz(idZadania); var e = r == null ? null : Wpis(r, n);
            int ile = 0; if (e != null) int.TryParse(P.S(e, "przypomnienia"), out ile);
            return ile;
        }
        public static bool Alarmowano(string n, string idZadania)
        {
            var r = Znajdz(idZadania); var e = r == null ? null : Wpis(r, n);
            if (e == null || P.B(e, "alarm")) return true;
            e["alarm"] = true; Zapisz(r);
            return false;
        }
    }
}
