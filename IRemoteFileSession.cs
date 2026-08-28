namespace AzertyCommander;

internal sealed record RemoteTransferProgress(long BytesTransferred, long? TotalBytes, string Name);

internal interface IRemoteFileSession : IDisposable
{
    string CurrentDirectory { get; }
    bool Connected { get; }
    Task ConnectAsync(FtpConnectionOptions options, CancellationToken token);
    Task<IReadOnlyList<FtpRemoteEntry>> ListAsync(CancellationToken token);
    Task<IReadOnlyList<FtpRemoteEntry>> ListAsync(string remoteDirectory, CancellationToken token);
    Task ChangeDirectoryAsync(string path, CancellationToken token);
    Task CreateDirectoryAsync(string path, CancellationToken token);
    Task DeleteFileAsync(string path, CancellationToken token);
    Task RemoveDirectoryAsync(string path, CancellationToken token);
    Task RenameAsync(string oldPath, string newPath, CancellationToken token);
    Task DownloadFileAsync(string remotePath, string localPath, IProgress<RemoteTransferProgress>? progress, CancellationToken token);
    Task UploadFileAsync(string localPath, string remotePath, IProgress<RemoteTransferProgress>? progress, CancellationToken token);
}
