// ECXP-MAILBOX – znaczniki wiadomości: krótki kod „#c73f” (4 ostatnie znaki identyfikatora – te same, co na końcu nazw
// plików w folderach Odebrane / Wysłane) w stałym kolorze. Odpowiedź pokazuje kod i kolor wiadomości, na którą odpowiada
// („↩ odpowiedź na zadanie #c73f”), a rozmowa – do czego jest wątkiem („Wątek do zadania #c73f”).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Ecxp
{
    partial class App
    {
        // Wiadomość, której kod reprezentuje wszystkie jej kopie (para 1:1: pierwsza kopia według adresata – jak w nazwie pliku).
        Msg Kanon(Msg m)
        {
            if (m == null) return null;
            if (m.KopiaDla.Length > 0 && !m.KopiaDla.StartsWith("czat:")) { var z = msgs.FirstOrDefault(x => x.Id == m.KopiaDla); if (z != null) m = z; }
            return P.Kanon(m, msgs);
        }

        // Kod wiadomości: z identyfikatora wspólnego dla wszystkich jej kopii (pole „wiadomosc”, ecxp/1.1; starsze – pierwsza kopia).
        string Tag(Msg m)
        {
            if (m != null && m.KopiaDla.Length > 0 && !m.KopiaDla.StartsWith("czat:")) { var z = msgs.FirstOrDefault(x => x.Id == m.KopiaDla); if (z != null) m = z; }
            return P.ZnacznikId(P.IdWiadomosci(m, msgs));
        }

        // Kolor wyniku FIN: pozytywny – zielony, negatywny – czerwony, niewykonane / odmowa / nieokreślony – pomarańczowy.
        static Color KolorWyniku(string wynik, Color domyslny)
        {
            switch (wynik)
            {
                case "pozytywny": return Zielony;
                case "negatywny": return Czerwony;
                case "niewykonane": case "odmowa": case "nieokreslony": return Motyw.K(Color.FromArgb(190, 90, 0));
            }
            return domyslny;
        }

        // Kolory znaczników: ten sam kod = ten sam kolor (w liście, podglądzie i czytniku).
        static readonly Color[] PaletaJasna = { Color.FromArgb(0, 102, 204), Color.FromArgb(176, 64, 0), Color.FromArgb(0, 128, 96), Color.FromArgb(140, 50, 170),
            Color.FromArgb(190, 30, 90), Color.FromArgb(92, 112, 0), Color.FromArgb(0, 120, 150), Color.FromArgb(150, 92, 0) };
        static readonly Color[] PaletaCiemna = { Color.FromArgb(110, 180, 255), Color.FromArgb(255, 160, 100), Color.FromArgb(90, 220, 180), Color.FromArgb(210, 150, 240),
            Color.FromArgb(255, 130, 180), Color.FromArgb(190, 210, 90), Color.FromArgb(100, 210, 235), Color.FromArgb(240, 190, 100) };
        static int NrKoloru(string tag) { int s = 0; foreach (var c in tag ?? "") s = s * 31 + c; return (s & 0x7fffffff) % PaletaJasna.Length; }
        // kolor tekstu znacznika na tle okna
        static Color KolorTagu(string tag) { return Motyw.Konsolowy ? Color.White : Motyw.Ciemny ? PaletaCiemna[NrKoloru(tag)] : PaletaJasna[NrKoloru(tag)]; }
        // tło plakietki (tekst na niej biały)
        static Color TloTagu(string tag) { return Motyw.Konsolowy ? Color.Black : PaletaJasna[NrKoloru(tag)]; }

        // „zadania”, „pytania”… – rodzaj wiadomości w dopełniaczu („wątek do …”, „odpowiedź na …” używa biernika poniżej)
        static string RodzajDo(Msg m)
        {
            switch (m.Typ)
            {
                case "zadanie": return "zadania";
                case "pytanie": return "pytania";
                case "informacja": return "informacji";
                case "blokada": return "blokady";
                case "odpowiedz": case "fin": return "odpowiedzi";
                default: return "wiadomości";
            }
        }
        static string RodzajNa(Msg m)
        {
            switch (m.Typ)
            {
                case "zadanie": return "zadanie";
                case "pytanie": return "pytanie";
                case "informacja": return "informację";
                case "blokada": return "blokadę";
                case "odpowiedz": case "fin": return "odpowiedź";
                default: return "wiadomość";
            }
        }

        // Pierwsza wiadomość rozmowy (jej kod jest kodem wątku).
        // Przy tym samym czasie pierwszeństwo ma wiadomość, która nie jest odpowiedzią na inną z tej rozmowy.
        Msg KorzenWatku(List<Msg> w)
        {
            var ids = new HashSet<string>(w.Select(x => x.Id));
            return Kanon(w.OrderBy(x => x.Czas).ThenBy(x => x.OdpNa.Length > 0 && ids.Contains(x.OdpNa) ? 1 : 0).ThenBy(x => x.Seq).First());
        }

        // „Wątek do zadania #c73f”
        string OpisWatku(Msg korzen) { return Jezyk.T("Wątek do " + RodzajDo(korzen)) + " " + Tag(korzen); }

        // Wiadomość, na którą odpowiada r – także z archiwum (wynik pamiętany; archiwum czytane najwyżej raz na 30 s).
        readonly Dictionary<string, Msg> pamiecPierwowzorow = new Dictionary<string, Msg>();
        DateTime ostatnieArchiwum = DateTime.MinValue;
        Msg Pierwowzor(Msg r)
        {
            if (r == null || r.OdpNa.Length == 0) return null;
            var o = msgs.FirstOrDefault(x => x.Id == r.OdpNa);
            if (o != null) return o;
            if (pamiecPierwowzorow.TryGetValue(r.OdpNa, out o)) return o;
            if (DateTime.Now - ostatnieArchiwum < TimeSpan.FromSeconds(30)) return null;
            ostatnieArchiwum = DateTime.Now;
            try
            {
                var szukane = new HashSet<string>(msgs.Where(x => x.OdpNa.Length > 0).Select(x => x.OdpNa));
                szukane.ExceptWith(msgs.Select(x => x.Id));
                foreach (var a in P.Wszystkie(true).Where(x => x.Katalog == "archiwum" && szukane.Contains(x.Id))) pamiecPierwowzorow[a.Id] = a;
                foreach (var id in szukane) if (!pamiecPierwowzorow.ContainsKey(id)) pamiecPierwowzorow[id] = null;   // nie ma nigdzie (np. w koszu)
            }
            catch (Exception ex) { Bledy.Wyjatek(ex); }
            return pamiecPierwowzorow.TryGetValue(r.OdpNa, out o) ? o : null;
        }

        // Kod wiadomości, na którą odpowiada r (także gdy jej już nie ma – wtedy z samego identyfikatora).
        string TagPierwowzoru(Msg r)
        {
            if (r.OdpNaLogiczne.Length > 0) return P.ZnacznikId(r.OdpNaLogiczne);      // ecxp/1.1: odpowiedź niesie identyfikator wiadomości
            var o = Pierwowzor(r);
            if (o == null) return P.ZnacznikId(r.OdpNa);
            return o.Katalog == "archiwum" ? P.ZnacznikId(P.IdWiadomosci(o, new[] { o })) : Tag(o);
        }

        // „odpowiedź na zadanie #c73f z 12.11 01:27” (krotko) albo pełny opis z tematem i nadawcą; null, gdy to nie odpowiedź.
        string OdpowiedzNaOpis(Msg r, bool krotko)
        {
            if (r == null || r.OdpNa.Length == 0) return null;
            var o = Pierwowzor(r);
            if (o == null) return "↩ " + Jezyk.T("odpowiedź na wiadomość") + " " + P.ZnacznikId(r.OdpNa) + " " + Jezyk.T("(usuniętą albo spoza tej skrzynki)");
            var s = "↩ " + Jezyk.T("odpowiedź na " + RodzajNa(o)) + " " + TagPierwowzoru(r);
            if (krotko) return s + " " + Jezyk.T("z") + " " + o.Czas.ToString("dd.MM HH:mm");
            return s + " · „" + P.Baza(o.Temat) + "” · " + (Wyslana(o) ? Jezyk.T("wysłane przez Ciebie") : Jezyk.T("od ") + NazwaW(o.Od)) + " " + o.Czas.ToString("dd.MM.yyyy HH:mm") +
                (o.Katalog == "archiwum" ? " · " + Jezyk.T("w archiwum") : "");
        }

        // Odpowiedzi, które przyszły na tę wiadomość (na dowolną jej kopię), bez komunikatów strażnika.
        List<Msg> OdpowiedziDo(Msg m)
        {
            var k = Kanon(m);
            var ids = new HashSet<string>(msgs.Where(x => x.Id == k.Id || (k.LustroId.Length > 0 && x.LustroId == k.LustroId) || x.KopiaDla == k.Id).Select(x => x.Id));
            return msgs.Where(x => x.OdpNa.Length > 0 && ids.Contains(x.OdpNa) && x.Od != P.Straznik && !ids.Contains(x.Id)).OrderBy(x => x.Czas).ToList();
        }
    }
}
