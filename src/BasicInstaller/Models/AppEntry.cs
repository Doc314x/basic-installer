using System.Collections.Generic;

namespace BasicInstaller.Models
{
    /// <summary>
    /// Eine optionale Sprachvariante einer App. Ueberschreibt, falls gesetzt,
    /// die Installer-URL der App und/oder haengt zusaetzliche Silent-Switches an
    /// (z.B. VLC waehlt die Sprache ueber /L=&lt;LCID&gt;, Firefox/Acrobat ueber
    /// eine andere Installer-Datei je Locale).
    /// </summary>
    public class LanguageVariant
    {
        public string installer_url { get; set; }
        public string[] extra_args { get; set; }
    }

    /// <summary>
    /// Eine einzelne, vom Nutzer einstellbare Installationsoption. Der "key"
    /// ist der Platzhaltername, der in options_ini_template ersetzt wird.
    ///
    /// type == null oder "bool" (Default): einfache Checkbox, z.B. "In
    /// Explorer-Kontextmenü integrieren" bei WinRAR - {key} wird durch "1"/"0"
    /// ersetzt.
    ///
    /// type == "choice": Auswahl zwischen mehreren festen Werten (z.B.
    /// Architektur x86/x64 bei Office 365) - {key} wird durch den gewählten
    /// Wert aus choice_values ersetzt (nicht durch "1"/"0"). choice_labels muss
    /// gleich viele Einträge wie choice_values haben (gleiche Reihenfolge,
    /// fürs Dropdown/die Radiobuttons in OptionsDialog); default_choice muss
    /// einer der choice_values sein.
    /// </summary>
    public class AppOption
    {
        public string key { get; set; }
        public string label { get; set; }
        public bool default_value { get; set; }
        public string type { get; set; }
        public string[] choice_values { get; set; }
        public string[] choice_labels { get; set; }
        public string default_choice { get; set; }
    }

    /// <summary>
    /// Ein Katalog-Eintrag = ein installierbares Programm. Entspricht 1:1 einem
    /// Objekt in catalog/apps.json. Siehe README.md "Katalog erweitern".
    /// </summary>
    public class AppEntry
    {
        public string id { get; set; }
        public string name { get; set; }
        public string category { get; set; }
        public string homepage { get; set; }
        public string installer_url { get; set; }
        public string installer_type { get; set; }
        public string[] silent_args { get; set; }
        public string default_install_path { get; set; }
        public string path_arg_template { get; set; }

        // Lizenzierung: zwei Mechanismen, je nachdem was der Hersteller-Installer
        // tatsaechlich unterstuetzt (siehe AppRow.LicenseKeyEditable):
        //   "cli_arg"   -> license_key_arg_template mit {key}-Platzhalter wird als
        //                  zusaetzlicher Silent-Switch angehaengt (z.B. ESET).
        //   "key_file"  -> license_file_name (z.B. "rarreg.key") wird NACH der
        //                  Installation mit dem eingegebenen Inhalt im
        //                  Zielverzeichnis angelegt (z.B. WinRAR-Volumenlizenz).
        // null = kein Lizenzmechanismus hinterlegt, Spalte bleibt gesperrt.
        public string license_type { get; set; }
        public string license_key_arg_template { get; set; }
        public string license_file_name { get; set; }

        // Installationsoptionen (Checkboxen), optional. Wenn gesetzt, wird vor
        // dem Silent-Install eine Datei options_ini_filename neben den
        // Installer gelegt, deren Inhalt aus options_ini_template entsteht
        // (jedes {key} aus "options" wird durch "1"/"0" ersetzt). Das macht die
        // tatsaechliche INI-Struktur komplett im Katalog editierbar, statt im
        // Code - wichtig, weil Hersteller ihr INI-Format jederzeit aendern
        // koennen und man das dann nicht neu bauen/deployen muss.
        public AppOption[] options { get; set; }
        public string options_ini_template { get; set; }
        public string options_ini_filename { get; set; }

        public string detect_display_name_contains { get; set; }
        public string sha256 { get; set; }
        public string default_language { get; set; }
        public Dictionary<string, LanguageVariant> languages { get; set; }
        public string notes { get; set; }
    }

    public class CatalogFile
    {
        public AppEntry[] apps { get; set; }
    }
}
