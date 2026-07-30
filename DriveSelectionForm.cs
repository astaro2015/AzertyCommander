namespace AzertyCommander;

internal sealed class DriveSelectionForm : Form
{
    private readonly CheckedListBox _drivesList = new();
    private readonly List<DriveOption> _drives = new();

    public DriveSelectionForm(IReadOnlyList<string> selectedRoots)
    {
        Text = "Выбор дисков";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(460, 360);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        BuildUi(selectedRoots);
    }

    public IReadOnlyList<string> SelectedRoots { get; private set; } = Array.Empty<string>();

    private void BuildUi(IReadOnlyList<string> selectedRoots)
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(8)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(new Label
        {
            Text = "Искать на дисках:",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _drivesList.Dock = DockStyle.Fill;
        _drivesList.CheckOnClick = true;
        _drivesList.HorizontalScrollbar = true;
        root.Controls.Add(_drivesList, 0, 1);

        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 3,
            Height = 116
        };
        buttons.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        buttons.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        buttons.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        buttons.Controls.Add(CreateButton("Все локальные", (_, _) => SelectFixedDrives()), 0, 0);
        buttons.Controls.Add(CreateButton("OK", (_, _) => AcceptSelection()), 0, 1);
        buttons.Controls.Add(CreateButton("Отмена", (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }), 0, 2);
        root.Controls.Add(buttons, 1, 1);

        Controls.Add(root);
        LoadDrives(selectedRoots);
    }

    private void LoadDrives(IReadOnlyList<string> selectedRoots)
    {
        var normalizedSelected = selectedRoots
            .Select(NormalizeRoot)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var drive in DriveInfo.GetDrives().Where(drive => drive.IsReady).OrderBy(drive => drive.Name))
        {
            var option = new DriveOption(drive.Name, FormatDrive(drive), drive.DriveType);
            _drives.Add(option);
            _drivesList.Items.Add(option, normalizedSelected.Contains(NormalizeRoot(drive.Name)));
        }

        if (_drivesList.CheckedItems.Count == 0)
        {
            SelectFixedDrives();
        }
    }

    private void SelectFixedDrives()
    {
        for (var index = 0; index < _drives.Count; index++)
        {
            _drivesList.SetItemChecked(index, _drives[index].DriveType == DriveType.Fixed);
        }
    }

    private void AcceptSelection()
    {
        SelectedRoots = _drivesList.CheckedItems
            .OfType<DriveOption>()
            .Select(drive => drive.Root)
            .ToList();

        if (SelectedRoots.Count == 0)
        {
            MessageBox.Show(this, "Выберите хотя бы один диск.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    private static string FormatDrive(DriveInfo drive)
    {
        var letter = drive.Name.TrimEnd(Path.DirectorySeparatorChar);
        return string.IsNullOrWhiteSpace(drive.VolumeLabel)
            ? letter
            : $"{letter}   {drive.VolumeLabel}";
    }

    private static string NormalizeRoot(string root)
    {
        try
        {
            return Path.GetPathRoot(Path.GetFullPath(root)) ?? root;
        }
        catch
        {
            return root;
        }
    }

    private static Button CreateButton(string text, EventHandler click)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(4, 3, 0, 3)
        };
        button.Click += click;
        return button;
    }

    private sealed class DriveOption
    {
        public DriveOption(string root, string displayText, DriveType driveType)
        {
            Root = root;
            DisplayText = displayText;
            DriveType = driveType;
        }

        public string Root { get; }
        public string DisplayText { get; }
        public DriveType DriveType { get; }
        public override string ToString() => DisplayText;
    }
}
