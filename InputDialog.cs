namespace AzertyCommander;

internal sealed class InputDialog : Form
{
    private readonly TextBox _textBox = new();

    private InputDialog(string title, string label, string defaultValue)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(420, 122);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var labelControl = new Label
        {
            Text = label,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft
        };
        labelControl.SetBounds(12, 12, 396, 22);

        _textBox.SetBounds(12, 40, 396, 24);
        _textBox.Text = defaultValue;
        _textBox.SelectAll();

        var okButton = new Button { Text = "OK", DialogResult = DialogResult.OK };
        okButton.SetBounds(220, 82, 90, 28);

        var cancelButton = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel };
        cancelButton.SetBounds(318, 82, 90, 28);

        AcceptButton = okButton;
        CancelButton = cancelButton;
        Controls.AddRange(new Control[] { labelControl, _textBox, okButton, cancelButton });
    }

    public static string? Show(IWin32Window owner, string title, string label, string defaultValue = "")
    {
        using var dialog = new InputDialog(title, label, defaultValue);
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog._textBox.Text.Trim() : null;
    }
}
