using System.Diagnostics;
using Renci.SshNet;

namespace AzertyCommander;

internal sealed class SftpClientSession : IRemoteFileSession
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private SftpClient? _client;
    private FtpConnectionOptions? _options;
    private bool _disposed;

    public string CurrentDirectory { get; private set; } = "/";
    public bool Connected => _client?.IsConnected == true;

    public async Task ConnectAsync(FtpConnectionOptions options, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _options = options;
        await _lock.WaitAsync(token);
        try
        {
            _client?.Dispose();
            _client = new SftpClient(options.Host, options.Port, options.UserName, options.Password)
            {
                KeepAliveInterval = TimeSpan.FromSeconds(30),
                OperationTimeout = TimeSpan.FromMinutes(5)
            };
            await Task.Run(_client.Connect, token);
            CurrentDirectory = FtpClientSession.NormalizeRemotePath(_client.WorkingDirectory);
        }
        finally
        {
            _lock.Release();
        }
    }

    public Task<IReadOnlyList<FtpRemoteEntry>> ListAsync(CancellationToken token) => ListAsync(CurrentDirectory, token);

    public async Task<IReadOnlyList<FtpRemoteEntry>> ListAsync(string remoteDirectory, CancellationToken token)
    {
        await EnsureConnectedAsync(token);
        await _lock.WaitAsync(token);
        try
        {
            var path = FtpClientSession.NormalizeRemotePath(remoteDirectory);
            var result = new List<FtpRemoteEntry>();
            await foreach (var entry in _client!.ListDirectoryAsync(path, token))
            {
                if (entry.Name is "." or "..") continue;
                result.Add(new FtpRemoteEntry
                {
                    Name = entry.Name,
                    FullPath = FtpClientSession.NormalizeRemotePath(entry.FullName),
                    IsDirectory = entry.IsDirectory,
                    Size = entry.IsDirectory ? 0 : entry.Length,
                    Modified = entry.LastWriteTime
                });
            }
            return result.OrderByDescending(entry => entry.IsDirectory).ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }
        finally { _lock.Release(); }
    }

    public async Task ChangeDirectoryAsync(string path, CancellationToken token)
    {
        await EnsureConnectedAsync(token);
        await _lock.WaitAsync(token);
        try
        {
            var normalized = FtpClientSession.NormalizeRemotePath(path);
            var attributes = await _client!.GetAttributesAsync(normalized, token);
            if (!attributes.IsDirectory) throw new DirectoryNotFoundException(normalized);
            _client.ChangeDirectory(normalized);
            CurrentDirectory = FtpClientSession.NormalizeRemotePath(_client.WorkingDirectory);
        }
        finally { _lock.Release(); }
    }

    public Task CreateDirectoryAsync(string path, CancellationToken token) => RunLockedAsync(client => client.CreateDirectoryAsync(FtpClientSession.NormalizeRemotePath(path), token), token);
    public Task DeleteFileAsync(string path, CancellationToken token) => RunLockedAsync(client => client.DeleteFileAsync(FtpClientSession.NormalizeRemotePath(path), token), token);
    public Task RemoveDirectoryAsync(string path, CancellationToken token) => RunLockedAsync(client => client.DeleteDirectoryAsync(FtpClientSession.NormalizeRemotePath(path), token), token);
    public Task RenameAsync(string oldPath, string newPath, CancellationToken token) => RunLockedAsync(client => client.RenameFileAsync(FtpClientSession.NormalizeRemotePath(oldPath), FtpClientSession.NormalizeRemotePath(newPath), token), token);

    public async Task DownloadFileAsync(string remotePath, string localPath, IProgress<RemoteTransferProgress>? progress, CancellationToken token)
    {
        await EnsureConnectedAsync(token);
        await _lock.WaitAsync(token);
        try
        {
            var normalized = FtpClientSession.NormalizeRemotePath(remotePath);
            var remoteSize = (await _client!.GetAttributesAsync(normalized, token)).Size;
            Directory.CreateDirectory(Path.GetDirectoryName(localPath) ?? ".");
            var offset = _options?.ResumeTransfers == true && File.Exists(localPath) ? Math.Min(new FileInfo(localPath).Length, remoteSize) : 0L;
            await using var source = await _client.OpenAsync(normalized, FileMode.Open, FileAccess.Read, token);
            await using var destination = new FileStream(localPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, 1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (offset == 0) destination.SetLength(0);
            source.Position = offset;
            destination.Position = offset;
            await CopyAsync(source, destination, Path.GetFileName(localPath), offset, remoteSize, progress, token);
        }
        finally { _lock.Release(); }
    }

    public async Task UploadFileAsync(string localPath, string remotePath, IProgress<RemoteTransferProgress>? progress, CancellationToken token)
    {
        await EnsureConnectedAsync(token);
        await _lock.WaitAsync(token);
        try
        {
            var normalized = FtpClientSession.NormalizeRemotePath(remotePath);
            var localSize = new FileInfo(localPath).Length;
            var offset = 0L;
            if (_options?.ResumeTransfers == true && await _client!.ExistsAsync(normalized, token))
            {
                offset = Math.Min((await _client.GetAttributesAsync(normalized, token)).Size, localSize);
            }
            await using var source = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 128, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var destination = await _client!.OpenAsync(normalized, FileMode.OpenOrCreate, FileAccess.Write, token);
            if (offset == 0) destination.SetLength(0);
            source.Position = offset;
            destination.Position = offset;
            await CopyAsync(source, destination, Path.GetFileName(localPath), offset, localSize, progress, token);
        }
        finally { _lock.Release(); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _client?.Disconnect(); } catch { }
        _client?.Dispose();
        _lock.Dispose();
    }

    private async Task RunLockedAsync(Func<SftpClient, Task> action, CancellationToken token)
    {
        await EnsureConnectedAsync(token);
        await _lock.WaitAsync(token);
        try { await action(_client!); }
        finally { _lock.Release(); }
    }

    private async Task EnsureConnectedAsync(CancellationToken token)
    {
        if (Connected) return;
        if (_options?.AutoReconnect != true) throw new IOException("SFTP соединение разорвано.");
        var desiredDirectory = CurrentDirectory;
        await ConnectAsync(_options, token);
        if (desiredDirectory != "/") await ChangeDirectoryAsync(desiredDirectory, token);
    }

    private async Task CopyAsync(Stream source, Stream destination, string name, long initial, long total, IProgress<RemoteTransferProgress>? progress, CancellationToken token)
    {
        var buffer = new byte[1024 * 128];
        var copied = initial;
        var currentRun = 0L;
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            var read = await source.ReadAsync(buffer, token);
            if (read == 0) break;
            await destination.WriteAsync(buffer.AsMemory(0, read), token);
            copied += read;
            currentRun += read;
            progress?.Report(new RemoteTransferProgress(copied, total, name));
            var limit = _options?.SpeedLimitKbps ?? 0;
            if (limit > 0)
            {
                var expected = TimeSpan.FromSeconds(currentRun / (limit * 1024D));
                var delay = expected - stopwatch.Elapsed;
                if (delay > TimeSpan.FromMilliseconds(2)) await Task.Delay(delay, token);
            }
        }
    }
}
