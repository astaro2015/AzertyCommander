using System.ComponentModel;

namespace AzertyCommander;

internal sealed class BatchRenameForm : Form
{
    private readonly IReadOnlyList<FileSystemEntry> _entries;
    private readonly TextBox _maskBox = new() { Text = "[N]" };
    private readonly TextBox _searchBox = new();
    private readonly TextBox _replaceBox = new();
    private readonly TextBox _prefixBox = new();
    private readonly TextBox _suffixBox = new();
    private readonly ComboBox _caseBox = new();
    private readonly NumericUpDown _counterStartBox = new();
    private readonly NumericUpDown _counterDigitsBox = new();
    private readonly CheckBox _regexBox = new();
    private readonly ComboBox _presetBox = new();
    private readonly List<BatchRenamePreset> _presets = new();
    private readonly DataGridView _previewGrid = new();
    private readonly BindingList<BatchRenamePlan> _preview = new();
    private readonly Button _applyButton = new();
    private readonly Label _statusLabel = new();

    public BatchRenameForm(IReadOnlyList<FileSystemEntry> entries)
    {
        _entries = entries;
        Text = "Групповое переименование";
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(900, 620);
        ClientSize = new Size(1080, 720);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        BuildUi();
        ReloadPresets();
        UpdatePreview();
    }

