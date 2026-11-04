// Czytnik: zmiana skali bez przebudowy wiadomości oraz lokalny eksport jej migawki.
using System;
using System.Drawing;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Drawing.Printing;
using System.Diagnostics;
using System.Reflection;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Ecxp
{
    static class Czytnik
    {
        [DllImport("user32.dll", EntryPoint = "SendMessage")]
        static extern IntPtr PointMsg(IntPtr h, int msg, IntPtr wp, ref Point point);
        [DllImport("user32.dll", EntryPoint = "SendMessage")]
        static extern IntPtr Msg(IntPtr h, int msg, IntPtr wp, IntPtr lp);

        class Styl
        {
            public int Motyw, Czcionka;
            public readonly Dictionary<Font, Font> Konsole = new Dictionary<Font, Font>();
        }
        static readonly ConditionalWeakTable<RichTextBox, Styl> style = new ConditionalWeakTable<RichTextBox, Styl>();
        static Styl Dla(RichTextBox tekst) { return style.GetValue(tekst, t => new Styl()); }
        public static Color Kolor(RichTextBox tekst, Color oryginal)
        { return Dla(tekst).Motyw == 2 ? Color.White : Dla(tekst).Motyw == 1 ? Color.Black : oryginal; }
        public static Font Czcionka(RichTextBox tekst, Font oryginal)
        {
            var st = Dla(tekst); if (st.Czcionka == 0) return oryginal;
            Font font; if (!st.Konsole.TryGetValue(oryginal, out font))
                st.Konsole[oryginal] = font = new Font("Consolas", oryginal.Size, oryginal.Style);
            return font;
        }
        public static void Tlo(RichTextBox tekst)
        {
            tekst.BackColor = Dla(tekst).Motyw == 2 ? Color.Black : Dla(tekst).Motyw == 1 ? Color.White : Motyw.Pole;
            tekst.ForeColor = Kolor(tekst, Motyw.Tekst);
            if (tekst.Parent != null && tekst.Parent.Name == "ramkaCzytnika") tekst.Parent.BackColor = tekst.BackColor;
        }
        public static FlowLayoutPanel Pasek(RichTextBox tekst, Action przerysuj = null)
        {
            var panel = new FlowLayoutPanel { Name = "pasekCzytania", Dock = DockStyle.Top, AutoSize = true,
                WrapContents = true, Padding = new Padding(12, 4, 12, 4) };
            var nazwa = new Label { Text = Jezyk.T("Rozmiar tekstu"), AutoSize = true, Margin = new Padding(0, 7, 8, 0) };
            var suwak = new TrackBar { Name = "rozmiarTekstu", Minimum = 75, Maximum = 250, Value = 100,
                SmallChange = 5, LargeChange = 25, TickFrequency = 25, TickStyle = TickStyle.None,
                AutoSize = false, Size = new Size(170, 30), AccessibleName = Jezyk.T("Rozmiar tekstu") };
            var procent = new Label { Text = "100%", Width = 48, Margin = new Padding(2, 7, 8, 0) };
            var reset = new Button { Text = "100%", AutoSize = true, Height = 28, Margin = new Padding(0),
                AccessibleName = Jezyk.T("Przywróć rozmiar tekstu") };
            suwak.ValueChanged += delegate { Skala(tekst, suwak.Value / 100f); procent.Text = suwak.Value + "%"; };
            reset.Click += delegate { suwak.Value = 100; };
            var motyw = new ComboBox { Name = "motywCzytania", DropDownStyle = ComboBoxStyle.DropDownList, Width = 160,
                Margin = new Padding(12, 3, 0, 0), AccessibleName = Jezyk.T("Motyw czytania") };
            motyw.Items.AddRange(new object[] { Jezyk.T("Motyw aplikacji"), Jezyk.T("Jasny"), Jezyk.T("Ciemny konsolowy") });
            motyw.SelectedIndex = 0;
            var czcionka = new ComboBox { Name = "czcionkaCzytania", DropDownStyle = ComboBoxStyle.DropDownList, Width = 110,
                Margin = new Padding(8, 3, 0, 0), AccessibleName = Jezyk.T("Czcionka") };
            czcionka.Items.AddRange(new object[] { "Segoe UI", "Consolas" }); czcionka.SelectedIndex = 0;
            Action zmiana = () =>
            {
                int start = tekst.SelectionStart, len = tekst.SelectionLength;
                int kotwica = tekst.GetCharIndexFromPosition(new Point(1, 1));
                int y = tekst.GetPositionFromCharIndex(kotwica).Y;
                var st = Dla(tekst); st.Motyw = motyw.SelectedIndex; st.Czcionka = czcionka.SelectedIndex;
                if (przerysuj != null) przerysuj();
                Tlo(tekst);
                tekst.Select(Math.Min(start, tekst.TextLength), Math.Min(len, Math.Max(0, tekst.TextLength - start)));
                var scroll = new Point(); PointMsg(tekst.Handle, 0x04DD, IntPtr.Zero, ref scroll);
                scroll.Y = Math.Max(0, scroll.Y + tekst.GetPositionFromCharIndex(Math.Min(kotwica, tekst.TextLength)).Y - y);
                PointMsg(tekst.Handle, 0x04DE, IntPtr.Zero, ref scroll);
            };
            motyw.SelectedIndexChanged += delegate { zmiana(); };
            czcionka.SelectedIndexChanged += delegate { zmiana(); };
            tekst.Disposed += delegate { foreach (var f in Dla(tekst).Konsole.Values) f.Dispose(); style.Remove(tekst); };
            panel.Controls.AddRange(new Control[] { nazwa, suwak, procent, reset, motyw, czcionka });
            return panel;
        }

        public static void Skala(RichTextBox tekst, float skala)
        {
            skala = Math.Max(.75f, Math.Min(2.5f, skala));
            if (Math.Abs(tekst.ZoomFactor - skala) < .001f) return;
            int start = tekst.SelectionStart, len = tekst.SelectionLength;
            int kotwica = tekst.GetCharIndexFromPosition(new Point(1, 1));
            int stareY = tekst.GetPositionFromCharIndex(kotwica).Y;
            var scroll = new Point(); PointMsg(tekst.Handle, 0x04DD, IntPtr.Zero, ref scroll);
            Msg(tekst.Handle, 0x000B, IntPtr.Zero, IntPtr.Zero);
            try
            {
                tekst.ZoomFactor = skala;
                tekst.Select(start, len);
                var teraz = new Point(); PointMsg(tekst.Handle, 0x04DD, IntPtr.Zero, ref teraz);
                teraz.Y = Math.Max(0, teraz.Y + tekst.GetPositionFromCharIndex(kotwica).Y - stareY);
                teraz.X = scroll.X;
                PointMsg(tekst.Handle, 0x04DE, IntPtr.Zero, ref teraz);
            }
            finally { Msg(tekst.Handle, 0x000B, new IntPtr(1), IntPtr.Zero); tekst.Invalidate(); }
        }

        static string Escape(string value) { return SecurityElement.Escape(value ?? "") ?? ""; }
        public static string Html(string tytul, string tresc)
        {
            return "<!doctype html>\n<html lang=\"" + Escape(Jezyk.Kod) + "\"><head><meta charset=\"utf-8\">" +
                "<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">" +
                "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'\">" +
                "<title>" + Escape(tytul) + "</title><style>" +
                "body{margin:0;background:#eef1f5;color:#202735;font:16px/1.65 'Segoe UI',sans-serif}" +
                "main{max-width:860px;margin:32px auto;padding:36px 44px;background:white;border:1px solid #d9dfe7;border-radius:12px}" +
                "header{border-bottom:2px solid #e4bb21;margin-bottom:24px;padding-bottom:16px}" +
                "small{color:#5c6878}h1{font-size:25px;line-height:1.3;margin:6px 0}" +
                "pre{font:inherit;white-space:pre-wrap;overflow-wrap:anywhere}" +
                "@media print{body{background:white}main{max-width:none;margin:0;padding:0;border:0}pre{orphans:3;widows:3}}" +
                "</style></head><body><main><header><small>ECXP · " + Escape(Jezyk.T("Wiadomość")) +
                "</small><h1>" + Escape(tytul) + "</h1></header><pre>" + Escape(tresc) + "</pre></main></body></html>";
        }

        // Eksport powstaje obok celu; istniejący plik zastępujemy dopiero po udanym zapisie.
        public static void Zapisz(string plik, string tytul, string tresc, bool pdf)
        {
            plik = Path.GetFullPath(plik);
            string tmp = Path.Combine(Path.GetDirectoryName(plik), ".ecxp-" + Guid.NewGuid().ToString("N") + (pdf ? ".pdf" : ".html"));
            try
            {
                if (pdf) Pdf(tmp, tytul, tresc);
                else File.WriteAllText(tmp, Html(tytul, tresc), new UTF8Encoding(false));
                if (File.Exists(plik)) File.Replace(tmp, plik, null);
                else File.Move(tmp, plik);
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }

        public static string ProgramPdf = Assembly.GetExecutingAssembly().Location;
        static void Pdf(string plik, string tytul, string tresc)
        {
            // Sterownik drukarki działa w oddzielnym procesie, żeby jego błąd nie zamknął czytnika.
            string dane = plik + ".json";
            try
            {
                File.WriteAllText(dane, P.Js().Serialize(P.D("tytul", tytul, "tresc", tresc)), new UTF8Encoding(false));
                var psi = new ProcessStartInfo(ProgramPdf, "/eksport-pdf \"" + dane + "\" \"" + plik + "\"")
                    { UseShellExecute = false, CreateNoWindow = true };
                using (var proces = Process.Start(psi))
                {
                    if (!proces.WaitForExit(120000)) { proces.Kill(); proces.WaitForExit(); throw new IOException(Jezyk.T("Przekroczono czas zapisu PDF.")); }
                    if (proces.ExitCode != 0) throw new IOException(Jezyk.T("Nie udało się utworzyć PDF. Sprawdź systemową drukarkę Microsoft Print to PDF."));
                }
            }
            finally { if (File.Exists(dane)) File.Delete(dane); }
        }
        public static int ProcesPdf(string dane, string plik)
        {
            try
            {
                var d = P.Js().Deserialize<Dictionary<string, object>>(File.ReadAllText(dane, Encoding.UTF8));
                DrukujPdf(plik, P.S(d, "tytul"), P.S(d, "tresc"));
                return 0;
            }
            catch { return 1; }
        }
        static void DrukujPdf(string plik, string tytul, string tresc)
        {
            using (var doc = new PrintDocument())
            using (var font = new Font("Segoe UI", 11f))
            using (var maly = new Font("Segoe UI", 8.5f))
            using (var format = new StringFormat(StringFormat.GenericTypographic))
            {
                doc.PrinterSettings.PrinterName = "Microsoft Print to PDF";
                if (!doc.PrinterSettings.IsValid) throw new IOException(Jezyk.T("Brak systemowej drukarki Microsoft Print to PDF."));
                doc.PrinterSettings.PrintToFile = true;
                doc.PrinterSettings.PrintFileName = plik;
                doc.PrintController = new StandardPrintController();
                doc.DocumentName = "ECXP — " + tytul;
                doc.DefaultPageSettings.Margins = new Margins(65, 65, 65, 65);
                format.FormatFlags |= StringFormatFlags.LineLimit;
                int pozycja = 0, strona = 0;
                tresc = (tresc ?? "").Replace("\r\n", "\n");
                doc.PrintPage += delegate(object sender, PrintPageEventArgs e)
                {
                    strona++;
                    var obszar = new RectangleF(e.MarginBounds.X, e.MarginBounds.Y + 28, e.MarginBounds.Width, e.MarginBounds.Height - 56);
                    if (obszar.Width < 50 || obszar.Height < 50) throw new IOException(Jezyk.T("Za mały obszar strony PDF."));
                    using (var nagl = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                        e.Graphics.DrawString("ECXP · " + tytul, maly, Brushes.DimGray,
                            new RectangleF(e.MarginBounds.X, e.MarginBounds.Y, e.MarginBounds.Width, 24), nagl);
                    string reszta = tresc.Substring(pozycja);
                    int znaki, wiersze;
                    e.Graphics.MeasureString(reszta, font, obszar.Size, format, out znaki, out wiersze);
                    if (reszta.Length > 0 && znaki == 0) throw new IOException(Jezyk.T("Nie można podzielić tekstu na strony PDF."));
                    if (znaki < reszta.Length && znaki > 0 && char.IsHighSurrogate(reszta[znaki - 1])) znaki--;
                    if (reszta.Length > 0 && znaki == 0) throw new IOException(Jezyk.T("Nie można podzielić tekstu na strony PDF."));
                    e.Graphics.DrawString(reszta.Substring(0, znaki), font, Brushes.Black, obszar, format);
                    pozycja += znaki;
                    e.Graphics.DrawString(strona.ToString(), maly, Brushes.DimGray,
                        new PointF(e.MarginBounds.Right - 30, e.MarginBounds.Bottom - 14));
                    e.HasMorePages = pozycja < tresc.Length;
                };
                doc.Print();
            }
            for (int i = 0; i < 50 && (!File.Exists(plik) || new FileInfo(plik).Length < 5); i++) Thread.Sleep(100);
            using (var f = File.OpenRead(plik))
            {
                var b = new byte[5];
                if (f.Read(b, 0, b.Length) != 5 || Encoding.ASCII.GetString(b) != "%PDF-")
                    throw new IOException(Jezyk.T("Drukarka nie utworzyła poprawnego pliku PDF."));
            }
        }
    }
}
