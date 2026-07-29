using System.Net;
using System.Net.Sockets;

namespace AzertyCommander;

internal sealed class FtpServerForm : Form
{
    private readonly TextBox _folderBox = new();
    private readonly NumericUpDown _portBox = new();
    private readonly NumericUpDown _passiveStartBox = new();
    private readonly NumericUpDown _passiveEndBox = new();
    private readonly CheckBox _anonymousBox = new();
    private readonly TextBox _userBox = new();
    private readonly TextBox _passwordBox = new();
    private readonly CheckBox _allowWriteBox = new();
    private readonly Button _startButton = new();
    private readonly Button _stopButton = new();
    private readonly ListBox _addressesBox = new();
    private readonly TextBox _logBox = new();
    private SimpleFtpServer? _server;

    public FtpServerForm(string initialFolder)
    {
        BuildUi(initialFolder);
        UpdateControls();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        StopServer();
        base.OnFormClosing(e);
    }

    private void BuildUi(string initialFolder)
    {
        Text = "Создать FTP сервер";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(900, 620);
        ClientSize = new Size(980, 660);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var tips = new ToolTip();
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 158));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        var settings = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 8,
            RowCount = 4
        };
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 98));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142));
        settings.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        settings.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        settings.Controls.Add(CreateLabel("Папка:"), 0, 0);
        _folderBox.Text = Directory.Exists(initialFolder) ? initialFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        _folderBox.Dock = DockStyle.Fill;
        tips.SetToolTip(_folderBox, "FTP сервер не выпустит клиентов выше этой папки.");
        settings.Controls.Add(_folderBox, 1, 0);

        var browseButton = new Button { Text = "Выбрать...", Dock = DockStyle.Fill };
        browseButton.Click += (_, _) => BrowseFolder();
        settings.Controls.Add(browseButton, 2, 0);

        settings.Controls.Add(CreateLabel("Порт:"), 3, 0);
        _portBox.Minimum = 1;
        _portBox.Maximum = 65535;
        _portBox.Value = 2121;
        _portBox.Dock = DockStyle.Fill;
        tips.SetToolTip(_portBox, "2121 обычно работает без прав администратора. Порт 21 может потребовать запуск от имени администратора.");
        settings.Controls.Add(_portBox, 4, 0);

        _startButton.Text = "Старт";
        _startButton.Dock = DockStyle.Fill;
        _startButton.Click += (_, _) => StartServer();
        settings.Controls.Add(_startButton, 6, 0);

        _stopButton.Text = "Стоп";
        _stopButton.Dock = DockStyle.Fill;
        _stopButton.Click += (_, _) => StopServer();
        settings.Controls.Add(_stopButton, 7, 0);

        _anonymousBox.Text = "Анонимный вход";
        _anonymousBox.Checked = true;
        _anonymousBox.Dock = DockStyle.Fill;
        _anonymousBox.CheckedChanged += (_, _) => UpdateControls();
        tips.SetToolTip(_anonymousBox, "Клиент может войти как anonymous. Удобно для быстрой раздачи в локальной сети.");
        settings.SetColumnSpan(_anonymousBox, 2);
        settings.Controls.Add(_anonymousBox, 0, 1);

        settings.Controls.Add(CreateLabel("Логин:"), 2, 1);
        _userBox.Text = "azerty";
        _userBox.Dock = DockStyle.Fill;
        settings.SetColumnSpan(_userBox, 2);
        settings.Controls.Add(_userBox, 3, 1);

        settings.Controls.Add(CreateLabel("Пароль:"), 5, 1);
        _passwordBox.UseSystemPasswordChar = true;
        _passwordBox.Dock = DockStyle.Fill;
        settings.SetColumnSpan(_passwordBox, 2);
        settings.Controls.Add(_passwordBox, 6, 1);

        _allowWriteBox.Text = "Разрешить запись, удаление и переименование";
        _allowWriteBox.Dock = DockStyle.Fill;
        tips.SetToolTip(_allowWriteBox, "Если включить, FTP-клиенты смогут менять файлы в выбранной папке.");
        settings.SetColumnSpan(_allowWriteBox, 4);
        settings.Controls.Add(_allowWriteBox, 0, 2);

        settings.Controls.Add(CreateLabel("PASV порты:"), 0, 3);
        _passiveStartBox.Minimum = 1;
        _passiveStartBox.Maximum = 65535;
        _passiveStartBox.Value = 50000;
        _passiveStartBox.Dock = DockStyle.Fill;
        tips.SetToolTip(_passiveStartBox, "Первый порт диапазона для передачи файлов в пассивном FTP.");
        settings.Controls.Add(_passiveStartBox, 1, 3);

        settings.Controls.Add(CreateLabel("до"), 2, 3);
        _passiveEndBox.Minimum = 1;
        _passiveEndBox.Maximum = 65535;
        _passiveEndBox.Value = 50100;
        _passiveEndBox.Dock = DockStyle.Fill;
        tips.SetToolTip(_passiveEndBox, "Последний порт диапазона. Для интернета пробрасывай и основной порт, и этот диапазон.");
        settings.Controls.Add(_passiveEndBox, 3, 3);

        var copyButton = new Button { Text = "Копировать адрес", Dock = DockStyle.Fill };
        copyButton.Click += (_, _) => CopySelectedAddress();
        settings.SetColumnSpan(copyButton, 2);
        settings.Controls.Add(copyButton, 6, 2);

        root.Controls.Add(settings, 0, 0);

        var hint = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Text = "Подсказка: если Windows спросит доступ через брандмауэр, разреши для нужной сети. Для интернета нужен проброс основного порта и PASV-диапазона; для локальной сети обычно достаточно адреса ниже."
        };
        root.Controls.Add(hint, 0, 1);

        var addressGroup = new GroupBox { Dock = DockStyle.Fill, Text = "Адреса для подключения" };
        _addressesBox.Dock = DockStyle.Fill;
        tips.SetToolTip(_addressesBox, "Открой один из этих адресов в FTP-клиенте. Пароль в адрес специально не вставляется.");
        addressGroup.Controls.Add(_addressesBox);
        root.Controls.Add(addressGroup, 0, 2);

        var logGroup = new GroupBox { Dock = DockStyle.Fill, Text = "Журнал сервера" };
        _logBox.Dock = DockStyle.Fill;
        _logBox.Multiline = true;
        _logBox.ScrollBars = ScrollBars.Vertical;
        _logBox.ReadOnly = true;
        logGroup.Controls.Add(_logBox);
        root.Controls.Add(logGroup, 0, 3);

        var bottom = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Text = "Обычный FTP без шифрования: для публичного интернета лучше использовать VPN/туннель или SFTP в отдельной программе."
        };
        root.Controls.Add(bottom, 0, 4);

        Controls.Add(root);
    }

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Выберите папку, которую будет видеть FTP клиент",
            SelectedPath = Directory.Exists(_folderBox.Text) ? _folderBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _folderBox.Text = dialog.SelectedPath;
        }
    }

    private void StartServer()
    {
        try
        {
            if (!Directory.Exists(_folderBox.Text))
            {
                MessageBox.Show(this, "Выбранная папка не найдена.", "FTP сервер", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!_anonymousBox.Checked && string.IsNullOrWhiteSpace(_userBox.Text))
            {
                MessageBox.Show(this, "Укажите логин или включите анонимный вход.", "FTP сервер", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _server = new SimpleFtpServer(new SimpleFtpServerOptions
            {
                RootDirectory = _folderBox.Text,
                Port = (int)_portBox.Value,
                PassivePortStart = (int)_passiveStartBox.Value,
                PassivePortEnd = (int)_passiveEndBox.Value,
                AllowAnonymous = _anonymousBox.Checked,
                UserName = _userBox.Text.Trim(),
                Password = _passwordBox.Text,
                ReadOnly = !_allowWriteBox.Checked
            });
            _server.LogReceived += ServerLogReceived;
            _server.Start();
            FillAddresses(_server.ActualPort);
            UpdateControls();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "FTP сервер", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StopServer()
    {
        if (_server is null)
        {
            return;
        }

        _server.LogReceived -= ServerLogReceived;
        _server.Dispose();
        _server = null;
        _addressesBox.Items.Clear();
        UpdateControls();
    }

    private void ServerLogReceived(object? sender, string message)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() => AppendLog(message)));
        }
        else
        {
            AppendLog(message);
        }
    }

    private void AppendLog(string message)
    {
        _logBox.AppendText(message + Environment.NewLine);
    }

    private void FillAddresses(int port)
    {
        _addressesBox.Items.Clear();
        foreach (var address in GetLocalIPv4Addresses())
        {
            var userPart = _anonymousBox.Checked ? string.Empty : Uri.EscapeDataString(_userBox.Text.Trim()) + "@";
            _addressesBox.Items.Add($"ftp://{userPart}{address}:{port}/");
        }

        if (_addressesBox.Items.Count > 0)
        {
            _addressesBox.SelectedIndex = 0;
        }
    }

    private void CopySelectedAddress()
    {
        if (_addressesBox.SelectedItem is not null)
        {
            Clipboard.SetText(_addressesBox.SelectedItem.ToString() ?? string.Empty);
        }
    }

    private void UpdateControls()
    {
        var running = _server?.IsRunning == true;
        _folderBox.Enabled = !running;
        _portBox.Enabled = !running;
        _passiveStartBox.Enabled = !running;
        _passiveEndBox.Enabled = !running;
        _anonymousBox.Enabled = !running;
        _userBox.Enabled = !running && !_anonymousBox.Checked;
        _passwordBox.Enabled = !running && !_anonymousBox.Checked;
        _allowWriteBox.Enabled = !running;
        _startButton.Enabled = !running;
        _stopButton.Enabled = running;
    }

    private static IEnumerable<string> GetLocalIPv4Addresses()
    {
        yield return "127.0.0.1";

        IPHostEntry hostEntry;
        try
        {
            hostEntry = Dns.GetHostEntry(Dns.GetHostName());
        }
        catch
        {
            yield break;
        }

        foreach (var address in hostEntry.AddressList
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
            .Select(address => address.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(address => address, StringComparer.OrdinalIgnoreCase))
        {
            yield return address;
        }
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
}
