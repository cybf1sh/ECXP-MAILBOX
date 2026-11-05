// Test schematu odświeżania okna „Śledzenie wysłanych” (Sledzenie.cs) na prawdziwej kontrolce ListView.
// csc /r:System.Windows.Forms.dll TestSledzenia.cs
//   TestSledzenia.exe stary  -> StackOverflowException (schemat klienta 3.4.0.0, awaria 2026-11-05 00:54)
//   TestSledzenia.exe        -> PASS (blokada ponownego wejścia: bez zagnieżdżenia, zaznaczenie zachowane)
// Odtworzenie schematu z Sledzenie.cs: przebudowa listy przy zaznaczonym wierszu.
using System; using System.Linq; using System.Collections.Generic; using System.Windows.Forms;
class T {
  static int Uruchom(bool zBlokada, out string wybranyPo, out string blad) {
    var f = new Form(); var lista = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false }; lista.Columns.Add("a"); f.Controls.Add(lista);
    var h = lista.Handle;
    string podpis = "", wybrany = ""; int gl = 0, maks = 0; bool budowanie = false; var etapy = new[] { "w kolejce", "w kolejce", "w kolejce" };
    Action rdzen = () => {
      gl++; maks = Math.Max(maks, gl); if (gl > 200) throw new Exception("rekurencja > 200");
      var nowy = string.Join("|", etapy.Select((e, i) => "id" + i + "/" + e));
      lista.BeginUpdate();
      try {
        if (nowy != podpis) {
          lista.Items.Clear();
          for (int i = 0; i < etapy.Length; i++) { var it = new ListViewItem(etapy[i]) { Tag = "id" + i }; lista.Items.Add(it); if ("id" + i == wybrany) it.Selected = true; }
          podpis = nowy;
        }
      } finally { lista.EndUpdate(); gl--; }
    };
    Action odswiez = zBlokada ? (Action)(() => { if (budowanie) return; budowanie = true; try { rdzen(); } finally { budowanie = false; } }) : rdzen;
    lista.SelectedIndexChanged += delegate {
      if (zBlokada && budowanie) return;
      wybrany = lista.SelectedItems.Count > 0 ? (string)lista.SelectedItems[0].Tag : ""; odswiez(); };
    blad = "";
    try {
      odswiez();
      lista.Items[1].Selected = true;      // operator klika wiersz
      etapy[1] = "odebrane po 40 s";       // etap się zmienia -> przebudowa przy zaznaczeniu (tik zegara)
      odswiez();
    } catch (Exception e) { blad = e.GetType().Name + ": " + e.Message; }
    wybranyPo = lista.SelectedItems.Count > 0 ? (string)lista.SelectedItems[0].Tag : "(brak)";
    f.Dispose(); return maks;
  }
  [STAThread] static int Main(string[] a) {
    Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
    string w, b; int fail = 0;
    int m = 0; if (a.Length > 0 && a[0] == "stary") { m = Uruchom(false, out w, out b);
    Console.WriteLine("STARY: glebokosc=" + m + " zaznaczony=" + w + " blad=" + (b == "" ? "-" : b));
    if (b == "" && m < 3) { Console.WriteLine("  (stary schemat nie odtworzyl bledu)"); fail++; } return fail; }
    m = Uruchom(true, out w, out b);
    Console.WriteLine("NOWY:  glebokosc=" + m + " zaznaczony=" + w + " blad=" + (b == "" ? "-" : b));
    if (b != "" || m != 1) { Console.WriteLine("  FAIL: zagniezdzenie"); fail++; }
    if (w != "id1") { Console.WriteLine("  FAIL: zaznaczenie utracone"); fail++; }
    Console.WriteLine(fail == 0 ? "PASS" : "FAIL " + fail); return fail;
  }
}
