// ECXP – warstwa sieci: adresowanie, tablica tras, pary 1:1 (Codex + Claude), role, lustro i kopie dla pary.
//
// Zasada 1:1: w projekcie i module pracuje para – jedna sesja Codexa i jedna Claude. Zadanie dla pary trafia
// do obu jako jedna sprawa (lustro: ten sam wątek i treść). Wejście operatora skierowane do jednej strony pary
// i meldunek jednej strony do operatora dostaje także druga strona (kopia dla pary). Treść kopiowana dosłownie.
//
// Adresy: wlasciciel | codex-<x> | claude-<x> | para:<moduł> | */<moduł> | codex/<moduł> | claude/<moduł> | rola:<rola>@<moduł|*>
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Ecxp
{
    class Trasa
    {
        public string Projekt = "", Modul = "", Strona = "", Nazwa = "", Sesja = "", Opis = "";
        public List<string> Role = new List<string>();
        public DateTime Od;
        public bool Aktywna = true;
    }

    static class Siec
    {
        public static readonly string[] RoleDomyslne = { "programista", "recenzent", "tester", "bezpieczenstwo", "koordynator", "integrator" };
        static string PlikTras { get { return Path.Combine(P.Root, "stan", "trasy.json"); } }

        // ---------- tablica tras ----------
        public static List<Trasa> Trasy()
        {
            var r = new List<Trasa>();
            var t = P.Czytaj(PlikTras);
            if (t.Trim().Length == 0) return r;
            object[] arr;
            try { arr = P.Js().Deserialize<object[]>(t); } catch { return r; }
            foreach (var o in arr)
            {
                var d = o as Dictionary<string, object>;
                if (d == null) continue;
                var x = new Trasa
                {
                    Projekt = P.S(d, "projekt"), Modul = P.S(d, "modul"), Strona = P.S(d, "strona"), Nazwa = P.S(d, "nazwa"),
                    Sesja = P.S(d, "sesja"), Opis = P.S(d, "opis"), Role = P.L(d, "role"),
                    Aktywna = !d.ContainsKey("aktywna") || P.B(d, "aktywna")
                };
                DateTimeOffset dto;
                x.Od = DateTimeOffset.TryParse(P.S(d, "od"), CultureInfo.InvariantCulture, DateTimeStyles.None, out dto) ? dto.LocalDateTime : DateTime.MinValue;
                r.Add(x);
            }
            return r;
        }

        static void ZapiszTrasy(List<Trasa> t)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PlikTras));
            var lista = t.Select(x => (object)P.D("projekt", x.Projekt, "modul", x.Modul, "strona", x.Strona, "nazwa", x.Nazwa, "sesja", x.Sesja,
                "opis", x.Opis, "role", x.Role, "od", new DateTimeOffset(x.Od).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture), "aktywna", x.Aktywna)).ToList();
            var tmp = PlikTras + "." + P.Losowe(3) + ".tmp";
            File.WriteAllText(tmp, P.Js().Serialize(lista), new UTF8Encoding(false));
            if (File.Exists(PlikTras)) File.Replace(tmp, PlikTras, null); else File.Move(tmp, PlikTras);
        }

        static string Strona(string nazwa) { return nazwa.StartsWith("codex") ? "codex" : "claude"; }

        // Para (projekt, moduł): aktywne trasy, a gdy ich brak – pole "pary" w projekty\<projekt>\projekt.json.
        public static Dictionary<string, object> Para(string projekt, string modul)
        {
            string codex = "", claude = "";
            foreach (var t in Trasy().Where(x => x.Aktywna && x.Projekt == projekt && x.Modul == modul).OrderBy(x => x.Od))
                if (t.Strona == "codex") codex = t.Nazwa; else claude = t.Nazwa;
            if (codex.Length == 0 || claude.Length == 0)
            {
                var pr = P.Projekty().FirstOrDefault(x => P.S(x, "nazwa") == projekt);
                object pv;
                if (pr != null && pr.TryGetValue("pary", out pv) && pv is Dictionary<string, object>)
                {
                    object mv;
                    var pary = (Dictionary<string, object>)pv;
                    if (pary.TryGetValue(modul, out mv) && mv is Dictionary<string, object>)
                    {
                        var m = (Dictionary<string, object>)mv;
                        if (codex.Length == 0) codex = P.S(m, "codex");
                        if (claude.Length == 0) claude = P.S(m, "claude");
                    }
                }
            }
            return P.D("projekt", projekt, "modul", modul, "codex", codex, "claude", claude, "pelna", codex.Length > 0 && claude.Length > 0);
        }

        // Do jakich par należy węzeł (projekt, moduł).
        public static List<Trasa> Przynaleznosc(string nazwa)
        {
            var r = Trasy().Where(x => x.Aktywna && x.Nazwa == nazwa).ToList();
            foreach (var pr in P.Projekty())
            {
                object pv;
                if (!pr.TryGetValue("pary", out pv) || !(pv is Dictionary<string, object>)) continue;
                foreach (var kv in (Dictionary<string, object>)pv)
                {
                    var m = kv.Value as Dictionary<string, object>;
                    if (m != null && (P.S(m, "codex") == nazwa || P.S(m, "claude") == nazwa) && !r.Any(x => x.Projekt == P.S(pr, "nazwa") && x.Modul == kv.Key))
                        r.Add(new Trasa { Projekt = P.S(pr, "nazwa"), Modul = kv.Key, Strona = Strona(nazwa), Nazwa = nazwa });
                }
            }
            return r;
        }

        public static string Partner(string projekt, string modul, string nazwa)
        {
            var p = Para(projekt, modul);
            if ((string)p["codex"] == nazwa) return (string)p["claude"];
            if ((string)p["claude"] == nazwa) return (string)p["codex"];
            return "";
        }

        // ---------- przypisanie sesji do modułu (jak DHCP/ARP) ----------
        public static Dictionary<string, object> Przypisz(string nazwa, string projekt, string modul, string sesja, List<string> role, string opis, string kto)
        {
            if (!P.PoprawnyNadawca(nazwa)) throw new EcxpException("Niepoprawna nazwa sesji '" + nazwa + "'. Użyj codex-<nazwa> albo claude-<nazwa>.");
            projekt = P.Slug(projekt, 32, false); modul = P.Slug(modul, 24, false);
            if (projekt.Length == 0 || modul.Length == 0) throw new EcxpException("Podaj projekt i moduł.");
            role = (role ?? new List<string>()).Select(x => P.Slug(x, 24, false)).Where(x => x.Length > 0).Distinct().ToList();
            var strona = Strona(nazwa);
            return P.ZBlokada(delegate
            {
                var t = Trasy();
                var poprzednie = t.Where(x => x.Aktywna && x.Projekt == projekt && x.Modul == modul && x.Strona == strona && x.Nazwa != nazwa).ToList();
                foreach (var x in poprzednie) x.Aktywna = false;
                var moja = t.FirstOrDefault(x => x.Projekt == projekt && x.Modul == modul && x.Nazwa == nazwa);
                if (moja == null) { moja = new Trasa { Projekt = projekt, Modul = modul, Nazwa = nazwa }; t.Add(moja); }
                moja.Aktywna = true; moja.Strona = strona; moja.Od = DateTime.Now;
                if ((sesja ?? "").Length > 0) moja.Sesja = sesja;
                if (role.Count > 0) moja.Role = role;
                if ((opis ?? "").Length > 0) moja.Opis = opis;
                ZapiszTrasy(t);
                P.Zdarzenie("przypisanie", P.D("kto", kto, "nazwa", nazwa, "projekt", projekt, "modul", modul, "strona", strona, "sesja", moja.Sesja, "role", moja.Role));
                var przekazane = new List<string>();
                foreach (var x in poprzednie) przekazane.AddRange(Przejmij(x.Nazwa, nazwa, projekt, modul));
                P.Sygnal(nazwa, "przypisz");
                return P.D("nazwa", nazwa, "projekt", projekt, "modul", modul, "strona", strona, "role", moja.Role,
                    "przejeto_od", poprzednie.Select(x => x.Nazwa).ToList(), "przekazane", przekazane, "para", Para(projekt, modul));
            });
        }

        // Nowa sesja przejmuje adres: niepotwierdzone wiadomości poprzedniej przechodzą do niej, nic nie ginie.
        static List<string> Przejmij(string stary, string nowy, string projekt, string modul)
        {
            var r = new List<string>();
            var dir = Path.Combine(P.Root, P.Folder(stary));
            if (!Directory.Exists(dir)) return r;
            var js = P.Js();
            foreach (var f in new DirectoryInfo(dir).GetFiles("*.json"))
            {
                var m = P.Wczytaj(f, P.Folder(stary), js);
                if (m.Blad != null || m.Do != stary) continue;
                if (m.Projekt.Length > 0 && m.Projekt != projekt) continue;
                var od = m.Od == P.Wlasciciel || m.Od == P.Straznik || P.PoprawnyNadawca(m.Od) ? m.Od : P.Straznik;
                var w = P.Wyslij(new P.Nowa
                {
                    Od = od, Do = nowy, Projekt = projekt, Modul = m.Modul.Length > 0 ? m.Modul : modul, Typ = P.Typy.Contains(m.Typ) ? m.Typ : "informacja",
                    Temat = m.Temat, Tresc = "[Przekazane po przejęciu modułu: wiadomość była do " + stary + "]\n\n" + m.Uwagi, Pilne = m.Pilne,
                    Watek = m.WatekPole.Length > 0 ? m.WatekPole : m.Id, Czesci = m.Czesci
                });
                P.PotwierdzPlik(m, stary, "przyjeta", "przekazana do " + nowy + " (" + w["id"] + ")");
                r.Add((string)w["id"]);
            }
            return r;
        }

        // ---------- role ----------
        public static string PlikRoli(string rola) { return Path.Combine(P.Root, "stan", "role", rola + ".json"); }

        public static void RoleDomyslnePliki()
        {
            var opisy = new Dictionary<string, string[]>
            {
                { "programista", new[] { "Zmiany w kodzie na wspólnej gałęzi pary, tylko w przydzielonych plikach i rezerwacjach.", "commit + pakiet przekazania (ecxp_przekaz)", "Bez push na gałąź główną, scalania i tagów." } },
                { "recenzent", new[] { "Niezależna recenzja zmiany drugiej strony pary.", "werdykt: przyjąć / poprawić / odrzucić, z uwagami", "Nie recenzuje własnej zmiany." } },
                { "tester", new[] { "Bramki projektu (np. cargo fmt, clippy -D warnings, cargo test z listą pominiętych, build --release) i testy okien.", "raport PASS/FAIL z dokładnymi poleceniami", "Wyniki FAIL i BLOCKED zapisuje takimi, jakie są." } },
                { "bezpieczenstwo", new[] { "Przegląd bezpieczeństwa zmian wrażliwych.", "raport zagrożeń i zaleceń", "Bez danych spraw i sekretów." } },
                { "koordynator", new[] { "Kieruje sprawy do par i ról, pilnuje terminów potwierdzeń.", "przydziały i przekazania", "Nie zmienia kodu." } },
                { "integrator", new[] { "Scalanie i wydania.", "commit scalający i raport bramek", "Push, scalenie i tagi tylko po decyzji operatora." } },
            };
            foreach (var kv in opisy)
            {
                var p = PlikRoli(kv.Key);
                if (File.Exists(p)) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllText(p, P.Js().Serialize(P.D("nazwa", kv.Key, "obowiazki", kv.Value[0], "wynik", kv.Value[1], "granice", kv.Value[2])), new UTF8Encoding(false));
            }
        }

        // Operator nadaje role sesji w module; sesja dostaje kartę roli jako kontekst.
        public static Dictionary<string, object> NadajRole(string nazwa, string projekt, string modul, List<string> role, string opis)
        {
            var r = Przypisz(nazwa, projekt, modul, "", role, opis, P.Wlasciciel);
            var karty = new List<string>();
            foreach (var rola in (List<string>)r["role"])
            {
                var def = new Dictionary<string, object>();
                try { def = P.Js().Deserialize<Dictionary<string, object>>(P.Czytaj(PlikRoli(rola))) ?? def; } catch { }
                var tresc = "Twoja rola w projekcie " + projekt + ", moduł " + modul + ": " + rola + ".\n" +
                    (P.S(def, "obowiazki").Length > 0 ? "Obowiązki: " + P.S(def, "obowiazki") + "\n" : "") +
                    (P.S(def, "wynik").Length > 0 ? "Wynik: " + P.S(def, "wynik") + "\n" : "") +
                    (P.S(def, "granice").Length > 0 ? "Granice: " + P.S(def, "granice") + "\n" : "") +
                    ((opis ?? "").Length > 0 ? "Od operatora: " + opis + "\n" : "") +
                    "Zasada 1:1: pracujesz w parze z drugą stroną modułu na wspólnej gałęzi; wszystko, co dostajesz od operatora, dostaje też ona.";
                var w = P.Wyslij(new P.Nowa
                {
                    Od = P.Wlasciciel, Do = nazwa, Projekt = projekt, Modul = modul, Typ = "informacja", Temat = "ROLA-" + rola, Tresc = tresc,
                    Czesci = new List<Dictionary<string, object>> { P.D("rodzaj", "dane", "schemat", "ecxp/karta-roli", "dane", P.D("rola", rola, "projekt", projekt, "modul", modul, "definicja", def, "opis_operatora", opis ?? "")) }
                });
                karty.Add((string)w["id"]);
            }
            r["karty_rol"] = karty;
            return r;
        }

        // ---------- adresy ----------
        public static List<string> Rozwiaz(string projekt, string adres)
        {
            adres = (adres ?? "").Trim().ToLowerInvariant();
            if (adres.Length == 0) throw new EcxpException("Podaj adresata.");
            if (P.PoprawnyAdresat(adres)) return new List<string> { adres };
            Func<string> wymagajProjektu = () =>
            {
                if (projekt.Length == 0) throw new EcxpException("Adres '" + adres + "' wymaga projektu – podaj pole projekt.");
                return projekt;
            };
            if (adres.StartsWith("para:") || adres.StartsWith("*/"))
            {
                var modul = adres.Substring(adres.IndexOf(adres.StartsWith("*/") ? '/' : ':') + 1);
                var p = Para(wymagajProjektu(), modul);
                var brak = new List<string>();
                if (((string)p["codex"]).Length == 0) brak.Add("Codex");
                if (((string)p["claude"]).Length == 0) brak.Add("Claude");
                if (brak.Count > 0)
                    throw new EcxpException("Para modułu '" + modul + "' w projekcie '" + projekt + "' jest niepełna – brak sesji: " + string.Join(", ", brak) +
                        ". Zasada 1:1 wymaga obu stron. Przypisz brakującą sesję (ecxp_przypisz albo konsola ECXP-MAILBOX → Agenci).");
                return new List<string> { (string)p["codex"], (string)p["claude"] };
            }
            if (adres.StartsWith("codex/") || adres.StartsWith("claude/"))
            {
                var strona = adres.Substring(0, adres.IndexOf('/'));
                var modul = adres.Substring(adres.IndexOf('/') + 1);
                var n = (string)Para(wymagajProjektu(), modul)[strona];
                if (n.Length == 0) throw new EcxpException("Brak sesji " + strona + " dla modułu '" + modul + "' w projekcie '" + projekt + "'. Przypisz ją.");
                return new List<string> { n };
            }
            if (adres.StartsWith("rola:"))
            {
                var r = adres.Substring(5);
                var at = r.IndexOf('@');
                var rola = at >= 0 ? r.Substring(0, at) : r;
                var modul = at >= 0 ? r.Substring(at + 1) : "*";
                var pr = wymagajProjektu();
                var lst = Trasy().Where(x => x.Aktywna && x.Projekt == pr && (modul == "*" || x.Modul == modul) && x.Role.Contains(rola)).Select(x => x.Nazwa).Distinct().ToList();
                if (lst.Count == 0) throw new EcxpException("Nikt nie pełni roli '" + rola + "' w module '" + modul + "' projektu '" + pr + "'. Nadaj rolę w konsoli.");
                return lst;
            }
            throw new EcxpException("Nieznany adres '" + adres + "'. Dozwolone: wlasciciel, codex-<x>, claude-<x>, para:<moduł>, codex/<moduł>, claude/<moduł>, rola:<rola>@<moduł>.");
        }

        // ---------- wysyłanie z zasadą 1:1 ----------
        public static Dictionary<string, object> Wyslij(P.Nowa n)
        {
            return P.ZBlokada(delegate
            {
                if (n.Projekt.Length == 0 && n.OdpNa.Length > 0)
                {
                    var org = P.Znajdz(n.OdpNa);
                    if (org != null) { n.Projekt = org.Projekt; if (n.Modul == "wspolne" && org.Modul.Length > 0) n.Modul = org.Modul; }
                }
                n.Projekt = P.Slug(n.Projekt, 32, false);
                var cele = Rozwiaz(n.Projekt, n.Do);
                string lustroId = cele.Count > 1 ? "L-" + DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + P.Losowe(3) : "";
                if (lustroId.Length > 0 && n.Watek.Length == 0 && n.OdpNa.Length == 0) n.Watek = lustroId;
                var wyslane = new List<object>();
                Dictionary<string, object> pierwsza = null;
                foreach (var c in cele)
                {
                    var k = n.Kopia(); k.Do = c;
                    if (cele.Count > 1) { k.Lustro = cele; k.LustroId = lustroId; }
                    var r = P.Wyslij(k);
                    if (pierwsza == null) pierwsza = r;
                    wyslane.Add(r);
                }
                // Kopie dla pary (zasada 1:1): to, co trafia między operatorem a jedną stroną pary, dostaje też druga.
                var kopie = new List<object>();
                var zrodlo = (string)pierwsza["id"];
                var watek = n.Watek.Length > 0 ? n.Watek : zrodlo;
                if (n.Od == P.Wlasciciel)
                {
                    foreach (var c in cele.Where(P.PoprawnyNadawca).ToList())
                        foreach (var t in Przynaleznosc(c).Where(x => n.Projekt.Length == 0 || x.Projekt == n.Projekt))
                        {
                            var partner = Partner(t.Projekt, t.Modul, c);
                            if (partner.Length == 0 || cele.Contains(partner)) continue;
                            kopie.Add(KopiaDlaPary(n, partner, t, "Operator napisał do " + P.Nazwa(c) + " (" + c + "):", zrodlo, watek));
                        }
                }
                else if (P.PoprawnyNadawca(n.Od) && cele.Contains(P.Wlasciciel))
                {
                    foreach (var t in Przynaleznosc(n.Od).Where(x => n.Projekt.Length == 0 || x.Projekt == n.Projekt))
                    {
                        var partner = Partner(t.Projekt, t.Modul, n.Od);
                        if (partner.Length == 0 || cele.Contains(partner)) continue;
                        kopie.Add(KopiaDlaPary(n, partner, t, P.Nazwa(n.Od) + " (" + n.Od + ") napisał do operatora:", zrodlo, watek));
                    }
                }
                var wynik = new Dictionary<string, object>(pierwsza);
                wynik["adresaci"] = cele;
                wynik["wyslane"] = wyslane;
                if (lustroId.Length > 0) wynik["lustro_id"] = lustroId;
                wynik["kopie_dla_pary"] = kopie;
                return wynik;
            });
        }

        static object KopiaDlaPary(P.Nowa n, string partner, Trasa t, string naglowek, string zrodlo, string watek)
        {
            var r = P.Wyslij(new P.Nowa
            {
                Od = n.Od, Do = partner, Projekt = t.Projekt, Modul = t.Modul, Typ = "informacja", Temat = n.Temat.Length > 0 ? n.Temat : "KOPIA-DLA-PARY",
                Tresc = naglowek + "\n\n" + n.Tresc, Watek = watek, KopiaDla = zrodlo, Czesci = n.Czesci, Pliki = n.Pliki, Commit = n.Commit
            });
            return P.D("id", r["id"], "do", partner);
        }

        // ---------- nowe zadanie / moduł: para o tych samych nazwach po obu stronach ----------
        public static Dictionary<string, object> NoweZadanie(string projekt, string modul, string tytul, string opis, List<string> roleCodex, List<string> roleClaude, string znacznik, List<Dictionary<string, object>> czesci = null)
        {
            projekt = P.Slug(projekt, 32, false); modul = P.Slug(modul, 24, false);
            if (projekt.Length == 0 || modul.Length == 0) throw new EcxpException("Podaj projekt i nazwę modułu lub zadania.");
            tytul = (tytul ?? "").Replace(znacznik, "").Trim();          // znacznik dodaje protokół – bez podwajania
            if (tytul.Length == 0) tytul = modul;
            if ((opis ?? "").Trim().Length == 0) throw new EcxpException("Opisz zadanie – trafi do obu stron pary.");
            var codex = "codex-" + modul; var claude = "claude-" + modul;
            var tytulSesji = znacznik + " " + tytul.Trim();
            return P.ZBlokada(delegate
            {
                // projekt.json: moduł i para
                var dir = Path.Combine(P.Root, "projekty", projekt);
                Directory.CreateDirectory(dir);
                var pp = Path.Combine(dir, "projekt.json");
                var pr = new Dictionary<string, object>();
                try { pr = P.Js().Deserialize<Dictionary<string, object>>(P.Czytaj(pp)) ?? pr; } catch { }
                if (P.S(pr, "nazwa").Length == 0) pr["nazwa"] = projekt;
                var moduly = P.L(pr, "moduly"); if (!moduly.Contains(modul)) moduly.Add(modul); pr["moduly"] = moduly;
                object pv; var pary = pr.TryGetValue("pary", out pv) && pv is Dictionary<string, object> ? (Dictionary<string, object>)pv : new Dictionary<string, object>();
                pary[modul] = P.D("codex", codex, "claude", claude, "tytul", tytulSesji);
                pr["pary"] = pary;
                var tmp = pp + ".tmp";
                File.WriteAllText(tmp, P.Js().Serialize(pr), new UTF8Encoding(false));
                if (File.Exists(pp)) File.Replace(tmp, pp, null); else File.Move(tmp, pp);

                Przypisz(codex, projekt, modul, "", roleCodex, tytul, P.Wlasciciel);
                Przypisz(claude, projekt, modul, "", roleClaude, tytul, P.Wlasciciel);

                var pakiety = new Dictionary<string, object>();
                foreach (var strona in new[] { "codex", "claude" })
                {
                    var ja = strona == "codex" ? codex : claude;
                    var partner = strona == "codex" ? claude : codex;
                    var role = strona == "codex" ? roleCodex : roleClaude;
                    var txt = "Nazwa tej sesji: " + tytulSesji + "\n\n" +
                        "Jesteś sesją " + (strona == "codex" ? "ChatGPT/Codex" : "Claude") + " w parze ECXP (zasada 1:1) projektu " + projekt + ", moduł " + modul + ".\n" +
                        "Twoja nazwa w ECXP: " + ja + ". Druga strona pary: " + partner + ". Role: " + (role.Count > 0 ? string.Join(", ", role) : "do ustalenia przez operatora") + ".\n\n" +
                        "Na początku: wywołaj narzędzie ecxp_przypisz (ja=" + ja + ", projekt=" + projekt + ", modul=" + modul + ", sesja=<identyfikator tej sesji, jeśli go znasz>), potem ecxp_odbierz (ja=" + ja + ").\n" +
                        "Każdą odebraną wiadomość potwierdzaj (ecxp_potwierdz albo odpowiedź ecxp_wyslij z odpowiedz_na). Do drugiej strony pisz na adres " + (strona == "codex" ? "claude/" : "codex/") + modul + ", do obu i operatora – para:" + modul + " / wlasciciel.\n" +
                        "Z drugą stroną ustalcie wspólną gałąź roboczą i commit bazowy, zanim zaczniecie zmiany; nie piszcie naraz w tych samych plikach.\n\n" +
                        "Zadanie od operatora:\n" + opis.Trim();
                    var sd = Path.Combine(P.Root, "stan", "start");
                    Directory.CreateDirectory(sd);
                    File.WriteAllText(Path.Combine(sd, ja + ".txt"), txt, new UTF8Encoding(false));
                    pakiety[strona] = P.D("nazwa", ja, "tytul_sesji", tytulSesji, "plik", Path.Combine(sd, ja + ".txt"), "tekst", txt);
                }
                var w = Wyslij(new P.Nowa { Od = P.Wlasciciel, Do = "para:" + modul, Projekt = projekt, Modul = modul, Typ = "zadanie", Temat = tytul, Tresc = opis.Trim(), Czesci = czesci ?? new List<Dictionary<string, object>>() });
                P.Zdarzenie("nowe_zadanie", P.D("projekt", projekt, "modul", modul, "codex", codex, "claude", claude, "tytul", tytulSesji));
                return P.D("projekt", projekt, "modul", modul, "tytul_sesji", tytulSesji, "para", Para(projekt, modul), "pakiety", pakiety, "zadanie", w);
            });
        }

        // Para, trasy i role dla projektu (narzędzie ecxp_trasy i konsola).
        public static Dictionary<string, object> Opis(string projekt)
        {
            projekt = P.Slug(projekt ?? "", 32, false);
            var t = Trasy().Where(x => x.Aktywna && (projekt.Length == 0 || x.Projekt == projekt)).ToList();
            var moduly = t.Select(x => x.Projekt + "|" + x.Modul).Distinct().ToList();
            foreach (var pr in P.Projekty().Where(x => projekt.Length == 0 || P.S(x, "nazwa") == projekt))
            {
                object pv;
                if (pr.TryGetValue("pary", out pv) && pv is Dictionary<string, object>)
                    foreach (var k in ((Dictionary<string, object>)pv).Keys) if (!moduly.Contains(P.S(pr, "nazwa") + "|" + k)) moduly.Add(P.S(pr, "nazwa") + "|" + k);
            }
            var pary = moduly.Select(x => (object)Para(x.Split('|')[0], x.Split('|')[1])).ToList();
            var trasy = t.Select(x => (object)P.D("projekt", x.Projekt, "modul", x.Modul, "strona", x.Strona, "nazwa", x.Nazwa, "sesja", x.Sesja, "role", x.Role, "opis", x.Opis,
                "od", x.Od.ToString("yyyy-MM-dd HH:mm"))).ToList();
            return P.D("projekt", projekt, "pary", pary, "trasy", trasy, "role_dostepne", RoleDomyslne);
        }
    }
}
