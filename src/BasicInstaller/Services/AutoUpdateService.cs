using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace BasicInstaller.Services
{
    public class ReleaseInfo
    {
        public string Version { get; set; }
        public string DownloadUrl { get; set; }
        public string ReleaseNotesUrl { get; set; }
    }

    /// <summary>
    /// Prueft gegen die GitHub-Releases-API auf eine neuere Version (Asset aus
    /// .github/workflows/release.yml) und kann das Update selbst anwenden.
    ///
    /// Braucht ein OEFFENTLICHES Repo: die unauthentifizierte Releases-API
    /// liefert bei privaten Repos 404 statt der Release-Liste. Es wird
    /// absichtlich KEIN Token im Client eingebettet, damit niemand ausversehen
    /// ein Secret in die verteilte exe packt - ein privates Repo heisst also:
    /// kein Auto-Update, keine halbe Loesung mit eingebettetem Token.
    /// </summary>
    public static class AutoUpdateService
    {
        private const string Owner = "Doc314x";
        private const string Repo = "basic-installer";
        private static readonly Uri LatestReleaseUri =
            new Uri($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");

        public static async Task<ReleaseInfo> CheckForUpdateAsync()
        {
            using (var client = new HttpClient())
            {
                // GitHub lehnt Anfragen ohne User-Agent-Header grundsaetzlich mit
                // 403 ab, unabhaengig von Auth. Timeout bewusst kurz: eine
                // Update-Pruefung darf den App-Start nicht spuerbar verzoegern,
                // wenn GitHub mal langsam antwortet oder gar nicht erreichbar ist.
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("BasicInstaller-AutoUpdate");

                HttpResponseMessage response;
                try
                {
                    response = await client.GetAsync(LatestReleaseUri).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    return null; // kein Netz / GitHub nicht erreichbar -> stiller Abbruch, kein Absturz beim Start
                }

                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var serializer = new JavaScriptSerializer();

                // Bewusst DeserializeObject statt Deserialize<Dictionary<string,object>>:
                // JavaScriptSerializer liefert bei untypisierten Zielen verschachtelte
                // JSON-Arrays als ArrayList (nicht object[]) - das muss beim Lesen
                // beruecksichtigt werden, sonst liefert ein Cast immer null.
                if (!(serializer.DeserializeObject(json) is Dictionary<string, object> data))
                    return null;

                var tagName = data.TryGetValue("tag_name", out var tagObj) ? tagObj as string : null;
                if (string.IsNullOrWhiteSpace(tagName))
                    return null;

                string downloadUrl = null;
                if (data.TryGetValue("assets", out var assetsObj) && assetsObj is ArrayList assets)
                {
                    foreach (var assetObj in assets)
                    {
                        if (!(assetObj is Dictionary<string, object> asset)) continue;
                        var name = asset.TryGetValue("name", out var n) ? n as string : null;
                        if (name != null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.TryGetValue("browser_download_url", out var u) ? u as string : null;
                            break;
                        }
                    }
                }

                if (downloadUrl == null)
                    return null;

                return new ReleaseInfo
                {
                    Version = tagName.TrimStart('v', 'V'),
                    DownloadUrl = downloadUrl,
                    ReleaseNotesUrl = $"https://github.com/{Owner}/{Repo}/releases/tag/{tagName}"
                };
            }
        }

        /// <summary>
        /// Einfacher X.Y.Z-Vergleich ohne System.Version: dessen Revision-Komponente
        /// unterscheidet "nicht angegeben" (-1) von "0" - eine Assembly-Version wie
        /// "1.0.0.0" (Revision=0) wuerde gegen ein geparstes "1.0.0" (Revision=-1)
        /// dadurch inkonsistent vergleichen. Hier werden fehlende Stellen stattdessen
        /// explizit als 0 behandelt und nur die ersten drei Stellen verglichen.
        /// </summary>
        public static bool IsNewer(string remoteVersion, string localVersion)
        {
            var remote = ParseVersionParts(remoteVersion);
            var local = ParseVersionParts(localVersion);
            for (var i = 0; i < 3; i++)
            {
                if (remote[i] != local[i])
                    return remote[i] > local[i];
            }
            return false;
        }

        private static int[] ParseVersionParts(string version)
        {
            var parts = (version ?? string.Empty).Split('.');
            var result = new int[3];
            for (var i = 0; i < 3; i++)
                result[i] = i < parts.Length && int.TryParse(parts[i], out var n) ? n : 0;
            return result;
        }

        /// <summary>
        /// Laedt das Release-ZIP, extrahiert es in einen Staging-Ordner und
        /// schreibt ein Batch-Skript, das NACH dem Beenden dieses Prozesses die
        /// Dateien ins Installationsverzeichnis kopiert und die App neu startet.
        ///
        /// Grund fuer den Umweg ueber ein Skript statt direktem Ueberschreiben:
        /// eine laufende .NET-exe haelt ihre eigene Datei gesperrt und kann sich
        /// nicht selbst ersetzen. Der Aufrufer (MainForm) MUSS sich direkt nach
        /// dem Start dieses Skripts beenden (Application.Exit) - das Skript
        /// wartet zwar kurz, aber robocopy schlaegt auf eine noch offene exe
        /// trotzdem fehl, wenn der Prozess laenger laeuft als die Pause.
        /// </summary>
        public static async Task StageUpdateAndLaunchInstallerAsync(ReleaseInfo release, string installDirectory)
        {
            var stagingDir = Path.Combine(Path.GetTempPath(), "BasicInstaller_update_" + Guid.NewGuid().ToString("N"));
            var zipPath = stagingDir + ".zip";
            Directory.CreateDirectory(stagingDir);

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromMinutes(5);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("BasicInstaller-AutoUpdate");
                var bytes = await client.GetByteArrayAsync(release.DownloadUrl).ConfigureAwait(false);
                File.WriteAllBytes(zipPath, bytes);
            }

            ZipFile.ExtractToDirectory(zipPath, stagingDir);

            var exeName = Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName);
            var scriptPath = Path.Combine(Path.GetTempPath(), "basicinstaller_update_" + Guid.NewGuid().ToString("N") + ".bat");

            // "timeout" wartet, bis der aufrufende Prozess sicher beendet ist - siehe
            // Methodenkommentar. /E kopiert auch leere Unterordner mit, /IS /IT
            // ueberschreibt bereits vorhandene Dateien ohne Rueckfrage.
            var script =
                "@echo off\r\n" +
                "timeout /t 2 /nobreak >nul\r\n" +
                $"robocopy \"{stagingDir}\" \"{installDirectory}\" /E /IS /IT >nul\r\n" +
                $"start \"\" \"{Path.Combine(installDirectory, exeName)}\"\r\n" +
                $"rmdir /s /q \"{stagingDir}\"\r\n" +
                $"del \"{zipPath}\"\r\n" +
                "del \"%~f0\"\r\n";

            File.WriteAllText(scriptPath, script);

            Process.Start(new ProcessStartInfo
            {
                FileName = scriptPath,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true
            });
        }
    }
}
