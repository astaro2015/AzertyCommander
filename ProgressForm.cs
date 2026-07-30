namespace AzertyCommander;

internal sealed class ProgressForm : Form
{
    private readonly Label _messageLabel = new();
    private readonly Label _detailLabel = new();
    private readonly ProgressBar _progressBar = new();
    private readonly Button _cancelButton = new();
    private DateTime? _startedUtc;

    public ProgressForm(string title)
    {
        Text = title;
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(600, 154);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        _messageLabel.SetBounds(12, 12, 576, 34);
        _messageLabel.AutoEllipsis = true;
        _messageLabel.TextAlign = ContentAlignment.MiddleLeft;

        _progressBar.SetBounds(12, 52, 576, 22);
        _progressBar.Minimum = 0;
        _progressBar.Maximum = 1;
        _progressBar.Value = 0;

        _detailLabel.SetBounds(12, 82, 576, 28);
        _detailLabel.AutoEllipsis = true;
        _detailLabel.TextAlign = ContentAlignment.MiddleLeft;

        _cancelButton.Text = "Отмена";
        _cancelButton.SetBounds(488, 118, 100, 26);
        _cancelButton.Click += (_, _) => CancelRequested?.Invoke(this, EventArgs.Empty);

        Controls.AddRange(new Control[] { _messageLabel, _progressBar, _detailLabel, _cancelButton });
    }

    public event EventHandler? CancelRequested;

    public void ShowCentered(Form owner)
    {
        CenterOver(owner);
        Show(owner);
        CenterOver(owner);
    }

    public void SetProgress(OperationProgress progress)
    {
        if (IsDisposed)
        {
            return;
        }

        _progressBar.Maximum = Math.Max(1, progress.Total);
        _progressBar.Value = Math.Min(Math.Max(0, progress.Current), _progressBar.Maximum);
        _messageLabel.Text = progress.Message;
        _detailLabel.Text = BuildDetailText(progress);
    }

    private void CenterOver(Form owner)
    {
        var ownerBounds = owner.WindowState == FormWindowState.Minimized
            ? owner.RestoreBounds
            : owner.Bounds;
        if (ownerBounds.Width <= 0 || ownerBounds.Height <= 0)
        {
            ownerBounds = Screen.FromControl(owner).WorkingArea;
        }

        var screenArea = Screen.FromRectangle(ownerBounds).WorkingArea;
        var x = ownerBounds.Left + (ownerBounds.Width - Width) / 2;
        var y = ownerBounds.Top + (ownerBounds.Height - Height) / 2;
        x = Math.Clamp(x, screenArea.Left, Math.Max(screenArea.Left, screenArea.Right - Width));
        y = Math.Clamp(y, screenArea.Top, Math.Max(screenArea.Top, screenArea.Bottom - Height));
        Location = new Point(x, y);
    }

    private string BuildDetailText(OperationProgress progress)
    {
        if (progress.BytesTotal <= 0)
        {
            return $"{progress.Current:N0} из {progress.Total:N0}";
        }

        _startedUtc ??= DateTime.UtcNow;
        var elapsed = Math.Max(0.001D, (DateTime.UtcNow - _startedUtc.Value).TotalSeconds);
        var bytesPerSecond = progress.BytesDone / elapsed;
        var remainingBytes = Math.Max(0, progress.BytesTotal - progress.BytesDone);
        var eta = bytesPerSecond > 1D
            ? TimeSpan.FromSeconds(remainingBytes / bytesPerSecond)
            : (TimeSpan?)null;

        return $"{FormatBytes(progress.BytesDone)} из {FormatBytes(progress.BytesTotal)}   {FormatBytesPerSecond(bytesPerSecond)}   осталось: {FormatEta(eta)}";
    }

    private static string FormatBytesPerSecond(double bytesPerSecond)
    {
        if (bytesPerSecond <= 0)
        {
            return "0 Б/с";
        }

        return FormatBytes(bytesPerSecond) + "/с";
    }

    private static string FormatBytes(double bytes)
    {
        string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
        var unit = 0;
        while (bytes >= 1024D && unit < units.Length - 1)
        {
            bytes /= 1024D;
            unit++;
        }

        return unit == 0 ? $"{bytes:N0} {units[unit]}" : $"{bytes:N1} {units[unit]}";
    }

    private static string FormatEta(TimeSpan? eta)
    {
        if (eta is null)
        {
            return "считаю";
        }

        if (eta.Value.TotalHours >= 1)
        {
            return eta.Value.ToString(@"h\:mm\:ss");
        }

        return eta.Value.ToString(@"m\:ss");
    }
}
