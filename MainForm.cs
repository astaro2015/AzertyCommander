using Microsoft.VisualBasic.FileIO;

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
    private readonly List<QuickLaunchEntry> _quickLaunchEntries = QuickLaunchStore.Load();
    private ContextMenuStrip? _favoriteDirectoriesMenu;
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
        SaveCurrentSettings();
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
        files.DropDownItems.Add(new ToolStripSeparator());
        files.DropDownItems.Add(CreateMenuItem("Копировать\tF5", async (_, _) => await CopySelectedAsync()));
        files.DropDownItems.Add(CreateMenuItem("Переместить\tF6", async (_, _) => await MoveSelectedAsync()));
        files.DropDownItems.Add(CreateMenuItem("Удалить\tF8/Del", (_, _) => DeleteSelected(false)));
        files.DropDownItems.Add(CreateMenuItem("Удалить безвозвратно\tShift+Del", (_, _) => DeleteSelected(true)));
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
        selection.DropDownItems.Add(CreateMenuItem("Выделить всё\tCtrl+A", (_, _) => _activePanel.SelectAllItems()));
        selection.DropDownItems.Add(CreateMenuItem("Снять выделение", (_, _) => _activePanel.ClearSelection()));

        var commands = new ToolStripMenuItem("Команды");
        commands.DropDownItems.Add(CreateMenuItem("Избранные каталоги\tCtrl+D", (_, _) => _activePanel.ShowFavoritesMenu()));
        commands.DropDownItems.Add(new ToolStripSeparator());
        commands.DropDownItems.Add(CreateMenuItem("Поиск\tCtrl+F", (_, _) => ShowSearch()));
        commands.DropDownItems.Add(CreateMenuItem("Сравнить файлы побайтово", async (_, _) => await CompareSelectedFilesAsync()));
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
        _leftPanel.RenameRequested += (_, args) => RenameEntry(args.Entry);
        _rightPanel.RenameRequested += (_, args) => RenameEntry(args.Entry);
        _leftPanel.FilesDropped += async (_, args) => await DropFilesIntoPanelAsync(args);
        _rightPanel.FilesDropped += async (_, args) => await DropFilesIntoPanelAsync(args);
        _leftPanel.ShellContextMenuRequested += (_, args) => ShowShellContextMenu(args);
        _rightPanel.ShellContextMenuRequested += (_, args) => ShowShellContextMenu(args);
        _leftPanel.FavoritesMenuRequested += (_, args) => ShowFavoriteDirectoriesMenu(_leftPanel, args.ScreenLocation);
        _rightPanel.FavoritesMenuRequested += (_, args) => ShowFavoriteDirectoriesMenu(_rightPanel, args.ScreenLocation);
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
        _commandPathLabel.Text = FormatCommandPath(_activePanel.CurrentPath);
        _toolTip.SetToolTip(_commandPathLabel, _commandPathLabel.Text);
    }

    private void RefreshPanels()
    {
        _leftPanel.RefreshList();
        _rightPanel.RefreshList();
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

        _settings.LeftPanel.Path = _leftPanel.CurrentPath;
        _settings.RightPanel.Path = _rightPanel.CurrentPath;
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

            DisposeFavoriteMenuLater(menu);
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

    private void DisposeFavoriteMenuLater(ContextMenuStrip menu)
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
        var entries = GetSelectedEntries("Копирование");
        if (entries is null)
        {
            return;
        }

        if (!ConfirmConflicts(entries, PassivePanel.CurrentPath, "Копирование"))
        {
            return;
        }

        await RunOperationAsync("Копирование", token => FileOperations.CopyAsync(entries, PassivePanel.CurrentPath, token.Progress, token.CancellationToken));
        RefreshPanels();
    }

    private async Task MoveSelectedAsync()
    {
        var entries = GetSelectedEntries("Перемещение");
        if (entries is null)
        {
            return;
        }

        if (!ConfirmConflicts(entries, PassivePanel.CurrentPath, "Перемещение"))
        {
            return;
        }

        await RunOperationAsync("Перемещение", token => FileOperations.MoveAsync(entries, PassivePanel.CurrentPath, token.Progress, token.CancellationToken));
        RefreshPanels();
    }

    private async Task CompareSelectedFilesAsync()
    {
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

        if (!ConfirmConflicts(entries, args.TargetDirectory, title))
        {
            return;
        }

        await RunOperationAsync(
            title,
            token => args.Effect == DragDropEffects.Move
                ? FileOperations.MoveAsync(entries, args.TargetDirectory, token.Progress, token.CancellationToken)
                : FileOperations.CopyAsync(entries, args.TargetDirectory, token.Progress, token.CancellationToken));
        RefreshPanels();
        UpdateStatus();
    }

    private void CreateFolder()
    {
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
        var entry = _activePanel.MarkedOrFocusedEntries.FirstOrDefault() ?? _activePanel.FocusedEntry;
        if (entry is null || entry.IsParent)
        {
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

    private void DeleteSelected(bool permanent)
    {
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

        try
        {
            foreach (var entry in entries)
            {
                if (entry.IsDirectory)
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                        entry.FullPath,
                        UIOption.OnlyErrorDialogs,
                        RecycleOptionFor(permanent),
                        UICancelOption.ThrowException);
                }
                else
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                        entry.FullPath,
                        UIOption.OnlyErrorDialogs,
                        RecycleOptionFor(permanent),
                        UICancelOption.ThrowException);
                }
            }

            _activePanel.RefreshList();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Удаление", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async Task CreateZipAsync()
    {
        var entries = GetSelectedEntries("ZIP");
        if (entries is null)
        {
            return;
        }

        var zipPath = CreateUniqueZipPath(entries, PassivePanel.CurrentPath);
        await RunOperationAsync("Упаковка ZIP", token => FileOperations.CreateZipAsync(entries, zipPath, token.Progress, token.CancellationToken));
        RefreshPanels();
        PassivePanel.SelectPath(zipPath);
    }

    private async Task ExtractZipAsync()
    {
        var zipEntries = _activePanel.MarkedOrFocusedEntries
            .Where(entry => !entry.IsDirectory && string.Equals(Path.GetExtension(entry.FullPath), ".zip", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (zipEntries.Count == 0)
        {
            MessageBox.Show(this, "Выберите один или несколько ZIP-файлов.", "Распаковка ZIP", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var question = zipEntries.Count == 1
            ? $"Распаковать ZIP в целевую панель?\n\n{PassivePanel.CurrentPath}"
            : $"Распаковать {zipEntries.Count} ZIP-файла в отдельные папки целевой панели?\n\n{PassivePanel.CurrentPath}";

        if (MessageBox.Show(this, question, "Распаковка ZIP", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var paths = zipEntries.Select(entry => entry.FullPath).ToList();
        await RunOperationAsync("Распаковка ZIP", token => FileOperations.ExtractZipAsync(paths, PassivePanel.CurrentPath, token.Progress, token.CancellationToken));
        RefreshPanels();
    }

    private void ShowSearch()
    {
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
        search.ShowDialog(this);
    }

    private void ShowFtpClient()
    {
        using var connectionManager = new FtpConnectionManagerForm(_settings.FtpConnections, _settings.FtpConnectionGroups, _activePanel.CurrentPath);
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

        using var ftpClient = new FtpClientForm(connectionManager.SelectedProfile, () => _activePanel.CurrentPath, RefreshPanels);
        ftpClient.ShowDialog(this);
    }

    private void ShowFtpServer()
    {
        using var ftpServer = new FtpServerForm(_activePanel.CurrentPath);
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
                WorkingDirectory = _activePanel.CurrentPath,
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

        if (!ConfirmConflicts(entries, _activePanel.CurrentPath, "Вставка"))
        {
            return;
        }

        var move = GetClipboardDropEffect() == 2;
        await RunOperationAsync(
            move ? "Вставка с перемещением" : "Вставка с копированием",
            token => move
                ? FileOperations.MoveAsync(entries, _activePanel.CurrentPath, token.Progress, token.CancellationToken)
                : FileOperations.CopyAsync(entries, _activePanel.CurrentPath, token.Progress, token.CancellationToken));
        RefreshPanels();
    }

    private void ShowQuickLaunchEmptyMenu(Point screenLocation)
    {
        var menu = new ContextMenuStrip();
        menu.Closed += (_, _) => menu.Dispose();
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
        menu.Closed += (_, _) => menu.Dispose();
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

    private static RecycleOption RecycleOptionFor(bool permanent)
    {
        return permanent ? RecycleOption.DeletePermanently : RecycleOption.SendToRecycleBin;
    }

    private void ShowAbout()
    {
        MessageBox.Show(
            this,
            $"AZERTY Commander {BuildInfo.Version}\nPrivalov Oleg\nСборка: {BuildInfo.BuildTimeLocal}\n\nTab переключить панель\nF3 просмотр текста\nF5 копирование\nF6 перемещение\nF7 новая папка\nF8/Del удалить в корзину\nShift+Del удалить безвозвратно\nIns выделить и вниз\nNum+ добавить выделение по маске\nNum- убрать выделение по маске\nF2 или спокойный второй клик переименовать\nПравый клик открывает меню Windows\nCtrl+C/Ctrl+Insert копировать\nCtrl+X вырезать\nCtrl+V/Shift+Insert вставить\nCtrl+D избранные каталоги\nCtrl+F поиск\nCtrl+Shift+Enter в командной строке вставляет полный путь\nСравнение файлов: левый против правого побайтово\nDrag && Drop: Ctrl копировать, Shift перемещать\nFTP: клиент и встроенный сервер без TLS",
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
