namespace AzertyCommander;

internal enum DirectoryComparisonMode
{
    DateAndSize,
    Sha256,
    Bytewise
}

internal sealed record DirectoryComparisonOptions(string FileMask, DirectoryComparisonMode Mode);

internal enum DirectoryComparisonKind
{
    OnlyLeft,
    OnlyRight,
    Different
}

internal enum DirectorySyncDirection
{
    LeftToRight,
    RightToLeft
}

internal sealed record DirectoryComparisonItem(
    string RelativePath,
    bool IsDirectory,
    DirectoryComparisonKind Kind,
    long? LeftSize,
    DateTime? LeftModified,
    long? RightSize,
    DateTime? RightModified)
{
    public string KindText => Kind switch
    {
        DirectoryComparisonKind.OnlyLeft => "Только слева",
        DirectoryComparisonKind.OnlyRight => "Только справа",
        _ => "Отличается"
    };

    public string LeftText => FormatSide(LeftSize, LeftModified, Kind != DirectoryComparisonKind.OnlyRight);
    public string RightText => FormatSide(RightSize, RightModified, Kind != DirectoryComparisonKind.OnlyLeft);

    private string FormatSide(long? size, DateTime? modified, bool exists)
    {
        if (!exists)
        {
            return "-";
        }

        if (IsDirectory)
        {
            return "<Папка>";
        }

        return $"{size.GetValueOrDefault():N0}  {modified:dd.MM.yyyy HH:mm}";
    }
}

internal sealed record DirectorySyncRequest(
    string LeftRoot,
    string RightRoot,
    DirectorySyncDirection Direction,
    IReadOnlyList<DirectoryComparisonItem> Items,
    bool Mirror = false);

internal static class DirectoryComparisonService
{
    private const int BufferSize = 1024 * 1024;

    public static Task<IReadOnlyList<DirectoryComparisonItem>> CompareAsync(
        string leftRoot,
        string rightRoot,
        bool compareContents,
        IProgress<OperationProgress>? progress,
        CancellationToken token)
    {
        return CompareAsync(leftRoot, rightRoot, new DirectoryComparisonOptions("*", compareContents ? DirectoryComparisonMode.Bytewise : DirectoryComparisonMode.DateAndSize), progress, token);
    }

    public static Task<IReadOnlyList<DirectoryComparisonItem>> CompareAsync(
        string leftRoot,
        string rightRoot,
        DirectoryComparisonOptions options,
        IProgress<OperationProgress>? progress,
        CancellationToken token)
    {
        return Task.Run<IReadOnlyList<DirectoryComparisonItem>>(() => Compare(leftRoot, rightRoot, options, progress, token), token);
    }

    public static Task CopyAsync(
        DirectorySyncRequest request,
        IProgress<OperationProgress> progress,
        CancellationToken token)
    {
        return Task.Run(() => Copy(request, progress, token), token);
    }

