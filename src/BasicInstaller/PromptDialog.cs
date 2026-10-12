using System.Drawing;
using System.Windows.Forms;

namespace BasicInstaller
{
    /// <summary>
    /// Minimaler Eingabedialog (WinForms hat kein eingebautes InputBox) - hier
    /// nur fuer den Profilnamen bei "Speichern als..." gebraucht.
    ///
    /// AutoSize statt fester Pixel-Positionen: bei groesserer Windows-
    /// Textskalierung/Schriftgroesse wuerden feste Koordinaten Text
    /// abschneiden oder Controls ueberlappen lassen. Inhalt und Buttons liegen
    /// dafuer in EINEM aeusseren TableLayoutPanel (nicht zwei getrennten
    /// andockenden Controls direkt aufs Form) - das ist der Teil, der
    /// Form.AutoSize zuverlaessig macht, siehe OptionsDialog/AboutDialog fuer
    /// die ausfuehrlichere Begruendung desselben Musters.
    /// </summary>
    public class PromptDialog : Form
    {
        private readonly TextBox _txtValue;

        public string Value => _txtValue.Text.Trim();

        public PromptDialog(string title, string label, string initialValue = "")
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9f);
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            MinimumSize = new Size(260, 100);

            var content = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 1,
                AutoSize = true,
                Padding = new Padding(16),
            };
            content.Controls.Add(new Label { Text = label, AutoSize = true, Margin = new Padding(0, 0, 0, 4) });
            _txtValue = new TextBox { Text = initialValue, Width = 300, Margin = new Padding(0, 0, 0, 0) };
            content.Controls.Add(_txtValue);

            var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            var btnCancel = new Button { Text = "Abbrechen", DialogResult = DialogResult.Cancel, AutoSize = true };
            var btnPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.RightToLeft,
                AutoSize = true,
                Padding = new Padding(16, 8, 16, 12),
            };
            btnPanel.Controls.Add(btnOk);
            btnPanel.Controls.Add(btnCancel);

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, AutoSize = true };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.Controls.Add(content, 0, 0);
            root.Controls.Add(btnPanel, 0, 1);

            Controls.Add(root);
            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }
    }
}
