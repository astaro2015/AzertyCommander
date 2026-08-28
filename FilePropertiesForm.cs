using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;

namespace AzertyCommander;

internal sealed class FilePropertiesForm : Form
{
    private readonly string _path;
    private readonly bool _isDirectory;
    private readonly Label _sizeValue = new();
    private readonly Label _detailsValue = new();
    private readonly Label _signatureValue = new();
    private readonly DateTimePicker _createdPicker = new();
    private readonly DateTimePicker _modifiedPicker = new();
    private readonly CheckBox _readOnlyBox = new();
    private readonly CheckBox _hiddenBox = new();
    private readonly CheckBox _systemBox = new();
    private readonly CheckBox _archiveBox = new();
    private readonly Button _hashButton = new();
    private readonly CancellationTokenSource _cancellation = new();

    public FilePropertiesForm(string path)
    {
        _path = Path.GetFullPath(path);
        _isDirectory = Directory.Exists(_path);
        if (!_isDirectory && !File.Exists(_path))
        {
            throw new FileNotFoundException("Элемент не найден.", _path);
        }

        Text = "Свойства: " + Path.GetFileName(_path.TrimEnd(Path.DirectorySeparatorChar));
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(680, 600);
        ClientSize = new Size(760, 690);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        BuildUi();
        LoadValues();
        Shown += async (_, _) => await LoadBackgroundDetailsAsync();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cancellation.Cancel();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cancellation.Dispose();
        }
        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        var icon = new PictureBox
        {
            Image = ShellIconProvider.GetSmallIcon(_path, _isDirectory, false),
            SizeMode = PictureBoxSizeMode.Zoom,
            Dock = DockStyle.Fill,
            Margin = new Padding(6)
        };
        heading.SetRowSpan(icon, 2);
        heading.Controls.Add(icon, 0, 0);
        heading.Controls.Add(new Label
        {
            Text = Path.GetFileName(_path.TrimEnd(Path.DirectorySeparatorChar)),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            AutoEllipsis = true,
            Font = new Font(Font, FontStyle.Bold)
        }, 1, 0);
        heading.Controls.Add(new Label { Text = _path, Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft, AutoEllipsis = true }, 1, 1);
        root.Controls.Add(heading, 0, 0);

