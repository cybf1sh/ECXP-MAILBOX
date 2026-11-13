// ECXP – niezawodność stosu (bez modelu AI):
//  * Powtorki: identyfikator żądania przy wysyłce – ponowione „wyślij” nie tworzy drugiej wiadomości.
//  * Zamiary: wysyłka do pary zapisuje zamiar przed pierwszą kopią; po awarii w środku stos dosyła brakujące kopie.
//  * Dokończenie obiegu (Obieg.Dokoncz): zadanie, w którym wszyscy skończyli, a wspólny FIN nie powstał, zostaje domknięte.
//  * Dzierżawa tury: trwająca tura agenta w tle co kilkanaście sekund potwierdza, że żyje; po restarcie stosu znaczniki tur znikają.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Ecxp
{
    static class Powtorki
    {
        static string Dir { get { return Path.Combine(P.Root, "stan", "zadania-klienta"); } }
        static DateTime ostatnieSprzatanie = DateTime.MinValue;

        // Wykonuje f raz dla danego klucza w oknie czasu; powtórka dostaje zapisany wynik z polem powtorzone = true.
        // Błąd (wyjątek) niczego nie zapisuje – ponowienie po błędzie wykona operację.
        public static Dictionary<string, object> Raz(string klucz, TimeSpan okno, Func<Dictionary<string, object>> f)
        {
            if (string.IsNullOrEmpty(klucz)) return f();
            return P.ZBlokada(delegate
            {
                Directory.CreateDirectory(Dir);
                var plik = Path.Combine(Dir, P.Sha(klucz).Substring(0, 40) + ".json");
                if (File.Exists(plik))
                {
                    var z = Pliki.Czytaj(plik);
                    DateTimeOffset t;
                    if (DateTimeOffset.TryParse(P.S(z, "czas"), out t) && DateTimeOffset.Now - t < okno && z.ContainsKey("wynik") && z["wynik"] is Dictionary<string, object>)
                    {
                        var stary = new Dictionary<string, object>((Dictionary<string, object>)z["wynik"]);
                        stary["powtorzone"] = true;
                        stary["uwagi_powtorki"] = "To żądanie zostało już wykonane (" + t.LocalDateTime.ToString("HH:mm:ss") + ") – nie utworzono drugiej wiadomości.";
                        P.Zdarzenie("powtorka", P.D("klucz", P.Sha(klucz).Substring(0, 16), "id", P.S(stary, "id")));
                        return stary;
                    }
                }
                var wynik = f();
                try { Pliki.Zapisz(plik, P.D("czas", P.Teraz(), "wynik", wynik)); } catch { }
                Sprzataj();
                return wynik;
            });
        }

        static void Sprzataj()
        {
            if (DateTime.Now - ostatnieSprzatanie < TimeSpan.FromHours(1)) return;
            ostatnieSprzatanie = DateTime.Now;
            try { foreach (var f in new DirectoryInfo(Dir).GetFiles("*.json")) if (f.LastWriteTime < DateTime.Now.AddDays(-2)) f.Delete(); } catch { }
        }
    }

    static class Zamiary
    {
        static string Dir { get { return Path.Combine(P.Root, "stan", "zamiary"); } }
        static string Plik(string lustroId) { return Path.Combine(Dir, Path.GetFileName(lustroId) + ".json"); }

        // Przed wysłaniem pierwszej kopii wiadomości do pary: pełny opis wysyłki (wystarczy do dosłania brakujących kopii).
        public static void Zapisz(string lustroId, P.Nowa n, List<string> cele)
        {
            Directory.CreateDirectory(Dir);
            Pliki.Zapisz(Plik(lustroId), P.D("lustro_id", lustroId, "czas", P.Teraz(), "cele", cele, "od", n.Od, "do", n.Do, "projekt", n.Projekt, "modul", n.Modul, "typ", n.Typ,
                "temat", n.Temat, "tresc", n.Tresc, "odpowiedz_na", n.OdpNa, "pilne", n.Pilne, "commit", n.Commit, "kontrole", n.Kontrole, "pliki", n.Pliki,
                "ograniczenia", n.Ograniczenia, "czesci", n.Czesci, "watek", n.Watek, "wynik", n.Wynik));
        }
        public static void Koniec(string lustroId) { try { File.Delete(Plik(lustroId)); } catch { } }

        // Po starcie stosu i okresowo: zamiary starsze niż 30 s to wysyłki przerwane w środku – dosłanie brakujących kopii.
        public static int Dokoncz(Action<string> log)
        {
            if (!Directory.Exists(Dir)) return 0;
            int ile = 0;
            foreach (var f in new DirectoryInfo(Dir).GetFiles("*.json").Where(x => x.LastWriteTime < DateTime.Now.AddSeconds(-30)))
            {
                try
                {
                    ile += P.ZBlokada(delegate
                    {
                        var z = Pliki.Czytaj(f.FullName);
                        var lid = P.S(z, "lustro_id"); var cele = P.L(z, "cele");
                        if (lid.Length == 0 || cele.Count == 0) { f.Delete(); return 0; }
                        var sa = P.Wszystkie(true).Concat(P.ZKosza()).Where(m => m.LustroId == lid).ToList();
                        var brak = cele.Where(c => !sa.Any(m => m.Do == c)).ToList();
                        int doslane = 0;
                        // bez żadnej kopii nie ma czego dosyłać: wysyłka nie zaczęła się, klient dostał błąd i może ją ponowić
                        if (sa.Count > 0)
                        {
                            var wzor = sa.OrderBy(m => m.Do).First();
                            foreach (var c in brak)
                            {
                                P.Wyslij(new P.Nowa
                                {
                                    Od = P.S(z, "od"), Do = c, Projekt = P.S(z, "projekt"), Modul = P.S(z, "modul"), Typ = P.S(z, "typ"), Temat = P.S(z, "temat"), Tresc = P.S(z, "tresc"),
                                    OdpNa = P.S(z, "odpowiedz_na"), Pilne = P.B(z, "pilne"), Commit = P.S(z, "commit"), Kontrole = P.L(z, "kontrole"), Pliki = P.L(z, "pliki"),
                                    Ograniczenia = P.S(z, "ograniczenia"), Czesci = P.Lista(z, "czesci"), Watek = wzor.WatekPole.Length > 0 ? wzor.WatekPole : P.S(z, "watek"), Wynik = P.S(z, "wynik"),
                                    Lustro = cele, LustroId = lid, Logiczne = wzor.Logiczne.Length > 0 ? wzor.Logiczne : wzor.Id
                                });
                                doslane++;
                            }
                            // obieg zadania operatora, jeśli nie zdążył powstać
                            if (P.S(z, "od") == P.Wlasciciel && P.S(z, "typ") == "zadanie" && Obieg.Znajdz(wzor.Id) == null)
                            {
                                var wszystkie = P.Wszystkie(true).Where(m => m.LustroId == lid).Select(m => (object)P.D("id", m.Id, "do", m.Do)).ToList();
                                Obieg.Zalozone(P.S(z, "projekt"), P.S(z, "modul"), P.S(z, "temat").Length > 0 ? P.S(z, "temat") : P.Baza(wzor.Temat), wszystkie, lid);
                            }
                            P.Zdarzenie("zamiar_dokonczony", P.D("lustro_id", lid, "doslane", brak));
                            if (log != null) log("zamiar " + lid + ": dosłano kopie dla " + (brak.Count > 0 ? string.Join(", ", brak) : "nikogo (kopie były komplet)"));
                        }
                        f.Delete();
                        return doslane;
                    });
                }
                catch (Exception ex) { if (log != null) log("zamiar " + f.Name + ": " + ex.Message); }
            }
            return ile;
        }
    }
}
