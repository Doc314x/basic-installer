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

        public bool PathEditable => !string.IsNullOrWhiteSpace(Source.path_arg_template);
        public bool LanguageSelectable => Source.languages != null && Source.languages.Count > 0;

        // Zwei unabhaengige Lizenzmechanismen (siehe AppEntry) - fuer die GUI
        // zaehlt nur, ob ueberhaupt einer von beiden konfiguriert ist.
        public bool LicenseKeyEditable =>
            Source.license_type == "cli_arg" && !string.IsNullOrWhiteSpace(Source.license_key_arg_template)
            || Source.license_type == "key_file" && !string.IsNullOrWhiteSpace(Source.license_file_name);

        public bool HasOptions => Source.options != null && Source.options.Length > 0;

        /// <summary>
        /// Checkbox-Zustand je Options-"key" (siehe AppEntry.AppOption). Wird in
        /// OptionsDialog bearbeitet und in config.json persistiert - im
        /// Gegensatz zu LicenseKey, das bewusst NIE in config.json landet.
        /// </summary>
        public Dictionary<string, bool> Options { get; } = new Dictionary<string, bool>();

        public string[] AvailableLanguages => LanguageResolution.AvailableLanguages(Source);

        public AppRow(AppEntry source)
        {
            Source = source;
            InstallPath = string.IsNullOrWhiteSpace(source.default_install_path)
                ? ""
                : Environment.ExpandEnvironmentVariables(source.default_install_path);
            SelectedLanguage = LanguageResolution.DefaultLanguage(source);
            LicenseKey = "";
            InstalledVersion = InstalledAppDetector.FindInstalledVersion(source.detect_display_name_contains) ?? "–";

            if (source.options != null)
                foreach (var option in source.options)
                    Options[option.key] = option.default_value;
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
