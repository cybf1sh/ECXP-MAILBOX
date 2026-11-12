// ECXP-MAILBOX – okno „👁 Co robi agent”: bieżące działania agenta (Aktywnosc.Podglad), odświeżane co sekundę.
// Otwierane z zakładki Agenci (prawy przycisk na parze) i z menu wiadomości („Co teraz robi agent?”).
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
        readonly Dictionary<string, Form> oknaAktywnosci = new Dictionary<string, Form>();

        // Agent rozmowy: adresat Twojej wiadomości albo nadawca odebranej – tylko nazwy agentów (codex-…, claude-…).
        static string AgentRozmowy(Msg m)
        {
            if (m == null) return null;
            var n = Wyslana(m) ? m.Do : m.Od;
            return n != null && (n.StartsWith("codex-") || n.StartsWith("claude-")) ? n : null;
        }

        static string OpisZrodla(string z)
        {
            return z == "tlo" ? Jezyk.T("agent w tle (zdarzenia zapisuje stos ECXP)")
                 : z == "claude" ? Jezyk.T("sesja w aplikacji Claude (odczyt zapisu rozmowy)")
                 : z == "codex" ? Jezyk.T("sesja w aplikacji Codex (odczyt zapisu rozmowy)") : Jezyk.T("brak danych");
        }

        void PokazAktywnosc(string nazwa)
        {
            if (string.IsNullOrEmpty(nazwa)) return;
            Form f;
            if (oknaAktywnosci.TryGetValue(nazwa, out f) && !f.IsDisposed)
            {
                if (f.WindowState == FormWindowState.Minimized) f.WindowState = FormWindowState.Normal;
                f.Activate(); return;
            }
            f = new Form { Text = Jezyk.T("👁 Co robi agent") + " – " + NazwaW(nazwa) + "  (" + nazwa + ")", Size = new Size(860, 560), MinimumSize = new Size(520, 320),
                StartPosition = FormStartPosition.CenterScreen, Icon = form.Icon, Font = form.Font, Name = "oknoAktywnosci" };
            var naglowek = new Label { Dock = DockStyle.Top, Height = 30, Padding = new Padding(10, 8, 10, 0), Font = fontB, Name = "stanAgenta" };
            var zrodlo = new Label { Dock = DockStyle.Top, Height = 40, Padding = new Padding(10, 2, 10, 0), ForeColor = Motyw.Slaby, Font = fontS, Name = "zrodloAktywnosci" };
            var lista = new ListView { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, HeaderStyle = ColumnHeaderStyle.Nonclickable, Name = "listaAktywnosci" };
            lista.Columns.Add("Czas", 120); lista.Columns.Add("Działanie", 700);
            lista.Resize += delegate { lista.Columns[1].Width = Math.Max(200, lista.ClientSize.Width - lista.Columns[0].Width - 4); };
            var dol = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
            var bZamknij = new Button { Text = "Zamknij", AutoSize = true, Height = 32 };
            var bLog = new Button { Text = "📄  Log tur", AutoSize = true, Height = 32 };
            var bZapis = new Button { Text = "📂  Pokaż zapis", AutoSize = true, Height = 32 };
            var chPrzewijaj = new CheckBox { Text = "Przewijaj do najnowszych", Checked = true, AutoSize = true, Margin = new Padding(12, 9, 6, 0) };
            dol.Controls.AddRange(new Control[] { bZamknij, bLog, bZapis, chPrzewijaj });
            f.Controls.Add(lista); f.Controls.Add(zrodlo); f.Controls.Add(naglowek); f.Controls.Add(dol);
            bZamknij.Click += delegate { f.Close(); };

            StanAktywnosci ostatni = null; string podpis = ""; bool trwa = false;
            var plikLogu = Path.Combine(P.Root, "stan", "agenci", Path.GetFileName(nazwa) + ".log");
            bLog.Click += delegate { if (File.Exists(plikLogu)) OpenPath(plikLogu); else MessageBox.Show(f, Jezyk.T("Ten agent nie ma logu tur (log mają tylko agenci w tle)."), f.Text); };
            bZapis.Click += delegate
            {
                var p = ostatni != null ? ostatni.Plik : "";
                if (p.Length > 0 && File.Exists(p)) try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + p + "\""); } catch (Exception ex) { Bledy.Wyjatek(ex); }
                else MessageBox.Show(f, Jezyk.T("Brak pliku z działaniami tego agenta."), f.Text);
            };

            Action<StanAktywnosci> pokaz = st =>
            {
                if (f.IsDisposed || st == null) return;
                ostatni = st;
                var ost = st.Ostatni;
                string praca = "";
                if (st.Praca.Length > 0) { var pm = msgs.FirstOrDefault(x => x.Id == st.Praca); praca = " · " + (pm != null ? Jezyk.T("praca: ") + P.Baza(pm.Temat) : st.Praca); }
                naglowek.Text = st.Pracuje
                    ? "● " + Jezyk.T("pracuje") + (st.Od.HasValue ? " " + Jezyk.T("od") + " " + P.Wiek(DateTime.Now - st.Od.Value) : "") + praca
                    : "○ " + Jezyk.T("bezczynny") + (ost != null ? " · " + Jezyk.T("ostatnie działanie") + " " + P.Wiek(DateTime.Now - ost.Czas) + " " + Jezyk.T("temu") : "");
                naglowek.ForeColor = st.Pracuje ? Zielony : Motyw.Tekst;
                zrodlo.Text = Jezyk.T("Źródło: ") + OpisZrodla(st.Zrodlo) + (st.Uwaga.Length > 0 ? "\n" + Jezyk.T(st.Uwaga) : "");
                bLog.Enabled = File.Exists(plikLogu);
                var nowy = st.Wpisy.Count + "|" + (ost != null ? ost.Czas.Ticks + ost.Opis : "");
                if (nowy == podpis) return;
                podpis = nowy;
                lista.BeginUpdate();
                try
                {
                    lista.Items.Clear();
                    foreach (var w in st.Wpisy)
                    {
                        var it = new ListViewItem(w.Czas.Date == DateTime.Today ? w.Czas.ToString("HH:mm:ss") : w.Czas.ToString("dd.MM HH:mm:ss"));
                        it.SubItems.Add(w.Opis);
                        it.ForeColor = w.Rodzaj == "koniec" ? Zielony : w.Rodzaj == "blad" ? Czerwony : w.Rodzaj == "praca" || w.Rodzaj == "wpis" || w.Rodzaj == "start" ? Motyw.Tekst : Motyw.Tekst;
                        if (w.Rodzaj == "praca" || w.Rodzaj == "wpis") it.Font = fontB;
                        lista.Items.Add(it);
                    }
                    if (chPrzewijaj.Checked && lista.Items.Count > 0) lista.EnsureVisible(lista.Items.Count - 1);
                }
                finally { lista.EndUpdate(); }
            };
            Action odswiez = () =>
            {
                if (trwa || f.IsDisposed) return;
                trwa = true;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    StanAktywnosci st = null;
                    try { st = Aktywnosc.Podglad(nazwa); } catch (Exception ex) { Bledy.Wyjatek(ex); }
                    try { if (!f.IsDisposed) f.BeginInvoke((Action)(() => { try { pokaz(st); } finally { trwa = false; } })); else trwa = false; }
                    catch { trwa = false; }
                });
            };
            var zegar = new System.Windows.Forms.Timer { Interval = 1000 };
            zegar.Tick += delegate { odswiez(); };
            f.Shown += delegate { odswiez(); zegar.Start(); };
            f.FormClosed += delegate { zegar.Stop(); zegar.Dispose(); oknaAktywnosci.Remove(nazwa); };
            oknaAktywnosci[nazwa] = f;
            Motyw.Zastosuj(f); Jezyk.Zastosuj(f);
            f.Show();
        }

        // ---------- linia „teraz: …” na liście rozmów ----------
        readonly Dictionary<string, KeyValuePair<DateTime, StanAktywnosci>> pamiecAktywnosci = new Dictionary<string, KeyValuePair<DateTime, StanAktywnosci>>();

        // Stan agenta z pamięcią 5 s (lista rozmów odświeża się często; odczyt zapisu sesji nie może jej spowalniać).
        StanAktywnosci AktywnoscAgenta(string nazwa)
        {
            KeyValuePair<DateTime, StanAktywnosci> kv;
            if (pamiecAktywnosci.TryGetValue(nazwa, out kv) && DateTime.Now - kv.Key < TimeSpan.FromSeconds(5)) return kv.Value;
            StanAktywnosci st = null;
            try { st = Aktywnosc.Podglad(nazwa); } catch (Exception ex) { Bledy.Wyjatek(ex); }
            pamiecAktywnosci[nazwa] = new KeyValuePair<DateTime, StanAktywnosci>(DateTime.Now, st);
            return st;
        }

        // Dla najnowszej Twojej wiadomości do każdego agenta (z ostatnich 2 dni, bez wyniku): co agent właśnie robi.
        // Agent w tle – tylko gdy jego bieżąca tura dotyczy tej wiadomości.
        Dictionary<string, string> TerazAgentow(IEnumerable<List<Msg>> watki)
        {
            var wy = new Dictionary<string, string>();
            var granica = DateTime.Now.AddDays(-2);
            var najnowsze = watki.Select(w => w.LastOrDefault(x => Wyslana(x) && x.Blad == null && AgentRozmowy(x) != null))
                .Where(x => x != null && x.Czas > granica).GroupBy(x => x.Do).Select(g => g.OrderByDescending(x => x.Czas).First());
            foreach (var m in najnowsze)
            {
                if (OdpowiedzNa(m) != null) continue;
                var st = AktywnoscAgenta(m.Do);
                if (st == null || !st.Pracuje) continue;
                if (st.Zrodlo == "tlo" && st.Praca.Length > 0 && st.Praca != m.Id && st.Praca != m.KopiaDla) continue;
                var t = Aktywnosc.Teraz(st);
                if (t.Length > 0) wy[m.Id] = t;
            }
            return wy;
        }
    }
}
