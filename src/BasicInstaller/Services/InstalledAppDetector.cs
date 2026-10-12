using System;
using System.Collections.Generic;
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
    /// Scannt HKLM (64-bit nativ + WOW6432Node fuer 32-bit-Apps) UND HKCU,
    /// weil viele dieser Installer (Firefox, Brave, VLC-Stub teils) je nach
    /// Aufruf per-Nutzer statt maschinenweit installieren koennen.
    /// </summary>
    public static class InstalledAppDetector
    {
        private static readonly string[] UninstallRegistryPaths =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
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

            foreach (var version in ScanHive(Registry.LocalMachine, displayNameContains))
                return version;
            foreach (var version in ScanHive(Registry.CurrentUser, displayNameContains))
                return version;
            return null;
        }

        private static IEnumerable<string> ScanHive(RegistryKey hive, string displayNameContains)
        {
            foreach (var path in UninstallRegistryPaths)
            {
                RegistryKey uninstallKey = null;
                try
                {
                    uninstallKey = hive.OpenSubKey(path);
                    if (uninstallKey == null) continue;

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
                                yield return version;
                        }
                    }
                }
                finally
                {
                    uninstallKey?.Dispose();
                }
            }
        }
    }
}
