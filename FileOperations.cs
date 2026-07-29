using System.IO.Compression;

namespace AzertyCommander;

internal static class FileOperations
{
    public static Task CopyAsync(IReadOnlyList<FileSystemEntry> entries, string targetDirectory, IProgress<OperationProgress> progress, CancellationToken token)
    {
        return Task.Run(() =>
        {
            var operations = PrepareOperationItems(entries, targetDirectory);
            var total = Math.Max(1, CountEntries(operations.Select(operation => operation.Entry)));
            var current = 0;

            foreach (var (entry, destination) in operations)
            {
                token.ThrowIfCancellationRequested();
                if (entry.IsDirectory)
                {
                    CopyDirectory(entry.FullPath, destination, progress, token, ref current, total);
                }
                else
                {
                    CopyFile(entry.FullPath, destination, progress, token, ref current, total);
                }
            }
        }, token);
    }

    public static Task MoveAsync(IReadOnlyList<FileSystemEntry> entries, string targetDirectory, IProgress<OperationProgress> progress, CancellationToken token)
    {
        return Task.Run(() =>
        {
            var operations = PrepareOperationItems(entries, targetDirectory);
            var total = Math.Max(1, CountEntries(operations.Select(operation => operation.Entry)));
            var current = 0;

            foreach (var (entry, destination) in operations)
            {
                token.ThrowIfCancellationRequested();
                if (entry.IsDirectory)
                {
                    MoveDirectory(entry.FullPath, destination, progress, token, ref current, total);
                }
                else
                {
                    MoveFile(entry.FullPath, destination, progress, token, ref current, total);
                }
            }
        }, token);
    }

