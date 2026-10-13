using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using BasicInstaller.Models;

namespace BasicInstaller.Services
{
    public enum InstallOutcome
    {
        Success,
        NonZeroExitCode,
        TimedOut,
        LaunchFailed
    }

    public class InstallResult
    {
        public InstallOutcome Outcome { get; set; }
        public int ExitCode { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Startet einen heruntergeladenen Installer mit seinen konfigurierten
    /// Silent-Switches, optionalem Pfad-Override, optionalen
    /// Installationsoptionen (INI neben dem Installer) und optionalem
    /// Lizenzschluessel bzw. Lizenzdatei.
    /// </summary>
    public static class SilentInstaller
    {
        // Ein haengender Installer (z.B. eine Reboot-Rueckfrage, die wegen /S nie
        // erscheint, oder ein "bereits installiert"-Dialog) darf die ganze
        // Warteschlange nicht fuer immer blockieren - gleiches Prinzip wie die
        // IMAP_TIMEOUT-Regel im IMAP-Mover-Projekt fuer haengende Netzwerk-Hops.
        private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(10);

        public static InstallResult Run(
            AppEntry app,
            string installerPath,
            string installPathOverride,
            string languageCode,
            string licenseValue,
            Dictionary<string, string> optionSubstitutions)
        {
            // "portable" ist kein Installer im eigentlichen Sinn (z.B. O&O
            // ShutUp10++, TeamViewer QuickSupport): nichts wird silent
            // ausgefuehrt, die heruntergeladene Datei wird nur an einen
            // Zielort kopiert (installPathOverride > default_install_path >
            // Downloads als Fallback - alle drei ueber die normale
            // "Installationspfad"-Spalte steuerbar, siehe AppRow.PathEditable)
            // und ist danach direkt startklar. Optional zusaetzlich eine
            // Desktop-Verknuepfung (app.shortcut_name). Komplett eigener,
            // fruehzeitiger Codepfad statt die Process.Start-Logik mit einem
            // weiteren Sonderfall zu verkomplizieren.
            if (string.Equals(app.installer_type, "portable", StringComparison.OrdinalIgnoreCase))
                return RunPortable(app, installerPath, installPathOverride);

            string optionsIniPath = null;
            if (!string.IsNullOrWhiteSpace(app.options_ini_template) && !string.IsNullOrWhiteSpace(app.options_ini_filename))
            {
                try
                {
                    optionsIniPath = WriteOptionsIni(app, installerPath, optionSubstitutions);
                }
                catch (Exception ex)
                {
                    return new InstallResult { Outcome = InstallOutcome.LaunchFailed, Message = "Optionen-INI konnte nicht geschrieben werden: " + ex.Message };
                }
            }

            var arguments = BuildArguments(app, installPathOverride, languageCode, licenseValue, optionSubstitutions);

            // Eine rohe .msi laesst sich nicht direkt per Process.Start ausfuehren
            // (kein eigenstaendiges PE-Executable) - sie muss ueber msiexec.exe
            // laufen, mit /i "<datei>" vor den eigentlichen Silent-Switches.
            // installer_type != "msi" (der Normalfall: exe) startet wie bisher
            // direkt. Siehe catalog/apps.json libreoffice/zoom-Eintraege, die
            // genau diese Luecke zuerst aufgedeckt haben.
            string fileName;
            string fullArguments;
            if (string.Equals(app.installer_type, "msi", StringComparison.OrdinalIgnoreCase))
            {
                fileName = "msiexec.exe";
                fullArguments = $"/i \"{installerPath}\" {arguments}".TrimEnd();
            }
            else
            {
                fileName = installerPath;
                fullArguments = arguments;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = fullArguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                // Wichtig fuer Installer mit relativen Dateiargumenten: Office' ODT-setup.exe wird
                // mit "/configure configuration.xml" gestartet, die Datei liegt neben dem
                // Installer. Ohne WorkingDirectory fand ODT sie nicht (Log: FinalizeConfigFilePath
                // Unexpected -> Fehlerdialog) und wartete unsichtbar bis zum Timeout - die Office-
                // Installation hat deshalb nie funktioniert (Praxistest 13.10.2026).
                WorkingDirectory = Path.GetDirectoryName(installerPath) ?? Environment.CurrentDirectory,
            };

            InstallResult result;
            try
            {
                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        result = new InstallResult { Outcome = InstallOutcome.LaunchFailed, Message = "Prozess konnte nicht gestartet werden." };
                    }
                    else if (!process.WaitForExit((int)InstallTimeout.TotalMilliseconds))
                    {
                        TryKill(process);
                        result = new InstallResult
                        {
                            Outcome = InstallOutcome.TimedOut,
                            Message = $"Installer hat nach {InstallTimeout.TotalMinutes:N0} Minuten nicht reagiert und wurde beendet."
                        };
                    }
                    else if (process.ExitCode != 0)
                    {
                        result = new InstallResult
                        {
                            Outcome = InstallOutcome.NonZeroExitCode,
                            ExitCode = process.ExitCode,
                            Message = $"Installer meldete Exit-Code {process.ExitCode}."
                        };
                    }
                    else
                    {
                        result = new InstallResult { Outcome = InstallOutcome.Success, ExitCode = 0 };
                    }
                }
            }
            catch (Exception ex)
            {
                result = new InstallResult { Outcome = InstallOutcome.LaunchFailed, Message = ex.Message };
            }
            finally
            {
                // Die Options-INI ist nur fuer diesen einen Lauf gedacht und
                // enthaelt ggf. interessante Konfigurationsdetails - nicht im
                // Cache-Ordner herumliegen lassen.
                if (optionsIniPath != null)
                    TryDelete(optionsIniPath);
            }

            if (result.Outcome == InstallOutcome.Success
                && app.license_type == "key_file"
                && !string.IsNullOrWhiteSpace(app.license_file_name)
                && !string.IsNullOrWhiteSpace(licenseValue))
            {
                try
                {
                    WriteLicenseFile(app, installPathOverride, licenseValue);
                }
                catch (Exception ex)
                {
                    // Die Installation selbst war erfolgreich - nur die Lizenz
                    // konnte nicht abgelegt werden. Das klar als eigenen,
                    // unmissverstaendlichen Hinweis zurueckgeben statt den
                    // Erfolg zu verschlucken oder die Lizenz stillschweigend
                    // wegzulassen (gleiches "fail loud"-Prinzip wie ueberall
                    // sonst in diesem Projekt).
                    result = new InstallResult
                    {
                        Outcome = InstallOutcome.Success,
                        ExitCode = 0,
                        Message = "Installiert, aber Lizenzdatei konnte nicht abgelegt werden: " + ex.Message
                    };
                }
            }

            return result;
        }

