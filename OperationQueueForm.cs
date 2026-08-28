namespace AzertyCommander;

internal sealed class OperationQueueForm : Form
{
    private readonly OperationQueueManager _manager;
    private readonly DataGridView _grid = new();
    private readonly BindingSource _source = new();
    private readonly Label _statusLabel = new();
    private readonly Button _pauseButton = new();
    private readonly Button _cancelButton = new();
    private readonly Button _clearButton = new();
    private readonly Button _retryButton = new();
    private readonly Button _upButton = new();
    private readonly Button _downButton = new();
    private readonly System.Windows.Forms.Timer _autoHideTimer = new() { Interval = 1400 };
    private bool _allowClose;
    private bool _hadActiveOperations;

    public OperationQueueForm(OperationQueueManager manager)
    {
        _manager = manager;
        Text = "Очередь операций";
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(820, 420);
        ClientSize = new Size(980, 520);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        ShowInTaskbar = false;

        BuildUi();
        _autoHideTimer.Tick += (_, _) =>
        {
            _autoHideTimer.Stop();
            if (!_manager.HasActiveOperations && Visible)
            {
                Hide();
            }
        };
        _manager.Changed += ManagerChanged;
        RefreshItems();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _manager.Changed -= ManagerChanged;
        _autoHideTimer.Stop();
        _autoHideTimer.Dispose();
        base.OnFormClosing(e);
    }

    public void ShowCentered(Form owner)
    {
        if (!Visible)
        {
            Show(owner);
        }

        var bounds = owner.WindowState == FormWindowState.Minimized ? owner.RestoreBounds : owner.Bounds;
        var area = Screen.FromControl(owner).WorkingArea;
        Location = new Point(
            Math.Clamp(bounds.Left + (bounds.Width - Width) / 2, area.Left, Math.Max(area.Left, area.Right - Width)),
            Math.Clamp(bounds.Top + (bounds.Height - Height) / 2, area.Top, Math.Max(area.Top, area.Bottom - Height)));
        BringToFront();
    }

    public void ClosePermanently()
    {
        _allowClose = true;
        Close();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.Controls.Add(BuildGrid(), 0, 0);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.AutoEllipsis = true;
        root.Controls.Add(_statusLabel, 0, 1);
        root.Controls.Add(BuildButtons(), 0, 2);
        Controls.Add(root);
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
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.BackgroundColor = SystemColors.Window;
        _grid.BorderStyle = BorderStyle.FixedSingle;
        _grid.DataSource = _source;
        _grid.SelectionChanged += (_, _) => UpdateButtons();
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(QueuedOperation.Title),
            HeaderText = "Операция",
            Width = 190
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(QueuedOperation.StatusText),
            HeaderText = "Состояние",
            Width = 110
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(QueuedOperation.ProgressText),
            HeaderText = "Скорость и время",
            Width = 250
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(QueuedOperation.CurrentMessage),
            HeaderText = "Текущий файл",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
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
        var closeButton = CreateButton("Скрыть", 110);
        closeButton.Click += (_, _) => Hide();
        _clearButton.Text = "Очистить завершённые";
        ConfigureButton(_clearButton, 190);
        _clearButton.Click += (_, _) => _manager.ClearFinished();
        _cancelButton.Text = "Отменить";
        ConfigureButton(_cancelButton, 120);
        _cancelButton.Click += (_, _) =>
        {
            if (SelectedItem is { } item)
            {
                _manager.Cancel(item);
            }
        };
        _pauseButton.Text = "Пауза";
        ConfigureButton(_pauseButton, 120);
        _pauseButton.Click += (_, _) =>
        {
            if (SelectedItem is not { } item)
            {
                return;
            }

            if (item.Status == QueuedOperationStatus.Running)
            {
                _manager.Pause(item);
            }
            else if (item.Status == QueuedOperationStatus.Paused)
            {
                _manager.Resume(item);
            }
        };

        _retryButton.Text = "Повторить";
        ConfigureButton(_retryButton, 120);
        _retryButton.Click += (_, _) =>
        {
            if (SelectedItem is { } item)
            {
                _manager.Retry(item);
            }
        };
        _upButton.Text = "Вверх";
        ConfigureButton(_upButton, 96);
        _upButton.Click += (_, _) =>
        {
            if (SelectedItem is { } item) _manager.MoveUp(item);
        };
        _downButton.Text = "Вниз";
        ConfigureButton(_downButton, 96);
        _downButton.Click += (_, _) =>
        {
            if (SelectedItem is { } item) _manager.MoveDown(item);
        };

        panel.Controls.Add(closeButton);
        panel.Controls.Add(_clearButton);
        panel.Controls.Add(_cancelButton);
        panel.Controls.Add(_pauseButton);
        panel.Controls.Add(_retryButton);
        panel.Controls.Add(_downButton);
        panel.Controls.Add(_upButton);
        return panel;
    }

    private QueuedOperation? SelectedItem => _grid.CurrentRow?.DataBoundItem as QueuedOperation;

    private void ManagerChanged(object? sender, EventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(RefreshItems);
        }
        else
        {
            RefreshItems();
        }
    }

    private void RefreshItems()
    {
        if (IsDisposed)
        {
            return;
        }

        var selectedId = SelectedItem?.Id;
        var items = _manager.Items.ToList();
        _source.DataSource = items;
        _source.ResetBindings(false);
        if (selectedId is not null)
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.DataBoundItem is QueuedOperation item && item.Id == selectedId)
                {
                    row.Selected = true;
                    _grid.CurrentCell = row.Cells[0];
                    break;
                }
            }
        }

        var active = items.Count(item => item.Status is QueuedOperationStatus.Queued or QueuedOperationStatus.Running or QueuedOperationStatus.Paused);
        var failed = items.Count(item => item.Status == QueuedOperationStatus.Failed);
        _statusLabel.Text = $"Всего: {items.Count}, активных: {active}, ошибок: {failed}";
        if (active > 0)
        {
            _hadActiveOperations = true;
            _autoHideTimer.Stop();
        }
        else if (_hadActiveOperations)
        {
            _hadActiveOperations = false;
            _autoHideTimer.Stop();
            _autoHideTimer.Start();
        }
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var item = SelectedItem;
        _pauseButton.Enabled = item?.Status is QueuedOperationStatus.Running or QueuedOperationStatus.Paused;
        _pauseButton.Text = item?.Status == QueuedOperationStatus.Paused ? "Продолжить" : "Пауза";
        _cancelButton.Enabled = item?.Status is QueuedOperationStatus.Queued or QueuedOperationStatus.Running or QueuedOperationStatus.Paused;
        _clearButton.Enabled = _manager.Items.Any(candidate => candidate.Status is QueuedOperationStatus.Completed or QueuedOperationStatus.Canceled or QueuedOperationStatus.Failed);
        _retryButton.Enabled = item?.Status is QueuedOperationStatus.Failed or QueuedOperationStatus.Canceled;
        _upButton.Enabled = item?.Status == QueuedOperationStatus.Queued;
        _downButton.Enabled = item?.Status == QueuedOperationStatus.Queued;
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
