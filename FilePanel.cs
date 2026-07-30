using System.ComponentModel;
using System.Diagnostics;

namespace AzertyCommander;

internal sealed class FilePanel : UserControl
{
    private readonly ComboBox _driveBox = new();
    private readonly TextBox _pathBox = new();
    private readonly Label _spaceLabel = new();
    private readonly DataGridView _grid = new();
    private readonly Label _statusLabel = new();
    private readonly BindingList<FileSystemEntry> _entries = new();
    private readonly HashSet<string> _markedPaths = new(StringComparer.OrdinalIgnoreCase);
    private Point _dragStartPoint;
    private DateTime _lastRenameClickUtc = DateTime.MinValue;
    private string? _lastRenameClickPath;
    private AppThemeSettings _theme = new();
    private Font _fileFont = new("Segoe UI", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
    private Font _folderFont = new("Segoe UI", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
    private Color _fileTextColor = Color.Black;
    private Color _folderTextColor = Color.Black;
    private Color _markedTextColor = Color.Red;
    private Color _listBackgroundColor = Color.White;
    private Color _selectedBackgroundColor = SystemColors.Highlight;
    private Color _selectedTextColor = SystemColors.HighlightText;
    private Color _activePanelBackgroundColor = Color.FromArgb(212, 232, 247);
    private Color _activePathBackgroundColor = Color.FromArgb(232, 246, 255);
    private bool _isActivePanel;
    private bool _loadingDrives;
    private string _sortColumn = nameof(FileSystemEntry.DisplayName);
    private bool _sortAscending = true;
    private const string IconColumnName = "IconColumn";

    public FilePanel()
    {
        InitializeUi();
        ReloadDrives();
    }

    public event EventHandler? ActivatedPanel;
    public event EventHandler? PathChanged;
    public event EventHandler<FilePanelEntryEventArgs>? RenameRequested;
    public event EventHandler<FilePanelDropEventArgs>? FilesDropped;
    public event EventHandler<FilePanelShellContextMenuEventArgs>? ShellContextMenuRequested;

    public string CurrentPath { get; private set; } = string.Empty;

    public IReadOnlyList<FileSystemEntry> SelectedEntries
    {
        get
        {
            var entriesByPath = new Dictionary<string, FileSystemEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in _entries.Where(entry => !entry.IsParent && _markedPaths.Contains(entry.FullPath)))
            {
                entriesByPath[entry.FullPath] = entry;
            }

            var mouseSelected = _grid.SelectedRows
                .Cast<DataGridViewRow>()
                .OrderBy(row => row.Index)
                .Select(row => row.DataBoundItem)
                .OfType<FileSystemEntry>()
                .Where(entry => !entry.IsParent)
                .ToList();

            if (mouseSelected.Count > 1)
            {
                foreach (var entry in mouseSelected)
                {
                    entriesByPath[entry.FullPath] = entry;
                }
            }

            return entriesByPath.Values
                .OrderBy(entry => _entries.IndexOf(entry))
                .ToList();
        }
    }

    public FileSystemEntry? FocusedEntry => _grid.CurrentRow?.DataBoundItem as FileSystemEntry;

    public IReadOnlyList<FileSystemEntry> MarkedOrFocusedEntries
    {
        get
        {
            var marked = SelectedEntries;
            if (marked.Count > 0)
            {
                return marked;
            }

            return FocusedEntry is { IsParent: false } focused
                ? new[] { focused }
                : Array.Empty<FileSystemEntry>();
        }
    }

    public void LoadPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (File.Exists(path))
        {
            path = Path.GetDirectoryName(path) ?? path;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            MessageBox.Show(this, "Некорректный путь.", "Переход", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!Directory.Exists(fullPath))
        {
            MessageBox.Show(this, "Папка не найдена.", "Переход", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!string.Equals(CurrentPath, fullPath, StringComparison.OrdinalIgnoreCase))
        {
            _markedPaths.Clear();
        }

        CurrentPath = fullPath;
        RefreshList();
    }

    public void ApplyTheme(AppThemeSettings theme)
    {
        _theme = theme.Clone();
        _fileFont = CreateFont(_theme.FileFontFamily, _theme.FileFontSize, _theme.FileFontStyle);
        _folderFont = CreateFont(_theme.FolderFontFamily, _theme.FolderFontSize, _theme.FolderFontStyle);
        _fileTextColor = ColorTools.FromHtml(_theme.FileTextColor, Color.Black);
        _folderTextColor = ColorTools.FromHtml(_theme.FolderTextColor, Color.Black);
        _markedTextColor = ColorTools.FromHtml(_theme.MarkedTextColor, Color.Red);
        _listBackgroundColor = ColorTools.FromHtml(_theme.ListBackgroundColor, Color.White);
        _selectedBackgroundColor = ColorTools.FromHtml(_theme.SelectedBackgroundColor, SystemColors.Highlight);
        _selectedTextColor = ColorTools.FromHtml(_theme.SelectedTextColor, SystemColors.HighlightText);
        _activePanelBackgroundColor = ColorTools.FromHtml(_theme.ActivePanelBackgroundColor, Color.FromArgb(212, 232, 247));
        _activePathBackgroundColor = ColorTools.FromHtml(_theme.ActivePathBackgroundColor, Color.FromArgb(232, 246, 255));

        var fontHeight = Math.Ceiling(Math.Max(_fileFont.GetHeight(), _folderFont.GetHeight()));
        var rowHeight = Math.Max(28, Math.Max(_theme.RowHeight, (int)fontHeight + 8));
        _grid.RowTemplate.Height = rowHeight;
        foreach (DataGridViewRow row in _grid.Rows)
        {
            row.Height = rowHeight;
        }

        _grid.ColumnHeadersHeight = Math.Max(32, rowHeight + 2);
        _grid.BackgroundColor = _listBackgroundColor;
        _grid.DefaultCellStyle.Font = _fileFont;
        _grid.DefaultCellStyle.BackColor = _listBackgroundColor;
        _grid.DefaultCellStyle.ForeColor = _fileTextColor;
        ApplySelectionColors();
        _grid.ColumnHeadersDefaultCellStyle.Font = _fileFont;
        _grid.Invalidate();
    }

    public Dictionary<string, int> GetColumnWidths()
    {
        return _grid.Columns
            .Cast<DataGridViewColumn>()
            .Where(column => column.Visible)
            .ToDictionary(column => column.Name, column => column.Width, StringComparer.OrdinalIgnoreCase);
    }

    public void ApplyColumnWidths(Dictionary<string, int>? widths)
    {
        if (widths is null || widths.Count == 0)
        {
            return;
        }

        foreach (DataGridViewColumn column in _grid.Columns)
        {
            if (widths.TryGetValue(column.Name, out var width))
            {
                column.Width = Math.Clamp(width, Math.Max(24, column.MinimumWidth), 1200);
            }
        }
    }

    public void RefreshList()
    {
        if (string.IsNullOrWhiteSpace(CurrentPath) || !Directory.Exists(CurrentPath))
        {
            return;
        }

        var loaded = new List<FileSystemEntry>();
        var errors = new List<string>();

        try
        {
            var info = new DirectoryInfo(CurrentPath);
            if (info.Parent is not null)
            {
                loaded.Add(new FileSystemEntry("[..]", info.Parent.FullName, true, true, null, DateTime.MinValue, FileAttributes.Directory));
            }

            foreach (var directory in SafeEnumerateDirectories(info, errors))
            {
                loaded.Add(new FileSystemEntry(directory.Name, directory.FullName, true, false, null, directory.LastWriteTime, directory.Attributes));
            }

            foreach (var file in SafeEnumerateFiles(info, errors))
            {
                loaded.Add(new FileSystemEntry(file.Name, file.FullName, false, false, file.Length, file.LastWriteTime, file.Attributes));
            }
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
        }

        loaded = SortEntries(loaded);

        _entries.RaiseListChangedEvents = false;
        _entries.Clear();
        foreach (var entry in loaded)
        {
            _entries.Add(entry);
        }
        _entries.RaiseListChangedEvents = true;
        _entries.ResetBindings();
        _markedPaths.IntersectWith(loaded.Where(entry => !entry.IsParent).Select(entry => entry.FullPath));
        _grid.Invalidate();

        _pathBox.Text = CurrentPath;
        UpdateDriveSelection();
        UpdateSpaceLabel();
        UpdateStatus(errors.Count == 0 ? null : "Часть элементов не прочитана: " + errors[0]);

        PathChanged?.Invoke(this, EventArgs.Empty);
    }

    public void FocusList()
    {
        _grid.Focus();
        if (_grid.CurrentCell is null && _grid.Rows.Count > 0)
        {
            _grid.CurrentCell = _grid.Rows[0].Cells[nameof(FileSystemEntry.DisplayName)];
        }
    }

    public void SelectAllItems()
    {
        FocusList();
        foreach (var entry in _entries.Where(entry => !entry.IsParent))
        {
            _markedPaths.Add(entry.FullPath);
        }

        _grid.Invalidate();
        UpdateStatus(null);
    }

    public void ClearSelection()
    {
        _markedPaths.Clear();
        _grid.ClearSelection();
        _grid.Invalidate();
        UpdateStatus(null);
    }

    public bool SelectPath(string path)
    {
        var fileName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        for (var index = 0; index < _grid.Rows.Count; index++)
        {
            if (_grid.Rows[index].DataBoundItem is FileSystemEntry entry &&
                string.Equals(entry.Name, fileName, StringComparison.OrdinalIgnoreCase))
            {
                _grid.ClearSelection();
                _grid.Rows[index].Selected = true;
                _grid.CurrentCell = _grid.Rows[index].Cells[nameof(FileSystemEntry.DisplayName)];
                FocusList();
                return true;
            }
        }

        return false;
    }

    public void OpenFocusedEntry()
    {
        var entry = FocusedEntry;
        if (entry is null)
        {
            return;
        }

        if (entry.IsDirectory)
        {
            LoadPath(entry.FullPath);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(entry.FullPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Открытие файла", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    public void EnterFocusedDirectory()
    {
        if (FocusedEntry is { IsDirectory: true } entry)
        {
            LoadPath(entry.FullPath);
        }
    }

    public void NavigateUp()
    {
        var parent = Directory.GetParent(CurrentPath);
        if (parent is not null)
        {
            LoadPath(parent.FullName);
        }
    }

    public void ToggleFocusedSelectionAndMoveNext()
    {
        FocusList();
        if (_grid.CurrentRow is null || _grid.Rows.Count == 0)
        {
            return;
        }

        var currentRow = _grid.CurrentRow;
        if (currentRow.DataBoundItem is FileSystemEntry { IsParent: false } currentEntry)
        {
            if (!_markedPaths.Add(currentEntry.FullPath))
            {
                _markedPaths.Remove(currentEntry.FullPath);
            }
        }

        var nextIndex = Math.Min(currentRow.Index + 1, _grid.Rows.Count - 1);
        if (nextIndex != currentRow.Index)
        {
            _grid.CurrentCell = _grid.Rows[nextIndex].Cells[nameof(FileSystemEntry.DisplayName)];
        }

        _grid.ClearSelection();
        if (_grid.CurrentRow is not null)
        {
            _grid.CurrentRow.Selected = true;
        }

        _grid.Invalidate();
        UpdateStatus(null);
    }

    public void MarkActive(bool active)
    {
        _isActivePanel = active;
        BackColor = active ? _activePanelBackgroundColor : SystemColors.Control;
        _pathBox.BackColor = active ? _activePathBackgroundColor : SystemColors.Window;
        ApplySelectionColors();
        _grid.Invalidate();
    }

    private void ApplySelectionColors()
    {
        _grid.DefaultCellStyle.SelectionBackColor = _isActivePanel ? _selectedBackgroundColor : _listBackgroundColor;
        _grid.DefaultCellStyle.SelectionForeColor = _isActivePanel ? _selectedTextColor : _fileTextColor;
    }

    private void InitializeUi()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        Dock = DockStyle.Fill;
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(2)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        var driveRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            Margin = Padding.Empty
        };
        driveRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        driveRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _driveBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _driveBox.Dock = DockStyle.Fill;
        _driveBox.Margin = new Padding(0, 4, 8, 4);
        _driveBox.SelectedIndexChanged += (_, _) =>
        {
            if (!_loadingDrives && _driveBox.SelectedItem is DriveItem drive)
            {
                LoadPath(drive.Root);
            }
        };
        _driveBox.Enter += (_, _) => ActivatePanel();
        driveRow.Controls.Add(_driveBox, 0, 0);

        _spaceLabel.Dock = DockStyle.Fill;
        _spaceLabel.Margin = new Padding(0, 4, 0, 4);
        _spaceLabel.TextAlign = ContentAlignment.MiddleLeft;
        _spaceLabel.AutoEllipsis = true;
        driveRow.Controls.Add(_spaceLabel, 1, 0);

        _pathBox.Dock = DockStyle.Fill;
        _pathBox.Margin = new Padding(0, 2, 0, 4);
        _pathBox.BorderStyle = BorderStyle.FixedSingle;
        _pathBox.KeyDown += (_, args) =>
        {
            if (args.KeyCode == Keys.Enter)
            {
                args.SuppressKeyPress = true;
                LoadPath(_pathBox.Text);
            }
        };
        _pathBox.Enter += (_, _) => ActivatePanel();

        _grid.Dock = DockStyle.Fill;
        _grid.AllowDrop = true;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeColumns = true;
        _grid.AllowUserToResizeRows = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.BackgroundColor = SystemColors.Window;
        _grid.BorderStyle = BorderStyle.FixedSingle;
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.None;
        _grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _grid.ColumnHeadersHeight = 32;
        _grid.DataSource = _entries;
        _grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            Font = new Font("Segoe UI", 9.75F, FontStyle.Regular, GraphicsUnit.Point),
            Padding = new Padding(0),
            SelectionBackColor = SystemColors.Highlight,
            SelectionForeColor = SystemColors.HighlightText
        };
        _grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            Font = new Font("Segoe UI", 9.75F, FontStyle.Regular, GraphicsUnit.Point),
            Padding = new Padding(0)
        };
        _grid.EditMode = DataGridViewEditMode.EditProgrammatically;
        _grid.MultiSelect = true;
        _grid.ReadOnly = true;
        _grid.RowHeadersVisible = false;
        _grid.RowTemplate.Height = AppThemeSettings.DefaultRowHeight;
        _grid.ScrollBars = ScrollBars.Both;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.StandardTab = true;
        _grid.Columns.Add(new DataGridViewImageColumn
        {
            DataPropertyName = nameof(FileSystemEntry.SmallIcon),
            HeaderText = string.Empty,
            Name = IconColumnName,
            Width = 26,
            Resizable = DataGridViewTriState.False,
            ImageLayout = DataGridViewImageCellLayout.Normal,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(FileSystemEntry.DisplayName),
            HeaderText = "Имя",
            Name = nameof(FileSystemEntry.DisplayName),
            MinimumWidth = 180,
            Width = 420,
            Resizable = DataGridViewTriState.True
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(FileSystemEntry.TypeText),
            HeaderText = "Тип",
            Name = nameof(FileSystemEntry.TypeText),
            Width = 76,
            Resizable = DataGridViewTriState.True
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(FileSystemEntry.SizeText),
            HeaderText = "Размер",
            Name = nameof(FileSystemEntry.SizeText),
            Width = 96,
            Resizable = DataGridViewTriState.True,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleRight, Padding = new Padding(0) }
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(FileSystemEntry.DateText),
            HeaderText = "Дата",
            Name = nameof(FileSystemEntry.DateText),
            Width = 112,
            Resizable = DataGridViewTriState.True
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(FileSystemEntry.AttributesText),
            HeaderText = "Атрибуты",
            Name = nameof(FileSystemEntry.AttributesText),
            Width = 64,
            Resizable = DataGridViewTriState.True
        });
        _grid.CellDoubleClick += (_, args) =>
        {
            ResetSlowRenameClick();
            if (args.RowIndex >= 0)
            {
                OpenFocusedEntry();
            }
        };
        _grid.CellMouseClick += (_, args) => HandleGridCellMouseClick(args);
        _grid.CellFormatting += (_, args) =>
        {
            if (args.RowIndex < 0 || args.CellStyle is null || _grid.Rows[args.RowIndex].DataBoundItem is not FileSystemEntry entry)
            {
                return;
            }

            var marked = !entry.IsParent && _markedPaths.Contains(entry.FullPath);
            var normalTextColor = marked ? _markedTextColor : entry.IsDirectory ? _folderTextColor : _fileTextColor;

            args.CellStyle.Font = entry.IsDirectory ? _folderFont : _fileFont;
            args.CellStyle.BackColor = _listBackgroundColor;
            args.CellStyle.ForeColor = normalTextColor;
            args.CellStyle.SelectionBackColor = _isActivePanel ? _selectedBackgroundColor : _listBackgroundColor;
            args.CellStyle.SelectionForeColor = _isActivePanel
                ? marked ? _markedTextColor : _selectedTextColor
                : normalTextColor;
        };
        _grid.ColumnHeaderMouseClick += (_, args) =>
        {
            var columnName = _grid.Columns[args.ColumnIndex].Name;
            if (columnName == IconColumnName)
            {
                return;
            }

            if (_sortColumn == columnName)
            {
                _sortAscending = !_sortAscending;
            }
            else
            {
                _sortColumn = columnName;
                _sortAscending = true;
            }

            RefreshList();
        };
        _grid.Enter += (_, _) => ActivatePanel();
        MouseUp += (_, args) => HandleMouseNavigation(args);
        layout.MouseUp += (_, args) => HandleMouseNavigation(args);
        driveRow.MouseUp += (_, args) => HandleMouseNavigation(args);
        _driveBox.MouseUp += (_, args) => HandleMouseNavigation(args);
        _spaceLabel.MouseUp += (_, args) => HandleMouseNavigation(args);
        _pathBox.MouseUp += (_, args) => HandleMouseNavigation(args);
        _statusLabel.MouseUp += (_, args) => HandleMouseNavigation(args);
        _grid.MouseDown += (_, args) =>
        {
            ActivatePanel();
            if (args.Button == MouseButtons.Left)
            {
                _dragStartPoint = args.Location;
            }
        };
        _grid.MouseUp += (_, args) => HandleMouseNavigation(args);
        _grid.MouseMove += (_, args) =>
        {
            if ((args.Button & MouseButtons.Left) == 0)
            {
                return;
            }

            var dragSize = SystemInformation.DragSize;
            var dragBox = new Rectangle(
                _dragStartPoint.X - dragSize.Width / 2,
                _dragStartPoint.Y - dragSize.Height / 2,
                dragSize.Width,
                dragSize.Height);

            if (dragBox.Contains(args.Location))
            {
                return;
            }

            var paths = MarkedOrFocusedEntries.Select(entry => entry.FullPath).ToArray();
            if (paths.Length > 0)
            {
                var data = new DataObject(DataFormats.FileDrop, paths);
                var result = _grid.DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
                if (result is DragDropEffects.Move or DragDropEffects.Copy)
                {
                    RefreshList();
                }
            }
        };
        RegisterDropTarget(this, false);
        RegisterDropTarget(layout, false);
        RegisterDropTarget(driveRow, false);
        RegisterDropTarget(_driveBox, false);
        RegisterDropTarget(_spaceLabel, false);
        RegisterDropTarget(_pathBox, false);
        RegisterDropTarget(_grid, true);
        RegisterDropTarget(_statusLabel, false);
        _grid.SelectionChanged += (_, _) => UpdateStatus(null);
        _grid.KeyDown += (_, args) =>
        {
            if (args.KeyCode == Keys.Enter)
            {
                args.SuppressKeyPress = true;
                OpenFocusedEntry();
            }
            else if (args.KeyCode == Keys.Insert)
            {
                args.SuppressKeyPress = true;
                ToggleFocusedSelectionAndMoveNext();
            }
        };

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.Margin = new Padding(0, 3, 0, 3);
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.AutoEllipsis = true;

        layout.Controls.Add(driveRow, 0, 0);
        layout.Controls.Add(_pathBox, 0, 1);
        layout.Controls.Add(_grid, 0, 2);
        layout.Controls.Add(_statusLabel, 0, 3);
        Controls.Add(layout);
    }