    public IReadOnlyList<BatchRenamePlan> Plans => _preview.ToList();

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 254));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        root.Controls.Add(BuildOptionsPanel(), 0, 0);
        root.Controls.Add(BuildPreviewGrid(), 0, 1);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.AutoEllipsis = true;
        root.Controls.Add(_statusLabel, 0, 2);
        root.Controls.Add(BuildButtons(), 0, 3);
        Controls.Add(root);
    }

    private Control BuildOptionsPanel()
    {
        var group = new GroupBox { Text = "Правила", Dock = DockStyle.Fill };
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 6,
            Padding = new Padding(8, 10, 8, 6)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var row = 0; row < 6; row++)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        }

        AddLabeledControl(panel, "Маска имени:", _maskBox, 0, 0);
        AddLabeledControl(panel, "Регистр:", _caseBox, 2, 0);
        AddLabeledControl(panel, "Найти:", _searchBox, 0, 1);
        AddLabeledControl(panel, "Заменить:", _replaceBox, 2, 1);
        AddLabeledControl(panel, "Префикс:", _prefixBox, 0, 2);
        AddLabeledControl(panel, "Суффикс:", _suffixBox, 2, 2);
        AddLabeledControl(panel, "Счётчик с:", _counterStartBox, 0, 3);
        AddLabeledControl(panel, "Цифр:", _counterDigitsBox, 2, 3);

        _caseBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _caseBox.Items.AddRange(["Не менять", "строчные", "ПРОПИСНЫЕ", "Первая заглавная"]);
        _caseBox.SelectedIndex = 0;
        _counterStartBox.Minimum = 0;
        _counterStartBox.Maximum = 999999999;
        _counterStartBox.Value = 1;
        _counterDigitsBox.Minimum = 1;
        _counterDigitsBox.Maximum = 9;
        _counterDigitsBox.Value = 2;

        var hint = new Label
        {
            Text = "Шаблоны: [N] - старое имя, [E] - расширение, [C] - счётчик. Если [E] не указан, расширение сохраняется автоматически.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        panel.SetColumnSpan(hint, 3);
        panel.Controls.Add(hint, 0, 4);
        _regexBox.Text = "Регулярное выражение";
        _regexBox.Dock = DockStyle.Fill;
        _regexBox.TextAlign = ContentAlignment.MiddleLeft;
        _regexBox.UseMnemonic = false;
        panel.Controls.Add(_regexBox, 3, 4);

        panel.Controls.Add(new Label { Text = "Шаблон:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 5);
        _presetBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _presetBox.Dock = DockStyle.Fill;
        panel.Controls.Add(_presetBox, 1, 5);
        var presetButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = Padding.Empty };
        var savePreset = new Button { Text = "Сохранить...", AutoSize = true, MinimumSize = new Size(118, 30), Margin = new Padding(3, 2, 3, 2) };
        savePreset.Click += (_, _) => SavePreset();
        var deletePreset = new Button { Text = "Удалить", AutoSize = true, MinimumSize = new Size(96, 30), Margin = new Padding(3, 2, 3, 2) };
        deletePreset.Click += (_, _) => DeletePreset();
        presetButtons.Controls.Add(savePreset);
        presetButtons.Controls.Add(deletePreset);
        panel.SetColumnSpan(presetButtons, 2);
        panel.Controls.Add(presetButtons, 2, 5);

        foreach (var control in new Control[]
                 {
                     _maskBox, _searchBox, _replaceBox, _prefixBox, _suffixBox,
                     _caseBox, _counterStartBox, _counterDigitsBox
                 })
        {
            control.Dock = DockStyle.Fill;
        }

        _maskBox.TextChanged += (_, _) => UpdatePreview();
        _searchBox.TextChanged += (_, _) => UpdatePreview();
        _replaceBox.TextChanged += (_, _) => UpdatePreview();
        _prefixBox.TextChanged += (_, _) => UpdatePreview();
        _suffixBox.TextChanged += (_, _) => UpdatePreview();
        _caseBox.SelectedIndexChanged += (_, _) => UpdatePreview();
        _counterStartBox.ValueChanged += (_, _) => UpdatePreview();
        _counterDigitsBox.ValueChanged += (_, _) => UpdatePreview();
        _regexBox.CheckedChanged += (_, _) => UpdatePreview();
        _presetBox.SelectedIndexChanged += (_, _) => LoadSelectedPreset();

        group.Controls.Add(panel);
        return group;
    }

    private Control BuildPreviewGrid()
    {
        _previewGrid.Dock = DockStyle.Fill;
        _previewGrid.AutoGenerateColumns = false;
        _previewGrid.AllowUserToAddRows = false;
        _previewGrid.AllowUserToDeleteRows = false;
        _previewGrid.AllowUserToResizeRows = false;
        _previewGrid.ReadOnly = false;
        _previewGrid.RowHeadersVisible = false;
        _previewGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _previewGrid.MultiSelect = true;
        _previewGrid.BackgroundColor = SystemColors.Window;
        _previewGrid.BorderStyle = BorderStyle.FixedSingle;
        _previewGrid.DataSource = _preview;
        _previewGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(BatchRenamePlan.OldName),
            HeaderText = "Старое имя",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 44,
            ReadOnly = true
        });
        _previewGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(BatchRenamePlan.NewName),
            HeaderText = "Новое имя",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 44,
            ReadOnly = false
        });
        _previewGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(BatchRenamePlan.Status),
            HeaderText = "Состояние",
            Width = 170,
            ReadOnly = true
        });
        _previewGrid.DataError += (_, _) => { };
        _previewGrid.CellEndEdit += (_, args) =>
        {
            if (args.RowIndex < 0 || args.ColumnIndex != 1)
            {
                return;
            }

            BatchRenameEngine.ValidateManualPlans(_preview.ToList());
            _preview.ResetBindings();
            UpdateStatus();
        };
        _previewGrid.CellFormatting += (_, args) =>
        {
            if (args.RowIndex >= 0 && args.RowIndex < _preview.Count && _preview[args.RowIndex] is { IsValid: false })
            {
                _previewGrid.Rows[args.RowIndex].DefaultCellStyle.ForeColor = Color.Firebrick;
            }
        };
        return _previewGrid;
    }

    private Control BuildButtons()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 5, 0, 0)
        };
        var cancelButton = CreateDialogButton("Отмена", 112);
        cancelButton.DialogResult = DialogResult.Cancel;
        _applyButton.Text = "Переименовать";
        _applyButton.AutoSize = true;
        _applyButton.MinimumSize = new Size(142, 36);
        _applyButton.Margin = new Padding(8, 2, 0, 2);
        _applyButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };
        panel.Controls.Add(cancelButton);
        panel.Controls.Add(_applyButton);
        AcceptButton = _applyButton;
        CancelButton = cancelButton;
        return panel;
    }

    private void UpdatePreview()
    {
        if (!IsHandleCreated && _caseBox.SelectedIndex < 0)
        {
            return;
        }

        var options = CurrentOptions();
        IReadOnlyList<BatchRenamePlan> plans;
        try
        {
            plans = BatchRenameEngine.BuildPlans(_entries, options);
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "Ошибка шаблона: " + ex.Message;
            _applyButton.Enabled = false;
            return;
        }

        _preview.RaiseListChangedEvents = false;
        _preview.Clear();
        foreach (var plan in plans)
        {
            _preview.Add(plan);
        }
        _preview.RaiseListChangedEvents = true;
        _preview.ResetBindings();

        UpdateStatus();
    }

    private BatchRenameOptions CurrentOptions() => new(
        _maskBox.Text,
        _searchBox.Text,
        _replaceBox.Text,
        _prefixBox.Text,
        _suffixBox.Text,
        (BatchRenameCaseMode)Math.Max(0, _caseBox.SelectedIndex),
        Decimal.ToInt32(_counterStartBox.Value),
        Decimal.ToInt32(_counterDigitsBox.Value),
        _regexBox.Checked);

    private void UpdateStatus()
    {
        var errors = _preview.Count(plan => !plan.IsValid);
        var changes = _preview.Count(plan => plan.HasChange);
        _statusLabel.Text = errors > 0
            ? $"Ошибок: {errors}. Исправьте правила перед переименованием."
            : $"Будет переименовано: {changes} из {_preview.Count}. Новое имя можно исправить прямо в таблице.";
        _applyButton.Enabled = errors == 0 && changes > 0;
    }

    private void ReloadPresets(string? selectName = null)
    {
        _presets.Clear();
        _presets.AddRange(BatchRenameStore.LoadPresets());
        _presetBox.BeginUpdate();
        _presetBox.Items.Clear();
        _presetBox.Items.Add("(без шаблона)");
        foreach (var preset in _presets.OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)) _presetBox.Items.Add(preset.Name);
        _presetBox.EndUpdate();
        _presetBox.SelectedIndex = selectName is null ? 0 : Math.Max(0, _presetBox.Items.IndexOf(selectName));
    }

    private void SavePreset()
    {
        var name = InputDialog.Show(this, "Сохранить шаблон", "Имя шаблона:", _presetBox.SelectedIndex > 0 ? _presetBox.Text : "Новый шаблон");
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        var existing = _presets.FindIndex(item => string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase));
        var preset = new BatchRenamePreset(name, CurrentOptions());
        if (existing >= 0) _presets[existing] = preset; else _presets.Add(preset);
        BatchRenameStore.SavePresets(_presets);
        ReloadPresets(name);
    }

    private void DeletePreset()
    {
        if (_presetBox.SelectedIndex <= 0) return;
        _presets.RemoveAll(item => string.Equals(item.Name, _presetBox.Text, StringComparison.OrdinalIgnoreCase));
        BatchRenameStore.SavePresets(_presets);
        ReloadPresets();
    }

    private void LoadSelectedPreset()
    {
        if (_presetBox.SelectedIndex <= 0) return;
        var preset = _presets.FirstOrDefault(item => string.Equals(item.Name, _presetBox.Text, StringComparison.OrdinalIgnoreCase));
        if (preset is null) return;
        var options = preset.Options;
        _maskBox.Text = options.NameMask;
        _searchBox.Text = options.SearchText;
        _replaceBox.Text = options.ReplaceText;
        _prefixBox.Text = options.Prefix;
        _suffixBox.Text = options.Suffix;
        _caseBox.SelectedIndex = (int)options.CaseMode;
        _counterStartBox.Value = Math.Clamp(options.CounterStart, 0, Decimal.ToInt32(_counterStartBox.Maximum));
        _counterDigitsBox.Value = Math.Clamp(options.CounterDigits, 1, 9);
        _regexBox.Checked = options.UseRegex;
        UpdatePreview();
    }

    private static void AddLabeledControl(TableLayoutPanel panel, string text, Control control, int column, int row)
    {
        panel.Controls.Add(new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        }, column, row);
        panel.Controls.Add(control, column + 1, row);
    }

    private static Button CreateDialogButton(string text, int width)
    {
        return new Button
        {
            Text = text,
            AutoSize = true,
            MinimumSize = new Size(width, 36),
            Margin = new Padding(8, 2, 0, 2),
            UseMnemonic = false
        };
    }
}
