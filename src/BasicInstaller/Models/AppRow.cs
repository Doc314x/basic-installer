using System;
using System.Collections.Generic;
using BasicInstaller.Services;

namespace BasicInstaller.Models
{
    /// <summary>
    /// GUI-Sicht auf einen Katalog-Eintrag: eine Zeile im DataGridView plus die
    /// vom Nutzer waehlbaren Optionen (ausgewaehlt?, Pfad, Sprache,
    /// Lizenzschluessel). Haelt die Referenz auf den Original-AppEntry, damit
    /// Installer-Logik (SilentInstaller, LanguageResolution) direkt mit dem
    /// Katalog-Eintrag arbeiten kann.
    /// </summary>
    public class AppRow
    {
        public AppEntry Source { get; }

        public bool Selected { get; set; }
        public string Name => Source.name;
        public string Category => string.IsNullOrWhiteSpace(Source.category) ? "Sonstiges" : Source.category;
        public string InstallPath { get; set; }
        public string SelectedLanguage { get; set; }
        public string LicenseKey { get; set; }
        public string InstalledVersion { get; private set; }

        /// <summary>
        /// Best-effort "verfuegbare" Version (siehe LatestVersionChecker) -
        /// "–" bis zur ersten Pruefung (laeuft automatisch beim Programmstart,
        /// zusaetzlich jederzeit per "Status aktualisieren") bzw. wenn fuer
        /// diese App nicht ermittelbar.
        /// </summary>
        public string LatestVersion { get; set; } = "–";

        /// <summary>
        /// True nur, wenn BEIDE Versionen bekannt sind UND sie voneinander
        /// abweichen - "–" (nicht installiert bzw. nicht geprueft) zaehlt
        /// nicht als Update. Steuert sowohl die Auto-Auswahl nach einer
        /// Versionspruefung als auch, ob die "bereits installiert"-Regel beim
        /// Installieren trotzdem einen Lauf erzwingt statt zu überspringen.
        /// </summary>
        public bool HasKnownUpdate => InstalledVersion != "–" && LatestVersion != "–"
            && !SameVersion(InstalledVersion, LatestVersion)
            && IsNewer(LatestVersion, InstalledVersion);

        /// <summary>
        /// True, wenn "candidate" numerisch neuer ist als "current" (fehlende Stellen = 0).
        /// Eine nicht numerisch lesbare Angabe zaehlt vorsichtig als "anders = Update", wie
        /// bisher. Verhindert, dass eine NEUERE installierte Version (z.B. Acrobat 21996 gegen
        /// die von Adobes API genannte 21931) als "Update verfuegbar" erscheint.
        /// </summary>
        private static bool IsNewer(string candidate, string current)
        {
            var pc = (candidate ?? "").Trim().TrimStart('v', 'V').Split('.');
            var pu = (current ?? "").Trim().TrimStart('v', 'V').Split('.');
            for (var i = 0; i < Math.Max(pc.Length, pu.Length); i++)
            {
                long x = 0, y = 0;
                if (i < pc.Length && !long.TryParse(pc[i], out x)) return true;
                if (i < pu.Length && !long.TryParse(pu[i], out y)) return true;
                if (x != y) return x > y;
            }
            return false;
        }

        /// <summary>
        /// Gleichheit zweier Versionsangaben trotz unterschiedlicher Schreibweise der
        /// Quellen - sonst gaebe es dauerhaft ein falsches "Update verfuegbar":
        /// WinRAR meldet in der Registry "7.23", die Download-Seite liefert "723";
        /// 7-Zip "26.04" vs. Dateiname "2604"; LibreOffice "26.8.1.2" vs. "26.8.1"
        /// (Praefix der Teile gilt als gleich).
        /// </summary>
        /// <summary>"7.23.0" -> "7.23" (WinRAR meldet in der Registry eine Stelle mehr als die Download-Seite).</summary>
        private static string TrimTrailingZeroParts(string version)
        {
            while (version.EndsWith(".0", StringComparison.Ordinal))
                version = version.Substring(0, version.Length - 2);
            return version;
        }

