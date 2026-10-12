using System;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BasicInstaller.Services
{
    /// <summary>
    /// Ermittelt best-effort die "verfuegbare" Version eines Katalog-Eintrags,
    /// indem die installer_url angefragt und die finale (nach allen
    /// Redirects aufgeloeste) URL nach einer Versionsnummer durchsucht wird -
    /// z.B. landet Firefox' Stub-URL letztlich bei
    /// ".../firefox/releases/157.0.1/win64/de/Firefox%20Setup%20157.0.1.exe".
    ///
    /// Funktioniert NUR fuer Apps, deren Redirect-Kette die Version ueberhaupt
    /// in der URL zeigt (bestaetigt fuer Firefox/Thunderbird/Zoom) - bei allen
    /// anderen (z.B. ESET, Office 365, Teams - deren Installer keine
    /// Versionsnummer in der URL tragen, oder Apps ohne installer_url wie
    /// IrfanView/WinRAR) liefert das schlicht null, was die GUI ehrlich als
    /// "–" statt als erfundene Angabe zeigt. Bewusst kein Download des
    /// eigentlichen Installers (HttpCompletionOption.ResponseHeadersRead,
    /// gleiche Technik wie in Downloader.cs).
    /// </summary>
    public static class LatestVersionChecker
    {
        private static readonly Regex VersionPattern = new Regex(@"\d+(?:\.\d+){1,3}", RegexOptions.Compiled);
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

        public static async Task<string> TryGetLatestVersionAsync(string installerUrl)
        {
            if (string.IsNullOrWhiteSpace(installerUrl))
                return null;

            try
            {
                using (var client = new HttpClient())
                {
                    client.Timeout = Timeout;
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 BasicInstaller-VersionCheck");

                    using (var response = await client
                        .GetAsync(installerUrl, HttpCompletionOption.ResponseHeadersRead)
                        .ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                            return null;

                        var finalUrl = response.RequestMessage?.RequestUri?.ToString() ?? installerUrl;
                        var match = VersionPattern.Match(WebUtility.UrlDecode(finalUrl));
                        return match.Success ? match.Value : null;
                    }
                }
            }
            catch
            {
                // Nur eine informative Zusatzspalte - kein Grund, "Status
                // aktualisieren" bei einem einzelnen langsamen/nicht
                // antwortenden Server fuer alle anderen Zeilen scheitern zu
                // lassen.
                return null;
            }
        }
    }
}
