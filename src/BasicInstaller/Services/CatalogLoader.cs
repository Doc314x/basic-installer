using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using BasicInstaller.Models;

namespace BasicInstaller.Services
{
    /// <summary>
    /// Laedt den mitgelieferten Katalog (catalog/apps.json neben der exe) und
    /// mischt optional einen benutzereigenen Katalog aus
    /// %APPDATA%\BasicInstaller\user_apps.json dazu - so koennen eigene oder
    /// angepasste Eintraege ueberleben, ohne die Installation zu veraendern.
    /// Eintraege werden ueber "id" gematcht: gleiche id ueberschreibt den
    /// mitgelieferten Eintrag vollstaendig, neue id wird angehaengt.
    /// </summary>
    public static class CatalogLoader
    {
        public static AppEntry[] Load(string baseDirectory)
        {
            var bundledPath = Path.Combine(baseDirectory, "catalog", "apps.json");
            var entries = LoadFile(bundledPath).ToList();

            var userPath = Path.Combine(ConfigStore.AppDataDirectory, "user_apps.json");
            foreach (var overrideEntry in LoadFile(userPath))
            {
                var existingIndex = entries.FindIndex(e => e.id == overrideEntry.id);
                if (existingIndex >= 0)
                    entries[existingIndex] = overrideEntry;
                else
                    entries.Add(overrideEntry);
            }

            return entries.ToArray();
        }

        private static AppEntry[] LoadFile(string path)
        {
            if (!File.Exists(path))
                return Array.Empty<AppEntry>();

            var json = File.ReadAllText(path);
            var serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var catalog = serializer.Deserialize<CatalogFile>(json);
            return catalog?.apps ?? Array.Empty<AppEntry>();
        }
    }
}