    private static IReadOnlyList<DirectoryComparisonItem> Compare(
        string leftRoot,
        string rightRoot,
        DirectoryComparisonOptions options,
        IProgress<OperationProgress>? progress,
        CancellationToken token)
    {
        var left = Snapshot(leftRoot, "Сканирование слева", options.FileMask, progress, token);
        var right = Snapshot(rightRoot, "Сканирование справа", options.FileMask, progress, token);
        var paths = left.Keys
            .Concat(right.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        var results = new List<DirectoryComparisonItem>();

        for (var index = 0; index < paths.Count; index++)
        {
            token.ThrowIfCancellationRequested();
            var path = paths[index];
            var hasLeft = left.TryGetValue(path, out var leftItem);
            var hasRight = right.TryGetValue(path, out var rightItem);
            progress?.Report(new OperationProgress(index, Math.Max(1, paths.Count), "Сравнение: " + path));

            if (!hasLeft)
            {
                results.Add(ToResult(path, DirectoryComparisonKind.OnlyRight, null, rightItem));
                continue;
            }

            if (!hasRight)
            {
                results.Add(ToResult(path, DirectoryComparisonKind.OnlyLeft, leftItem, null));
                continue;
            }

            if (leftItem!.IsDirectory != rightItem!.IsDirectory)
            {
                results.Add(ToResult(path, DirectoryComparisonKind.Different, leftItem, rightItem));
                continue;
            }

            if (leftItem.IsDirectory)
            {
                continue;
            }

            var different = leftItem.Size != rightItem.Size;
            if (!different && options.Mode == DirectoryComparisonMode.Bytewise)
            {
                try
                {
                    different = !FilesEqual(
                        Path.Combine(leftRoot, path),
                        Path.Combine(rightRoot, path),
                        token);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch
                {
                    different = true;
                }
            }
            else if (!different && options.Mode == DirectoryComparisonMode.Sha256)
            {
                try
                {
                    different = !string.Equals(
                        FileHashService.ComputeSha256Async(Path.Combine(leftRoot, path), token).GetAwaiter().GetResult(),
                        FileHashService.ComputeSha256Async(Path.Combine(rightRoot, path), token).GetAwaiter().GetResult(),
                        StringComparison.Ordinal);
                }
                catch (OperationCanceledException) { throw; }
                catch { different = true; }
            }
            else if (!different)
            {
                different = Math.Abs((leftItem.Modified - rightItem.Modified).TotalSeconds) > 2D;
            }

            if (different)
            {
                results.Add(ToResult(path, DirectoryComparisonKind.Different, leftItem, rightItem));
            }
        }

        progress?.Report(new OperationProgress(paths.Count, Math.Max(1, paths.Count), $"Найдено отличий: {results.Count}"));
        return results;
    }

    private static Dictionary<string, SnapshotItem> Snapshot(
        string root,
        string message,
        string fileMask,
        IProgress<OperationProgress>? progress,
        CancellationToken token)
    {
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(root);
        }

        var result = new Dictionary<string, SnapshotItem>(StringComparer.OrdinalIgnoreCase);
        var directories = new Stack<string>();
        directories.Push(root);
        var scanned = 0;

        while (directories.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            var directory = directories.Pop();
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateFileSystemEntries(directory).ToList();
            }
            catch
            {
                continue;
            }

            foreach (var path in children)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var relativePath = Path.GetRelativePath(root, path);
                    var attributes = File.GetAttributes(path);
                    var isDirectory = (attributes & FileAttributes.Directory) != 0;
                    if (isDirectory)
                    {
                        var info = new DirectoryInfo(path);
                        result[relativePath] = new SnapshotItem(true, null, info.LastWriteTime);
                        if ((attributes & FileAttributes.ReparsePoint) == 0)
                        {
                            directories.Push(path);
                        }
                    }
                    else
                    {
                        var info = new FileInfo(path);
                        if (MatchesMask(Path.GetFileName(path), fileMask))
                        {
                            result[relativePath] = new SnapshotItem(false, info.Length, info.LastWriteTime);
                        }
                    }

                    scanned++;
                    if (scanned % 64 == 0)
                    {
                        progress?.Report(new OperationProgress(0, 0, $"{message}: {relativePath}"));
                    }
                }
                catch
                {
                    // Unreadable entries are skipped, like in the file panels.
                }
            }
        }

        return result;
    }

    private static DirectoryComparisonItem ToResult(
        string path,
        DirectoryComparisonKind kind,
        SnapshotItem? left,
        SnapshotItem? right)
    {
        return new DirectoryComparisonItem(
            path,
            left?.IsDirectory ?? right?.IsDirectory ?? false,
            kind,
            left?.Size,
            left?.Modified,
            right?.Size,
            right?.Modified);
    }

    private static bool FilesEqual(string leftPath, string rightPath, CancellationToken token)
    {
        using var left = new FileStream(leftPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize, FileOptions.SequentialScan);
        using var right = new FileStream(rightPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize, FileOptions.SequentialScan);
        if (left.Length != right.Length)
        {
            return false;
        }

        var leftBuffer = new byte[BufferSize];
        var rightBuffer = new byte[BufferSize];
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var leftRead = left.Read(leftBuffer, 0, leftBuffer.Length);
            var rightRead = right.Read(rightBuffer, 0, rightBuffer.Length);
            if (leftRead != rightRead)
            {
                return false;
            }

            if (leftRead == 0)
            {
                return true;
            }

            if (!leftBuffer.AsSpan(0, leftRead).SequenceEqual(rightBuffer.AsSpan(0, rightRead)))
            {
                return false;
            }
        }
    }

    private static void Copy(DirectorySyncRequest request, IProgress<OperationProgress> progress, CancellationToken token)
    {
        var sourceRoot = request.Direction == DirectorySyncDirection.LeftToRight ? request.LeftRoot : request.RightRoot;
        var targetRoot = request.Direction == DirectorySyncDirection.LeftToRight ? request.RightRoot : request.LeftRoot;
        var sourceItems = request.Items
            .Select(item => item with { IsDirectory = Directory.Exists(SafeCombine(sourceRoot, item.RelativePath)) })
            .Where(item => item.IsDirectory
                ? Directory.Exists(SafeCombine(sourceRoot, item.RelativePath))
                : File.Exists(SafeCombine(sourceRoot, item.RelativePath)))
            .ToList();
        var normalizedItems = RemoveNestedDuplicates(sourceItems);
        var copyPlan = BuildCopyPlan(sourceRoot, normalizedItems, token);
        var totalBytes = copyPlan.Files.Sum(item => item.Size);
        var totalItems = Math.Max(1, copyPlan.Files.Count + copyPlan.Directories.Count);
        var completedItems = 0;
        var completedBytes = 0L;

        foreach (var relativePath in copyPlan.Directories)
        {
            token.ThrowIfCancellationRequested();
            var targetDirectory = SafeCombine(targetRoot, relativePath);
            if (File.Exists(targetDirectory))
            {
                File.Delete(targetDirectory);
            }
            Directory.CreateDirectory(targetDirectory);
            completedItems++;
            progress.Report(new OperationProgress(completedItems, totalItems, relativePath, completedBytes, totalBytes));
        }

        var buffer = new byte[BufferSize];
        foreach (var file in copyPlan.Files)
        {
            token.ThrowIfCancellationRequested();
            var destination = SafeCombine(targetRoot, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? targetRoot);
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, recursive: true);
            }

            using (var source = new FileStream(file.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan))
            using (var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.SequentialScan))
            {
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    var read = source.Read(buffer, 0, buffer.Length);
                    if (read == 0)
                    {
                        break;
                    }

                    target.Write(buffer, 0, read);
                    completedBytes += read;
                    progress.Report(new OperationProgress(completedItems, totalItems, file.RelativePath, completedBytes, totalBytes));
                }
            }

            File.SetLastWriteTime(destination, File.GetLastWriteTime(file.SourcePath));
            completedItems++;
            progress.Report(new OperationProgress(completedItems, totalItems, file.RelativePath, completedBytes, totalBytes));
        }

        if (request.Mirror)
        {
            var sourceMissingKind = request.Direction == DirectorySyncDirection.LeftToRight
                ? DirectoryComparisonKind.OnlyRight
                : DirectoryComparisonKind.OnlyLeft;
            var toDelete = request.Items.Where(item => item.Kind == sourceMissingKind)
                .OrderByDescending(item => item.RelativePath.Length)
                .ToList();
            foreach (var item in toDelete.Where(item => !item.IsDirectory))
            {
                token.ThrowIfCancellationRequested();
                var path = SafeCombine(targetRoot, item.RelativePath);
                if (File.Exists(path)) File.Delete(path);
                progress.Report(new OperationProgress(totalItems, totalItems, "Удалено лишнее: " + item.RelativePath, completedBytes, totalBytes));
            }
            foreach (var item in toDelete.Where(item => item.IsDirectory))
            {
                token.ThrowIfCancellationRequested();
                var path = SafeCombine(targetRoot, item.RelativePath);
                if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                progress.Report(new OperationProgress(totalItems, totalItems, "Удалена лишняя папка: " + item.RelativePath, completedBytes, totalBytes));
            }
        }
    }

    private static List<DirectoryComparisonItem> RemoveNestedDuplicates(IReadOnlyList<DirectoryComparisonItem> items)
    {
        var selectedDirectories = items
            .Where(item => item.IsDirectory)
            .Select(item => item.RelativePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            .OrderBy(path => path.Length)
            .ToList();
        var result = new List<DirectoryComparisonItem>();
        foreach (var item in items.OrderBy(item => item.RelativePath.Length))
        {
            var nested = selectedDirectories.Any(parent =>
                !string.Equals(parent, item.RelativePath, StringComparison.OrdinalIgnoreCase) &&
                item.RelativePath.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            if (!nested)
            {
                result.Add(item);
            }
        }

        return result;
    }

    private static CopyPlan BuildCopyPlan(string sourceRoot, IReadOnlyList<DirectoryComparisonItem> items, CancellationToken token)
    {
        var files = new Dictionary<string, CopyFile>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            token.ThrowIfCancellationRequested();
            var source = SafeCombine(sourceRoot, item.RelativePath);
            if (item.IsDirectory)
            {
                if (!Directory.Exists(source))
                {
                    continue;
                }

                var pending = new Stack<string>();
                pending.Push(source);
                while (pending.Count > 0)
                {
                    token.ThrowIfCancellationRequested();
                    var directory = pending.Pop();
                    directories.Add(Path.GetRelativePath(sourceRoot, directory));
                    foreach (var child in Directory.EnumerateFileSystemEntries(directory))
                    {
                        token.ThrowIfCancellationRequested();
                        var attributes = File.GetAttributes(child);
                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            directories.Add(Path.GetRelativePath(sourceRoot, child));
                            if ((attributes & FileAttributes.ReparsePoint) == 0)
                            {
                                pending.Push(child);
                            }
                        }
                        else
                        {
                            var relativePath = Path.GetRelativePath(sourceRoot, child);
                            files[relativePath] = new CopyFile(child, relativePath, new FileInfo(child).Length);
                        }
                    }
                }
            }
            else if (File.Exists(source))
            {
                files[item.RelativePath] = new CopyFile(source, item.RelativePath, new FileInfo(source).Length);
            }
        }

        return new CopyPlan(
            directories.OrderBy(path => path.Length).ThenBy(path => path, StringComparer.CurrentCultureIgnoreCase).ToList(),
            files.Values.OrderBy(file => file.RelativePath, StringComparer.CurrentCultureIgnoreCase).ToList());
    }

    private static string SafeCombine(string root, string relativePath)
    {
        var fullRoot = Path.GetFullPath(root);
        var combined = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        var prefix = fullRoot.EndsWith(Path.DirectorySeparatorChar) ? fullRoot : fullRoot + Path.DirectorySeparatorChar;
        if (!combined.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Некорректный относительный путь синхронизации.");
        }

        return combined;
    }

    private static bool MatchesMask(string name, string mask)
    {
        var parts = (string.IsNullOrWhiteSpace(mask) ? "*" : mask)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 || parts.Any(part => System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(part, name, ignoreCase: true));
    }

    private sealed record SnapshotItem(bool IsDirectory, long? Size, DateTime Modified);
    private sealed record CopyFile(string SourcePath, string RelativePath, long Size);
    private sealed record CopyPlan(IReadOnlyList<string> Directories, IReadOnlyList<CopyFile> Files);
}
