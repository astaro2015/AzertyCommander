using System.ComponentModel;
using System.Text.RegularExpressions;

namespace AzertyCommander;

internal sealed class SearchForm : Form
{
    private readonly TextBox _rootBox = new();
    private readonly TextBox _maskBox = new();
    private readonly CheckBox _includeFoldersBox = new();
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();
    private readonly Button _openButton = new();
    private readonly DataGridView _grid = new();
    private readonly Label _statusLabel = new();
    private readonly BindingList<SearchResult> _results = new();
    private CancellationTokenSource? _searchCancellation;

    public SearchForm(string root)
    {
        Text = "Поиск файлов";
        StartPosition = FormStartPosition.CenterParent;
        Width = 920;
        Height = 620;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        BuildUi(root);
    }

    public event Action<string>? OpenRequested;

    private void BuildUi(string root)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));

        var rootRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        rootRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        rootRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rootRow.Controls.Add(new Label { Text = "Где искать:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 0);
        _rootBox.Text = root;
        _rootBox.Dock = DockStyle.Fill;
        rootRow.Controls.Add(_rootBox, 1, 0);

        var commandRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6 };
        commandRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        commandRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        commandRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
        commandRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        commandRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        commandRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128));
        commandRow.Controls.Add(new Label { Text = "Маска:", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 0);
        _maskBox.Text = "*";
        _maskBox.Dock = DockStyle.Fill;
        commandRow.Controls.Add(_maskBox, 1, 0);
        _includeFoldersBox.Text = "Искать папки";
        _includeFoldersBox.Checked = true;
        _includeFoldersBox.Dock = DockStyle.Fill;
        commandRow.Controls.Add(_includeFoldersBox, 2, 0);
        _startButton.Text = "Старт";
        _startButton.Dock = DockStyle.Fill;
        _startButton.Click += async (_, _) => await StartSearchAsync();
        commandRow.Controls.Add(_startButton, 3, 0);
        _stopButton.Text = "Стоп";
        _stopButton.Dock = DockStyle.Fill;
        _stopButton.Enabled = false;
        _stopButton.Click += (_, _) => _searchCancellation?.Cancel();
        commandRow.Controls.Add(_stopButton, 4, 0);
        _openButton.Text = "Открыть в панели";
        _openButton.Dock = DockStyle.Fill;
        _openButton.Click += (_, _) => OpenSelected();
        commandRow.Controls.Add(_openButton, 5, 0);

        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.BackgroundColor = SystemColors.Window;
        _grid.DataSource = _results;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SearchResult.Name), HeaderText = "Имя", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 35 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SearchResult.Directory), HeaderText = "Папка", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 45 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SearchResult.Type), HeaderText = "Тип", Width = 80 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SearchResult.SizeText), HeaderText = "Размер", Width = 105, DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight } });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(SearchResult.DateText), HeaderText = "Дата", Width = 125 });
        _grid.CellDoubleClick += (_, args) =>
        {
            if (args.RowIndex >= 0)
            {
                OpenSelected();
            }
        };

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;

        layout.Controls.Add(rootRow, 0, 0);
        layout.Controls.Add(commandRow, 0, 1);
        layout.Controls.Add(_grid, 0, 2);
        layout.Controls.Add(_statusLabel, 0, 3);
        Controls.Add(layout);
    }

    private async Task StartSearchAsync()
    {
        var root = _rootBox.Text.Trim();
        if (!Directory.Exists(root))
        {
            MessageBox.Show(this, "Папка поиска не найдена.", "Поиск", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var mask = string.IsNullOrWhiteSpace(_maskBox.Text) ? "*" : _maskBox.Text.Trim();
        _results.Clear();
        _statusLabel.Text = "Идет поиск...";
        _startButton.Enabled = false;
        _stopButton.Enabled = true;
        _searchCancellation = new CancellationTokenSource();

        var progress = new Progress<SearchResult>(result =>
        {
            _results.Add(result);
            _statusLabel.Text = $"Найдено: {_results.Count}";
        });

        try
        {
            await Task.Run(() => Search(root, mask, _includeFoldersBox.Checked, progress, _searchCancellation.Token));
            _statusLabel.Text = $"Готово. Найдено: {_results.Count}";
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = $"Остановлено. Найдено: {_results.Count}";
        }
        finally
        {
            _startButton.Enabled = true;
            _stopButton.Enabled = false;
            _searchCancellation.Dispose();
            _searchCancellation = null;
        }
    }

    private void OpenSelected()
    {
        if (_grid.CurrentRow?.DataBoundItem is SearchResult result)
        {
            OpenRequested?.Invoke(result.FullPath);
        }
    }

    private static void Search(string root, string mask, bool includeFolders, IProgress<SearchResult> progress, CancellationToken token)
    {
        var matcher = CreateMatcher(mask);
        var queue = new Queue<string>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var directory = queue.Dequeue();

            foreach (var childDirectory in SafeDirectories(directory))
            {
                token.ThrowIfCancellationRequested();
                queue.Enqueue(childDirectory);

                if (includeFolders && matcher.IsMatch(Path.GetFileName(childDirectory)))
                {
                    var info = new DirectoryInfo(childDirectory);
                    progress.Report(SearchResult.FromDirectory(info));
                }
            }

            foreach (var file in SafeFiles(directory))
            {
                token.ThrowIfCancellationRequested();
                if (matcher.IsMatch(Path.GetFileName(file)))
                {
                    progress.Report(SearchResult.FromFile(new FileInfo(file)));
                }
            }
        }
    }

    private static Regex CreateMatcher(string mask)
    {
        var parts = mask.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            parts = new[] { "*" };
        }

        var expression = string.Join("|", parts.Select(part => "^" + Regex.Escape(part).Replace("\\*", ".*").Replace("\\?", ".") + "$"));
        return new Regex(expression, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    private static IEnumerable<string> SafeDirectories(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> SafeFiles(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private sealed class SearchResult
    {
        public string Name { get; init; } = string.Empty;
        public string Directory { get; init; } = string.Empty;
        public string FullPath { get; init; } = string.Empty;
        public string Type { get; init; } = string.Empty;
        public long? Size { get; init; }
        public DateTime Modified { get; init; }
        public string SizeText => Size is null ? string.Empty : Size.Value.ToString("N0");
        public string DateText => Modified.ToString("dd.MM.yyyy HH:mm");

        public static SearchResult FromFile(FileInfo file)
        {
            return new SearchResult
            {
                Name = file.Name,
                Directory = file.DirectoryName ?? string.Empty,
                FullPath = file.FullName,
                Type = Path.GetExtension(file.Name).TrimStart('.').ToLowerInvariant(),
                Size = file.Length,
                Modified = file.LastWriteTime
            };
        }

        public static SearchResult FromDirectory(DirectoryInfo directory)
        {
            return new SearchResult
            {
                Name = directory.Name,
                Directory = directory.Parent?.FullName ?? string.Empty,
                FullPath = directory.FullName,
                Type = "<Папка>",
                Size = null,
                Modified = directory.LastWriteTime
            };
        }
    }
}
