using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using BasicInstaller.Models;
using BasicInstaller.Services;

namespace BasicInstaller
{
    /// <summary>
    /// Popup fuer App-spezifische Installationsoptionen (Checkboxen aus
    /// AppEntry.options) und/oder Lizenz (Schluessel oder Lizenzdatei-Inhalt).
    /// Nur sichtbar/aufrufbar ueber den "⚙"-Button in MainForm - bewusst kein
    /// Platz dafuer in der Haupttabelle, die clean bleiben soll.
    ///
    /// Aenderungen werden erst bei "OK" auf die AppRow uebernommen (Cancel
    /// verwirft sie clean, ohne Nebenwirkungen).
    /// </summary>
    public class OptionsDialog : Form
    {
        private readonly AppRow _row;
        private readonly Dictionary<string, CheckBox> _optionCheckboxes = new Dictionary<string, CheckBox>();
        private TextBox _txtLicense;
        private CheckBox _chkRemember;

        public bool RememberLicenseInVault { get; private set; }

        public OptionsDialog(AppRow row)
        {
            _row = row;

            Text = "Optionen – " + row.Name;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9f);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(0, 0, 0, 8);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                AutoSize = true,
                Padding = new Padding(16),
            };

            if (row.HasOptions)
            {
                layout.Controls.Add(new Label
                {
                    Text = "Installationsoptionen:",
                    AutoSize = true,
                    Font = new Font(Font, FontStyle.Bold),
                });

                foreach (var option in row.Source.options)
                {
                    var hasSaved = row.Options.TryGetValue(option.key, out var savedValue);
                    var chk = new CheckBox
                    {
                        Text = option.label,
                        AutoSize = true,
                        Checked = hasSaved ? savedValue : option.default_value,
                        Margin = new Padding(8, 2, 0, 2),
                    };
                    _optionCheckboxes[option.key] = chk;
                    layout.Controls.Add(chk);
                }
            }

            if (row.LicenseKeyEditable)
            {
                var isKeyFile = row.Source.license_type == "key_file";

                layout.Controls.Add(new Label { Text = " ", AutoSize = true }); // etwas Abstand
                layout.Controls.Add(new Label
                {
                    Text = isKeyFile ? $"Lizenzdatei-Inhalt ({row.Source.license_file_name}):" : "Lizenzschlüssel:",
                    AutoSize = true,
                    Font = new Font(Font, FontStyle.Bold),
                });

                _txtLicense = new TextBox
                {
                    Text = row.LicenseKey,
                    Width = 380,
                    Multiline = isKeyFile,
                    Height = isKeyFile ? 100 : 23,
                    ScrollBars = isKeyFile ? ScrollBars.Vertical : ScrollBars.None,
                };
                layout.Controls.Add(_txtLicense);

                if (isKeyFile)
                {
                    var btnLoad = new Button { Text = "Aus Datei laden…", AutoSize = true, Margin = new Padding(0, 4, 0, 0) };
                    btnLoad.Click += (s, e) =>
                    {
                        using (var ofd = new OpenFileDialog { Filter = "Alle Dateien (*.*)|*.*" })
                        {
                            if (ofd.ShowDialog(this) == DialogResult.OK)
                            {
                                try { _txtLicense.Text = File.ReadAllText(ofd.FileName); }
                                catch (Exception ex)
                                {
                                    MessageBox.Show(this, "Datei konnte nicht gelesen werden: " + ex.Message,
                                        "Basic Installer", MessageBoxButtons.OK, MessageBoxIcon.Error);
                                }
                            }
                        }
                    };
                    layout.Controls.Add(btnLoad);
                }

                _chkRemember = new CheckBox
                {
                    Text = "🔒 Im Windows Credential Manager merken (nur dieser PC – nie in config.json, nie im Repo)",
                    AutoSize = true,
                    Checked = WindowsCredentialStore.TryRead(WindowsCredentialStore.KeyForApp(row.Source.id)) != null,
                    Margin = new Padding(0, 8, 0, 0),
                    MaximumSize = new Size(380, 0),
                };
                layout.Controls.Add(_chkRemember);
            }

            var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            var btnCancel = new Button { Text = "Abbrechen", DialogResult = DialogResult.Cancel, AutoSize = true };
            btnOk.Click += (s, e) => CommitChanges();

            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Padding = new Padding(16, 0, 16, 0),
            };
            btnPanel.Controls.Add(btnOk);
            btnPanel.Controls.Add(btnCancel);

            Controls.Add(btnPanel);
            Controls.Add(layout);
            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        private void CommitChanges()
        {
            foreach (var kv in _optionCheckboxes)
                _row.Options[kv.Key] = kv.Value.Checked;

            if (_row.LicenseKeyEditable)
            {
                _row.LicenseKey = _txtLicense.Text;
                RememberLicenseInVault = _chkRemember.Checked;
            }
        }
    }
}
