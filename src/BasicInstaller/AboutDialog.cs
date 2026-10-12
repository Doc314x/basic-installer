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
    /// Erstellung dieses Dialogs. Die E-Mail-Adresse ist auf der Webseite per
    /// JavaScript verschleiert ("[email&#160;protected]"-Platzhalter) und wurde
    /// deshalb bewusst NICHT geraten/erfunden - bitte unten manuell ergänzen.
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
            ClientSize = new Size(360, 260);
            Font = new Font("Segoe UI", 9f);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
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
            AddLine(layout, "E-Mail: (bitte eintragen - auf der Webseite" +
                             " per JavaScript verschleiert, siehe Quellcode-Kommentar)");

            var website = new LinkLabel { Text = "https://as-ct.de", AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
            website.LinkClicked += (s, e) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://as-ct.de") { UseShellExecute = true }); }
                catch { /* kein Browser verfuegbar - nicht kritisch */ }
            };
            layout.Controls.Add(website);

            var btnClose = new Button { Text = "Schließen", AutoSize = true, Anchor = AnchorStyles.Right };
            btnClose.Click += (s, e) => Close();
            var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true };
            btnPanel.Controls.Add(btnClose);

            Controls.Add(layout);
            Controls.Add(btnPanel);
            AcceptButton = btnClose;
        }

        private static void AddLine(TableLayoutPanel layout, string text, bool bold = false, float size = 9f)
        {
            layout.Controls.Add(new Label
            {
                Text = text,
                AutoSize = true,
                Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular),
                Margin = new Padding(0, 1, 0, 1),
            });
        }
    }
}
