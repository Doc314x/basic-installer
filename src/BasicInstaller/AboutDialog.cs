using System;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

namespace BasicInstaller
{
    /// <summary>
    /// Kontakt-/Impressum-Popup. Bewusst getrennt vom Hauptfenster, damit
    /// dieses clean bleibt - nur über den "ℹ Über / Kontakt"-Link erreichbar.
    /// Quelle der Angaben: https://as-ct.de (Startseite + Impressum), Stand
    /// Erstellung dieses Dialogs. Die E-Mail-Adresse stand dort nur als
    /// Cloudflare-"data-cfemail"-Hex (reversible Anti-Scraping-Verschleierung,
    /// keine echte Geheimhaltung) im HTML - kein erratener/erfundener Wert,
    /// sondern aus der eigenen oeffentlichen Impressum-Seite dekodiert.
    /// </summary>
    public class AboutDialog : Form
    {
        public AboutDialog()
        {
            Text = "Über Basic Software Installer";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9f);
            // AutoSize statt fester ClientSize: eine feste Pixelgroesse war
            // hier schon einmal zu knapp fuer die lange E-Mail-Platzhalter-
            // Zeile (ohne Windows-GUI zum Testen nicht zuverlaessig vorhersagbar
            // - gleiches Prinzip wie bei OptionsDialog/MainForm).
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                AutoSize = true,
                Padding = new Padding(16),
            };

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            AddLine(layout, "Basic Software Installer", bold: true, size: 11f);
            AddLine(layout, $"Version {version.Major}.{version.Minor}.{version.Build}");
            AddLine(layout, "");
            AddLine(layout, "AS Computertechnik", bold: true);
            AddLine(layout, "Inh. Arne Sandfort");
            AddLine(layout, "Stühmeyerstr. 3");
            AddLine(layout, "44787 Bochum");
            AddLine(layout, "");
            AddLine(layout, "Telefon: +49 (0) 234 43 818 42");

            var email = new LinkLabel { Text = "info@as-ct.de", AutoSize = true, Margin = new Padding(0, 1, 0, 1) };
            email.LinkClicked += (s, e) => OpenLink("mailto:info@as-ct.de");
            layout.Controls.Add(email);

            var website = new LinkLabel { Text = "https://as-ct.de", AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
            website.LinkClicked += (s, e) => OpenLink("https://as-ct.de");
            layout.Controls.Add(website);

            var btnClose = new Button { Text = "Schließen", AutoSize = true, Anchor = AnchorStyles.Right };
            btnClose.Click += (s, e) => Close();
            var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
            btnPanel.Controls.Add(btnClose);

            Controls.Add(layout);
            Controls.Add(btnPanel);
            AcceptButton = btnClose;
        }

        private static void OpenLink(string target)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); }
            catch { /* kein Browser/Mail-Client verfuegbar - nicht kritisch */ }
        }

        private static void AddLine(TableLayoutPanel layout, string text, bool bold = false, float size = 9f, int maxWidth = 0)
        {
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
                Margin = new Padding(0, 1, 0, 1),
            };
            if (maxWidth > 0)
                label.MaximumSize = new Size(maxWidth, 0); // 0 = Hoehe frei, nur Breite begrenzen -> Text bricht um
            layout.Controls.Add(label);
        }
    }
}
