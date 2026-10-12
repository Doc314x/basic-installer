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

            Application.SetHighDpiMode(HighDpiMode.SystemAware);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
