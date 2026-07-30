namespace AzertyCommander;

internal sealed class FtpConnectionManagerForm : Form
{
    private readonly TreeView _tree = new();
    private readonly Button _connectButton = new();
    private readonly Button _copyButton = new();
    private readonly Button _editButton = new();
    private readonly Button _deleteButton = new();
    private readonly List<FtpConnectionProfile> _profiles;
    private readonly List<string> _groups;
    private readonly string _defaultLocalDirectory;

    public FtpConnectionManagerForm(IEnumerable<FtpConnectionProfile> profiles, IEnumerable<string> groups, string defaultLocalDirectory)
    {
        _defaultLocalDirectory = defaultLocalDirectory;
        _profiles = profiles
            .Select(NormalizeProfile)
            .ToList();
        _groups = groups
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .Select(group => group.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group)
            .ToList();

        if (_profiles.Count == 0)
        {
            _profiles.Add(FtpConnectionProfile.CreateDefault(defaultLocalDirectory));
        }

        BuildUi();
        RefreshTree();
        UpdateButtons();
    }

    public bool ProfilesChanged { get; private set; }
    public FtpConnectionProfile? SelectedProfile { get; private set; }
    public IReadOnlyList<FtpConnectionProfile> Profiles => _profiles.Select(profile => profile.Clone()).ToList();
    public IReadOnlyList<string> Groups => KnownGroups().ToList();

