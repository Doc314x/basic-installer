using System;
using System.IO;

namespace BasicInstaller.Services
{
    /// <summary>
    /// Schreibt eine dauerhafte Kopie des Protokolls mit, zusaetzlich zur
    /// Textbox im Hauptfenster (die beim Schliessen verloren geht). Eine
    /// Datei pro Programmstart unter %APPDATA%\BasicInstaller\logs - fuer den
    /// Fall, dass spaeter nachvollzogen werden muss, was auf einem Rechner
    /// wann installiert wurde (Support/Audit-Spur).
    /// </summary>
    public static class InstallLog
    {
        private static readonly object SyncRoot = new object();
        private static string _logFilePath;

        public static string LogDirectory { get; } = Path.Combine(ConfigStore.AppDataDirectory, "logs");

        public static void Write(string line)
        {
            lock (SyncRoot)
            {
                try
                {
                    Directory.CreateDirectory(LogDirectory);
                    if (_logFilePath == null)
                        _logFilePath = Path.Combine(LogDirectory, "install_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".log");

                    File.AppendAllText(_logFilePath, $"[{DateTime.Now:HH:mm:ss}] {line}{Environment.NewLine}");
                }
                catch
                {
                    // Logging darf die eigentliche Installation niemals zum
                    // Absturz bringen - ein voller/gesperrter Log-Ordner ist
                    // kein Grund, den Rest des Programms zu stoppen.
                }
            }
        }
    }
}
