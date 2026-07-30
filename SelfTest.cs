using System.IO.Compression;
using System.Net;
using System.Text;

namespace AzertyCommander;

internal static class SelfTest
{
    public static bool Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "AzertyCommanderSelfTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            var left = Path.Combine(root, "left");
            var right = Path.Combine(root, "right");
            Directory.CreateDirectory(left);
            Directory.CreateDirectory(right);
            File.WriteAllText(Path.Combine(left, "one.txt"), "hello");
            Directory.CreateDirectory(Path.Combine(left, "folder"));
            File.WriteAllText(Path.Combine(left, "folder", "two.txt"), "world");

            var entries = new[]
            {
                new FileSystemEntry("one.txt", Path.Combine(left, "one.txt"), false, false, 5, DateTime.Now, FileAttributes.Archive),
                new FileSystemEntry("folder", Path.Combine(left, "folder"), true, false, null, DateTime.Now, FileAttributes.Directory)
            };

            var progress = new RecordedProgress();
            FileOperations.CopyAsync(entries, right, progress, CancellationToken.None).GetAwaiter().GetResult();
            if (!File.Exists(Path.Combine(right, "one.txt")) || !File.Exists(Path.Combine(right, "folder", "two.txt")))
            {
                Console.Error.WriteLine("Copy check failed.");
                return false;
            }

            if (!progress.Items.Any(item => item.BytesTotal >= 10 && item.BytesDone > 0))
            {
                Console.Error.WriteLine("Copy byte progress check failed.");
                return false;
            }

            var sameCompare = FileOperations.CompareFilesByBytesAsync(
                Path.Combine(left, "one.txt"),
                Path.Combine(right, "one.txt"),
                progress,
                CancellationToken.None).GetAwaiter().GetResult();
            if (!sameCompare.AreEqual)
            {
                Console.Error.WriteLine("Byte compare equal check failed.");
                return false;
            }

            var differentSameSize = Path.Combine(right, "different.txt");
            File.WriteAllText(differentSameSize, "jello");
            var contentCompare = FileOperations.CompareFilesByBytesAsync(
                Path.Combine(left, "one.txt"),
                differentSameSize,
                progress,
                CancellationToken.None).GetAwaiter().GetResult();
            if (contentCompare.AreEqual || contentCompare.FirstDifferenceOffset != 0)
            {
                Console.Error.WriteLine("Byte compare content difference check failed.");
                return false;
            }

            var differentSize = Path.Combine(right, "different-size.txt");
            File.WriteAllText(differentSize, "hello!");
            var sizeCompare = FileOperations.CompareFilesByBytesAsync(
                Path.Combine(left, "one.txt"),
                differentSize,
                progress,
                CancellationToken.None).GetAwaiter().GetResult();
            if (sizeCompare.AreEqual || sizeCompare.FirstDifferenceOffset is not null)
            {
                Console.Error.WriteLine("Byte compare size difference check failed.");
                return false;
            }

            FileOperations.CopyAsync(new[] { entries[0] }, left, progress, CancellationToken.None).GetAwaiter().GetResult();
            try
            {
                FileOperations.CopyAsync(new[] { entries[1] }, Path.Combine(left, "folder"), progress, CancellationToken.None).GetAwaiter().GetResult();
                Console.Error.WriteLine("Nested directory guard check failed.");
                return false;
            }
            catch (InvalidOperationException)
            {
                // Expected: a folder cannot be copied into itself.
            }

            var zipPath = Path.Combine(root, "packed.zip");
            var zipProgress = new RecordedProgress();
            FileOperations.CreateZipAsync(entries, zipPath, zipProgress, CancellationToken.None).GetAwaiter().GetResult();
            if (!File.Exists(zipPath))
            {
                Console.Error.WriteLine("ZIP create check failed.");
                return false;
            }

