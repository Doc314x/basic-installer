using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
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
        private readonly Dictionary<string, List<RadioButton>> _choiceRadios = new Dictionary<string, List<RadioButton>>();
        private TextBox _txtLicense;
        private CheckBox _chkRemember;

        public bool RememberLicenseInVault { get; private set; }

        public OptionsDialog(AppRow row)
        {
            _row = row;

            Text = "Optionen – " + row.Name;
            // Feste Pixelgroessen sind bei abweichender Windows-Textskalierung/
            // Schriftgroesse ein echtes Problem (zu Recht als Feedback
            // gekommen) - AutoSize ist hier also richtig, NICHT das Problem.
            // Der eigentliche Fehler war, Inhalt und Button-Leiste als zwei
            // GETRENNTE andockende Controls direkt aufs Form zu setzen - das
            // macht Form.AutoSize unzuverlaessig (anders als bei Panels).
            // Fix: beides in EINEM aeusseren TableLayoutPanel zusammenfassen
            // (siehe "root" unten) - damit hat das Form nur ein einziges Kind
            // zu vermessen und AutoSize funktioniert wie erwartet. Sizable +
            // AutoScroll bleiben als Sicherheitsnetz fuer Extremfaelle (riesige
            // Schrift + viele Optionen gleichzeitig).
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9f);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(320, 160);
            MaximumSize = new Size(700, 800); // Sicherheitsnetz: ab hier scrollen statt endlos wachsen
            AutoScroll = true;
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
                    if (option.type == "choice")
                    {
                        layout.Controls.Add(new Label { Text = option.label, AutoSize = true, Margin = new Padding(8, 4, 0, 0) });

                        var currentChoice = row.ChoiceOptions.TryGetValue(option.key, out var savedChoice)
                            ? savedChoice
                            : option.default_choice;

                        var choicePanel = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(16, 0, 0, 4) };
                        var radios = new List<RadioButton>();
                        for (var i = 0; i < option.choice_values.Length; i++)
                        {
                            var value = option.choice_values[i];
                            var radioLabel = option.choice_labels != null && i < option.choice_labels.Length
                                ? option.choice_labels[i]
                                : value;
                            var radio = new RadioButton
                            {
                                Text = radioLabel,
                                AutoSize = true,
                                Checked = value == currentChoice,
                                Margin = new Padding(0, 0, 16, 0),
                                Tag = value,
                            };
                            radios.Add(radio);
                            choicePanel.Controls.Add(radio);
                        }
                        _choiceRadios[option.key] = radios;
                        layout.Controls.Add(choicePanel);
                        continue;
                    }

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
                    // Kurzer Text: ein CheckBox mit AutoSize + MaximumSize bricht lange
                    // Texte NICHT um, sondern schneidet sie ab (im Praxistest gesehen).
                    // Die Erklaerung steht deshalb in einem Label, das zuverlaessig umbricht.
                    Text = "🔒 Im Windows Credential Manager merken",
                    AutoSize = true,
                    Checked = WindowsCredentialStore.TryRead(WindowsCredentialStore.KeyForApp(row.Source.id)) != null,
                    Margin = new Padding(0, 8, 0, 0),
                };
                layout.Controls.Add(_chkRemember);
                layout.Controls.Add(new Label
                {
                    Text = "Gilt nur für diesen PC – nie in config.json, nie im Repo.",
                    AutoSize = true,
                    ForeColor = Color.DimGray,
                    Margin = new Padding(20, 0, 0, 0),
                    MaximumSize = new Size(360, 0),
                });
            }

            var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            var btnCancel = new Button { Text = "Abbrechen", DialogResult = DialogResult.Cancel, AutoSize = true };
            btnOk.Click += (s, e) => CommitChanges();

            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Padding = new Padding(16, 8, 16, 8),
            };
            btnPanel.Controls.Add(btnOk);
            btnPanel.Controls.Add(btnCancel);

            // EIN aeusserer Container fuer Inhalt + Buttons (siehe Kommentar
            // oben am Konstruktoranfang) - das ist der einzige direkte
            // Form-Kind-Control, damit Form.AutoSize zuverlaessig greift.
            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(layout, 0, 0);
            root.Controls.Add(btnPanel, 0, 1);

            Controls.Add(root);
            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        private void CommitChanges()
        {
            foreach (var kv in _optionCheckboxes)
                _row.Options[kv.Key] = kv.Value.Checked;

            foreach (var kv in _choiceRadios)
            {
                var chosen = kv.Value.FirstOrDefault(r => r.Checked);
                if (chosen != null)
                    _row.ChoiceOptions[kv.Key] = (string)chosen.Tag;
            }

            if (_row.LicenseKeyEditable)
            {
                _row.LicenseKey = _txtLicense.Text;
                RememberLicenseInVault = _chkRemember.Checked;
            }
        }
    }
}
