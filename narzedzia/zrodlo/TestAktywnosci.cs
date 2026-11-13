// Test „Co robi agent” (Aktywnosc): opisy narzędzi, maskowanie, zdarzenia Claude/Codex, zapis i odczyt na żywo.
// Tylko dane syntetyczne; katalog testu musi być pusty.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Ecxp
{
    class TestAktywnosci
    {
        static int bledy;
        static void Ok(bool w, string n) { Console.WriteLine((w ? "PASS " : "FAIL ") + n); if (!w) bledy++; }
        static Dictionary<string, object> J(string s) { return P.Js().Deserialize<Dictionary<string, object>>(s); }

        public static int Main(string[] args)
        {
            var root = Path.GetFullPath(args[0]);
            if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new Exception("katalog testu musi być pusty");
            P.Root = root; foreach (var d in P.Foldery) Directory.CreateDirectory(Path.Combine(root, d));
            try
            {
                Ok(Aktywnosc.OpisNarzedzia("Bash", P.D("command", "dotnet test -c Release")) == "▶ uruchamia dotnet test -c Release", "Bash → uruchamia polecenie");
                Ok(Aktywnosc.OpisNarzedzia("Read", P.D("file_path", @"C:\a\b\c\d\e\f\g\h\i\j\k\l\m\n\o\p\q\r\s\t\u\v\w\x\y\z\katalog\plik.cs")).EndsWith(@"katalog\plik.cs"), "długa ścieżka skrócona do końcówki");
                Ok(Aktywnosc.OpisNarzedzia("mcp__ecxp__ecxp_wyslij", null) == "📨 ECXP: ecxp_wyslij", "narzędzie ECXP rozpoznane");
                Ok(Aktywnosc.OpisNarzedzia("mcp__github__create_pr", null) == "🔌 github: create_pr", "inne narzędzie MCP: serwer i nazwa");
                var mask = Aktywnosc.Skroc("curl -H \"Authorization: Bearer abc123\" --data password=tajne123 api_key=XYZ");
                Ok(!mask.Contains("abc123") && !mask.Contains("tajne123") && !mask.Contains("XYZ"), "hasła i klucze maskowane w opisie");
                Ok(Aktywnosc.Skroc(new string('x', 500)).Length == Aktywnosc.MaksOpisu, "opis przycięty do limitu");

                var cl = Aktywnosc.WpisyClaude(J("{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"name\":\"Grep\",\"input\":{\"pattern\":\"TODO\",\"path\":\"src\"}},{\"type\":\"text\",\"text\":\"Sprawdzam testy\"}]}}"));
                Ok(cl.Count == 2 && cl[0].Opis.StartsWith("🔎 szuka „TODO”") && cl[1].Rodzaj == "pisze", "Claude stream-json: narzędzie i tekst");
                Ok(Aktywnosc.WpisyClaude(J("{\"type\":\"result\",\"is_error\":false}"))[0].Rodzaj == "koniec", "Claude result → koniec tury");
                Ok(Aktywnosc.WpisyClaude(J("{\"type\":\"user\",\"message\":{\"content\":[{\"type\":\"tool_result\",\"content\":\"tajny wynik\"}]}}")).Count == 0, "wyniki narzędzi nie są zapisywane");

                var cx = Aktywnosc.WpisCodexAppServer("item/started", J("{\"item\":{\"type\":\"commandExecution\",\"command\":[\"powershell.exe\",\"-Command\",\"pytest -q\"]}}"));
                Ok(cx != null && cx.Opis == "▶ uruchamia pytest -q", "Codex app-server: polecenie bez powłoki");
                var cs = Aktywnosc.WpisCodexAppServer("item/started", P.D("item", P.D("type", "commandExecution", "command", "\"C:\\Program Files\\PowerShell\\7\\pwsh.exe\" -Command 'Get-Date -Format yyyy'")));
                Ok(cs != null && cs.Opis == "▶ uruchamia Get-Date -Format yyyy", "Codex app-server: polecenie jako napis z programem powłoki");
                var fc = Aktywnosc.WpisCodexAppServer("item/started", J("{\"item\":{\"type\":\"fileChange\",\"changes\":[{\"path\":\"a.cs\"},{\"path\":\"b.cs\"}]}}"));
                Ok(fc != null && fc.Opis == "✏ edytuje a.cs, b.cs", "Codex app-server: zmiana plików");
                Ok(Aktywnosc.WpisCodexAppServer("turn/completed", J("{\"turn\":{\"status\":\"failed\"}}")).Rodzaj == "blad", "Codex: nieudana tura → błąd");

                Ok(Aktywnosc.OpisCodexExec("text(await tools.shell_command({command:\"git status --short\"}));") == "▶ uruchamia git status --short", "Codex Desktop exec: polecenie powłoki");
                Ok(Aktywnosc.OpisCodexExec("text(await tools.exec_command({cmd:String.raw`$ErrorActionPreference='Stop'\n$repo='D:\\x'\ngit -C $repo status`}));") == "▶ uruchamia $repo='D:\\x' (+1 linii)", "Codex Desktop exec: skrypt String.raw – pierwsza znacząca linia");
                Ok(Aktywnosc.OpisNarzedzia("PowerShell", P.D("command", "$ErrorActionPreference='Stop'\r\ndotnet build\r\ndotnet test")) == "▶ uruchamia dotnet build (+1 linii)", "Claude: wieloliniowe polecenie skrócone");
                Ok(Aktywnosc.OpisCodexExec("text(await tools.mcp__ecxp__ecxp_odbierz({ja:\"codex-x\"}));") == "📨 ECXP: ecxp_odbierz", "Codex Desktop exec: narzędzie ECXP");
                Ok(Aktywnosc.OpisCodexExec("await tools.apply_patch(\"*** Begin Patch\\n*** Update File: src/app.cs\\n@@\")").StartsWith("✏ edytuje src/app.cs"), "Codex Desktop exec: apply_patch z plikiem");
                var rz = Aktywnosc.ZZapisuCodex(new[] {
                    "{\"timestamp\":\"2026-11-12T00:00:00Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_started\"}}",
                    "{\"timestamp\":\"2026-11-12T00:00:01Z\",\"type\":\"response_item\",\"payload\":{\"type\":\"custom_tool_call\",\"name\":\"exec\",\"input\":\"text(await tools.shell_command({command:\\\"dir\\\"}))\"}}",
                    "{\"timestamp\":\"2026-11-12T00:00:02Z\",\"type\":\"response_item\",\"payload\":{\"type\":\"function_call_output\",\"output\":\"wynik\"}}",
                    "{\"timestamp\":\"2026-11-12T00:00:03Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"task_complete\"}}" });
                Ok(rz.Count == 3 && rz[0].Rodzaj == "start" && rz[1].Opis == "▶ uruchamia dir" && rz[2].Rodzaj == "koniec", "zapis Codex Desktop: start, polecenie, koniec (bez wyników)");

                // agent w tle: zapis stosu i odczyt konsoli
                Directory.CreateDirectory(Path.Combine(root, "stan", "agenci"));
                Pliki.Zapisz(Path.Combine(root, "stan", "agenci", "claude-demo.json"), P.D("nazwa", "claude-demo", "strona", "claude", "katalog", root, "tura_od", P.Teraz(), "tura_praca", "zad-1"));
                Aktywnosc.Zapisz("claude-demo", "praca", "📥 zadanie: DEMO");
                Aktywnosc.ZClaude("claude-demo", J("{\"type\":\"assistant\",\"message\":{\"content\":[{\"type\":\"tool_use\",\"name\":\"Bash\",\"input\":{\"command\":\"pytest\"}}]}}"));
                var st = Aktywnosc.Podglad("claude-demo");
                Ok(st.Zrodlo == "tlo" && st.Pracuje && st.Praca == "zad-1" && st.Wpisy.Count == 2 && st.Ostatni.Opis == "▶ uruchamia pytest", "agent w tle: podgląd z zapisu stosu");
                Ok(Aktywnosc.Teraz(st).StartsWith("uruchamia pytest ("), "linia „teraz” dla listy rozmów");
                for (int i = 0; i < Aktywnosc.Maks * 3; i++) Aktywnosc.Zapisz("claude-demo", "narzedzie", "🔧 krok " + i + " " + new string('x', 60));
                var po = Aktywnosc.Podglad("claude-demo");
                Ok(File.ReadAllLines(Aktywnosc.Plik("claude-demo")).Length <= Aktywnosc.Maks * 2 + 5 && po.Ostatni.Opis.StartsWith("🔧 krok " + (Aktywnosc.Maks * 3 - 1)), "plik na żywo przycinany, najnowsze zostają");
                Pliki.Zapisz(Path.Combine(root, "stan", "agenci", "claude-demo.json"), P.D("nazwa", "claude-demo", "strona", "claude", "katalog", root));
                Ok(!Aktywnosc.Podglad("claude-demo").Pracuje && Aktywnosc.Teraz(Aktywnosc.Podglad("claude-demo")) == "", "po turze: bezczynny, bez linii na liście");
                Ok(Aktywnosc.Podglad("claude-nieznany").Uwaga.Length > 0, "sesja bez zapisu rozmowy: czytelna uwaga, bez wyjątku");
            }
            catch (Exception ex) { Ok(false, "wyjątek: " + ex); }
            Console.WriteLine("WYNIK: " + (bledy == 0 ? "OK" : "FAIL " + bledy));
            return bledy == 0 ? 0 : 1;
        }
    }
}
