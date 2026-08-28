namespace AzertyCommander;

internal sealed class MainForm : Form
{
    private readonly AppSettings _settings = AppSettingsStore.Load();
    private readonly FilePanel _leftPanel = new();
    private readonly FilePanel _rightPanel = new();
    private readonly Label _commandPathLabel = new();
    private readonly ToolTip _toolTip = new();
    private readonly TextBox _commandBox = new();
    private readonly SplitContainer _splitContainer = new();
    private readonly ToolStrip _quickLaunchToolbar = new();
    private readonly Dictionary<FilePanel, IRemoteFileSession> _ftpSessions = new();
    private readonly Dictionary<FilePanel, FtpConnectionProfile> _ftpProfiles = new();
    private readonly List<QuickLaunchEntry> _quickLaunchEntries = QuickLaunchStore.Load();
    private readonly OperationQueueManager _operationQueue = new();
    private ContextMenuStrip? _favoriteDirectoriesMenu;
    private OperationQueueForm? _operationQueueForm;
    private DirectoryCompareForm? _directoryCompareForm;
    private FilePanel _activePanel;
    private bool _centeringSplitter;
    private bool _splitterMovedByUser;

    public MainForm()
    {
        _activePanel = _leftPanel;
        BuildUi();
        ApplySavedSettings();
        WirePanels();
        LoadInitialPaths();
        SetActivePanel(_leftPanel);
    }

    private FilePanel PassivePanel => ReferenceEquals(_activePanel, _leftPanel) ? _rightPanel : _leftPanel;

