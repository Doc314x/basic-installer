using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BasicInstaller.Models;

namespace BasicInstaller.Services
{
    /// <summary>
    /// Ermittelt die aktuelle Versionsnummer einer App zur Laufzeit, damit
    /// versionierte Download-URLs im Katalog nicht veralten. Ein Katalog-Eintrag
    /// setzt dafuer <c>version_url</c> (Seite/Verzeichnisliste/Redirect-Ziel) und
    /// <c>version_regex</c> (Gruppe 1 = Version) und nutzt in <c>installer_url</c>
    /// die Platzhalter <c>{version}</c> (z.B. "26.04") und <c>{version_nodots}</c>
    /// (z.B. "2604"). Trifft die Regex mehrfach (Verzeichnislisten), gewinnt die
    /// hoechste Version. Zuerst wird die Regex gegen die finale URL nach allen
    /// Redirects geprueft (z.B. github.com/.../releases/latest -> .../tag/26.04,
    /// ohne API und damit ohne Rate-Limit), erst danach gegen den Seitentext.
    /// </summary>
    public static class VersionResolver
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
        private static readonly ConcurrentDictionary<string, Task<string>> Cache =
            new ConcurrentDictionary<string, Task<string>>();

        public static bool HasVersionSource(AppEntry app) =>
            !string.IsNullOrWhiteSpace(app.version_url) && !string.IsNullOrWhiteSpace(app.version_regex);

        /// <summary>Verwirft alle zwischengespeicherten Ergebnisse ("Status aktualisieren").</summary>
        public static void ClearCache() => Cache.Clear();

        /// <summary>
        /// Liefert die aktuelle Version oder null. Mit allowFallback=true wird bei
        /// einem Fehlschlag <c>fallback_version</c> des Katalogs genommen (fuer den
        /// Download sinnvoll, fuer die "Verfuegbar"-Anzeige nicht: dort waere eine
        /// veraltete Fallback-Version eine falsche Angabe).
        /// </summary>
        public static async Task<string> ResolveAsync(AppEntry app, bool allowFallback)
        {
            string version = null;
            if (HasVersionSource(app))
            {
                var task = Cache.GetOrAdd(app.id, _ => FetchAsync(app.version_url, app.version_regex));
                version = await task.ConfigureAwait(false);
                if (version == null)
                {
                    // Fehlschlag nicht dauerhaft merken - der naechste Versuch darf neu anfragen.
                    Task<string> removed;
                    Cache.TryRemove(app.id, out removed);
                }
            }

            if (version == null && allowFallback && !string.IsNullOrWhiteSpace(app.fallback_version))
                return app.fallback_version;
            return version;
        }

        /// <summary>Ersetzt {version} und {version_nodots} in einer URL.</summary>
        public static string Apply(string url, string version)
        {
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(version)) return url;
            return url.Replace("{version_nodots}", version.Replace(".", ""))
                      .Replace("{version}", version);
        }

        public static bool ContainsPlaceholder(string url) =>
            url != null && url.IndexOf("{version", StringComparison.Ordinal) >= 0;

        private static async Task<string> FetchAsync(string url, string pattern)
        {
            try
            {
                var regex = new Regex(pattern, RegexOptions.CultureInvariant);
                using (var client = new HttpClient())
                {
                    client.Timeout = Timeout;
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 BasicInstaller-VersionCheck");

                    using (var response = await client
                        .GetAsync(url, HttpCompletionOption.ResponseHeadersRead)
                        .ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                            return null;

                        var finalUrl = response.RequestMessage?.RequestUri?.ToString();
                        var best = Best(regex, finalUrl);
                        if (best != null)
                            return best;

                        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return Best(regex, body);
                    }
                }
            }
            catch
            {
                // Rein informativ/Komfort - der Aufrufer faellt auf fallback_version zurueck
                // bzw. meldet "Version nicht ermittelbar".
                return null;
            }
        }

        private static string Best(Regex regex, string text)
        {
            if (string.IsNullOrEmpty(text)) return null;

            string best = null;
            foreach (Match m in regex.Matches(text))
            {
                if (!m.Success || m.Groups.Count < 2) continue;
                var candidate = m.Groups[1].Value;
                if (best == null || Compare(candidate, best) > 0)
                    best = candidate;
            }
            return best;
        }

        /// <summary>Numerischer Vergleich "26.8.1" gegen "26.2.6" (fehlende Stellen = 0).</summary>
        private static int Compare(string a, string b)
        {
            var pa = a.Split('.');
            var pb = b.Split('.');
            for (var i = 0; i < Math.Max(pa.Length, pb.Length); i++)
            {
                long x = 0, y = 0;
                if (i < pa.Length) long.TryParse(pa[i], out x);
                if (i < pb.Length) long.TryParse(pb[i], out y);
                if (x != y) return x.CompareTo(y);
            }
            return 0;
        }
    }
}
