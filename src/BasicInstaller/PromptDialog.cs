using System.Drawing;
using System.Windows.Forms;

namespace BasicInstaller
{
    /// <summary>
    /// Minimaler Eingabedialog (WinForms hat kein eingebautes InputBox) - hier
    /// nur fuer den Profilnamen bei "Speichern als..." gebraucht.
    /// </summary>
    public class PromptDialog : Form
    {
        private readonly TextBox _txtValue;

        public string Value => _txtValue.Text.Trim();

        public PromptDialog(string title, string label, string initialValue = "")
        {
            Text = title;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(340, 120);
            Font = new Font("Segoe UI", 9f);

            var lbl = new Label { Text = label, AutoSize = true, Location = new Point(12, 14) };
            _txtValue = new TextBox { Text = initialValue, Location = new Point(12, 36), Width = 300 };

            var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(156, 72), Width = 75 };
            var btnCancel = new Button { Text = "Abbrechen", DialogResult = DialogResult.Cancel, Location = new Point(237, 72), Width = 75 };

            Controls.Add(lbl);
            Controls.Add(_txtValue);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }
    }
}
