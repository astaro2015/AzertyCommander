using System.IO.Compression;
using System.Text;

namespace AzertyCommander;

internal static class FileOperations
{
    private const int CompareBufferSize = 1024 * 1024;
    private const int TransferBufferSize = 1024 * 1024;
    private static readonly Encoding[] ZipNameEncodings =
    [
        Encoding.UTF8,
        Encoding.GetEncoding(866),
        Encoding.GetEncoding(1251),
        Encoding.GetEncoding(437)
    ];

    public static Task CopyAsync(IReadOnlyList<FileSystemEntry> entries, string targetDirectory, IProgress<OperationProgress> progress, CancellationToken token)
    {
        return Task.Run(() =>
        {
            var operations = PrepareOperationItems(entries, targetDirectory);
            var state = new TransferProgressState(
                progress,
                Math.Max(1, CountEntries(operations.Select(operation => operation.Entry))),
                CountBytes(operations.Select(operation => operation.Entry)));

            foreach (var (entry, destination) in operations)
            {
                token.ThrowIfCancellationRequested();
                if (entry.IsDirectory)
                {
                    CopyDirectory(entry.FullPath, destination, state, token);
                }
                else
                {
                    CopyFile(entry.FullPath, destination, state, token);
                }
            }
        }, token);
    }

    public static Task<FileCompareResult> CompareFilesByBytesAsync(string leftPath, string rightPath, IProgress<OperationProgress> progress, CancellationToken token)
    {
        return Task.Run(() =>
        {
            var leftInfo = new FileInfo(leftPath);
            var rightInfo = new FileInfo(rightPath);
            if (!leftInfo.Exists)
            {
                throw new FileNotFoundException("Левый файл не найден.", leftPath);
            }

            if (!rightInfo.Exists)
            {
                throw new FileNotFoundException("Правый файл не найден.", rightPath);
            }

            if (leftInfo.Length != rightInfo.Length)
            {
                progress.Report(new OperationProgress(1, 1, "Размеры файлов отличаются."));
                return new FileCompareResult(false, leftInfo.Length, rightInfo.Length, null);
            }

            if (leftInfo.Length == 0)
            {
                progress.Report(new OperationProgress(1, 1, "Пустые файлы одинаковы."));
                return new FileCompareResult(true, 0, 0, null);
            }

            var leftBuffer = new byte[CompareBufferSize];
            var rightBuffer = new byte[CompareBufferSize];
            var processed = 0L;

            using var leftStream = new FileStream(leftPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, CompareBufferSize, FileOptions.SequentialScan);
            using var rightStream = new FileStream(rightPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, CompareBufferSize, FileOptions.SequentialScan);

            while (processed < leftInfo.Length)
            {
                token.ThrowIfCancellationRequested();
                var leftRead = ReadBlock(leftStream, leftBuffer, token);
                var rightRead = ReadBlock(rightStream, rightBuffer, token);

                if (leftRead != rightRead)
                {
                    return new FileCompareResult(false, leftInfo.Length, rightInfo.Length, processed);
                }

                for (var index = 0; index < leftRead; index++)
                {
                    if (leftBuffer[index] != rightBuffer[index])
                    {
                        return new FileCompareResult(false, leftInfo.Length, rightInfo.Length, processed + index);
                    }
                }

                processed += leftRead;
                var progressValue = (int)Math.Clamp(processed * 1000 / leftInfo.Length, 0, 1000);
                progress.Report(new OperationProgress(progressValue, 1000, $"Сравнение: {FormatBytes(processed)} из {FormatBytes(leftInfo.Length)}", processed, leftInfo.Length));
            }

            return new FileCompareResult(true, leftInfo.Length, rightInfo.Length, null);
        }, token);
    }

