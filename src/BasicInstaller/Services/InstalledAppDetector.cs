using System;
using Microsoft.Win32;

namespace BasicInstaller.Services
{
    /// <summary>
    /// Erkennt bereits installierte Programme ueber die Windows-Uninstall-
    /// Registry (gleicher Mechanismus, den auch "Programme und Features"
    /// nutzt) - der Patch-My-PC-artige "was ist installiert, welche Version"-
    /// Baustein. Matching erfolgt ueber einen Teilstring im DisplayName, weil
    /// DisplayName-Werte haeufig Versionsnummern/Zusaetze enthalten
    /// ("Mozilla Firefox (x64 de)") und so kein exakter Vergleich moeglich ist.
    ///
    /// WICHTIG: nutzt bewusst NICHT die statischen Registry.LocalMachine/
    /// Registry.CurrentUser-Properties. Die liefern nur die Registry-Ansicht
    /// der EIGENEN Prozess-Bitness: ein 32-Bit-Prozess sieht
    /// HKLM\SOFTWARE\...\Uninstall transparent umgeleitet auf
    /// HKLM\SOFTWARE\WOW6432Node\...\Uninstall und bekommt den nativen
    /// 64-Bit-Zweig NIE zu sehen (und umgekehrt) - ganz ohne Fehler, einfach
    /// leer. Ein echtes 64-Bit-7-Zip (heute der Normalfall) waere so fuer
    /// Basic Installer unsichtbar, je nachdem in welcher Bitness die exe
    /// gerade laeuft. RegistryKey.OpenBaseKey(..., RegistryView.Registry64/32)
    /// umgeht das: beide Ansichten werden explizit angefragt, unabhaengig
    /// davon, wie die eigene exe kompiliert/gestartet wurde.
    /// </summary>
    public static class InstalledAppDetector
    {
        private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

        private static readonly (RegistryHive Hive, RegistryView View)[] Targets =
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Registry64),
            (RegistryHive.CurrentUser, RegistryView.Registry32),
        };

        /// <summary>
        /// Liefert die DisplayVersion des ersten Treffers oder null, wenn
        /// nichts Passendes gefunden wurde (oder displayNameContains leer ist -
        /// dann ist fuer diese App keine Erkennung hinterlegt).
        /// </summary>
        public static string FindInstalledVersion(string displayNameContains)
        {
            if (string.IsNullOrWhiteSpace(displayNameContains))
                return null;

            foreach (var (hive, view) in Targets)
            {
                var version = ScanOneView(hive, view, displayNameContains);
                if (version != null)
                    return version;
            }

            return null;
        }

        // MSIX/AppX-Pakete (z.B. das neue Teams) tauchen in der klassischen Uninstall-Registry NICHT
        // auf. Ihr Paketname steht aber als Schluessel "<Name>_<Version>_<Arch>__<PublisherId>" im
        // AppX-Paketspeicher: fuer alle Benutzer unter HKLM ...\AppxAllUserStore\Applications, fuer
        // den aktuellen Benutzer unter HKCU ...\AppModel\Repository\Packages. Ohne PowerShell/WinRT.
        private static readonly (RegistryHive Hive, string Path)[] AppxTargets =
        {
            (RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Appx\AppxAllUserStore\Applications"),
            (RegistryHive.CurrentUser, @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages"),
        };

        /// <summary>
        /// Liefert die Version eines installierten MSIX-Pakets (z.B. "MSTeams" -> "26260.1701.5139.3736")
        /// oder null. packageName ist der Paketname vor dem ersten Unterstrich.
        /// </summary>
        public static string FindInstalledAppxVersion(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                return null;

            var prefix = packageName + "_";
            foreach (var (hive, path) in AppxTargets)
            {
                RegistryKey baseKey = null;
                RegistryKey key = null;
                try
                {
                    baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                    key = baseKey.OpenSubKey(path);
                    if (key == null) continue;

                    foreach (var name in key.GetSubKeyNames())
                    {
                        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                        var parts = name.Split('_');
                        if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1]))
                            return parts[1];
                    }
                }
                catch (Exception)
                {
                    // wie bei der Uninstall-Erkennung: Zugriffsfehler = "nicht gefunden"
                }
                finally
                {
                    key?.Dispose();
                    baseKey?.Dispose();
                }
            }

            return null;
        }

        private static string ScanOneView(RegistryHive hive, RegistryView view, string displayNameContains)
        {
            RegistryKey baseKey = null;
            RegistryKey uninstallKey = null;
            try
            {
                baseKey = RegistryKey.OpenBaseKey(hive, view);
                uninstallKey = baseKey.OpenSubKey(UninstallPath);
                if (uninstallKey == null)
                    return null;

                foreach (var subKeyName in uninstallKey.GetSubKeyNames())
                {
                    using (var subKey = uninstallKey.OpenSubKey(subKeyName))
                    {
                        var displayName = subKey?.GetValue("DisplayName") as string;
                        if (displayName == null) continue;
                        if (displayName.IndexOf(displayNameContains, StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        var version = subKey.GetValue("DisplayVersion") as string;
                        if (!string.IsNullOrWhiteSpace(version))
                            return version;
                    }
                }

                return null;
            }
            catch (Exception)
            {
                // Manche Hive/View-Kombinationen koennen in seltenen Faellen
                // (Berechtigungen, Windows-Version) einen Zugriffsfehler werfen
                // statt schlicht nichts zu finden - defensiv als "nicht
                // gefunden" werten statt die komplette Erkennung mit einer
                // Exception abzubrechen.
                return null;
            }
            finally
            {
                uninstallKey?.Dispose();
                baseKey?.Dispose();
            }
        }
    }
}
