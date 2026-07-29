using System.IO.Compression;
using System.Net;

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

            var progress = new Progress<OperationProgress>(_ => { });
            FileOperations.CopyAsync(entries, right, progress, CancellationToken.None).GetAwaiter().GetResult();
            if (!File.Exists(Path.Combine(right, "one.txt")) || !File.Exists(Path.Combine(right, "folder", "two.txt")))
            {
                Console.Error.WriteLine("Copy check failed.");
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
            FileOperations.CreateZipAsync(entries, zipPath, progress, CancellationToken.None).GetAwaiter().GetResult();
            if (!File.Exists(zipPath))
            {
                Console.Error.WriteLine("ZIP create check failed.");
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

            using var settingsForm = new SettingsForm(new AppThemeSettings());
            if (settingsForm.Text != "Настройки")
            {
                Console.Error.WriteLine("Settings form check failed.");
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
            host.Controls.Add(panel);
            host.Show();
            Application.DoEvents();
            panel.LoadPath(left);
            panel.FocusList();
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
            if (!panel.GetColumnWidths().TryGetValue(nameof(FileSystemEntry.DisplayName), out var nameWidth) || nameWidth != 220)
            {
                Console.Error.WriteLine("Column settings check failed.");
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
}
