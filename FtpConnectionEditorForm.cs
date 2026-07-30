namespace AzertyCommander;

internal sealed class FtpConnectionEditorForm : Form
{
    private readonly TextBox _nameBox = new();
    private readonly TextBox _serverBox = new();
    private readonly TextBox _userBox = new();
    private readonly TextBox _passwordBox = new();
    private readonly TextBox _remoteDirectoryBox = new();
    private readonly TextBox _localDirectoryBox = new();
    private readonly ComboBox _groupBox = new();
    private readonly CheckBox _anonymousBox = new();
    private readonly CheckBox _passiveBox = new();
    private readonly FtpConnectionProfile _profile;

    public FtpConnectionEditorForm(FtpConnectionProfile profile, IEnumerable<string> groups, string defaultLocalDirectory)
    {
        _profile = profile.Clone();
        BuildUi(groups, defaultLocalDirectory);
        LoadProfile();
    }

    public FtpConnectionProfile Profile => _profile.Clone();

    private void BuildUi(IEnumerable<string> groups, string defaultLocalDirectory)
    {
        Text = "Настройка FTP-соединения";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(610, 560);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var tabs = new TabControl
        {
            Dock = DockStyle.Fill
        };
        tabs.TabPages.Add(BuildGeneralPage(defaultLocalDirectory));
        tabs.TabPages.Add(BuildAdvancedPage(groups));
        root.Controls.Add(tabs, 0, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 8, 0, 0)
        };

        var helpButton = new Button { Text = "Справка", Size = new Size(132, 32), Margin = new Padding(4, 0, 0, 0) };
        helpButton.Click += (_, _) => ShowHelp();

