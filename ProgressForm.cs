namespace AzertyCommander;

internal sealed class ProgressForm : Form
{
    private readonly Label _messageLabel = new();
    private readonly ProgressBar _progressBar = new();
    private readonly Button _cancelButton = new();

    public ProgressForm(string title)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(520, 118);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        _messageLabel.SetBounds(12, 12, 496, 34);
        _messageLabel.AutoEllipsis = true;
        _messageLabel.TextAlign = ContentAlignment.MiddleLeft;

        _progressBar.SetBounds(12, 52, 496, 22);
        _progressBar.Minimum = 0;
        _progressBar.Maximum = 1;
        _progressBar.Value = 0;

        _cancelButton.Text = "Отмена";
        _cancelButton.SetBounds(408, 84, 100, 26);
        _cancelButton.Click += (_, _) => CancelRequested?.Invoke(this, EventArgs.Empty);

        Controls.AddRange(new Control[] { _messageLabel, _progressBar, _cancelButton });
    }

    public event EventHandler? CancelRequested;

    public void SetProgress(OperationProgress progress)
    {
        if (IsDisposed)
        {
            return;
        }

        _progressBar.Maximum = Math.Max(1, progress.Total);
        _progressBar.Value = Math.Min(Math.Max(0, progress.Current), _progressBar.Maximum);
        _messageLabel.Text = progress.Message;
    }
}
