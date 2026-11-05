// Powiadomienie istniejącej sesji przez oficjalne codex queue. Bez nowej tury zastępczej, bez nadpisywania uprawnień.
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
namespace Ecxp
{
    static class CodexBudzenie
    {
        const string KolejkaPrefix = "powiadomienie w natywnej kolejce Codexa: ";
        const string WznowieniePrefix = "; wznowienie niepotwierdzone: ";
        public static bool DodanoDoKolejki(string wynik) { return (wynik ?? "").StartsWith(KolejkaPrefix,StringComparison.Ordinal); }
        public static bool NieWznowiono(string wynik) { return (wynik ?? "").Contains(WznowieniePrefix); }

        public static string NatywnyProgram(string program)
        {
            if (string.IsNullOrEmpty(program)) throw new EcxpException("Brak Codex CLI.");
            if (program.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(program)) return program;
            // Oficjalna instalacja npm zawiera natywny program. Nie przekazujemy wiadomości przez cmd.exe/PowerShell.
            var dir = Path.GetDirectoryName(Path.GetFullPath(program));
            foreach (var arch in new[] { "x64", "arm64" })
            {
                var triple = arch == "x64" ? "x86_64-pc-windows-msvc" : "aarch64-pc-windows-msvc";
                var native = Path.Combine(dir, "node_modules", "@openai", "codex", "node_modules", "@openai", "codex-win32-" + arch, "vendor", triple, "bin", "codex.exe");
                if (File.Exists(native)) return native;
            }
            throw new EcxpException("Nie znaleziono natywnego codex.exe obsługującego queue. Zainstaluj aktualny Codex CLI.");
        }
        // Cytowanie jednego argumentu według reguł Windows CRT (bez udziału powłoki).
        public static string Argument(string value)
        {
            var b = new StringBuilder("\""); int slashes = 0;
            foreach (char c in value ?? "")
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { b.Append('\\', slashes * 2 + 1); b.Append(c); }
                else { b.Append('\\', slashes); b.Append(c); }
                slashes = 0;
            }
            b.Append('\\', slashes * 2); return b.Append('"').ToString();
        }
        public static ProcessStartInfo Przygotuj(string program, string watek, string tekst, string katalog)
        {
            Guid id; if (!Guid.TryParse(watek, out id)) throw new EcxpException("Niepoprawny identyfikator wątku Codexa.");
            return new ProcessStartInfo(NatywnyProgram(program), "queue --thread " + Argument(watek) + " --message " + Argument(tekst) + " --resume")
            {
                UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = katalog,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
        }
        public static string Wyslij(string program, string watek, string tekst, string katalog)
        {
            var output = new StringBuilder(); var errors = new StringBuilder(); int kod;
            using (var pr = Process.Start(Przygotuj(program, watek, tekst, katalog)))
            {
                pr.OutputDataReceived += (s,e) => { if(e.Data != null) lock(output) output.AppendLine(e.Data); };
                pr.ErrorDataReceived += (s,e) => { if(e.Data != null) lock(errors) errors.AppendLine(e.Data); };
                pr.BeginOutputReadLine(); pr.BeginErrorReadLine(); pr.StandardInput.Close();
                if (!pr.WaitForExit(20000)) { try {pr.Kill();} catch {} throw new EcxpException("Codex queue nie odpowiedział w ciągu 20 s; sprawdź kolejkę sesji przed ponowieniem."); }
                pr.WaitForExit(); kod = pr.ExitCode;
            }
            string text = output.ToString(), err = errors.ToString().Trim();
            var m = Regex.Match(text, @"Queued message ([0-9a-fA-F-]{36}) for thread ([0-9a-fA-F-]{36})");
            if(kod != 0 || !m.Success || !string.Equals(m.Groups[2].Value,watek,StringComparison.OrdinalIgnoreCase))
                throw new EcxpException("Nie potwierdzono dodania powiadomienia do kolejki Codexa (kod " + kod + "). " + (err.Length > 400 ? err.Substring(0,400) : err));
            // Samo dodanie do kolejki nie jest ACK zadania. Odbiór i wykonanie potwierdza później właściciel sesji przez ECXP.
            bool bladWznowienia = Regex.IsMatch(err, @"could not resume|failed to resume|unable to resume|cannot resume|start a local daemon",RegexOptions.IgnoreCase);
            return KolejkaPrefix + m.Groups[1].Value + "; oczekuję na odbiór ECXP" +
                (err.Length > 0 ? (bladWznowienia ? WznowieniePrefix : "; ostrzeżenie CLI: ") + (err.Length > 300 ? err.Substring(0,300) : err) : "");

        }
    }
}
