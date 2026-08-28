namespace AzertyCommander;

internal sealed class ConflictResolutionForm : Form
{
    private readonly FileConflict _conflict;
    private readonly CheckBox _applyAllBox = new();

    private ConflictResolutionForm(string title, FileConflict conflict, int index, int total)
    {
        _conflict = conflict;
        Text = title + " - совпадение имён";
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(720, 410);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        BuildUi(index, total);
    }

    public FileConflictAction? SelectedAction { get; private set; }
    public bool ApplyToAll => _applyAllBox.Checked;

    public static FileConflictPlan? Resolve(IWin32Window owner, string title, IReadOnlyList<FileConflict> conflicts)
    {
        var decisions = new List<(string DestinationPath, FileConflictAction Action)>();
        FileConflictAction? remainingAction = null;
        for (var index = 0; index < conflicts.Count; index++)
        {
            var conflict = conflicts[index];
            if (remainingAction is { } allAction)
            {
                decisions.Add((conflict.DestinationPath, allAction));
                continue;
            }

            using var form = new ConflictResolutionForm(title, conflict, index + 1, conflicts.Count);
            if (form.ShowDialog(owner) != DialogResult.OK || form.SelectedAction is null)
            {
                return null;
            }

            decisions.Add((conflict.DestinationPath, form.SelectedAction.Value));
            if (form.ApplyToAll)
            {
                remainingAction = form.SelectedAction;
            }
        }

        return new FileConflictPlan(decisions);
    }

    private void BuildUi(int index, int total)
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.Controls.Add(new Label
        {
            Text = $"В месте назначения уже есть элемент с таким именем ({index} из {total}).",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(Font, FontStyle.Bold),
            AutoEllipsis = true
        }, 0, 0);
        root.Controls.Add(BuildSide("Источник", _conflict.SourcePath, _conflict.SourceIsDirectory, _conflict.SourceSize, _conflict.SourceModified), 0, 1);
        root.Controls.Add(BuildSide("Назначение", _conflict.DestinationPath, _conflict.DestinationIsDirectory, _conflict.DestinationSize, _conflict.DestinationModified), 0, 2);
        _applyAllBox.Text = "Применить выбранное действие ко всем оставшимся совпадениям";
        _applyAllBox.Dock = DockStyle.Fill;
        _applyAllBox.UseMnemonic = false;
        root.Controls.Add(_applyAllBox, 0, 3);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 7, 0, 0) };
        var cancel = CreateButton("Отмена", 108, null);
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(CreateButton("Оставить оба", 142, FileConflictAction.KeepBoth));
        buttons.Controls.Add(CreateButton("Пропустить", 126, FileConflictAction.Skip));
        buttons.Controls.Add(CreateButton("Заменить", 126, FileConflictAction.Replace));
        root.Controls.Add(buttons, 0, 4);
        CancelButton = cancel;
        Controls.Add(root);
    }

    private Control BuildSide(string caption, string path, bool isDirectory, long? size, DateTime modified)
    {
        var group = new GroupBox { Text = caption, Dock = DockStyle.Fill };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(8, 4, 8, 4) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        var icon = new PictureBox { Image = ShellIconProvider.GetSmallIcon(path, isDirectory, false), SizeMode = PictureBoxSizeMode.CenterImage, Dock = DockStyle.Fill };
        layout.SetRowSpan(icon, 2);
        layout.Controls.Add(icon, 0, 0);
        layout.Controls.Add(new Label { Text = path, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }, 1, 0);
        var details = isDirectory ? $"Папка; изменена {modified:dd.MM.yyyy HH:mm:ss}" : $"{size.GetValueOrDefault():N0} байт; изменён {modified:dd.MM.yyyy HH:mm:ss}";
        layout.Controls.Add(new Label { Text = details, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }, 1, 1);
        group.Controls.Add(layout);
        return group;
    }

    private Button CreateButton(string text, int width, FileConflictAction? action)
    {
        var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(width, 36), Margin = new Padding(8, 2, 0, 2), UseMnemonic = false };
        if (action is not null)
        {
            button.Click += (_, _) =>
            {
                SelectedAction = action;
                DialogResult = DialogResult.OK;
                Close();
            };
        }
        return button;
    }
}
