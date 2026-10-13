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
    ///
    /// Bis zu <see cref="MaxAttempts"/> Versuche: Mirror-Redirector wie
    /// get.videolan.org leiten jede Anfrage auf einen anderen Mirror um, und
    /// einzelne Mirror sind zeitweise kaputt ("Fehler beim Senden der Anforderung",
    /// im Praxistest bei VLC gesehen) - ein neuer Versuch trifft oft einen
    /// funktionierenden. Bewusst KEIN User-Agent-Header: get.videolan.org liefert
    /// bei einem Browser-User-Agent eine HTML-Seite statt der Datei.
    /// </summary>
    public static class Downloader
    {
        private const int MaxAttempts = 3;
        private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(15);

        public static string CacheDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BasicInstaller", "cache");

        public static async Task<string> DownloadAsync(
            string url,
            string appId,
            IProgress<int> progress,
            CancellationToken cancellationToken,
            string referer = null)
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

                for (var attempt = 1; ; attempt++)
                {
                    try
                    {
                        await DownloadOnceAsync(url, destination, progress, referer, cts.Token).ConfigureAwait(false);
                        return destination;
                    }
                    catch (Exception ex) when (!cts.IsCancellationRequested
                        && (ex is HttpRequestException || ex is IOException))
                    {
                        if (attempt >= MaxAttempts)
                            throw new IOException(Describe(ex) + $" (nach {MaxAttempts} Versuchen)", ex);

                        await Task.Delay(TimeSpan.FromSeconds(attempt), cts.Token).ConfigureAwait(false);
                    }
                }
            }
        }

        private static async Task DownloadOnceAsync(
            string url, string destination, IProgress<int> progress, string referer, CancellationToken token)
        {
            using (var client = new HttpClient())
            {
                if (!string.IsNullOrWhiteSpace(referer))
                    client.DefaultRequestHeaders.Referrer = new Uri(referer);
                await FetchToFileAsync(client, url, destination, progress, token).ConfigureAwait(false);
            }
        }

        private static async Task FetchToFileAsync(
            HttpClient client, string url, string destination, IProgress<int> progress, CancellationToken token)
        {
            using (var response = await client
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token)
                .ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                // Eine HTML-Seite statt der Installer-Datei (Zwischen-/Fehlerseite, z.B. IrfanView ohne
                // Referer) darf nicht still als "Installer" im Cache landen und spaeter fehlschlagen.
                // InvalidDataException wird bewusst NICHT wiederholt (kein transienter Fehler).
                var mediaType = response.Content.Headers.ContentType?.MediaType;
                var ext = Path.GetExtension(destination);
                if (string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase)
                    && (string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(ext, ".msi", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidDataException(
                        "Der Server hat eine Webseite statt der Installer-Datei geliefert (ggf. Referer im Katalog setzen).");
                }

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;

                using (var contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var fileStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                {
                    var buffer = new byte[81920];
                    long totalRead = 0;
                    int read;
                    while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, read, token).ConfigureAwait(false);
                        totalRead += read;
                        if (totalBytes > 0 && progress != null)
                            progress.Report((int)(totalRead * 100 / totalBytes));
                    }
                }
            }
        }

        /// <summary>Fehlertext inklusive innerster Ursache (die Hauptmeldung allein ist oft nichtssagend).</summary>
        private static string Describe(Exception ex)
        {
            var inner = ex;
            while (inner.InnerException != null)
                inner = inner.InnerException;
            return ReferenceEquals(inner, ex) ? ex.Message : ex.Message + " - " + inner.Message;
        }
    }
}
