// ECXP-MAILBOX – spersonalizowani agenci: zakładka Agenci pokazuje pojedynczych agentów (jeden wiersz = jeden agent)
// z nazwą własną, stanem i bieżącym zadaniem. Z prawego menu operator przydziela zadanie konkretnemu agentowi,
// edytuje jego pliki, konfiguruje go, wyłącza i zatrzymuje. Widok par 1:1 zostaje jako drugi widok tej samej zakładki.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace Ecxp
{
    partial class App
    {
        ListView lvOsoby;
        RadioButton rbOsoby, rbPary;
        bool odswiezanieOsob;
        string wybranaOsoba = "";          // tożsamość ECXP agenta zaznaczonego w widoku agentów
        string przydzielDo, przydzielProjekt, przydzielModul;   // „Przydziel zadanie”: wstępne wypełnienie okna „Napisz”

        bool WidokOsob { get { return rbOsoby != null && rbOsoby.Checked; } }

        // Nazwa, pod którą operator widzi agenta: własna (profil) → nazwa z katalogu agentów → tożsamość ECXP.
        string ImieAgenta(string nazwa, List<AgentKatalogu> katalog = null)
        {
            var imie = Profile.Imie(nazwa);
            if (imie.Length > 0) return imie;
            try
            {
                var a = (katalog ?? KatalogAgentow.Znajdz(katalogAgentow)).FirstOrDefault(x => x.Tozsamosc == nazwa);
                if (a != null && a.Nazwa.Length > 0) return a.Nazwa;
            }
            catch (Exception ex) { Bledy.Wyjatek(ex); }
            return nazwa;
        }

        void BudujOsoby(Control tab)
        {
            lvOsoby = new ListView { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, HideSelection = false, MultiSelect = false, Name = "listaOsob", ShowItemToolTips = true };
            foreach (var c in new object[][] { new object[] { "Agent", 210 }, new object[] { "Strona", 80 }, new object[] { "Stan", 190 }, new object[] { "Bieżące zadanie", 300 },
                new object[] { "Projekt / moduł", 190 }, new object[] { "Role", 170 }, new object[] { "Tożsamość ECXP", 200 }, new object[] { "Katalog pracy", 240 }, new object[] { "Opis", 260 } })
                lvOsoby.Columns.Add((string)c[0], (int)c[1]);
            var pasek = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, WrapContents = false, Name = "widokAgentow", Padding = new Padding(4, 2, 0, 0) };
            rbOsoby = new RadioButton { Text = "👤  Agenci", Appearance = Appearance.Button, AutoSize = true, Checked = true, Name = "widokOsoby", TextAlign = ContentAlignment.MiddleCenter };
            rbPary = new RadioButton { Text = "🔗  Pary 1:1", Appearance = Appearance.Button, AutoSize = true, Name = "widokPary", TextAlign = ContentAlignment.MiddleCenter };
            var podp = new Label { AutoSize = true, Margin = new Padding(12, 8, 0, 0), ForeColor = Motyw.Slaby, Name = "podpowiedzOsob",
                Text = "Prawy klawisz na agencie: przydziel zadanie, edytuj, skonfiguruj, wyłącz, zatrzymaj. Dwuklik – przydziel zadanie." };
            pasek.Controls.AddRange(new Control[] { rbOsoby, rbPary, podp });
            tab.Controls.Add(lvOsoby);
            tab.Controls.Add(pasek);
            EventHandler widok = delegate
            {
                lvOsoby.Visible = rbOsoby.Checked; lvAg.Visible = !rbOsoby.Checked; podp.Visible = rbOsoby.Checked;
                if (!rbOsoby.Checked) wybranaOsoba = "";
                try { SprawdzStraznikiem(); } catch (Exception ex) { Bledy.Wyjatek(ex); }
            };
            rbOsoby.CheckedChanged += widok;
            lvAg.Visible = false;

            var menu = new ContextMenuStrip { Name = "menuOsoby" };
            Func<string, string, Action<string>, ToolStripMenuItem> poz = (tekst, nazwa, akcja) =>
            {
                var mi = new ToolStripMenuItem(tekst, null, delegate { var st = StronaOsoby(); if (st != null) akcja(st); }) { Name = nazwa };
                menu.Items.Add(mi); return mi;
            };
            var miZadanie = poz("✉  Przydziel zadanie temu agentowi…", "osobaZadanie", st => PrzydzielZadanie(st));
            miZadanie.Font = new Font(menu.Font, FontStyle.Bold);
            poz("👁  Co robi ten agent…", "osobaCoRobi", st => PokazAktywnosc(wybranaOsoba));
            menu.Items.Add(new ToolStripSeparator());
            poz("📝  Edytuj agenta (pliki konfiguracji)…", "osobaEdytuj", st => EdytujAgenta(st));
            poz("✉  Zmień konfigurację wiadomością…", "osobaZmienWiadomoscia", st => ZmienKonfiguracjeWiadomoscia(st));
            poz("⚙  Skonfiguruj agenta…", "osobaKonfiguruj", st => KonfigurujAgenta(st));
            poz("🏷  Nazwa własna i opis agenta…", "osobaProfil", st => NazwijAgenta(wybranaOsoba));
            menu.Items.Add(new ToolStripSeparator());
            var miWyl = poz("⏸  Wyłącz agenta", "osobaWylacz", st => PrzelaczAgenta(st));
            var miStop = poz("■  Zatrzymaj teraz agenta", "osobaZatrzymaj", st => ZatrzymajAgentaTeraz(st));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("🗂  Katalog agentów: dodaj agenta, zmień katalog…", null, delegate { PokazKatalogAgentow(); }) { Name = "osobaKatalog" });
            menu.Opening += (s, e) =>
            {
                bool jest = StronaOsoby() != null, wyl = jest && Wylaczeni.Jest(wybranaOsoba);
                foreach (ToolStripItem it in menu.Items) if (it.Name != "osobaKatalog") it.Enabled = jest;
                miWyl.Text = wyl ? "▶  " + Jezyk.T("Włącz agenta") : "⏸  " + Jezyk.T("Wyłącz agenta");
                miStop.Enabled = jest && !wyl;
                miZadanie.Enabled = jest && !wyl;
                Jezyk.Zastosuj(menu);
            };
            lvOsoby.ContextMenuStrip = menu;
            lvOsoby.DoubleClick += delegate { var st = StronaOsoby(); if (st != null && !Wylaczeni.Jest(wybranaOsoba)) PrzydzielZadanie(st); };
            lvOsoby.MouseDown += (s, e) =>
            {
                // prawy klawisz od razu ustawia agenta, którego dotyczy menu; klik w puste miejsce zdejmuje zaznaczenie
                var trafiony = lvOsoby.HitTest(e.Location).Item;
                if (trafiony == null) { foreach (ListViewItem it in lvOsoby.SelectedItems) it.Selected = false; wybranaOsoba = ""; }
                else { wybranaOsoba = (string)((object[])trafiony.Tag)[2]; if (!trafiony.Selected) trafiony.Selected = true; }
            };
            lvOsoby.SelectedIndexChanged += delegate
            {
                if (odswiezanieOsob) return;
                var tag = lvOsoby.SelectedItems.Count > 0 ? (object[])lvOsoby.SelectedItems[0].Tag : null;
                wybranaOsoba = tag != null ? (string)tag[2] : "";
                try { SprawdzStraznikiem(); } catch (Exception ex) { Bledy.Wyjatek(ex); }
            };
        }

        // Strona („codex” / „claude”) agenta zaznaczonego w widoku agentów; null, gdy nic nie zaznaczono.
        string StronaOsoby()
        {
            var os = WybranaOsobaTag();
            return os != null ? (string)os[1] : null;
        }

        // Dane zaznaczonego agenta { para, strona, tożsamość }. Źródłem jest zapamiętana tożsamość (wybranaOsoba), nie zaznaczenie
        // kontrolki – lista jest przebudowywana przy każdym odświeżeniu, a zaznaczenie kontrolki bywa wtedy chwilowo puste.
        object[] WybranaOsobaTag()
        {
            if (lvOsoby == null || wybranaOsoba.Length == 0) return null;
            foreach (ListViewItem it in lvOsoby.Items) { var t = (object[])it.Tag; if ((string)t[2] == wybranaOsoba) return t; }
            return null;
        }

        // Zadanie operatora, nad którym agent teraz pracuje: najnowsze zadanie do niego bez odpowiedzi.
        Msg BiezaceZadanie(string nazwa)
        {
            try
            {
                return msgs.Where(m => m.Do == nazwa && m.Od == P.Wlasciciel && m.Typ == "zadanie" && OdpowiedzNa(m) == null).OrderByDescending(m => m.Czas).FirstOrDefault();
            }
            catch (Exception ex) { Bledy.Wyjatek(ex); return null; }
        }

        void RefreshOsoby()
        {
            if (lvOsoby == null) return;
            var trasy = Siec.Trasy().Where(x => x.Aktywna).ToList();
            var wTle = Agenci.Lista();
            var katalog = new List<AgentKatalogu>();
            try { katalog = KatalogAgentow.Znajdz(katalogAgentow); } catch (Exception ex) { Bledy.Wyjatek(ex); }
            odswiezanieOsob = true;
            lvOsoby.BeginUpdate();
            try
            {
                lvOsoby.Items.Clear();
                foreach (ListViewItem wiersz in lvAg.Items)
                {
                    var d = (Dictionary<string, object>)wiersz.Tag;
                    string pr = P.S(d, "projekt"), mod = P.S(d, "modul");
                    foreach (var st in new[] { "codex", "claude" })
                    {
                        var n = P.S(d, st);
                        if (n.Length == 0) continue;
                        var rec = wTle.FirstOrDefault(x => P.S(x, "nazwa") == n);
                        var trasa = trasy.FirstOrDefault(x => x.Projekt == pr && x.Modul == mod && x.Nazwa == n);
                        bool wyl = Wylaczeni.Jest(n), tlo = rec != null && P.B(rec, "aktywny");
                        bool pracuje = false; DateTime od = DateTime.MinValue;
                        if (rec != null && Agenci.TuraZyje(rec)) { DateTimeOffset t; if (DateTimeOffset.TryParse(P.S(rec, "tura_od"), out t)) { od = t.LocalDateTime; pracuje = true; } }
                        var zad = BiezaceZadanie(n);
                        string stan = wyl ? "⏸ " + Jezyk.T("WYŁĄCZONY")
                            : pracuje ? "● " + Jezyk.T("pracuje od") + " " + od.ToString("HH:mm")
                            : zad != null ? "◔ " + Jezyk.T("ma zadanie, czeka na turę")
                            : tlo ? "○ " + Jezyk.T("wolny (agent w tle)") : "○ " + Jezyk.T("wolny (sesja w aplikacji)");
                        var imie = ImieAgenta(n, katalog);
                        var it = new ListViewItem((st == "codex" ? "◆ " : "◇ ") + imie);
                        it.SubItems.Add(st == "codex" ? "Codex" : "Claude");
                        it.SubItems.Add(stan);
                        it.SubItems.Add(zad != null ? P.Baza(zad.Temat) : "—");
                        it.SubItems.Add(pr + " / " + mod);
                        it.SubItems.Add(trasa != null ? string.Join(", ", trasa.Role) : "");
                        it.SubItems.Add(n);
                        it.SubItems.Add(rec != null ? P.S(rec, "katalog") : "");
                        it.SubItems.Add(Profile.Opis(n));
                        it.ToolTipText = imie + "  ·  " + n + "\n" + stan + (Profile.Opis(n).Length > 0 ? "\n" + Profile.Opis(n) : "");
                        it.ForeColor = wyl ? Motyw.Slaby : pracuje ? Zielony : Motyw.Tekst;
                        if (pracuje) it.Font = fontB;
                        it.Tag = new object[] { d, st, n };
                        lvOsoby.Items.Add(it);
                        if (n == wybranaOsoba) it.Selected = true;
                    }
                }
                if (WybranaOsobaTag() == null) wybranaOsoba = "";       // agent zniknął z listy (np. inny projekt)
            }
            finally { lvOsoby.EndUpdate(); odswiezanieOsob = false; }
            if (WidokOsob) tabAg.Text = Jezyk.T("Agenci") + " (" + lvOsoby.Items.Count + ")";
        }

        // „Przydziel zadanie”: okno „Napisz” z adresem tylko tego agenta (druga strona pary nie dostaje kopii) i typem „zadanie”.
        void PrzydzielZadanie(string strona)
        {
            var d = WybranaPara(); var n = d != null ? P.S(d, strona) : "";
            if (n.Length == 0) return;
            if (Wylaczeni.Jest(n)) { MessageBox.Show(form, string.Format(Jezyk.T("Agent {0} jest wyłączony – najpierw go włącz."), ImieAgenta(n)), Jezyk.T("Przydziel zadanie"), MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
            przydzielDo = strona + "/" + P.S(d, "modul"); przydzielProjekt = P.S(d, "projekt"); przydzielModul = P.S(d, "modul");
            try { Pisz(null); } finally { przydzielDo = null; }
        }

        // Nazwa własna i opis agenta (polecenie stosu profil_agenta).
        void NazwijAgenta(string nazwa)
        {
            if (string.IsNullOrEmpty(nazwa)) return;
            using (var f = new Formularz(Jezyk.T("Nazwa własna i opis agenta") + " – " + nazwa, fontN, fontB))
            {
                f.Size = new Size(720, 360);
                var tbI = f.Tekst("Nazwa własna:", Profile.Imie(nazwa)); tbI.Name = "imieAgenta"; tbI.MaxLength = Profile.MaksImie;
                var tbO = f.Tekst("Opis (czym się zajmuje):", Profile.Opis(nazwa)); tbO.Name = "opisAgenta"; tbO.MaxLength = Profile.MaksOpis;
                f.Uwaga("Nazwa własna służy Tobie do rozpoznawania agenta w konsoli (np. „Tester sieci”, „Recenzent raportów”). Tożsamość ECXP (" + nazwa + ") i adresowanie się nie zmieniają. Puste pola przywracają nazwę z katalogu agentów albo tożsamość ECXP.");
                f.PrzyciskOk("Zapisz");
                while (f.ShowDialog(form) == DialogResult.OK)
                {
                    try
                    {
                        Stos("profil_agenta", P.D("nazwa", nazwa, "imie", tbI.Text, "opis", tbO.Text));
                        stInfo.Text = Jezyk.T("Zapisano nazwę agenta: ") + ImieAgenta(nazwa);
                        Skanuj();
                        break;
                    }
                    catch (Exception ex) { Bledy.Wyjatek(ex); f.Blad(ex.Message); }
                }
            }
        }
    }
}
