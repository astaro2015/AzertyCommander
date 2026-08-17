using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;

namespace AzertyCommander;

internal sealed class SearchForm : Form
{
    private readonly ComboBox _maskBox = new();
    private readonly ComboBox _rootBox = new();
    private readonly ComboBox _depthBox = new();
    private readonly CheckBox _regexBox = new();
    private readonly CheckBox _includeFoldersBox = new();
    private readonly CheckBox _withTextBox = new();
    private readonly ComboBox _textBox = new();
    private readonly CheckBox _wholeWordsBox = new();
    private readonly CheckBox _caseSensitiveBox = new();
    private readonly CheckBox _textRegexBox = new();
    private readonly CheckBox _ansiBox = new();
    private readonly CheckBox _dosBox = new();
    private readonly CheckBox _utf16Box = new();
    private readonly CheckBox _utf8Box = new();
    private readonly Button _startButton = new();
    private readonly Button _cancelButton = new();
    private readonly Button _openButton = new();
    private readonly Button _viewButton = new();
    private readonly Button _newSearchButton = new();
    private readonly Button _feedButton = new();
    private readonly Label _resultsTitleLabel = new();
    private readonly Label _statusLabel = new();
    private readonly ListBox _resultsList = new();
    private readonly BindingList<SearchResult> _results = new();
    private CancellationTokenSource? _searchCancellation;

    public SearchForm(string root)
    {
        Text = "Поиск файлов";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(980, 640);
        ClientSize = new Size(1220, 840);
        Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

        BuildUi(root);
        UpdateTextSearchControls();
        UpdateResultButtons();
    }

