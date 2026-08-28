using System.ComponentModel;

namespace AzertyCommander;

internal sealed class HashesForm : Form
{
    private readonly IReadOnlyList<string> _paths;
    private readonly BindingList<HashRow> _rows = new();
    private readonly DataGridView _grid = new();
    private readonly Label _status = new();
    private readonly Button _copyButton = new();
    private readonly Button _closeButton = new();
    private CancellationTokenSource? _cancellation;

    public HashesForm(IEnumerable<string> paths)
    {
        _paths = paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        Text = "Контрольные суммы";
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(900, 480);
        ClientSize = new Size(1120, 620);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        BuildUi();
        Shown += async (_, _) => await CalculateAsync();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cancellation?.Cancel();
        base.OnFormClosing(e);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.BackgroundColor = SystemColors.Window;
        _grid.DataSource = _rows;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(HashRow.Name), HeaderText = "Файл", Width = 260 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(HashRow.SizeText), HeaderText = "Размер", Width = 110 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(HashRow.Crc32), HeaderText = "CRC32", Width = 95 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(HashRow.Md5), HeaderText = "MD5", Width = 245 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(HashRow.Sha256), HeaderText = "SHA-256", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _grid.SelectionChanged += (_, _) => _copyButton.Enabled = _grid.SelectedRows.Count > 0 && _cancellation is null;
        root.Controls.Add(_grid, 0, 0);

        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.AutoEllipsis = true;
        root.Controls.Add(_status, 0, 1);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 5, 0, 0) };
        _closeButton.Text = "Закрыть";
        ConfigureButton(_closeButton, 112);
        _closeButton.Click += (_, _) => Close();
        _copyButton.Text = "Копировать выбранное";
        ConfigureButton(_copyButton, 190);
        _copyButton.Enabled = false;
        _copyButton.Click += (_, _) => CopySelected();
        buttons.Controls.Add(_closeButton);
        buttons.Controls.Add(_copyButton);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
    }

    private async Task CalculateAsync()
    {
        _cancellation = new CancellationTokenSource();
        _closeButton.Text = "Отмена";
        _status.Text = "Вычисление контрольных сумм...";
        try
        {
            for (var index = 0; index < _paths.Count; index++)
            {
                var path = _paths[index];
                var progress = new Progress<OperationProgress>(item =>
                    _status.Text = $"{index + 1} из {_paths.Count}: {item.Message}  {item.Current * 100 / Math.Max(1, item.Total)}%");
                var result = await FileHashService.ComputeAsync(path, progress, _cancellation.Token);
                _rows.Add(HashRow.FromResult(result));
            }

            _status.Text = $"Готово. Файлов: {_rows.Count}.";
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Вычисление отменено.";
        }
        catch (Exception ex)
        {
            _status.Text = "Ошибка вычисления.";
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            _closeButton.Text = "Закрыть";
            _copyButton.Enabled = _grid.SelectedRows.Count > 0;
        }
    }

    private void CopySelected()
    {
        var selected = _grid.SelectedRows.Cast<DataGridViewRow>()
            .OrderBy(row => row.Index)
            .Select(row => row.DataBoundItem)
            .OfType<HashRow>()
            .ToList();
        if (selected.Count == 0)
        {
            return;
        }

        var text = string.Join(Environment.NewLine, selected.Select(row =>
            $"{row.Path}{Environment.NewLine}CRC32: {row.Crc32}{Environment.NewLine}MD5: {row.Md5}{Environment.NewLine}SHA-256: {row.Sha256}"));
        Clipboard.SetText(text);
        _status.Text = "Контрольные суммы скопированы в буфер обмена.";
    }

    private static void ConfigureButton(Button button, int width)
    {
        button.AutoSize = true;
        button.MinimumSize = new Size(width, 36);
        button.Margin = new Padding(8, 2, 0, 2);
        button.UseMnemonic = false;
    }

    private sealed record HashRow(string Path, string Name, string SizeText, string Crc32, string Md5, string Sha256)
    {
        public static HashRow FromResult(FileHashResult result) => new(
            result.Path,
            System.IO.Path.GetFileName(result.Path),
            $"{result.Size:N0}",
            result.Crc32,
            result.Md5,
            result.Sha256);
    }
}
