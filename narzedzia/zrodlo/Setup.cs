// ECXP-Setup – instalator stanowiska ECXP: stos ECXP (usługa ecxpd), konsola operatora ECXP-MAILBOX,
// adapter MCP dla agentów Claude Code i ChatGPT/Codex oraz hook lustra czatu.
// Jeden plik: programy są zasobami tego exe. Instalacja dla bieżącego użytkownika, bez uprawnień administratora.
//
//   ECXP-Setup.exe                                   – kreator (PL, EN, FR, CS, DE; domyślnie język systemu)
//   ECXP-Setup.exe /cicho [/katalog <dir>] [/jezyk en] [/bez-rejestracji] [/bez-autostartu] [/bez-pulpitu] [/bez-uruchomienia]
//   ECXP-Setup.exe /odinstaluj [/cicho]               – usuwa programy, skróty i rejestracje; dane zostają
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("ECXP-Setup")]
[assembly: AssemblyDescription("Instalator ECXP: stos ecxpd, konsola ECXP-MAILBOX, adapter MCP")]
[assembly: AssemblyCompany("EvidSec")]
[assembly: AssemblyProduct("ECXP")]
[assembly: AssemblyCopyright("EvidSec")]
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace EcxpSetup
{
    static class L
    {
        public static readonly string[] Kody = { "pl", "en", "fr", "cs", "de" };
        public static readonly string[] Nazwy = { "Polski", "English", "Français", "Čeština", "Deutsch" };
        public static int I = 1;

        static readonly Dictionary<string, string[]> T = new Dictionary<string, string[]>
        {
            { "tytul", new[] { "Instalator ECXP", "ECXP Setup", "Installation d’ECXP", "Instalace ECXP", "ECXP-Installation" } },
            { "witaj_n", new[] { "Witaj w instalatorze ECXP", "Welcome to ECXP Setup", "Bienvenue dans l’installation d’ECXP", "Vítejte v instalaci ECXP", "Willkommen bei der ECXP-Installation" } },
            { "witaj_t", new[] {
                "Instalator umieści na tym komputerze stos ECXP (usługę ecxpd), konsolę operatora ECXP-MAILBOX i adapter MCP dla agentów Claude Code i ChatGPT/Codex. Wszystko instaluje się dla bieżącego użytkownika, bez uprawnień administratora.",
                "This setup installs the ECXP stack (the ecxpd service), the ECXP-MAILBOX operator console and the MCP adapter for Claude Code and ChatGPT/Codex agents on this computer. Everything is installed for the current user, without administrator rights.",
                "Ce programme installe sur cet ordinateur la pile ECXP (le service ecxpd), la console opérateur ECXP-MAILBOX et l’adaptateur MCP pour les agents Claude Code et ChatGPT/Codex. Tout est installé pour l’utilisateur actuel, sans droits d’administrateur.",
                "Instalátor na tento počítač nainstaluje zásobník ECXP (službu ecxpd), konzoli operátora ECXP-MAILBOX a adaptér MCP pro agenty Claude Code a ChatGPT/Codex. Vše se instaluje pro aktuálního uživatele, bez oprávnění správce.",
                "Dieses Setup installiert auf diesem Computer den ECXP-Stack (den Dienst ecxpd), die Operator-Konsole ECXP-MAILBOX und den MCP-Adapter für Claude Code- und ChatGPT/Codex-Agenten. Alles wird für den aktuellen Benutzer installiert, ohne Administratorrechte." } },
            { "wymagania", new[] {
                "Wymagania: Windows 10 lub 11 (.NET Framework 4.8 jest w systemie). Agenci w tle potrzebują Claude Code CLI albo Codex CLI – można je doinstalować później z konsoli.",
                "Requirements: Windows 10 or 11 (.NET Framework 4.8 is built in). Background agents need Claude Code CLI or Codex CLI – you can add them later from the console.",
                "Configuration requise : Windows 10 ou 11 (.NET Framework 4.8 est intégré). Les agents en arrière-plan ont besoin de Claude Code CLI ou de Codex CLI – vous pourrez les ajouter plus tard depuis la console.",
                "Požadavky: Windows 10 nebo 11 (.NET Framework 4.8 je součástí systému). Agenti na pozadí potřebují Claude Code CLI nebo Codex CLI – lze je doinstalovat později z konzole.",
                "Voraussetzungen: Windows 10 oder 11 (.NET Framework 4.8 ist enthalten). Hintergrund-Agenten brauchen Claude Code CLI oder Codex CLI – sie lassen sich später aus der Konsole nachinstallieren." } },
            { "jezyk", new[] { "Język:", "Language:", "Langue :", "Jazyk:", "Sprache:" } },
            { "opcje_n", new[] { "Miejsce i opcje", "Location and options", "Emplacement et options", "Umístění a volby", "Speicherort und Optionen" } },
            { "katalog", new[] { "Katalog instalacji (programy i skrzynka ECXP):", "Install folder (programs and the ECXP mailbox):", "Dossier d’installation (programmes et boîte ECXP) :", "Instalační složka (programy a schránka ECXP):", "Installationsordner (Programme und ECXP-Postfach):" } },
            { "przegladaj", new[] { "Przeglądaj…", "Browse…", "Parcourir…", "Procházet…", "Durchsuchen…" } },
            { "o_rej", new[] { "Zarejestruj ECXP w Claude Code i Codex (serwer MCP, lustro czatu)", "Register ECXP in Claude Code and Codex (MCP server, chat mirror)", "Enregistrer ECXP dans Claude Code et Codex (serveur MCP, miroir du chat)", "Zaregistrovat ECXP v Claude Code a Codexu (server MCP, zrcadlo chatu)", "ECXP in Claude Code und Codex registrieren (MCP-Server, Chat-Spiegel)" } },
            { "o_auto", new[] { "Uruchamiaj usługę i konsolę przy logowaniu do Windows", "Start the service and the console when signing in to Windows", "Lancer le service et la console à l’ouverture de session Windows", "Spouštět službu a konzoli při přihlášení do Windows", "Dienst und Konsole bei der Windows-Anmeldung starten" } },
            { "o_pulpit", new[] { "Skrót na pulpicie", "Desktop shortcut", "Raccourci sur le bureau", "Zástupce na ploše", "Desktop-Verknüpfung" } },
            { "o_start", new[] { "Uruchom ECXP-MAILBOX po instalacji", "Launch ECXP-MAILBOX after setup", "Lancer ECXP-MAILBOX après l’installation", "Spustit ECXP-MAILBOX po instalaci", "ECXP-MAILBOX nach der Installation starten" } },
            { "wykryto", new[] { "Na tym komputerze:", "On this computer:", "Sur cet ordinateur :", "Na tomto počítači:", "Auf diesem Computer:" } },
            { "jest", new[] { "jest", "found", "présent", "nalezeno", "vorhanden" } },
            { "brak", new[] { "brak", "not found", "absent", "nenalezeno", "nicht vorhanden" } },
            { "codex_konf", new[] { "konfiguracja Codex", "Codex configuration", "configuration Codex", "konfigurace Codexu", "Codex-Konfiguration" } },
            { "aktualizacja", new[] { "W tym katalogu jest już ECXP – zostanie zaktualizowany, dane zostają.", "ECXP is already in this folder – it will be updated, data is kept.", "ECXP est déjà présent dans ce dossier – il sera mis à jour, les données sont conservées.", "V této složce už ECXP je – bude aktualizován, data zůstanou.", "ECXP ist in diesem Ordner bereits vorhanden – es wird aktualisiert, die Daten bleiben erhalten." } },
            { "instaluj", new[] { "Instaluj", "Install", "Installer", "Instalovat", "Installieren" } },
            { "dalej", new[] { "Dalej >", "Next >", "Suivant >", "Další >", "Weiter >" } },
            { "wstecz", new[] { "< Wstecz", "< Back", "< Précédent", "< Zpět", "< Zurück" } },
            { "anuluj", new[] { "Anuluj", "Cancel", "Annuler", "Zrušit", "Abbrechen" } },
            { "zakoncz", new[] { "Zakończ", "Finish", "Terminer", "Dokončit", "Fertig stellen" } },
            { "postep_n", new[] { "Instalacja…", "Installing…", "Installation…", "Instalace…", "Installation…" } },
            { "gotowe_n", new[] { "ECXP jest zainstalowany", "ECXP is installed", "ECXP est installé", "ECXP je nainstalován", "ECXP ist installiert" } },
            { "gotowe_t", new[] {
                "Konsola ECXP-MAILBOX sama uruchamia stos ECXP. Ikona ECXP pojawi się przy zegarku. Jeśli Codex był otwarty, uruchom go ponownie, żeby wczytał serwer MCP ecxp.",
                "The ECXP-MAILBOX console starts the ECXP stack by itself. The ECXP icon appears next to the clock. If Codex was open, restart it so that it loads the ecxp MCP server.",
                "La console ECXP-MAILBOX démarre elle-même la pile ECXP. L’icône ECXP apparaît près de l’horloge. Si Codex était ouvert, redémarrez-le pour qu’il charge le serveur MCP ecxp.",
                "Konzole ECXP-MAILBOX sama spouští zásobník ECXP. Ikona ECXP se objeví u hodin. Pokud byl Codex otevřený, spusťte ho znovu, aby načetl server MCP ecxp.",
                "Die Konsole ECXP-MAILBOX startet den ECXP-Stack selbst. Das ECXP-Symbol erscheint neben der Uhr. Falls Codex geöffnet war, starten Sie es neu, damit es den MCP-Server ecxp lädt." } },
            { "blad_n", new[] { "Instalacja nie powiodła się", "Setup failed", "L’installation a échoué", "Instalace selhala", "Installation fehlgeschlagen" } },
            { "k_zatrzymaj", new[] { "Zatrzymywanie działającej konsoli i stosu", "Stopping the running console and stack", "Arrêt de la console et de la pile en cours", "Zastavování běžící konzole a zásobníku", "Laufende Konsole und Stack werden beendet" } },
            { "k_pliki", new[] { "Kopiowanie programów", "Copying programs", "Copie des programmes", "Kopírování programů", "Programme werden kopiert" } },
            { "k_jezyk", new[] { "Ustawienie języka konsoli", "Setting the console language", "Réglage de la langue de la console", "Nastavení jazyka konzole", "Sprache der Konsole wird eingestellt" } },
            { "k_rej", new[] { "Rejestracja w Claude Code i Codex", "Registering in Claude Code and Codex", "Enregistrement dans Claude Code et Codex", "Registrace v Claude Code a Codexu", "Registrierung in Claude Code und Codex" } },
            { "k_skroty", new[] { "Skróty i autostart", "Shortcuts and autostart", "Raccourcis et démarrage automatique", "Zástupci a automatické spuštění", "Verknüpfungen und Autostart" } },
            { "k_apl", new[] { "Wpis w Aplikacjach Windows (odinstalowanie)", "Entry in Windows Apps (uninstall)", "Entrée dans les applications Windows (désinstallation)", "Položka v aplikacích Windows (odinstalace)", "Eintrag in Windows-Apps (Deinstallation)" } },
            { "k_start", new[] { "Uruchamianie ECXP-MAILBOX", "Launching ECXP-MAILBOX", "Lancement d’ECXP-MAILBOX", "Spouštění ECXP-MAILBOX", "ECXP-MAILBOX wird gestartet" } },
            { "ok", new[] { "gotowe", "done", "terminé", "hotovo", "erledigt" } },
            { "pominieto", new[] { "pominięto", "skipped", "ignoré", "přeskočeno", "übersprungen" } },
            { "raport", new[] { "Szczegóły rejestracji:", "Registration details:", "Détails de l’enregistrement :", "Podrobnosti registrace:", "Details der Registrierung:" } },
            { "odinst_pyt", new[] {
                "Odinstalować ECXP z tego komputera?\n\nProgramy, skróty i rejestracje w Claude Code i Codex zostaną usunięte. Wiadomości, dziennik i projekty zostają w katalogu:\n{0}",
                "Uninstall ECXP from this computer?\n\nPrograms, shortcuts and the registrations in Claude Code and Codex will be removed. Messages, the journal and projects stay in:\n{0}",
                "Désinstaller ECXP de cet ordinateur ?\n\nLes programmes, raccourcis et enregistrements dans Claude Code et Codex seront supprimés. Les messages, le journal et les projets restent dans :\n{0}",
                "Odinstalovat ECXP z tohoto počítače?\n\nProgramy, zástupci a registrace v Claude Code a Codexu budou odstraněny. Zprávy, deník a projekty zůstanou ve složce:\n{0}",
                "ECXP von diesem Computer deinstallieren?\n\nProgramme, Verknüpfungen und die Registrierungen in Claude Code und Codex werden entfernt. Nachrichten, Journal und Projekte bleiben in:\n{0}" } },
            { "odinst_ok", new[] { "ECXP odinstalowano. Dane zostały w:\n{0}", "ECXP has been uninstalled. Your data is still in:\n{0}", "ECXP a été désinstallé. Vos données restent dans :\n{0}", "ECXP byl odinstalován. Data zůstala ve složce:\n{0}", "ECXP wurde deinstalliert. Ihre Daten liegen weiterhin in:\n{0}" } },
            { "odinst_brak", new[] { "Nie znaleziono instalacji ECXP.", "No ECXP installation was found.", "Aucune installation d’ECXP trouvée.", "Instalace ECXP nebyla nalezena.", "Keine ECXP-Installation gefunden." } },
            { "katalog_zly", new[] { "Podaj katalog instalacji.", "Enter an install folder.", "Indiquez un dossier d’installation.", "Zadejte instalační složku.", "Geben Sie einen Installationsordner an." } },
            { "wersja", new[] { "Wersja", "Version", "Version", "Verze", "Version" } },
        };

        public static string S(string k) { string[] v; return T.TryGetValue(k, out v) ? v[I] : k; }
        public static string Kod { get { return Kody[I]; } }
        public static void Ustaw(string kod) { var i = Array.IndexOf(Kody, kod); if (i >= 0) I = i; }
        public static void ZSystemu() { Ustaw(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant()); }
    }

    static class Instalacja
    {
        public const string Wersja = "1.1";
        const string KluczApl = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ECXP";
        static readonly string[] Programy = { "ecxpd.exe", "ECXP-MAILBOX.exe", "ecxp-mcp.exe", "ECXP-MAILBOX.ico", "hook-claude.ps1" };
        static readonly string[] Logo = { "ecxp-logo-light.png", "ecxp-logo-dark.png" };

        public static string Domyslny { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ECXP"); } }

        public static string Zainstalowany()
        {
            using (var k = Registry.CurrentUser.OpenSubKey(KluczApl))
                return k == null ? "" : (k.GetValue("InstallLocation") as string ?? "");
        }

        public static bool JestW(string katalog) { return File.Exists(Path.Combine(katalog, "narzedzia", "ECXP-MAILBOX.exe")); }

        public static string Program(string nazwa)
        {
            var sciezki = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';').Where(x => x.Trim().Length > 0).ToList();
            sciezki.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm"));
            foreach (var d in sciezki)
                foreach (var ext in new[] { ".cmd", ".exe", ".bat" })
                    try { var p = Path.Combine(d.Trim(), nazwa + ext); if (File.Exists(p)) return p; } catch { }
            return "";
        }

        public static bool CodexKonf() { return File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.toml")); }

        static Stream Zasob(string n) { return Assembly.GetExecutingAssembly().GetManifestResourceStream(n); }

        // plik w użyciu (np. ecxp-mcp.exe uruchomiony przez agenta) przenosimy na bok – Windows pozwala zmienić nazwę działającego exe
        static void Zapisz(string zasob, string cel)
        {
            foreach (var stary in Directory.GetFiles(Path.GetDirectoryName(cel), Path.GetFileName(cel) + ".stary-*"))
                try { File.Delete(stary); } catch { }
            if (File.Exists(cel))
                try { File.Delete(cel); }
                catch { File.Move(cel, cel + ".stary-" + DateTime.Now.Ticks); }
            using (var z = Zasob(zasob))
            using (var f = File.Create(cel)) z.CopyTo(f);
        }

        static void Zatrzymaj(string katalog)
        {
            var narz = Path.Combine(katalog, "narzedzia");
            foreach (var n in new[] { "ECXP-MAILBOX", "ecxpd" })
                foreach (var p in Process.GetProcessesByName(n))
                    try
                    {
                        if (p.MainModule.FileName.StartsWith(narz, StringComparison.OrdinalIgnoreCase)) { p.Kill(); p.WaitForExit(5000); }
                    }
                    catch { }
        }

        // cel istniejącego skrótu (pusty, gdy go nie ma)
        static string CelSkrotu(string lnk)
        {
            if (!File.Exists(lnk)) return "";
            try
            {
                var sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
                var s = sh.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
                return (string)s.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, s, null) ?? "";
            }
            catch { return ""; }
        }

        static bool Nasz(string sciezka, string narz) { return sciezka.StartsWith(narz.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase); }

        // skrót tworzymy albo usuwamy – usuwamy tylko taki, który wskazuje na tę instalację (narz)
        static void Skrot(string lnk, string exe, string args, bool jest, string narz)
        {
            if (!jest) { try { if (File.Exists(lnk) && Nasz(CelSkrotu(lnk), narz)) File.Delete(lnk); } catch { } return; }
            Directory.CreateDirectory(Path.GetDirectoryName(lnk));
            var sh = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));
            var s = sh.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
            foreach (var kv in new[] { new[] { "TargetPath", exe }, new[] { "Arguments", args }, new[] { "WorkingDirectory", Path.GetDirectoryName(exe) },
                new[] { "IconLocation", Path.Combine(Path.GetDirectoryName(exe), "ECXP-MAILBOX.exe") + ",0" }, new[] { "Description", "ECXP-MAILBOX" } })
                s.GetType().InvokeMember(kv[0], BindingFlags.SetProperty, null, s, new object[] { kv[1] });
            s.GetType().InvokeMember("Save", BindingFlags.InvokeMethod, null, s, null);
        }

        static string Skroty(string kat) { return Path.Combine(Environment.GetFolderPath(kat == "pulpit" ? Environment.SpecialFolder.Desktop : kat == "autostart" ? Environment.SpecialFolder.Startup : Environment.SpecialFolder.Programs), kat == "menu" ? @"ECXP\ECXP-MAILBOX.lnk" : "ECXP-MAILBOX.lnk"); }

        static string Uruchom(string exe, string args, int ms)
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(exe) };
            using (var p = Process.Start(psi)) { p.WaitForExit(ms); return p.HasExited ? "" + p.ExitCode : "timeout"; }
        }

        // krok: (nazwa kroku, wynik) – kreator pokazuje je na liście
        public static string Wykonaj(string katalog, bool rejestruj, bool autostart, bool pulpit, bool uruchom, Action<string, string> krok)
        {
            katalog = Path.GetFullPath(katalog);
            var narz = Path.Combine(katalog, "narzedzia");
            var konsola = Path.Combine(narz, "ECXP-MAILBOX.exe");
            var raport = "";

            krok(L.S("k_zatrzymaj"), "…"); Zatrzymaj(katalog); krok(L.S("k_zatrzymaj"), L.S("ok"));

            krok(L.S("k_pliki"), "…");
            foreach (var d in new[] { katalog, narz, Path.Combine(katalog, "logo") }) Directory.CreateDirectory(d);
            foreach (var p in Programy) Zapisz(p, Path.Combine(narz, p));
            foreach (var p in Logo) Zapisz(p, Path.Combine(katalog, "logo", p));
            var ja = Application.ExecutablePath;
            var setup = Path.Combine(narz, "ECXP-Setup.exe");
            if (!string.Equals(Path.GetFullPath(ja), setup, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(setup)) try { File.Delete(setup); } catch { File.Move(setup, setup + ".stary-" + DateTime.Now.Ticks); }
                File.Copy(ja, setup);
            }
            krok(L.S("k_pliki"), L.S("ok"));

            krok(L.S("k_jezyk"), "…");
            var ust = Path.Combine(narz, "ustawienia-wlasciciel.txt");
            var linie = File.Exists(ust) ? File.ReadAllLines(ust).Where(x => !x.StartsWith("jezyk=")).ToList() : new List<string> { "motyw=jasny", "uslugi=pasek" };
            linie.Add("jezyk=" + L.Kod);
            File.WriteAllLines(ust, linie, new UTF8Encoding(false));
            krok(L.S("k_jezyk"), L.Nazwy[L.I]);

            if (rejestruj)
            {
                krok(L.S("k_rej"), "…");
                var kod = Uruchom(konsola, "/instaluj /cicho /jezyk " + L.Kod, 180000);
                try { raport = File.ReadAllText(Path.Combine(katalog, "stan", "instalacja.txt"), Encoding.UTF8); } catch { }
                krok(L.S("k_rej"), kod == "0" ? L.S("ok") : kod);
            }
            else krok(L.S("k_rej"), L.S("pominieto"));

            krok(L.S("k_skroty"), "…");
            Skrot(Skroty("pulpit"), konsola, "", pulpit, narz);
            Skrot(Skroty("autostart"), konsola, "", autostart, narz);
            Skrot(Skroty("menu"), konsola, "", true, narz);
            krok(L.S("k_skroty"), L.S("ok"));

            krok(L.S("k_apl"), "…");
            using (var k = Registry.CurrentUser.CreateSubKey(KluczApl))
            {
                k.SetValue("DisplayName", "ECXP-MAILBOX (ECXP " + Wersja + ")");
                k.SetValue("DisplayVersion", Wersja);
                k.SetValue("Publisher", "EvidSec");
                k.SetValue("URLInfoAbout", "https://evidsec.pl/ecxp/");
                k.SetValue("InstallLocation", katalog);
                k.SetValue("DisplayIcon", konsola + ",0");
                k.SetValue("UninstallString", "\"" + setup + "\" /odinstaluj");
                k.SetValue("QuietUninstallString", "\"" + setup + "\" /odinstaluj /cicho");
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                long kb = new DirectoryInfo(narz).GetFiles().Sum(f => f.Length) / 1024;
                k.SetValue("EstimatedSize", (int)kb, RegistryValueKind.DWord);
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            }
            krok(L.S("k_apl"), L.S("ok"));

            if (uruchom) { krok(L.S("k_start"), "…"); Process.Start(new ProcessStartInfo(konsola) { UseShellExecute = true, WorkingDirectory = narz }); krok(L.S("k_start"), L.S("ok")); }
            return raport;
        }

        static bool Zarejestrowany(string narz)
        {
            var dom = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var wzor = narz.TrimEnd('\\');
            foreach (var f in new[] { Path.Combine(dom, ".codex", "config.toml"), Path.Combine(dom, ".claude.json"), Path.Combine(dom, ".claude", "settings.json") })
                try
                {
                    if (!File.Exists(f)) continue;
                    var t = File.ReadAllText(f);
                    if (t.IndexOf(wzor, StringComparison.OrdinalIgnoreCase) >= 0 || t.IndexOf(wzor.Replace("\\", "\\\\"), StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
                catch { }
            return false;
        }

        public static int Odinstaluj(bool cicho)
        {
            var katalog = Zainstalowany();
            if (katalog.Length == 0) katalog = Path.GetDirectoryName(Path.GetDirectoryName(Application.ExecutablePath));
            var narz = Path.Combine(katalog, "narzedzia");
            if (!JestW(katalog)) { if (!cicho) MessageBox.Show(L.S("odinst_brak"), L.S("tytul")); return 1; }
            if (!cicho && MessageBox.Show(string.Format(L.S("odinst_pyt"), katalog), L.S("tytul"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return 2;

            Zatrzymaj(katalog);
            // rejestracje w Claude Code i Codex cofamy tylko wtedy, gdy wskazują na tę instalację
            if (Zarejestrowany(narz))
                try { Uruchom(Path.Combine(narz, "ECXP-MAILBOX.exe"), "/odinstaluj /cicho /jezyk " + L.Kod, 180000); } catch { }
            foreach (var k in new[] { "pulpit", "autostart", "menu" }) Skrot(Skroty(k), "", "", false, narz);
            try { Directory.Delete(Path.GetDirectoryName(Skroty("menu"))); } catch { }
            Registry.CurrentUser.DeleteSubKeyTree(KluczApl, false);

            // programy usuwamy, dane (wiadomości, dziennik, stan, projekty, ustawienia) zostają
            var doUsuniecia = new List<string>();
            foreach (var p in Programy.Concat(new[] { "ECXP-Setup.exe" }))
            {
                var f = Path.Combine(narz, p);
                foreach (var g in Directory.GetFiles(narz, p + ".stary-*")) doUsuniecia.Add(g);
                if (File.Exists(f)) doUsuniecia.Add(f);
            }
            var pozniej = new List<string>();
            foreach (var f in doUsuniecia) try { File.Delete(f); } catch { pozniej.Add(f); }
            if (pozniej.Count > 0)
            {
                // ten instalator i pliki w użyciu – usunięcie po zamknięciu procesu
                var cmd = "/d /c ping 127.0.0.1 -n 4 >nul & " + string.Join(" & ", pozniej.Select(f => "del /f /q \"" + f + "\""));
                Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), cmd) { UseShellExecute = false, CreateNoWindow = true });
            }
            if (!cicho) MessageBox.Show(string.Format(L.S("odinst_ok"), katalog), L.S("tytul"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }
    }

    class Kreator : Form
    {
        readonly Panel strona1 = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 12, 24, 8) };
        readonly Panel strona2 = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 12, 24, 8) };
        readonly Panel strona3 = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 12, 24, 8) };
        readonly Label naglowek = new Label { AutoSize = false, Dock = DockStyle.Top, Height = 34 };
        readonly Label lWitaj = new Label { AutoSize = false, Location = new Point(24, 50), Size = new Size(560, 96) };
        readonly Label lJezyk = new Label { AutoSize = true, Location = new Point(24, 162) };
        readonly ComboBox cbJezyk = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(130, 158), Width = 180 };
        readonly Label lWymagania = new Label { AutoSize = false, Location = new Point(24, 204), Size = new Size(560, 60), ForeColor = Color.DimGray };
        readonly Label lKatalog = new Label { AutoSize = true, Location = new Point(24, 50) };
        readonly TextBox tbKatalog = new TextBox { Location = new Point(24, 74), Width = 440 };
        readonly Button bPrzegladaj = new Button { Location = new Point(472, 72), Width = 112, Height = 28 };
        readonly Label lAktualizacja = new Label { AutoSize = false, Location = new Point(24, 102), Size = new Size(560, 20), ForeColor = Color.FromArgb(0, 102, 51) };
        readonly CheckBox chRej = new CheckBox { AutoSize = true, Location = new Point(24, 128), Checked = true };
        readonly CheckBox chAuto = new CheckBox { AutoSize = true, Location = new Point(24, 154), Checked = true };
        readonly CheckBox chPulpit = new CheckBox { AutoSize = true, Location = new Point(24, 180), Checked = true };
        readonly CheckBox chStart = new CheckBox { AutoSize = true, Location = new Point(24, 206), Checked = true };
        readonly Label lWykryto = new Label { AutoSize = false, Location = new Point(24, 238), Size = new Size(560, 44), ForeColor = Color.DimGray };
        readonly ListView lvKroki = new ListView { View = View.Details, Location = new Point(24, 48), Size = new Size(560, 150), HeaderStyle = ColumnHeaderStyle.None, FullRowSelect = true };
        readonly TextBox tbRaport = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Location = new Point(24, 204), Size = new Size(560, 74), BackColor = Color.White };
        readonly Button bWstecz = new Button { Width = 100, Height = 30 }, bDalej = new Button { Width = 110, Height = 30 }, bAnuluj = new Button { Width = 100, Height = 30 };
        int strona = 1;
        bool zakonczone;
        DateTime zmianaStrony = DateTime.MinValue;
        bool args_demo;   // podwójne kliknięcie „Dalej” nie może przeskoczyć strony opcji

        public Kreator() : this(0) { }

        // podglad > 0: pokaz strony kreatora bez instalacji (zrzuty do dokumentacji)
        public Kreator(int podglad)
        {
            args_demo = podglad > 0;
            Font = new Font("Segoe UI", 9.5f);
            ClientSize = new Size(620, 470);
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var baner = new Panel { Dock = DockStyle.Top, Height = 84, BackColor = Color.White };
            var logo = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Location = new Point(20, 14), Size = new Size(184, 56) };
            try { using (var z = Assembly.GetExecutingAssembly().GetManifestResourceStream("ecxp-logo-light.png")) logo.Image = Image.FromStream(z); } catch { }
            var lWersja = new Label { AutoSize = true, Location = new Point(220, 34), ForeColor = Color.DimGray, Text = "ECXP-MAILBOX · ECXP " + Instalacja.Wersja };
            baner.Controls.Add(logo); baner.Controls.Add(lWersja);
            var linia = new Panel { Dock = DockStyle.Top, Height = 3, BackColor = Color.FromArgb(155, 25, 42) };

            naglowek.Font = new Font("Segoe UI Semibold", 13f);
            var tresc = new Panel { Dock = DockStyle.Fill };
            foreach (var s in new[] { strona1, strona2, strona3 }) { tresc.Controls.Add(s); s.Visible = false; }
            strona1.Controls.AddRange(new Control[] { lWitaj, lJezyk, cbJezyk, lWymagania });
            strona2.Controls.AddRange(new Control[] { lKatalog, tbKatalog, bPrzegladaj, lAktualizacja, chRej, chAuto, chPulpit, chStart, lWykryto });
            lvKroki.Columns.Add("", 400); lvKroki.Columns.Add("", 130);
            strona3.Controls.AddRange(new Control[] { lvKroki, tbRaport });

            var dol = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = Color.FromArgb(244, 245, 247) };
            bAnuluj.Location = new Point(500, 13); bDalej.Location = new Point(382, 13); bWstecz.Location = new Point(276, 13);
            bDalej.BackColor = Color.FromArgb(255, 222, 0); bDalej.FlatStyle = FlatStyle.Flat; bDalej.Font = new Font(Font, FontStyle.Bold);
            dol.Controls.AddRange(new Control[] { bWstecz, bDalej, bAnuluj });

            Controls.Add(tresc); Controls.Add(naglowek); Controls.Add(linia); Controls.Add(baner); Controls.Add(dol);
            naglowek.Padding = new Padding(22, 8, 0, 0);
            naglowek.BringToFront(); tresc.BringToFront();

            foreach (var n in L.Nazwy) cbJezyk.Items.Add(n);
            cbJezyk.SelectedIndex = L.I;
            cbJezyk.SelectedIndexChanged += delegate { L.I = cbJezyk.SelectedIndex; Teksty(); };
            var zainst = Instalacja.Zainstalowany();
            tbKatalog.Text = zainst.Length > 0 ? zainst : Instalacja.Domyslny;
            tbKatalog.TextChanged += delegate { Teksty(); };
            bPrzegladaj.Click += delegate
            {
                using (var d = new FolderBrowserDialog { SelectedPath = tbKatalog.Text, ShowNewFolderButton = true })
                    if (d.ShowDialog(this) == DialogResult.OK) tbKatalog.Text = d.SelectedPath.EndsWith("ECXP", StringComparison.OrdinalIgnoreCase) ? d.SelectedPath : Path.Combine(d.SelectedPath, "ECXP");
            };
            bWstecz.Click += delegate { if (strona > 1) { strona--; Pokaz(); } };
            bAnuluj.Click += delegate { Close(); };
            bDalej.Click += delegate
            {
                if (zakonczone) { Close(); return; }
                if ((DateTime.Now - zmianaStrony).TotalMilliseconds < 700) return;
                if (strona == 1) { strona = 2; Pokaz(); return; }
                if (strona == 2)
                {
                    if (tbKatalog.Text.Trim().Length == 0) { MessageBox.Show(this, L.S("katalog_zly"), L.S("tytul")); return; }
                    strona = 3; Pokaz();
                    if (!args_demo) Instaluj();
                }
            };
            Teksty(); Pokaz();
            if (podglad >= 2) { strona = 2; tbKatalog.Text = Instalacja.Domyslny; Pokaz(); }
            if (podglad == 3)
            {
                strona = 3; zakonczone = true;
                foreach (var k in new[] { "k_zatrzymaj", "k_pliki", "k_jezyk", "k_rej", "k_skroty", "k_apl", "k_start" })
                {
                    var it = new ListViewItem(L.S(k)); it.SubItems.Add(k == "k_jezyk" ? L.Nazwy[L.I] : L.S("ok"));
                    it.ForeColor = Color.FromArgb(0, 102, 51); lvKroki.Items.Add(it);
                }
                tbRaport.Text = L.S("gotowe_t");
                Pokaz();
            }
            if (podglad > 0) bDalej.Click += delegate { };
        }

        void Teksty()
        {
            Text = L.S("tytul") + " – ECXP " + Instalacja.Wersja;
            lWitaj.Text = L.S("witaj_t"); lJezyk.Text = L.S("jezyk"); lWymagania.Text = L.S("wymagania");
            lKatalog.Text = L.S("katalog"); bPrzegladaj.Text = L.S("przegladaj");
            chRej.Text = L.S("o_rej"); chAuto.Text = L.S("o_auto"); chPulpit.Text = L.S("o_pulpit"); chStart.Text = L.S("o_start");
            lAktualizacja.Text = Instalacja.JestW(tbKatalog.Text.Trim()) ? L.S("aktualizacja") : "";
            lWykryto.Text = L.S("wykryto") + "  Claude Code CLI: " + (Instalacja.Program("claude").Length > 0 ? L.S("jest") : L.S("brak")) +
                            "  ·  Codex CLI: " + (Instalacja.Program("codex").Length > 0 ? L.S("jest") : L.S("brak")) +
                            "  ·  " + L.S("codex_konf") + ": " + (Instalacja.CodexKonf() ? L.S("jest") : L.S("brak"));
            bWstecz.Text = L.S("wstecz"); bAnuluj.Text = L.S("anuluj");
            bDalej.Text = zakonczone ? L.S("zakoncz") : strona == 2 ? L.S("instaluj") : L.S("dalej");
            naglowek.Text = strona == 1 ? L.S("witaj_n") : strona == 2 ? L.S("opcje_n") : zakonczone ? L.S("gotowe_n") : L.S("postep_n");
        }

        void Pokaz()
        {
            zmianaStrony = DateTime.Now;
            strona1.Visible = strona == 1; strona2.Visible = strona == 2; strona3.Visible = strona == 3;
            bWstecz.Enabled = strona == 2;
            bDalej.Enabled = strona < 3 || zakonczone;
            bAnuluj.Enabled = strona < 3;
            Teksty();
        }

        void Instaluj()
        {
            string katalog = tbKatalog.Text.Trim();
            bool rej = chRej.Checked, auto = chAuto.Checked, pulpit = chPulpit.Checked, start = chStart.Checked;
            var wiersze = new Dictionary<string, ListViewItem>();
            Action<string, string> krok = (n, w) => BeginInvoke((Action)(() =>
            {
                ListViewItem it;
                if (!wiersze.TryGetValue(n, out it)) { it = new ListViewItem(n); it.SubItems.Add(""); lvKroki.Items.Add(it); wiersze[n] = it; }
                it.SubItems[1].Text = w;
                it.ForeColor = w == "…" ? Color.DimGray : Color.FromArgb(0, 102, 51);
            }));
            new Thread(() =>
            {
                string raport = "", blad = null;
                try { raport = Instalacja.Wykonaj(katalog, rej, auto, pulpit, start, krok); }
                catch (Exception ex) { blad = ex.Message; }
                BeginInvoke((Action)(() =>
                {
                    zakonczone = true;
                    if (blad != null) { naglowek.Text = L.S("blad_n"); tbRaport.Text = blad; tbRaport.ForeColor = Color.FromArgb(180, 0, 0); }
                    else tbRaport.Text = L.S("gotowe_t") + (raport.Length > 0 ? "\r\n\r\n" + L.S("raport") + "\r\n" + raport.Replace("\r\n", "\n").Replace("\n", "\r\n") : "");
                    Pokaz();
                    if (blad != null) naglowek.Text = L.S("blad_n");
                }));
            }) { IsBackground = true }.Start();
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            L.ZSystemu();
            Func<string, string> arg = n => { var i = Array.IndexOf(args, n); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; };
            if (arg("/jezyk") != null) L.Ustaw(arg("/jezyk"));
            bool cicho = args.Contains("/cicho");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Contains("/odinstaluj")) return Instalacja.Odinstaluj(cicho);
            if (cicho)
            {
                var log = new StringBuilder();
                try
                {
                    var katalog = arg("/katalog") ?? (Instalacja.Zainstalowany().Length > 0 ? Instalacja.Zainstalowany() : Instalacja.Domyslny);
                    var r = Instalacja.Wykonaj(katalog, !args.Contains("/bez-rejestracji"), !args.Contains("/bez-autostartu"), !args.Contains("/bez-pulpitu"),
                        !args.Contains("/bez-uruchomienia"), (n, w) => { if (w != "…") log.AppendLine(n + ": " + w); });
                    log.AppendLine(r);
                    return 0;
                }
                catch (Exception ex) { log.AppendLine("BŁĄD: " + ex.Message); return 1; }
                finally { try { File.WriteAllText(Path.Combine(Path.GetTempPath(), "ECXP-Setup.log"), log.ToString(), new UTF8Encoding(false)); } catch { } }
            }
            int podglad; int.TryParse(arg("/demo") ?? "0", out podglad);
            Application.Run(new Kreator(podglad));
            return 0;
        }
    }
}
