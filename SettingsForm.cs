namespace AzertyCommander;

internal sealed class SettingsForm : Form
{
    private readonly Label _fileFontLabel = new();
    private readonly Label _folderFontLabel = new();
    private readonly NumericUpDown _rowHeightBox = new();
    private readonly Dictionary<string, Button> _colorButtons = new(StringComparer.Ordinal);
    private Font _fileFont;
    private Font _folderFont;

    public SettingsForm(AppThemeSettings theme)
    {
        Theme = theme.Clone();
        _fileFont = CreateFont(Theme.FileFontFamily, Theme.FileFontSize, Theme.FileFontStyle);
        _folderFont = CreateFont(Theme.FolderFontFamily, Theme.FolderFontSize, Theme.FolderFontStyle);

        BuildUi();
        FillFromTheme();
    }

    public event EventHandler? ApplyRequested;
    public AppThemeSettings Theme { get; private set; }

    private void BuildUi()
    {
        Text = "Настройки";
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScroll = true;
        ClientSize = new Size(660, 610);
        MinimumSize = new Size(660, 610);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 156));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));

        root.Controls.Add(BuildFontGroup(), 0, 0);
        root.Controls.Add(BuildColorGroup(), 0, 1);
        root.Controls.Add(BuildButtons(), 0, 2);

        Controls.Add(root);
    }

    private Control BuildFontGroup()
    {
        var group = new GroupBox { Text = "Шрифты", Dock = DockStyle.Fill };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, Padding = new Padding(10, 14, 10, 10) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        grid.Controls.Add(new Label { Text = "Файлы:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        _fileFontLabel.Dock = DockStyle.Fill;
        _fileFontLabel.TextAlign = ContentAlignment.MiddleLeft;
        _fileFontLabel.AutoEllipsis = true;
        grid.Controls.Add(_fileFontLabel, 1, 0);
        grid.Controls.Add(CreateButton("Выбрать", (_, _) => ChooseFileFont()), 2, 0);

        grid.Controls.Add(new Label { Text = "Папки:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 1);
        _folderFontLabel.Dock = DockStyle.Fill;
        _folderFontLabel.TextAlign = ContentAlignment.MiddleLeft;
        _folderFontLabel.AutoEllipsis = true;
        grid.Controls.Add(_folderFontLabel, 1, 1);
        grid.Controls.Add(CreateButton("Выбрать", (_, _) => ChooseFolderFont()), 2, 1);

        grid.Controls.Add(new Label { Text = "Высота строки:", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 2);
        _rowHeightBox.Minimum = 24;
        _rowHeightBox.Maximum = 96;
        _rowHeightBox.Dock = DockStyle.Left;
        _rowHeightBox.Width = 104;
        grid.Controls.Add(_rowHeightBox, 1, 2);

        group.Controls.Add(grid);
        return group;
    }

    private Control BuildColorGroup()
    {
        var group = new GroupBox { Text = "Цвета", Dock = DockStyle.Fill };
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 8, Padding = new Padding(10, 14, 10, 10) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddColorButton(grid, 0, "Текст файлов", nameof(AppThemeSettings.FileTextColor));
        AddColorButton(grid, 1, "Текст папок", nameof(AppThemeSettings.FolderTextColor));
        AddColorButton(grid, 2, "Помеченные", nameof(AppThemeSettings.MarkedTextColor));
        AddColorButton(grid, 3, "Фон списка", nameof(AppThemeSettings.ListBackgroundColor));
        AddColorButton(grid, 4, "Фон выделения", nameof(AppThemeSettings.SelectedBackgroundColor));
        AddColorButton(grid, 5, "Текст выделения", nameof(AppThemeSettings.SelectedTextColor));
        AddColorButton(grid, 6, "Фон активной панели", nameof(AppThemeSettings.ActivePanelBackgroundColor));
        AddColorButton(grid, 7, "Фон активного пути", nameof(AppThemeSettings.ActivePathBackgroundColor));

        group.Controls.Add(grid);
        return group;
    }

    private Control BuildButtons()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            Padding = new Padding(0, 10, 0, 0),
            Margin = Padding.Empty
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        panel.Controls.Add(CreateDialogButton("Сбросить", 112, (_, _) =>
        {
            Theme = new AppThemeSettings();
            _fileFont = CreateFont(Theme.FileFontFamily, Theme.FileFontSize, Theme.FileFontStyle);
            _folderFont = CreateFont(Theme.FolderFontFamily, Theme.FolderFontSize, Theme.FolderFontStyle);
            FillFromTheme();
        }), 1, 0);
        panel.Controls.Add(CreateDialogButton("Применить", 124, (_, _) =>
        {
            SaveToTheme();
            ApplyRequested?.Invoke(this, EventArgs.Empty);
        }), 2, 0);
        panel.Controls.Add(CreateDialogButton("OK", 88, (_, _) =>
        {
            SaveToTheme();
            DialogResult = DialogResult.OK;
        }), 3, 0);
        panel.Controls.Add(CreateDialogButton("Отмена", 112, (_, _) => DialogResult = DialogResult.Cancel), 4, 0);

        return panel;
    }

    private void AddColorButton(TableLayoutPanel grid, int row, string label, string key)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        grid.Controls.Add(new Label { Text = label + ":", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        var button = CreateButton("", (_, _) => ChooseColor(key));
        button.Dock = DockStyle.Left;
        button.Width = 154;
        _colorButtons[key] = button;
        grid.Controls.Add(button, 1, row);
    }

    private void ChooseFileFont()
    {
        using var dialog = new FontDialog { Font = _fileFont, ShowEffects = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _fileFont = dialog.Font;
            UpdateFontLabels();
        }
    }

    private void ChooseFolderFont()
    {
        using var dialog = new FontDialog { Font = _folderFont, ShowEffects = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _folderFont = dialog.Font;
            UpdateFontLabels();
        }
    }

    private void ChooseColor(string key)
    {
        var current = GetThemeColor(key);
        using var dialog = new ColorDialog { Color = current, FullOpen = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            SetThemeColor(key, dialog.Color);
            FillColorButtons();
        }
    }

    private void FillFromTheme()
    {
        _rowHeightBox.Value = Math.Clamp(Theme.RowHeight, (int)_rowHeightBox.Minimum, (int)_rowHeightBox.Maximum);
        UpdateFontLabels();
        FillColorButtons();
    }

    private void UpdateFontLabels()
    {
        _fileFontLabel.Text = DescribeFont(_fileFont);
        _fileFontLabel.Font = _fileFont;
        _folderFontLabel.Text = DescribeFont(_folderFont);
        _folderFontLabel.Font = _folderFont;
    }

    private void FillColorButtons()
    {
        foreach (var (key, button) in _colorButtons)
        {
            var color = GetThemeColor(key);
            button.BackColor = color;
            button.Text = ColorTools.ToHtml(color);
            button.ForeColor = color.GetBrightness() < 0.45F ? Color.White : Color.Black;
        }
    }

    private void SaveToTheme()
    {
        Theme.FileFontFamily = _fileFont.FontFamily.Name;
        Theme.FileFontSize = _fileFont.Size;
        Theme.FileFontStyle = (int)_fileFont.Style;
        Theme.FolderFontFamily = _folderFont.FontFamily.Name;
        Theme.FolderFontSize = _folderFont.Size;
        Theme.FolderFontStyle = (int)_folderFont.Style;
        Theme.RowHeight = (int)_rowHeightBox.Value;
    }

    private Color GetThemeColor(string key)
    {
        return key switch
        {
            nameof(AppThemeSettings.FileTextColor) => ColorTools.FromHtml(Theme.FileTextColor, Color.Black),
            nameof(AppThemeSettings.FolderTextColor) => ColorTools.FromHtml(Theme.FolderTextColor, Color.Black),
            nameof(AppThemeSettings.MarkedTextColor) => ColorTools.FromHtml(Theme.MarkedTextColor, Color.Red),
            nameof(AppThemeSettings.ListBackgroundColor) => ColorTools.FromHtml(Theme.ListBackgroundColor, Color.White),
            nameof(AppThemeSettings.SelectedBackgroundColor) => ColorTools.FromHtml(Theme.SelectedBackgroundColor, SystemColors.Highlight),
            nameof(AppThemeSettings.SelectedTextColor) => ColorTools.FromHtml(Theme.SelectedTextColor, SystemColors.HighlightText),
            nameof(AppThemeSettings.ActivePanelBackgroundColor) => ColorTools.FromHtml(Theme.ActivePanelBackgroundColor, Color.FromArgb(212, 232, 247)),
            nameof(AppThemeSettings.ActivePathBackgroundColor) => ColorTools.FromHtml(Theme.ActivePathBackgroundColor, Color.FromArgb(232, 246, 255)),
            _ => Color.Black
        };
    }

    private void SetThemeColor(string key, Color color)
    {
        var html = ColorTools.ToHtml(color);
        switch (key)
        {
            case nameof(AppThemeSettings.FileTextColor):
                Theme.FileTextColor = html;
                break;
            case nameof(AppThemeSettings.FolderTextColor):
                Theme.FolderTextColor = html;
                break;
            case nameof(AppThemeSettings.MarkedTextColor):
                Theme.MarkedTextColor = html;
                break;
            case nameof(AppThemeSettings.ListBackgroundColor):
                Theme.ListBackgroundColor = html;
                break;
            case nameof(AppThemeSettings.SelectedBackgroundColor):
                Theme.SelectedBackgroundColor = html;
                break;
            case nameof(AppThemeSettings.SelectedTextColor):
                Theme.SelectedTextColor = html;
                break;
            case nameof(AppThemeSettings.ActivePanelBackgroundColor):
                Theme.ActivePanelBackgroundColor = html;
                break;
            case nameof(AppThemeSettings.ActivePathBackgroundColor):
                Theme.ActivePathBackgroundColor = html;
                break;
        }
    }

    private static Button CreateButton(string text, EventHandler click)
    {
        var width = string.Equals(text, "По умолчанию", StringComparison.Ordinal) ? 132 : 104;
        var button = new Button { Text = text, Width = width, Height = 32, MinimumSize = new Size(width, 32), Margin = new Padding(4, 2, 4, 2) };
        button.Click += click;
        return button;
    }

    private static Button CreateDialogButton(string text, int minimumWidth, EventHandler click)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(minimumWidth, 34),
            Height = 34,
            Margin = new Padding(4, 2, 0, 2),
            UseMnemonic = false
        };
        button.Click += click;
        return button;
    }

    private static Font CreateFont(string family, float size, int style)
    {
        try
        {
            return new Font(family, size, (FontStyle)style, GraphicsUnit.Point);
        }
        catch
        {
            return new Font("Segoe UI", Math.Clamp(size, 8F, 32F), FontStyle.Regular, GraphicsUnit.Point);
        }
    }

    private static string DescribeFont(Font font)
    {
        return $"{font.FontFamily.Name}, {font.Size:0.#}, {font.Style}";
    }
}