    private void RegisterDropTarget(Control control, bool useGridHitTest)
    {
        control.AllowDrop = true;
        control.DragEnter += (_, args) => HandleFileDragOver(args, useGridHitTest);
        control.DragOver += (_, args) => HandleFileDragOver(args, useGridHitTest);
        control.DragDrop += (_, args) => HandleFileDragDrop(args, useGridHitTest);
    }

    private void HandleFileDragOver(DragEventArgs args, bool useGridHitTest)
    {
        ActivatePanel();
        if (!TryGetDroppedPaths(args, out var paths))
        {
            args.Effect = DragDropEffects.None;
            return;
        }

        var targetDirectory = GetDropTargetDirectory(args, useGridHitTest);
        args.Effect = ChooseDropEffect(args, paths, targetDirectory);
    }

    private void HandleFileDragDrop(DragEventArgs args, bool useGridHitTest)
    {
        ActivatePanel();
        if (!TryGetDroppedPaths(args, out var paths))
        {
            args.Effect = DragDropEffects.None;
            return;
        }

        var targetDirectory = GetDropTargetDirectory(args, useGridHitTest);
        var effect = ChooseDropEffect(args, paths, targetDirectory);
        args.Effect = effect;
        if (effect is not (DragDropEffects.Copy or DragDropEffects.Move))
        {
            return;
        }

        FilesDropped?.Invoke(this, new FilePanelDropEventArgs(paths, targetDirectory, effect));
    }