    public static Task CreateZipAsync(IReadOnlyList<FileSystemEntry> entries, string zipPath, IProgress<OperationProgress> progress, CancellationToken token)
    {
        return Task.Run(() =>
        {
            var total = Math.Max(1, CountEntries(entries));
            var current = 0;

            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(zipPath) ?? ".");
            using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            var zipFullPath = Path.GetFullPath(zipPath);

            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested();
                if (entry.IsDirectory)
                {
                    AddDirectoryToZip(archive, entry.FullPath, entry.Name, zipFullPath, progress, token, ref current, total);
                }
                else
                {
                    AddFileToZip(archive, entry.FullPath, entry.Name, zipFullPath, progress, token, ref current, total);
                }
            }
        }, token);
    }

    public static Task ExtractZipAsync(IReadOnlyList<string> zipPaths, string targetDirectory, IProgress<OperationProgress> progress, CancellationToken token)
    {
        return Task.Run(() =>
        {
            var total = Math.Max(1, CountZipEntries(zipPaths));
            var current = 0;

            foreach (var zipPath in zipPaths)
            {
                token.ThrowIfCancellationRequested();
                var destinationRoot = zipPaths.Count == 1
                    ? targetDirectory
                    : Path.Combine(targetDirectory, Path.GetFileNameWithoutExtension(zipPath));
                Directory.CreateDirectory(destinationRoot);

                using var archive = ZipFile.OpenRead(zipPath);
                foreach (var entry in archive.Entries)
                {
                    token.ThrowIfCancellationRequested();
                    var destinationPath = GetSafeExtractPath(destinationRoot, entry.FullName);

                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                        entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                    {
                        Directory.CreateDirectory(destinationPath);
                    }
                    else
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? destinationRoot);
                        entry.ExtractToFile(destinationPath, true);
                    }

                    current++;
                    progress.Report(new OperationProgress(current, total, entry.FullName));
                }
            }
        }, token);
    }

    public static bool HasTopLevelConflicts(IReadOnlyList<FileSystemEntry> entries, string targetDirectory)
    {
        return entries.Any(entry =>
        {
            var destination = Path.Combine(targetDirectory, entry.Name);
            if (IsSamePath(entry.FullPath, destination))
            {
                return false;
            }

            return entry.IsDirectory ? Directory.Exists(destination) : File.Exists(destination);
        });
    }

    private static List<(FileSystemEntry Entry, string Destination)> PrepareOperationItems(IReadOnlyList<FileSystemEntry> entries, string targetDirectory)
    {
        var operations = new List<(FileSystemEntry Entry, string Destination)>();

        foreach (var entry in entries)
        {
            var destination = Path.GetFullPath(Path.Combine(targetDirectory, entry.Name));
            if (IsSamePath(entry.FullPath, destination))
            {
                continue;
            }

            if (entry.IsDirectory && IsSameOrChildPath(destination, entry.FullPath))
            {
                throw new InvalidOperationException("Нельзя копировать или перемещать папку внутрь самой себя.");
            }

            operations.Add((entry, destination));
        }

        return operations;
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory, IProgress<OperationProgress> progress, CancellationToken token, ref int current, int total)
    {
        Directory.CreateDirectory(destinationDirectory);
        current++;
        progress.Report(new OperationProgress(Math.Min(current, total), total, sourceDirectory));

        foreach (var file in SafeFiles(sourceDirectory))
        {
            token.ThrowIfCancellationRequested();
            CopyFile(file, Path.Combine(destinationDirectory, Path.GetFileName(file)), progress, token, ref current, total);
        }

        foreach (var directory in SafeDirectories(sourceDirectory))
        {
            token.ThrowIfCancellationRequested();
            CopyDirectory(directory, Path.Combine(destinationDirectory, Path.GetFileName(directory)), progress, token, ref current, total);
        }
    }

    private static void CopyFile(string sourceFile, string destinationFile, IProgress<OperationProgress> progress, CancellationToken token, ref int current, int total)
    {
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile) ?? ".");
        File.Copy(sourceFile, destinationFile, true);
        current++;
        progress.Report(new OperationProgress(Math.Min(current, total), total, sourceFile));
    }

    private static void MoveDirectory(string sourceDirectory, string destinationDirectory, IProgress<OperationProgress> progress, CancellationToken token, ref int current, int total)
    {
        token.ThrowIfCancellationRequested();

        try
        {
            if (!Directory.Exists(destinationDirectory))
            {
                Directory.Move(sourceDirectory, destinationDirectory);
                current += Math.Max(1, CountDirectoryEntries(destinationDirectory));
                progress.Report(new OperationProgress(Math.Min(current, total), total, sourceDirectory));
                return;
            }
        }
        catch
        {
            // Cross-volume moves and existing targets fall back to copy + delete.
        }

        CopyDirectory(sourceDirectory, destinationDirectory, progress, token, ref current, total);
        Directory.Delete(sourceDirectory, true);
    }

    private static void MoveFile(string sourceFile, string destinationFile, IProgress<OperationProgress> progress, CancellationToken token, ref int current, int total)
    {
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile) ?? ".");

        try
        {
            File.Move(sourceFile, destinationFile, true);
        }
        catch
        {
            File.Copy(sourceFile, destinationFile, true);
            File.Delete(sourceFile);
        }

        current++;
        progress.Report(new OperationProgress(Math.Min(current, total), total, sourceFile));
    }

    private static void AddDirectoryToZip(ZipArchive archive, string sourceDirectory, string relativeRoot, string zipPath, IProgress<OperationProgress> progress, CancellationToken token, ref int current, int total)
    {
        var hadEntries = false;
        foreach (var file in SafeFiles(sourceDirectory))
        {
            token.ThrowIfCancellationRequested();
            hadEntries = true;
            var relativePath = Path.Combine(relativeRoot, Path.GetFileName(file));
            AddFileToZip(archive, file, relativePath, zipPath, progress, token, ref current, total);
        }

        foreach (var directory in SafeDirectories(sourceDirectory))
        {
            token.ThrowIfCancellationRequested();
            hadEntries = true;
            AddDirectoryToZip(archive, directory, Path.Combine(relativeRoot, Path.GetFileName(directory)), zipPath, progress, token, ref current, total);
        }

        if (!hadEntries)
        {
            archive.CreateEntry(NormalizeZipName(relativeRoot) + "/");
            current++;
            progress.Report(new OperationProgress(Math.Min(current, total), total, relativeRoot));
        }
    }

    private static void AddFileToZip(ZipArchive archive, string sourceFile, string relativePath, string zipPath, IProgress<OperationProgress> progress, CancellationToken token, ref int current, int total)
    {
        token.ThrowIfCancellationRequested();
        if (string.Equals(Path.GetFullPath(sourceFile), zipPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        archive.CreateEntryFromFile(sourceFile, NormalizeZipName(relativePath), CompressionLevel.Optimal);
        current++;
        progress.Report(new OperationProgress(Math.Min(current, total), total, relativePath));
    }

    private static string GetSafeExtractPath(string destinationRoot, string entryName)
    {
        var rootFullPath = Path.GetFullPath(destinationRoot);
        var destinationPath = Path.GetFullPath(Path.Combine(rootFullPath, entryName));
        var rootWithSeparator = rootFullPath.EndsWith(Path.DirectorySeparatorChar)
            ? rootFullPath
            : rootFullPath + Path.DirectorySeparatorChar;

        if (!destinationPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(destinationPath, rootFullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("ZIP содержит путь вне папки распаковки.");
        }

        return destinationPath;
    }

    private static int CountEntries(IEnumerable<FileSystemEntry> entries)
    {
        return entries.Sum(entry => entry.IsDirectory ? CountDirectoryEntries(entry.FullPath) : 1);
    }

    private static int CountDirectoryEntries(string directory)
    {
        var count = 1;

        foreach (var file in SafeFiles(directory))
        {
            count++;
        }

        foreach (var childDirectory in SafeDirectories(directory))
        {
            count += CountDirectoryEntries(childDirectory);
        }

        return count;
    }

    private static int CountZipEntries(IEnumerable<string> zipPaths)
    {
        var count = 0;
        foreach (var zipPath in zipPaths)
        {
            using var archive = ZipFile.OpenRead(zipPath);
            count += Math.Max(1, archive.Entries.Count);
        }

        return count;
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

    private static string NormalizeZipName(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static bool IsSamePath(string first, string second)
    {
        return string.Equals(NormalizePathForCompare(first), NormalizePathForCompare(second), StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameOrChildPath(string candidatePath, string rootPath)
    {
        var candidate = EnsureTrailingSeparator(NormalizePathForCompare(candidatePath));
        var root = EnsureTrailingSeparator(NormalizePathForCompare(rootPath));
        return candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePathForCompare(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string EnsureTrailingSeparator(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }
}
