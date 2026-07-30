namespace AzertyCommander;

internal sealed class SelectionMaskForm : Form
{
    private readonly ComboBox _maskBox = new();
    private readonly ListBox _templateList = new();

    public SelectionMaskForm(bool mark)
    {
        Text = mark ? "Добавить выделение" : "Убрать выделение";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(580, 414);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        BuildUi();
    }

    public string SelectedPattern => _maskBox.Text.Trim();

    private void BuildUi()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(12)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        layout.Controls.Add(new Label
        {
            Text = "Укажите маску файлов (пример *.txt;*.doc)\r\nили регулярное выражение с символом '<', например, <[ab].*",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);

        _maskBox.Dock = DockStyle.Fill;
        _maskBox.DropDownStyle = ComboBoxStyle.DropDown;
        _maskBox.Items.AddRange(["*.*", "*.txt;*.doc", "*.jpg;*.png;*.gif", "*.zip;*.rar;*.7z", "<[ab].*"]);
        _maskBox.Text = "*.*";
        _maskBox.SelectAll();
        layout.Controls.Add(_maskBox, 0, 1);

        layout.Controls.Add(new Label
        {
            Text = "или выберите тип файлов по шаблону:",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 2);

        _templateList.Dock = DockStyle.Fill;
        _templateList.IntegralHeight = false;
        _templateList.Items.AddRange([
            new SelectionTemplate("Все", "*.*"),
            new SelectionTemplate("Текст", "*.txt;*.log;*.ini;*.cfg;*.csv"),
            new SelectionTemplate("Документы", "*.doc;*.docx;*.rtf;*.pdf;*.xls;*.xlsx;*.ppt;*.pptx"),
            new SelectionTemplate("Картинки", "*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.webp;*.tif;*.tiff"),
            new SelectionTemplate("Видео", "*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.m4v"),
            new SelectionTemplate("Музыка", "*.mp3;*.wav;*.flac;*.ogg;*.m4a"),
            new SelectionTemplate("Архивы", "*.zip;*.rar;*.7z;*.tar;*.gz"),
            new SelectionTemplate("Программы", "*.exe;*.com;*.bat;*.cmd;*.msi")
        ]);
        _templateList.SelectedIndexChanged += (_, _) => ApplySelectedTemplate();
        _templateList.DoubleClick += (_, _) =>
        {
            ApplySelectedTemplate();
            DialogResult = DialogResult.OK;
        };
        layout.Controls.Add(_templateList, 0, 3);

        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));

        var templateButton = new Button { Text = "Шаблон...", Dock = DockStyle.Fill, Margin = new Padding(0, 4, 6, 0) };
        templateButton.Click += (_, _) => ApplySelectedTemplate();
        buttons.Controls.Add(templateButton, 1, 0);

        var okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 6, 0) };
        var cancelButton = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Dock = DockStyle.Fill, Margin = new Padding(0, 4, 0, 0) };
        buttons.Controls.Add(okButton, 2, 0);
        buttons.Controls.Add(cancelButton, 3, 0);
        layout.Controls.Add(buttons, 0, 4);

        AcceptButton = okButton;
        CancelButton = cancelButton;
        Controls.Add(layout);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult == DialogResult.OK && string.IsNullOrWhiteSpace(SelectedPattern))
        {
            MessageBox.Show(this, "Укажите маску выделения.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            e.Cancel = true;
        }

        base.OnFormClosing(e);
    }

    private void ApplySelectedTemplate()
    {
        if (_templateList.SelectedItem is SelectionTemplate template)
        {
            _maskBox.Text = template.Pattern;
            _maskBox.SelectionStart = _maskBox.Text.Length;
            _maskBox.SelectionLength = 0;
        }
    }

    private sealed record SelectionTemplate(string Name, string Pattern)
    {
        public override string ToString()
        {
            return $"{Name} ({Pattern})";
        }
    }
}