    private void BuildUi()
    {
        Text = "Соединение с FTP-сервером";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(700, 520);
        ClientSize = new Size(720, 590);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(8)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 176));

        var listPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 8, 0)
        };
        listPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        listPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        listPanel.Controls.Add(new Label
        {
            Text = "Соединиться с:",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        }, 0, 0);

        _tree.Dock = DockStyle.Fill;
        _tree.HideSelection = false;
        _tree.FullRowSelect = true;
        _tree.ShowNodeToolTips = true;
        _tree.ImageList = CreateImageList();
        _tree.AfterSelect += (_, _) => UpdateButtons();
        _tree.NodeMouseDoubleClick += (_, args) =>
        {
            _tree.SelectedNode = args.Node;
            ConnectSelected();
        };
        _tree.KeyDown += (_, args) =>
        {
            if (args.KeyCode == Keys.Enter)
            {
                args.SuppressKeyPress = true;
                ConnectSelected();
            }
            else if (args.KeyCode == Keys.Delete)
            {
                args.SuppressKeyPress = true;
                DeleteSelected();
            }
            else if (args.KeyCode == Keys.F2)
            {
                args.SuppressKeyPress = true;
                EditSelected();
            }
        };
        listPanel.Controls.Add(_tree, 0, 1);
        root.Controls.Add(listPanel, 0, 0);

        var buttons = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 10,
            Height = 464
        };

        for (var index = 0; index < 10; index++)
        {
            buttons.RowStyles.Add(new RowStyle(SizeType.Absolute, index is 1 or 5 or 8 ? 54 : 42));
        }

        _connectButton.Text = "Соединиться";
        _connectButton.Click += (_, _) => ConnectSelected();
        buttons.Controls.Add(StyleButton(_connectButton), 0, 0);

        buttons.Controls.Add(CreateButton("Добавить...", (_, _) => AddProfile()), 0, 1);
        buttons.Controls.Add(CreateButton("Новый URL...", (_, _) => AddUrl()), 0, 2);

        _copyButton.Text = "Копировать...";
        _copyButton.Click += (_, _) => CopySelected();
        buttons.Controls.Add(StyleButton(_copyButton), 0, 3);

        buttons.Controls.Add(CreateButton("Новая папка...", (_, _) => AddGroup()), 0, 4);

        _editButton.Text = "Изменить...";
        _editButton.Click += (_, _) => EditSelected();
        buttons.Controls.Add(StyleButton(_editButton), 0, 5);

        _deleteButton.Text = "Удалить";
        _deleteButton.Click += (_, _) => DeleteSelected();
        buttons.Controls.Add(StyleButton(_deleteButton), 0, 6);

        buttons.Controls.Add(CreateButton("Отмена", (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }), 0, 7);

        buttons.Controls.Add(CreateButton("Справка", (_, _) => ShowHelp()), 0, 9);
        root.Controls.Add(buttons, 1, 0);

        AcceptButton = _connectButton;
        CancelButton = buttons.Controls
            .OfType<Button>()
            .FirstOrDefault(button => string.Equals(button.Text, "Отмена", StringComparison.Ordinal));
        Controls.Add(root);
    }

    private void RefreshTree()
    {
        var selectedId = SelectedProfileFromTree()?.Id;
        var selectedGroup = SelectedGroupFromTree();
        _tree.BeginUpdate();
        _tree.Nodes.Clear();

        foreach (var profile in _profiles.Where(profile => string.IsNullOrWhiteSpace(profile.Group)).OrderBy(profile => profile.Name))
        {
            _tree.Nodes.Add(CreateProfileNode(profile));
        }

        foreach (var group in KnownGroups())
        {
            var groupNode = new TreeNode(group)
            {
                Tag = new GroupTag(group),
                ImageKey = "folder",
                SelectedImageKey = "folder"
            };

            foreach (var profile in _profiles.Where(profile => string.Equals(profile.Group, group, StringComparison.OrdinalIgnoreCase)).OrderBy(profile => profile.Name))
            {
                groupNode.Nodes.Add(CreateProfileNode(profile));
            }

            groupNode.Expand();
            _tree.Nodes.Add(groupNode);
        }

        _tree.EndUpdate();

        if (!string.IsNullOrWhiteSpace(selectedId))
        {
            SelectProfileNode(selectedId);
        }
        else if (!string.IsNullOrWhiteSpace(selectedGroup))
        {
            SelectGroupNode(selectedGroup);
        }

        _tree.SelectedNode ??= FirstProfileNode() ?? (_tree.Nodes.Count > 0 ? _tree.Nodes[0] : null);
        UpdateButtons();
    }

    private TreeNode CreateProfileNode(FtpConnectionProfile profile)
    {
        var node = new TreeNode(profile.Name)
        {
            Tag = profile.Id,
            ImageKey = "ftp",
            SelectedImageKey = "ftp"
        };

        node.ToolTipText = $"{profile.Host}:{profile.Port}";
        return node;
    }

    private void ConnectSelected()
    {
        var profile = SelectedProfileFromTree();
        if (profile is null)
        {
            return;
        }

        SelectedProfile = profile.Clone();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void AddProfile()
    {
        var profile = FtpConnectionProfile.CreateDefault(_defaultLocalDirectory);
        profile.Group = SelectedGroupFromTree() ?? string.Empty;

        using var editor = new FtpConnectionEditorForm(profile, KnownGroups(), _defaultLocalDirectory);
        if (editor.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _profiles.Add(editor.Profile);
        ProfilesChanged = true;
        RefreshTree();
        SelectProfileNode(editor.Profile.Id);
    }

    private void AddUrl()
    {
        var text = InputDialog.Show(this, "Новый FTP URL", "FTP URL:", "ftp://127.0.0.1:2121/");
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        if (!TryCreateProfileFromUrl(text, out var profile, out var error))
        {
            MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        profile.Group = SelectedGroupFromTree() ?? string.Empty;
        profile.LocalDirectory = _defaultLocalDirectory;
        using var editor = new FtpConnectionEditorForm(profile, KnownGroups(), _defaultLocalDirectory);
        if (editor.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _profiles.Add(editor.Profile);
        ProfilesChanged = true;
        RefreshTree();
        SelectProfileNode(editor.Profile.Id);
    }

    private void CopySelected()
    {
        var selected = SelectedProfileFromTree();
        if (selected is null)
        {
            return;
        }

        var copy = selected.Clone();
        copy.Id = Guid.NewGuid().ToString("N");
        copy.Name = UniqueName(copy.Name + " копия");

        using var editor = new FtpConnectionEditorForm(copy, KnownGroups(), _defaultLocalDirectory);
        if (editor.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _profiles.Add(editor.Profile);
        ProfilesChanged = true;
        RefreshTree();
        SelectProfileNode(editor.Profile.Id);
    }

    private void AddGroup()
    {
        var name = InputDialog.Show(this, "Новая папка FTP", "Имя папки:", "Новая папка");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        name = name.Trim();
        if (KnownGroups().Any(group => string.Equals(group, name, StringComparison.OrdinalIgnoreCase)))
        {
            SelectGroupNode(name);
            return;
        }

        _groups.Add(name);
        ProfilesChanged = true;
        RefreshTree();
        SelectGroupNode(name);
    }

    private void EditSelected()
    {
        var selected = SelectedProfileFromTree();
        if (selected is null)
        {
            return;
        }

        using var editor = new FtpConnectionEditorForm(selected, KnownGroups(), _defaultLocalDirectory);
        if (editor.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        var index = _profiles.FindIndex(profile => profile.Id == selected.Id);
        if (index >= 0)
        {
            _profiles[index] = editor.Profile;
            ProfilesChanged = true;
            RefreshTree();
            SelectProfileNode(editor.Profile.Id);
        }
    }

    private void DeleteSelected()
    {
        var selected = SelectedProfileFromTree();
        if (selected is not null)
        {
            if (MessageBox.Show(this, $"Удалить соединение \"{selected.Name}\"?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                return;
            }

            _profiles.RemoveAll(profile => profile.Id == selected.Id);
            ProfilesChanged = true;
            RefreshTree();
            return;
        }

        var group = SelectedGroupFromTree();
        if (string.IsNullOrWhiteSpace(group))
        {
            return;
        }

        if (MessageBox.Show(this, $"Убрать папку \"{group}\" из списка? Соединения останутся.", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        foreach (var profile in _profiles.Where(profile => string.Equals(profile.Group, group, StringComparison.OrdinalIgnoreCase)))
        {
            profile.Group = string.Empty;
        }

        _groups.RemoveAll(item => string.Equals(item, group, StringComparison.OrdinalIgnoreCase));
        ProfilesChanged = true;
        RefreshTree();
    }

    private void ShowHelp()
    {
        MessageBox.Show(
            this,
            "Добавьте соединение, укажите сервер и нажмите \"Соединиться\". \"Новый URL\" понимает адреса вида ftp://user:pass@host:2121/path. Встроенный клиент работает с обычным FTP без TLS.",
            "FTP справка",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void UpdateButtons()
    {
        var profileSelected = SelectedProfileFromTree() is not null;
        var hasSelection = _tree.SelectedNode is not null;
        _connectButton.Enabled = profileSelected;
        _copyButton.Enabled = profileSelected;
        _editButton.Enabled = profileSelected;
        _deleteButton.Enabled = hasSelection;
    }

    private FtpConnectionProfile? SelectedProfileFromTree()
    {
        if (_tree.SelectedNode?.Tag is not string id)
        {
            return null;
        }

        return _profiles.FirstOrDefault(profile => profile.Id == id);
    }

    private string? SelectedGroupFromTree()
    {
        if (_tree.SelectedNode?.Tag is GroupTag group)
        {
            return group.Name;
        }

        if (_tree.SelectedNode?.Parent?.Tag is GroupTag parentGroup)
        {
            return parentGroup.Name;
        }

        return null;
    }

    private IEnumerable<string> KnownGroups()
    {
        return _profiles
            .Select(profile => profile.Group)
            .Concat(_groups)
            .Where(group => !string.IsNullOrWhiteSpace(group))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group);
    }

    private void SelectProfileNode(string id)
    {
        foreach (TreeNode node in FlattenNodes(_tree.Nodes))
        {
            if (node.Tag is string nodeId && string.Equals(nodeId, id, StringComparison.Ordinal))
            {
                _tree.SelectedNode = node;
                node.EnsureVisible();
                return;
            }
        }
    }

    private void SelectGroupNode(string group)
    {
        foreach (TreeNode node in _tree.Nodes)
        {
            if (node.Tag is GroupTag groupTag && string.Equals(groupTag.Name, group, StringComparison.OrdinalIgnoreCase))
            {
                _tree.SelectedNode = node;
                node.EnsureVisible();
                return;
            }
        }
    }

    private TreeNode? FirstProfileNode()
    {
        return FlattenNodes(_tree.Nodes).FirstOrDefault(node => node.Tag is string);
    }

    private string UniqueName(string baseName)
    {
        var candidate = baseName;
        var index = 2;
        while (_profiles.Any(profile => string.Equals(profile.Name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseName} ({index})";
            index++;
        }

        return candidate;
    }

    private static FtpConnectionProfile NormalizeProfile(FtpConnectionProfile source)
    {
        var profile = source.Clone();
        if (string.IsNullOrWhiteSpace(profile.Id))
        {
            profile.Id = Guid.NewGuid().ToString("N");
        }

        if (string.IsNullOrWhiteSpace(profile.Name))
        {
            profile.Name = string.IsNullOrWhiteSpace(profile.Host) ? "FTP соединение" : profile.Host;
        }

        if (string.IsNullOrWhiteSpace(profile.Host))
        {
            profile.Host = "127.0.0.1";
        }

        if (profile.Port is < 1 or > 65535)
        {
            profile.Port = 21;
        }

        if (string.IsNullOrWhiteSpace(profile.UserName))
        {
            profile.UserName = profile.Anonymous ? "anonymous" : string.Empty;
        }

        if (profile.Anonymous && string.IsNullOrWhiteSpace(profile.Password))
        {
            profile.Password = "guest@";
        }

        profile.PassiveMode = true;
        return profile;
    }

    private static bool TryCreateProfileFromUrl(string text, out FtpConnectionProfile profile, out string error)
    {
        profile = FtpConnectionProfile.CreateDefault();
        error = string.Empty;

        if (!Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri) || !string.Equals(uri.Scheme, Uri.UriSchemeFtp, StringComparison.OrdinalIgnoreCase))
        {
            error = "Укажите FTP URL вида ftp://host:21/path.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            error = "В URL нет сервера.";
            return false;
        }

        profile.Name = uri.Host;
        profile.Host = uri.Host;
        profile.Port = uri.IsDefaultPort ? 21 : uri.Port;
        profile.RemoteDirectory = string.IsNullOrWhiteSpace(uri.AbsolutePath) || uri.AbsolutePath == "/" ? string.Empty : Uri.UnescapeDataString(uri.AbsolutePath);

        if (!string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            var userInfo = uri.UserInfo.Split(':', 2);
            profile.Anonymous = false;
            profile.UserName = Uri.UnescapeDataString(userInfo[0]);
            profile.Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
        }

        return true;
    }

    private static IEnumerable<TreeNode> FlattenNodes(TreeNodeCollection nodes)
    {
        foreach (TreeNode node in nodes)
        {
            yield return node;
            foreach (var child in FlattenNodes(node.Nodes))
            {
                yield return child;
            }
        }
    }

    private static ImageList CreateImageList()
    {
        var images = new ImageList
        {
            ColorDepth = ColorDepth.Depth32Bit,
            ImageSize = new Size(16, 16)
        };
        images.Images.Add("folder", ShellIconProvider.GetSmallIcon(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), true, false));
        images.Images.Add("ftp", ToolbarIconFactory.FtpClient());
        return images;
    }

    private static Button CreateButton(string text, EventHandler click)
    {
        var button = new Button { Text = text };
        button.Click += click;
        return StyleButton(button);
    }

    private static Button StyleButton(Button button)
    {
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(0, 4, 0, 4);
        return button;
    }

    private sealed record GroupTag(string Name);
}
