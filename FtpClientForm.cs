using System.ComponentModel;

namespace AzertyCommander;

internal sealed class FtpClientForm : Form
{
    private readonly Func<string> _getLocalDirectory;
    private readonly Action _refreshLocalPanels;
    private readonly BindingList<FtpRemoteEntry> _entries = new();
    private readonly TextBox _hostBox = new();
    private readonly NumericUpDown _portBox = new();
    private readonly CheckBox _anonymousBox = new();
    private readonly TextBox _userBox = new();
    private readonly TextBox _passwordBox = new();
    private readonly Button _connectButton = new();
    private readonly Button _disconnectButton = new();
    private readonly TextBox _pathBox = new();
    private readonly Label _localPathLabel = new();
    private readonly DataGridView _grid = new();
    private readonly Label _statusLabel = new();
    private FtpClientSession? _session;
    private bool _busy;

    public FtpClientForm(Func<string> getLocalDirectory, Action refreshLocalPanels)
    {
        _getLocalDirectory = getLocalDirectory;
        _refreshLocalPanels = refreshLocalPanels;
        BuildUi();
        UpdateConnectionFields();
        UpdateButtons();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _session?.Dispose();
        base.OnFormClosing(e);
    }

    private void BuildUi()
    {
        Text = "FTP подключение";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(940, 620);
        ClientSize = new Size(1040, 680);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var tips = new ToolTip();
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));

        var connection = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 10,
            RowCount = 2
        };
        connection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
        connection.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        connection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
        connection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78));
        connection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        connection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        connection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        connection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        connection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        connection.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        connection.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        connection.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        connection.Controls.Add(CreateLabel("Адрес:"), 0, 0);
        _hostBox.Text = "127.0.0.1";
        _hostBox.Dock = DockStyle.Fill;
        tips.SetToolTip(_hostBox, "Адрес без ftp://. Например: 192.168.1.20 или ftp.example.com");
        connection.Controls.Add(_hostBox, 1, 0);

        connection.Controls.Add(CreateLabel("Порт:"), 2, 0);
        _portBox.Minimum = 1;
        _portBox.Maximum = 65535;
        _portBox.Value = 2121;
        _portBox.Dock = DockStyle.Fill;
        tips.SetToolTip(_portBox, "Обычно 21. Встроенный AZERTY FTP сервер по умолчанию запускается на 2121.");
        connection.Controls.Add(_portBox, 3, 0);

        _anonymousBox.Text = "Анонимно";
        _anonymousBox.Checked = true;
        _anonymousBox.Dock = DockStyle.Fill;
        _anonymousBox.CheckedChanged += (_, _) => UpdateConnectionFields();
        tips.SetToolTip(_anonymousBox, "Для быстрых локальных серверов часто достаточно anonymous/guest@.");
        connection.Controls.Add(_anonymousBox, 4, 0);

        connection.Controls.Add(CreateLabel("Логин:"), 5, 0);
        _userBox.Text = "anonymous";
        _userBox.Dock = DockStyle.Fill;
        connection.Controls.Add(_userBox, 6, 0);

        connection.Controls.Add(CreateLabel("Пароль:"), 7, 0);
        _passwordBox.Text = "guest@";
        _passwordBox.UseSystemPasswordChar = true;
        _passwordBox.Dock = DockStyle.Fill;
        connection.Controls.Add(_passwordBox, 8, 0);

        _connectButton.Text = "Соединиться";
        _connectButton.Dock = DockStyle.Fill;
        _connectButton.Click += async (_, _) => await ConnectAsync();
        connection.Controls.Add(_connectButton, 9, 0);

        _localPathLabel.Dock = DockStyle.Fill;
        _localPathLabel.TextAlign = ContentAlignment.MiddleLeft;
        _localPathLabel.AutoEllipsis = true;
        connection.SetColumnSpan(_localPathLabel, 8);
        connection.Controls.Add(_localPathLabel, 0, 1);

        _disconnectButton.Text = "Отключиться";
        _disconnectButton.Dock = DockStyle.Fill;
        _disconnectButton.Click += (_, _) => Disconnect();
        connection.SetColumnSpan(_disconnectButton, 2);
        connection.Controls.Add(_disconnectButton, 8, 1);

        root.Controls.Add(connection, 0, 0);

        var hint = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Text = "Подсказка: скачивание идет в активную локальную панель главного окна. Закачка берёт файлы с диска и кладёт их в текущую FTP-папку. Обычный FTP не шифрует пароль."
        };
        root.Controls.Add(hint, 0, 1);

        var pathRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.Controls.Add(CreateLabel("FTP папка:"), 0, 0);
        _pathBox.Dock = DockStyle.Fill;
        _pathBox.KeyDown += async (_, args) =>
        {
            if (args.KeyCode == Keys.Enter)
            {
                args.SuppressKeyPress = true;
                await ChangeDirectoryAsync(_pathBox.Text);
            }
        };
        tips.SetToolTip(_pathBox, "Можно вписать путь и нажать Enter. Например: /pub/files");
        pathRow.Controls.Add(_pathBox, 1, 0);
        root.Controls.Add(pathRow, 0, 2);

        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.ReadOnly = true;
        _grid.MultiSelect = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.RowHeadersVisible = false;
        _grid.BackgroundColor = Color.White;
        _grid.DataSource = _entries;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(FtpRemoteEntry.DisplayName), HeaderText = "Имя", Width = 430 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(FtpRemoteEntry.TypeText), HeaderText = "Тип", Width = 100 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(FtpRemoteEntry.SizeText),
            HeaderText = "Размер",
            Width = 120,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight }
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(FtpRemoteEntry.DateText), HeaderText = "Дата", Width = 150 });
        _grid.CellDoubleClick += async (_, args) =>
        {
            if (args.RowIndex >= 0)
            {
                await OpenFocusedAsync();
            }
        };
        _grid.KeyDown += async (_, args) =>
        {
            if (args.KeyCode == Keys.Enter)
            {
                args.SuppressKeyPress = true;
                await OpenFocusedAsync();
            }
            else if (args.KeyCode == Keys.Delete)
            {
                args.SuppressKeyPress = true;
                await DeleteSelectedAsync();
            }
        };
        root.Controls.Add(_grid, 0, 3);

        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 8 };
        for (var index = 0; index < 8; index++)
        {
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12.5F));
        }

        buttons.Controls.Add(CreateButton("Обновить", async (_, _) => await RefreshRemoteAsync()), 0, 0);
        buttons.Controls.Add(CreateButton("Вверх", async (_, _) => await ChangeDirectoryAsync(FtpClientSession.ParentRemotePath(CurrentRemotePath))), 1, 0);
        buttons.Controls.Add(CreateButton("Скачать", async (_, _) => await DownloadSelectedAsync()), 2, 0);
        buttons.Controls.Add(CreateButton("Закачать файл", async (_, _) => await UploadFilesAsync()), 3, 0);
        buttons.Controls.Add(CreateButton("Закачать папку", async (_, _) => await UploadFolderAsync()), 4, 0);
        buttons.Controls.Add(CreateButton("Новая папка", async (_, _) => await CreateDirectoryAsync()), 5, 0);
        buttons.Controls.Add(CreateButton("Переименовать", async (_, _) => await RenameSelectedAsync()), 6, 0);
        buttons.Controls.Add(CreateButton("Удалить", async (_, _) => await DeleteSelectedAsync()), 7, 0);
        root.Controls.Add(buttons, 0, 4);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.AutoEllipsis = true;
        root.Controls.Add(_statusLabel, 0, 5);

        Controls.Add(root);
    }

    private string CurrentRemotePath => _session?.CurrentDirectory ?? "/";

    private async Task ConnectAsync()
    {
        await RunBusyAsync("Подключение...", async token =>
        {
            _session?.Dispose();
            _session = new FtpClientSession();
            await _session.ConnectAsync(new FtpConnectionOptions
            {
                Host = _hostBox.Text.Trim(),
                Port = (int)_portBox.Value,
                UserName = _anonymousBox.Checked ? "anonymous" : _userBox.Text.Trim(),
                Password = _anonymousBox.Checked ? "guest@" : _passwordBox.Text
            }, token);
            await LoadRemoteListAsync(token);
            _statusLabel.Text = "Подключено.";
        });
    }

    private void Disconnect()
    {
        _session?.Dispose();
        _session = null;
        _entries.Clear();
        _pathBox.Clear();
        _statusLabel.Text = "Отключено.";
        UpdateButtons();
    }

    private async Task RefreshRemoteAsync()
    {
        await RunBusyAsync("Обновление FTP...", LoadRemoteListAsync);
    }

    private async Task ChangeDirectoryAsync(string path)
    {
        if (_session is null)
        {
            return;
        }

        await RunBusyAsync("Переход...", async token =>
        {
            await _session.ChangeDirectoryAsync(path, token);
            await LoadRemoteListAsync(token);
        });
    }

    private async Task OpenFocusedAsync()
    {
        var entry = FocusedEntry;
        if (entry is null)
        {
            return;
        }

        if (entry.IsParent)
        {
            await ChangeDirectoryAsync(FtpClientSession.ParentRemotePath(CurrentRemotePath));
            return;
        }

        if (entry.IsDirectory)
        {
            await ChangeDirectoryAsync(entry.FullPath);
        }
    }

    private async Task LoadRemoteListAsync(CancellationToken token)
    {
        if (_session is null)
        {
            return;
        }

        var list = await _session.ListAsync(token);
        _entries.Clear();
        if (_session.CurrentDirectory != "/")
        {
            _entries.Add(new FtpRemoteEntry
            {
                Name = "..",
                FullPath = FtpClientSession.ParentRemotePath(_session.CurrentDirectory),
                IsDirectory = true,
                IsParent = true
            });
        }

        foreach (var entry in list)
        {
            _entries.Add(entry);
        }

        _pathBox.Text = _session.CurrentDirectory;
        _localPathLabel.Text = "Активная локальная папка главного окна: " + _getLocalDirectory();
        _statusLabel.Text = $"FTP: {_entries.Count(entry => !entry.IsParent)} элемент(ов).";
    }

    private async Task DownloadSelectedAsync()
    {
        var entries = SelectedEntries();
        if (entries.Count == 0)
        {
            MessageBox.Show(this, "Выберите файлы или папки на FTP.", "Скачать", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var localDirectory = _getLocalDirectory();
        if (!Directory.Exists(localDirectory))
        {
            MessageBox.Show(this, "Активная локальная папка не найдена.", "Скачать", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!ConfirmLocalConflicts(entries, localDirectory))
        {
            return;
        }

        await RunBusyAsync("Скачивание...", async token =>
        {
            var progress = new Progress<string>(text => _statusLabel.Text = text);
            foreach (var entry in entries)
            {
                await DownloadEntryAsync(entry, localDirectory, progress, token);
            }
            _refreshLocalPanels();
            _statusLabel.Text = "Скачивание завершено.";
        });
    }

    private async Task DownloadEntryAsync(FtpRemoteEntry entry, string localDirectory, IProgress<string> progress, CancellationToken token)
    {
        if (_session is null)
        {
            return;
        }

        if (entry.IsDirectory)
        {
            var targetDirectory = Path.Combine(localDirectory, entry.Name);
            Directory.CreateDirectory(targetDirectory);
            var children = await _session.ListAsync(entry.FullPath, token);
            foreach (var child in children)
            {
                await DownloadEntryAsync(child, targetDirectory, progress, token);
            }
            return;
        }

        await _session.DownloadFileAsync(entry.FullPath, Path.Combine(localDirectory, entry.Name), progress, token);
    }

    private async Task UploadFilesAsync()
    {
        if (_session is null)
        {
            return;
        }

        using var dialog = new OpenFileDialog
        {
            Title = "Закачать файлы на FTP",
            Filter = "Все файлы (*.*)|*.*",
            Multiselect = true,
            InitialDirectory = Directory.Exists(_getLocalDirectory()) ? _getLocalDirectory() : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await RunBusyAsync("Закачка файлов...", async token =>
        {
            var progress = new Progress<string>(text => _statusLabel.Text = text);
            foreach (var file in dialog.FileNames)
            {
                var remotePath = FtpClientSession.CombineRemotePath(CurrentRemotePath, Path.GetFileName(file));
                await _session.UploadFileAsync(file, remotePath, progress, token);
            }
            await LoadRemoteListAsync(token);
            _statusLabel.Text = "Закачка завершена.";
        });
    }

    private async Task UploadFolderAsync()
    {
        if (_session is null)
        {
            return;
        }

        using var dialog = new FolderBrowserDialog
        {
            Description = "Выберите папку для закачки на FTP",
            SelectedPath = Directory.Exists(_getLocalDirectory()) ? _getLocalDirectory() : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await RunBusyAsync("Закачка папки...", async token =>
        {
            var progress = new Progress<string>(text => _statusLabel.Text = text);
            var remoteDirectory = FtpClientSession.CombineRemotePath(CurrentRemotePath, Path.GetFileName(dialog.SelectedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
            await UploadDirectoryAsync(dialog.SelectedPath, remoteDirectory, progress, token);
            await LoadRemoteListAsync(token);
            _statusLabel.Text = "Папка закачана.";
        });
    }

    private async Task UploadDirectoryAsync(string localDirectory, string remoteDirectory, IProgress<string> progress, CancellationToken token)
    {
        if (_session is null)
        {
            return;
        }

        try
        {
            await _session.CreateDirectoryAsync(remoteDirectory, token);
        }
        catch
        {
            // Existing directories are fine for recursive upload.
        }

        foreach (var file in Directory.EnumerateFiles(localDirectory))
        {
            var remotePath = FtpClientSession.CombineRemotePath(remoteDirectory, Path.GetFileName(file));
            await _session.UploadFileAsync(file, remotePath, progress, token);
        }

        foreach (var directory in Directory.EnumerateDirectories(localDirectory))
        {
            await UploadDirectoryAsync(directory, FtpClientSession.CombineRemotePath(remoteDirectory, Path.GetFileName(directory)), progress, token);
        }
    }

    private async Task CreateDirectoryAsync()
    {
        if (_session is null)
        {
            return;
        }

        var name = InputDialog.Show(this, "FTP новая папка", "Имя папки:", "Новая папка");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await RunBusyAsync("Создание папки...", async token =>
        {
            await _session.CreateDirectoryAsync(FtpClientSession.CombineRemotePath(CurrentRemotePath, name), token);
            await LoadRemoteListAsync(token);
        });
    }

    private async Task RenameSelectedAsync()
    {
        if (_session is null)
        {
            return;
        }

        var entry = FocusedEntry;
        if (entry is null || entry.IsParent)
        {
            MessageBox.Show(this, "Выберите один FTP-элемент.", "Переименовать", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var name = InputDialog.Show(this, "FTP переименовать", "Новое имя:", entry.Name);
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, entry.Name, StringComparison.Ordinal))
        {
            return;
        }

        await RunBusyAsync("Переименование...", async token =>
        {
            var newPath = FtpClientSession.CombineRemotePath(FtpClientSession.ParentRemotePath(entry.FullPath), name);
            await _session.RenameAsync(entry.FullPath, newPath, token);
            await LoadRemoteListAsync(token);
        });
    }

    private async Task DeleteSelectedAsync()
    {
        var entries = SelectedEntries();
        if (entries.Count == 0)
        {
            MessageBox.Show(this, "Выберите FTP-элементы.", "Удалить", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(this, $"Удалить на FTP: {entries.Count} элемент(ов)?", "FTP удаление", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        await RunBusyAsync("Удаление FTP...", async token =>
        {
            foreach (var entry in entries)
            {
                await DeleteEntryAsync(entry, token);
            }
            await LoadRemoteListAsync(token);
        });
    }

    private async Task DeleteEntryAsync(FtpRemoteEntry entry, CancellationToken token)
    {
        if (_session is null)
        {
            return;
        }

        if (entry.IsDirectory)
        {
            var children = await _session.ListAsync(entry.FullPath, token);
            foreach (var child in children)
            {
                await DeleteEntryAsync(child, token);
            }
            await _session.RemoveDirectoryAsync(entry.FullPath, token);
        }
        else
        {
            await _session.DeleteFileAsync(entry.FullPath, token);
        }
    }

    private async Task RunBusyAsync(string status, Func<CancellationToken, Task> action)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        UpdateButtons();
        Cursor = Cursors.WaitCursor;
        _statusLabel.Text = status;

        try
        {
            await action(CancellationToken.None);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "FTP", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _statusLabel.Text = "Ошибка FTP.";
        }
        finally
        {
            Cursor = Cursors.Default;
            _busy = false;
            UpdateButtons();
        }
    }

    private bool ConfirmLocalConflicts(IReadOnlyList<FtpRemoteEntry> entries, string localDirectory)
    {
        var hasConflicts = entries.Any(entry =>
        {
            var target = Path.Combine(localDirectory, entry.Name);
            return entry.IsDirectory ? Directory.Exists(target) : File.Exists(target);
        });

        return !hasConflicts || MessageBox.Show(
            this,
            "В активной локальной панели уже есть элементы с такими именами. Файлы будут заменены, папки объединены. Продолжить?",
            "Скачать",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) == DialogResult.Yes;
    }

    private IReadOnlyList<FtpRemoteEntry> SelectedEntries()
    {
        var selected = _grid.SelectedRows
            .Cast<DataGridViewRow>()
            .OrderBy(row => row.Index)
            .Select(row => row.DataBoundItem)
            .OfType<FtpRemoteEntry>()
            .Where(entry => !entry.IsParent)
            .ToList();

        if (selected.Count > 0)
        {
            return selected;
        }

        return FocusedEntry is { IsParent: false } entry
            ? new[] { entry }
            : Array.Empty<FtpRemoteEntry>();
    }

    private FtpRemoteEntry? FocusedEntry => _grid.CurrentRow?.DataBoundItem as FtpRemoteEntry;

    private void UpdateConnectionFields()
    {
        if (_anonymousBox.Checked)
        {
            _userBox.Text = "anonymous";
            _passwordBox.Text = "guest@";
        }

        _userBox.Enabled = !_anonymousBox.Checked;
        _passwordBox.Enabled = !_anonymousBox.Checked;
        _localPathLabel.Text = "Активная локальная папка главного окна: " + _getLocalDirectory();
    }

    private void UpdateButtons()
    {
        var connected = _session?.Connected == true;
        _connectButton.Enabled = !_busy && !connected;
        _disconnectButton.Enabled = !_busy && connected;
        _hostBox.Enabled = !_busy && !connected;
        _portBox.Enabled = !_busy && !connected;
        _anonymousBox.Enabled = !_busy && !connected;
        _userBox.Enabled = !_busy && !connected && !_anonymousBox.Checked;
        _passwordBox.Enabled = !_busy && !connected && !_anonymousBox.Checked;
        _pathBox.Enabled = !_busy && connected;
        _grid.Enabled = !_busy && connected;
    }

    private static Label CreateLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
    }

    private static Button CreateButton(string text, EventHandler click)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(3)
        };
        button.Click += click;
        return button;
    }
}
