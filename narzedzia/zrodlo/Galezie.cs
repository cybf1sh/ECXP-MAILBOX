// ECXP-MAILBOX – „Gałęzie wątków”: opcja listy rozmów. Wątek jest główną gałęzią (wiersz-nagłówek), a pod nim – wcięte,
// połączone pionową kreską w kolorze wątku – wszystkie jego wiadomości i zadania, wysłane i odebrane, w kolejności czasu.
// Klik w nagłówek zwija / rozwija gałąź; klik w wiadomość otwiera ją jak dotąd. Ustawienie: galezie=1 w ustawienia-wlasciciel.txt.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Ecxp
{
    partial class App
    {
        public const int WysokoscWierszaGalezi = 78;
        bool galezieWatkow;
        CheckBox chGalezie;
        readonly HashSet<string> zwinieteGalezie = new HashSet<string>();

        void WczytajGalezie(string ust) { galezieWatkow = (ust ?? "").Contains("galezie=1"); }
        string UstawieniaGalezi() { return "galezie=" + (galezieWatkow ? 1 : 0) + Environment.NewLine; }

        CheckBox BudujOpcjeGalezi()
        {
            chGalezie = new CheckBox { Name = "galezieWatkow", Text = "Gałęzie wątków", AutoSize = true, Checked = galezieWatkow, Margin = new Padding(3, 8, 3, 3) };
            new ToolTip().SetToolTip(chGalezie, "Wątek jako główna gałąź, a pod nim wszystkie jego wiadomości i zadania (wysłane i odebrane) w kolorze wątku.\nKlik w nagłówek gałęzi zwija ją albo rozwija.");
            chGalezie.CheckedChanged += delegate { UstawGalezie(chGalezie.Checked); };
            return chGalezie;
        }

        void UstawGalezie(bool wl)
        {
            galezieWatkow = wl;
            if (chGalezie != null && chGalezie.Checked != wl) chGalezie.Checked = wl;
            UstawWysokoscWierszy();
            podpisListy = "";
            ZapiszUstawienia();
            RefreshView();
        }

        void UstawWysokoscWierszy()
        {
            if (lv == null) return;
            int h = galezieWatkow ? WysokoscWierszaGalezi : WysokoscWiersza;
            if (lv.SmallImageList == null || lv.SmallImageList.ImageSize.Height != h) lv.SmallImageList = new ImageList { ImageSize = new Size(1, h) };
        }

        // Wiersz-nagłówek gałęzi (z istniejącego wiersza rozmowy) i wiersze wiadomości pod nim.
        List<Wiersz> Galaz(Wiersz naglowek, List<Msg> w, DateTime teraz)
        {
            var wynik = new List<Wiersz>();
            var rep = (Msg)naglowek.Tag;
            bool zwinieta = zwinieteGalezie.Contains(rep.Watek);
            Color kolor = naglowek.ChipTlo, kolorTekstu;
            try { kolorTekstu = KolorWatku(w); } catch { kolorTekstu = Motyw.Tekst; }
            naglowek.Name = "w:" + rep.Watek;
            naglowek.Naglowek = true;
            naglowek.Tytul = (zwinieta ? "▸ " : "▾ ") + naglowek.Tytul;
            naglowek.Data = w.Count + Jezyk.T(" wiad.") + "  ·  " + naglowek.Data;
            naglowek.Etap = "";
            if (zwinieta) return wynik;
            var lustra = new HashSet<string>();
            foreach (var m in w.OrderBy(x => x.Czas).ThenBy(x => x.Nr).ThenBy(x => x.OdpNa.Length > 0 ? 1 : 0))
            {
                if (m.LustroId.Length > 0 && m.Lustro.Count > 1 && !lustra.Add(m.LustroId)) continue;     // wiadomość do pary: jedna pozycja
                var c = new Wiersz(P.TytulWiadomosci(m)) { Tag = m, Name = "m:" + m.Id, ForeColor = Motyw.Tekst, BackColor = Motyw.Pole, Wciecie = 26, Pasek = kolor };
                c.Chip = Tag(m); c.ChipTlo = kolor;
                c.Tytul = (Wyslana(m) ? "📤 " + Jezyk.T("Ty") + " → " + Adresaci(m, w) : "📥 " + NazwaW(m.Od)) + "  ·  " + Jezyk.T(P.Rodzaj(m));
                c.Data = m.Czas.ToString("dd.MM.yyyy HH:mm");
                Znacznik(m, w, out c.Znak, out c.ZnakTlo, out c.ZnakTekst, false);
                var wm = P.WynikWiadomosci(m);
                string stan = "";
                if (Wyslana(m) && m.Blad == null && P.PoprawnyNadawca(m.Do)) try { Color ke; stan = Etap(m, teraz, out ke); c.EtapKolor = ke; } catch (Exception ex) { Bledy.Wyjatek(ex); }
                c.Linia = (wm.Length > 0 ? Jezyk.T(P.WynikOpis(wm)) + "  ·  " : "") + (stan.Length > 0 ? stan : Zdanie(m));
                c.ToolTipText = Zdanie(m);
                if (m.Nowa) c.Font = fontB;
                if (m.Blad != null || wm == "negatywny") c.ForeColor = Czerwony;
                wynik.Add(c);
            }
            if (wynik.Count > 0) wynik[wynik.Count - 1].OstatniWGalezi = true;
            return wynik;
        }

        // Klik w nagłówek gałęzi: zwinięcie / rozwinięcie. Zwraca true, gdy klik obsłużono.
        bool KlikGalezi(ListViewItem it)
        {
            var w = it as Wiersz;
            if (!galezieWatkow || w == null || !w.Naglowek) return false;
            var watek = ((Msg)w.Tag).Watek;
            if (!zwinieteGalezie.Remove(watek)) zwinieteGalezie.Add(watek);
            podpisListy = "";
            RefreshView();
            return true;
        }
    }
}
