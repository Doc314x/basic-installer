using System;
using System.Runtime.InteropServices;

namespace BasicInstaller.Services
{
    /// <summary>
    /// Duenner P/Invoke-Wrapper um den Windows Credential Manager
    /// (advapi32 Cred*-API) - bewusst ohne NuGet-Paket, damit das Projekt
    /// komplett ohne Package-Restore baut (siehe README "Abhaengigkeiten").
    ///
    /// Zweck: Lizenzinhalte (z.B. eine WinRAR-Volumenlizenz) duerfen NIEMALS in
    /// der eigenen config.json landen, weil diese Datei grundsaetzlich als
    /// "koennte versehentlich geteilt/committet werden" behandelt wird - exakt
    /// das gleiche Prinzip wie die IMAP-Passwoerter und das FritzBox-TOTP-Secret
    /// im Schwesterprojekt IMAP-Mover, dort ueber die Python-"keyring"-
    /// Bibliothek, hier eben direkt per WinAPI.
    /// </summary>
    public static class WindowsCredentialStore
    {
        /// <summary>Einheitlicher Target-Name je App - einzige Quelle dieses Formats, siehe MainForm/OptionsDialog.</summary>
        public static string KeyForApp(string appId) => "BasicInstaller_license_" + appId;

        private const int CRED_TYPE_GENERIC = 1;
        private const int CRED_PERSIST_LOCAL_MACHINE = 2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public int Flags;
            public int Type;
            public string TargetName;
            public string Comment;
            public long LastWritten;
            public int CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public int AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CredWrite(ref CREDENTIAL credential, int flags);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CredRead(string target, int type, int flags, out IntPtr credentialPtr);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CredDelete(string target, int type, int flags);

        [DllImport("advapi32.dll")]
        private static extern void CredFree(IntPtr buffer);

        public static void Save(string key, string secret)
        {
            if (string.IsNullOrEmpty(secret))
            {
                Delete(key);
                return;
            }

            var bytes = System.Text.Encoding.Unicode.GetBytes(secret);
            var blobPtr = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, blobPtr, bytes.Length);

                var credential = new CREDENTIAL
                {
                    Type = CRED_TYPE_GENERIC,
                    TargetName = key,
                    CredentialBlobSize = bytes.Length,
                    CredentialBlob = blobPtr,
                    Persist = CRED_PERSIST_LOCAL_MACHINE,
                    UserName = "BasicInstaller",
                };

                if (!CredWrite(ref credential, 0))
                    throw new InvalidOperationException(
                        "Schreiben in den Windows Credential Manager fehlgeschlagen (Fehlercode " + Marshal.GetLastWin32Error() + ").");
            }
            finally
            {
                Marshal.FreeHGlobal(blobPtr);
            }
        }

        public static string TryRead(string key)
        {
            if (!CredRead(key, CRED_TYPE_GENERIC, 0, out var credentialPtr))
                return null;

            try
            {
                var credential = Marshal.PtrToStructure<CREDENTIAL>(credentialPtr);
                if (credential.CredentialBlobSize <= 0 || credential.CredentialBlob == IntPtr.Zero)
                    return null;

                var bytes = new byte[credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
                return System.Text.Encoding.Unicode.GetString(bytes);
            }
            finally
            {
                CredFree(credentialPtr);
            }
        }

        public static void Delete(string key)
        {
            // Rueckgabewert bewusst ignoriert: "existiert nicht" ist kein Fehler,
            // sondern der Normalfall, wenn nie etwas gespeichert wurde.
            CredDelete(key, CRED_TYPE_GENERIC, 0);
        }
    }
}