    private string GetDropTargetDirectory(DragEventArgs args, bool useGridHitTest)
    {
        if (!useGridHitTest)
        {
            return CurrentPath;
        }

        var clientPoint = _grid.PointToClient(new Point(args.X, args.Y));
        var hit = _grid.HitTest(clientPoint.X, clientPoint.Y);
        if (hit.RowIndex >= 0 &&
            _grid.Rows[hit.RowIndex].DataBoundItem is FileSystemEntry { IsDirectory: true } entry)
        {
            return entry.FullPath;
        }

        return CurrentPath;
    }

    private static bool TryGetDroppedPaths(DragEventArgs args, out string[] paths)
    {
        paths = Array.Empty<string>();
        if (args.Data?.GetDataPresent(DataFormats.FileDrop) != true ||
            args.Data.GetData(DataFormats.FileDrop) is not string[] droppedPaths)
        {
            return false;
        }

        paths = droppedPaths
            .Where(path => !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path)))
            .ToArray();
        return paths.Length > 0;
    }

    private static DragDropEffects ChooseDropEffect(DragEventArgs args, IReadOnlyList<string> paths, string targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory) || !Directory.Exists(targetDirectory))
        {
            return DragDropEffects.None;
        }

        var canCopy = (args.AllowedEffect & DragDropEffects.Copy) == DragDropEffects.Copy;
        var canMove = (args.AllowedEffect & DragDropEffects.Move) == DragDropEffects.Move;
        if (!canCopy && !canMove)
        {
            return DragDropEffects.None;
        }

        var shiftPressed = (args.KeyState & 4) == 4;
        var controlPressed = (args.KeyState & 8) == 8;
        if (shiftPressed && canMove)
        {
            return DragDropEffects.Move;
        }

        if (controlPressed && canCopy)
        {
            return DragDropEffects.Copy;
        }

        if (canMove && IsSameRoot(paths, targetDirectory))
        {
            return DragDropEffects.Move;
        }

        return canCopy ? DragDropEffects.Copy : DragDropEffects.Move;
    }

    private static bool IsSameRoot(IReadOnlyList<string> paths, string targetDirectory)
    {
        try
        {
            var targetRoot = Path.GetPathRoot(Path.GetFullPath(targetDirectory));
            return !string.IsNullOrWhiteSpace(targetRoot) &&
                paths.All(path => string.Equals(Path.GetPathRoot(Path.GetFullPath(path)), targetRoot, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private void ActivatePanel()
    {
        ActivatedPanel?.Invoke(this, EventArgs.Empty);
    }

    private void HandleSlowRenameClick(DataGridViewCellMouseEventArgs args)
    {
        if (args.Button != MouseButtons.Left ||
            args.Clicks != 1 ||
            args.RowIndex < 0 ||
            _grid.Rows[args.RowIndex].DataBoundItem is not FileSystemEntry { IsParent: false } entry)
        {
            ResetSlowRenameClick();
            return;
        }

        var now = DateTime.UtcNow;
        var elapsed = now - _lastRenameClickUtc;
        var doubleClickTimeout = TimeSpan.FromMilliseconds(SystemInformation.DoubleClickTime);
        var slowRenameTimeout = TimeSpan.FromMilliseconds(Math.Max(1200, SystemInformation.DoubleClickTime * 5));

        if (string.Equals(_lastRenameClickPath, entry.FullPath, StringComparison.OrdinalIgnoreCase) &&
            elapsed > doubleClickTimeout &&
            elapsed <= slowRenameTimeout)
        {
            ResetSlowRenameClick();
            RenameRequested?.Invoke(this, new FilePanelEntryEventArgs(entry));
            return;
        }

        _lastRenameClickPath = entry.FullPath;
        _lastRenameClickUtc = now;
    }

    private void HandleGridCellMouseClick(DataGridViewCellMouseEventArgs args)
    {
        if (args.Button == MouseButtons.Right)
        {
            HandleShellContextMenuClick(args);
            return;
        }

        HandleSlowRenameClick(args);
    }

    private void HandleShellContextMenuClick(DataGridViewCellMouseEventArgs args)
    {
        ResetSlowRenameClick();
        if (args.RowIndex < 0 ||
            _grid.Rows[args.RowIndex].DataBoundItem is not FileSystemEntry { IsParent: false } entry)
        {
            return;
        }

        ActivatePanel();
        var selectedEntries = SelectedEntries;
        var rowIsAlreadySelected = selectedEntries.Any(selected =>
            string.Equals(selected.FullPath, entry.FullPath, StringComparison.OrdinalIgnoreCase));

        if (!rowIsAlreadySelected)
        {
            _markedPaths.Clear();
            _grid.ClearSelection();
            _grid.Rows[args.RowIndex].Selected = true;
            _grid.CurrentCell = _grid.Rows[args.RowIndex].Cells[nameof(FileSystemEntry.DisplayName)];
            selectedEntries = new[] { entry };
            _grid.Invalidate();
            UpdateStatus(null);
        }
        else
        {
            _grid.CurrentCell = _grid.Rows[args.RowIndex].Cells[nameof(FileSystemEntry.DisplayName)];
        }

        var paths = selectedEntries
            .Where(selected => !selected.IsParent && (File.Exists(selected.FullPath) || Directory.Exists(selected.FullPath)))
            .Select(selected => selected.FullPath)
            .ToArray();

        if (paths.Length == 0)
        {
            return;
        }

        var screenLocation = _grid.PointToScreen(new Point(args.X, args.Y));
        ShellContextMenuRequested?.Invoke(this, new FilePanelShellContextMenuEventArgs(paths, screenLocation));
    }

    private void ResetSlowRenameClick()
    {
        _lastRenameClickPath = null;
        _lastRenameClickUtc = DateTime.MinValue;
    }

    private void HandleMouseNavigation(MouseEventArgs args)
    {
        if (args.Button == MouseButtons.XButton1)
        {
            NavigateUp();
        }
        else if (args.Button == MouseButtons.XButton2)
        {
            EnterFocusedDirectory();
        }
    }

    private void ReloadDrives()
    {
        _loadingDrives = true;
        _driveBox.Items.Clear();

        foreach (var drive in DriveInfo.GetDrives().OrderBy(drive => drive.Name))
        {
            _driveBox.Items.Add(new DriveItem(drive));
        }

        _loadingDrives = false;
    }

    private void UpdateDriveSelection()
    {
        var root = Path.GetPathRoot(CurrentPath);
        if (string.IsNullOrEmpty(root))
        {
            return;
        }

        _loadingDrives = true;
        for (var index = 0; index < _driveBox.Items.Count; index++)
        {
            if (_driveBox.Items[index] is DriveItem item &&
                string.Equals(item.Root, root, StringComparison.OrdinalIgnoreCase))
            {
                _driveBox.SelectedIndex = index;
                break;
            }
        }
        _loadingDrives = false;
    }

    private void UpdateSpaceLabel()
    {
        try
        {
            var root = Path.GetPathRoot(CurrentPath);
            if (string.IsNullOrWhiteSpace(root))
            {
                _spaceLabel.Text = string.Empty;
                return;
            }

            var drive = new DriveInfo(root);
            if (!drive.IsReady)
            {
                _spaceLabel.Text = "диск не готов";
                return;
            }

            _spaceLabel.Text = $"{drive.VolumeLabel}  {FormatKb(drive.AvailableFreeSpace)} Кб из {FormatKb(drive.TotalSize)} Кб свободно";
        }
        catch
        {
            _spaceLabel.Text = string.Empty;
        }
    }

    private void UpdateStatus(string? warning)
    {
        var files = _entries.Count(entry => !entry.IsParent && !entry.IsDirectory);
        var folders = _entries.Count(entry => !entry.IsParent && entry.IsDirectory);
        var bytes = _entries.Where(entry => !entry.IsDirectory && entry.Size is not null).Sum(entry => entry.Size!.Value);
        var selected = SelectedEntries;
        if (warning is null && selected.Count > 0)
        {
            var selectedFiles = selected.Count(entry => !entry.IsDirectory);
            var selectedFolders = selected.Count(entry => entry.IsDirectory);
            var selectedBytes = selected.Where(entry => !entry.IsDirectory && entry.Size is not null).Sum(entry => entry.Size!.Value);
            _statusLabel.Text = $"выделено: {FormatKb(selectedBytes)} Кб, файлов: {selectedFiles}, папок: {selectedFolders}";
            return;
        }

        _statusLabel.Text = warning ?? $"{FormatKb(bytes)} Кб, файлов: {files}, папок: {folders}";
    }

    private List<FileSystemEntry> SortEntries(List<FileSystemEntry> loaded)
    {
        var parents = loaded.Where(entry => entry.IsParent);
        var normal = loaded.Where(entry => !entry.IsParent);

        IOrderedEnumerable<FileSystemEntry> sorted = _sortColumn switch
        {
            nameof(FileSystemEntry.TypeText) => normal.OrderByDescending(entry => entry.IsDirectory).ThenBy(entry => entry.TypeText),
            nameof(FileSystemEntry.SizeText) => normal.OrderByDescending(entry => entry.IsDirectory).ThenBy(entry => entry.Size ?? -1),
            nameof(FileSystemEntry.DateText) => normal.OrderByDescending(entry => entry.IsDirectory).ThenBy(entry => entry.Modified),
            nameof(FileSystemEntry.AttributesText) => normal.OrderByDescending(entry => entry.IsDirectory).ThenBy(entry => entry.AttributesText),
            _ => normal.OrderByDescending(entry => entry.IsDirectory).ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
        };

        if (!_sortAscending)
        {
            sorted = _sortColumn switch
            {
                nameof(FileSystemEntry.TypeText) => normal.OrderByDescending(entry => entry.IsDirectory).ThenByDescending(entry => entry.TypeText),
                nameof(FileSystemEntry.SizeText) => normal.OrderByDescending(entry => entry.IsDirectory).ThenByDescending(entry => entry.Size ?? -1),
                nameof(FileSystemEntry.DateText) => normal.OrderByDescending(entry => entry.IsDirectory).ThenByDescending(entry => entry.Modified),
                nameof(FileSystemEntry.AttributesText) => normal.OrderByDescending(entry => entry.IsDirectory).ThenByDescending(entry => entry.AttributesText),
                _ => normal.OrderByDescending(entry => entry.IsDirectory).ThenByDescending(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            };
        }

        return parents.Concat(sorted).ToList();
    }

    private static IEnumerable<DirectoryInfo> SafeEnumerateDirectories(DirectoryInfo directory, List<string> errors)
    {
        try
        {
            return directory.EnumerateDirectories()
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
            return Array.Empty<DirectoryInfo>();
        }
    }

    private static IEnumerable<FileInfo> SafeEnumerateFiles(DirectoryInfo directory, List<string> errors)
    {
        try
        {
            return directory.EnumerateFiles()
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch (Exception ex)
        {
            errors.Add(ex.Message);
            return Array.Empty<FileInfo>();
        }
    }

    private static string FormatKb(long bytes)
    {
        return Math.Max(0, bytes / 1024).ToString("N0");
    }

    private static Font CreateFont(string family, float size, int style)
    {
        try
        {
            return new Font(family, Math.Clamp(size, 8F, 32F), (FontStyle)style, GraphicsUnit.Point);
        }
        catch
        {
            return new Font("Segoe UI", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
        }
    }

    private sealed class DriveItem
    {
        public DriveItem(DriveInfo drive)
        {
            Root = drive.Name;
            Text = BuildText(drive);
        }

        public string Root { get; }
        public string Text { get; }

        public override string ToString()
        {
            return Text;
        }

        private static string BuildText(DriveInfo drive)
        {
            try
            {
                return drive.Name.TrimEnd(Path.DirectorySeparatorChar);
            }
            catch
            {
                return drive.Name.TrimEnd(Path.DirectorySeparatorChar);
            }
        }
    }
}
