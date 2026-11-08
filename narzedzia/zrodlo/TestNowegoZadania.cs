using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using System.Collections.Generic;
namespace Ecxp {
// Okno „Nowe zadanie”: brak modułu albo opisu nie zamyka okna i nie gubi wpisanego tekstu (zgłoszenie operatora 2026-11-08).
// Uruchomienie: TestNowegoZadania.exe <pusty katalog> – wyłącznie dane syntetyczne, bez połączenia ze stosem.
class TestNowegoZadania {
    static int failed;
    static void Check(bool ok, string name) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failed++; }
    static IEnumerable<Control> Descendants(Control parent) { foreach (Control c in parent.Controls) { yield return c; foreach (var d in Descendants(c)) yield return d; } }
    static T Wiersz<T>(Formularz f, Func<T, bool> warunek) where T : Control {
        var t = Descendants(f).OfType<TableLayoutPanel>().First();
        return Descendants(t).OfType<T>().Where(warunek).OrderBy(c => t.GetRow(c.Parent == t ? c : c.Parent)).First();
    }
    [STAThread] public static int Main(string[] args) {
      // reguły wspólne z stosem
      Check(Siec.BrakujacePole("bug-bounty-cert", "", "Opis") == "modul", "pusty moduł (przypadek operatora)");
      Check(Siec.BrakujacePole("bug-bounty-cert", " !!! ", "Opis") == "modul", "moduł bez liter i cyfr");
      Check(Siec.BrakujacePole("", "rozpoznanie", "Opis") == "projekt", "pusty projekt");
      Check(Siec.BrakujacePole("bug-bounty-cert", "rozpoznanie", " \r\n ") == "opis", "pusty opis");
      Check(Siec.BrakujacePole("bug-bounty-cert", "Rozpoznanie stron", "Opis") == "", "komplet pól");
      try { Siec.NoweZadanie("bug-bounty-cert", "", "", "Opis", new List<string>(), new List<string>(), "🔗ECXP"); Check(false, "stos odrzuca pusty moduł"); }
      catch (EcxpException ex) { Check(ex.Message == "Podaj projekt i nazwę modułu lub zadania.", "stos odrzuca pusty moduł tym samym komunikatem"); }

      if (args.Length < 1) return 2;
      string root = Path.GetFullPath(args[0]);
      if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new Exception("Test root must be empty");
      P.Root = root; foreach (string d in P.Foldery) Directory.CreateDirectory(Path.Combine(root, d));
      Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
      Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
      var app = new App();
      var fl = BindingFlags.Instance | BindingFlags.NonPublic;
      ((System.Windows.Forms.Timer)typeof(App).GetField("timer", fl).GetValue(app)).Stop();
      var form = (Form)typeof(App).GetField("form", fl).GetValue(app); form.Text = "ECXP — TESTY SYNTETYCZNE"; form.Show();
      const string opis = "Syntetyczne zadanie: zapoznaj się z regulaminem programu testowego.";
      int krok = 0; bool zamknieteAnuluj = false;
      var tm = new System.Windows.Forms.Timer { Interval = 150 };
      tm.Tick += delegate {
        var f = Application.OpenForms.OfType<Formularz>().FirstOrDefault();
        if (f == null || !f.Visible) { if (krok == 3) { zamknieteAnuluj = true; tm.Stop(); } return; }
        var cbP = Wiersz<ComboBox>(f, c => true);
        var tbM = Wiersz<TextBox>(f, c => !c.Multiline);
        var tbO = Wiersz<TextBox>(f, c => c.Multiline && !c.ReadOnly);
        var ok = (Button)typeof(Formularz).GetField("ok", fl).GetValue(f);
        try {
          if (krok == 0) { cbP.Text = "bug-bounty-cert"; tbM.Text = ""; tbO.Text = opis; ok.PerformClick(); krok = 1; return; }
          if (krok == 1) {
            Check(f.Visible, "okno zostaje otwarte przy pustym module");
            Check(tbO.Text == opis, "opis zadania nie zginął");
            Check(f.TekstBledu.Contains("modułu"), "komunikat o module: " + f.TekstBledu);
            Check(f.DialogResult == DialogResult.None, "DialogResult wyzerowany");
            tbM.Text = "rozpoznanie"; tbO.Text = "  "; ok.PerformClick(); krok = 2; return;
          }
          if (krok == 2) {
            Check(f.Visible, "okno zostaje otwarte przy pustym opisie");
            Check(f.TekstBledu.Contains("Opisz"), "komunikat o opisie: " + f.TekstBledu);
            Check(tbM.Text == "rozpoznanie", "nazwa modułu nie zginęła");
            krok = 3; f.Close(); return;
          }
        } catch (Exception ex) { Check(false, "wyjątek: " + ex.Message); tm.Stop(); f.Close(); }
      };
      tm.Start();
      typeof(App).GetMethod("NoweZadanie", fl).Invoke(app, null);
      Check(zamknieteAnuluj || krok == 3, "zamknięcie bez wysyłania kończy okno");
      Check(!Directory.Exists(Path.Combine(root, "projekty", "bug-bounty-cert")), "nic nie wysłano do stosu");
      form.Close();
      Console.WriteLine(failed == 0 ? "WYNIK: PASS" : "WYNIK: FAIL " + failed);
      return failed == 0 ? 0 : 1;
    }
}
}