            if (!zipProgress.Items.Any(item => item.BytesDone > 0 && item.BytesTotal >= 10))
            {
                Console.Error.WriteLine("ZIP byte progress check failed.");
                return false;
            }

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                if (archive.GetEntry("one.txt") is null || archive.GetEntry("folder/two.txt") is null)
                {
                    Console.Error.WriteLine("ZIP content check failed.");
                    return false;
                }
            }

            var extract = Path.Combine(root, "extract");
            FileOperations.ExtractZipAsync(new[] { zipPath }, extract, progress, CancellationToken.None).GetAwaiter().GetResult();
            if (!File.Exists(Path.Combine(extract, "one.txt")) || !File.Exists(Path.Combine(extract, "folder", "two.txt")))
            {
                Console.Error.WriteLine("ZIP extract check failed.");
                return false;
            }

            var legacyZip = Path.Combine(root, "legacy-cp866.zip");
            var legacyName = "Кулер июль 26_2К.mp4";
            CreateSingleEntryZip(legacyZip, legacyName, Encoding.GetEncoding(866));
            var legacyExtract = Path.Combine(root, "legacy-extract");
            FileOperations.ExtractZipAsync(new[] { legacyZip }, legacyExtract, progress, CancellationToken.None).GetAwaiter().GetResult();
            if (!File.Exists(Path.Combine(legacyExtract, legacyName)))
            {
                Console.Error.WriteLine("Legacy ZIP name encoding check failed.");
                return false;
            }

            if (!ShellContextMenu.CanCreateForPaths(new[] { Path.Combine(left, "one.txt") }))
            {
                Console.Error.WriteLine("Shell context menu check failed.");
                return false;
            }

            var ftpRoot = Path.Combine(root, "ftp-root");
            Directory.CreateDirectory(ftpRoot);
            File.WriteAllText(Path.Combine(ftpRoot, "remote.txt"), "remote");
            using (var ftpServer = new SimpleFtpServer(new SimpleFtpServerOptions
            {
                RootDirectory = ftpRoot,
                ListenAddress = IPAddress.Loopback,
                Port = 0,
                PassivePortStart = 0,
                PassivePortEnd = 0,
                AllowAnonymous = true,
                ReadOnly = false
            }))
            {
                ftpServer.Start();
                using var ftpClient = new FtpClientSession();
                ftpClient.ConnectAsync(new FtpConnectionOptions
                {
                    Host = "127.0.0.1",
                    Port = ftpServer.ActualPort
                }, CancellationToken.None).GetAwaiter().GetResult();

                var remoteList = ftpClient.ListAsync(CancellationToken.None).GetAwaiter().GetResult();
                if (!remoteList.Any(entry => entry.Name == "remote.txt"))
                {
                    Console.Error.WriteLine("FTP list check failed.");
                    return false;
                }

                var ftpDownload = Path.Combine(root, "ftp-download");
                Directory.CreateDirectory(ftpDownload);
                ftpClient.DownloadFileAsync("/remote.txt", Path.Combine(ftpDownload, "remote.txt"), null, CancellationToken.None).GetAwaiter().GetResult();
                if (File.ReadAllText(Path.Combine(ftpDownload, "remote.txt")) != "remote")
                {
                    Console.Error.WriteLine("FTP download check failed.");
                    return false;
                }

                var uploadSource = Path.Combine(root, "upload.txt");
                File.WriteAllText(uploadSource, "upload");
                ftpClient.UploadFileAsync(uploadSource, "/uploaded.txt", null, CancellationToken.None).GetAwaiter().GetResult();
                ftpClient.CreateDirectoryAsync("/made", CancellationToken.None).GetAwaiter().GetResult();
                ftpClient.RenameAsync("/uploaded.txt", "/renamed.txt", CancellationToken.None).GetAwaiter().GetResult();
                ftpClient.DeleteFileAsync("/renamed.txt", CancellationToken.None).GetAwaiter().GetResult();
                ftpClient.RemoveDirectoryAsync("/made", CancellationToken.None).GetAwaiter().GetResult();
                if (File.Exists(Path.Combine(ftpRoot, "renamed.txt")) || Directory.Exists(Path.Combine(ftpRoot, "made")))
                {
                    Console.Error.WriteLine("FTP write operations check failed.");
                    return false;
                }
            }

            using var form = new MainForm();
            if (!form.Text.StartsWith("AZERTY Commander ", StringComparison.Ordinal))
            {
                Console.Error.WriteLine("Main form check failed.");
                return false;
            }

            if (form.MainMenuStrip is not null && form.MainMenuStrip.Height < 32)
            {
                Console.Error.WriteLine("Main menu height check failed.");
                return false;
            }

            if (DescendantControls(form).OfType<StatusStrip>().Any())
            {
                Console.Error.WriteLine("Main status strip removal check failed.");
                return false;
            }

            if (!DescendantControls(form).OfType<Label>().Any(label => label.Text.EndsWith('>')))
            {
                Console.Error.WriteLine("Command path label check failed.");
                return false;
            }

            var defaultTheme = new AppThemeSettings();
            if (!string.Equals(defaultTheme.SelectedBackgroundColor, "#5AB9F0", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(defaultTheme.SelectedTextColor, "#000000", StringComparison.OrdinalIgnoreCase))
            {
                Console.Error.WriteLine("Selection color defaults check failed.");
                return false;
            }

            var toolbarTips = DescendantControls(form)
                .OfType<ToolStrip>()
                .SelectMany(toolStrip => toolStrip.Items.OfType<ToolStripButton>())
                .Select(button => button.ToolTipText)
                .ToHashSet(StringComparer.Ordinal);
            if (!toolbarTips.Contains("Добавить выделение (Num+)") || !toolbarTips.Contains("Убрать выделение (Num-)"))
            {
                Console.Error.WriteLine("Selection toolbar buttons check failed.");
                return false;
            }

            if (!ExecutableIconLooksBlue())
            {
                Console.Error.WriteLine("Executable icon color check failed.");
                return false;
            }

            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-32000, -32000);
            form.Show();
            Application.DoEvents();
            var mainFavoritesButton = DescendantControls(form).OfType<Button>().FirstOrDefault(button => button.Text == "*");
            if (mainFavoritesButton is null)
            {
                Console.Error.WriteLine("Main favorite directories button check failed.");
                return false;
            }

            mainFavoritesButton.PerformClick();
            Application.DoEvents();
            mainFavoritesButton.PerformClick();
            Application.DoEvents();

            using var settingsForm = new SettingsForm(new AppThemeSettings());
            if (settingsForm.Text != "Настройки")
            {
                Console.Error.WriteLine("Settings form check failed.");
                return false;
            }

            if (settingsForm.ClientSize.Height < 560 || settingsForm.ClientSize.Width < 620)
            {
                Console.Error.WriteLine("Settings form size check failed.");
                return false;
            }

            var settingsButtonTexts = DescendantControls(settingsForm)
                .OfType<Button>()
                .Select(button => button.Text)
                .ToHashSet(StringComparer.Ordinal);
            if (!settingsButtonTexts.Contains("Сбросить") || !settingsButtonTexts.Contains("Применить"))
            {
                Console.Error.WriteLine("Settings form button text check failed.");
                return false;
            }

            var ftpProfile = new FtpConnectionProfile
            {
                Name = "Self-test",
                Host = "127.0.0.1",
                Port = 2121,
                Anonymous = true,
                LocalDirectory = left,
                Group = "LAN"
            };
            using var ftpConnections = new FtpConnectionManagerForm(new[] { ftpProfile }, new[] { "LAN" }, left);
            if (ftpConnections.Text != "Соединение с FTP-сервером" || ftpConnections.Profiles.Count != 1)
            {
                Console.Error.WriteLine("FTP connection manager check failed.");
                return false;
            }

            using var ftpEditor = new FtpConnectionEditorForm(ftpProfile, ftpConnections.Groups, left);
            if (ftpEditor.Text != "Настройка FTP-соединения")
            {
                Console.Error.WriteLine("FTP editor check failed.");
                return false;
            }

            using var ftpBrowser = new FtpClientForm(ftpProfile, () => left, () => { });
            if (!ftpBrowser.Text.StartsWith("FTP: ", StringComparison.Ordinal))
            {
                Console.Error.WriteLine("FTP browser form check failed.");
                return false;
            }

            using var searchForm = new SearchForm(left);
            if (searchForm.Text != "Поиск файлов" || searchForm.ClientSize.Width < 980)
            {
                Console.Error.WriteLine("Search form check failed.");
                return false;
            }

            using var selectionMaskForm = new SelectionMaskForm(true);
            if (selectionMaskForm.Text != "Добавить выделение" || selectionMaskForm.SelectedPattern != "*.*")
            {
                Console.Error.WriteLine("Selection mask form check failed.");
                return false;
            }

            using var driveSelection = new DriveSelectionForm(new[] { Path.GetPathRoot(left) ?? left });
            if (driveSelection.Text != "Выбор дисков")
            {
                Console.Error.WriteLine("Drive selection form check failed.");
                return false;
            }

            using var host = new Form
            {
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-32000, -32000),
                Size = new Size(800, 600)
            };
            using var panel = new FilePanel { Dock = DockStyle.Fill };
            var favoritesMenuRaised = false;
            panel.FavoritesMenuRequested += (_, args) =>
            {
                favoritesMenuRaised = args.ScreenLocation != Point.Empty;
            };
            host.Controls.Add(panel);
            host.Show();
            Application.DoEvents();
            panel.LoadPath(left);
            panel.FocusList();
            var favoritesButton = DescendantControls(panel).OfType<Button>().FirstOrDefault(button => button.Text == "*");
            if (favoritesButton is null)
            {
                Console.Error.WriteLine("Favorite directories button check failed.");
                return false;
            }

            favoritesButton.PerformClick();
            if (!favoritesMenuRaised)
            {
                Console.Error.WriteLine("Favorite directories menu event check failed.");
                return false;
            }

            var markCount = panel.MarkByPattern("*.txt", true);
            if (markCount == 0 || panel.SelectedEntries.Count == 0)
            {
                Console.Error.WriteLine("Selection by mask check failed.");
                return false;
            }

            var unmarkCount = panel.MarkByPattern("<one.*", false);
            if (unmarkCount == 0 || panel.SelectedEntries.Count != 0)
            {
                Console.Error.WriteLine("Unselect by regex check failed.");
                return false;
            }

            var folderPath = Path.Combine(left, "folder");
            if (!panel.SelectPath(folderPath))
            {
                Console.Error.WriteLine("Folder selection check failed.");
                return false;
            }

            WaitWithMessagePump(panel.ToggleFocusedSelectionAndCalculateDirectorySizeAsync());
            var folderEntry = panel.SelectedEntries.FirstOrDefault(entry => string.Equals(entry.FullPath, folderPath, StringComparison.OrdinalIgnoreCase));
            if (folderEntry?.Size != 5 || string.IsNullOrWhiteSpace(folderEntry.SizeText))
            {
                Console.Error.WriteLine("Space folder size check failed.");
                return false;
            }

            panel.ClearSelection();
            panel.ToggleFocusedSelectionAndMoveNext();
            panel.ToggleFocusedSelectionAndMoveNext();
            if (panel.SelectedEntries.Count == 0)
            {
                Console.Error.WriteLine("Insert selection check failed.");
                return false;
            }

            panel.ApplyTheme(new AppThemeSettings
            {
                FileFontSize = 10,
                FolderFontStyle = (int)FontStyle.Bold,
                RowHeight = 34,
                MarkedTextColor = "#CC0000",
                ListBackgroundColor = "#FFFFFF"
            });
            panel.ApplyColumnWidths(new Dictionary<string, int> { [nameof(FileSystemEntry.DisplayName)] = 220 });
            if (!panel.GetColumnWidths().TryGetValue(nameof(FileSystemEntry.DisplayName), out var nameWidth) || nameWidth < 220)
            {
                Console.Error.WriteLine("Column settings check failed.");
                return false;
            }

            var panelGrid = DescendantControls(panel).OfType<DataGridView>().Single();
            var nameColumn = panelGrid.Columns[nameof(FileSystemEntry.DisplayName)];
            if (!panelGrid.AllowUserToResizeColumns ||
                panelGrid.AutoSizeColumnsMode != DataGridViewAutoSizeColumnsMode.None ||
                nameColumn.AutoSizeMode != DataGridViewAutoSizeColumnMode.NotSet ||
                nameColumn.Resizable != DataGridViewTriState.True)
            {
                Console.Error.WriteLine("Column resize check failed.");
                return false;
            }

            Application.DoEvents();
            var initialFillTarget = GridFillTargetWidth(panelGrid);
            var initialColumnsWidth = VisibleColumnWidthSum(panelGrid);
            if (initialFillTarget > 0 && initialColumnsWidth < initialFillTarget - 6)
            {
                Console.Error.WriteLine("Column fill initial width check failed.");
                return false;
            }

            var previousNameWidth = nameColumn.Width;
            host.Size = new Size(1100, 600);
            Application.DoEvents();
            Application.DoEvents();
            var expandedFillTarget = GridFillTargetWidth(panelGrid);
            var expandedColumnsWidth = VisibleColumnWidthSum(panelGrid);
            if (expandedFillTarget > initialFillTarget + 50 &&
                (expandedColumnsWidth < expandedFillTarget - 6 || nameColumn.Width <= previousNameWidth))
            {
                Console.Error.WriteLine("Column proportional resize check failed.");
                return false;
            }

            Console.WriteLine("OK");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
            catch
            {
                // Temporary self-test cleanup is best effort.
            }
        }
    }

    private static void CreateSingleEntryZip(string zipPath, string entryName, Encoding entryNameEncoding)
    {
        using var stream = new FileStream(zipPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, false, entryNameEncoding);
        var entry = archive.CreateEntry(entryName);
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write("zip");
    }

    private static IEnumerable<Control> DescendantControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in DescendantControls(child))
            {
                yield return descendant;
            }
        }
    }

    private static void WaitWithMessagePump(Task task)
    {
        while (!task.IsCompleted)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }

        task.GetAwaiter().GetResult();
    }

    private static int VisibleColumnWidthSum(DataGridView grid)
    {
        return grid.Columns
            .Cast<DataGridViewColumn>()
            .Where(column => column.Visible)
            .Sum(column => column.Width);
    }

    private static int GridFillTargetWidth(DataGridView grid)
    {
        var width = grid.ClientSize.Width;
        if (grid.Controls.OfType<VScrollBar>().Any(scrollBar => scrollBar.Visible))
        {
            width -= SystemInformation.VerticalScrollBarWidth;
        }

        return Math.Max(0, width - 2);
    }

    private static bool ExecutableIconLooksBlue()
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (icon is null)
            {
                return false;
            }

            using var bitmap = icon.ToBitmap();
            long red = 0;
            long green = 0;
            long blue = 0;
            long count = 0;
            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    if (pixel.A < 16)
                    {
                        continue;
                    }

                    red += pixel.R;
                    green += pixel.G;
                    blue += pixel.B;
                    count++;
                }
            }

            return count > 0 && blue > green && green > red;
        }
        catch
        {
            return false;
        }
    }

    private sealed class RecordedProgress : IProgress<OperationProgress>
    {
        public List<OperationProgress> Items { get; } = new();

        public void Report(OperationProgress value)
        {
            Items.Add(value);
        }
    }
}
