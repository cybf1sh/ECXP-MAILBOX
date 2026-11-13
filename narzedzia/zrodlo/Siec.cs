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
                if (kto == nazwa) P.Sygnal(nazwa, "przypisz");     // kontakt węzła liczy się tylko, gdy zgłosił się sam (nie operator za niego)
                return P.D("nazwa", nazwa, "projekt", projekt, "modul", modul, "strona", strona, "role", moja.Role,
                    "przejeto_od", poprzednie.Select(x => x.Nazwa).ToList(), "przekazane", przekazane, "para", Para(projekt, modul));
            });
        }

        // Sesje znalezione na stanowisku (okno „Pobieranie danych”, np. po instalacji): pakiet startowy w zapisie rozmowy Codexa albo Claude.
        // Trasa powstaje tylko, gdy moduł nie ma jeszcze aktywnej sesji tej strony; ta sama nazwa bez identyfikatora sesji dostaje go.
        // Nigdy nie zastępuje innej aktywnej sesji (bez przejmowania wiadomości) i niczego nie kasuje.
        public static Dictionary<string, object> ZarejestrujZnalezione(List<Dictionary<string, object>> lista)
        {
            return P.ZBlokada(delegate
            {
                var wyniki = new List<object>();
                int nowe = 0, uzupelnione = 0, bez = 0, pominiete = 0;
                foreach (var z in lista ?? new List<Dictionary<string, object>>())
                {
                    string nazwa = P.S(z, "nazwa").Trim().ToLowerInvariant(), projekt = P.Slug(P.S(z, "projekt"), 32, false), modul = P.Slug(P.S(z, "modul"), 24, false);
                    string sesja = P.S(z, "sesja").Trim(), opis = P.S(z, "opis").Trim();
                    Func<string, string, object> wynik = (stan, txt) => P.D("nazwa", nazwa, "projekt", projekt, "modul", modul, "stan", stan, "opis", txt);
                    if (!P.PoprawnyNadawca(nazwa) || projekt.Length == 0 || modul.Length == 0) { pominiete++; wyniki.Add(wynik("pominieta", "niepełne dane w pakiecie")); continue; }
                    if (!System.Text.RegularExpressions.Regex.IsMatch(sesja, "^[0-9a-f-]{36}$")) sesja = "";
                    var strona = Strona(nazwa);
                    var t = Trasy();
                    var ta = t.FirstOrDefault(x => x.Aktywna && x.Nazwa == nazwa && x.Projekt == projekt && x.Modul == modul);
                    var inna = t.FirstOrDefault(x => x.Aktywna && x.Projekt == projekt && x.Modul == modul && x.Strona == strona && x.Nazwa != nazwa);
                    if (inna != null) { pominiete++; wyniki.Add(wynik("pominieta", "moduł ma już aktywną sesję " + inna.Nazwa + " – bez zmian")); continue; }
                    if (ta != null && (ta.Sesja.Length > 0 || sesja.Length == 0)) { bez++; wyniki.Add(wynik("znana", "już zarejestrowana")); ZapiszHookSesje(strona, sesja, nazwa); continue; }
                    var role = ta != null && ta.Role.Count > 0 ? ta.Role : (strona == "codex" ? new List<string> { "programista" } : new List<string> { "recenzent", "tester" });
                    Przypisz(nazwa, projekt, modul, sesja, role, opis.Length > 0 ? opis : "znaleziona na stanowisku (pobieranie danych)", P.Wlasciciel);
                    DopiszPare(projekt, modul, strona, nazwa);
                    ZapiszHookSesje(strona, sesja, nazwa);
                    if (ta != null) { uzupelnione++; wyniki.Add(wynik("uzupelniona", "dopisany identyfikator sesji")); }
                    else { nowe++; wyniki.Add(wynik("nowa", "zarejestrowana jako sesja pary")); }
                }
                P.Zdarzenie("pobranie_danych", P.D("nowe", nowe, "uzupelnione", uzupelnione, "znane", bez, "pominiete", pominiete));
                return P.D("nowe", nowe, "uzupelnione", uzupelnione, "znane", bez, "pominiete", pominiete, "wyniki", wyniki);
            });
        }

        // projekt.json: moduł i strona pary (tylko, gdy ta strona jest jeszcze pusta).
        static void DopiszPare(string projekt, string modul, string strona, string nazwa)
        {
            var dir = Path.Combine(P.Root, "projekty", projekt);
            Directory.CreateDirectory(dir);
            var pp = Path.Combine(dir, "projekt.json");
            var pr = new Dictionary<string, object>();
            try { pr = P.Js().Deserialize<Dictionary<string, object>>(P.Czytaj(pp)) ?? pr; } catch { }
            if (P.S(pr, "nazwa").Length == 0) pr["nazwa"] = projekt;
            var moduly = P.L(pr, "moduly"); if (!moduly.Contains(modul)) moduly.Add(modul); pr["moduly"] = moduly;
            object pv; var pary = pr.TryGetValue("pary", out pv) && pv is Dictionary<string, object> ? (Dictionary<string, object>)pv : new Dictionary<string, object>();
            object sv; var para = pary.TryGetValue(modul, out sv) && sv is Dictionary<string, object> ? (Dictionary<string, object>)sv : new Dictionary<string, object>();
            if (P.S(para, strona).Length == 0) para[strona] = nazwa;
            pary[modul] = para; pr["pary"] = pary;
            var tmp = pp + ".tmp";
            File.WriteAllText(tmp, P.Js().Serialize(pr), new UTF8Encoding(false));
            if (File.Exists(pp)) File.Replace(tmp, pp, null); else File.Move(tmp, pp);
        }

        // Mapa sesja Claude → nazwa ECXP (jak zapisuje ją hook-claude.ps1); tylko gdy nie ma jeszcze rozpoznanej nazwy.
        static void ZapiszHookSesje(string strona, string sesja, string nazwa)
        {
            if (strona != "claude" || sesja.Length == 0) return;
            try
            {
                var dir = Path.Combine(P.Root, "stan", "hook-sesje");
                Directory.CreateDirectory(dir);
                var p = Path.Combine(dir, sesja + ".txt");
                var obecna = File.Exists(p) ? P.Czytaj(p).Trim() : "";
                if (obecna.StartsWith("claude-")) return;
                File.WriteAllText(p, nazwa, new UTF8Encoding(false));
            }
            catch { }
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
                if (UsuwanieProjektu.Trwa(n.Projekt)) throw new EcxpException("Projekt jest w trakcie usuwania; nie utworzono wiadomości.");
                // obieg zadania: „w trakcie” i FIN agenta zapisuje stos; do operatora trafia tylko końcowy FIN
                if (P.PoprawnyNadawca(n.Od))
                {
                    var ob = Obieg.ZAgenta(n);
                    if (ob != null) return ob;
                }
                // adres modułu (para:, codex/, claude/) musi zgadzać się z modułem wiadomości – inaczej wiadomość trafiłaby do innej pary
                var mAdr = System.Text.RegularExpressions.Regex.Match((n.Do ?? "").Trim().ToLowerInvariant(), @"^(?:para:|\*/|codex/|claude/)([a-z0-9-]+)$");
                if (mAdr.Success)
                {
                    var modAdr = mAdr.Groups[1].Value;
                    n.Modul = P.Slug(n.Modul ?? "", 24, false);               // to samo, co zapisze P.Wyslij – porównujemy po normalizacji
                    if (n.Modul.Length == 0 || n.Modul == "wspolne") n.Modul = modAdr;
                    else if (n.Modul != modAdr)
                        throw new EcxpException("Adres '" + n.Do + "' to sesje modułu '" + modAdr + "', a wiadomość ma moduł '" + n.Modul +
                            "'. Wiadomość trafiłaby do innej pary – popraw adres (np. para:" + n.Modul + ") albo moduł.");
                }
                var cele = Rozwiaz(n.Projekt, n.Do);
                ZasadaOdpowiedzi(n, cele);
                // para wstrzymana po rozbieżności gałęzi: agenci nie dostają nowych zadań, dopóki operator jej nie wznowi
                if (n.Od != P.Wlasciciel && n.Typ == "zadanie")
                    foreach (var c in cele.Where(P.PoprawnyNadawca))
                        foreach (var t in Przynaleznosc(c).Where(x => n.Projekt.Length == 0 || x.Projekt == n.Projekt))
                            if (Galezie.Wstrzymana(t.Projekt, t.Modul))
                                throw new EcxpException("Para " + t.Projekt + "/" + t.Modul + " jest wstrzymana po rozbieżności wspólnej gałęzi – czeka na decyzję operatora.");
                string lustroId = cele.Count > 1 ? "L-" + DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + P.Losowe(3) : "";
                if (lustroId.Length > 0 && n.Watek.Length == 0 && n.OdpNa.Length == 0) n.Watek = lustroId;
                var wyslane = new List<object>();
                Dictionary<string, object> pierwsza = null;
                // ecxp/1.1: jedna wiadomość = jeden identyfikator (id kopii dla pierwszego adresata według nazwy); pozostałe kopie go niosą
                foreach (var c in cele.Count > 1 ? cele.OrderBy(x => x).ToList() : cele)
                {
                    var k = n.Kopia(); k.Do = c;
                    if (cele.Count > 1) { k.Lustro = cele; k.LustroId = lustroId; }
                    if (pierwsza != null) k.Logiczne = (string)pierwsza["id"];
                    var r = P.Wyslij(k);
                    if (pierwsza == null) pierwsza = r;
                    wyslane.Add(r);
                }
                // Bez kopii dla drugiej strony pary: wynik zadania przekazuje obieg (Obieg.ZAgenta) – jak najmniej wiadomości.
                var kopie = new List<object>();
                var zrodlo = (string)pierwsza["id"];
                if (n.Od == P.Wlasciciel && Typ(n.Typ) == "zadanie") Obieg.Zalozone(n.Projekt, n.Modul, n.Temat.Length > 0 ? n.Temat : P.Baza((string)P.Znajdz(zrodlo).Temat), wyslane, lustroId);
                var wynik = new Dictionary<string, object>(pierwsza);
                // adresat bez kontaktu od doby (albo nigdy): wiadomość zostaje wysłana, usługa doręczania spróbuje go obudzić
                var ostrz = new List<string>();
                // ecxp/1.1: jeden zapis obecności z rodzajem adresata (sesja w aplikacji / agent w tle) – ostrzeżenie tylko, gdy adresat nie odbierze
                foreach (var ob in Obecnosci("").Where(x => cele.Contains(P.S(x, "nazwa")) && !P.B(x, "dostepny")))
                    ostrz.Add("Adresat " + P.S(ob, "nazwa") + ": " + P.S(ob, "opis") + ".");
                if (ostrz.Count > 0)
                {
                    ostrz.Add("Wiadomość wysłana. Stos spróbuje obudzić adresata w jego sesji; jeśli się nie uda, dostaniesz alarm „NIEDORECZONA”.");
                    wynik["ostrzezenia"] = ostrz;
                }
                wynik["adresaci"] = cele;
                wynik["wyslane"] = wyslane;
                if (lustroId.Length > 0) wynik["lustro_id"] = lustroId;
                wynik["kopie_dla_pary"] = kopie;
                return wynik;
            });
        }

        static string Typ(string t) { return (t ?? "").Trim().ToLowerInvariant(); }

        // ---------- ecxp/1.1: obecność adresatów ----------
        // Dla każdego adresata z tras (albo jednego, gdy podano nazwę): rodzaj (tlo / sesja), ostatni kontakt, czy odbierze wiadomość, opis.
        //  * agent w tle: dostępny, gdy jest aktywny i usługa agentów jego strony działa (odzywa się tylko w turach);
        //  * sesja w aplikacji: dostępna, gdy odezwała się w ciągu doby.
        public static List<Dictionary<string, object>> Obecnosci(string nazwa)
        {
            var wezly = P.Wezly().GroupBy(x => x.Nazwa).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Ostatnio).First());
            var wTle = Agenci.Lista().Where(a => P.B(a, "aktywny")).GroupBy(a => P.S(a, "nazwa")).ToDictionary(g => g.Key, g => g.First());
            var nazwy = Trasy().Where(t => t.Aktywna).Select(t => t.Nazwa).Concat(wTle.Keys).Concat(wezly.Keys).Where(P.PoprawnyNadawca).Distinct()
                .Where(x => string.IsNullOrEmpty(nazwa) || x == nazwa).OrderBy(x => x).ToList();
            if (!string.IsNullOrEmpty(nazwa) && nazwy.Count == 0 && P.PoprawnyNadawca(nazwa)) nazwy.Add(nazwa);
            var wynik = new List<Dictionary<string, object>>();
            foreach (var n in nazwy)
            {
                Wezel w; wezly.TryGetValue(n, out w);
                Dictionary<string, object> ag; wTle.TryGetValue(n, out ag);
                var d = P.D("nazwa", n, "rodzaj", ag != null ? "tlo" : "sesja", "ostatnio", w != null ? w.Ostatnio.ToString("yyyy-MM-dd'T'HH:mm:ss") : "");
                if (ag != null)
                {
                    bool stoi = Agenci.Zatrzymana(P.S(ag, "strona"));
                    d["dostepny"] = !stoi;
                    d["opis"] = stoi ? "agent w tle, ale usługa agentów " + P.S(ag, "strona") + " jest zatrzymana – odbierze wiadomość dopiero po jej uruchomieniu (Agenci → Start)"
                                     : "agent w tle, usługa działa" + (w != null ? " (ostatnia tura " + P.Wiek(DateTime.Now - w.Ostatnio) + " temu)" : "");
                }
                else if (w == null) { d["dostepny"] = false; d["opis"] = "sesja w aplikacji jeszcze nigdy nie odezwała się w ECXP"; }
                else
                {
                    bool dawno = (DateTime.Now - w.Ostatnio).TotalHours >= 24;
                    d["dostepny"] = !dawno;
                    d["opis"] = dawno ? "sesja w aplikacji nie odzywa się od " + P.Wiek(DateTime.Now - w.Ostatnio) + " (ostatni kontakt " + w.Ostatnio.ToString("dd.MM.yyyy HH:mm") + ")"
                                      : "sesja w aplikacji, ostatni kontakt " + P.Wiek(DateTime.Now - w.Ostatnio) + " temu";
                }
                wynik.Add(d);
            }
            return wynik;
        }

        // Zasada „pytanie → odpowiedź” dla agentów:
        //  * typ=odpowiedz musi wskazywać wiadomość, na którą odpowiada (odpowiedz_na);
        //  * kto ma od adresata nieodpowiedziane pytanie albo zadanie, nie wysyła mu informacji ani meldunku obok –
        //    odpowiada na nie (typ=odpowiedz, odpowiedz_na=<id>). Doręczenie pilnuje stos, meldunki „czekam/przyjąłem” są zbędne.
        // Pytanie zwrotne i blokada są dozwolone (agent może czegoś potrzebować, zanim odpowie).
        static void ZasadaOdpowiedzi(P.Nowa n, List<string> cele)
        {
            if (!P.PoprawnyNadawca(n.Od)) return;
            var typ = Typ(n.Typ);
            if (typ == "odpowiedz" && n.OdpNa.Length == 0)
                throw new EcxpException("Odpowiedź musi wskazywać wiadomość, na którą odpowiadasz: podaj odpowiedz_na=<id tej wiadomości>.");
            if (n.OdpNa.Length > 0 || (typ != "informacja" && typ != "meldunek")) return;
            // zadanie operatora w obiegu: do operatora idzie tylko FIN (albo pytanie, gdy agent czegoś potrzebuje)
            if (cele.Contains(P.Wlasciciel))
            {
                var ob = Obieg.OtwarteDla(n.Od, n.Projekt);
                if (ob != null)
                    throw new EcxpException("Zadanie „" + P.S(ob, "temat") + "” jest w obiegu: operator dostaje tylko FIN. Meldunki pośrednie nie są potrzebne – stos sam zapisuje, że pracujesz. " +
                        "Po wykonaniu: ecxp_wyslij(do=wlasciciel, typ=fin, wynik=pozytywny|negatywny|niewykonane, odpowiedz_na=<id zadania>). Potrzebujesz czegoś – typ=pytanie.");
            }
            var wszystkie = P.Wszystkie(false);
            var odpowiedziane = new HashSet<string>(wszystkie.Where(x => x.Od == n.Od && x.OdpNa.Length > 0).Select(x => x.OdpNa), StringComparer.OrdinalIgnoreCase);
            var czeka = wszystkie.Where(m => m.Do == n.Od && cele.Contains(m.Od) && (m.Typ == "pytanie" || m.Typ == "zadanie") && m.KopiaDla.Length == 0
                    && (n.Projekt.Length == 0 || m.Projekt == n.Projekt) && !odpowiedziane.Contains(m.Id))
                .OrderBy(m => m.Czas).FirstOrDefault();
            if (czeka != null)
                throw new EcxpException("Najpierw odpowiedz na " + (czeka.Typ == "zadanie" ? "zadanie" : "pytanie") + " „" + czeka.Temat + "” od " + czeka.Od +
                    ": ecxp_wyslij(do=" + czeka.Od + ", typ=odpowiedz, odpowiedz_na=" + czeka.Id + "). Meldunki pośrednie („czekam”, „przyjąłem”) nie są potrzebne – doręczenie pilnuje stos.");
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

        // Wybrane strony zadania: "codex" i/lub "claude"; pusta lista = obie (zasada 1:1, jak dotąd).
        public static List<string> Strony(List<string> wybrane)
        {
            var s = (wybrane ?? new List<string>()).Select(x => (x ?? "").Trim().ToLowerInvariant()).Where(x => x == "codex" || x == "claude").Distinct().ToList();
            return s.Count == 0 ? new List<string> { "codex", "claude" } : new[] { "codex", "claude" }.Where(s.Contains).ToList();
        }

        // ---------- nowe zadanie / moduł: para o tych samych nazwach po obu stronach (albo tylko wybrana strona) ----------
        // Ikony projektu i modułu (wybór w „Nowe zadanie / moduł”), zapisywane w projekt.json: "ikona" i "ikony_modulow".
        public static readonly string[] Ikony = { "🖥", "🛡", "📄", "🗄", "🤖", "💡" };

        // Brakujące pole nowego zadania: "projekt", "modul", "opis" albo "" – te same warunki sprawdza konsola przed zamknięciem okna.
        public static string BrakujacePole(string projekt, string modul, string opis)
        {
            if (P.Slug(projekt, 32, false).Length == 0) return "projekt";
            if (P.Slug(modul, 24, false).Length == 0) return "modul";
            if ((opis ?? "").Trim().Length == 0) return "opis";
            return "";
        }

        public static Dictionary<string, object> NoweZadanie(string projekt, string modul, string tytul, string opis, List<string> roleCodex, List<string> roleClaude, string znacznik, List<Dictionary<string, object>> czesci = null, bool wyslijZadanie = true, List<string> strony = null,
            string ikonaProjektu = null, string ikonaModulu = null, bool repozytorium = false, bool sprawdzanie = true)
        {
            var brak = BrakujacePole(projekt, modul, opis);
            projekt = P.Slug(projekt, 32, false); modul = P.Slug(modul, 24, false);
            if (brak == "projekt" || brak == "modul") throw new EcxpException("Podaj projekt i nazwę modułu lub zadania.");
            tytul = (tytul ?? "").Replace(znacznik, "").Trim();          // znacznik dodaje protokół – bez podwajania
            if (tytul.Length == 0) tytul = modul;
            strony = Strony(strony);
            var obie = strony.Count == 2;
            if (brak == "opis") throw new EcxpException(obie ? "Opisz zadanie – trafi do obu stron pary." : "Opisz zadanie – trafi do wybranego agenta.");
            var tytulSesji = znacznik + " " + tytul.Trim();
            return P.ZBlokada(delegate
            {
                // zadanie trafia do sesji, która już prowadzi moduł; nowa nazwa codex-/claude-<moduł> tylko, gdy takiej sesji nie ma
                if (UsuwanieProjektu.Trwa(projekt)) throw new EcxpException("Projekt jest w trakcie usuwania; nie utworzono zadania.");
                var istn = Para(projekt, modul);
                var codex = P.S(istn, "codex").Length > 0 ? P.S(istn, "codex") : "codex-" + modul;
                var claude = P.S(istn, "claude").Length > 0 ? P.S(istn, "claude") : "claude-" + modul;
                // projekt.json: moduł i para
                var dir = Path.Combine(P.Root, "projekty", projekt);
                Directory.CreateDirectory(dir);
                var pp = Path.Combine(dir, "projekt.json");
                var pr = new Dictionary<string, object>();
                try { pr = P.Js().Deserialize<Dictionary<string, object>>(P.Czytaj(pp)) ?? pr; } catch { }
                if (P.S(pr, "nazwa").Length == 0) pr["nazwa"] = projekt;
                var moduly = P.L(pr, "moduly"); if (!moduly.Contains(modul)) moduly.Add(modul); pr["moduly"] = moduly;
                object pv; var pary = pr.TryGetValue("pary", out pv) && pv is Dictionary<string, object> ? (Dictionary<string, object>)pv : new Dictionary<string, object>();
                // strona bez zadania zostaje taka, jaka była w projekcie (albo pusta) – nie rejestrujemy jej na zapas
                object sv; var stara = pary.TryGetValue(modul, out sv) && sv is Dictionary<string, object> ? (Dictionary<string, object>)sv : new Dictionary<string, object>();
                // w parze jedna strona wykonuje, druga sprawdza wynik: wykonawcą jest strona z rolą „programista”, inaczej Codex
                bool cxProg = roleCodex.Contains("programista"), clProg = roleClaude.Contains("programista");
                var wykonawca = cxProg == clProg ? "codex" : cxProg ? "codex" : "claude";
                pary[modul] = P.D("codex", strony.Contains("codex") ? codex : P.S(stara, "codex"), "claude", strony.Contains("claude") ? claude : P.S(stara, "claude"), "tytul", tytulSesji,
                    "wykonawca", obie && sprawdzanie ? (wykonawca == "codex" ? codex : claude) : "");
                pr["pary"] = pary;
                // ikony: null = bez zmian, "" = usuń, inna wartość tylko z listy Ikony
                if (ikonaProjektu != null) { if (Ikony.Contains(ikonaProjektu)) pr["ikona"] = ikonaProjektu; else if (ikonaProjektu.Length == 0) pr.Remove("ikona"); }
                if (ikonaModulu != null)
                {
                    object iv; var ik = pr.TryGetValue("ikony_modulow", out iv) && iv is Dictionary<string, object> ? (Dictionary<string, object>)iv : new Dictionary<string, object>();
                    if (Ikony.Contains(ikonaModulu)) ik[modul] = ikonaModulu; else if (ikonaModulu.Length == 0) ik.Remove(modul);
                    if (ik.Count > 0) pr["ikony_modulow"] = ik; else pr.Remove("ikony_modulow");
                }
                var tmp = pp + ".tmp";
                File.WriteAllText(tmp, P.Js().Serialize(pr), new UTF8Encoding(false));
                if (File.Exists(pp)) File.Replace(tmp, pp, null); else File.Move(tmp, pp);

                if (strony.Contains("codex")) Przypisz(codex, projekt, modul, "", roleCodex, tytul, P.Wlasciciel);
                if (strony.Contains("claude")) Przypisz(claude, projekt, modul, "", roleClaude, tytul, P.Wlasciciel);

                var pakiety = new Dictionary<string, object>();
                foreach (var strona in strony)
                {
                    var ja = strona == "codex" ? codex : claude;
                    var partner = strona == "codex" ? claude : codex;
                    var role = strona == "codex" ? roleCodex : roleClaude;
                    var kim = strona == "codex" ? "ChatGPT/Codex" : "Claude";
                    // bez sprawdzania obie strony wykonują zadanie niezależnie (szybciej – jeden przebieg zamiast dwóch)
                    var rolaWParze = obie && sprawdzanie ? (strona == wykonawca ? "wykonawca" : "sprawdzajacy") : "sam";
                    var txt = Pakiet(tytulSesji, kim, ja, projekt, modul, role, obie ? partner : "", rolaWParze, repozytorium, opis.Trim() + Zalaczniki(czesci));
                    var sd = Path.Combine(P.Root, "stan", "start");
                    Directory.CreateDirectory(sd);
                    File.WriteAllText(Path.Combine(sd, ja + ".txt"), txt, new UTF8Encoding(false));
                    pakiety[strona] = P.D("nazwa", ja, "tytul_sesji", tytulSesji, "plik", Path.Combine(sd, ja + ".txt"), "tekst", txt, "rola", rolaWParze);
                }
                object w = null;
                if (wyslijZadanie) w = Wyslij(new P.Nowa
                {
                    Od = P.Wlasciciel, Do = obie ? "para:" + modul : strony[0] + "/" + modul, BezKopii = !obie, Projekt = projekt, Modul = modul, Typ = "zadanie", Temat = tytul,
                    Tresc = opis.Trim(), Czesci = czesci ?? new List<Dictionary<string, object>>()
                });
                P.Zdarzenie("nowe_zadanie", P.D("projekt", projekt, "modul", modul, "codex", strony.Contains("codex") ? codex : "", "claude", strony.Contains("claude") ? claude : "", "strony", strony, "tytul", tytulSesji));
                return P.D("projekt", projekt, "modul", modul, "tytul_sesji", tytulSesji, "para", Para(projekt, modul), "pakiety", pakiety, "strony", strony, "zadanie", w);
            });
        }

        // Pakiet startowy: kim jesteś, jak zacząć, zasada „jedno pytanie – jedna odpowiedź” i zadanie. Bez procedur, których zadanie nie wymaga.
        //   rola w parze: "wykonawca" (robi zadanie), "sprawdzajacy" (sprawdza wynik wykonawcy), "sam" (zadanie tylko dla tej strony).
        public static string Pakiet(string tytulSesji, string kim, string ja, string projekt, string modul, List<string> role, string partner, string rolaWParze, bool repozytorium, string zadanie)
        {
            var sb = new StringBuilder();
            sb.Append("Nazwa tej sesji: " + tytulSesji + " (jeśli możesz zmienić nazwę sesji, nadaj ją).\n\n");
            sb.Append("Jesteś " + kim + " w ECXP. Projekt " + projekt + ", moduł " + modul + ". Twoja nazwa w ECXP: " + ja + "." +
                (role != null && role.Count > 0 ? " Role: " + string.Join(", ", role) + "." : "") + "\n");
            sb.Append("Zacznij od: ecxp_przypisz(ja=" + ja + ", projekt=" + projekt + ", modul=" + modul + "), potem ecxp_odbierz(ja=" + ja + ").\n");
            sb.Append("Bez narzędzi ecxp_* (np. sesja w chmurze) nie wykonuj zadania – napisz operatorowi: „Brak narzędzi ECXP”.\n\n");
            sb.Append("Wykonujesz tylko to zadanie – nic poza nim (bez porządków, dodatkowych plików, commitów i czynności spoza projektu).\n\n");
            sb.Append("Komunikacja – tylko dwa kroki, resztę robi stos ECXP:\n");
            sb.Append("1. ecxp_odbierz – odbiór stos zapisuje jako przyjęcie (ACK). Nic nie potwierdzaj i nie wysyłaj meldunków („przyjąłem”, „czekam”).\n");
            sb.Append("2. Po wykonaniu JEDEN FIN: ecxp_wyslij(do=wlasciciel, typ=fin, wynik=pozytywny|negatywny|niewykonane, odpowiedz_na=<id zadania>, tresc=<krótko, co zrobiłeś>). Stos odpowie ACK i przekaże dalej. Zakończ turę.\n");
            sb.Append("Bez czegoś nie da się wykonać zadania? Jedno pytanie (typ=pytanie) i zakończ turę.\n\n");
            if (rolaWParze == "wykonawca")
                sb.Append("Ty wykonujesz zadanie, " + partner + " je sprawdzi – Twój FIN stos przekaże " + partner + ".\n\n");
            else if (rolaWParze == "sprawdzajacy")
                sb.Append("Zadanie wykonuje " + partner + ", Ty sprawdzasz. Teraz nic nie rób i zakończ turę. Gdy stos przekaże Ci FIN " + partner + ", sprawdź wynik i wyślij swój FIN (odpowiedz_na=<id Twojego zadania>).\n\n");
            else
                sb.Append("Zadanie dostałeś tylko Ty – Twój FIN trafi prosto do operatora.\n\n");
            if (repozytorium)
                sb.Append("Zadanie zmienia repozytorium: przed pierwszą zmianą " + (rolaWParze == "sam" ? "" : "uzgodnij z " + partner + " ") + "gałąź i commit bazowy (ecxp_umowa_galezi), " +
                    "po commicie ecxp_stan_galezi. Bez push i scalenia bez decyzji operatora.\n\n");
            sb.Append("Zadanie od operatora:\n" + zadanie);
            return sb.ToString();
        }

        // Lista załączników do pakietu: nazwa i lokalna kopia (tylko do odczytu) – obrazy można od razu otworzyć.
        static string Zalaczniki(List<Dictionary<string, object>> czesci)
        {
            if (czesci == null) return "";
            var sb = new StringBuilder();
            foreach (var c in czesci.Where(x => P.S(x, "rodzaj") == "plik"))
            {
                string sc;
                try { sc = P.KopiaZalacznika(P.S(c, "sha256"), P.S(c, "nazwa")); } catch (Exception ex) { sc = "niedostępny: " + ex.Message; }
                sb.Append("\n- " + P.S(c, "nazwa") + " (" + P.S(c, "typ") + "): " + sc);
            }
            return sb.Length == 0 ? "" : "\n\nZałączniki od operatora (otwórz plik z podanej ścieżki; ecxp_odbierz pokaże też obrazy):" + sb;
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