        private static InstallResult RunPortable(AppEntry app, string installerPath, string installPathOverride)
        {
            try
            {
                // Rangfolge: explizite Nutzerangabe in der Pfad-Spalte (koennte
                // z.B. "%userprofile%\.." enthalten) > Katalog-Default >
                // Downloads als letzter Fallback (bisheriges ShutUp10-Verhalten).
                var destinationDir = !string.IsNullOrWhiteSpace(installPathOverride)
                    ? Environment.ExpandEnvironmentVariables(installPathOverride)
                    : !string.IsNullOrWhiteSpace(app.default_install_path)
                        ? Environment.ExpandEnvironmentVariables(app.default_install_path)
                        : KnownFolders.GetDownloadsFolder();

                Directory.CreateDirectory(destinationDir); // existiert praktisch immer, aber kein Grund darauf zu vertrauen
                // Der Download-Cache legt Dateien als "<app-id>_<name>" ab (Downloader). Dieses interne
                // Praefix gehoert nicht auf den Zielort ("shutup10_OOSU10.exe", "teamviewer_qs_asct_
                // TeamViewerQS.exe" im Praxistest) - hier wieder entfernen.
                var targetName = Path.GetFileName(installerPath);
                var cachePrefix = app.id + "_";
                if (targetName.StartsWith(cachePrefix, StringComparison.OrdinalIgnoreCase) && targetName.Length > cachePrefix.Length)
                    targetName = targetName.Substring(cachePrefix.Length);
                var destination = Path.Combine(destinationDir, targetName);
                File.Copy(installerPath, destination, overwrite: true);

                var message = $"Portable Anwendung abgelegt unter: {destination}";

                if (!string.IsNullOrWhiteSpace(app.shortcut_name))
                {
                    try
                    {
                        ShortcutCreator.CreateDesktopShortcut(app.shortcut_name, destination);
                        message += $" | Desktop-Verknüpfung „{app.shortcut_name}“ angelegt.";
                    }
                    catch (Exception ex)
                    {
                        // Datei liegt bereits da und ist nutzbar - nur die
                        // Verknuepfung fehlt. Gleiches "nicht den Erfolg
                        // verschlucken"-Prinzip wie beim Lizenzdatei-Schreiben.
                        message += $" | Verknüpfung konnte nicht angelegt werden: {ex.Message}";
                    }
                }

                return new InstallResult { Outcome = InstallOutcome.Success, Message = message };
            }
            catch (Exception ex)
            {
                return new InstallResult { Outcome = InstallOutcome.LaunchFailed, Message = "Konnte nicht kopiert werden: " + ex.Message };
            }
        }

