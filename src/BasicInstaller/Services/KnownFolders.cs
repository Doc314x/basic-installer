using System;
using System.Runtime.InteropServices;

namespace BasicInstaller.Services
{
    /// <summary>
    /// Duenner P/Invoke-Wrapper fuer SHGetKnownFolderPath - gebraucht fuer den
    /// "Downloads"-Ordner, weil System.Environment.SpecialFolder (CSIDL-basiert,
    /// seit Windows 95) "Downloads" bis heute gar nicht kennt; das Known-Folder-
    /// System (FOLDERID_*) kam erst mit Vista dazu. Ein hartcodiertes
    /// "%USERPROFILE%\Downloads" waere fuer die meisten Faelle richtig, aber
    /// falsch, sobald jemand den Downloads-Ordner ueber die Ordner-
    /// Eigenschaften an einen anderen Ort verlegt hat - deshalb die echte
    /// Shell-API statt eines Pfad-Ratens.
    /// </summary>
    public static class KnownFolders
    {
        private static readonly Guid FolderIdDownloads = new Guid("374DE290-123F-4565-9164-39C4925E467B");

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetKnownFolderPath(
            [MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr pszPath);

        public static string GetDownloadsFolder()
        {
            if (SHGetKnownFolderPath(FolderIdDownloads, 0, IntPtr.Zero, out var pathPtr) != 0)
            {
                // Fallback nur fuer den unwahrscheinlichen Fall, dass die API
                // selbst fehlschlaegt (z.B. sehr alte/kaputte Shell-Installation) -
                // besser ein plausibler Standardpfad als eine Exception mitten
                // im Download-Flow.
                return System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }

            try
            {
                return Marshal.PtrToStringUni(pathPtr);
            }
            finally
            {
                Marshal.FreeCoTaskMem(pathPtr);
            }
        }
    }
}
