// ECXP-MAILBOX – śledzenie wysłanych: co dzieje się z każdą wiadomością operatora i ile czasu minęło od wysłania.
// Tylko odczyt: skrzynka (msgs ze skanu konsoli), dziennik stosu i obieg zadań. Niczego nie wysyła ani nie potwierdza.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Ecxp
{
    partial class App
    {
        Form oknoSledzenia;

        // Czas od zdarzenia, z sekundami do godziny – licznik w oknie śledzenia odświeżany co sekundę.
        public static string CzasOd(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            if (t.TotalSeconds < 60) return (int)t.TotalSeconds + " s";
            if (t.TotalMinutes < 60) return (int)t.TotalMinutes + " min " + t.Seconds.ToString("00") + " s";
            if (t.TotalHours < 24) return (int)t.TotalHours + " h " + t.Minutes.ToString("00") + " min";
            return (int)t.TotalDays + " " + Jezyk.T("dni") + " " + t.Hours + " h";
        }

        // Kopie tej samej wiadomości (zasada 1:1: para dostaje lustrzane kopie) – odpowiedź może wskazywać dowolną z nich.
        List<string> IdKopii(Msg m)
        {
            if (m.LustroId.Length == 0) return new List<string> { m.Id };
            return msgs.Where(x => x.LustroId == m.LustroId).Select(x => x.Id).Concat(new[] { m.Id }).Distinct().ToList();
        }

        // Pierwsza odpowiedź adresata (albo FIN obiegu) na tę wiadomość.
        Msg OdpowiedzNa(Msg m)
        {
            var ids = new HashSet<string>(IdKopii(m));
            return msgs.Where(x => x.OdpNa.Length > 0 && ids.Contains(x.OdpNa) && x.Od != P.Wlasciciel && Obieg.WynikAdresata(x, m.Do))
                .OrderBy(x => x.Czas).FirstOrDefault();
        }

        // Etap wysłanej wiadomości: krótki opis i chwila, od której trwa.
        string Etap(Msg m, DateTime teraz, out Color kolor)
        {
            kolor = Motyw.Tekst;
            if (m.Blad != null) { kolor = Czerwony; return Jezyk.T("uszkodzona"); }
            var odp = OdpowiedzNa(m);
            if (odp != null)
            {
                // wynik FIN (pole ecxp/1.1 albo początek treści starszych FIN) decyduje o kolorze
                var wynik = P.WynikWiadomosci(odp);
                kolor = KolorWyniku(wynik, Zielony);
                return (odp.Typ == "fin" ? "FIN" : Jezyk.T("odpowiedź")) + " " + Jezyk.T("po") + " " + CzasOd(odp.Czas - m.Czas) +
                    (wynik.Length > 0 ? " · " + Jezyk.T(P.WynikOpis(wynik)) : "");
            }
            var d = m.Dor;
            if (d != null && d.Wynik == "wycofana") { kolor = Motyw.Slaby; return Jezyk.T("wycofana przez Ciebie"); }
            if (d != null && d.Dostarczona.HasValue)
            {
                var po = CzasOd(d.Dostarczona.Value - m.Czas);
                // kto odebrał: adresat tej kopii (para dostaje osobne kopie – każda ma swojego adresata)
                var kto = P.PoprawnyNadawca(m.Do) && !m.Do.Contains(":") && !m.Do.Contains("/") ? " " + Jezyk.T("przez") + " " + NazwaW(m.Do) : "";
                if (m.Typ == "zadanie")
                    return (Jezyk.T("odebrane") + kto + " " + Jezyk.T("po") + " " + po + " · " + (Obieg.CzekaNaFin(m.Id, m.Do) ? Jezyk.T("w trakcie, czeka na FIN") : Jezyk.T("w trakcie"))) + EtapAgenta(m, teraz);
                if (m.WymagaOdp) return Jezyk.T("odebrana") + kto + " " + Jezyk.T("po") + " " + po + " · " + Jezyk.T("czeka na odpowiedź") + EtapAgenta(m, teraz);
                kolor = Zielony;
                return Jezyk.T("odebrana") + kto + " " + Jezyk.T("po") + " " + po;
            }
            if (m.Katalog == "przeczytane") { kolor = Zielony; return Jezyk.T("odebrana"); }
            if (d != null && (d.Eskalacja || d.Zaginiona)) { kolor = Czerwony; return Jezyk.T("NIEDORĘCZONA – alarm") + (d.Ponowienia > 0 ? " · " + Jezyk.T("ponowień: ") + d.Ponowienia : ""); }
            kolor = Motyw.K(Color.FromArgb(160, 95, 0));
            return Jezyk.T("w kolejce, czeka na odbiór") + (d != null && d.Ponowienia > 0 ? " · " + Jezyk.T("ponowień: ") + d.Ponowienia : "");
        }

        // Oś czasu wiadomości z dziennika stosu (zdarzenia z jej id i z obiegu zadania) oraz odpowiedzi adresatów.
        string OsCzasu(Msg m)
        {
            var ids = new HashSet<string>(IdKopii(m));
            var klucz = m.LustroId.Length > 0 ? m.LustroId : m.Id;
            var wpisy = new List<Tuple<DateTime, string>>();
            var dz = Path.Combine(P.Root, "dziennik", "dziennik.jsonl");
            try
            {
                var js = P.Js();
                using (var fs = new FileStream(dz, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var r = new StreamReader(fs, Encoding.UTF8))
                {
                    string l;
                    while ((l = r.ReadLine()) != null)
                    {
                        if (l.IndexOf(m.Id, StringComparison.Ordinal) < 0 && l.IndexOf(klucz, StringComparison.Ordinal) < 0 && !ids.Any(i => l.IndexOf(i, StringComparison.Ordinal) >= 0)) continue;
                        Dictionary<string, object> e;
                        try { e = js.Deserialize<Dictionary<string, object>>(l); } catch { continue; }
                        var id = P.S(e, "id"); var kl = P.S(e, "klucz");
                        if (!(ids.Contains(id) || kl == klucz || kl == m.Id)) continue;
                        DateTime cz; if (!DateTime.TryParse(P.S(e, "czas"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out cz)) continue;
                        cz = cz.ToLocalTime();
                        var kto = P.S(e, "kto"); var doK = P.S(e, "do");
                        string opis;
                        switch (P.S(e, "zdarzenie"))
                        {
                            case "wyslana": opis = Jezyk.T("wysłana do") + " " + NazwaW(doK); break;
                            case "dostarczona": opis = Jezyk.T("odebrana przez") + " " + NazwaW(kto); break;
                            case "potwierdzona": opis = Jezyk.T("potwierdzenie") + " (" + NazwaW(kto) + "): " + P.S(e, "wynik") + (P.S(e, "uwagi").Length > 0 ? " – " + P.S(e, "uwagi") : ""); break;
                            case "ponowienie": opis = Jezyk.T("ponowienie doręczenia nr") + " " + P.S(e, "nr"); break;
                            case "eskalacja": opis = Jezyk.T("eskalacja – brak odbioru w terminie"); break;
                            case "zaginiona": opis = Jezyk.T("zaginiona"); break;
                            case "zadanie": opis = Jezyk.T("stan zadania") + ": " + P.S(e, "stan") + (kto.Length > 0 ? " (" + NazwaW(kto) + ")" : ""); break;
                            case "zarchiwizowana": opis = Jezyk.T("przeniesiona do archiwum") + (P.S(e, "powod").Length > 0 ? ": " + P.S(e, "powod") : ""); break;
                            case "obieg_zalozony": opis = Jezyk.T("obieg zadania założony"); break;
                            case "obieg_fin": opis = "FIN " + Jezyk.T("od") + " " + NazwaW(kto); break;
                            case "obieg_wycofany": opis = Jezyk.T("obieg: wycofane") + " (" + NazwaW(kto) + ")"; break;
                            case "obieg_zakonczony": opis = Jezyk.T("obieg zakończony"); break;
                            default: continue;
                        }
                        wpisy.Add(Tuple.Create(cz, opis));
                    }
                }
            }
            catch (IOException) { }
            foreach (var o in msgs.Where(x => x.OdpNa.Length > 0 && ids.Contains(x.OdpNa) && x.Od != P.Wlasciciel))
                wpisy.Add(Tuple.Create(o.Czas, (o.Typ == "fin" ? "FIN" : Jezyk.T(P.Rodzaj(o))) + " " + Jezyk.T("od") + " " + NazwaW(o.Od) + ": " + Zdanie(o)));
            var sb = new StringBuilder();
            sb.AppendLine(P.Baza(m.Temat) + "  →  " + NazwaW(m.Do));
            sb.AppendLine(Jezyk.T("Wysłano: ") + m.Czas.ToString("dd.MM.yyyy HH:mm:ss") + "   ·   " + Jezyk.T("od wysłania: ") + CzasOd(DateTime.Now - m.Czas));
            sb.AppendLine();
            foreach (var w in wpisy.OrderBy(x => x.Item1))
                sb.AppendLine(w.Item1.ToString("HH:mm:ss") + "   +" + CzasOd(w.Item1 - m.Czas).PadRight(14) + "  " + w.Item2);
            return sb.ToString();
        }

        void SledzenieWyslanych()
        {
            if (oknoSledzenia != null && !oknoSledzenia.IsDisposed) { oknoSledzenia.Show(); oknoSledzenia.WindowState = FormWindowState.Normal; oknoSledzenia.Activate(); return; }
            var f = oknoSledzenia = new OknoEcxp { Text = Jezyk.T("Śledzenie wysłanych – ECXP-MAILBOX"), Size = new Size(1180, 720), MinimumSize = new Size(760, 460),
                StartPosition = FormStartPosition.CenterParent, Font = fontN };
            var gora = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 6, 8, 2), WrapContents = true };
            var zakres = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230, Margin = new Padding(3, 5, 10, 3) };
            zakres.Items.AddRange(new object[] { Jezyk.T("Ostatnie 24 h"), Jezyk.T("Ostatnie 3 dni"), Jezyk.T("Ostatnie 7 dni"), Jezyk.T("Tylko oczekujące") });
            zakres.SelectedIndex = 0;
            var opis = new Label { AutoSize = true, Margin = new Padding(6, 9, 3, 3), ForeColor = Motyw.Slaby,
                Text = Jezyk.T("Twoje wiadomości: etap i czas od wysłania (odświeżanie co sekundę). Kliknij wiersz – oś czasu; dwuklik – otwórz wiadomość.") };
            gora.Controls.AddRange(new Control[] { zakres, opis });
            var lista = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, Dock = DockStyle.Fill, Font = fontN };
            lista.Columns.Add(Jezyk.T("Wysłano"), 140); lista.Columns.Add(Jezyk.T("Od wysłania"), 120); lista.Columns.Add(Jezyk.T("Do"), 190);
            lista.Columns.Add(Jezyk.T("Temat"), 260); lista.Columns.Add(Jezyk.T("Etap"), 380);
            var os = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 9.5f), DetectUrls = false };
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 380 };
            split.Panel1.Controls.Add(lista); split.Panel2.Controls.Add(os);
            f.Controls.Add(split); f.Controls.Add(gora);
            string podpis = ""; string wybrany = ""; string osPodpis = "";
            // Przebudowa listy i przywrócenie zaznaczenia wywołują SelectedIndexChanged od razu (wewnątrz Items.Clear
            // i it.Selected). Bez tej blokady obsługa zdarzenia przebudowywała listę od nowa – rekurencja bez końca
            // i przepełnienie stosu (awaria konsoli 3.4.0.0, 2026-11-05 00:54).
            bool budowanie = false;
            Action odswiez = () =>
            {
                if (budowanie) return;
                budowanie = true;
                try { OdswiezSledzenie(lista, os, zakres, ref podpis, ref osPodpis, wybrany); }
                finally { budowanie = false; }
            };
            lista.SelectedIndexChanged += delegate
            {
                if (budowanie) return;
                wybrany = lista.SelectedItems.Count > 0 ? ((Msg)lista.SelectedItems[0].Tag).Id : ""; osPodpis = ""; odswiez();
            };
            lista.DoubleClick += delegate { if (lista.SelectedItems.Count > 0) OtworzWiadomosc((Msg)lista.SelectedItems[0].Tag); };
            zakres.SelectedIndexChanged += delegate { podpis = ""; odswiez(); };
            var zegar = new Timer { Interval = 1000 };
            int tik = 0;
            zegar.Tick += delegate
            {
                // licznik co sekundę; oś czasu (czytanie dziennika) co 5 s
                if (++tik % 5 != 0) { var teraz = DateTime.Now; foreach (ListViewItem it in lista.Items) it.SubItems[1].Text = CzasOd(teraz - ((Msg)it.Tag).Czas); return; }
                odswiez();
            };
            f.Shown += delegate { odswiez(); zegar.Start(); };
            f.FormClosed += delegate { zegar.Stop(); zegar.Dispose(); };
            Motyw.Zastosuj(f); Jezyk.Zastosuj(f);
            os.BackColor = Motyw.Pole; os.ForeColor = Motyw.Tekst;
            f.Show(form);
        }

        // Jedno odświeżenie okna śledzenia. Wywołujący pilnuje, by nie było zagnieżdżone (blokada „budowanie”).
        void OdswiezSledzenie(ListView lista, RichTextBox os, ComboBox zakres, ref string podpis, ref string osPodpis, string wybrany)
        {
                var teraz = DateTime.Now;
                var granica = zakres.SelectedIndex == 0 ? teraz.AddDays(-1) : zakres.SelectedIndex == 1 ? teraz.AddDays(-3) : zakres.SelectedIndex == 2 ? teraz.AddDays(-7) : DateTime.MinValue;
                var wyslane = msgs.Where(m => Wyslana(m) && m.Czas >= granica && P.PoprawnyNadawca(m.Do)).OrderByDescending(m => m.Czas).ToList();
                var wiersze = new List<Tuple<Msg, string, Color>>();
                foreach (var m in wyslane)
                {
                    Color k; var etap = Etap(m, teraz, out k);
                    if (zakres.SelectedIndex == 3 && (k == Zielony || k == Motyw.Slaby)) continue;     // tylko oczekujące
                    wiersze.Add(Tuple.Create(m, etap, k));
                }
                var nowy = string.Join("|", wiersze.Select(w => w.Item1.Id + "/" + w.Item2));
                lista.BeginUpdate();
                try
                {
                    if (nowy != podpis)
                    {
                        lista.Items.Clear();
                        foreach (var w in wiersze)
                        {
                            var it = new ListViewItem(new[] { w.Item1.Czas.ToString("dd.MM HH:mm:ss"), "", NazwaW(w.Item1.Do), P.Baza(w.Item1.Temat), w.Item2 }) { Tag = w.Item1, UseItemStyleForSubItems = false };
                            it.SubItems[4].ForeColor = w.Item3;
                            lista.Items.Add(it);
                            if (w.Item1.Id == wybrany) it.Selected = true;
                        }
                        podpis = nowy;
                    }
                    foreach (ListViewItem it in lista.Items) it.SubItems[1].Text = CzasOd(teraz - ((Msg)it.Tag).Czas);
                }
                finally { lista.EndUpdate(); }
                if (lista.SelectedItems.Count > 0)
                {
                    var m = (Msg)lista.SelectedItems[0].Tag;
                    var t = OsCzasu(m);
                    if (t != osPodpis) { os.Text = t; osPodpis = t; }
                }
                else if (os.TextLength > 0 && wybrany.Length == 0) { os.Clear(); osPodpis = ""; }
        }
    }
}
