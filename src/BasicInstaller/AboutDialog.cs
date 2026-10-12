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
            // Feste Pixelgroessen sind bei abweichender Windows-Textskalierung
            // ein echtes Problem - AutoSize ist richtig, war nicht die Ursache
            // des "verzerrten Fensters". Der Fehler war, Inhalt und Button-
            // Leiste als zwei GETRENNTE andockende Controls direkt aufs Form
            // zu setzen, was Form.AutoSize unzuverlaessig macht. Fix: beides in
            // einem aeusseren TableLayoutPanel ("root" unten) zusammenfassen.
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9f);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(300, 140);
            MaximumSize = new Size(600, 700);
            AutoScroll = true;

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

            var btnClose = new Button { Text = "Schließen", AutoSize = true };
            btnClose.Click += (s, e) => Close();
            var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Top, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(16, 8, 16, 8) };
            btnPanel.Controls.Add(btnClose);

            // EIN aeusserer Container fuer Inhalt + Button (siehe Kommentar
            // oben) - einziges direktes Form-Kind, damit Form.AutoSize
            // zuverlaessig greift.
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(layout, 0, 0);
            root.Controls.Add(btnPanel, 0, 1);

            Controls.Add(root);
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