        var details = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 10, Padding = new Padding(4, 6, 4, 0) };
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 164));
        details.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var row = 0; row < 10; row++) details.RowStyles.Add(new RowStyle(SizeType.Absolute, row is 3 or 4 ? 58 : 42));
        AddValueRow(details, "Тип:", _isDirectory ? "Папка" : GetTypeText(), 0);
        AddControlRow(details, "Размер:", _sizeValue, 1);
        AddValueRow(details, "Расположение:", Path.GetDirectoryName(_path) ?? _path, 2);
        AddControlRow(details, "Версия и описание:", _detailsValue, 3);
        AddControlRow(details, "Цифровая подпись:", _signatureValue, 4);
        AddControlRow(details, "Создан:", _createdPicker, 5);
        AddControlRow(details, "Изменён:", _modifiedPicker, 6);

        var attributes = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        foreach (var check in new[] { _readOnlyBox, _hiddenBox, _systemBox, _archiveBox })
        {
            check.AutoSize = true;
            check.Margin = new Padding(0, 8, 18, 0);
            attributes.Controls.Add(check);
        }
        _readOnlyBox.Text = "Только чтение";
        _hiddenBox.Text = "Скрытый";
        _systemBox.Text = "Системный";
        _archiveBox.Text = "Архивный";
        AddControlRow(details, "Атрибуты:", attributes, 7);

        _hashButton.Text = "CRC32, MD5 и SHA-256...";
        _hashButton.AutoSize = true;
        _hashButton.MinimumSize = new Size(240, 34);
        _hashButton.Enabled = !_isDirectory;
        _hashButton.Click += (_, _) =>
        {
            using var hashes = new HashesForm([_path]);
            hashes.ShowDialog(this);
        };
        AddControlRow(details, "Контрольные суммы:", _hashButton, 8);
        root.Controls.Add(details, 0, 1);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Padding = new Padding(0, 7, 0, 0) };
        var cancel = CreateButton("Отмена", 112);
        cancel.DialogResult = DialogResult.Cancel;
        var apply = CreateButton("Применить", 124);
        apply.Click += (_, _) => ApplyChanges(closeAfter: false);
        var ok = CreateButton("OK", 112);
        ok.Click += (_, _) => ApplyChanges(closeAfter: true);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(apply);
        buttons.Controls.Add(ok);
        root.Controls.Add(buttons, 0, 2);
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(root);
    }

    private void LoadValues()
    {
        var attributes = File.GetAttributes(_path);
        _readOnlyBox.Checked = attributes.HasFlag(FileAttributes.ReadOnly);
        _hiddenBox.Checked = attributes.HasFlag(FileAttributes.Hidden);
        _systemBox.Checked = attributes.HasFlag(FileAttributes.System);
        _archiveBox.Checked = attributes.HasFlag(FileAttributes.Archive);
        _createdPicker.Format = DateTimePickerFormat.Custom;
        _createdPicker.CustomFormat = "dd.MM.yyyy HH:mm:ss";
        _createdPicker.Dock = DockStyle.Left;
        _createdPicker.Width = 220;
        _modifiedPicker.Format = DateTimePickerFormat.Custom;
        _modifiedPicker.CustomFormat = "dd.MM.yyyy HH:mm:ss";
        _modifiedPicker.Dock = DockStyle.Left;
        _modifiedPicker.Width = 220;
        _createdPicker.Value = _isDirectory ? Directory.GetCreationTime(_path) : File.GetCreationTime(_path);
        _modifiedPicker.Value = _isDirectory ? Directory.GetLastWriteTime(_path) : File.GetLastWriteTime(_path);
        _sizeValue.Text = _isDirectory ? "Подсчёт..." : FormatBytes(new FileInfo(_path).Length);
        _detailsValue.Text = GetVersionDetails();
        _signatureValue.Text = GetSignatureDetails();
    }

    private async Task LoadBackgroundDetailsAsync()
    {
        if (!_isDirectory)
        {
            return;
        }

        try
        {
            var result = await Task.Run(() => CountDirectory(_path, _cancellation.Token), _cancellation.Token);
            _sizeValue.Text = $"{FormatBytes(result.Bytes)}; файлов: {result.Files:N0}, папок: {result.Directories:N0}";
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            _sizeValue.Text = "Не удалось полностью подсчитать";
        }
    }

    private void ApplyChanges(bool closeAfter)
    {
        try
        {
            var attributes = File.GetAttributes(_path);
            attributes = SetFlag(attributes, FileAttributes.ReadOnly, _readOnlyBox.Checked);
            attributes = SetFlag(attributes, FileAttributes.Hidden, _hiddenBox.Checked);
            attributes = SetFlag(attributes, FileAttributes.System, _systemBox.Checked);
            attributes = SetFlag(attributes, FileAttributes.Archive, _archiveBox.Checked);
            File.SetAttributes(_path, attributes);
            if (_isDirectory)
            {
                Directory.SetCreationTime(_path, _createdPicker.Value);
                Directory.SetLastWriteTime(_path, _modifiedPicker.Value);
            }
            else
            {
                File.SetCreationTime(_path, _createdPicker.Value);
                File.SetLastWriteTime(_path, _modifiedPicker.Value);
            }

            if (closeAfter)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private string GetTypeText()
    {
        var extension = Path.GetExtension(_path);
        return string.IsNullOrWhiteSpace(extension) ? "Файл" : $"Файл {extension.TrimStart('.').ToUpperInvariant()}";
    }

    private string GetVersionDetails()
    {
        if (_isDirectory || !(Path.GetExtension(_path).Equals(".exe", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(_path).Equals(".dll", StringComparison.OrdinalIgnoreCase)))
        {
            return "-";
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(_path);
            var description = string.IsNullOrWhiteSpace(info.FileDescription) ? "без описания" : info.FileDescription;
            return $"{description}{Environment.NewLine}Версия файла: {info.FileVersion ?? "не указана"}";
        }
        catch
        {
            return "Не удалось прочитать версию";
        }
    }

    private string GetSignatureDetails()
    {
        if (_isDirectory)
        {
            return "-";
        }

        try
        {
#pragma warning disable SYSLIB0057
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(_path));
#pragma warning restore SYSLIB0057
            var valid = DateTime.Now >= certificate.NotBefore && DateTime.Now <= certificate.NotAfter;
            return $"{certificate.GetNameInfo(X509NameType.SimpleName, false)}; {(valid ? "срок действия актуален" : "срок действия истёк")}";
        }
        catch
        {
            return "Подпись отсутствует или не читается";
        }
    }

    private static (long Bytes, int Files, int Directories) CountDirectory(string root, CancellationToken token)
    {
        var bytes = 0L;
        var files = 0;
        var directories = 0;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            string[] children;
            try { children = Directory.GetFileSystemEntries(directory); } catch { continue; }
            foreach (var child in children)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var attributes = File.GetAttributes(child);
                    if (attributes.HasFlag(FileAttributes.Directory))
                    {
                        directories++;
                        if (!attributes.HasFlag(FileAttributes.ReparsePoint)) pending.Push(child);
                    }
                    else
                    {
                        files++;
                        bytes += new FileInfo(child).Length;
                    }
                }
                catch { }
            }
        }
        return (bytes, files, directories);
    }

    private static FileAttributes SetFlag(FileAttributes attributes, FileAttributes flag, bool enabled) => enabled ? attributes | flag : attributes & ~flag;
    private static string FormatBytes(long value)
    {
        string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
        var size = (double)value;
        var unit = 0;
        while (size >= 1024D && unit < units.Length - 1) { size /= 1024D; unit++; }
        return unit == 0 ? $"{value:N0} {units[unit]}" : $"{size:N2} {units[unit]} ({value:N0} байт)";
    }

    private static void AddValueRow(TableLayoutPanel panel, string label, string value, int row) => AddControlRow(panel, label, new Label { Text = value, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }, row);
    private static void AddControlRow(TableLayoutPanel panel, string label, Control control, int row)
    {
        panel.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true }, 0, row);
        control.Dock = control is DateTimePicker or Button ? DockStyle.Left : DockStyle.Fill;
        panel.Controls.Add(control, 1, row);
    }
    private static Button CreateButton(string text, int width) => new() { Text = text, AutoSize = true, MinimumSize = new Size(width, 36), Margin = new Padding(8, 2, 0, 2), UseMnemonic = false };
}
