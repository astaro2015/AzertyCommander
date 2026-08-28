using System.ComponentModel;
using System.Text.RegularExpressions;

namespace AzertyCommander;

internal sealed class DuplicateFinderForm : Form
{
    private readonly TextBox _rootBox = new();
    private readonly ComboBox _maskBox = new();
    private readonly CheckBox _subfoldersBox = new();
    private readonly NumericUpDown _minimumSizeBox = new();
    private readonly BindingList<DuplicateRow> _rows = new();
    private readonly DataGridView _grid = new();
    private readonly Label _status = new();
    private readonly Button _startButton = new();
    private readonly Button _feedButton = new();
    private CancellationTokenSource? _cancellation;

    public DuplicateFinderForm(string root)
    {
        Text = "Поиск одинаковых файлов";
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(940, 600);
        ClientSize = new Size(1180, 740);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        BuildUi(root);
    }

    public event Action<string>? OpenRequested;
    public event Action<IReadOnlyList<FileSystemEntry>, string>? FeedResultsRequested;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cancellation?.Cancel();
        base.OnFormClosing(e);
    }

    private void BuildUi(string rootPath)
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 124));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));

        var options = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3 };
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        options.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        options.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        options.Controls.Add(CreateLabel("Каталог:"), 0, 0);
        _rootBox.Text = rootPath;
        _rootBox.Dock = DockStyle.Fill;
        options.Controls.Add(_rootBox, 1, 0);
        var browse = new Button { Text = ">>", Dock = DockStyle.Fill, Margin = new Padding(4, 2, 4, 2) };
        browse.Click += (_, _) => Browse();
        options.Controls.Add(browse, 2, 0);
        _subfoldersBox.Text = "С подпапками";
        _subfoldersBox.Checked = true;
        _subfoldersBox.Dock = DockStyle.Fill;
        options.Controls.Add(_subfoldersBox, 3, 0);
        options.Controls.Add(CreateLabel("Маска файлов:"), 0, 1);
        _maskBox.Text = "*";
        _maskBox.Items.AddRange(["*", "*.jpg;*.png", "*.mp3;*.flac", "*.zip;*.rar;*.7z"]);
        _maskBox.Dock = DockStyle.Fill;
        options.SetColumnSpan(_maskBox, 3);
        options.Controls.Add(_maskBox, 1, 1);
        options.Controls.Add(CreateLabel("Минимум, КБ:"), 0, 2);
        _minimumSizeBox.Maximum = decimal.MaxValue;
        _minimumSizeBox.ThousandsSeparator = true;
        _minimumSizeBox.Dock = DockStyle.Left;
        _minimumSizeBox.Width = 180;
        options.Controls.Add(_minimumSizeBox, 1, 2);
        root.Controls.Add(options, 0, 0);

        ConfigureGrid();
        root.Controls.Add(_grid, 0, 1);
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.AutoEllipsis = true;
        _status.Text = "Размер проверяется сначала, SHA-256 вычисляется только для возможных совпадений.";
        root.Controls.Add(_status, 0, 2);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 6, 0, 0) };
        var close = CreateButton("Закрыть", 112);
        close.Click += (_, _) => Close();
        _startButton.Text = "Начать поиск";
        ConfigureButton(_startButton, 150);
        _startButton.Click += async (_, _) =>
        {
            if (_cancellation is null)
            {
                await SearchAsync();
            }
            else
            {
                _cancellation.Cancel();
            }
        };
        _feedButton.Text = "Вывести в панель";
        ConfigureButton(_feedButton, 170);
        _feedButton.Enabled = false;
        _feedButton.Click += (_, _) => FeedToPanel();
        buttons.Controls.Add(close);
        buttons.Controls.Add(_startButton);
        buttons.Controls.Add(_feedButton);
        root.Controls.Add(buttons, 0, 3);
        Controls.Add(root);
    }

    private void ConfigureGrid()
    {
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
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(DuplicateRow.Group), HeaderText = "Группа", Width = 76 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(DuplicateRow.Name), HeaderText = "Имя", Width = 270 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(DuplicateRow.Directory), HeaderText = "Каталог", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(DuplicateRow.SizeText), HeaderText = "Размер", Width = 115 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(DuplicateRow.Hash), HeaderText = "SHA-256", Width = 260 });
        _grid.DoubleClick += (_, _) =>
        {
            if (_grid.CurrentRow?.DataBoundItem is DuplicateRow row)
            {
                OpenRequested?.Invoke(row.Path);
            }
        };
    }

    private async Task SearchAsync()
    {
        var root = _rootBox.Text.Trim();
        if (!Directory.Exists(root))
        {
            MessageBox.Show(this, "Каталог не найден.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _rows.Clear();
        _feedButton.Enabled = false;
        _startButton.Text = "Остановить";
        _cancellation = new CancellationTokenSource();
        var progress = new Progress<string>(text => _status.Text = text);
        try
        {
            var results = await Task.Run(() => FindDuplicates(
                root,
                _maskBox.Text,
                _subfoldersBox.Checked,
                Decimal.ToInt64(_minimumSizeBox.Value) * 1024L,
                progress,
                _cancellation.Token), _cancellation.Token);
            foreach (var row in results)
            {
                _rows.Add(row);
            }

            var groups = results.Select(row => row.Group).Distinct().Count();
            _status.Text = $"Найдено одинаковых файлов: {results.Count}, групп: {groups}.";
            _feedButton.Enabled = results.Count > 0;
        }
        catch (OperationCanceledException)
        {
            _status.Text = "Поиск остановлен.";
        }
        catch (Exception ex)
        {
            _status.Text = "Ошибка поиска.";
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _cancellation.Dispose();
            _cancellation = null;
            _startButton.Text = "Начать поиск";
        }
    }

    private static IReadOnlyList<DuplicateRow> FindDuplicates(
        string root,
        string mask,
        bool recursive,
        long minimumSize,
        IProgress<string> progress,
        CancellationToken token)
    {
        var matcher = CreateMaskMatcher(mask);
        var files = EnumerateFilesSafe(root, recursive, token)
            .Where(path => matcher.IsMatch(Path.GetFileName(path)))
            .Select(path => TryCreateFileInfo(path))
            .Where(info => info is not null && info.Length >= minimumSize)
            .Cast<FileInfo>()
            .ToList();
        var candidates = files.GroupBy(info => info.Length).Where(group => group.Count() > 1).ToList();
        var hashed = new List<(FileInfo File, string Hash)>();
        var processed = 0;
        var candidateCount = Math.Max(1, candidates.Sum(group => group.Count()));
        foreach (var sizeGroup in candidates)
        {
            foreach (var file in sizeGroup)
            {
                token.ThrowIfCancellationRequested();
                progress.Report($"Проверка {++processed} из {candidateCount}: {file.Name}");
                try
                {
                    hashed.Add((file, FileHashService.ComputeSha256Async(file.FullName, token).GetAwaiter().GetResult()));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    // Files that changed or became unreadable during the scan are omitted.
                }
            }
        }

        var result = new List<DuplicateRow>();
        var groupNumber = 0;
        foreach (var group in hashed.GroupBy(item => (item.File.Length, item.Hash)).Where(group => group.Count() > 1).OrderByDescending(group => group.Key.Length))
        {
            groupNumber++;
            result.AddRange(group.OrderBy(item => item.File.FullName, StringComparer.CurrentCultureIgnoreCase).Select(item => new DuplicateRow(
                groupNumber,
                item.File.FullName,
                item.File.Name,
                item.File.DirectoryName ?? string.Empty,
                $"{item.File.Length:N0}",
                item.Hash)));
        }

        return result;
    }

    private void FeedToPanel()
    {
        var entries = _rows.Select(row =>
        {
            var info = new FileInfo(row.Path);
            return new FileSystemEntry(info.Name, info.FullName, false, false, info.Length, info.LastWriteTime, info.Attributes);
        }).ToList();
        FeedResultsRequested?.Invoke(entries, $"Дубликаты: {_rootBox.Text}");
        Close();
    }

    private void Browse()
    {
        using var dialog = new FolderBrowserDialog { SelectedPath = Directory.Exists(_rootBox.Text) ? _rootBox.Text : string.Empty, UseDescriptionForTitle = true, Description = "Каталог для поиска дубликатов" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _rootBox.Text = dialog.SelectedPath;
        }
    }

    private static Regex CreateMaskMatcher(string mask)
    {
        var parts = (string.IsNullOrWhiteSpace(mask) ? "*" : mask).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new Regex(string.Join("|", parts.Select(part => "^" + Regex.Escape(part).Replace("\\*", ".*").Replace("\\?", ".") + "$")), RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root, bool recursive, CancellationToken token)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            string[] files;
            try { files = Directory.GetFiles(directory); } catch { files = []; }
            foreach (var file in files) yield return file;
            if (!recursive) continue;
            string[] directories;
            try { directories = Directory.GetDirectories(directory); } catch { directories = []; }
            foreach (var child in directories)
            {
                try
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
                }
                catch { }
            }
        }
    }

    private static FileInfo? TryCreateFileInfo(string path)
    {
        try { return new FileInfo(path); } catch { return null; }
    }

    private static Label CreateLabel(string text) => new() { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
    private static Button CreateButton(string text, int width) { var button = new Button { Text = text }; ConfigureButton(button, width); return button; }
    private static void ConfigureButton(Button button, int width) { button.AutoSize = true; button.MinimumSize = new Size(width, 36); button.Margin = new Padding(8, 2, 0, 2); button.UseMnemonic = false; }

    private sealed record DuplicateRow(int Group, string Path, string Name, string Directory, string SizeText, string Hash);
}
