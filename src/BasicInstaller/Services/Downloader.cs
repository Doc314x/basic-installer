using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace BasicInstaller.Services
{
    /// <summary>
    /// Laedt Installer-Dateien in einen lokalen Cache. Silent-Installer sind
    /// oft mehrere hundert MB; ohne Timeout haengt ein Download im Zweifel
    /// ewig an einer toten Verbindung (gleiches Problem wie IMAP_TIMEOUT im
    /// IMAP-Mover-Projekt), daher ein grosszuegiges, aber endliches Limit statt
    /// gar keinem.
    /// </summary>
    public static class Downloader
    {
        private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(15);

        public static string CacheDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BasicInstaller", "cache");

        public static async Task<string> DownloadAsync(
            string url,
            string appId,
            IProgress<int> progress,
            CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(CacheDirectory);

            string fileName;
            try
            {
                fileName = Path.GetFileName(new Uri(url).LocalPath);
            }
            catch (UriFormatException)
            {
                fileName = null;
            }
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = appId + ".exe";

            var destination = Path.Combine(CacheDirectory, appId + "_" + fileName);

            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                cts.CancelAfter(DownloadTimeout);

                using (var client = new HttpClient())
                using (var response = await client
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token)
                    .ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? -1L;

                    using (var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var fileStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        var buffer = new byte[81920];
                        long totalRead = 0;
                        int read;
                        while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, cts.Token).ConfigureAwait(false)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, read, cts.Token).ConfigureAwait(false);
                            totalRead += read;
                            if (totalBytes > 0 && progress != null)
                                progress.Report((int)(totalRead * 100 / totalBytes));
                        }
                    }
                }
            }

            return destination;
        }
    }
}
