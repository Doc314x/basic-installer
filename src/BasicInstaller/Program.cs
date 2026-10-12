using System;
using System.Net;
using System.Windows.Forms;

namespace BasicInstaller
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // .NET 4.8 handelt TLS je nach Windows-Version/Registry-Default nicht
            // zuverlaessig als TLS 1.2 aus - Downloads von Herstellerseiten, die
            // aeltere Protokolle ablehnen, wuerden sonst mit einer kryptischen
            // "underlying connection was closed" SSL-Exception fehlschlagen statt
            // mit einer klaren Fehlermeldung im Log.
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            // DPI-Awareness laeuft fuer .NET Framework 4.8 ausschliesslich ueber
            // app.manifest (dpiAware/dpiAwareness) - Application.SetHighDpiMode()
            // existiert erst ab .NET Core 3 WinForms und wuerde hier nicht mal
            // compilieren (CS0103/CS0117), nicht nur zur Laufzeit nichts tun.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
