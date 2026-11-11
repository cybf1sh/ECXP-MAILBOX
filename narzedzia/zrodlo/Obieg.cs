// ECXP – obieg zadania operatora: zadanie → (w trakcie) → FIN.
//  * Zadanie operatora zakłada zapis stan\obieg\<klucz>.json: kto wykonuje, kto sprawdza, stan każdego agenta.
//  * Odbiór ustawia „w trakcie” i od razu informuje operatora. FIN każdej strony jest widoczny, wspólny FIN nadal zamyka parę.
//  * W parze FIN wykonawcy idzie też do sprawdzającego, a operator dostaje
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

        // ---------- odbiór: „w trakcie”, ACK i widoczne potwierdzenie dla operatora ----------
        public static void Odebrane(string ja, IEnumerable<Msg> odebrane)
        {
            foreach (var m in odebrane.ToList())
            {
                var r = Znajdz(m.Id);
                if (r == null || P.B(r, "zakonczone")) continue;
                var w = Wpis(r, ja);
                if (w == null) continue;
                if (P.S(w, "stan") == "wyslane") PotwierdzenieOdbioru(r,w,ja);
                try { if (File.Exists(m.Sciezka) && m.Katalog != "przeczytane") P.PotwierdzPlik(m, ja, "przyjeta", "w trakcie (ECXP)"); } catch { }
            }
        }

        static void PotwierdzenieOdbioru(Dictionary<string, object> r, Dictionary<string, object> w, string ja)
        {
            if (P.S(w,"odbior_do_operatora").Length > 0) return;
            w["stan"] = "w_trakcie"; w["zmiana"] = P.Teraz();
            var potw = P.Wyslij(new P.Nowa { Od = ja, Do = P.Wlasciciel, Projekt = P.S(r,"projekt"), Modul = P.S(r,"modul"),
                Typ = "informacja", EtapObiegu = "odebrane", Temat = "Odebrano: " + P.S(r,"temat"), OdpNa = P.S(w,"zadanie"), Watek = P.S(r,"klucz"),
                Tresc = P.Nazwa(ja) + " odebrał zadanie „" + P.S(r,"temat") + "”. Zadanie jest w trakcie; po zakończeniu otrzymasz wynik tej strony." });
            w["odbior_do_operatora"] = potw["id"]; Zapisz(r);
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
                if (P.S(w, "stan") == "wyslane") PotwierdzenieOdbioru(r,w,n.Od);
                // etap podany przez agenta (np. w odpowiedzi na pytanie strażnika) – widoczny w konsoli; to nie jest FIN
                var etap = (n.Tresc ?? "").Trim();
                if (etap.Length > 0 && P.S(w, "stan") != "fin")
                {
                    if (etap.Length > EtapMaks) etap = etap.Substring(0, EtapMaks) + "…";
                    w["etap"] = etap; w["etap_czas"] = P.Teraz(); w["zmiana"] = P.Teraz();
                    P.Zdarzenie("obieg_etap", P.D("klucz", r["klucz"], "kto", n.Od, "etap", etap));
                }
                Zapisz(r);
                var m = P.Znajdz(P.S(w, "zadanie"));
                if (m != null && m.Katalog != "przeczytane" && m.Katalog != "archiwum") P.PotwierdzPlik(m, n.Od, "przyjeta", "w trakcie (ECXP)");
                return P.D("ack", true, "stan", "w_trakcie", "zadanie", r["klucz"], "uwagi", etap.Length > 0
                    ? "Etap zapisany i widoczny dla operatora. Pracuj dalej; po wykonaniu wyślij typ=fin."
                    : "Odbiór zadania jest widoczny dla operatora. Po wykonaniu wyślij typ=fin.");
            }
            if ((n.Tresc ?? "").Trim().Length == 0) throw new EcxpException("FIN bez treści: napisz wynik POZYTYWNY albo NEGATYWNY i krótko, co zrobiłeś.");
            var ag = Agenci(r);
            var wyk = ag.Where(kv => P.S(kv.Value as Dictionary<string, object>, "rola") == "wykonawca").Select(kv => kv.Key).FirstOrDefault() ?? "";
            if (P.S(w, "rola") == "sprawdzajacy" && wyk.Length > 0 && P.S(Wpis(r, wyk), "stan") != "fin")
                throw new EcxpException("Zadanie wykonuje " + wyk + ". Poczekaj na jego FIN (stos Ci go przekaże), wtedy sprawdź wynik i wyślij swój FIN. Teraz zakończ turę.");
            if (P.S(w,"stan") == "fin")
                return P.D("ack",true,"stan","fin","zadanie",r["klucz"],"fin_do_operatora",P.S(w,"fin_czesciowy_do_operatora"),"uwagi","Ten wynik został już zapisany; nie wysłano duplikatu.");
            w["stan"] = "fin"; w["fin"] = n.Tresc.Trim(); w["zmiana"] = P.Teraz();
            Zapisz(r);
            PotwierdzZadania(r, n.Od);
            P.Zdarzenie("obieg_fin", P.D("klucz", r["klucz"], "kto", n.Od));
            P.Sygnal(n.Od, "wyslij");

            // Wynik zakończonej strony jest od razu widoczny. Cały obieg pozostaje otwarty do wspólnego FIN.
            if (ag.Values.Cast<Dictionary<string,object>>().Any(x => P.S(x,"stan") != "fin"))
            {
                var czesc = P.Wyslij(new P.Nowa { Od=n.Od,Do=P.Wlasciciel,Projekt=P.S(r,"projekt"),Modul=P.S(r,"modul"),
                    Typ="fin",EtapObiegu="fin_czesciowy",Temat="FIN " + P.Nazwa(n.Od) + ": " + P.S(r,"temat"),
                    OdpNa=P.S(w,"zadanie"),Watek=P.S(r,"klucz"),Tresc=n.Tresc.Trim()+"\n\nTo wynik tej strony. Wspólny FIN zadania otrzymasz po zakończeniu pozostałych adresatów.",
                    Pliki=n.Pliki,Czesci=n.Czesci,Commit=n.Commit });
                w["fin_czesciowy_do_operatora"]=czesc["id"]; Zapisz(r);
            }
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
                return P.D("ack", true, "stan", "fin", "zadanie", r["klucz"], "przekazano", spr, "uwagi", "Twój FIN jest widoczny dla operatora. Stos przekazał wynik " + spr + " do sprawdzenia; operator dostanie wspólny FIN. Zakończ turę.");
            }
            if (ag.Values.Cast<Dictionary<string, object>>().Any(x => P.S(x, "stan") != "fin"))
                return P.D("ack", true, "stan", "fin", "zadanie", r["klucz"], "uwagi", "Twój FIN jest widoczny dla operatora. Wspólny FIN powstanie po zakończeniu pozostałych stron. Zakończ turę.");

            return FinWspolny(r, w, n);
        }

        // Wszyscy skończyli: jeden FIN do operatora (wynik każdej strony; wycofane wpisy pominięte).
        static Dictionary<string, object> FinWspolny(Dictionary<string, object> r, Dictionary<string, object> w, P.Nowa n)
        {
            var ag = Agenci(r);
            string tresc;
            if (ag.Count == 1) tresc = n.Tresc.Trim();
            else
                tresc = string.Join("\n\n", ag.Where(kv => !P.B(kv.Value as Dictionary<string, object>, "wycofane")).OrderBy(kv => P.S(kv.Value as Dictionary<string, object>, "rola") == "wykonawca" || P.S(kv.Value as Dictionary<string, object>, "rola") == "przekazal" ? 0 : 1).Select(kv =>
                {
                    var x = (Dictionary<string, object>)kv.Value;
                    var rola = P.S(x, "rola") == "wykonawca" || P.S(x, "rola") == "przejal" ? "wykonał" : P.S(x, "rola") == "sprawdzajacy" ? "sprawdził" : P.S(x, "rola") == "przekazal" ? "przekazał" : "";
                    return P.Nazwa(kv.Key) + " (" + kv.Key + ")" + (rola.Length > 0 ? " – " + rola : "") + ":\n" + P.S(x, "fin");
                }));
            var f = P.Wyslij(new P.Nowa
            {
                Od = n.Od, Do = P.Wlasciciel, Projekt = P.S(r, "projekt"), Modul = P.S(r, "modul"), Typ = "fin", EtapObiegu = "fin_wspolny", Temat = "FIN " + P.S(r, "temat"),
                OdpNa = P.S(w, "zadanie"), Tresc = tresc, Pliki = n.Pliki, Czesci = n.Czesci, Commit = n.Commit
            });
            r["zakonczone"] = true; r["zakonczone_czas"] = P.Teraz(); r["fin_do_operatora"] = f["id"];
            Zapisz(r);
            P.Zdarzenie("obieg_zakonczony", P.D("klucz", r["klucz"], "fin", f["id"]));
            return P.D("ack", true, "stan", "zakonczone", "zadanie", r["klucz"], "fin_do_operatora", f["id"], "uwagi", "FIN dotarł do operatora. Zadanie zakończone – zakończ turę.");
        }

        // Trwały błąd wykonawcy (blokada treści, brak logowania): ponawianie nic nie da. Jego wpis dostaje NEGATYWNY z przyczyną,
        // a zadanie wykonuje partner z pary (sprawdzający zostaje wykonawcą). Bez partnera – od razu FIN NEGATYWNY do operatora.
        public static List<string> Przekaz(string n, string powod)
        {
            var wynik = new List<string>();
            foreach (var r in Otwarte())
            {
                var e = Wpis(r, n);
                if (e == null || P.S(e, "stan") == "fin" || P.S(e, "rola") == "sprawdzajacy") continue;
                var tekst = "NEGATYWNY – nie mogę wykonać zadania: " + powod;
                var partner = Agenci(r).Where(kv => kv.Key != n && P.S(kv.Value as Dictionary<string, object>, "stan") != "fin").Select(kv => kv.Key).FirstOrDefault();
                // Pentest wymaga czterech niezależnych testerów i dwóch recenzentów.
                // Błąd jednego nie może po cichu zmienić składu ani modelu zespołu.
                if (Pentest.JestModul(P.S(r, "projekt"), P.S(r, "modul"))) partner = null;
                if (partner == null)
                {
                    ZAgenta(new P.Nowa { Od = n, Do = P.Wlasciciel, Projekt = P.S(r, "projekt"), Modul = P.S(r, "modul"), Typ = "fin", Tresc = tekst, OdpNa = P.S(e, "zadanie") });
                    wynik.Add(P.S(r, "klucz"));
                    continue;
                }
                e["stan"] = "fin"; e["fin"] = tekst + " Zadanie przekazane " + partner + "."; e["zmiana"] = P.Teraz(); e["rola"] = "przekazal";
                var ep = Wpis(r, partner);
                ep["rola"] = "przejal"; ep["zmiana"] = P.Teraz(); ep["przypomnienia"] = 0;
                PotwierdzZadania(r, n);
                var zad = P.Znajdz(P.S(e, "zadanie"));
                var p = P.Wyslij(new P.Nowa
                {
                    Od = P.Straznik, Do = partner, Projekt = P.S(r, "projekt"), Modul = P.S(r, "modul"), Typ = "informacja", Temat = "PRZEJMIJ " + P.S(r, "temat"), Watek = P.S(r, "klucz"),
                    Tresc = n + " nie może wykonać zadania (" + powod + "). Wykonaj je Ty – zamiast sprawdzania:\n\n" + (zad != null ? zad.Uwagi : P.S(r, "temat")) +
                            "\n\nPo wykonaniu wyślij jeden FIN: ecxp_wyslij(do=wlasciciel, typ=fin, odpowiedz_na=" + P.S(ep, "zadanie") + ") – POZYTYWNY albo NEGATYWNY i krótko, co zrobiłeś."
                });
                var prz = P.L(r, "przekazane"); prz.Add((string)p["id"]); r["przekazane"] = prz;
                Zapisz(r);
                P.Zdarzenie("obieg_przekazany", P.D("klucz", r["klucz"], "od", n, "do", partner, "powod", powod));
                wynik.Add(P.S(r, "klucz"));
            }
            return wynik;
        }

        // Agent ma otwarte zadanie bez FIN? (wtedy do operatora nie wysyła informacji ani meldunków – tylko FIN albo pytanie)
        public static Dictionary<string, object> OtwarteDla(string n, string projekt)
        {
            return Otwarte().FirstOrDefault(r => Wpis(r, n) != null && (projekt.Length == 0 || P.S(r, "projekt") == projekt));
        }

        // Potwierdzenie odbioru nie kończy pracy. Częściowy FIN dotyczy wyłącznie swojego adresata.
        public static bool WynikAdresata(Msg odpowiedz, string adresat)
        {
            if (odpowiedz.EtapObiegu == "odebrane") return false;
            return odpowiedz.Od == adresat || (odpowiedz.Typ == "fin" && odpowiedz.EtapObiegu != "fin_czesciowy");
        }

        // ---------- gwarancja FIN ----------
        // Zadanie, na które agent jest teraz winien FIN: nie dał go, a nie czeka zasadnie (sprawdzający przed FIN wykonawcy,
        // albo agent zadał pytanie, na które nikt jeszcze nie odpowiedział).
        public static Msg Brakujacy(string n)
        {
            List<Msg> w = null; HashSet<string> odp = null;      // wszystkie wiadomości czytane dopiero przy kandydacie (pętla co 0,5 s)
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
                if (w == null) { w = P.Wszystkie(false); odp = new HashSet<string>(w.Where(x => x.OdpNa.Length > 0).Select(x => x.OdpNa), StringComparer.OrdinalIgnoreCase); }
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
            ZamknijWycofane();
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

        // Zadanie wycofane przez operatora (⊘ Zamknij wątek, ↪ Wyślij ponownie – wynik „wycofana” w dzienniku) nie czeka
        // na FIN: wpis zamknięty bez przypomnień. Gdy wszystko wycofane – obieg zamknięty bez FIN (operator sam zdecydował);
        // gdy któraś strona dała już FIN – operator dostaje wspólny FIN z jej wynikiem.
        public static int ZamknijWycofane()
        {
            var otwarte = Otwarte();
            if (otwarte.Count == 0) return 0;
            string spoj; var stany = P.Stany(out spoj);
            int ile = 0;
            foreach (var r in otwarte)
            {
                bool zmiana = false;
                foreach (var kv in Agenci(r))
                {
                    var e = kv.Value as Dictionary<string, object>; Stan s;
                    if (e == null || P.S(e, "stan") == "fin" || !stany.TryGetValue(P.S(e, "zadanie"), out s) || s.Wynik != "wycofana") continue;
                    e["stan"] = "fin"; e["fin"] = "WYCOFANE przez operatora – bez wykonania."; e["wycofane"] = true; e["zmiana"] = P.Teraz(); zmiana = true; ile++;
                    P.Zdarzenie("obieg_wycofany", P.D("klucz", r["klucz"], "kto", kv.Key, "zadanie", P.S(e, "zadanie")));
                }
                if (!zmiana) continue;
                var ag = Agenci(r);
                if (ag.Values.Cast<Dictionary<string, object>>().All(x => P.S(x, "stan") == "fin"))
                {
                    var wykonane = ag.Where(kv => !P.B(kv.Value as Dictionary<string, object>, "wycofane")).ToList();
                    if (wykonane.Count == 0) { r["zakonczone"] = true; r["zakonczone_czas"] = P.Teraz(); r["wycofane"] = true; Zapisz(r); P.Zdarzenie("obieg_zakonczony", P.D("klucz", r["klucz"], "fin", "")); }
                    else { var w = (Dictionary<string, object>)wykonane[0].Value; FinWspolny(r, w, new P.Nowa { Od = wykonane[0].Key, Tresc = P.S(w, "fin") }); }
                }
                else Zapisz(r);
            }
            return ile;
        }

        // Czy agent jest winien FIN tego zadania (wpis w otwartym obiegu bez FIN).
        public static bool CzekaNaFin(string idZadania, string n)
        {
            var r = Znajdz(idZadania); if (r == null || P.B(r, "zakonczone")) return false;
            var e = Wpis(r, n); return e != null && P.S(e, "stan") != "fin" && P.S(e, "zadanie") == idZadania;
        }

        // ---------- strażnik zadań: etap zgłoszony przez agenta i pytanie o etap ----------
        public const int EtapMaks = 400;
        public const string TematPytaniaOEtap = "STAN-ZADANIA";   // stos zapisuje temat z łącznikami: STAN-ZADANIA-<temat>

        // Ostatni etap, który agent podał sam (typ=w_trakcie z treścią). Pusty, gdy nie podał.
        public static string EtapZgloszony(string idZadania, string n, out DateTime czas)
        {
            czas = DateTime.MinValue;
            var r = Znajdz(idZadania); var e = r == null ? null : Wpis(r, n);
            if (e == null || P.S(e, "etap").Length == 0) return "";
            DateTimeOffset t; if (DateTimeOffset.TryParse(P.S(e, "etap_czas"), out t)) czas = t.LocalDateTime;
            return P.S(e, "etap");
        }

        // Chwila ostatniego pytania strażnika o etap tego zadania u agenta (MinValue = nie pytano).
        public static DateTime OstatniePytanieOEtap(string idZadania, string n)
        {
            var r = Znajdz(idZadania); var e = r == null ? null : Wpis(r, n);
            DateTimeOffset t; return e != null && DateTimeOffset.TryParse(P.S(e, "pytanie_o_etap"), out t) ? t.LocalDateTime : DateTime.MinValue;
        }

        // Pytanie o etap (polecenie operatora). Sesja w aplikacji dostaje od strażnika informację STAN-ZADANIA i odpowiada
        // typem w_trakcie z opisem etapu – to nie jest FIN i nie zamyka zadania. Pytanie nie trafia do „przekazane”,
        // więc odpowiedź na nie nie może być wzięta za FIN. Agent w tle (bez MCP) nie dostaje pytania: jego stan zna stos.
        public static List<object> ZapytajOEtap(string idZadania, int minOdstepSek = 60)
        {
            var wynik = new List<object>();
            var r = Znajdz(idZadania);
            if (r == null) throw new EcxpException("To nie jest zadanie w obiegu – o etap można pytać tylko przy zadaniu operatora.");
            if (P.B(r, "zakonczone")) throw new EcxpException("Zadanie „" + P.S(r, "temat") + "” jest już zakończone.");
            var trasy = Siec.Trasy();
            foreach (var kv in Agenci(r))
            {
                var n = kv.Key; var e = kv.Value as Dictionary<string, object>;
                if (e == null || P.S(e, "stan") == "fin") continue;
                bool wAplikacji = trasy.Any(t => t.Aktywna && t.Nazwa == n && (t.Sesja ?? "").Length > 0 && !t.Sesja.StartsWith("tlo:"));
                if (!wAplikacji) { wynik.Add(P.D("agent", n, "sposob", "w_tle")); continue; }
                DateTimeOffset ost;
                if (DateTimeOffset.TryParse(P.S(e, "pytanie_o_etap"), out ost) && ost > DateTimeOffset.Now.AddSeconds(-minOdstepSek))
                { wynik.Add(P.D("agent", n, "sposob", "niedawno")); continue; }
                var zad = P.S(e, "zadanie");
                P.Wyslij(new P.Nowa
                {
                    Od = P.Straznik, Do = n, Projekt = P.S(r, "projekt"), Modul = P.S(r, "modul"), Typ = "informacja", Watek = P.S(r, "klucz"),
                    Temat = TematPytaniaOEtap + " " + P.S(r, "temat"),
                    Tresc = "Operator pyta, na jakim etapie jest zadanie „" + P.S(r, "temat") + "” (id " + zad + "). Nie przerywaj pracy i nie wysyłaj FIN, jeśli nie skończyłeś.\n" +
                            "Odpowiedz jednym wywołaniem: ecxp_wyslij(ja=" + n + ", do=wlasciciel, typ=w_trakcie, odpowiedz_na=" + zad +
                            ", tresc=<etap jednym–dwoma zdaniami: co już zrobione, co robisz teraz, co zostało>). Potem pracuj dalej. " +
                            "Jeśli zadanie jest już wykonane – wyślij zwykły FIN (typ=fin)."
                });
                e["pytanie_o_etap"] = P.Teraz();
                P.Zdarzenie("obieg_pytanie_o_etap", P.D("klucz", r["klucz"], "kto", n));
                wynik.Add(P.D("agent", n, "sposob", "zapytano"));
            }
            Zapisz(r);
            return wynik;
        }

        public static string Rola(string n, string idZadania) { var r = Znajdz(idZadania); var e = r == null ? null : Wpis(r, n); return e == null ? "" : P.S(e, "rola"); }
        public static string FinWykonawcy(string idZadania)
        {
            var r = Znajdz(idZadania); if (r == null) return "";
            return string.Join("\n\n", Agenci(r).Values.Cast<Dictionary<string, object>>().Where(x => P.S(x, "rola") != "sprawdzajacy" && P.S(x, "fin").Length > 0).Select(x => P.S(x, "fin")));
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