    private void BuildUi()
    {
        Text = $"AZERTY Commander {BuildInfo.Version}";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1180, 700);
        Size = CalculateDefaultWindowSize();
        KeyPreview = true;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? Icon;
        }
        catch
        {
            // The window icon is cosmetic; startup must not fail because of it.
        }

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        var mainMenu = BuildMenu();
        MainMenuStrip = mainMenu;
        root.Controls.Add(mainMenu, 0, 0);
        root.Controls.Add(BuildQuickLaunchBar(), 0, 1);

        _splitContainer.Dock = DockStyle.Fill;
        _splitContainer.Orientation = Orientation.Vertical;
        _splitContainer.SplitterWidth = 2;
        _splitContainer.Panel1MinSize = 1;
        _splitContainer.Panel2MinSize = 1;
        _splitContainer.SplitterMoved += (_, _) =>
        {
            if (!_centeringSplitter && Visible)
            {
                _splitterMovedByUser = true;
            }
        };
        _splitContainer.Panel1.Controls.Add(_leftPanel);
        _splitContainer.Panel2.Controls.Add(_rightPanel);
        root.Controls.Add(_splitContainer, 0, 2);

        var commandRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(4, 5, 4, 5) };
        commandRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330));
        commandRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _commandPathLabel.TextAlign = ContentAlignment.MiddleRight;
        _commandPathLabel.Dock = DockStyle.Fill;
        _commandPathLabel.AutoEllipsis = true;
        commandRow.Controls.Add(_commandPathLabel, 0, 0);
        _commandBox.Dock = DockStyle.Fill;
        _commandBox.KeyDown += (_, args) =>
        {
            if (args.KeyCode == Keys.Enter && args.Control && args.Shift)
            {
                args.SuppressKeyPress = true;
                InsertFocusedPathIntoCommandLine();
            }
            else if (args.KeyCode == Keys.Enter)
            {
                args.SuppressKeyPress = true;
                RunCommandLine();
            }
        };
        commandRow.Controls.Add(_commandBox, 1, 0);
        root.Controls.Add(commandRow, 0, 3);

        root.Controls.Add(BuildFunctionBar(), 0, 4);
        Controls.Add(root);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_settings.Window.SplitterRatio > 0)
        {
            SetSplitterRatio(_settings.Window.SplitterRatio);
            _splitterMovedByUser = true;
        }
        else
        {
            CenterSplitter();
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (IsHandleCreated && !_splitterMovedByUser && WindowState != FormWindowState.Minimized)
        {
            BeginInvoke(CenterSplitter);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_operationQueue.HasActiveOperations && e.CloseReason == CloseReason.UserClosing &&
            MessageBox.Show(
                this,
                "В очереди ещё есть операции. Отменить их и выйти?",
                "Очередь операций",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        SaveCurrentSettings();
        DisposeFtpSessions();
        _operationQueue.Dispose();
        _operationQueueForm?.ClosePermanently();
        base.OnFormClosing(e);
    }

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Padding = new Padding(4, 4, 4, 4),
            Margin = Padding.Empty
        };

        var files = new ToolStripMenuItem("Файлы");
        files.DropDownItems.Add(CreateMenuItem("Просмотр\tF3", (_, _) => ViewText()));
        files.DropDownItems.Add(CreateMenuItem("Переименовать\tF2", (_, _) => RenameSelected()));
        files.DropDownItems.Add(CreateMenuItem("Групповое переименование...\tCtrl+M", (_, _) => ShowBatchRename()));
        files.DropDownItems.Add(CreateMenuItem("Отменить последнее групповое переименование", async (_, _) => await UndoBatchRenameAsync()));
        files.DropDownItems.Add(new ToolStripSeparator());
        files.DropDownItems.Add(CreateMenuItem("Копировать\tF5", async (_, _) => await CopySelectedAsync()));
        files.DropDownItems.Add(CreateMenuItem("Переместить\tF6", async (_, _) => await MoveSelectedAsync()));
        files.DropDownItems.Add(CreateMenuItem("Удалить\tF8/Del", (_, _) => DeleteSelected(false)));
        files.DropDownItems.Add(CreateMenuItem("Удалить безвозвратно\tShift+Del", (_, _) => DeleteSelected(true)));
        files.DropDownItems.Add(CreateMenuItem("Свойства...\tAlt+Enter", (_, _) => ShowProperties()));
        files.DropDownItems.Add(new ToolStripSeparator());
        files.DropDownItems.Add(CreateMenuItem("Копировать в буфер\tCtrl+C", (_, _) => CopySelectionToClipboard(false)));
        files.DropDownItems.Add(CreateMenuItem("Вырезать в буфер\tCtrl+X", (_, _) => CopySelectionToClipboard(true)));
        files.DropDownItems.Add(CreateMenuItem("Вставить из буфера\tCtrl+V", async (_, _) => await PasteFromClipboardAsync()));
        files.DropDownItems.Add(new ToolStripSeparator());
        files.DropDownItems.Add(CreateMenuItem("Выход\tAlt+F4", (_, _) => Close()));

        var selection = new ToolStripMenuItem("Выделение");
        selection.DropDownItems.Add(CreateMenuItem("Добавить выделение...\tNum+", (_, _) => ShowSelectionMaskDialog(true)));
        selection.DropDownItems.Add(CreateMenuItem("Убрать выделение...\tNum-", (_, _) => ShowSelectionMaskDialog(false)));
        selection.DropDownItems.Add(new ToolStripSeparator());
        selection.DropDownItems.Add(CreateMenuItem("Выделить всё\tCtrl+A / Num*", (_, _) => _activePanel.SelectAllItems()));
        selection.DropDownItems.Add(CreateMenuItem("Снять выделение", (_, _) => _activePanel.ClearSelection()));

        var commands = new ToolStripMenuItem("Команды");
        commands.DropDownItems.Add(CreateMenuItem("Избранные каталоги\tCtrl+D", (_, _) => _activePanel.ShowFavoritesMenu()));
        commands.DropDownItems.Add(new ToolStripSeparator());
        commands.DropDownItems.Add(CreateMenuItem("Поиск\tCtrl+F", (_, _) => ShowSearch()));
        commands.DropDownItems.Add(CreateMenuItem("Сравнить файлы побайтово", async (_, _) => await CompareSelectedFilesAsync()));
        commands.DropDownItems.Add(CreateMenuItem("Сравнить каталоги...", (_, _) => ShowDirectoryComparison()));
        commands.DropDownItems.Add(CreateMenuItem("Контрольные суммы...", (_, _) => ShowHashes()));
        commands.DropDownItems.Add(CreateMenuItem("Найти одинаковые файлы...", (_, _) => ShowDuplicateFinder()));
        commands.DropDownItems.Add(CreateMenuItem("Очередь операций...", (_, _) => ShowOperationQueue()));
        commands.DropDownItems.Add(new ToolStripSeparator());
        commands.DropDownItems.Add(CreateMenuItem("Упаковать ZIP", async (_, _) => await CreateZipAsync()));
        commands.DropDownItems.Add(CreateMenuItem("Распаковать ZIP", async (_, _) => await ExtractZipAsync()));

        var ftp = new ToolStripMenuItem("FTP");
        ftp.DropDownItems.Add(CreateMenuItem("Подключиться к FTP...", (_, _) => ShowFtpClient()));
        ftp.DropDownItems.Add(CreateMenuItem("Создать FTP сервер...", (_, _) => ShowFtpServer()));

        var view = new ToolStripMenuItem("Вид");
        view.DropDownItems.Add(CreateMenuItem("Обновить\tCtrl+R", (_, _) => RefreshPanels()));

        var settings = new ToolStripMenuItem("Настройки");
        settings.DropDownItems.Add(CreateMenuItem("Оформление...", (_, _) => ShowSettings()));
        settings.DropDownItems.Add(CreateMenuItem("Запомнить текущий вид", (_, _) => SaveCurrentSettings()));

        var help = new ToolStripMenuItem("Справка");
        help.DropDownItems.Add(CreateMenuItem("О программе", (_, _) => ShowAbout()));

        menu.Items.AddRange(new ToolStripItem[] { files, selection, commands, ftp, view, settings, help });
        return menu;
    }

    private ToolStrip BuildQuickLaunchBar()
    {
        _quickLaunchToolbar.Dock = DockStyle.Fill;
        _quickLaunchToolbar.GripStyle = ToolStripGripStyle.Hidden;
        _quickLaunchToolbar.ImageScalingSize = new Size(24, 24);
        _quickLaunchToolbar.Padding = new Padding(4, 2, 4, 2);
        _quickLaunchToolbar.AllowDrop = true;
        _quickLaunchToolbar.MouseUp += (_, args) =>
        {
            if (args.Button == MouseButtons.Right && _quickLaunchToolbar.GetItemAt(args.Location) is null)
            {
                ShowQuickLaunchEmptyMenu(_quickLaunchToolbar.PointToScreen(args.Location));
            }
        };
        _quickLaunchToolbar.DragEnter += (_, args) =>
        {
            args.Effect = args.Data?.GetDataPresent(DataFormats.FileDrop) == true
                ? DragDropEffects.Link
                : DragDropEffects.None;
        };
        _quickLaunchToolbar.DragDrop += (_, args) =>
        {
            if (args.Data?.GetData(DataFormats.FileDrop) is string[] paths)
            {
                AddQuickLaunchPaths(paths);
            }
        };

        RebuildQuickLaunchBar();
        return _quickLaunchToolbar;
    }

    private void RebuildQuickLaunchBar()
    {
        _quickLaunchToolbar.Items.Clear();

        _quickLaunchToolbar.Items.Add(CreateQuickButton("Обновить", ToolbarIconFactory.Refresh(), (_, _) => RefreshPanels()));
        _quickLaunchToolbar.Items.Add(CreateQuickButton("Вверх", ToolbarIconFactory.Up(), (_, _) => _activePanel.NavigateUp()));
        _quickLaunchToolbar.Items.Add(CreateQuickButton("Добавить выделение (Num+)", ToolbarIconFactory.SelectAdd(), (_, _) => ShowSelectionMaskDialog(true)));
        _quickLaunchToolbar.Items.Add(CreateQuickButton("Убрать выделение (Num-)", ToolbarIconFactory.SelectRemove(), (_, _) => ShowSelectionMaskDialog(false)));
        _quickLaunchToolbar.Items.Add(new ToolStripSeparator());
        _quickLaunchToolbar.Items.Add(CreateQuickButton("Просмотр текста (F3)", ToolbarIconFactory.View(), (_, _) => ViewText()));
        _quickLaunchToolbar.Items.Add(CreateQuickButton("Копировать (F5)", ToolbarIconFactory.Copy(), async (_, _) => await CopySelectedAsync()));
        _quickLaunchToolbar.Items.Add(CreateQuickButton("Переместить (F6)", ToolbarIconFactory.Move(), async (_, _) => await MoveSelectedAsync()));
        _quickLaunchToolbar.Items.Add(CreateQuickButton("Новая папка (F7)", ToolbarIconFactory.NewFolder(), (_, _) => CreateFolder()));
        _quickLaunchToolbar.Items.Add(CreateQuickButton("Удалить в корзину (F8/Del)", ToolbarIconFactory.Delete(), (_, _) => DeleteSelected(false)));
        _quickLaunchToolbar.Items.Add(new ToolStripSeparator());
        _quickLaunchToolbar.Items.Add(CreateQuickButton("Поиск (Ctrl+F)", ToolbarIconFactory.Search(), (_, _) => ShowSearch()));
        _quickLaunchToolbar.Items.Add(CreateQuickButton("Сравнить файлы побайтово", ToolbarIconFactory.Compare(), async (_, _) => await CompareSelectedFilesAsync()));
        _quickLaunchToolbar.Items.Add(CreateQuickButton("Упаковать ZIP", ToolbarIconFactory.ZipPack(), async (_, _) => await CreateZipAsync()));
        _quickLaunchToolbar.Items.Add(CreateQuickButton("Распаковать ZIP", ToolbarIconFactory.ZipExtract(), async (_, _) => await ExtractZipAsync()));
        _quickLaunchToolbar.Items.Add(CreateQuickButton("FTP подключение", ToolbarIconFactory.FtpClient(), (_, _) => ShowFtpClient()));
        _quickLaunchToolbar.Items.Add(CreateQuickButton("Создать FTP сервер", ToolbarIconFactory.FtpServer(), (_, _) => ShowFtpServer()));
        _quickLaunchToolbar.Items.Add(new ToolStripSeparator());

        foreach (var entry in _quickLaunchEntries.ToList())
        {
            if (!File.Exists(entry.Path) && !Directory.Exists(entry.Path))
            {
                continue;
            }

            _quickLaunchToolbar.Items.Add(CreateUserQuickButton(entry));
        }
    }

    private TableLayoutPanel BuildFunctionBar()
    {
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 7,
            RowCount = 1,
            Padding = new Padding(0)
        };

        for (var index = 0; index < 7; index++)
        {
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / 7F));
        }

        bar.Controls.Add(CreateBottomButton("F3 Просмотр", (_, _) => ViewText()), 0, 0);
        bar.Controls.Add(CreateBottomButton("F5 Копирование", async (_, _) => await CopySelectedAsync()), 1, 0);
        bar.Controls.Add(CreateBottomButton("F6 Перемещение", async (_, _) => await MoveSelectedAsync()), 2, 0);
        bar.Controls.Add(CreateBottomButton("F7 Каталог", (_, _) => CreateFolder()), 3, 0);
        bar.Controls.Add(CreateBottomButton("F8/Del Удаление", (_, _) => DeleteSelected(false)), 4, 0);
        bar.Controls.Add(CreateBottomButton("Ctrl+F Поиск", (_, _) => ShowSearch()), 5, 0);
        bar.Controls.Add(CreateBottomButton("Alt+F4 Выход", (_, _) => Close()), 6, 0);

        return bar;
    }

    private void WirePanels()
    {
        _leftPanel.ActivatedPanel += (_, _) => SetActivePanel(_leftPanel);
        _rightPanel.ActivatedPanel += (_, _) => SetActivePanel(_rightPanel);
        _leftPanel.PathChanged += (_, _) => UpdateStatus();
        _rightPanel.PathChanged += (_, _) => UpdateStatus();
        _leftPanel.RenameRequested += (_, args) => RenamePanelEntry(_leftPanel, args.Entry);
        _rightPanel.RenameRequested += (_, args) => RenamePanelEntry(_rightPanel, args.Entry);
        _leftPanel.FilesDropped += async (_, args) => await DropFilesIntoPanelAsync(args);
        _rightPanel.FilesDropped += async (_, args) => await DropFilesIntoPanelAsync(args);
        _leftPanel.ShellContextMenuRequested += (_, args) => ShowShellContextMenu(args);
        _rightPanel.ShellContextMenuRequested += (_, args) => ShowShellContextMenu(args);
        _leftPanel.FavoritesMenuRequested += (_, args) => ShowFavoriteDirectoriesMenu(_leftPanel, args.ScreenLocation);
        _rightPanel.FavoritesMenuRequested += (_, args) => ShowFavoriteDirectoriesMenu(_rightPanel, args.ScreenLocation);
        _leftPanel.FtpEntryOpenRequested += async (_, args) => await OpenFtpEntryAsync(_leftPanel, args.Entry);
        _rightPanel.FtpEntryOpenRequested += async (_, args) => await OpenFtpEntryAsync(_rightPanel, args.Entry);
        _leftPanel.FtpPathRequested += async (_, args) => await ChangeFtpDirectoryAsync(_leftPanel, args.Path);
        _rightPanel.FtpPathRequested += async (_, args) => await ChangeFtpDirectoryAsync(_rightPanel, args.Path);
        _leftPanel.FtpDisconnectRequested += (_, _) => DisconnectFtpPanel(_leftPanel);
        _rightPanel.FtpDisconnectRequested += (_, _) => DisconnectFtpPanel(_rightPanel);
    }

    private void LoadInitialPaths()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var left = Directory.Exists(_settings.LeftPanel.Path)
            ? _settings.LeftPanel.Path
            : Directory.Exists(documents) ? documents : userProfile;
        var right = Directory.Exists(_settings.RightPanel.Path)
            ? _settings.RightPanel.Path
            : Directory.Exists(Environment.CurrentDirectory) ? Environment.CurrentDirectory : left;

        _leftPanel.LoadPath(left);
        _rightPanel.LoadPath(right);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Tab:
            case Keys.Shift | Keys.Tab:
                ToggleActivePanel();
                return true;
            case Keys.Control | Keys.Shift | Keys.Enter:
                if (ActiveControl == _commandBox)
                {
                    InsertFocusedPathIntoCommandLine();
                    return true;
                }
                break;
            case Keys.F2:
                RenameSelected();
                return true;
            case Keys.F3:
                ViewText();
                return true;
            case Keys.F5:
                _ = CopySelectedAsync();
                return true;
            case Keys.F6:
                _ = MoveSelectedAsync();
                return true;
            case Keys.F7:
                CreateFolder();
                return true;
            case Keys.F8:
                DeleteSelected(false);
                return true;
            case Keys.Delete:
                if (!IsInputControlFocused())
                {
                    DeleteSelected(false);
                    return true;
                }
                break;
            case Keys.Insert:
                _activePanel.ToggleFocusedSelectionAndMoveNext();
                return true;
            case Keys.Space:
                if (!IsInputControlFocused())
                {
                    _ = _activePanel.ToggleFocusedSelectionAndCalculateDirectorySizeAsync();
                    return true;
                }
                break;
            case Keys.Add:
                ShowSelectionMaskDialog(true);
                return true;
            case Keys.Subtract:
                ShowSelectionMaskDialog(false);
                return true;
            case Keys.Multiply:
                _activePanel.SelectAllItems();
                return true;
            case Keys.BrowserBack:
                _activePanel.NavigateUp();
                return true;
            case Keys.BrowserForward:
                _activePanel.EnterFocusedDirectory();
                return true;
            case Keys.Shift | Keys.Delete:
                if (!IsInputControlFocused())
                {
                    DeleteSelected(true);
                    return true;
                }
                break;
            case Keys.Control | Keys.C:
            case Keys.Control | Keys.Insert:
                CopySelectionToClipboard(false);
                return true;
            case Keys.Control | Keys.X:
                CopySelectionToClipboard(true);
                return true;
            case Keys.Control | Keys.V:
            case Keys.Shift | Keys.Insert:
                _ = PasteFromClipboardAsync();
                return true;
            case Keys.Control | Keys.F:
                ShowSearch();
                return true;
            case Keys.Control | Keys.M:
                ShowBatchRename();
                return true;
            case Keys.Alt | Keys.Enter:
                ShowProperties();
                return true;
            case Keys.Control | Keys.R:
                RefreshPanels();
                return true;
            case Keys.Control | Keys.A:
                _activePanel.SelectAllItems();
                return true;
            case Keys.Control | Keys.D:
                _activePanel.ShowFavoritesMenu();
                return true;
            case Keys.Back:
                if (ActiveControl is not TextBox)
                {
                    _activePanel.NavigateUp();
                    return true;
                }
                break;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private bool IsInputControlFocused()
    {
        var focused = FindFocusedControl(this);
        return focused is TextBoxBase or ComboBox or NumericUpDown;
    }

    private static Control? FindFocusedControl(Control root)
    {
        if (root.Focused)
        {
            return root;
        }

        foreach (Control child in root.Controls)
        {
            var focused = FindFocusedControl(child);
            if (focused is not null)
            {
                return focused;
            }
        }

        return null;
    }

    private void SetActivePanel(FilePanel panel)
    {
        _activePanel = panel;
        _leftPanel.MarkActive(ReferenceEquals(panel, _leftPanel));
        _rightPanel.MarkActive(ReferenceEquals(panel, _rightPanel));
        UpdateStatus();
    }

    private void ToggleActivePanel()
    {
        SetActivePanel(PassivePanel);
        _activePanel.FocusList();
    }

    private void UpdateStatus()
    {
        _commandPathLabel.Text = FormatCommandPath(_activePanel.CommandPathText);
        _toolTip.SetToolTip(_commandPathLabel, _commandPathLabel.Text);
    }

    private void RefreshPanels()
    {
        RefreshPanel(_leftPanel);
        RefreshPanel(_rightPanel);
    }

    private void RefreshPanel(FilePanel panel)
    {
        if (panel.IsFtpMode)
        {
            _ = RefreshFtpPanelAsync(panel);
            return;
        }

        panel.RefreshList();
    }

    private void CenterSplitter()
    {
        if (_splitContainer.Width <= 10)
        {
            return;
        }

        _centeringSplitter = true;
        try
        {
            var availableWidth = _splitContainer.ClientSize.Width - _splitContainer.SplitterWidth;
            if (availableWidth > 0)
            {
                _splitContainer.SplitterDistance = availableWidth / 2;
            }
        }
        finally
        {
            _centeringSplitter = false;
        }
    }

    private void SetSplitterRatio(double ratio)
    {
        if (_splitContainer.Width <= 10)
        {
            return;
        }

        _centeringSplitter = true;
        try
        {
            var availableWidth = _splitContainer.ClientSize.Width - _splitContainer.SplitterWidth;
            if (availableWidth > 0)
            {
                _splitContainer.SplitterDistance = Math.Clamp((int)(availableWidth * ratio), 1, Math.Max(1, availableWidth - 1));
            }
        }
        finally
        {
            _centeringSplitter = false;
        }
    }

    private void ApplySavedSettings()
    {
        ApplyTheme();
        ApplySavedWindowBounds();
        _leftPanel.ApplyColumnWidths(_settings.LeftPanel.ColumnWidths);
        _rightPanel.ApplyColumnWidths(_settings.RightPanel.ColumnWidths);
    }

    private void ApplyTheme()
    {
        _leftPanel.ApplyTheme(_settings.Theme);
        _rightPanel.ApplyTheme(_settings.Theme);
        _leftPanel.MarkActive(ReferenceEquals(_activePanel, _leftPanel));
        _rightPanel.MarkActive(ReferenceEquals(_activePanel, _rightPanel));
    }

    private void ApplySavedWindowBounds()
    {
        if (_settings.Window.Width < MinimumSize.Width || _settings.Window.Height < MinimumSize.Height)
        {
            return;
        }

        var bounds = EnsureVisible(new Rectangle(_settings.Window.X, _settings.Window.Y, _settings.Window.Width, _settings.Window.Height));
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        if (_settings.Window.Maximized)
        {
            WindowState = FormWindowState.Maximized;
        }
    }

    private void SaveCurrentSettings()
    {
        var bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _settings.Window.X = bounds.X;
        _settings.Window.Y = bounds.Y;
        _settings.Window.Width = bounds.Width;
        _settings.Window.Height = bounds.Height;
        _settings.Window.Maximized = WindowState == FormWindowState.Maximized;

        var availableWidth = _splitContainer.ClientSize.Width - _splitContainer.SplitterWidth;
        _settings.Window.SplitterRatio = availableWidth > 0
            ? Math.Clamp((double)_splitContainer.SplitterDistance / availableWidth, 0.05D, 0.95D)
            : 0.5D;

        _settings.LeftPanel.Path = _leftPanel.SettingsPath;
        _settings.RightPanel.Path = _rightPanel.SettingsPath;
        _settings.LeftPanel.ColumnWidths = _leftPanel.GetColumnWidths();
        _settings.RightPanel.ColumnWidths = _rightPanel.GetColumnWidths();

        AppSettingsStore.Save(_settings);
    }

    private void ShowSettings()
    {
        using var dialog = new SettingsForm(_settings.Theme);
        dialog.ApplyRequested += (_, _) =>
        {
            _settings.Theme = dialog.Theme.Clone();
            ApplyTheme();
            SaveCurrentSettings();
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _settings.Theme = dialog.Theme.Clone();
            ApplyTheme();
            SaveCurrentSettings();
        }
    }

    private void ShowFavoriteDirectoriesMenu(FilePanel panel, Point screenLocation)
    {
        SetActivePanel(panel);

        if (_favoriteDirectoriesMenu is { IsDisposed: false } existingMenu)
        {
            existingMenu.Close(ToolStripDropDownCloseReason.CloseCalled);
            return;
        }

        var menu = new ContextMenuStrip();
        _favoriteDirectoriesMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_favoriteDirectoriesMenu, menu))
            {
                _favoriteDirectoriesMenu = null;
            }

            DisposeContextMenuLater(menu);
        };

        var favorites = _settings.FavoriteDirectories.ToList();
        var existingFavorites = favorites.Where(Directory.Exists).ToList();
        if (existingFavorites.Count == 0)
        {
            menu.Items.Add(new ToolStripMenuItem("(нет избранных каталогов)") { Enabled = false });
        }
        else
        {
            foreach (var path in existingFavorites)
            {
                var item = new ToolStripMenuItem(FavoriteDirectoryName(path))
                {
                    ToolTipText = path,
                    Image = ShellIconProvider.GetSmallIcon(path, true, false)
                };
                item.Click += (_, _) =>
                {
                    SetActivePanel(panel);
                    panel.LoadPath(path);
                    SaveCurrentSettings();
                };
                menu.Items.Add(item);
            }
        }

        menu.Items.Add(new ToolStripSeparator());

        var currentPath = panel.CurrentPath;
        var currentIsFavorite = favorites.Any(path => IsSamePath(path, currentPath));
        var addCurrent = new ToolStripMenuItem("+ Добавить текущий каталог")
        {
            Enabled = Directory.Exists(currentPath) && !currentIsFavorite
        };
        addCurrent.Click += (_, _) => AddFavoriteDirectory(currentPath);
        menu.Items.Add(addCurrent);

        var removeCurrent = new ToolStripMenuItem("- Удалить текущий каталог")
        {
            Enabled = currentIsFavorite
        };
        removeCurrent.Click += (_, _) => RemoveFavoriteDirectory(currentPath);
        menu.Items.Add(removeCurrent);

        if (favorites.Count > 0)
        {
            var removeMenu = new ToolStripMenuItem("Удалить из избранного");
            foreach (var path in favorites)
            {
                var item = new ToolStripMenuItem(FavoriteDirectoryName(path))
                {
                    ToolTipText = path,
                    Image = Directory.Exists(path) ? ShellIconProvider.GetSmallIcon(path, true, false) : null
                };
                item.Click += (_, _) => RemoveFavoriteDirectory(path);
                removeMenu.DropDownItems.Add(item);
            }

            menu.Items.Add(removeMenu);
        }

        menu.Show(screenLocation);
    }

    private void DisposeContextMenuLater(ContextMenuStrip menu)
    {
        if (menu.IsDisposed)
        {
            return;
        }

        if (!IsHandleCreated || IsDisposed || Disposing)
        {
            menu.Dispose();
            return;
        }

        try
        {
            BeginInvoke((Action)(() =>
            {
                if (!menu.IsDisposed)
                {
                    menu.Dispose();
                }
            }));
        }
        catch
        {
            if (!menu.IsDisposed)
            {
                menu.Dispose();
            }
        }
    }

    private void AddFavoriteDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            MessageBox.Show(this, "Текущий каталог не найден.", "Избранные каталоги", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var fullPath = Path.GetFullPath(path);
        if (_settings.FavoriteDirectories.Any(item => IsSamePath(item, fullPath)))
        {
            return;
        }

        _settings.FavoriteDirectories.Add(fullPath);
        SaveCurrentSettings();
    }

    private void RemoveFavoriteDirectory(string path)
    {
        _settings.FavoriteDirectories.RemoveAll(item => IsSamePath(item, path));
        SaveCurrentSettings();
    }

    private static string FavoriteDirectoryName(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var name = Path.GetFileName(trimmed);
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        return path;
    }

    private void ShowSelectionMaskDialog(bool mark)
    {
        using var dialog = new SelectionMaskForm(mark);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var changed = _activePanel.MarkByPattern(dialog.SelectedPattern, mark);
            if (changed == 0)
            {
                MessageBox.Show(
                    this,
                    mark ? "По этой маске новых элементов не выделено." : "По этой маске ничего не снято.",
                    dialog.Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, dialog.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ViewText()
    {
        if (_activePanel.IsFtpMode || _activePanel.IsArchiveMode)
        {
            MessageBox.Show(this, _activePanel.IsArchiveMode
                ? "Файл из ZIP сначала распакуйте в соседнюю панель клавишей F5."
                : "FTP-файл сначала скачайте в соседнюю панель клавишей F5.", "Просмотр", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var entry = _activePanel.MarkedOrFocusedEntries.FirstOrDefault() ?? _activePanel.FocusedEntry;
        if (entry is null || entry.IsDirectory)
        {
            MessageBox.Show(this, "Выберите текстовый файл.", "Просмотр", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var viewer = new TextViewerForm(entry.FullPath);
        viewer.ShowDialog(this);
    }

    private async Task CopySelectedAsync()
    {
        if (_activePanel.IsArchiveMode)
        {
            await ExtractArchiveSelectionAsync();
            return;
        }

        if (PassivePanel.IsArchiveMode)
        {
            await AddSelectionToArchiveAsync();
            return;
        }

        if (_activePanel.IsFtpMode || PassivePanel.IsFtpMode)
        {
            await CopyOrMoveWithFtpAsync(move: false);
            return;
        }

        var entries = GetSelectedEntries("Копирование");
        if (entries is null)
        {
            return;
        }

        var conflictPlan = await ResolveConflictsAsync(entries, PassivePanel.CurrentPath, "Копирование");
        if (conflictPlan is null)
        {
            return;
        }

        var targetDirectory = PassivePanel.CurrentPath;
        QueueOperation(
            "Копирование",
            $"{entries.Count} элемент(ов) -> {targetDirectory}",
            (progress, token) => FileOperations.CopyAsync(entries, targetDirectory, progress, token, conflictPlan),
            RefreshPanels);
    }

    private async Task MoveSelectedAsync()
    {
        if (_activePanel.IsArchiveMode || PassivePanel.IsArchiveMode)
        {
            MessageBox.Show(this, "Для архивов F5 копирует внутрь ZIP или распаковывает наружу. F6 не удаляет исходник.", "Архив", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_activePanel.IsFtpMode || PassivePanel.IsFtpMode)
        {
            await CopyOrMoveWithFtpAsync(move: true);
            return;
        }

        var entries = GetSelectedEntries("Перемещение");
        if (entries is null)
        {
            return;
        }

        var conflictPlan = await ResolveConflictsAsync(entries, PassivePanel.CurrentPath, "Перемещение");
        if (conflictPlan is null)
        {
            return;
        }

        var targetDirectory = PassivePanel.CurrentPath;
        QueueOperation(
            "Перемещение",
            $"{entries.Count} элемент(ов) -> {targetDirectory}",
            (progress, token) => FileOperations.MoveAsync(entries, targetDirectory, progress, token, conflictPlan),
            RefreshPanels);
    }

    private async Task ExtractArchiveSelectionAsync()
    {
        if (PassivePanel.IsFtpMode || PassivePanel.IsArchiveMode)
        {
            MessageBox.Show(this, "Распаковка из архива работает в соседнюю обычную локальную панель.", "Архив", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var entries = GetSelectedEntries("Распаковка архива");
        if (entries is null)
        {
            return;
        }

        var targetDirectory = PassivePanel.SettingsPath;
        if (!Directory.Exists(targetDirectory))
        {
            MessageBox.Show(this, "Соседняя локальная папка не найдена.", "Архив", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!ConfirmConflicts(entries, targetDirectory, "Распаковка архива"))
        {
            return;
        }

        QueueOperation(
            "Распаковка архива",
            $"{entries.Count} элемент(ов) -> {targetDirectory}",
            (progress, token) => FileOperations.ExtractArchiveEntriesAsync(entries, targetDirectory, progress, token),
            RefreshPanels);
        await Task.CompletedTask;
    }

    private async Task AddSelectionToArchiveAsync()
    {
        var entries = GetSelectedEntries("Добавление в ZIP");
        if (entries is null)
        {
            return;
        }

        await AddEntriesToArchiveAsync(PassivePanel, entries);
    }

    private async Task AddEntriesToArchiveAsync(FilePanel archivePanel, IReadOnlyList<FileSystemEntry> entries)
    {
        if (!archivePanel.IsArchiveMode || !FileOperations.IsEditableArchive(archivePanel.ArchivePath))
        {
            MessageBox.Show(this, "Добавление, удаление и переименование поддерживаются только внутри ZIP. 7z и RAR доступны для просмотра и распаковки.", "Архив", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var archivePath = archivePanel.ArchivePath;
        var internalPath = archivePanel.ArchiveInternalPath;
        if (entries.Any(entry => string.Equals(Path.GetFullPath(entry.FullPath), Path.GetFullPath(archivePath), StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, "Нельзя добавить ZIP-файл внутрь самого себя.", "ZIP", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        QueueOperation(
            "Добавление в ZIP",
            $"{entries.Count} элемент(ов) -> {Path.GetFileName(archivePath)}",
            (progress, token) => FileOperations.AddEntriesToZipAsync(archivePath, internalPath, entries, progress, token),
            archivePanel.RefreshList);
        await Task.CompletedTask;
    }

    private async Task RenameArchiveEntryAsync(FilePanel panel, FileSystemEntry entry)
    {
        if (!FileOperations.IsEditableArchive(panel.ArchivePath))
        {
            MessageBox.Show(this, "Переименование поддерживается только внутри ZIP.", "Архив", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var name = InputDialog.Show(this, "Переименовать в ZIP", "Новое имя:", entry.Name);
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, entry.Name, StringComparison.Ordinal))
        {
            return;
        }

        QueueOperation(
            "Переименование в ZIP",
            $"{entry.Name} -> {name}",
            (progress, token) => FileOperations.RenameZipEntryAsync(entry, name.Trim(), progress, token),
            panel.RefreshList);
        await Task.CompletedTask;
    }

    private void DeleteArchiveSelection()
    {
        var entries = _activePanel.MarkedOrFocusedEntries.Where(entry => entry.IsArchiveEntry && !entry.IsParent).ToList();
        if (entries.Count == 0)
        {
            return;
        }

        if (!FileOperations.IsEditableArchive(_activePanel.ArchivePath))
        {
            MessageBox.Show(this, "Удаление поддерживается только внутри ZIP.", "Архив", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (MessageBox.Show(this, $"Удалить из ZIP {entries.Count} элемент(ов)? Архив будет безопасно пересобран.", "Удаление из ZIP", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        var panel = _activePanel;
        QueueOperation(
            "Удаление из ZIP",
            $"Элементов: {entries.Count}",
            (progress, token) => FileOperations.DeleteZipEntriesAsync(entries, progress, token),
            panel.RefreshList);
    }

    private async Task CopyOrMoveWithFtpAsync(bool move)
    {
        if (_activePanel.IsFtpMode && PassivePanel.IsFtpMode)
        {
            MessageBox.Show(this, "Прямое копирование FTP -> FTP пока не поддержано.", move ? "FTP перемещение" : "FTP копирование", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_activePanel.IsFtpMode)
        {
            await DownloadFromFtpAsync(move);
            return;
        }

        if (PassivePanel.IsFtpMode)
        {
            await UploadToFtpAsync(move);
        }
    }

    private async Task DownloadFromFtpAsync(bool move)
    {
        var title = move ? "FTP перемещение: скачивание" : "FTP скачивание";
        if (!TryGetFtpSession(_activePanel, out var session, out _))
        {
            MessageBox.Show(this, "FTP-панель не подключена.", title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var entries = GetFtpSelectedEntries(_activePanel, title);
        if (entries is null)
        {
            return;
        }

        var targetDirectory = PassivePanel.SettingsPath;
        var ftpPanel = _activePanel;
        var localPanel = PassivePanel;
        if (!Directory.Exists(targetDirectory))
        {
            MessageBox.Show(this, "Соседняя локальная папка не найдена.", title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!ConfirmLocalConflicts(entries, targetDirectory, title))
        {
            return;
        }

        QueueOperation(title, $"{entries.Count} элемент(ов) -> {targetDirectory}", async (progress, token) =>
        {
            var completed = 0;
            var total = Math.Max(1, entries.Count);
            foreach (var entry in entries)
            {
                await DownloadFtpEntryAsync(session, entry, targetDirectory, progress, token, total, () => ++completed, countAsItem: true);
            }

            if (move)
            {
                foreach (var entry in entries)
                {
                    await DeleteFtpEntryAsync(session, entry, token);
                }
            }
        }, () =>
        {
            localPanel.RefreshList();
            _ = RefreshFtpPanelAsync(ftpPanel);
        });
        await Task.CompletedTask;
    }

    private async Task UploadToFtpAsync(bool move)
    {
        var title = move ? "FTP перемещение: закачка" : "FTP закачка";
        if (!TryGetFtpSession(PassivePanel, out var session, out _))
        {
            MessageBox.Show(this, "Соседняя FTP-панель не подключена.", title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var entries = GetSelectedEntries(title);
        if (entries is null)
        {
            return;
        }

        if (entries.Any(entry => entry.IsRemote))
        {
            MessageBox.Show(this, "Для FTP -> FTP используйте сначала локальную панель.", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!ConfirmFtpConflicts(entries, PassivePanel, title))
        {
            return;
        }

        var localPanel = _activePanel;
        var ftpPanel = PassivePanel;
        var remoteDirectory = ftpPanel.CurrentPath;
        QueueOperation(title, $"{entries.Count} элемент(ов) -> {remoteDirectory}", async (progress, token) =>
        {
            var completed = 0;
            var total = Math.Max(1, entries.Count);
            foreach (var entry in entries)
            {
                await UploadLocalEntryAsync(session, entry.FullPath, remoteDirectory, progress, token, total, () => ++completed, countAsItem: true);
            }

            if (move)
            {
                foreach (var entry in entries)
                {
                    DeleteLocalEntryAfterUpload(entry);
                }
            }
        }, () =>
        {
            localPanel.RefreshList();
            _ = RefreshFtpPanelAsync(ftpPanel);
        });
        await Task.CompletedTask;
    }

    private async Task DownloadFtpEntryAsync(
        IRemoteFileSession session,
        FileSystemEntry entry,
        string localDirectory,
        IProgress<OperationProgress> progress,
        CancellationToken token,
        int total,
        Func<int> completeItem,
        bool countAsItem)
    {
        token.ThrowIfCancellationRequested();
        progress.Report(new OperationProgress(0, total, "FTP: " + entry.FullPath));
        if (entry.IsDirectory)
        {
            var targetDirectory = Path.Combine(localDirectory, entry.Name);
            Directory.CreateDirectory(targetDirectory);
            foreach (var child in await session.ListAsync(entry.FullPath, token))
            {
                await DownloadFtpEntryAsync(session, CreateEntryFromFtp(child), targetDirectory, progress, token, total, completeItem, countAsItem: false);
            }
        }
        else
        {
            var remoteProgress = new Progress<RemoteTransferProgress>(item =>
                progress.Report(new OperationProgress(0, total, "Скачивание: " + item.Name, item.BytesTransferred, item.TotalBytes ?? 0)));
            await session.DownloadFileAsync(entry.FullPath, Path.Combine(localDirectory, entry.Name), remoteProgress, token);
        }

        if (countAsItem)
        {
            var current = completeItem();
            progress.Report(new OperationProgress(Math.Min(current, total), total, entry.Name));
        }
    }

    private async Task UploadLocalEntryAsync(
        IRemoteFileSession session,
        string localPath,
        string remoteDirectory,
        IProgress<OperationProgress> progress,
        CancellationToken token,
        int total,
        Func<int> completeItem,
        bool countAsItem)
    {
        token.ThrowIfCancellationRequested();
        progress.Report(new OperationProgress(0, total, "FTP: " + localPath));
        if (Directory.Exists(localPath))
        {
            var remotePath = FtpClientSession.CombineRemotePath(remoteDirectory, Path.GetFileName(localPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
            try
            {
                await session.CreateDirectoryAsync(remotePath, token);
            }
            catch
            {
                // Existing FTP directories are fine for recursive upload.
            }

            foreach (var file in Directory.EnumerateFiles(localPath))
            {
                await UploadLocalEntryAsync(session, file, remotePath, progress, token, total, completeItem, countAsItem: false);
            }

            foreach (var directory in Directory.EnumerateDirectories(localPath))
            {
                await UploadLocalEntryAsync(session, directory, remotePath, progress, token, total, completeItem, countAsItem: false);
            }
        }
        else
        {
            var remotePath = FtpClientSession.CombineRemotePath(remoteDirectory, Path.GetFileName(localPath));
            var remoteProgress = new Progress<RemoteTransferProgress>(item =>
                progress.Report(new OperationProgress(0, total, "Закачка: " + item.Name, item.BytesTransferred, item.TotalBytes ?? 0)));
            await session.UploadFileAsync(localPath, remotePath, remoteProgress, token);
        }

        if (countAsItem)
        {
            var current = completeItem();
            progress.Report(new OperationProgress(Math.Min(current, total), total, Path.GetFileName(localPath)));
        }
    }

    private IReadOnlyList<FileSystemEntry>? GetFtpSelectedEntries(FilePanel panel, string title)
    {
        var entries = panel.MarkedOrFocusedEntries
            .Where(entry => entry.IsRemote && !entry.IsParent)
            .ToList();
        if (entries.Count == 0)
        {
            MessageBox.Show(this, "Ничего не выделено на FTP-панели.", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        return entries;
    }

    private bool ConfirmLocalConflicts(IReadOnlyList<FileSystemEntry> entries, string localDirectory, string title)
    {
        var hasConflicts = entries.Any(entry =>
        {
            var target = Path.Combine(localDirectory, entry.Name);
            return entry.IsDirectory ? Directory.Exists(target) : File.Exists(target);
        });

        return !hasConflicts || MessageBox.Show(
            this,
            "В соседней локальной панели уже есть элементы с такими именами. Файлы будут заменены, папки объединены. Продолжить?",
            title,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) == DialogResult.Yes;
    }

    private bool ConfirmFtpConflicts(IReadOnlyList<FileSystemEntry> entries, FilePanel ftpPanel, string title)
    {
        var remoteNames = ftpPanel.Entries
            .Select(entry => entry.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasConflicts = entries.Any(entry => remoteNames.Contains(entry.Name));

        return !hasConflicts || MessageBox.Show(
            this,
            "В FTP-панели уже есть элементы с такими именами. Файлы будут заменены, папки объединены. Продолжить?",
            title,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) == DialogResult.Yes;
    }

    private async Task DeleteFtpEntryAsync(IRemoteFileSession session, FileSystemEntry entry, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (entry.IsDirectory)
        {
            foreach (var child in await session.ListAsync(entry.FullPath, token))
            {
                await DeleteFtpEntryAsync(session, CreateEntryFromFtp(child), token);
            }

            await session.RemoveDirectoryAsync(entry.FullPath, token);
        }
        else
        {
            await session.DeleteFileAsync(entry.FullPath, token);
        }
    }

    private static FileSystemEntry CreateEntryFromFtp(FtpRemoteEntry entry)
    {
        return new FileSystemEntry(
            entry.Name,
            entry.FullPath,
            entry.IsDirectory,
            entry.IsParent,
            entry.IsDirectory ? null : entry.Size,
            entry.Modified ?? DateTime.MinValue,
            entry.IsDirectory ? FileAttributes.Directory : FileAttributes.Archive,
            isRemote: true);
    }

    private static void DeleteLocalEntryAfterUpload(FileSystemEntry entry)
    {
        if (entry.IsDirectory)
        {
            Directory.Delete(entry.FullPath, recursive: true);
        }
        else
        {
            File.Delete(entry.FullPath);
        }
    }

    private async Task CompareSelectedFilesAsync()
    {
        if (_leftPanel.IsFtpMode || _rightPanel.IsFtpMode || _leftPanel.IsArchiveMode || _rightPanel.IsArchiveMode)
        {
            MessageBox.Show(this, "Побайтовое сравнение работает для обычных локальных файлов. FTP/ZIP-файл сначала скачайте или распакуйте.", "Сравнение файлов", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var left = GetSingleCompareFile(_leftPanel, "левой");
        if (left is null)
        {
            return;
        }

        var right = GetSingleCompareFile(_rightPanel, "правой");
        if (right is null)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        using var progressForm = new ProgressForm("Сравнение файлов");
        progressForm.CancelRequested += (_, _) => cancellation.Cancel();
        var progress = new Progress<OperationProgress>(progressForm.SetProgress);
        progressForm.ShowCentered(this);

        try
        {
            var result = await FileOperations.CompareFilesByBytesAsync(left.FullPath, right.FullPath, progress, cancellation.Token);
            progressForm.Close();
            ShowCompareResult(left, right, result);
        }
        catch (OperationCanceledException)
        {
            progressForm.Close();
            MessageBox.Show(this, "Сравнение отменено.", "Сравнение файлов", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            progressForm.Close();
            MessageBox.Show(this, ex.Message, "Сравнение файлов", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowDirectoryComparison()
    {
        if (_leftPanel.IsFtpMode || _rightPanel.IsFtpMode ||
            _leftPanel.IsArchiveMode || _rightPanel.IsArchiveMode ||
            _leftPanel.IsSearchMode || _rightPanel.IsSearchMode)
        {
            MessageBox.Show(
                this,
                "Сравнение каталогов работает между двумя обычными локальными панелями.",
                "Сравнение каталогов",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (!Directory.Exists(_leftPanel.CurrentPath) || !Directory.Exists(_rightPanel.CurrentPath))
        {
            MessageBox.Show(this, "Один из каталогов недоступен.", "Сравнение каталогов", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_directoryCompareForm is { IsDisposed: false })
        {
            _directoryCompareForm.Show();
            _directoryCompareForm.BringToFront();
            return;
        }

        var form = new DirectoryCompareForm(_leftPanel.CurrentPath, _rightPanel.CurrentPath);
        _directoryCompareForm = form;
        form.FormClosed += (_, _) => _directoryCompareForm = null;
        form.SynchronizationRequested += (_, request) =>
        {
            var direction = request.Direction == DirectorySyncDirection.LeftToRight ? "слева направо" : "справа налево";
            QueueOperation(
                "Синхронизация каталогов",
                $"{request.Items.Count} элемент(ов), {direction}",
                (progress, token) => DirectoryComparisonService.CopyAsync(request, progress, token),
                () =>
                {
                    RefreshPanels();
                    if (!form.IsDisposed)
                    {
                        _ = form.ReloadAsync();
                    }
                });
        };
        form.ShowCentered(this);
    }

    private async Task DropFilesIntoPanelAsync(FilePanelDropEventArgs args)
    {
        var entries = args.Paths
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Select(CreateEntryFromPath)
            .Where(entry => entry is not null)
            .Cast<FileSystemEntry>()
            .Where(entry => !entry.IsParent)
            .ToList();

        if (entries.Count == 0)
        {
            return;
        }

        if (_activePanel.IsArchiveMode)
        {
            await AddEntriesToArchiveAsync(_activePanel, entries);
            return;
        }

        if (!Directory.Exists(args.TargetDirectory))
        {
            MessageBox.Show(this, "Папка назначения не найдена.", "Drag && Drop", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        entries.RemoveAll(entry => IsSamePath(entry.FullPath, Path.Combine(args.TargetDirectory, entry.Name)));
        if (entries.Count == 0)
        {
            UpdateStatus();
            return;
        }

        var title = args.Effect == DragDropEffects.Move
            ? "Drag && Drop: перемещение"
            : "Drag && Drop: копирование";

        if (!ValidateDropTarget(entries, args.TargetDirectory, title))
        {
            return;
        }

        var conflictPlan = await ResolveConflictsAsync(entries, args.TargetDirectory, title);
        if (conflictPlan is null)
        {
            return;
        }

        QueueOperation(
            title,
            $"{entries.Count} элемент(ов) -> {args.TargetDirectory}",
            (progress, token) => args.Effect == DragDropEffects.Move
                ? FileOperations.MoveAsync(entries, args.TargetDirectory, progress, token, conflictPlan)
                : FileOperations.CopyAsync(entries, args.TargetDirectory, progress, token, conflictPlan),
            RefreshPanels);
        UpdateStatus();
        await Task.CompletedTask;
    }

    private void CreateFolder()
    {
        if (_activePanel.IsFtpMode)
        {
            _ = CreateFtpFolderAsync();
            return;
        }

        var name = InputDialog.Show(this, "Новая папка", "Имя папки:", "Новая папка");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        try
        {
            var path = Path.Combine(_activePanel.CurrentPath, name);
            Directory.CreateDirectory(path);
            _activePanel.RefreshList();
            _activePanel.SelectPath(path);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Новая папка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RenameSelected()
    {
        if (_activePanel.IsArchiveMode)
        {
            var archiveEntry = _activePanel.MarkedOrFocusedEntries.FirstOrDefault() ?? _activePanel.FocusedEntry;
            if (archiveEntry is not null && !archiveEntry.IsParent) _ = RenameArchiveEntryAsync(_activePanel, archiveEntry);
            return;
        }

        var entry = _activePanel.MarkedOrFocusedEntries.FirstOrDefault() ?? _activePanel.FocusedEntry;
        if (entry is null || entry.IsParent)
        {
            return;
        }

        if (_activePanel.IsFtpMode)
        {
            _ = RenameFtpEntryAsync(entry);
            return;
        }

        RenameEntry(entry);
    }

    private void ShowBatchRename()
    {
        if (_activePanel.IsFtpMode || _activePanel.IsArchiveMode)
        {
            MessageBox.Show(
                this,
                "Групповое переименование сейчас работает с локальными файлами и папками.",
                "Групповое переименование",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var entries = _activePanel.MarkedOrFocusedEntries.ToList();
        if (entries.Count == 0)
        {
            MessageBox.Show(this, "Ничего не выделено.", "Групповое переименование", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new BatchRenameForm(entries);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var plans = dialog.Plans.Where(plan => plan.HasChange).ToList();
        if (plans.Count == 0)
        {
            return;
        }

        var panel = _activePanel;
        QueueOperation(
            "Групповое переименование",
            $"Элементов: {plans.Count}",
            (progress, token) => BatchRenameEngine.ApplyAsync(plans, progress, token),
            () =>
            {
                if (panel.IsSearchMode)
                {
                    panel.LoadPath(panel.SettingsPath);
                }
                else
                {
                    panel.RefreshList();
                }

                if (plans.Count == 1)
                {
                    panel.SelectPath(plans[0].DestinationPath);
                }
            });
    }

    private async Task UndoBatchRenameAsync()
    {
        var history = BatchRenameStore.LoadHistory();
        if (history is null || history.Entries.Count == 0)
        {
            MessageBox.Show(this, "Нет сохранённого массового переименования для отмены.", "Групповое переименование", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var unavailable = history.Entries.FirstOrDefault(entry =>
            !(entry.IsDirectory ? Directory.Exists(entry.DestinationPath) : File.Exists(entry.DestinationPath)) ||
            File.Exists(entry.SourcePath) || Directory.Exists(entry.SourcePath));
        if (unavailable is not null)
        {
            MessageBox.Show(this, "Отмена невозможна: один из файлов уже перемещён, удалён или старое имя занято.\n\n" + unavailable.DestinationPath, "Групповое переименование", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (MessageBox.Show(this, $"Вернуть прежние имена для {history.Entries.Count} элемент(ов)?", "Отмена переименования", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        QueueOperation(
            "Отмена группового переименования",
            $"Элементов: {history.Entries.Count}",
            (progress, token) => BatchRenameEngine.UndoAsync(history.Entries, progress, token),
            () =>
            {
                BatchRenameStore.ClearHistory();
                RefreshPanels();
            });
        await Task.CompletedTask;
    }

    private void RenamePanelEntry(FilePanel panel, FileSystemEntry entry)
    {
        SetActivePanel(panel);
        if (panel.IsArchiveMode)
        {
            _ = RenameArchiveEntryAsync(panel, entry);
            return;
        }

        if (panel.IsFtpMode)
        {
            _ = RenameFtpEntryAsync(entry);
            return;
        }

        RenameEntry(entry);
    }

    private void RenameEntry(FileSystemEntry entry)
    {
        if (entry.IsParent)
        {
            return;
        }

        var name = InputDialog.Show(this, "Переименовать", "Новое имя:", entry.Name);
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, entry.Name, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var destination = Path.Combine(Path.GetDirectoryName(entry.FullPath) ?? _activePanel.CurrentPath, name);
            if (entry.IsDirectory)
            {
                Directory.Move(entry.FullPath, destination);
            }
            else
            {
                File.Move(entry.FullPath, destination);
            }

            _activePanel.RefreshList();
            _activePanel.SelectPath(destination);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Переименовать", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task CreateFtpFolderAsync()
    {
        if (!TryGetFtpSession(_activePanel, out var session, out _))
        {
            MessageBox.Show(this, "FTP-панель не подключена.", "FTP новая папка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var name = InputDialog.Show(this, "FTP новая папка", "Имя папки:", "Новая папка");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        await RunOperationAsync("FTP новая папка", async context =>
        {
            await session.CreateDirectoryAsync(FtpClientSession.CombineRemotePath(_activePanel.CurrentPath, name), context.CancellationToken);
        });

        await RefreshFtpPanelAsync(_activePanel);
        _activePanel.SelectPath(FtpClientSession.CombineRemotePath(_activePanel.CurrentPath, name));
    }

    private async Task RenameFtpEntryAsync(FileSystemEntry entry)
    {
        if (!TryGetFtpSession(_activePanel, out var session, out _))
        {
            MessageBox.Show(this, "FTP-панель не подключена.", "FTP переименование", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var name = InputDialog.Show(this, "FTP переименовать", "Новое имя:", entry.Name);
        if (string.IsNullOrWhiteSpace(name) || string.Equals(name, entry.Name, StringComparison.Ordinal))
        {
            return;
        }

        var newPath = FtpClientSession.CombineRemotePath(FtpClientSession.ParentRemotePath(entry.FullPath), name);
        await RunOperationAsync("FTP переименование", async context =>
        {
            await session.RenameAsync(entry.FullPath, newPath, context.CancellationToken);
        });

        await RefreshFtpPanelAsync(_activePanel);
        _activePanel.SelectPath(newPath);
    }

    private void DeleteSelected(bool permanent)
    {
        if (_activePanel.IsArchiveMode)
        {
            DeleteArchiveSelection();
            return;
        }

        if (_activePanel.IsFtpMode)
        {
            _ = DeleteFtpSelectedAsync();
            return;
        }

        var entries = GetSelectedEntries(permanent ? "Удаление безвозвратно" : "Удаление");
        if (entries is null)
        {
            return;
        }

        var message = permanent
            ? $"Удалить безвозвратно: {entries.Count} элемент(ов)?"
            : $"Переместить в корзину: {entries.Count} элемент(ов)?";
        if (MessageBox.Show(this, message, "Удаление", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        var panel = _activePanel;
        QueueOperation(
            permanent ? "Удаление безвозвратно" : "Удаление в корзину",
            $"Элементов: {entries.Count}",
            (progress, token) => FileOperations.DeleteAsync(entries, permanent, progress, token),
            panel.RefreshList);
    }

    private async Task DeleteFtpSelectedAsync()
    {
        if (!TryGetFtpSession(_activePanel, out var session, out _))
        {
            MessageBox.Show(this, "FTP-панель не подключена.", "FTP удаление", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var entries = GetFtpSelectedEntries(_activePanel, "FTP удаление");
        if (entries is null)
        {
            return;
        }

        if (MessageBox.Show(this, $"Удалить на FTP: {entries.Count} элемент(ов)?", "FTP удаление", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        var panel = _activePanel;
        QueueOperation("FTP удаление", $"Элементов: {entries.Count}", async (progress, token) =>
        {
            var completed = 0;
            var total = Math.Max(1, entries.Count);
            foreach (var entry in entries)
            {
                progress.Report(new OperationProgress(completed, total, "Удаление: " + entry.FullPath));
                await DeleteFtpEntryAsync(session, entry, token);
                completed++;
                progress.Report(new OperationProgress(completed, total, entry.Name));
            }
        }, () => _ = RefreshFtpPanelAsync(panel));
        await Task.CompletedTask;
    }

    private async Task CreateZipAsync()
    {
        if (_activePanel.IsFtpMode || PassivePanel.IsFtpMode || _activePanel.IsArchiveMode || PassivePanel.IsArchiveMode)
        {
            MessageBox.Show(this, "ZIP-операции пока работают только между локальными панелями.", "ZIP", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var entries = GetSelectedEntries("ZIP");
        if (entries is null)
        {
            return;
        }

        var zipPath = CreateUniqueZipPath(entries, PassivePanel.CurrentPath);
        var targetPanel = PassivePanel;
        QueueOperation(
            "Упаковка ZIP",
            zipPath,
            (progress, token) => FileOperations.CreateZipAsync(entries, zipPath, progress, token),
            () =>
            {
                RefreshPanels();
                targetPanel.SelectPath(zipPath);
            });
        await Task.CompletedTask;
    }

    private async Task ExtractZipAsync()
    {
        if (_activePanel.IsArchiveMode)
        {
            await ExtractArchiveSelectionAsync();
            return;
        }

        if (_activePanel.IsFtpMode || PassivePanel.IsFtpMode || PassivePanel.IsArchiveMode)
        {
            MessageBox.Show(this, "ZIP-операции пока работают только между локальными панелями.", "Распаковка ZIP", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var zipEntries = _activePanel.MarkedOrFocusedEntries
            .Where(entry => !entry.IsDirectory && FileOperations.IsArchiveFile(entry.FullPath))
            .ToList();

        if (zipEntries.Count == 0)
        {
            MessageBox.Show(this, "Выберите один или несколько архивов ZIP, 7z или RAR.", "Распаковка архива", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var question = zipEntries.Count == 1
            ? $"Распаковать архив в целевую панель?\n\n{PassivePanel.CurrentPath}"
            : $"Распаковать {zipEntries.Count} архива в отдельные папки целевой панели?\n\n{PassivePanel.CurrentPath}";

        if (MessageBox.Show(this, question, "Распаковка архива", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var paths = zipEntries.Select(entry => entry.FullPath).ToList();
        var targetDirectory = PassivePanel.CurrentPath;
        QueueOperation(
            "Распаковка архива",
            $"{paths.Count} архив(ов) -> {targetDirectory}",
            (progress, token) => FileOperations.ExtractZipAsync(paths, targetDirectory, progress, token),
            RefreshPanels);
    }

    private void ShowSearch()
    {
        if (_activePanel.IsFtpMode)
        {
            MessageBox.Show(this, "Поиск сейчас работает по локальным папкам. Для FTP используйте навигацию панели.", "Поиск", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var search = new SearchForm(_activePanel.CurrentPath);
        search.OpenRequested += path =>
        {
            var targetDirectory = Directory.Exists(path) ? Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar)) : Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(targetDirectory))
            {
                targetDirectory = path;
            }

            _activePanel.LoadPath(targetDirectory);
            _activePanel.SelectPath(path);
            _activePanel.FocusList();
        };
        search.FeedResultsRequested += (entries, caption) =>
        {
            _activePanel.LoadSearchResults(entries, caption);
            _activePanel.FocusList();
            UpdateStatus();
        };
        search.ShowDialog(this);
    }

    private void ShowHashes()
    {
        if (_activePanel.IsFtpMode || _activePanel.IsArchiveMode)
        {
            MessageBox.Show(this, "Контрольные суммы вычисляются для локальных файлов. Сначала скачайте или распакуйте выбранное.", "Контрольные суммы", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var files = _activePanel.MarkedOrFocusedEntries.Where(entry => !entry.IsDirectory && !entry.IsParent && File.Exists(entry.FullPath)).Select(entry => entry.FullPath).ToList();
        if (files.Count == 0)
        {
            MessageBox.Show(this, "Выберите один или несколько файлов.", "Контрольные суммы", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var form = new HashesForm(files);
        form.ShowDialog(this);
    }

    private void ShowDuplicateFinder()
    {
        if (_activePanel.IsFtpMode || _activePanel.IsArchiveMode)
        {
            MessageBox.Show(this, "Поиск одинаковых файлов работает в локальных каталогах.", "Поиск дубликатов", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var form = new DuplicateFinderForm(_activePanel.SettingsPath);
        form.OpenRequested += path =>
        {
            var directory = Path.GetDirectoryName(path);
            if (directory is null) return;
            _activePanel.LoadPath(directory);
            _activePanel.SelectPath(path);
            _activePanel.FocusList();
        };
        form.FeedResultsRequested += (entries, caption) =>
        {
            _activePanel.LoadSearchResults(entries, caption);
            _activePanel.FocusList();
            UpdateStatus();
        };
        form.ShowDialog(this);
    }

    private void ShowProperties()
    {
        if (_activePanel.IsFtpMode || _activePanel.IsArchiveMode)
        {
            MessageBox.Show(this, "Свойства с изменением дат и атрибутов доступны для локальных файлов и папок.", "Свойства", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var entry = _activePanel.FocusedEntry;
        if (entry is null || entry.IsParent)
        {
            return;
        }

        try
        {
            using var properties = new FilePropertiesForm(entry.FullPath);
            if (properties.ShowDialog(this) == DialogResult.OK)
            {
                _activePanel.RefreshList();
                _activePanel.SelectPath(entry.FullPath);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Свойства", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void ShowFtpClient()
    {
        var targetPanel = _activePanel;
        using var connectionManager = new FtpConnectionManagerForm(_settings.FtpConnections, _settings.FtpConnectionGroups, targetPanel.SettingsPath);
        var result = connectionManager.ShowDialog(this);
        if (connectionManager.ProfilesChanged)
        {
            _settings.FtpConnections = connectionManager.Profiles.Select(profile => profile.Clone()).ToList();
            _settings.FtpConnectionGroups = connectionManager.Groups.ToList();
            AppSettingsStore.Save(_settings);
        }

        if (result != DialogResult.OK || connectionManager.SelectedProfile is null)
        {
            return;
        }

        await ConnectFtpPanelAsync(targetPanel, connectionManager.SelectedProfile);
    }

    private async Task ConnectFtpPanelAsync(FilePanel panel, FtpConnectionProfile profile)
    {
        await RunOperationAsync("FTP подключение", async context =>
        {
            DisconnectFtpPanel(panel);
            IRemoteFileSession session = profile.Protocol == RemoteConnectionProtocol.Sftp
                ? new SftpClientSession()
                : new FtpClientSession();
            try
            {
                await session.ConnectAsync(CreateFtpOptions(profile), context.CancellationToken);

                if (!string.IsNullOrWhiteSpace(profile.RemoteDirectory) && profile.RemoteDirectory.Trim() != "/")
                {
                    await session.ChangeDirectoryAsync(profile.RemoteDirectory.Trim(), context.CancellationToken);
                }

                var list = await session.ListAsync(context.CancellationToken);
                _ftpSessions[panel] = session;
                _ftpProfiles[panel] = profile.Clone();
                panel.LoadFtpEntries(profile.Name, session.CurrentDirectory, list);
            }
            catch
            {
                session.Dispose();
                throw;
            }
        });

        SetActivePanel(panel);
        panel.FocusList();
    }

    private async Task RefreshFtpPanelAsync(FilePanel panel)
    {
        if (!TryGetFtpSession(panel, out var session, out var profile))
        {
            return;
        }

        try
        {
            var focusedPath = panel.FocusedEntry?.FullPath;
            var list = await session.ListAsync(CancellationToken.None);
            panel.LoadFtpEntries(profile.Name, session.CurrentDirectory, list, focusedPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "FTP обновление", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task ChangeFtpDirectoryAsync(FilePanel panel, string path)
    {
        if (!TryGetFtpSession(panel, out var session, out var profile))
        {
            return;
        }

        await RunOperationAsync("FTP переход", async context =>
        {
            var previousPath = session.CurrentDirectory;
            await session.ChangeDirectoryAsync(path, context.CancellationToken);
            var list = await session.ListAsync(context.CancellationToken);
            var selectPath = IsFtpParentNavigation(previousPath, session.CurrentDirectory) ? previousPath : null;
            panel.LoadFtpEntries(profile.Name, session.CurrentDirectory, list, selectPath);
        });

        panel.FocusList();
        UpdateStatus();
    }

    private async Task OpenFtpEntryAsync(FilePanel panel, FileSystemEntry entry)
    {
        if (entry.IsDirectory)
        {
            await ChangeFtpDirectoryAsync(panel, entry.FullPath);
            return;
        }

        MessageBox.Show(this, "FTP-файл можно скачать в соседнюю панель клавишей F5.", "FTP", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static bool IsFtpParentNavigation(string previousPath, string currentPath)
    {
        var normalizedPrevious = FtpClientSession.NormalizeRemotePath(previousPath);
        var normalizedCurrent = FtpClientSession.NormalizeRemotePath(currentPath);
        return !string.Equals(normalizedPrevious, normalizedCurrent, StringComparison.Ordinal) &&
            string.Equals(FtpClientSession.ParentRemotePath(normalizedPrevious), normalizedCurrent, StringComparison.Ordinal);
    }

    private void DisconnectFtpPanel(FilePanel panel)
    {
        if (_ftpSessions.Remove(panel, out var session))
        {
            session.Dispose();
        }

        _ftpProfiles.Remove(panel);
    }

    private void DisposeFtpSessions()
    {
        foreach (var session in _ftpSessions.Values)
        {
            session.Dispose();
        }

        _ftpSessions.Clear();
        _ftpProfiles.Clear();
    }

    private bool TryGetFtpSession(FilePanel panel, out IRemoteFileSession session, out FtpConnectionProfile profile)
    {
        if (_ftpSessions.TryGetValue(panel, out session!) &&
            _ftpProfiles.TryGetValue(panel, out profile!))
        {
            return true;
        }

        session = null!;
        profile = null!;
        return false;
    }

    private static FtpConnectionOptions CreateFtpOptions(FtpConnectionProfile profile)
    {
        return new FtpConnectionOptions
        {
            Host = profile.Host.Trim(),
            Port = profile.Port,
            UserName = profile.Anonymous ? "anonymous" : profile.UserName.Trim(),
            Password = profile.Anonymous ? "guest@" : profile.Password,
            UseTls = profile.Protocol == RemoteConnectionProtocol.FtpsExplicit,
            AcceptAnyCertificate = profile.AcceptAnyCertificate,
            ResumeTransfers = profile.ResumeTransfers,
            AutoReconnect = profile.AutoReconnect,
            SpeedLimitKbps = profile.SpeedLimitKbps
        };
    }

    private void ShowFtpServer()
    {
        using var ftpServer = new FtpServerForm(_activePanel.SettingsPath);
        ftpServer.ShowDialog(this);
    }

    private void ShowShellContextMenu(FilePanelShellContextMenuEventArgs args)
    {
        try
        {
            var commandInvoked = ShellContextMenu.Show(this, args.Paths, args.ScreenLocation);
            if (commandInvoked)
            {
                BeginInvoke(RefreshPanels);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Меню Windows", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RunCommandLine()
    {
        var command = _commandBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c " + command)
            {
                WorkingDirectory = Directory.Exists(_activePanel.SettingsPath) ? _activePanel.SettingsPath : Environment.CurrentDirectory,
                UseShellExecute = true
            });
            _commandBox.Clear();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Командная строка", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void InsertFocusedPathIntoCommandLine()
    {
        var entry = _activePanel.FocusedEntry;
        var path = !string.IsNullOrWhiteSpace(entry?.FullPath)
            ? entry.FullPath
            : _activePanel.CurrentPath;

        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        InsertTextIntoCommandLine(QuoteCommandArgument(path));
    }

    private void InsertTextIntoCommandLine(string text)
    {
        var start = _commandBox.SelectionStart;
        var length = _commandBox.SelectionLength;
        var before = _commandBox.Text[..start];
        var after = _commandBox.Text[(start + length)..];

        if (before.Length > 0 && !char.IsWhiteSpace(before[^1]) && !text.StartsWith(' '))
        {
            text = " " + text;
        }

        if (after.Length > 0 && !char.IsWhiteSpace(after[0]) && !text.EndsWith(' '))
        {
            text += " ";
        }

        _commandBox.Text = before + text + after;
        _commandBox.SelectionStart = before.Length + text.Length;
        _commandBox.SelectionLength = 0;
        _commandBox.Focus();
    }

    private static string QuoteCommandArgument(string value)
    {
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private IReadOnlyList<FileSystemEntry>? GetSelectedEntries(string title)
    {
        var entries = _activePanel.MarkedOrFocusedEntries;
        if (entries.Count == 0)
        {
            MessageBox.Show(this, "Ничего не выделено.", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        return entries;
    }

    private FileSystemEntry? GetSingleCompareFile(FilePanel panel, string panelName)
    {
        var entries = panel.MarkedOrFocusedEntries.ToList();
        if (entries.Count != 1)
        {
            MessageBox.Show(this, $"В {panelName} панели выберите ровно один файл.", "Сравнение файлов", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        var entry = entries[0];
        if (entry.IsDirectory)
        {
            MessageBox.Show(this, $"В {panelName} панели выбрана папка. Нужен файл.", "Сравнение файлов", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return null;
        }

        return entry;
    }

    private void ShowCompareResult(FileSystemEntry left, FileSystemEntry right, FileCompareResult result)
    {
        if (result.AreEqual)
        {
            MessageBox.Show(
                this,
                $"Файлы одинаковы.\n\n{left.FullPath}\n{right.FullPath}\n\nРазмер: {FormatBytes(result.LeftLength)}",
                "Сравнение файлов",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var detail = result.FirstDifferenceOffset is long offset
            ? $"Первое отличие: байт {offset:N0} (0x{offset:X})."
            : $"Размеры отличаются: {FormatBytes(result.LeftLength)} и {FormatBytes(result.RightLength)}.";

        MessageBox.Show(
            this,
            $"Файлы отличаются.\n\n{left.FullPath}\n{right.FullPath}\n\n{detail}",
            "Сравнение файлов",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private void CopySelectionToClipboard(bool cut)
    {
        if (_activePanel.IsFtpMode || _activePanel.IsArchiveMode)
        {
            MessageBox.Show(this, _activePanel.IsArchiveMode
                ? "Буфер Windows работает с обычными локальными файлами. Из ZIP используйте F5 для распаковки."
                : "Буфер Windows работает с локальными файлами. Для FTP используйте F5/F6.", cut ? "Вырезать" : "Копировать", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var entries = GetSelectedEntries(cut ? "Вырезать" : "Копировать");
        if (entries is null)
        {
            return;
        }

        var paths = entries.Select(entry => entry.FullPath).ToArray();
        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, paths);
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 1)));
        Clipboard.SetDataObject(data, true);
        UpdateStatus();
    }

    private async Task PasteFromClipboardAsync()
    {
        if (_activePanel.IsFtpMode || _activePanel.IsArchiveMode)
        {
            MessageBox.Show(this, _activePanel.IsArchiveMode
                ? "Вставка внутрь ZIP пока не поддержана."
                : "Вставка из буфера в FTP пока не поддержана. Для закачки откройте FTP в соседней панели и нажмите F5 на локальных файлах.", "Вставка", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!Clipboard.ContainsFileDropList())
        {
            MessageBox.Show(this, "В буфере обмена нет файлов.", "Вставка", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var entries = Clipboard.GetFileDropList()
            .Cast<string>()
            .Where(path => File.Exists(path) || Directory.Exists(path))
            .Select(CreateEntryFromPath)
            .Where(entry => entry is not null)
            .Cast<FileSystemEntry>()
            .ToList();

        if (entries.Count == 0)
        {
            MessageBox.Show(this, "Файлы из буфера обмена не найдены.", "Вставка", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var conflictPlan = await ResolveConflictsAsync(entries, _activePanel.CurrentPath, "Вставка");
        if (conflictPlan is null)
        {
            return;
        }

        var move = GetClipboardDropEffect() == 2;
        var targetDirectory = _activePanel.CurrentPath;
        QueueOperation(
            move ? "Вставка с перемещением" : "Вставка с копированием",
            $"{entries.Count} элемент(ов) -> {targetDirectory}",
            (progress, token) => move
                ? FileOperations.MoveAsync(entries, targetDirectory, progress, token, conflictPlan)
                : FileOperations.CopyAsync(entries, targetDirectory, progress, token, conflictPlan),
            RefreshPanels);
        await Task.CompletedTask;
    }

    private void ShowQuickLaunchEmptyMenu(Point screenLocation)
    {
        var menu = new ContextMenuStrip();
        menu.Closed += (_, _) => DisposeContextMenuLater(menu);
        menu.Items.Add("Добавить файл...", null, (_, _) => AddQuickLaunchByDialog());

        var focused = _activePanel.FocusedEntry;
        if (focused is { IsParent: false })
        {
            menu.Items.Add("Добавить текущий элемент", null, (_, _) => AddQuickLaunchPaths(new[] { focused.FullPath }));
        }

        if (_quickLaunchEntries.Count > 0)
        {
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Очистить быстрый запуск", null, (_, _) => ClearQuickLaunch());
        }

        menu.Show(screenLocation);
    }

    private void ShowQuickLaunchItemMenu(QuickLaunchEntry entry, Point screenLocation)
    {
        var menu = new ContextMenuStrip();
        menu.Closed += (_, _) => DisposeContextMenuLater(menu);
        menu.Items.Add("Запустить", null, (_, _) => LaunchQuickEntry(entry));
        menu.Items.Add("Открыть папку", null, (_, _) => OpenQuickEntryFolder(entry));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Убрать с панели", null, (_, _) => RemoveQuickLaunchEntry(entry));
        menu.Show(screenLocation);
    }

    private void AddQuickLaunchByDialog()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Добавить в быстрый запуск",
            Filter = "Все файлы (*.*)|*.*",
            Multiselect = true,
            InitialDirectory = _activePanel.CurrentPath
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            AddQuickLaunchPaths(dialog.FileNames);
        }
    }

    private void AddQuickLaunchPaths(IEnumerable<string> paths)
    {
        var added = false;
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(path);
            if (_quickLaunchEntries.Any(entry => string.Equals(entry.Path, fullPath, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _quickLaunchEntries.Add(new QuickLaunchEntry { Path = fullPath });
            added = true;
        }

        if (!added)
        {
            return;
        }

        SaveAndRefreshQuickLaunch();
    }

    private void RemoveQuickLaunchEntry(QuickLaunchEntry entry)
    {
        _quickLaunchEntries.RemoveAll(item => string.Equals(item.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
        SaveAndRefreshQuickLaunch();
    }

    private void ClearQuickLaunch()
    {
        if (MessageBox.Show(this, "Очистить пользовательские значки быстрого запуска?", "Быстрый запуск", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _quickLaunchEntries.Clear();
        SaveAndRefreshQuickLaunch();
    }

    private void SaveAndRefreshQuickLaunch()
    {
        QuickLaunchStore.Save(_quickLaunchEntries);
        RebuildQuickLaunchBar();
    }

    private void LaunchQuickEntry(QuickLaunchEntry entry)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(entry.Path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Быстрый запуск", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenQuickEntryFolder(QuickLaunchEntry entry)
    {
        try
        {
            var directory = Directory.Exists(entry.Path) ? entry.Path : Path.GetDirectoryName(entry.Path);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(directory) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Быстрый запуск", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool ConfirmConflicts(IReadOnlyList<FileSystemEntry> entries, string targetDirectory, string title)
    {
        if (!FileOperations.HasTopLevelConflicts(entries, targetDirectory))
        {
            return true;
        }

        return MessageBox.Show(
            this,
            "В целевой панели уже есть элементы с такими именами. Заменить существующие файлы?",
            title,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning) == DialogResult.Yes;
    }

    private async Task<FileConflictPlan?> ResolveConflictsAsync(IReadOnlyList<FileSystemEntry> entries, string targetDirectory, string title)
    {
        if (!FileOperations.HasTopLevelConflicts(entries, targetDirectory))
        {
            return new FileConflictPlan([]);
        }

        using var cancellation = new CancellationTokenSource();
        using var progressForm = new ProgressForm("Проверка совпадений");
        progressForm.CancelRequested += (_, _) => cancellation.Cancel();
        var progress = new Progress<OperationProgress>(progressForm.SetProgress);
        progressForm.ShowCentered(this);
        IReadOnlyList<FileConflict> conflicts;
        try
        {
            conflicts = await FileOperations.FindConflictsAsync(entries, targetDirectory, progress, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }
        finally
        {
            progressForm.Close();
        }

        return conflicts.Count == 0
            ? new FileConflictPlan([])
            : ConflictResolutionForm.Resolve(this, title, conflicts);
    }

    private bool ValidateDropTarget(IReadOnlyList<FileSystemEntry> entries, string targetDirectory, string title)
    {
        foreach (var entry in entries.Where(entry => entry.IsDirectory))
        {
            if (IsSameOrChildPath(targetDirectory, entry.FullPath))
            {
                MessageBox.Show(
                    this,
                    "Нельзя копировать или перемещать папку внутрь самой себя.",
                    title,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }
        }

        return true;
    }

    private static FileSystemEntry? CreateEntryFromPath(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                var directory = new DirectoryInfo(path);
                return new FileSystemEntry(directory.Name, directory.FullName, true, false, null, directory.LastWriteTime, directory.Attributes);
            }

            if (File.Exists(path))
            {
                var file = new FileInfo(path);
                return new FileSystemEntry(file.Name, file.FullName, false, false, file.Length, file.LastWriteTime, file.Attributes);
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static int GetClipboardDropEffect()
    {
        try
        {
            if (Clipboard.GetDataObject()?.GetData("Preferred DropEffect") is MemoryStream stream)
            {
                var bytes = stream.ToArray();
                return bytes.Length >= 4 ? BitConverter.ToInt32(bytes, 0) : 1;
            }
        }
        catch
        {
            return 1;
        }

        return 1;
    }

    private static bool IsSamePath(string first, string second)
    {
        try
        {
            return string.Equals(NormalizePathForCompare(first), NormalizePathForCompare(second), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSameOrChildPath(string candidatePath, string rootPath)
    {
        try
        {
            var candidate = EnsureTrailingSeparator(NormalizePathForCompare(candidatePath));
            var root = EnsureTrailingSeparator(NormalizePathForCompare(rootPath));
            return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string NormalizePathForCompare(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static string FormatCommandPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ">";
        }

        if (path.StartsWith("ftp:", StringComparison.OrdinalIgnoreCase))
        {
            var ftpPath = path.Replace('\\', '/');
            return ftpPath.EndsWith(":/", StringComparison.Ordinal)
                ? ftpPath + ">"
                : ftpPath.TrimEnd('/') + ">";
        }

        var root = Path.GetPathRoot(path);
        if (!string.IsNullOrWhiteSpace(root) &&
            string.Equals(
                path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase))
        {
            return root + ">";
        }

        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ">";
    }

    private static string CreateUniqueZipPath(IReadOnlyList<FileSystemEntry> entries, string targetDirectory)
    {
        var baseName = entries.Count == 1
            ? entries[0].IsDirectory ? entries[0].Name : Path.GetFileNameWithoutExtension(entries[0].Name)
            : "archive";

        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "archive";
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            baseName = baseName.Replace(invalid, '_');
        }

        var candidate = Path.Combine(targetDirectory, baseName + ".zip");
        var index = 2;
        while (File.Exists(candidate) || Directory.Exists(candidate))
        {
            candidate = Path.Combine(targetDirectory, $"{baseName} ({index}).zip");
            index++;
        }

        return candidate;
    }

    private static string FormatBytes(long value)
    {
        string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
        var size = (double)value;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value:N0} {units[unit]}" : $"{size:N1} {units[unit]}";
    }

    private async Task RunOperationAsync(string title, Func<OperationContext, Task> operation)
    {
        using var cancellation = new CancellationTokenSource();
        using var progressForm = new ProgressForm(title);
        progressForm.CancelRequested += (_, _) => cancellation.Cancel();
        var progress = new Progress<OperationProgress>(progressForm.SetProgress);
        progressForm.ShowCentered(this);

        try
        {
            await operation(new OperationContext(progress, cancellation.Token));
        }
        catch (OperationCanceledException)
        {
            MessageBox.Show(this, "Операция отменена.", title, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            progressForm.Close();
        }
    }

    private void QueueOperation(
        string title,
        string details,
        Func<IProgress<OperationProgress>, CancellationToken, Task> operation,
        Action? completed = null)
    {
        Action? safeCompleted = completed is null
            ? null
            : () => RunOnUiThread(completed);
        _operationQueue.Enqueue(title, details, operation, safeCompleted);
        ShowOperationQueue();
    }

    private void ShowOperationQueue()
    {
        if (_operationQueueForm is null || _operationQueueForm.IsDisposed)
        {
            _operationQueueForm = new OperationQueueForm(_operationQueue);
            _operationQueueForm.FormClosed += (_, _) => _operationQueueForm = null;
        }

        _operationQueueForm.ShowCentered(this);
    }

    private void RunOnUiThread(Action action)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(action);
        }
        else
        {
            action();
        }
    }

    private void ShowAbout()
    {
        MessageBox.Show(
            this,
            $"AZERTY Commander {BuildInfo.Version}\nPrivalov Oleg\nСборка: {BuildInfo.BuildTimeLocal}\n\nTab переключить панель\nF3 просмотр текста\nF5 копирование\nF6 перемещение\nF7 новая папка\nF8/Del удалить в корзину\nShift+Del удалить безвозвратно\nIns выделить и вниз\nNum+ добавить выделение по маске\nNum- убрать выделение по маске\nNum* выделить всё\nF2 или спокойный второй клик переименовать\nCtrl+M групповое переименование с предпросмотром\nПравый клик открывает меню Windows\nCtrl+C/Ctrl+Insert копировать\nCtrl+X вырезать\nCtrl+V/Shift+Insert вставить\nCtrl+D избранные каталоги\nCtrl+F поиск, найденное можно вывести в панель\nCtrl+Shift+Enter в командной строке вставляет полный путь\nСравнение файлов: левый против правого побайтово\nСравнение каталогов: различия и синхронизация в обе стороны\nОчередь операций: скорость, время, пауза и отмена\nDrag && Drop: обычный бросок копирует, Shift перемещает\nZIP: Enter открыть как папку, F5 распаковать выбранное\nFTP: подключение в активную панель, F5/F6 обмен с локальной панелью, keep-alive от простоя\nFTP сервер: обычный FTP без TLS",
            "О программе",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static ToolStripMenuItem CreateMenuItem(string text, EventHandler click)
    {
        var parts = text.Split('\t', 2);
        var item = new ToolStripMenuItem(parts[0]);
        if (parts.Length == 2)
        {
            item.ShortcutKeyDisplayString = parts[1];
        }

        item.Click += click;
        return item;
    }

    private static ToolStripButton CreateQuickButton(string tooltip, Image image, EventHandler click)
    {
        var button = new ToolStripButton
        {
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            Image = image,
            ToolTipText = tooltip,
            AutoToolTip = true,
            Margin = new Padding(2, 0, 2, 0)
        };
        button.Click += click;
        return button;
    }

    private ToolStripButton CreateUserQuickButton(QuickLaunchEntry entry)
    {
        var name = Path.GetFileName(entry.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name))
        {
            name = entry.Path;
        }

        var button = new ToolStripButton
        {
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            Image = ShellIconProvider.GetSmallIcon(entry.Path, Directory.Exists(entry.Path), false),
            ToolTipText = $"{name}\n{entry.Path}",
            AutoToolTip = true,
            Margin = new Padding(2, 0, 2, 0),
            Tag = entry
        };
        button.Click += (_, _) => LaunchQuickEntry(entry);
        button.MouseUp += (_, args) =>
        {
            if (args.Button == MouseButtons.Right)
            {
                ShowQuickLaunchItemMenu(entry, _quickLaunchToolbar.PointToScreen(new Point(button.Bounds.Left + args.X, button.Bounds.Top + args.Y)));
            }
        };
        return button;
    }

    private static Button CreateBottomButton(string text, EventHandler click)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            FlatStyle = FlatStyle.System,
            MinimumSize = new Size(0, 34)
        };
        button.Click += click;
        return button;
    }

    private static Rectangle EnsureVisible(Rectangle bounds)
    {
        foreach (var screen in Screen.AllScreens)
        {
            if (screen.WorkingArea.IntersectsWith(bounds))
            {
                return bounds;
            }
        }

        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1600, 900);
        var width = Math.Min(Math.Max(bounds.Width, 980), area.Width);
        var height = Math.Min(Math.Max(bounds.Height, 640), area.Height);
        return new Rectangle(area.Left + 20, area.Top + 20, width - 40, height - 40);
    }

    private static Size CalculateDefaultWindowSize()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1600, 900);
        var width = Math.Min(area.Width - 20, Math.Min(1800, Math.Max(1360, area.Width * 90 / 100)));
        var height = Math.Min(area.Height - 20, Math.Min(1050, Math.Max(820, area.Height * 88 / 100)));
        return new Size(Math.Max(980, width), Math.Max(640, height));
    }

    private sealed record OperationContext(IProgress<OperationProgress> Progress, CancellationToken CancellationToken);
}
