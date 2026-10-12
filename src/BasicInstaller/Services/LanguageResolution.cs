using System;
using System.Linq;
using BasicInstaller.Models;

namespace BasicInstaller.Services
{
    /// <summary>
    /// Loest die optionale "languages"-Zuordnung eines Katalog-Eintrags auf.
    /// Eine App ohne "languages" gilt als nicht sprachlich steuerbar (GUI zeigt
    /// dann nur "Standard (Systemsprache)") - siehe catalog/apps.json fuer
    /// Beispiele beider Faelle.
    /// </summary>
    public static class LanguageResolution
    {
        public const string SystemDefaultCode = "Standard";

        public static string[] AvailableLanguages(AppEntry app)
        {
            if (app.languages == null || app.languages.Count == 0)
                return new[] { SystemDefaultCode };
            return app.languages.Keys.ToArray();
        }

        public static string DefaultLanguage(AppEntry app)
        {
            if (app.languages == null || app.languages.Count == 0)
                return SystemDefaultCode;
            if (!string.IsNullOrWhiteSpace(app.default_language) && app.languages.ContainsKey(app.default_language))
                return app.default_language;
            return app.languages.Keys.First();
        }

        public static string ResolveInstallerUrl(AppEntry app, string languageCode)
        {
            if (app.languages != null
                && languageCode != null
                && app.languages.TryGetValue(languageCode, out var variant)
                && !string.IsNullOrWhiteSpace(variant.installer_url))
                return variant.installer_url;

            return app.installer_url;
        }

        public static string[] ResolveExtraArgs(AppEntry app, string languageCode)
        {
            if (app.languages != null
                && languageCode != null
                && app.languages.TryGetValue(languageCode, out var variant)
                && variant.extra_args != null)
                return variant.extra_args;

            return Array.Empty<string>();
        }
    }
}