        public static bool SameVersion(string a, string b)
        {
            a = TrimTrailingZeroParts((a ?? "").Trim().TrimStart('v', 'V'));
            b = TrimTrailingZeroParts((b ?? "").Trim().TrimStart('v', 'V'));
            if (a == b) return true;
            if (a.Replace(".", "") == b.Replace(".", "")) return true;

            var pa = a.Split('.');
            var pb = b.Split('.');
            var n = Math.Min(pa.Length, pb.Length);
            if (n < 2) return false; // "3" als Praefix von "3.0.24" waere zu grosszuegig
            for (var i = 0; i < n; i++)
                if (pa[i] != pb[i]) return false;
            return true;
        }

        // Bei "portable"-Apps ist InstallPath kein Installations-, sondern ein
        // Kopierziel (siehe SilentInstaller.RunPortable) - deshalb auch ohne
        // path_arg_template editierbar, nicht nur wenn der Installer einen
        // Pfad-Switch unterstuetzt.
        public bool PathEditable => !string.IsNullOrWhiteSpace(Source.path_arg_template)
            || Source.installer_type == "portable";
        public bool LanguageSelectable => Source.languages != null && Source.languages.Count > 0;

        // Zwei unabhaengige Lizenzmechanismen (siehe AppEntry) - fuer die GUI
        // zaehlt nur, ob ueberhaupt einer von beiden konfiguriert ist.
        public bool LicenseKeyEditable =>
            Source.license_type == "cli_arg" && !string.IsNullOrWhiteSpace(Source.license_key_arg_template)
            || Source.license_type == "key_file" && !string.IsNullOrWhiteSpace(Source.license_file_name);

        public bool HasOptions => Source.options != null && Source.options.Length > 0;

        /// <summary>
        /// Checkbox-Zustand je bool-Options-"key" (siehe AppEntry.AppOption).
        /// Wird in OptionsDialog bearbeitet und in config.json persistiert - im
        /// Gegensatz zu LicenseKey, das bewusst NIE in config.json landet.
        /// </summary>
        public Dictionary<string, bool> Options { get; } = new Dictionary<string, bool>();

        /// <summary>
        /// Gewaehlter Wert je "choice"-Options-"key" (z.B. Architektur x86/x64
        /// bei Office 365). Getrennt von Options, weil der Wert hier ein
        /// beliebiger String ist, keine Checkbox.
        /// </summary>
        public Dictionary<string, string> ChoiceOptions { get; } = new Dictionary<string, string>();

        public string[] AvailableLanguages => LanguageResolution.AvailableLanguages(Source);

        public AppRow(AppEntry source)
        {
            Source = source;
            if (!string.IsNullOrWhiteSpace(source.default_install_path))
                InstallPath = Environment.ExpandEnvironmentVariables(source.default_install_path);
            else if (source.installer_type == "portable")
                // Portable Apps ohne explizit gesetzten Zielordner landen in
                // Downloads (z.B. ShutUp10) - hier schon im Konstruktor
                // aufgeloest, damit die Tabelle direkt den echten Pfad zeigt
                // statt eines leeren Felds.
                InstallPath = KnownFolders.GetDownloadsFolder();
            else
                InstallPath = "";
            SelectedLanguage = LanguageResolution.DefaultLanguage(source);
            LicenseKey = "";
            InstalledVersion = InstalledAppDetector.FindInstalledVersion(source.detect_display_name_contains) ?? "–";

            if (source.options != null)
            {
                foreach (var option in source.options)
                {
                    if (option.type == "choice")
                        ChoiceOptions[option.key] = option.default_choice;
                    else
                        Options[option.key] = option.default_value;
                }
            }
        }

        /// <summary>
        /// Fuehrt Options und ChoiceOptions zu einer einzigen String-Ersetzung
        /// zusammen (bool -> "1"/"0", choice -> gewaehlter Wert woertlich) -
        /// genau das Format, das SilentInstaller.WriteOptionsIni fuer die
        /// {key}-Platzhalter in options_ini_template braucht.
        /// </summary>
        public Dictionary<string, string> BuildOptionSubstitutions()
        {
            var result = new Dictionary<string, string>();
            foreach (var kv in Options)
                result[kv.Key] = kv.Value ? "1" : "0";
            foreach (var kv in ChoiceOptions)
                result[kv.Key] = kv.Value;
            return result;
        }

        /// <summary>
        /// Erneut gegen die Registry pruefen (nach einer Installation oder per
        /// manuellem "Status aktualisieren"-Button in MainForm).
        /// </summary>
        public void RefreshInstalledVersion()
        {
            InstalledVersion = InstalledAppDetector.FindInstalledVersion(Source.detect_display_name_contains) ?? "–";
        }
    }
}
