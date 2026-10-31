// ECXP – sprawy pary: podpisane decyzje operatora, łańcuchy ról, wspólna gałąź pary (umowa, STAN_GALEZI, rozbieżność).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Ecxp
{
    static class Pliki
    {
        public static Dictionary<string, object> Czytaj(string p)
        {
            try { return P.Js().Deserialize<Dictionary<string, object>>(P.Czytaj(p)) ?? new Dictionary<string, object>(); }
            catch { return new Dictionary<string, object>(); }
        }
        public static void Zapisz(string p, object d)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(p));
            var tmp = p + "." + P.Losowe(3) + ".tmp";
            File.WriteAllText(tmp, P.Js().Serialize(d), new UTF8Encoding(false));
            if (File.Exists(p)) File.Replace(tmp, p, null); else File.Move(tmp, p);
        }
        public static Dictionary<string, object> Slownik(Dictionary<string, object> d, string k)
        {
            object v;
            if (d.TryGetValue(k, out v) && v is Dictionary<string, object>) return (Dictionary<string, object>)v;
            var n = new Dictionary<string, object>(); d[k] = n; return n;
        }
    }

    // ---------- podpisane decyzje operatora ----------
    // Klucz RSA-2048 operatora leży w stan\klucz-operatora.dpapi, zaszyfrowany przez Windows (DPAPI, tylko ten użytkownik).
    // Decyzję wydaje wyłącznie operator z konsoli; stos podpisuje treść (SHA-256), agent sprawdza podpis przez ecxp_sprawdz_decyzje.
    // Decyzja z ważnym podpisem ma moc decyzji operatora wydanej w czacie – tylko w zakresie dokładnie w niej opisanym.
    static class Decyzje
    {
        public static readonly string[] Rodzaje = { "user_accepted", "push", "scalenie", "usuniecie", "wymaganie", "lancuch", "inne" };
        static readonly Regex ReSha40 = new Regex("^[0-9a-f]{40}$");
        static string Dir { get { return Path.Combine(P.Root, "stan", "decyzje"); } }
        static string PlikKlucza { get { return Path.Combine(P.Root, "stan", "klucz-operatora.dpapi"); } }
        static string PlikPublicznego { get { return Path.Combine(P.Root, "stan", "klucz-operatora.pub.xml"); } }

        static RSACryptoServiceProvider Nowy()
        {
            return new RSACryptoServiceProvider(2048, new CspParameters(24)) { PersistKeyInCsp = false };   // PROV_RSA_AES: SHA-256
        }

        static RSACryptoServiceProvider Prywatny()
        {
            var rsa = Nowy();
            if (File.Exists(PlikKlucza))
                rsa.FromXmlString(Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(PlikKlucza), null, DataProtectionScope.CurrentUser)));
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PlikKlucza));
                File.WriteAllBytes(PlikKlucza, ProtectedData.Protect(Encoding.UTF8.GetBytes(rsa.ToXmlString(true)), null, DataProtectionScope.CurrentUser));
                File.WriteAllText(PlikPublicznego, rsa.ToXmlString(false), new UTF8Encoding(false));
                P.Zdarzenie("klucz_operatora", P.D("publiczny_sha256", P.Sha(rsa.ToXmlString(false))));
            }
            return rsa;
        }

        public static Dictionary<string, object> Wydaj(string projekt, string modul, string adres, string rodzaj, string commit, string galaz, string opis, string odpNa, string lancuch)
        {
            rodzaj = (rodzaj ?? "").Trim().ToLowerInvariant();
            commit = (commit ?? "").Trim().ToLowerInvariant();
            if (!Rodzaje.Contains(rodzaj)) throw new EcxpException("Nieznany rodzaj decyzji '" + rodzaj + "'. Dozwolone: " + string.Join(", ", Rodzaje));
            if ((rodzaj == "user_accepted" || rodzaj == "push" || rodzaj == "scalenie") && !ReSha40.IsMatch(commit))
                throw new EcxpException("Decyzja " + rodzaj + " wymaga pełnego hasha commitu (40 znaków).");
            if ((opis ?? "").Trim().Length == 0) throw new EcxpException("Opisz decyzję – co dokładnie wolno zrobić.");
            return P.ZBlokada(delegate
            {
                var id = "D-" + DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + P.Losowe(3);
                var tresc = P.D("protokol", P.Protokol, "id", id, "rodzaj", rodzaj, "projekt", projekt ?? "", "modul", modul ?? "", "commit", commit,
                    "galaz", galaz ?? "", "opis", opis.Trim(), "lancuch", lancuch ?? "", "czas", P.Teraz(), "operator", P.Wlasciciel);
                var kanon = P.Js().Serialize(tresc);
                string podpis;
                using (var rsa = Prywatny()) podpis = Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(kanon), "SHA256"));
                Pliki.Zapisz(Path.Combine(Dir, id + ".json"), P.D("id", id, "kanon", kanon, "podpis", podpis, "klucz_sha256", P.Sha(File.Exists(PlikPublicznego) ? P.Czytaj(PlikPublicznego) : "")));
                P.Zdarzenie("decyzja", P.D("id", id, "rodzaj", rodzaj, "commit", commit, "tresc_sha256", P.Sha(kanon)));
                var opisRodzaju = new Dictionary<string, string> {
                    { "user_accepted", "USER_ACCEPTED dla commitu" }, { "push", "zgoda na push" }, { "scalenie", "zgoda na scalenie" },
                    { "usuniecie", "zgoda na usunięcie" }, { "wymaganie", "zmiana wymagań" }, { "lancuch", "decyzja w łańcuchu ról" }, { "inne", "decyzja" } };
                object wyslane = null;
                if ((adres ?? "").Trim().Length > 0)
                    wyslane = Siec.Wyslij(new P.Nowa
                    {
                        Od = P.Wlasciciel, Do = adres, Projekt = projekt ?? "", Modul = (modul ?? "").Length > 0 ? modul : "wspolne", Typ = "decyzja",
                        Temat = "DECYZJA-" + rodzaj, OdpNa = odpNa ?? "",
                        Tresc = "Decyzja operatora (podpisana): " + opisRodzaju[rodzaj] + (commit.Length > 0 ? " " + commit : "") + (galaz ?? "").Trim().Insert(0, (galaz ?? "").Length > 0 ? ", gałąź " : "") +
                                ".\n" + opis.Trim() + "\n\nSprawdź podpis przed wykonaniem: ecxp_sprawdz_decyzje(id=" + id + "). Decyzja obowiązuje tylko w opisanym zakresie.",
                        Czesci = new List<Dictionary<string, object>> { P.D("rodzaj", "dane", "schemat", "ecxp/decyzja", "dane", P.D("id", id, "tresc", tresc, "podpis", podpis)) }
                    });
                if ((lancuch ?? "").Length > 0) Lancuchy.PoDecyzji(lancuch, id);
                return P.D("id", id, "rodzaj", rodzaj, "podpis_sha256", P.Sha(podpis), "wyslane", wyslane);
            });
        }

        public static Dictionary<string, object> Sprawdz(string id)
        {
            id = Path.GetFileName((id ?? "").Trim());
            var p = Path.Combine(Dir, id + ".json");
            if (!File.Exists(p)) return P.D("id", id, "wazna", false, "powod", "Nie ma decyzji o tym id – nie wykonuj jej.");
            var d = Pliki.Czytaj(p);
            bool ok = false;
            try
            {
                using (var rsa = Nowy())
                {
                    rsa.FromXmlString(P.Czytaj(PlikPublicznego));
                    ok = rsa.VerifyData(Encoding.UTF8.GetBytes(P.S(d, "kanon")), "SHA256", Convert.FromBase64String(P.S(d, "podpis")));
                }
            }
            catch { ok = false; }
            Dictionary<string, object> tresc = null;
            try { tresc = P.Js().Deserialize<Dictionary<string, object>>(P.S(d, "kanon")); } catch { }
            return P.D("id", id, "wazna", ok, "tresc", tresc, "powod", ok ? "Podpis operatora poprawny. Wykonaj tylko to, co opisano." : "PODPIS NIEPRAWIDŁOWY – nie wykonuj i zgłoś operatorowi.");
        }
    }

    // ---------- łańcuchy ról ----------
    // stan\lancuchy\<id>.json: kroki (role), bieżący krok, zadania kroku, historia. Stos sam przekazuje sprawę dalej:
    // zakończone zadania kroku → następna rola; „nieudane”/„odrzucone” u testera lub recenzenta → z powrotem do programisty;
    // krok „decyzja” czeka na podpisaną decyzję operatora.
    static class Lancuchy
    {
        public static readonly string[] Domyslny = { "programista", "tester", "recenzent", "decyzja", "integrator" };
        static string Dir { get { return Path.Combine(P.Root, "stan", "lancuchy"); } }

        public static List<Dictionary<string, object>> Wszystkie()
        {
            if (!Directory.Exists(Dir)) return new List<Dictionary<string, object>>();
            return Directory.GetFiles(Dir, "*.json").Select(Pliki.Czytaj).Where(d => P.S(d, "id").Length > 0).ToList();
        }

        public static Dictionary<string, object> Utworz(string projekt, string modul, string tytul, string opis, List<string> kroki, List<Dictionary<string, object>> czesci)
        {
            kroki = (kroki == null || kroki.Count == 0 ? Domyslny.ToList() : kroki).Select(k => P.Slug(k, 24, false)).Where(k => k.Length > 0).ToList();
            return P.ZBlokada(delegate
            {
                var id = "LR-" + DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + P.Losowe(2);
                var l = P.D("id", id, "projekt", P.Slug(projekt, 32, false), "modul", P.Slug(modul, 24, false), "tytul", tytul ?? "", "opis", opis ?? "",
                    "kroki", kroki, "krok", 0, "stan", "w_toku", "zadania", new List<object>(), "historia", new List<object>(), "czesci", czesci ?? new List<Dictionary<string, object>>());
                Start(l, "");
                return l;
            });
        }

        static void Zapisz(Dictionary<string, object> l) { Pliki.Zapisz(Path.Combine(Dir, P.S(l, "id") + ".json"), l); }

        static void Historia(Dictionary<string, object> l, string wpis)
        {
            var h = l.ContainsKey("historia") ? ((System.Collections.IEnumerable)l["historia"]).Cast<object>().ToList() : new List<object>();
            h.Add(DateTime.Now.ToString("dd.MM HH:mm") + "  " + wpis);
            l["historia"] = h;
        }

        static void Start(Dictionary<string, object> l, string uwagiPoprzednie)
        {
            var kroki = P.L(l, "kroki");
            int k = Convert.ToInt32(l["krok"]);
            string projekt = P.S(l, "projekt"), modul = P.S(l, "modul"), tytul = P.S(l, "tytul");
            if (k >= kroki.Count)
            {
                l["stan"] = "zakonczony"; Historia(l, "łańcuch zakończony");
                P.Wyslij(new P.Nowa { Od = P.Straznik, Do = P.Wlasciciel, Projekt = projekt, Modul = modul, Typ = "informacja", Temat = tytul, Tresc = "Łańcuch ról „" + tytul + "” zakończony (" + string.Join(" → ", kroki) + ")." });
                Zapisz(l); return;
            }
            var rola = kroki[k];
            if (rola == "decyzja")
            {
                l["stan"] = "czeka_na_decyzje"; l["zadania"] = new List<object>();
                Historia(l, "czeka na decyzję operatora");
                P.Wyslij(new P.Nowa
                {
                    Od = P.Straznik, Do = P.Wlasciciel, Projekt = projekt, Modul = modul, Typ = "blokada", Temat = tytul,
                    Tresc = "Łańcuch ról „" + tytul + "” czeka na Twoją decyzję (krok " + (k + 1) + " z " + kroki.Count + ").\n" + uwagiPoprzednie +
                            "\nWydaj decyzję w konsoli: Decyzja… → łańcuch " + P.S(l, "id") + "."
                });
                Zapisz(l); return;
            }
            l["stan"] = "w_toku";
            try
            {
                var w = Siec.Wyslij(new P.Nowa
                {
                    Od = P.Wlasciciel, Do = "rola:" + rola + "@" + modul, Projekt = projekt, Modul = modul, Typ = "zadanie", Temat = tytul + "-" + rola,
                    Tresc = "Łańcuch ról „" + tytul + "”, krok " + (k + 1) + " z " + kroki.Count + ": " + rola + " (" + string.Join(" → ", kroki) + ").\n\n" + P.S(l, "opis") +
                            (uwagiPoprzednie.Length > 0 ? "\n\nZ poprzedniego kroku: " + uwagiPoprzednie : "") +
                            "\n\nPo wykonaniu ustaw stan zadania: ecxp_zadanie(stan=zakonczone) albo nieudane/odrzucone z uwagami.",
                    Czesci = P.Lista(l, "czesci")
                });
                l["zadania"] = ((System.Collections.IEnumerable)w["wyslane"]).Cast<Dictionary<string, object>>().Select(x => (object)P.D("id", P.S(x, "id"), "stan", "zgloszone")).ToList();
                Historia(l, "krok " + (k + 1) + ": " + rola + " → " + string.Join(", ", ((System.Collections.IEnumerable)w["adresaci"]).Cast<object>()));
            }
            catch (Exception ex)
            {
                l["stan"] = "blad"; Historia(l, "nie przekazano roli " + rola + ": " + ex.Message);
                P.Wyslij(new P.Nowa { Od = P.Straznik, Do = P.Wlasciciel, Projekt = projekt, Modul = modul, Typ = "blokada", Temat = tytul,
                    Tresc = "Łańcuch „" + tytul + "” stanął na roli " + rola + ": " + ex.Message + " Nadaj tę rolę sesji w module " + modul + " (konsola → Agenci → Nadaj role) i wznów łańcuch." });
            }
            Zapisz(l);
        }

        // Wołane przez stos po każdej zmianie stanu zadania (P.ZmienZadanie).
        public static void PoZmianieZadania(string zadanieId, string stan, string kto, string uwagi)
        {
            foreach (var l in Wszystkie().Where(x => P.S(x, "stan") == "w_toku"))
            {
                var zad = P.Lista(l, "zadania");
                var moje = zad.FirstOrDefault(z => P.S(z, "id") == zadanieId);
                if (moje == null) continue;
                moje["stan"] = stan;
                l["zadania"] = zad.Cast<object>().ToList();
                var kroki = P.L(l, "kroki");
                int k = Convert.ToInt32(l["krok"]);
                var rola = kroki[k];
                if ((stan == "nieudane" || stan == "odrzucone") && (rola == "tester" || rola == "recenzent" || rola == "bezpieczenstwo"))
                {
                    var prog = kroki.IndexOf("programista");
                    Historia(l, rola + " (" + kto + "): " + stan + " – wraca do programisty" + (string.IsNullOrEmpty(uwagi) ? "" : ": " + uwagi));
                    l["krok"] = prog >= 0 ? prog : 0;
                    Start(l, rola + " zgłosił: " + stan + (string.IsNullOrEmpty(uwagi) ? "" : " – " + uwagi));
                }
                else if (zad.All(z => P.S(z, "stan") == "zakonczone"))
                {
                    Historia(l, rola + " (" + kto + "): zakończone" + (string.IsNullOrEmpty(uwagi) ? "" : " – " + uwagi));
                    l["krok"] = k + 1;
                    Start(l, rola + ": " + (string.IsNullOrEmpty(uwagi) ? "zakończone" : uwagi));
                }
                else Zapisz(l);
            }
        }

        public static void PoDecyzji(string lancuchId, string decyzjaId)
        {
            var p = Path.Combine(Dir, Path.GetFileName(lancuchId) + ".json");
            if (!File.Exists(p)) throw new EcxpException("Nie ma łańcucha '" + lancuchId + "'.");
            var l = Pliki.Czytaj(p);
            if (P.S(l, "stan") != "czeka_na_decyzje") throw new EcxpException("Łańcuch '" + lancuchId + "' nie czeka na decyzję (stan: " + P.S(l, "stan") + ").");
            Historia(l, "decyzja operatora " + decyzjaId);
            l["krok"] = Convert.ToInt32(l["krok"]) + 1;
            Start(l, "operator wydał decyzję " + decyzjaId + " (sprawdź: ecxp_sprawdz_decyzje).");
        }

        public static Dictionary<string, object> Wznow(string lancuchId)
        {
            var p = Path.Combine(Dir, Path.GetFileName(lancuchId) + ".json");
            if (!File.Exists(p)) throw new EcxpException("Nie ma łańcucha '" + lancuchId + "'.");
            var l = Pliki.Czytaj(p);
            Historia(l, "wznowiony przez operatora");
            Start(l, "");
            return l;
        }
    }

    // ---------- wspólna gałąź pary ----------
    static class Galezie
    {
        static string Plik(string projekt, string modul) { return Path.Combine(P.Root, "stan", "galezie", P.Slug(projekt, 32, false) + "__" + P.Slug(modul, 24, false) + ".json"); }
        static readonly Regex ReSha = new Regex("^[0-9a-f]{7,40}$");

        public static Dictionary<string, object> Stan(string projekt, string modul)
        {
            var d = Pliki.Czytaj(Plik(projekt, modul));
            if (d.Count == 0) d = P.D("projekt", projekt, "modul", modul, "para_stan", "brak_umowy");
            return d;
        }

        public static bool Wstrzymana(string projekt, string modul) { return P.S(Stan(projekt, modul), "para_stan") == "rozbieznosc"; }

        static Trasa Moja(string ja, string projekt, string modul)
        {
            var t = Siec.Przynaleznosc(ja).FirstOrDefault(x => x.Projekt == P.Slug(projekt, 32, false) && x.Modul == P.Slug(modul, 24, false));
            if (t == null) throw new EcxpException(ja + " nie jest w parze modułu " + modul + " projektu " + projekt + ". Najpierw ecxp_przypisz.");
            return t;
        }

        public static Dictionary<string, object> Umowa(string ja, string projekt, string modul, string repo, string galaz, string baza)
        {
            var t = Moja(ja, projekt, modul);
            if ((repo ?? "").Trim().Length == 0 || (galaz ?? "").Trim().Length == 0 || !ReSha.IsMatch((baza ?? "").Trim().ToLowerInvariant()))
                throw new EcxpException("Podaj repozytorium, gałąź i commit bazowy (hash).");
            baza = baza.Trim().ToLowerInvariant();
            return P.ZBlokada(delegate
            {
                var d = Stan(t.Projekt, t.Modul);
                var u = Pliki.Slownik(d, "umowa");
                var partner = Siec.Partner(t.Projekt, t.Modul, ja);
                bool taSama = P.S(u, "repozytorium") == repo.Trim() && P.S(u, "galaz") == galaz.Trim() && P.S(u, "baza") == baza;
                string wynik;
                if (taSama && P.S(u, "stan") == "proponowana" && P.S(u, "zaproponowal") != ja)
                {
                    u["stan"] = "uzgodniona"; u["potwierdzil"] = ja; u["czas"] = P.Teraz();
                    wynik = "uzgodniona";
                    if (partner.Length > 0) P.Wyslij(new P.Nowa { Od = ja, Do = partner, Projekt = t.Projekt, Modul = t.Modul, Typ = "informacja", Temat = "UMOWA-GALEZI",
                        Tresc = "Potwierdzam wspólną gałąź: " + galaz + " w " + repo + " od " + baza + ". Po każdym commicie zgłaszamy ecxp_stan_galezi." });
                }
                else if (taSama && P.S(u, "stan") == "uzgodniona") wynik = "uzgodniona";
                else
                {
                    d["umowa"] = P.D("repozytorium", repo.Trim(), "galaz", galaz.Trim(), "baza", baza, "stan", "proponowana", "zaproponowal", ja, "czas", P.Teraz());
                    wynik = "proponowana";
                    if (partner.Length > 0) P.Wyslij(new P.Nowa { Od = ja, Do = partner, Projekt = t.Projekt, Modul = t.Modul, Typ = "pytanie", Temat = "UMOWA-GALEZI",
                        Tresc = "Proponuję wspólną gałąź roboczą pary: " + galaz + " w " + repo + ", commit bazowy " + baza +
                                ". Jeśli się zgadzasz, wywołaj ecxp_umowa_galezi z tymi samymi wartościami." });
                }
                d["projekt"] = t.Projekt; d["modul"] = t.Modul;
                if (P.S(d, "para_stan") == "brak_umowy" || P.S(d, "para_stan").Length == 0) d["para_stan"] = "ok";
                Pliki.Zapisz(Plik(t.Projekt, t.Modul), d);
                P.Zdarzenie("umowa_galezi", P.D("kto", ja, "projekt", t.Projekt, "modul", t.Modul, "galaz", galaz, "baza", baza, "stan", wynik));
                return P.D("stan", wynik, "umowa", d["umowa"]);
            });
        }

        // Zwraca: 1 – a jest przodkiem b, 0 – nie jest, -1 – nie da się sprawdzić (np. commit nieznany lokalnie).
        static int Przodek(string repo, string a, string b)
        {
            try
            {
                var psi = new ProcessStartInfo("git", "--no-optional-locks -C \"" + repo + "\" merge-base --is-ancestor " + a + " " + b)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
                using (var pr = Process.Start(psi))
                {
                    pr.StandardOutput.ReadToEnd(); pr.StandardError.ReadToEnd();
                    if (!pr.WaitForExit(15000)) { try { pr.Kill(); } catch { } return -1; }
                    return pr.ExitCode == 0 ? 1 : pr.ExitCode == 1 ? 0 : -1;
                }
            }
            catch { return -1; }
        }

        // STAN_GALEZI: strona zgłasza HEAD wspólnej gałęzi. Stos porównuje HEAD obu stron; gdy żaden nie jest przodkiem
        // drugiego – rozbieżność: para wstrzymana (nowe zadania od agentów odrzucane), alarm u operatora i u obu stron.
        public static Dictionary<string, object> StanGalezi(string ja, string projekt, string modul, string galaz, string head)
        {
            var t = Moja(ja, projekt, modul);
            head = (head ?? "").Trim().ToLowerInvariant();
            if (!ReSha.IsMatch(head)) throw new EcxpException("Podaj HEAD (hash commitu).");
            return P.ZBlokada(delegate
            {
                var d = Stan(t.Projekt, t.Modul);
                var u = Pliki.Slownik(d, "umowa");
                var strony = Pliki.Slownik(d, "strony");
                strony[t.Strona] = P.D("nazwa", ja, "head", head, "galaz", galaz ?? "", "czas", P.Teraz());
                var uwagi = new List<string>();
                if (P.S(u, "stan") != "uzgodniona") uwagi.Add("brak uzgodnionej gałęzi – ustalcie ją przez ecxp_umowa_galezi");
                else if ((galaz ?? "").Length > 0 && galaz != P.S(u, "galaz")) uwagi.Add("zgłoszona gałąź " + galaz + " różni się od uzgodnionej " + P.S(u, "galaz"));
                var repo = P.S(u, "repozytorium");
                string stan = "ok";
                var druga = t.Strona == "codex" ? "claude" : "codex";
                var inna = strony.ContainsKey(druga) ? strony[druga] as Dictionary<string, object> : null;
                if (repo.Length > 0 && P.S(u, "baza").Length > 0 && Przodek(repo, P.S(u, "baza"), head) == 0) { stan = "rozbieznosc"; uwagi.Add("HEAD " + head + " nie wyrasta z commitu bazowego " + P.S(u, "baza")); }
                if (repo.Length > 0 && inna != null && P.S(inna, "head").Length > 0 && P.S(inna, "head") != head)
                {
                    int ab = Przodek(repo, head, P.S(inna, "head")), ba = Przodek(repo, P.S(inna, "head"), head);
                    if (ab == 0 && ba == 0) { stan = "rozbieznosc"; uwagi.Add("historie stron się rozjechały: " + t.Strona + " " + head + " i " + druga + " " + P.S(inna, "head")); }
                    else if (ab == -1 || ba == -1) uwagi.Add("nie można porównać z HEAD drugiej strony (commit nieznany w lokalnym repozytorium – potrzebny fetch)");
                }
                var poprzedni = P.S(d, "para_stan");
                d["para_stan"] = stan; d["projekt"] = t.Projekt; d["modul"] = t.Modul; d["uwagi"] = uwagi;
                Pliki.Zapisz(Plik(t.Projekt, t.Modul), d);
                P.Zdarzenie("stan_galezi", P.D("kto", ja, "projekt", t.Projekt, "modul", t.Modul, "head", head, "stan", stan));
                var partner = Siec.Partner(t.Projekt, t.Modul, ja);
                if (stan == "rozbieznosc" && poprzedni != "rozbieznosc")
                {
                    var tekst = "STOP – rozbieżność wspólnej gałęzi pary " + t.Projekt + "/" + t.Modul + ": " + string.Join("; ", uwagi) + ". Para wstrzymana do decyzji operatora.";
                    P.Wyslij(new P.Nowa { Od = P.Straznik, Do = P.Wlasciciel, Projekt = t.Projekt, Modul = t.Modul, Typ = "blokada", Temat = "ROZBIEZNOSC-GALEZI", Pilne = true, Tresc = tekst });
                    foreach (var n in new[] { ja, partner }.Where(x => x.Length > 0))
                        P.Wyslij(new P.Nowa { Od = P.Straznik, Do = n, Projekt = t.Projekt, Modul = t.Modul, Typ = "blokada", Temat = "ROZBIEZNOSC-GALEZI", Pilne = true, Tresc = tekst + " Nie róbcie kolejnych zmian." });
                }
                else if (stan == "ok" && partner.Length > 0 && (inna == null || P.S(inna, "head") != head))
                    P.Wyslij(new P.Nowa { Od = ja, Do = partner, Projekt = t.Projekt, Modul = t.Modul, Typ = "informacja", Temat = "STAN-GALEZI",
                        Tresc = "Nowy HEAD wspólnej gałęzi " + (galaz ?? P.S(u, "galaz")) + ": " + head + ". Zsynchronizuj się przed kolejną zmianą." });
                return P.D("para_stan", stan, "uwagi", uwagi, "umowa", u, "strony", strony);
            });
        }

        public static Dictionary<string, object> Wznow(string projekt, string modul, string uwagi)
        {
            return P.ZBlokada(delegate
            {
                var d = Stan(projekt, modul);
                d["para_stan"] = "ok"; d["uwagi"] = new List<string> { "wznowiona przez operatora: " + (uwagi ?? "") };
                Pliki.Zapisz(Plik(projekt, modul), d);
                P.Zdarzenie("para_wznowiona", P.D("projekt", projekt, "modul", modul, "uwagi", uwagi ?? ""));
                return d;
            });
        }
    }
}
