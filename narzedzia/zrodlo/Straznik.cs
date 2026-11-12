// ECXP-MAILBOX – strażnik zadań: hook konsoli, który co ustawiony czas (10–60 s, domyślnie 60 s) sprawdza, czy zadania
// i zapytania wysłane przez operatora są wykonywane i na jakim są etapie. Czyta stan stosu (doręczenie, obieg, etap podany
// przez agenta, tura agenta w tle) – bez modelu AI. Gdy zadanie stoi bez zmian dłużej niż ustawiony próg, prosi stos
// o pytanie sesji o etap (Obieg.ZapytajOEtap – odpowiedź typu w_trakcie, nie FIN). Uruchamiany w zakładce Agenci.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace Ecxp
{
    partial class App
    {
        public const int StraznikMinSek = 10, StraznikMaxSek = 60, StraznikDomyslnieSek = 60, StraznikPytajDomyslnieMin = 10, StraznikZakonczoneMin = 15;

        class StanStraznika { public string Klucz = "", Etap = ""; public DateTime Zmiana; public Color Kolor; }

        bool straznikWlaczony, straznikPytaj = true, straznikDymki = true;
        int straznikSek = StraznikDomyslnieSek, straznikPytajMin = StraznikPytajDomyslnieMin;
        System.Windows.Forms.Timer timerStraznika;
        readonly Dictionary<string, StanStraznika> stanStraznika = new Dictionary<string, StanStraznika>();
        DateTime ostatnieSprawdzenie = DateTime.MinValue;
        bool pierwszeSprawdzenie = true;
        Button bStraznik; TrackBar suwakStraznika; Label lblSuwak, lblStraznik; CheckBox chPytaj, chDymki; NumericUpDown nudPytaj;
        ListView lvStraznik;

        // ---------- ustawienia (ustawienia-wlasciciel.txt: straznik=włączony|sekundy|pytaj|minuty|dymki) ----------
        void WczytajStraznika(string ust)
        {
            var m = System.Text.RegularExpressions.Regex.Match(ust ?? "", @"straznik=([01])\|(\d+)\|([01])\|(\d+)\|([01])");
            if (!m.Success) return;
            straznikWlaczony = m.Groups[1].Value == "1";
            straznikSek = Math.Max(StraznikMinSek, Math.Min(StraznikMaxSek, int.Parse(m.Groups[2].Value)));
            straznikPytaj = m.Groups[3].Value == "1";
            straznikPytajMin = Math.Max(1, Math.Min(120, int.Parse(m.Groups[4].Value)));
            straznikDymki = m.Groups[5].Value == "1";
        }
        string UstawieniaStraznika()
        {
            return "straznik=" + (straznikWlaczony ? 1 : 0) + "|" + straznikSek + "|" + (straznikPytaj ? 1 : 0) + "|" + straznikPytajMin + "|" + (straznikDymki ? 1 : 0) + Environment.NewLine;
        }

        // ---------- etap od agenta: podany przez niego (w_trakcie) albo trwająca tura agenta w tle ----------
        string EtapAgenta(Msg m, DateTime teraz)
        {
            var s = "";
            try
            {
                if (m.Typ == "zadanie")
                {
                    DateTime kiedy; var e = Obieg.EtapZgloszony(m.Id, m.Do, out kiedy);
                    if (e.Length > 0) s += " · " + Jezyk.T("agent:") + " „" + (e.Length > 160 ? e.Substring(0, 160) + "…" : e) + "”" +
                        (kiedy > DateTime.MinValue ? " (" + CzasOd(teraz - kiedy) + " " + Jezyk.T("temu") + ")" : "");
                }
                DateTime od;
                if (TuraWTle(m, out od)) s += " · " + Jezyk.T("agent w tle pracuje od") + " " + CzasOd(teraz - od);
            }
            catch { }
            return s;
        }

        // Trwająca tura agenta w tle nad tą wiadomością (znacznik tura_od w stan\agenci\<nazwa>.json; najwyżej 35 min).
        bool TuraWTle(Msg m, out DateTime od)
        {
            od = DateTime.MinValue;
            if (!P.PoprawnyNadawca(m.Do)) return false;
            var p = Path.Combine(P.Root, "stan", "agenci", m.Do + ".json");
            if (!File.Exists(p)) return false;
            var d = Pliki.Czytaj(p);
            if (P.S(d, "tura_praca") != m.Id) return false;
            DateTimeOffset t; if (!DateTimeOffset.TryParse(P.S(d, "tura_od"), out t)) return false;
            od = t.LocalDateTime;
            return od > DateTime.Now.AddMinutes(-35);
        }

        // Klucz stanu bez upływającego czasu: zmiana klucza = nowy etap (dymek, „ostatnia zmiana”).
        string KluczEtapu(Msg m)
        {
            var d = m.Dor; DateTime kiedy, od;
            var zgl = m.Typ == "zadanie" ? Obieg.EtapZgloszony(m.Id, m.Do, out kiedy) : "";
            return string.Join("|", new[] { m.Katalog, d != null && d.Dostarczona.HasValue ? "dor" : "", d != null && (d.Eskalacja || d.Zaginiona) ? "alarm" : "",
                d != null ? d.Wynik ?? "" : "", m.Typ == "zadanie" && Obieg.CzekaNaFin(m.Id, m.Do) ? "fin?" : "", zgl, TuraWTle(m, out od) ? "tlo" : "",
                OdpowiedzNa(m) != null ? "odp" : "" });
        }

        // Śledzone: zadania i zapytania operatora z ostatnich 7 dni, które czekają jeszcze na wynik (każda kopia = jeden adresat).
        List<Msg> SledzonePrzezStraznika(DateTime teraz)
        {
            // w toku (bez wyniku adresata) oraz zakończone w ostatnich StraznikZakonczoneMin minutach – zadanie, które skończyło
            // się między dwoma sprawdzeniami, też jest widoczne (z etapem „FIN po …”)
            return msgs.Where(m => Wyslana(m) && m.Blad == null && P.PoprawnyNadawca(m.Do) && (m.Typ == "zadanie" || m.WymagaOdp)
                    && m.Czas > teraz.AddDays(-7) && !(m.Dor != null && m.Dor.Wynik == "wycofana"))
                .Select(m => new { m, odp = OdpowiedzNa(m) })
                .Where(x => x.odp == null || x.odp.Czas >= teraz.AddMinutes(-StraznikZakonczoneMin))
                .OrderBy(x => x.odp != null).ThenByDescending(x => x.m.Czas).Select(x => x.m).Take(200).ToList();
        }

        // ---------- panel w zakładce Agenci ----------
        void BudujStraznika(TabPage tab)
        {
            var panel = new Panel { Dock = DockStyle.Bottom, Height = 270, Padding = new Padding(6, 4, 6, 6), Name = "panelStraznika" };
            var naglowek = new Label { Dock = DockStyle.Top, Height = 24, Font = fontB, Text = "🛡  Strażnik zadań" };
            var opis = new Label { Dock = DockStyle.Top, Height = 34, ForeColor = Motyw.Slaby, Font = fontS,
                Text = "Hook konsoli: co ustawiony czas sprawdza, czy Twoje zadania i zapytania są wykonywane i na jakim są etapie. Gdy zadanie stoi bez zmian, pyta sesję agenta o etap (odpowiedź „w trakcie”, nie FIN)." };
            var rzad = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, WrapContents = false, AutoScroll = true };
            bStraznik = Btn("▶  Uruchom strażnika"); bStraznik.Name = "przyciskStraznika";
            var lblCo = new Label { Text = "Sprawdzaj co:", AutoSize = true, Margin = new Padding(12, 10, 2, 0) };
            suwakStraznika = new TrackBar { Name = "suwakStraznika", Minimum = StraznikMinSek, Maximum = StraznikMaxSek, SmallChange = 5, LargeChange = 10,
                TickFrequency = 10, Value = straznikSek, Width = 190, Height = 34, AutoSize = false, Margin = new Padding(2, 4, 2, 0) };
            lblSuwak = new Label { AutoSize = true, Margin = new Padding(2, 10, 12, 0), Font = fontB };
            chPytaj = new CheckBox { Text = "Pytaj agenta o etap po", AutoSize = true, Checked = straznikPytaj, Margin = new Padding(8, 9, 2, 0) };
            nudPytaj = new NumericUpDown { Minimum = 1, Maximum = 120, Value = straznikPytajMin, Width = 52, Margin = new Padding(2, 7, 2, 0) };
            var lblMin = new Label { Text = "min bez zmian", AutoSize = true, Margin = new Padding(2, 10, 12, 0) };
            chDymki = new CheckBox { Text = "Powiadomienia", AutoSize = true, Checked = straznikDymki, Margin = new Padding(8, 9, 8, 0) };
            var bTeraz = Btn("Zapytaj agenta teraz"); bTeraz.Name = "zapytajTeraz";
            rzad.Controls.AddRange(new Control[] { bStraznik, lblCo, suwakStraznika, lblSuwak, chPytaj, nudPytaj, lblMin, chDymki, bTeraz });
            lblStraznik = new Label { Dock = DockStyle.Top, Height = 22, ForeColor = Motyw.Slaby, Font = fontS };
            lvStraznik = new ListView { Name = "listaStraznika", View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, HideSelection = false };
            foreach (var c in new object[][] { new object[] { "Wysłano", 120 }, new object[] { "Do", 150 }, new object[] { "Temat", 220 }, new object[] { "Etap", 430 },
                new object[] { "Ostatnia zmiana", 110 }, new object[] { "Pytanie o etap", 110 } })
                lvStraznik.Columns.Add((string)c[0], (int)c[1]);
            panel.Controls.Add(lvStraznik); panel.Controls.Add(lblStraznik); panel.Controls.Add(rzad); panel.Controls.Add(opis); panel.Controls.Add(naglowek);
            var dzielnik = new Splitter { Dock = DockStyle.Bottom, Height = 5 };
            tab.Controls.Add(dzielnik);
            tab.Controls.Add(panel);
            new ToolTip().SetToolTip(suwakStraznika, Jezyk.T("Co ile sekund strażnik sprawdza stan zadań: od 10 s do 1 min (domyślnie 1 min)."));
            new ToolTip().SetToolTip(bTeraz, Jezyk.T("Prosi sesję agenta zaznaczonego zadania o podanie etapu. Agent w tle nie dostaje pytania – jego turę widać od razu."));

            timerStraznika = new System.Windows.Forms.Timer { Interval = straznikSek * 1000 };
            timerStraznika.Tick += delegate { SprawdzStraznikiem(); };
            form.Disposed += delegate { timerStraznika.Dispose(); };
            suwakStraznika.ValueChanged += delegate
            {
                straznikSek = suwakStraznika.Value; timerStraznika.Interval = straznikSek * 1000; OpisSuwaka(); ZapiszUstawienia();
            };
            chPytaj.CheckedChanged += delegate { straznikPytaj = chPytaj.Checked; nudPytaj.Enabled = straznikPytaj; ZapiszUstawienia(); };
            nudPytaj.ValueChanged += delegate { straznikPytajMin = (int)nudPytaj.Value; ZapiszUstawienia(); };
            chDymki.CheckedChanged += delegate { straznikDymki = chDymki.Checked; ZapiszUstawienia(); };
            bStraznik.Click += delegate { UstawStraznika(!straznikWlaczony); };
            bTeraz.Click += delegate
            {
                var m = lvStraznik.SelectedItems.Count > 0 ? lvStraznik.SelectedItems[0].Tag as Msg : null;
                if (m == null) { MessageBox.Show(form, Jezyk.T("Zaznacz zadanie na liście strażnika."), Jezyk.T("Strażnik zadań")); return; }
                if (m.Typ != "zadanie") { MessageBox.Show(form, Jezyk.T("O etap można zapytać tylko przy zadaniu (obieg z FIN). Zapytanie czeka na zwykłą odpowiedź."), Jezyk.T("Strażnik zadań")); return; }
                ZapytajOEtap(m, true);
            };
            lvStraznik.DoubleClick += delegate { var m = lvStraznik.SelectedItems.Count > 0 ? lvStraznik.SelectedItems[0].Tag as Msg : null; if (m != null) OtworzWiadomosc(m); };
            OpisSuwaka();
            UstawStraznika(straznikWlaczony, false);
        }

        void OpisSuwaka()
        {
            if (lblSuwak == null) return;
            lblSuwak.Text = straznikSek >= 60 ? "1 min" : straznikSek + " s";
        }

        void UstawStraznika(bool wl, bool zapisz = true)
        {
            straznikWlaczony = wl;
            if (bStraznik != null) bStraznik.Text = Jezyk.T(wl ? "■  Zatrzymaj strażnika" : "▶  Uruchom strażnika");
            if (timerStraznika != null) { timerStraznika.Interval = straznikSek * 1000; timerStraznika.Enabled = wl; }
            if (wl) { pierwszeSprawdzenie = true; SprawdzStraznikiem(); }
            else if (lblStraznik != null) lblStraznik.Text = Jezyk.T("Strażnik zatrzymany. Uruchom go, aby śledzić etap zadań i zapytań.");
            if (zapisz) ZapiszUstawienia();
        }

        // Jedno sprawdzenie: etapy, zmiany (dymki), pytanie o etap przy zastoju.
        void SprawdzStraznikiem()
        {
            if (lvStraznik == null) return;
            var teraz = DateTime.Now;
            var ostatnieSprawdzeniePrzed = pierwszeSprawdzenie ? teraz : ostatnieSprawdzenie;
            ostatnieSprawdzenie = teraz;
            List<Msg> sledzone;
            try { sledzone = SledzonePrzezStraznika(teraz); } catch (Exception ex) { Bledy.Wyjatek(ex); return; }
            var zmiany = new List<string>();
            var doPytania = new List<Msg>();
            var wiersze = new List<ListViewItem>();
            int wToku = 0, zakonczonych = 0;
            foreach (var m in sledzone)
            {
                Color kolor; string etap, klucz; bool zakonczone;
                try { etap = Etap(m, teraz, out kolor); klucz = KluczEtapu(m); zakonczone = OdpowiedzNa(m) != null; } catch (Exception ex) { Bledy.Wyjatek(ex); continue; }
                if (zakonczone) zakonczonych++; else wToku++;
                StanStraznika s;
                if (!stanStraznika.TryGetValue(m.Id, out s))
                {
                    s = new StanStraznika { Klucz = klucz, Zmiana = teraz }; stanStraznika[m.Id] = s;
                    // wysłane i zakończone między dwoma sprawdzeniami: też powiadomienie
                    if (zakonczone && m.Czas > ostatnieSprawdzeniePrzed) zmiany.Add(P.Baza(m.Temat) + " (" + NazwaW(m.Do) + "): " + etap);
                }
                else if (s.Klucz != klucz) { s.Klucz = klucz; s.Zmiana = teraz; zmiany.Add(P.Baza(m.Temat) + " (" + NazwaW(m.Do) + "): " + etap); }
                s.Etap = etap; s.Kolor = kolor;
                var pytanie = m.Typ == "zadanie" ? Obieg.OstatniePytanieOEtap(m.Id, m.Do) : DateTime.MinValue;
                if (!zakonczone && straznikPytaj && m.Typ == "zadanie" && m.Dor != null && m.Dor.Dostarczona.HasValue && Obieg.CzekaNaFin(m.Id, m.Do)
                    && teraz - s.Zmiana >= TimeSpan.FromMinutes(straznikPytajMin) && teraz - pytanie >= TimeSpan.FromMinutes(straznikPytajMin))
                    doPytania.Add(m);
                var it = new ListViewItem(m.Czas.ToString("dd.MM HH:mm:ss")) { Tag = m, ForeColor = kolor };
                it.SubItems.Add(NazwaW(m.Do));
                it.SubItems.Add(P.Baza(m.Temat));
                it.SubItems.Add(etap);
                it.SubItems.Add(CzasOd(teraz - s.Zmiana) + " " + Jezyk.T("temu"));
                it.SubItems.Add(pytanie > DateTime.MinValue ? CzasOd(teraz - pytanie) + " " + Jezyk.T("temu") : "—");
                wiersze.Add(it);
            }
            foreach (var id in stanStraznika.Keys.Where(k => !sledzone.Any(m => m.Id == k)).ToList()) stanStraznika.Remove(id);
            var zaznaczony = lvStraznik.SelectedItems.Count > 0 ? (lvStraznik.SelectedItems[0].Tag as Msg) : null;
            lvStraznik.BeginUpdate();
            lvStraznik.Items.Clear();
            lvStraznik.Items.AddRange(wiersze.ToArray());
            if (zaznaczony != null) foreach (ListViewItem it in lvStraznik.Items) if (((Msg)it.Tag).Id == zaznaczony.Id) { it.Selected = true; break; }
            lvStraznik.EndUpdate();
            lblStraznik.Text = Jezyk.T("Strażnik działa") + " · " + Jezyk.T("ostatnie sprawdzenie") + " " + teraz.ToString("HH:mm:ss") + " · " +
                Jezyk.T("w toku") + ": " + wToku + " · " + Jezyk.T("zakończone (15 min)") + ": " + zakonczonych + (straznikPytaj ? " · " + Jezyk.T("pytanie o etap po") + " " + straznikPytajMin + " min " + Jezyk.T("bez zmian") : "");
            if (!pierwszeSprawdzenie && straznikDymki && zmiany.Count > 0 && tray != null)
                try { tray.ShowBalloonTip(5000, Jezyk.T("Strażnik zadań – nowy etap"), string.Join("\n", zmiany.Take(3)) + (zmiany.Count > 3 ? "\n…" : ""), ToolTipIcon.Info); } catch { }
            pierwszeSprawdzenie = false;
            foreach (var m in doPytania.GroupBy(x => x.LustroId.Length > 0 ? x.LustroId : x.Id).Select(g => g.First())) ZapytajOEtap(m, false);
        }

        // Pytanie o etap przez stos (polecenie operatora zapytaj_o_etap); w tle, żeby nie blokować okna.
        void ZapytajOEtap(Msg m, bool recznie)
        {
            var k = klient;
            if (k == null) { if (recznie) MessageBox.Show(form, Jezyk.T("Brak połączenia ze stosem ECXP."), Jezyk.T("Strażnik zadań")); return; }
            var id = m.Id; var temat = P.Baza(m.Temat);
            new Thread(() =>
            {
                string opis;
                try
                {
                    var r = k.Wywolaj("zapytaj_o_etap", P.D("id", id), 20000) as Dictionary<string, object>;
                    var ag = P.Lista(r, "agenci");
                    var zapytani = ag.Where(x => P.S(x, "sposob") == "zapytano").Select(x => NazwaW(P.S(x, "agent"))).ToList();
                    var wTle = ag.Where(x => P.S(x, "sposob") == "w_tle").Select(x => NazwaW(P.S(x, "agent"))).ToList();
                    var niedawno = ag.Where(x => P.S(x, "sposob") == "niedawno").Select(x => NazwaW(P.S(x, "agent"))).ToList();
                    opis = temat + ": " + (zapytani.Count > 0 ? Jezyk.T("zapytano o etap") + " – " + string.Join(", ", zapytani) : Jezyk.T("nikogo nie zapytano")) +
                        (wTle.Count > 0 ? " · " + Jezyk.T("agent w tle (stan widoczny bez pytania)") + ": " + string.Join(", ", wTle) : "") +
                        (niedawno.Count > 0 ? " · " + Jezyk.T("pytany przed chwilą") + ": " + string.Join(", ", niedawno) : "");
                }
                catch (Exception ex) { opis = temat + ": " + Jezyk.T("nie zapytano o etap – ") + ex.Message; }
                try
                {
                    form.BeginInvoke((Action)(() =>
                    {
                        if (stInfo != null) stInfo.Text = opis;
                        if (recznie) { MessageBox.Show(form, opis, Jezyk.T("Strażnik zadań")); SprawdzStraznikiem(); }
                    }));
                }
                catch { }
            }) { IsBackground = true, Name = "ecxp-straznik-pytanie" }.Start();
        }
    }
}
