using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace BasicInstaller.Services
{
    /// <summary>
    /// Benannte Auswahl-Profile (z.B. "Kundenstandard", "Technikerstandard") -
    /// jeweils ein vollstaendiger Snapshot dessen, was auch config.json
    /// speichert (Auswahl, Pfade, Sprache, Optionen). Liegt bewusst in einer
    /// eigenen Datei statt in config.json, damit "zuletzt benutzter Zustand"
    /// und "gespeicherte Vorlagen" nicht vermischt werden.
    ///
    /// Enthaelt wie config.json NIE Lizenzschluessel/-dateien - UserConfig hat
    /// dafuer schlicht kein Feld, das gilt hier automatisch mit.
    /// </summary>
    public static class ProfileStore
    {
        private static string ProfilesPath => Path.Combine(ConfigStore.AppDataDirectory, "profiles.json");

        public static Dictionary<string, UserConfig> LoadAll()
        {
            try
            {
                if (!File.Exists(ProfilesPath))
                    return new Dictionary<string, UserConfig>();

                var json = File.ReadAllText(ProfilesPath);
                var serializer = new JavaScriptSerializer();
                return serializer.Deserialize<Dictionary<string, UserConfig>>(json) ?? new Dictionary<string, UserConfig>();
            }
            catch
            {
                // Eine beschaedigte Profildatei darf den Start nicht verhindern.
                return new Dictionary<string, UserConfig>();
            }
        }

        public static void Save(string name, UserConfig config)
        {
            var all = LoadAll();
            all[name] = config;
            WriteAll(all);
        }

        public static void Delete(string name)
        {
            var all = LoadAll();
            if (all.Remove(name))
                WriteAll(all);
        }

        private static void WriteAll(Dictionary<string, UserConfig> all)
        {
            Directory.CreateDirectory(ConfigStore.AppDataDirectory);
            var serializer = new JavaScriptSerializer();
            File.WriteAllText(ProfilesPath, serializer.Serialize(all));
        }
    }
}
