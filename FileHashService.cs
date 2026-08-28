using System.Buffers;
using System.Security.Cryptography;

namespace AzertyCommander;

internal sealed record FileHashResult(string Path, long Size, string Crc32, string Md5, string Sha256);

internal static class FileHashService
{
    private const int BufferSize = 1024 * 1024;
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static Task<FileHashResult> ComputeAsync(
        string path,
        IProgress<OperationProgress>? progress,
        CancellationToken token)
    {
        return Task.Run(() => Compute(path, progress, token), token);
    }

    public static Task<string> ComputeSha256Async(string path, CancellationToken token)
    {
        return Task.Run(() => ComputeSha256(path, token), token);
    }

    private static FileHashResult Compute(string path, IProgress<OperationProgress>? progress, CancellationToken token)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("Файл не найден.", path);
        }

        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize, FileOptions.SequentialScan);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        var crc = uint.MaxValue;
        var processed = 0L;
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                md5.AppendData(buffer, 0, read);
                sha256.AppendData(buffer, 0, read);
                crc = UpdateCrc32(crc, buffer.AsSpan(0, read));
                processed += read;
                progress?.Report(new OperationProgress(
                    info.Length == 0 ? 1 : (int)Math.Clamp(processed * 10_000 / info.Length, 0, 10_000),
                    10_000,
                    info.Name,
                    processed,
                    info.Length));
            }

            return new FileHashResult(
                info.FullName,
                info.Length,
                (~crc).ToString("X8"),
                Convert.ToHexString(md5.GetHashAndReset()),
                Convert.ToHexString(sha256.GetHashAndReset()));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static string ComputeSha256(string path, CancellationToken token)
    {
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, BufferSize, FileOptions.SequentialScan);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var read = stream.Read(buffer, 0, buffer.Length);
                if (read == 0)
                {
                    break;
                }

                sha256.AppendData(buffer, 0, read);
            }

            return Convert.ToHexString(sha256.GetHashAndReset());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static uint UpdateCrc32(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc = (crc >> 8) ^ CrcTable[(crc ^ value) & 0xFF];
        }

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? 0xEDB88320U ^ (value >> 1) : value >> 1;
            }

            table[index] = value;
        }

        return table;
    }
}