    public static Task MoveAsync(IReadOnlyList<FileSystemEntry> entries, string targetDirectory, IProgress<OperationProgress> progress, CancellationToken token)
    {
        return Task.Run(() =>
        {
            var operations = PrepareOperationItems(entries, targetDirectory);
            var state = new TransferProgressState(
                progress,
                Math.Max(1, CountEntries(operations.Select(operation => operation.Entry))),
                CountBytes(operations.Select(operation => operation.Entry)));

            foreach (var (entry, destination) in operations)
            {
                token.ThrowIfCancellationRequested();
                if (entry.IsDirectory)
                {
                    MoveDirectory(entry.FullPath, destination, state, token);
                }
                else
                {
                    MoveFile(entry.FullPath, destination, state, token);
                }
            }
        }, token);
    }

    public static Task CreateZipAsync(IReadOnlyList<FileSystemEntry> entries, string zipPath, IProgress<OperationProgress> progress, CancellationToken token)
    {
        return Task.Run(() =>
        {
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(zipPath) ?? ".");
            using var zipStream = new FileStream(zipPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, false, Encoding.UTF8);
            var zipFullPath = Path.GetFullPath(zipPath);
            var state = new TransferProgressState(
                progress,
                Math.Max(1, CountEntries(entries)),
                CountBytes(entries));

            foreach (var entry in entries)
            {
                token.ThrowIfCancellationRequested();
                if (entry.IsDirectory)
                {
                    AddDirectoryToZip(archive, entry.FullPath, entry.Name, zipFullPath, state, token);
                }
                else
                {
                    AddFileToZip(archive, entry.FullPath, entry.Name, zipFullPath, state, token);
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

                using var archive = OpenZipRead(zipPath);
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

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory, TransferProgressState state, CancellationToken token)
    {
        Directory.CreateDirectory(destinationDirectory);
        state.CompleteItem(sourceDirectory);

        foreach (var file in SafeFiles(sourceDirectory))
        {
            token.ThrowIfCancellationRequested();
            CopyFile(file, Path.Combine(destinationDirectory, Path.GetFileName(file)), state, token);
        }

        foreach (var directory in SafeDirectories(sourceDirectory))
        {
            token.ThrowIfCancellationRequested();
            CopyDirectory(directory, Path.Combine(destinationDirectory, Path.GetFileName(directory)), state, token);
        }
    }

    private static void CopyFile(string sourceFile, string destinationFile, TransferProgressState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile) ?? ".");
        CopyFileWithProgress(sourceFile, destinationFile, state, token);
        state.CompleteItem(sourceFile);
    }

    private static void MoveDirectory(string sourceDirectory, string destinationDirectory, TransferProgressState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        try
        {
            if (!Directory.Exists(destinationDirectory))
            {
                var bytes = CountDirectoryBytes(sourceDirectory);
                var items = Math.Max(1, CountDirectoryEntries(sourceDirectory));
                Directory.Move(sourceDirectory, destinationDirectory);
                state.AddCompletedBytes(bytes, sourceDirectory, force: true);
                state.CompleteItems(items, sourceDirectory);
                return;
            }
        }
        catch
        {
            // Cross-volume moves and existing targets fall back to copy + delete.
        }

        CopyDirectory(sourceDirectory, destinationDirectory, state, token);
        Directory.Delete(sourceDirectory, true);
    }

    private static void MoveFile(string sourceFile, string destinationFile, TransferProgressState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile) ?? ".");

        try
        {
            var bytes = GetFileLength(sourceFile);
            File.Move(sourceFile, destinationFile, true);
            state.AddCompletedBytes(bytes, sourceFile, force: true);
            state.CompleteItem(sourceFile);
            return;
        }
        catch
        {
            CopyFileWithProgress(sourceFile, destinationFile, state, token);
            File.Delete(sourceFile);
        }

