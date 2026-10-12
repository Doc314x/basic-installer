using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace BasicInstaller.Services
{
    public class AppUserSettings
    {
        public bool selected { get; set; }
        public string install_path { get; set; }
        public string language { get; set; }
        public Dictionary<string, bool> options { get; set; }
        public Dictionary<string, string> choice_options { get; set; }

        // Bewusst KEIN Feld fuer Lizenzschluessel/-dateien (z.B. ESET, WinRAR):
        // Secrets landen nicht in einer Klartext-JSON-Datei unter %APPDATA% -
        // analog zur IMAP-Mover-Regel, Passwoerter nur im Windows Credential
        // Manager zu halten. Siehe WindowsCredentialStore fuer die optionale,
        // bewusst separate Persistenz dieser Werte.
    }

    public class UserConfig
    {
        public Dictionary<string, AppUserSettings> apps { get; set; } = new Dictionary<string, AppUserSettings>();
    }

    /// <summary>
    /// Persistiert Checkbox-Zustand, Pfad- und Sprachauswahl je App zwischen
    /// Programmstarts. Liegt unter %APPDATA%\BasicInstaller, getrennt vom
    /// mitgelieferten Katalog - ein Update der App ueberschreibt diese Datei
    /// nie.
    /// </summary>
    public static class ConfigStore
    {
        public static string AppDataDirectory { get; } =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BasicInstaller");

        private static string ConfigPath => Path.Combine(AppDataDirectory, "config.json");

        public static UserConfig Load()
        {
            try
            {
                if (!File.Exists(ConfigPath))
                    return new UserConfig();

                var json = File.ReadAllText(ConfigPath);
                var serializer = new JavaScriptSerializer();
                return serializer.Deserialize<UserConfig>(json) ?? new UserConfig();
            }
            catch
            {
                // Eine beschaedigte Config darf den Start nicht verhindern - lieber
                // mit leeren Einstellungen weitermachen als abzustuerzen.
                return new UserConfig();
            }
        }

        public static void Save(UserConfig config)
        {
            Directory.CreateDirectory(AppDataDirectory);
            var serializer = new JavaScriptSerializer();
            File.WriteAllText(ConfigPath, serializer.Serialize(config));
        }
    }
}