        var cancelButton = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, Size = new Size(132, 32), Margin = new Padding(4, 0, 0, 0) };
        var okButton = new Button { Text = "OK", DialogResult = DialogResult.OK, Size = new Size(132, 32), Margin = new Padding(4, 0, 0, 0) };
        okButton.Click += (_, args) =>
        {
            if (!SaveProfile())
            {
                DialogResult = DialogResult.None;
            }
        };

        buttons.Controls.Add(helpButton);
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(okButton);
        root.Controls.Add(buttons, 0, 1);

        AcceptButton = okButton;
        CancelButton = cancelButton;
        Controls.Add(root);
    }

    private TabPage BuildGeneralPage(string defaultLocalDirectory)
    {
        var page = new TabPage("Общие") { Padding = new Padding(8) };
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 10
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));

        for (var index = 0; index < 10; index++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, index == 5 ? 28 : 40));
        }

        grid.Controls.Add(CreateLabel("Имя соединения:"), 0, 0);
        _nameBox.Dock = DockStyle.Fill;
        grid.SetColumnSpan(_nameBox, 2);
        grid.Controls.Add(_nameBox, 1, 0);

        grid.Controls.Add(CreateLabel("Сервер [:Порт]:"), 0, 1);
        _serverBox.Dock = DockStyle.Fill;
        grid.SetColumnSpan(_serverBox, 2);
        grid.Controls.Add(_serverBox, 1, 1);

        _anonymousBox.Text = "Анонимное соединение (пароль - адрес E-mail)";
        _anonymousBox.Dock = DockStyle.Fill;
        _anonymousBox.CheckedChanged += (_, _) => UpdateAnonymousFields();
        grid.SetColumnSpan(_anonymousBox, 2);
        grid.Controls.Add(_anonymousBox, 1, 2);

        grid.Controls.Add(CreateLabel("Учётная запись:"), 0, 3);
        _userBox.Dock = DockStyle.Fill;
        grid.SetColumnSpan(_userBox, 2);
        grid.Controls.Add(_userBox, 1, 3);

        grid.Controls.Add(CreateLabel("Пароль:"), 0, 4);
        _passwordBox.Dock = DockStyle.Fill;
        _passwordBox.UseSystemPasswordChar = true;
        grid.SetColumnSpan(_passwordBox, 2);
        grid.Controls.Add(_passwordBox, 1, 4);

        var warning = new Label
        {
            Text = "ВНИМАНИЕ: пароль хранится здесь без шифрования.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DarkRed,
            AutoEllipsis = true
        };
        grid.SetColumnSpan(warning, 3);
        grid.Controls.Add(warning, 0, 5);

        grid.Controls.Add(CreateLabel("Удалённый каталог:"), 0, 6);
        _remoteDirectoryBox.Dock = DockStyle.Fill;
        grid.SetColumnSpan(_remoteDirectoryBox, 2);
        grid.Controls.Add(_remoteDirectoryBox, 1, 6);

        grid.Controls.Add(CreateLabel("Локальный каталог:"), 0, 7);
        _localDirectoryBox.Dock = DockStyle.Fill;
        _localDirectoryBox.Text = defaultLocalDirectory;
        grid.Controls.Add(_localDirectoryBox, 1, 7);

        var browseButton = new Button { Text = ">>", Dock = DockStyle.Fill, Margin = new Padding(4, 3, 0, 5) };
        browseButton.Click += (_, _) => BrowseLocalDirectory();
        grid.Controls.Add(browseButton, 2, 7);

        _passiveBox.Text = "Пассивный режим обмена";
        _passiveBox.Dock = DockStyle.Fill;
        _passiveBox.Checked = true;
        _passiveBox.Enabled = false;
        grid.SetColumnSpan(_passiveBox, 2);
        grid.Controls.Add(_passiveBox, 1, 8);

        var note = new Label
        {
            Text = "Обычный FTP без TLS. Для интернета лучше VPN/туннель или отдельный SFTP-клиент.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        grid.SetColumnSpan(note, 3);
        grid.Controls.Add(note, 0, 9);

        page.Controls.Add(grid);
        return page;
    }

    private TabPage BuildAdvancedPage(IEnumerable<string> groups)
    {
        var page = new TabPage("Расширенные") { Padding = new Padding(8) };
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 2,
            Height = 88
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

        grid.Controls.Add(CreateLabel("Папка в списке:"), 0, 0);
        _groupBox.Dock = DockStyle.Fill;
        _groupBox.DropDownStyle = ComboBoxStyle.DropDown;
        _groupBox.Items.Add(string.Empty);
        foreach (var group in groups.Where(group => !string.IsNullOrWhiteSpace(group)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(group => group))
        {
            _groupBox.Items.Add(group);
        }
        grid.Controls.Add(_groupBox, 1, 0);

        var hint = new Label
        {
            Text = "Папка нужна только для порядка в списке соединений.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        grid.SetColumnSpan(hint, 2);
        grid.Controls.Add(hint, 0, 1);

        page.Controls.Add(grid);
        return page;
    }

    private void LoadProfile()
    {
        _nameBox.Text = _profile.Name;
        _serverBox.Text = BuildServerText(_profile.Host, _profile.Port);
        _anonymousBox.Checked = _profile.Anonymous;
        _userBox.Text = string.IsNullOrWhiteSpace(_profile.UserName) ? "anonymous" : _profile.UserName;
        _passwordBox.Text = string.IsNullOrWhiteSpace(_profile.Password) && _profile.Anonymous ? "guest@" : _profile.Password;
        _remoteDirectoryBox.Text = _profile.RemoteDirectory;
        if (!string.IsNullOrWhiteSpace(_profile.LocalDirectory))
        {
            _localDirectoryBox.Text = _profile.LocalDirectory;
        }
        _groupBox.Text = _profile.Group;
        _passiveBox.Checked = true;
        UpdateAnonymousFields();
        _nameBox.SelectAll();
    }

    private bool SaveProfile()
    {
        if (string.IsNullOrWhiteSpace(_nameBox.Text))
        {
            MessageBox.Show(this, "Укажите имя соединения.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _nameBox.Focus();
            return false;
        }

        if (!TryParseServer(_serverBox.Text, out var host, out var port, out var error))
        {
            MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _serverBox.Focus();
            return false;
        }

        _profile.Name = _nameBox.Text.Trim();
        _profile.Host = host;
        _profile.Port = port;
        _profile.Anonymous = _anonymousBox.Checked;
        _profile.UserName = _anonymousBox.Checked ? "anonymous" : _userBox.Text.Trim();
        _profile.Password = _anonymousBox.Checked ? "guest@" : _passwordBox.Text;
        _profile.RemoteDirectory = _remoteDirectoryBox.Text.Trim();
        _profile.LocalDirectory = _localDirectoryBox.Text.Trim();
        _profile.Group = _groupBox.Text.Trim();
        _profile.PassiveMode = true;
        return true;
    }

    private void UpdateAnonymousFields()
    {
        if (_anonymousBox.Checked)
        {
            _userBox.Text = "anonymous";
            if (string.IsNullOrWhiteSpace(_passwordBox.Text))
            {
                _passwordBox.Text = "guest@";
            }
        }

        _userBox.Enabled = !_anonymousBox.Checked;
        _passwordBox.Enabled = !_anonymousBox.Checked;
    }

    private void BrowseLocalDirectory()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Выберите локальный каталог для этого FTP-соединения",
            SelectedPath = Directory.Exists(_localDirectoryBox.Text) ? _localDirectoryBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _localDirectoryBox.Text = dialog.SelectedPath;
        }
    }

    private void ShowHelp()
    {
        MessageBox.Show(
            this,
            "Сервер можно писать как 192.168.1.10, ftp.example.com или host:2121. Удалённый каталог откроется сразу после подключения. Локальный каталог используется для скачивания; если он пустой или недоступен, берётся активная панель главного окна.",
            "FTP справка",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static bool TryParseServer(string text, out string host, out int port, out string error)
    {
        host = string.Empty;
        port = 21;
        error = string.Empty;

        text = text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Укажите сервер.";
            return false;
        }

        if (Uri.TryCreate(text.Contains("://", StringComparison.Ordinal) ? text : "ftp://" + text, UriKind.Absolute, out var uri))
        {
            host = uri.Host;
            port = uri.IsDefaultPort ? 21 : uri.Port;
        }
        else
        {
            error = "Сервер указан некорректно.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            error = "Укажите сервер.";
            return false;
        }

        if (port < 1 || port > 65535)
        {
            error = "Порт должен быть от 1 до 65535.";
            return false;
        }

        return true;
    }

    private static string BuildServerText(string host, int port)
    {
        var cleanHost = string.IsNullOrWhiteSpace(host) ? "127.0.0.1" : host.Trim();
        var cleanPort = port is >= 1 and <= 65535 ? port : 21;
        return $"{cleanHost}:{cleanPort}";
    }

    private static Label CreateLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0, 3, 8, 5)
        };
    }
}