    public event Action<string>? OpenRequested;
    public event Action<IReadOnlyList<FileSystemEntry>, string>? FeedResultsRequested;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_searchCancellation is not null && e.CloseReason == CloseReason.UserClosing)
        {
            _searchCancellation.Cancel();
            _statusLabel.Text = "Прерываю поиск...";
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }

    private void BuildUi(string root)
    {
        var rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(8)
        };
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));

        var main = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0, 0, 8, 0)
        };
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 368));
        main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        main.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildGeneralTab(root));
        tabs.TabPages.Add(BuildAdditionalTab());
        main.Controls.Add(tabs, 0, 0);
        main.Controls.Add(BuildResultsPanel(), 0, 1);

        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.AutoEllipsis = true;
        main.Controls.Add(_statusLabel, 0, 2);

        rootLayout.Controls.Add(main, 0, 0);
        rootLayout.Controls.Add(BuildRightButtons(), 1, 0);
        Controls.Add(rootLayout);
    }

    private TabPage BuildGeneralTab(string root)
    {
        var page = new TabPage("Общие параметры") { Padding = new Padding(8) };
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 168));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        panel.Controls.Add(BuildNameSearchPanel(root), 0, 0);
        panel.Controls.Add(new Label { Dock = DockStyle.Fill, BorderStyle = BorderStyle.Fixed3D }, 0, 1);
        panel.Controls.Add(BuildTextSearchPanel(), 0, 2);
        page.Controls.Add(panel);
        return page;
    }

    private Control BuildNameSearchPanel(string root)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 4
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        panel.Controls.Add(CreateLabel("Искать файлы:"), 0, 0);
        _maskBox.Dock = DockStyle.Fill;
        _maskBox.DropDownStyle = ComboBoxStyle.DropDown;
        _maskBox.Text = "*";
        _maskBox.Items.AddRange(["*", "*.exe", "*.txt;*.doc;*.docx", "*.zip;*.rar;*.7z"]);
        panel.SetColumnSpan(_maskBox, 4);
        panel.Controls.Add(_maskBox, 1, 0);

        panel.Controls.Add(CreateLabel("Место поиска:"), 0, 1);
        _rootBox.Dock = DockStyle.Fill;
        _rootBox.DropDownStyle = ComboBoxStyle.DropDown;
        _rootBox.Text = root;
        _rootBox.Items.Add(root);
        panel.Controls.Add(_rootBox, 1, 1);

        var browseButton = CreateSmallButton(">>", (_, _) => BrowseFolder());
        panel.Controls.Add(browseButton, 2, 1);
        panel.Controls.Add(CreateButton("Диски...", (_, _) => ChooseDrives()), 3, 1);

        var flags = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty
        };
        _regexBox.Text = "Рег. выраж.";
        _regexBox.AutoSize = true;
        _includeFoldersBox.Text = "Искать также каталоги";
        _includeFoldersBox.Checked = true;
        _includeFoldersBox.AutoSize = true;
        flags.Controls.Add(_regexBox);
        flags.Controls.Add(_includeFoldersBox);
        panel.SetColumnSpan(flags, 4);
        panel.Controls.Add(flags, 1, 2);

        panel.Controls.Add(CreateLabel("Глубина вложенности:"), 0, 3);
        _depthBox.Dock = DockStyle.Left;
        _depthBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _depthBox.Width = 250;
        _depthBox.Items.AddRange(["Все (неограниченная)", "Только текущий каталог", "1 уровень", "2 уровня", "3 уровня"]);
        _depthBox.SelectedIndex = 0;
        panel.SetColumnSpan(_depthBox, 4);
        panel.Controls.Add(_depthBox, 1, 3);

        return panel;
    }

    private Control BuildTextSearchPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 5
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));

        _withTextBox.Text = "С текстом:";
        _withTextBox.Dock = DockStyle.Fill;
        _withTextBox.CheckedChanged += (_, _) => UpdateTextSearchControls();
        panel.Controls.Add(_withTextBox, 0, 0);

        _textBox.Dock = DockStyle.Fill;
        _textBox.DropDownStyle = ComboBoxStyle.DropDown;
        panel.SetColumnSpan(_textBox, 2);
        panel.Controls.Add(_textBox, 1, 0);

        _wholeWordsBox.Text = "Только слова целиком";
        _caseSensitiveBox.Text = "Учитывать регистр символов";
        _textRegexBox.Text = "Регулярные выражения";
        _ansiBox.Text = "В кодировке ANSI (Windows)";
        _dosBox.Text = "В кодировке ASCII (DOS)";
        _utf16Box.Text = "UTF-16";
        _utf8Box.Text = "UTF-8";
        _ansiBox.Checked = true;
        _utf8Box.Checked = true;

        panel.Controls.Add(_wholeWordsBox, 1, 1);
        panel.Controls.Add(_caseSensitiveBox, 1, 2);
        panel.Controls.Add(_textRegexBox, 1, 3);
        panel.Controls.Add(_ansiBox, 2, 1);
        panel.Controls.Add(_dosBox, 2, 2);
        panel.Controls.Add(_utf16Box, 2, 3);
        panel.Controls.Add(_utf8Box, 2, 4);

        return panel;
    }

    private TabPage BuildAdditionalTab()
    {
        var page = new TabPage("Дополнительно") { Padding = new Padding(8) };
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 2,
            Height = 80
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        var hiddenBox = new CheckBox
        {
            Text = "Не пропускать скрытые и системные файлы",
            Checked = true,
            Enabled = false,
            Dock = DockStyle.Fill
        };
        panel.Controls.Add(hiddenBox, 0, 0);
        page.Controls.Add(panel);
        return page;
    }

    private Control BuildResultsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        _resultsTitleLabel.Text = "Результаты поиска";
        _resultsTitleLabel.Dock = DockStyle.Fill;
        _resultsTitleLabel.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(_resultsTitleLabel, 0, 0);

        _resultsList.Dock = DockStyle.Fill;
        _resultsList.DataSource = _results;
        _resultsList.DisplayMember = nameof(SearchResult.DisplayText);
        _resultsList.HorizontalScrollbar = true;
        _resultsList.IntegralHeight = false;
        _resultsList.SelectedIndexChanged += (_, _) => UpdateResultButtons();
        _resultsList.DoubleClick += (_, _) => OpenSelected();
        _resultsList.KeyDown += (_, args) =>
        {
            if (args.KeyCode == Keys.Enter)
            {
                args.SuppressKeyPress = true;
                OpenSelected();
            }
            else if (args.KeyCode == Keys.F3)
            {
                args.SuppressKeyPress = true;
                ViewSelected();
            }
            else if (args.KeyCode == Keys.F5)
            {
                args.SuppressKeyPress = true;
                FeedResultsToPanel();
            }
        };
        panel.Controls.Add(_resultsList, 0, 1);

        var bottomButtons = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            Padding = new Padding(0, 6, 0, 0)
        };
        bottomButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottomButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        bottomButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        bottomButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        bottomButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));

        _viewButton.Text = "F3 Просмотр";
        _viewButton.Dock = DockStyle.Fill;
        _viewButton.Click += (_, _) => ViewSelected();
        _newSearchButton.Text = "Новый поиск";
        _newSearchButton.Dock = DockStyle.Fill;
        _newSearchButton.Click += (_, _) => ResetSearch();
        _openButton.Text = "Перейти к файлу";
        _openButton.Dock = DockStyle.Fill;
        _openButton.Click += (_, _) => OpenSelected();
        _feedButton.Text = "Вывести всё в панель";
        _feedButton.Dock = DockStyle.Fill;
        _feedButton.Click += (_, _) => FeedResultsToPanel();

        bottomButtons.Controls.Add(_viewButton, 1, 0);
        bottomButtons.Controls.Add(_newSearchButton, 2, 0);
        bottomButtons.Controls.Add(_openButton, 3, 0);
        bottomButtons.Controls.Add(_feedButton, 4, 0);
        panel.Controls.Add(bottomButtons, 0, 2);

        return panel;
    }

    private Control BuildRightButtons()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 3,
            Height = 116
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        _startButton.Text = "Начать поиск";
        _startButton.Dock = DockStyle.Fill;
        _startButton.Click += async (_, _) => await StartSearchAsync();
        _cancelButton.Text = "Отмена";
        _cancelButton.Dock = DockStyle.Fill;
        _cancelButton.Click += (_, _) =>
        {
            if (_searchCancellation is null)
            {
                Close();
            }
            else
            {
                _searchCancellation.Cancel();
            }
        };
        var helpButton = CreateButton("Справка", (_, _) => ShowHelp());

        panel.Controls.Add(_startButton, 0, 0);
        panel.Controls.Add(_cancelButton, 0, 1);
        panel.Controls.Add(helpButton, 0, 2);
        return panel;
    }

    private async Task StartSearchAsync()
    {
        if (!TryCreateSearchOptions(out var options))
        {
            return;
        }

        RememberComboText(_maskBox);
        RememberComboText(_rootBox);
        if (_withTextBox.Checked)
        {
            RememberComboText(_textBox);
        }

        _results.Clear();
        _statusLabel.Text = "Идет поиск...";
        _resultsTitleLabel.Text = "Результаты поиска";
        SetSearching(true);
        _searchCancellation = new CancellationTokenSource();

        var progress = new Progress<SearchProgress>(report =>
        {
            if (report.Results is { Count: > 0 } results)
            {
                AddResults(results);
                UpdateResultStatus(report.SearchInterrupted);
            }
            else if (!string.IsNullOrWhiteSpace(report.CurrentPath))
            {
                _statusLabel.Text = report.CurrentPath;
            }
        });

        var interrupted = false;
        try
        {
            await Task.Run(() => Search(options, progress, _searchCancellation.Token));
        }
        catch (OperationCanceledException)
        {
            interrupted = true;
        }
        finally
        {
            _searchCancellation.Dispose();
            _searchCancellation = null;
            SetSearching(false);
        }

        UpdateResultStatus(interrupted);
        UpdateResultButtons();
    }

    private bool TryCreateSearchOptions(out SearchOptions options)
    {
        options = default!;

        var roots = ParseRoots(_rootBox.Text);
        if (roots.Count == 0)
        {
            MessageBox.Show(this, "Укажите место поиска.", "Поиск", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        var missingRoot = roots.FirstOrDefault(root => !Directory.Exists(root));
        if (missingRoot is not null)
        {
            MessageBox.Show(this, "Папка поиска не найдена:\n" + missingRoot, "Поиск", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        var mask = string.IsNullOrWhiteSpace(_maskBox.Text) ? "*" : _maskBox.Text.Trim();
        if (_regexBox.Checked && !TryCompileRegex(mask, false, out _, out var error))
        {
            MessageBox.Show(this, error, "Поиск", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        var textPattern = string.Empty;
        var encodings = new List<Encoding>();
        if (_withTextBox.Checked)
        {
            textPattern = _textBox.Text;
            if (string.IsNullOrWhiteSpace(textPattern))
            {
                MessageBox.Show(this, "Введите текст для поиска.", "Поиск", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (_ansiBox.Checked)
            {
                encodings.Add(Encoding.GetEncoding(1251));
            }

            if (_dosBox.Checked)
            {
                encodings.Add(Encoding.GetEncoding(866));
            }

            if (_utf16Box.Checked)
            {
                encodings.Add(Encoding.Unicode);
            }

            if (_utf8Box.Checked)
            {
                encodings.Add(Encoding.UTF8);
            }

            if (encodings.Count == 0)
            {
                MessageBox.Show(this, "Выберите хотя бы одну кодировку текста.", "Поиск", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (_textRegexBox.Checked && !TryCompileRegex(textPattern, _caseSensitiveBox.Checked, out _, out error))
            {
                MessageBox.Show(this, error, "Поиск", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
        }

        options = new SearchOptions(
            roots,
            mask,
            _regexBox.Checked,
            _includeFoldersBox.Checked,
            DepthFromSelection(),
            _withTextBox.Checked,
            textPattern,
            _caseSensitiveBox.Checked,
            _wholeWordsBox.Checked,
            _textRegexBox.Checked,
            encodings);
        return true;
    }

    private static void Search(SearchOptions options, IProgress<SearchProgress> progress, CancellationToken token)
    {
        var nameMatcher = CreateNameMatcher(options.FileMask, options.UseNameRegex);
        var textMatcher = options.SearchText
            ? CreateTextMatcher(options.TextPattern, options.TextCaseSensitive, options.TextWholeWords, options.TextRegex)
            : null;
        var reporter = new SearchProgressReporter(progress);

        try
        {
            foreach (var root in options.Roots)
            {
                SearchDirectory(root, 0, options, nameMatcher, textMatcher, reporter, token);
            }
        }
        finally
        {
            reporter.Flush();
        }
    }

    private static void SearchDirectory(
        string directory,
        int depth,
        SearchOptions options,
        Regex nameMatcher,
        Regex? textMatcher,
        SearchProgressReporter reporter,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        reporter.ReportCurrentPath(directory);

        foreach (var file in SafeFiles(directory))
        {
            token.ThrowIfCancellationRequested();
            var name = Path.GetFileName(file);
            if (!nameMatcher.IsMatch(name))
            {
                continue;
            }

            if (textMatcher is not null && !FileContainsText(file, textMatcher, options.TextEncodings, token))
            {
                continue;
            }

            reporter.AddResult(SearchResult.FromFile(new FileInfo(file)));
        }

        foreach (var childDirectory in SafeDirectories(directory))
        {
            token.ThrowIfCancellationRequested();
            var childName = Path.GetFileName(childDirectory);
            if (options.IncludeFolders && nameMatcher.IsMatch(childName))
            {
                reporter.AddResult(SearchResult.FromDirectory(new DirectoryInfo(childDirectory)));
            }

            if (options.MaxDepth is null || depth < options.MaxDepth.Value)
            {
                SearchDirectory(childDirectory, depth + 1, options, nameMatcher, textMatcher, reporter, token);
            }
        }
    }

    private static Regex CreateNameMatcher(string mask, bool useRegex)
    {
        if (useRegex)
        {
            return new Regex(mask, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }

        var parts = mask.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            parts = ["*"];
        }

        var expressions = parts.Select(part =>
        {
            if (!part.Contains('*') && !part.Contains('?'))
            {
                return Regex.Escape(part);
            }

            return "^" + Regex.Escape(part).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        });
        return new Regex(string.Join("|", expressions), RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    private static Regex CreateTextMatcher(string text, bool caseSensitive, bool wholeWords, bool useRegex)
    {
        var expression = useRegex ? text : Regex.Escape(text);
        if (wholeWords)
        {
            expression = @"(?<![\p{L}\p{Nd}_])" + expression + @"(?![\p{L}\p{Nd}_])";
        }

        var options = RegexOptions.Compiled;
        if (!caseSensitive)
        {
            options |= RegexOptions.IgnoreCase;
        }

        return new Regex(expression, options);
    }

    private static bool TryCompileRegex(string expression, bool caseSensitive, out Regex? regex, out string error)
    {
        regex = null;
        error = string.Empty;
        try
        {
            regex = new Regex(expression, caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
            return true;
        }
        catch (Exception ex)
        {
            error = "Ошибка регулярного выражения:\n" + ex.Message;
            return false;
        }
    }

    private static bool FileContainsText(string filePath, Regex matcher, IReadOnlyList<Encoding> encodings, CancellationToken token)
    {
        foreach (var encoding in encodings)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, encoding, true);
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    token.ThrowIfCancellationRequested();
                    if (matcher.IsMatch(line))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Skip unreadable/binary-locked files for text search.
            }
        }

        return false;
    }

    private void OpenSelected()
    {
        if (_resultsList.SelectedItem is SearchResult result)
        {
            OpenRequested?.Invoke(result.FullPath);
        }
    }

    private void ViewSelected()
    {
        if (_resultsList.SelectedItem is not SearchResult { IsDirectory: false } result)
        {
            return;
        }

        using var viewer = new TextViewerForm(result.FullPath);
        viewer.ShowDialog(this);
    }

    private void FeedResultsToPanel()
    {
        if (_results.Count == 0)
        {
            return;
        }

        var entries = _results
            .Where(result => result.Exists)
            .Select(result => result.ToFileSystemEntry())
            .ToList();
        if (entries.Count == 0)
        {
            MessageBox.Show(this, "Найденные файлы уже недоступны.", "Поиск", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var caption = $"Результаты поиска: {_maskBox.Text.Trim()}";
        FeedResultsRequested?.Invoke(entries, caption);
        Close();
    }

    private void ResetSearch()
    {
        _searchCancellation?.Cancel();
        _results.Clear();
        _resultsTitleLabel.Text = "Результаты поиска";
        _statusLabel.Text = string.Empty;
        _maskBox.Focus();
        UpdateResultButtons();
    }

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Выберите место поиска",
            SelectedPath = Directory.Exists(_rootBox.Text) ? _rootBox.Text : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _rootBox.Text = dialog.SelectedPath;
        }
    }

    private void ChooseDrives()
    {
        using var dialog = new DriveSelectionForm(ParseRoots(_rootBox.Text));
        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.SelectedRoots.Count > 0)
        {
            _rootBox.Text = string.Join("; ", dialog.SelectedRoots);
        }
    }

    private void ShowHelp()
    {
        MessageBox.Show(
            this,
            "Маски разделяются точкой с запятой: *.txt;*.cs. Если звездочек нет, поиск идет по вхождению в имени. Несколько мест поиска тоже можно разделить точкой с запятой.",
            "Поиск файлов",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void UpdateTextSearchControls()
    {
        var enabled = _withTextBox.Checked && _startButton.Enabled;
        foreach (var control in new Control[] { _textBox, _wholeWordsBox, _caseSensitiveBox, _textRegexBox, _ansiBox, _dosBox, _utf16Box, _utf8Box })
        {
            control.Enabled = enabled;
        }
    }

    private void UpdateResultButtons()
    {
        var selected = _resultsList.SelectedItem is SearchResult;
        _openButton.Enabled = selected;
        _viewButton.Enabled = _resultsList.SelectedItem is SearchResult { IsDirectory: false };
        _feedButton.Enabled = _searchCancellation is null && _results.Count > 0;
    }

    private void UpdateResultStatus(bool interrupted)
    {
        var files = _results.Count(result => !result.IsDirectory);
        var folders = _results.Count(result => result.IsDirectory);
        var text = $"Найдено: файлов - {files}, каталогов - {folders}";
        if (interrupted)
        {
            text += " - поиск прерван";
        }

        _resultsTitleLabel.Text = "Результаты поиска";
        _statusLabel.Text = text;
    }

    private void SetSearching(bool searching)
    {
        _startButton.Enabled = !searching;
        _cancelButton.Text = searching ? "Прервать" : "Отмена";
        _maskBox.Enabled = !searching;
        _rootBox.Enabled = !searching;
        _depthBox.Enabled = !searching;
        _regexBox.Enabled = !searching;
        _includeFoldersBox.Enabled = !searching;
        _withTextBox.Enabled = !searching;
        UpdateTextSearchControls();
        UpdateResultButtons();
    }

    private void AddResults(IReadOnlyList<SearchResult> results)
    {
        if (results.Count == 0)
        {
            return;
        }

        var hadSelection = _resultsList.SelectedIndex >= 0;
        _resultsList.BeginUpdate();
        _results.RaiseListChangedEvents = false;
        foreach (var result in results)
        {
            _results.Add(result);
        }

        _results.RaiseListChangedEvents = true;
        _results.ResetBindings();
        if (!hadSelection && _results.Count > 0)
        {
            _resultsList.SelectedIndex = 0;
        }

        _resultsList.EndUpdate();
        UpdateResultButtons();
    }

    private int? DepthFromSelection()
    {
        return _depthBox.SelectedIndex switch
        {
            1 => 0,
            2 => 1,
            3 => 2,
            4 => 3,
            _ => null
        };
    }

    private static IReadOnlyList<string> ParseRoots(string text)
    {
        return text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(root => root.Trim('"'))
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root =>
            {
                try
                {
                    return Path.GetFullPath(root);
                }
                catch
                {
                    return root;
                }
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void RememberComboText(ComboBox box)
    {
        var text = box.Text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        for (var index = box.Items.Count - 1; index >= 0; index--)
        {
            if (string.Equals(box.Items[index]?.ToString(), text, StringComparison.OrdinalIgnoreCase))
            {
                box.Items.RemoveAt(index);
            }
        }

        box.Items.Insert(0, text);
        while (box.Items.Count > 12)
        {
            box.Items.RemoveAt(box.Items.Count - 1);
        }
    }

    private static IEnumerable<string> SafeDirectories(string directory)
    {
        try
        {
            return Directory.EnumerateDirectories(directory).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> SafeFiles(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory).ToList();
        }
        catch
        {
            return Array.Empty<string>();
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

    private static Button CreateSmallButton(string text, EventHandler click)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            Margin = new Padding(4, 3, 4, 3)
        };
        button.Click += click;
        return button;
    }

    private sealed record SearchOptions(
        IReadOnlyList<string> Roots,
        string FileMask,
        bool UseNameRegex,
        bool IncludeFolders,
        int? MaxDepth,
        bool SearchText,
        string TextPattern,
        bool TextCaseSensitive,
        bool TextWholeWords,
        bool TextRegex,
        IReadOnlyList<Encoding> TextEncodings);

    private sealed record SearchProgress(IReadOnlyList<SearchResult>? Results, string? CurrentPath, bool SearchInterrupted);

    private sealed class SearchProgressReporter
    {
        private const int BatchSize = 96;
        private static readonly TimeSpan BatchInterval = TimeSpan.FromMilliseconds(120);
        private static readonly TimeSpan PathInterval = TimeSpan.FromMilliseconds(160);
        private readonly IProgress<SearchProgress> _progress;
        private readonly List<SearchResult> _pending = new();
        private DateTime _lastBatchUtc = DateTime.UtcNow;
        private DateTime _lastPathUtc = DateTime.MinValue;

        public SearchProgressReporter(IProgress<SearchProgress> progress)
        {
            _progress = progress;
        }

        public void AddResult(SearchResult result)
        {
            _pending.Add(result);
            if (_pending.Count >= BatchSize || DateTime.UtcNow - _lastBatchUtc >= BatchInterval)
            {
                Flush();
            }
        }

        public void ReportCurrentPath(string path)
        {
            var now = DateTime.UtcNow;
            if (now - _lastPathUtc < PathInterval)
            {
                return;
            }

            _lastPathUtc = now;
            _progress.Report(new SearchProgress(null, path, false));
        }

        public void Flush()
        {
            if (_pending.Count == 0)
            {
                return;
            }

            var results = _pending.ToArray();
            _pending.Clear();
            _lastBatchUtc = DateTime.UtcNow;
            _progress.Report(new SearchProgress(results, null, false));
        }
    }

    private sealed class SearchResult
    {
        public string Name { get; init; } = string.Empty;
        public string Directory { get; init; } = string.Empty;
        public string FullPath { get; init; } = string.Empty;
        public bool IsDirectory { get; init; }
        public long? Size { get; init; }
        public DateTime Modified { get; init; }
        public string DisplayText => FullPath;
        public bool Exists => IsDirectory ? System.IO.Directory.Exists(FullPath) : System.IO.File.Exists(FullPath);

        public FileSystemEntry ToFileSystemEntry()
        {
            var attributes = FileAttributes.Archive;
            if (Exists)
            {
                try
                {
                    attributes = File.GetAttributes(FullPath);
                }
                catch
                {
                    attributes = IsDirectory ? FileAttributes.Directory : FileAttributes.Archive;
                }
            }
            else if (IsDirectory)
            {
                attributes = FileAttributes.Directory;
            }

            return new FileSystemEntry(
                Name,
                FullPath,
                IsDirectory,
                isParent: false,
                Size,
                Modified,
                attributes,
                displayName: FullPath);
        }

        public static SearchResult FromFile(FileInfo file)
        {
            return new SearchResult
            {
                Name = file.Name,
                Directory = file.DirectoryName ?? string.Empty,
                FullPath = file.FullName,
                IsDirectory = false,
                Size = file.Length,
                Modified = file.LastWriteTime
            };
        }

        public static SearchResult FromDirectory(DirectoryInfo directory)
        {
            return new SearchResult
            {
                Name = directory.Name,
                Directory = directory.Parent?.FullName ?? string.Empty,
                FullPath = directory.FullName,
                IsDirectory = true,
                Size = null,
                Modified = directory.LastWriteTime
            };
        }
    }
}
