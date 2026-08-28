using System.ComponentModel;

namespace AzertyCommander;

internal sealed class DirectoryCompareForm : Form
{
    private readonly string _leftRoot;
    private readonly string _rightRoot;
    private readonly DataGridView _grid = new();
    private readonly BindingList<DirectoryComparisonItem> _items = new();
    private readonly ComboBox _comparisonModeBox = new();
    private readonly TextBox _maskBox = new() { Text = "*" };
    private readonly CheckBox _mirrorBox = new();
    private readonly Label _statusLabel = new();
    private readonly Button _refreshButton = new();
    private readonly Button _leftToRightButton = new();
    private readonly Button _rightToLeftButton = new();
    private CancellationTokenSource? _scanCancellation;

    public DirectoryCompareForm(string leftRoot, string rightRoot)
    {
        _leftRoot = Path.GetFullPath(leftRoot);
        _rightRoot = Path.GetFullPath(rightRoot);
        Text = "Сравнение и синхронизация каталогов";
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(980, 620);
        ClientSize = new Size(1240, 760);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        BuildUi();
        Shown += async (_, _) => await ReloadAsync();
    }

    public event EventHandler<DirectorySyncRequest>? SynchronizationRequested;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _scanCancellation?.Cancel();
        base.OnFormClosing(e);
    }

    public void ShowCentered(Form owner)
    {
        Show(owner);
        var bounds = owner.WindowState == FormWindowState.Minimized ? owner.RestoreBounds : owner.Bounds;
        var area = Screen.FromControl(owner).WorkingArea;
        Location = new Point(
            Math.Clamp(bounds.Left + (bounds.Width - Width) / 2, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(bounds.Top + (bounds.Height - Height) / 2, area.Top, Math.Max(area.Top, area.Bottom - Height)));
    }

    public async Task ReloadAsync()
    {
        if (_scanCancellation is not null)
        {
            return;
        }

        _scanCancellation = new CancellationTokenSource();
        SetScanning(true);
        _statusLabel.Text = "Сканирование каталогов...";
        var progress = new Progress<OperationProgress>(item => _statusLabel.Text = item.Message);
        try
        {
            var results = await DirectoryComparisonService.CompareAsync(
                _leftRoot,
                _rightRoot,
                new DirectoryComparisonOptions(
                    string.IsNullOrWhiteSpace(_maskBox.Text) ? "*" : _maskBox.Text.Trim(),
                    (DirectoryComparisonMode)Math.Max(0, _comparisonModeBox.SelectedIndex)),
                progress,
                _scanCancellation.Token);
            _items.RaiseListChangedEvents = false;
            _items.Clear();
            foreach (var item in results)
            {
                _items.Add(item);
            }
            _items.RaiseListChangedEvents = true;
            _items.ResetBindings();
            _grid.ClearSelection();
            _grid.CurrentCell = null;
            _statusLabel.Text = results.Count == 0
                ? "Каталоги совпадают."
                : $"Отличий: {results.Count}. Выделите строки или оставьте выделение пустым, чтобы обработать все подходящие.";
        }
        catch (OperationCanceledException)
        {
            _statusLabel.Text = "Сравнение отменено.";
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Ошибка сравнения.";
            MessageBox.Show(this, ex.Message, "Сравнение каталогов", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _scanCancellation.Dispose();
            _scanCancellation = null;
            SetScanning(false);
        }
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 154));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.Controls.Add(BuildPathsPanel(), 0, 0);
        root.Controls.Add(BuildGrid(), 0, 1);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.AutoEllipsis = true;
        root.Controls.Add(_statusLabel, 0, 2);
        root.Controls.Add(BuildButtons(), 0, 3);
        Controls.Add(root);
    }

    private Control BuildPathsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 88));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 5; row++) panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        panel.Controls.Add(CreateLabel("Слева:"), 0, 0);
        panel.Controls.Add(CreatePathLabel(_leftRoot), 1, 0);
        panel.Controls.Add(CreateLabel("Справа:"), 0, 1);
        panel.Controls.Add(CreatePathLabel(_rightRoot), 1, 1);
        panel.Controls.Add(CreateLabel("Режим:"), 0, 2);
        _comparisonModeBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _comparisonModeBox.Items.AddRange(["Дата и размер", "SHA-256", "Побайтовое сравнение"]);
        _comparisonModeBox.SelectedIndex = 0;
        _comparisonModeBox.Dock = DockStyle.Left;
        _comparisonModeBox.Width = 280;
        panel.Controls.Add(_comparisonModeBox, 1, 2);
        panel.Controls.Add(CreateLabel("Маска:"), 0, 3);
        _maskBox.Dock = DockStyle.Fill;
        panel.Controls.Add(_maskBox, 1, 3);
        _mirrorBox.Text = "Зеркальный режим: удалить в назначении элементы, которых нет в источнике";
        _mirrorBox.Dock = DockStyle.Fill;
        _mirrorBox.UseMnemonic = false;
        panel.SetColumnSpan(_mirrorBox, 2);
        panel.Controls.Add(_mirrorBox, 0, 4);
        return panel;
    }

    private Control BuildGrid()
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
        _grid.BorderStyle = BorderStyle.FixedSingle;
        _grid.DataSource = _items;
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(DirectoryComparisonItem.KindText),
            HeaderText = "Состояние",
            Width = 130
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(DirectoryComparisonItem.RelativePath),
            HeaderText = "Относительный путь",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 55
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(DirectoryComparisonItem.LeftText),
            HeaderText = "Слева",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 23
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(DirectoryComparisonItem.RightText),
            HeaderText = "Справа",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 23
        });
        _grid.CellFormatting += (_, args) =>
        {
            if (args.RowIndex < 0 || args.RowIndex >= _items.Count) return;
            var color = _items[args.RowIndex].Kind switch
            {
                DirectoryComparisonKind.OnlyLeft => Color.FromArgb(225, 244, 225),
                DirectoryComparisonKind.OnlyRight => Color.FromArgb(225, 238, 252),
                _ => Color.FromArgb(255, 241, 205)
            };
            _grid.Rows[args.RowIndex].DefaultCellStyle.BackColor = color;
            _grid.Rows[args.RowIndex].DefaultCellStyle.SelectionForeColor = Color.Black;
            _grid.Rows[args.RowIndex].DefaultCellStyle.SelectionBackColor = ControlPaint.Dark(color, 0.05F);
        };
        return _grid;
    }

    private Control BuildButtons()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 6, 0, 0)
        };
        var closeButton = CreateButton("Закрыть", 110);
        closeButton.Click += (_, _) => Close();
        _refreshButton.Text = "Обновить сравнение";
        ConfigureButton(_refreshButton, 180);
        _refreshButton.Click += async (_, _) => await ReloadAsync();
        _rightToLeftButton.Text = "Справа -> влево";
        ConfigureButton(_rightToLeftButton, 170);
        _rightToLeftButton.Click += (_, _) => RequestSynchronization(DirectorySyncDirection.RightToLeft);
        _leftToRightButton.Text = "Слева -> вправо";
        ConfigureButton(_leftToRightButton, 170);
        _leftToRightButton.Click += (_, _) => RequestSynchronization(DirectorySyncDirection.LeftToRight);

        panel.Controls.Add(closeButton);
        panel.Controls.Add(_refreshButton);
        panel.Controls.Add(_rightToLeftButton);
        panel.Controls.Add(_leftToRightButton);
        return panel;
    }

    private void RequestSynchronization(DirectorySyncDirection direction)
    {
        if (_mirrorBox.Checked && !string.Equals(_maskBox.Text.Trim(), "*", StringComparison.Ordinal))
        {
            MessageBox.Show(this, "Зеркальный режим доступен только с маской *: иначе можно удалить папки с файлами, скрытыми фильтром.", "Синхронизация", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var selected = _grid.SelectedRows
            .Cast<DataGridViewRow>()
            .OrderBy(row => row.Index)
            .Select(row => row.DataBoundItem)
            .OfType<DirectoryComparisonItem>()
            .ToList();
        var sourceMissingKind = direction == DirectorySyncDirection.LeftToRight
            ? DirectoryComparisonKind.OnlyRight
            : DirectoryComparisonKind.OnlyLeft;
        var scope = selected.Count > 0 ? selected : _items.ToList();
        var candidates = (_mirrorBox.Checked ? scope : scope.Where(item => item.Kind != sourceMissingKind)).ToList();
        var copyCount = candidates.Count(item => item.Kind != sourceMissingKind);
        var deleteCount = _mirrorBox.Checked ? candidates.Count(item => item.Kind == sourceMissingKind) : 0;
        if (candidates.Count == 0)
        {
            MessageBox.Show(this, "Для выбранного направления нет файлов-источников.", "Синхронизация", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var directionText = direction == DirectorySyncDirection.LeftToRight ? "слева направо" : "справа налево";
        var mirrorWarning = _mirrorBox.Checked
            ? $"\n\nЗеркальный режим удалит в назначении {deleteCount} лишних элемент(ов). Это удаление без корзины."
            : string.Empty;
        if (MessageBox.Show(
                this,
                $"Поставить в очередь синхронизацию {copyCount} элемент(ов) {directionText}?\n\nСуществующие файлы будут заменены.{mirrorWarning}",
                "Синхронизация каталогов",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        SynchronizationRequested?.Invoke(this, new DirectorySyncRequest(_leftRoot, _rightRoot, direction, candidates, _mirrorBox.Checked));
        _statusLabel.Text = "Синхронизация добавлена в очередь операций.";
    }

    private void SetScanning(bool scanning)
    {
        _refreshButton.Enabled = !scanning;
        _leftToRightButton.Enabled = !scanning && _items.Count > 0;
        _rightToLeftButton.Enabled = !scanning && _items.Count > 0;
        _comparisonModeBox.Enabled = !scanning;
        _maskBox.Enabled = !scanning;
        _mirrorBox.Enabled = !scanning;
    }

    private static Label CreateLabel(string text)
    {
        return new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    }

    private static Label CreatePathLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            BorderStyle = BorderStyle.Fixed3D
        };
    }

    private static Button CreateButton(string text, int width)
    {
        var button = new Button { Text = text };
        ConfigureButton(button, width);
        return button;
    }

    private static void ConfigureButton(Button button, int width)
    {
        button.AutoSize = true;
        button.MinimumSize = new Size(width, 36);
        button.Margin = new Padding(8, 2, 0, 2);
        button.UseMnemonic = false;
    }
}