        private static string WriteOptionsIni(AppEntry app, string installerPath, Dictionary<string, string> substitutions)
        {
            // Bool- und Choice-Optionen sind bereits in AppRow.BuildOptionSubstitutions
            // zu einheitlichen Strings zusammengefuehrt (bool -> "1"/"0", choice ->
            // der gewaehlte Wert woertlich) - hier nur noch reine Textersetzung.
            var content = app.options_ini_template;
            if (substitutions != null)
                foreach (var kv in substitutions)
                    content = content.Replace("{" + kv.Key + "}", kv.Value);

            var iniPath = Path.Combine(Path.GetDirectoryName(installerPath) ?? ".", app.options_ini_filename);
            File.WriteAllText(iniPath, content, Encoding.Default);
            return iniPath;
        }

        private static void WriteLicenseFile(AppEntry app, string installPathOverride, string licenseContent)
        {
            var installDir = !string.IsNullOrWhiteSpace(installPathOverride)
                ? installPathOverride
                : Environment.ExpandEnvironmentVariables(app.default_install_path ?? "");

            if (string.IsNullOrWhiteSpace(installDir))
                throw new InvalidOperationException("Kein Installationsverzeichnis bekannt (weder Override noch default_install_path gesetzt).");

            Directory.CreateDirectory(installDir);
            File.WriteAllText(Path.Combine(installDir, app.license_file_name), licenseContent, Encoding.ASCII);
        }

        private static void TryKill(Process process)
        {
            // Den ganzen Prozessbaum beenden (taskkill /T): ein Installer-Bootstrapper startet oft
            // Kindprozesse, und bei Office blieb der Prozess nach Process.Kill() sichtbar weiter
            // am Leben (Praxistest). Process.Kill() bleibt als Rueckfallebene.
            try
            {
                var tk = Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = $"/F /T /PID {process.Id}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                tk?.WaitForExit(15000);
            }
            catch { /* Rueckfall unten */ }

            try { if (!process.HasExited) process.Kill(); } catch { /* bereits beendet - egal */ }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* bestenfalls - kein Grund den Lauf als fehlgeschlagen zu werten */ }
        }

        private static string BuildArguments(
            AppEntry app, string installPathOverride, string languageCode, string licenseValue,
            Dictionary<string, string> optionSubstitutions)
        {
            var parts = new StringBuilder();

            void AppendArg(string arg)
            {
                if (string.IsNullOrWhiteSpace(arg)) return;
                if (parts.Length > 0) parts.Append(' ');
                parts.Append(arg);
            }

            if (app.silent_args != null)
                foreach (var arg in app.silent_args)
                    AppendArg(arg);

            foreach (var arg in LanguageResolution.ResolveExtraArgs(app, languageCode))
                AppendArg(arg);

            // Optionen mit eigenem cli_arg_template haengen sich direkt als
            // Silent-Switch an (z.B. ESETs PUA-Erkennung) - unabhaengig vom
            // Datei-Mechanismus (options_ini_template), der separat weiter oben
            // in Run() lief. {key} wird durch den in AppRow.
            // BuildOptionSubstitutions() berechneten Wert ersetzt ("1"/"0" bei
            // Checkboxen, der gewaehlte Wert bei "choice"-Optionen).
            if (app.options != null && optionSubstitutions != null)
            {
                foreach (var option in app.options)
                {
                    if (string.IsNullOrWhiteSpace(option.cli_arg_template)) continue;
                    if (!optionSubstitutions.TryGetValue(option.key, out var value)) continue;
                    AppendArg(option.cli_arg_template.Replace("{" + option.key + "}", value));
                }
            }

            if (!string.IsNullOrWhiteSpace(installPathOverride) && !string.IsNullOrWhiteSpace(app.path_arg_template))
            {
                var quotedPath = "\"" + installPathOverride.TrimEnd('\\') + "\"";
                AppendArg(app.path_arg_template.Replace("{path}", quotedPath));
            }

            // Lizenzschluessel als CLI-Switch ist optional (z.B. ESET): leeres
            // Feld -> Installer laeuft ohne --license-key, Produkt bleibt
            // unaktiviert und kann spaeter aktiviert werden. Die "key_file"-
            // Variante (z.B. WinRAR) laeuft NICHT hier, sondern erst nach
            // erfolgreicher Installation ueber WriteLicenseFile.
            if (app.license_type == "cli_arg"
                && !string.IsNullOrWhiteSpace(licenseValue)
                && !string.IsNullOrWhiteSpace(app.license_key_arg_template))
                AppendArg(app.license_key_arg_template.Replace("{key}", licenseValue));

            return parts.ToString();
        }
    }
}
