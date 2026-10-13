using System;
using System.IO;

namespace BasicInstaller.Services
{
    /// <summary>
    /// Legt eine .lnk-Desktop-Verknuepfung an - ueber spaet gebundenes COM
    /// ("WScript.Shell", Windows Script Host Shell Object), das auf jedem
    /// Windows von Haus aus registriert ist. Bewusst KEIN Verweis auf die
    /// "Windows Script Host Object Model"-Typbibliothek (IWshRuntimeLibrary):
    /// das wuerde einen COM-Reference-Eintrag im Projekt brauchen, der auf dem
    /// Build-Rechner (hier: GitHub-Actions-Windows-Runner) registriert sein
    /// muesste - spaete Bindung per "dynamic" braucht nur Microsoft.CSharp
    /// (in .NET Framework 4.8 Teil der Standardbibliothek, kein NuGet).
    /// </summary>
    public static class ShortcutCreator
    {
        public static void CreateDesktopShortcut(string name, string targetPath)
        {
            var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var shortcutPath = Path.Combine(desktopDir, name + ".lnk");

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            dynamic shell = Activator.CreateInstance(shellType);
            try
            {
                dynamic shortcut = shell.CreateShortcut(shortcutPath);
                try
                {
                    shortcut.TargetPath = targetPath;
                    shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath);
                    shortcut.Save();
                }
                finally
                {
                    System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
                }
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