        state.CompleteItem(sourceFile);
    }

    private static void CopyFileWithProgress(string sourceFile, string destinationFile, TransferProgressState state, CancellationToken token)
    {
        var buffer = new byte[TransferBufferSize];
        state.Report(sourceFile, force: true);

        using var source = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, TransferBufferSize, FileOptions.SequentialScan);
        using var destination = new FileStream(destinationFile, FileMode.Create, FileAccess.Write, FileShare.None, TransferBufferSize, FileOptions.SequentialScan);

        while (true)
        {
            token.ThrowIfCancellationRequested();
            var read = source.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            destination.Write(buffer, 0, read);
            state.AddCompletedBytes(read, sourceFile);
        }

        state.Report(sourceFile, force: true);
    }

    private static void AddDirectoryToZip(ZipArchive archive, string sourceDirectory, string relativeRoot, string zipPath, TransferProgressState state, CancellationToken token)
    {
        var hadEntries = false;
        foreach (var file in SafeFiles(sourceDirectory))
        {
            token.ThrowIfCancellationRequested();
            hadEntries = true;
            var relativePath = Path.Combine(relativeRoot, Path.GetFileName(file));
            AddFileToZip(archive, file, relativePath, zipPath, state, token);
        }

        foreach (var directory in SafeDirectories(sourceDirectory))
        {
            token.ThrowIfCancellationRequested();
            hadEntries = true;
            AddDirectoryToZip(archive, directory, Path.Combine(relativeRoot, Path.GetFileName(directory)), zipPath, state, token);
        }

        if (!hadEntries)
        {
            archive.CreateEntry(NormalizeZipName(relativeRoot) + "/");
            state.CompleteItem(relativeRoot);
        }
    }

    private static void AddFileToZip(ZipArchive archive, string sourceFile, string relativePath, string zipPath, TransferProgressState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (string.Equals(Path.GetFullPath(sourceFile), zipPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var buffer = new byte[TransferBufferSize];
        var entry = archive.CreateEntry(NormalizeZipName(relativePath), CompressionLevel.Optimal);
        state.Report(relativePath, force: true);

        using var source = new FileStream(sourceFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, TransferBufferSize, FileOptions.SequentialScan);
        using var destination = entry.Open();

        while (true)
        {
            token.ThrowIfCancellationRequested();
            var read = source.Read(buffer, 0, buffer.Length);
            if (read == 0)
            {
                break;
            }

            destination.Write(buffer, 0, read);
            state.AddCompletedBytes(read, relativePath);
        }

        state.CompleteItem(relativePath);
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

    private static long CountBytes(IEnumerable<FileSystemEntry> entries)
    {
        long total = 0;
        foreach (var entry in entries)
        {
            total += entry.IsDirectory ? CountDirectoryBytes(entry.FullPath) : GetFileLength(entry.FullPath);
        }

        return total;
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

    private static long CountDirectoryBytes(string directory)
    {
        long bytes = 0;

        foreach (var file in SafeFiles(directory))
        {
            bytes += GetFileLength(file);
        }

        foreach (var childDirectory in SafeDirectories(directory))
        {
            bytes += CountDirectoryBytes(childDirectory);
        }

        return bytes;
    }

    private static long GetFileLength(string file)
    {
        try
        {
            return new FileInfo(file).Length;
        }
        catch
        {
            return 0;
        }
    }

    private static int CountZipEntries(IEnumerable<string> zipPaths)
    {
        var count = 0;
        foreach (var zipPath in zipPaths)
        {
            using var archive = OpenZipRead(zipPath);
            count += Math.Max(1, archive.Entries.Count);
        }

        return count;
    }

    private static ZipArchive OpenZipRead(string zipPath)
    {
        var encoding = DetectZipEntryNameEncoding(zipPath);
        var stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            return new ZipArchive(stream, ZipArchiveMode.Read, false, encoding);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static Encoding DetectZipEntryNameEncoding(string zipPath)
    {
        var bestEncoding = Encoding.UTF8;
        var bestScore = int.MinValue;

        foreach (var encoding in ZipNameEncodings)
        {
            try
            {
                using var stream = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read, false, encoding);
                var score = ScoreZipEntryNames(archive.Entries.Select(entry => entry.FullName));
                if (score > bestScore)
                {
                    bestScore = score;
                    bestEncoding = encoding;
                }
            }
            catch
            {
                // Try the next legacy encoding candidate.
            }
        }

        return bestEncoding;
    }

    private static int ScoreZipEntryNames(IEnumerable<string> names)
    {
        var score = 0;
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                score -= 100;
                continue;
            }

            foreach (var ch in name)
            {
                if (ch == '\uFFFD')
                {
                    score -= 100;
                }
                else if (IsCommonCyrillic(ch))
                {
                    score += 10;
                }
                else if (IsRareCyrillicMojibake(ch) || IsBoxDrawing(ch))
                {
                    score -= 20;
                }
                else if (char.IsControl(ch))
                {
                    score -= 50;
                }
                else if (ch is '<' or '>' or ':' or '"' or '|' or '?' or '*')
                {
                    score -= 15;
                }
                else if (ch is '«' or '»' or '¤' or '¦' or '¬' or '°' or '±')
                {
                    score -= 6;
                }
                else if (ch < 128)
                {
                    score += 1;
                }
            }
        }

        return score;
    }

    private static bool IsCommonCyrillic(char ch)
    {
        return ch is >= 'А' and <= 'я' or 'Ё' or 'ё';
    }

    private static bool IsRareCyrillicMojibake(char ch)
    {
        return "ЉЊЃѓЄєЅѕІіЇїЈјЌќЎўЏџҐґ".Contains(ch);
    }

    private static bool IsBoxDrawing(char ch)
    {
        return ch is >= '\u2500' and <= '\u257F';
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

    private static int ReadBlock(Stream stream, byte[] buffer, CancellationToken token)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            token.ThrowIfCancellationRequested();
            var read = stream.Read(buffer, offset, buffer.Length - offset);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        return offset;
    }

    private static string FormatBytes(long value)
    {
        string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
        var size = (double)value;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{value:N0} {units[unit]}" : $"{size:N1} {units[unit]}";
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

    private sealed class TransferProgressState
    {
        private const int ProgressScale = 10_000;
        private readonly IProgress<OperationProgress> _progress;
        private readonly int _itemsTotal;
        private readonly long _bytesTotal;
        private DateTime _lastReportUtc = DateTime.MinValue;
        private int _itemsDone;
        private long _bytesDone;

        public TransferProgressState(IProgress<OperationProgress> progress, int itemsTotal, long bytesTotal)
        {
            _progress = progress;
            _itemsTotal = Math.Max(1, itemsTotal);
            _bytesTotal = Math.Max(0, bytesTotal);
        }

        public void AddCompletedBytes(long bytes, string message, bool force = false)
        {
            if (bytes > 0)
            {
                _bytesDone = Math.Min(_bytesTotal, _bytesDone + bytes);
            }

            Report(message, force);
        }

        public void CompleteItem(string message)
        {
            CompleteItems(1, message);
        }

        public void CompleteItems(int count, string message)
        {
            if (count > 0)
            {
                _itemsDone = Math.Min(_itemsTotal, _itemsDone + count);
            }

            Report(message, force: true);
        }

        public void Report(string message, bool force = false)
        {
            var now = DateTime.UtcNow;
            if (!force && (now - _lastReportUtc).TotalMilliseconds < 120)
            {
                return;
            }

            _lastReportUtc = now;
            if (_bytesTotal > 0)
            {
                var current = (int)Math.Clamp(_bytesDone * ProgressScale / _bytesTotal, 0, ProgressScale);
                _progress.Report(new OperationProgress(current, ProgressScale, message, _bytesDone, _bytesTotal));
                return;
            }

            _progress.Report(new OperationProgress(Math.Min(_itemsDone, _itemsTotal), _itemsTotal, message));
        }
    }
}
