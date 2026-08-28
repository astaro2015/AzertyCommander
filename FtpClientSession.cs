using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace AzertyCommander;

internal sealed class FtpClientSession : IRemoteFileSession
{
    private readonly Encoding _encoding = new UTF8Encoding(false);
    private readonly SemaphoreSlim _controlLock = new(1, 1);
    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private System.Threading.Timer? _keepAliveTimer;
    private DateTime _lastControlActivityUtc = DateTime.MinValue;
    private FtpConnectionOptions? _options;
    private bool _tlsEnabled;

    public string CurrentDirectory { get; private set; } = "/";

    public bool Connected => _client?.Connected == true;

    public async Task ConnectAsync(FtpConnectionOptions options, CancellationToken token)
    {
        Disconnect();
        _options = options;

        await _controlLock.WaitAsync(token);
        try
        {
            _client = new TcpClient { NoDelay = true };
            await _client.ConnectAsync(options.Host, options.Port, token);

            var stream = _client.GetStream();
            _reader = new StreamReader(stream, _encoding, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            _writer = new StreamWriter(stream, _encoding) { NewLine = "\r\n", AutoFlush = true };

            var welcome = await ReadReplyAsync(token);
            EnsurePositive(welcome, "FTP сервер не принял подключение.");

            if (options.UseTls)
            {
                var authReply = await SendCommandAsync("AUTH TLS", token);
                EnsurePositive(authReply, "FTP сервер не включил TLS.");
                var sslStream = new SslStream(stream, leaveInnerStreamOpen: false, ValidateServerCertificate);
                await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = options.Host,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.Online
                }, token);
                _reader = new StreamReader(sslStream, _encoding, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                _writer = new StreamWriter(sslStream, _encoding) { NewLine = "\r\n", AutoFlush = true };
                _tlsEnabled = true;
            }

            var userReply = await SendCommandAsync("USER " + CleanArgument(options.UserName), token);
            if (userReply.Code == 331)
            {
                var passReply = await SendCommandAsync("PASS " + CleanArgument(options.Password), token);
                EnsurePositive(passReply, "FTP сервер не принял пароль.");
            }
            else
            {
                EnsurePositive(userReply, "FTP сервер не принял пользователя.");
            }

            await TryCommandAsync("OPTS UTF8 ON", token);
            EnsurePositive(await SendCommandAsync("TYPE I", token), "FTP сервер не включил двоичный режим.");
            if (_tlsEnabled)
            {
                EnsurePositive(await SendCommandAsync("PBSZ 0", token), "FTP сервер не настроил TLS-защиту данных.");
                EnsurePositive(await SendCommandAsync("PROT P", token), "FTP сервер не включил шифрование данных.");
            }
            CurrentDirectory = await GetWorkingDirectoryAsync(token);
            StartKeepAlive();
        }
        finally
        {
            _controlLock.Release();
        }
    }

    public void Disconnect()
    {
        _keepAliveTimer?.Dispose();
        _keepAliveTimer = null;

        try
        {
            if (_writer is not null && Connected)
            {
                _writer.WriteLine("QUIT");
                _writer.Flush();
            }
        }
        catch
        {
            // Closing a network session is best effort.
        }

        _reader?.Dispose();
        _writer?.Dispose();
        _client?.Dispose();
        _reader = null;
        _writer = null;
        _client = null;
        _tlsEnabled = false;
        CurrentDirectory = "/";
    }

    public async Task<IReadOnlyList<FtpRemoteEntry>> ListAsync(CancellationToken token)
    {
        await EnsureConnectedAsync(token);
        await _controlLock.WaitAsync(token);
        try
        {
            return await ListCoreAsync(CurrentDirectory, token);
        }
        finally
        {
            _controlLock.Release();
        }
    }

    public async Task<IReadOnlyList<FtpRemoteEntry>> ListAsync(string remoteDirectory, CancellationToken token)
    {
        await EnsureConnectedAsync(token);
        await _controlLock.WaitAsync(token);
        try
        {
            return await ListCoreAsync(remoteDirectory, token);
        }
        finally
        {
            _controlLock.Release();
        }
    }

    private async Task<IReadOnlyList<FtpRemoteEntry>> ListCoreAsync(string remoteDirectory, CancellationToken token)
    {
        var path = NormalizeRemotePath(remoteDirectory);
        try
        {
            var mlsd = await ExecuteDataReadCommandAsync("MLSD " + path, token);
            return ParseMlsd(mlsd, path);
        }
        catch
        {
            var list = await ExecuteDataReadCommandAsync("LIST " + path, token);
            return ParseList(list, path);
        }
    }

    public async Task ChangeDirectoryAsync(string path, CancellationToken token)
    {
        await EnsureConnectedAsync(token);
        await _controlLock.WaitAsync(token);
        try
        {
            var reply = await SendCommandAsync("CWD " + NormalizeRemotePath(path), token);
            EnsurePositive(reply, "FTP сервер не открыл папку.");
            CurrentDirectory = await GetWorkingDirectoryAsync(token);
        }
        finally
        {
            _controlLock.Release();
        }
    }

    public async Task CreateDirectoryAsync(string path, CancellationToken token)
    {
        await EnsureConnectedAsync(token);
        await _controlLock.WaitAsync(token);
        try
        {
            var reply = await SendCommandAsync("MKD " + NormalizeRemotePath(path), token);
            EnsurePositive(reply, "FTP сервер не создал папку.");
        }
        finally
        {
            _controlLock.Release();
        }
    }

    public async Task DeleteFileAsync(string path, CancellationToken token)
    {
        await EnsureConnectedAsync(token);
        await _controlLock.WaitAsync(token);
        try
        {
            var reply = await SendCommandAsync("DELE " + NormalizeRemotePath(path), token);
            EnsurePositive(reply, "FTP сервер не удалил файл.");
        }
        finally
        {
            _controlLock.Release();
        }
    }

    public async Task RemoveDirectoryAsync(string path, CancellationToken token)
    {
        await EnsureConnectedAsync(token);
        await _controlLock.WaitAsync(token);
        try
        {
            var reply = await SendCommandAsync("RMD " + NormalizeRemotePath(path), token);
            EnsurePositive(reply, "FTP сервер не удалил папку.");
        }
        finally
        {
            _controlLock.Release();
        }
    }

    public async Task RenameAsync(string oldPath, string newPath, CancellationToken token)
    {
        await EnsureConnectedAsync(token);
        await _controlLock.WaitAsync(token);
        try
        {
            var fromReply = await SendCommandAsync("RNFR " + NormalizeRemotePath(oldPath), token);
            EnsurePositive(fromReply, "FTP сервер не начал переименование.");
            var toReply = await SendCommandAsync("RNTO " + NormalizeRemotePath(newPath), token);
            EnsurePositive(toReply, "FTP сервер не переименовал элемент.");
        }
        finally
        {
            _controlLock.Release();
        }
    }

    public async Task DownloadFileAsync(string remotePath, string localPath, IProgress<RemoteTransferProgress>? progress, CancellationToken token)
    {
        await EnsureConnectedAsync(token);
        await _controlLock.WaitAsync(token);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(localPath) ?? ".");
            var normalizedPath = NormalizeRemotePath(remotePath);
            var remoteSize = await TryGetRemoteSizeAsync(normalizedPath, token);
            var offset = _options?.ResumeTransfers == true && File.Exists(localPath)
                ? Math.Min(new FileInfo(localPath).Length, remoteSize ?? 0)
                : 0L;
            using var file = new FileStream(localPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None, 1024 * 128, FileOptions.SequentialScan);
            if (offset == 0) file.SetLength(0);
            file.Position = offset;
            if (offset > 0)
            {
                var rest = await SendCommandAsync("REST " + offset.ToString(CultureInfo.InvariantCulture), token);
                if (!rest.IsPositive)
                {
                    offset = 0;
                    file.SetLength(0);
                    file.Position = 0;
                }
            }
            await ExecuteDataStreamCommandAsync(
                "RETR " + normalizedPath,
                async stream =>
                {
                    await CopyStreamAsync(stream, file, progress, Path.GetFileName(localPath), offset, remoteSize, token);
                },
                token);
        }
        finally
        {
            _controlLock.Release();
        }
    }

    public async Task UploadFileAsync(string localPath, string remotePath, IProgress<RemoteTransferProgress>? progress, CancellationToken token)
    {
        await EnsureConnectedAsync(token);
        await _controlLock.WaitAsync(token);
        try
        {
            var normalizedPath = NormalizeRemotePath(remotePath);
            var localSize = new FileInfo(localPath).Length;
            var offset = _options?.ResumeTransfers == true
                ? Math.Min(await TryGetRemoteSizeAsync(normalizedPath, token) ?? 0, localSize)
                : 0L;
            using var file = File.OpenRead(localPath);
            if (offset > 0)
            {
                var rest = await SendCommandAsync("REST " + offset.ToString(CultureInfo.InvariantCulture), token);
                if (rest.IsPositive) file.Position = offset; else offset = 0;
            }
            await ExecuteDataStreamCommandAsync(
                "STOR " + normalizedPath,
                async stream =>
                {
                    await CopyStreamAsync(file, stream, progress, Path.GetFileName(localPath), offset, localSize, token);
                },
                token);
        }
        finally
        {
            _controlLock.Release();
        }
    }

    public void Dispose()
    {
        Disconnect();
        _controlLock.Dispose();
    }

    private void StartKeepAlive()
    {
        _keepAliveTimer?.Dispose();
        _lastControlActivityUtc = DateTime.UtcNow;
        _keepAliveTimer = new System.Threading.Timer(_ => _ = KeepAliveAsync(), null, TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(45));
    }

    private async Task KeepAliveAsync()
    {
        if (_writer is null || !Connected || DateTime.UtcNow - _lastControlActivityUtc < TimeSpan.FromSeconds(40))
        {
            return;
        }

        try
        {
            if (!_controlLock.Wait(0))
            {
                return;
            }
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        try
        {
            if (_writer is not null && Connected)
            {
                var reply = await TryCommandAsync("NOOP", CancellationToken.None);
                if (reply is null)
                {
                    var directory = CurrentDirectory;
                    Disconnect();
                    CurrentDirectory = directory;
                }
            }
        }
        finally
        {
            try
            {
                _controlLock.Release();
            }
            catch (ObjectDisposedException)
            {
                // The session is already closed.
            }
        }
    }

    private async Task<string> GetWorkingDirectoryAsync(CancellationToken token)
    {
        var reply = await SendCommandAsync("PWD", token);
        EnsurePositive(reply, "FTP сервер не сообщил текущую папку.");

        var match = Regex.Match(reply.Message, "\"(?<path>[^\"]+)\"");
        return match.Success ? NormalizeRemotePath(match.Groups["path"].Value) : "/";
    }

    private async Task<long?> TryGetRemoteSizeAsync(string path, CancellationToken token)
    {
        var reply = await TryCommandAsync("SIZE " + path, token);
        if (reply is null || !reply.IsPositive) return null;
        var text = reply.Message.Length > 4 ? reply.Message[4..].Trim() : string.Empty;
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) ? size : null;
    }

    private async Task EnsureConnectedAsync(CancellationToken token)
    {
        if (Connected) return;
        if (_options?.AutoReconnect != true) throw new IOException("FTP соединение разорвано.");
        var desiredDirectory = CurrentDirectory;
        await ConnectAsync(_options, token);
        if (desiredDirectory != "/") await ChangeDirectoryAsync(desiredDirectory, token);
    }

    private async Task<string> ExecuteDataReadCommandAsync(string command, CancellationToken token)
    {
        await using var memory = new MemoryStream();
        await ExecuteDataStreamCommandAsync(
            command,
            async stream =>
            {
                await stream.CopyToAsync(memory, token);
            },
            token);

        return _encoding.GetString(memory.ToArray());
    }

    private async Task ExecuteDataStreamCommandAsync(string command, Func<Stream, Task> transfer, CancellationToken token)
    {
        using var dataClient = await OpenPassiveDataClientAsync(token);
        var startReply = await SendCommandAsync(command, token);
        if (!startReply.IsPreliminary && !startReply.IsPositive)
        {
            throw new InvalidOperationException(startReply.Message);
        }

        Stream dataStream = dataClient.GetStream();
        if (_tlsEnabled && _options is not null)
        {
            var sslStream = new SslStream(dataStream, leaveInnerStreamOpen: false, ValidateServerCertificate);
            await sslStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = _options.Host,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.Online
            }, token);
            dataStream = sslStream;
        }

        using (dataStream)
        {
            await transfer(dataStream);
        }

        if (startReply.IsPreliminary)
        {
            var doneReply = await ReadReplyAsync(token);
            EnsurePositive(doneReply, "FTP операция не завершилась.");
        }
    }

    private async Task<TcpClient> OpenPassiveDataClientAsync(CancellationToken token)
    {
        var reply = await SendCommandAsync("PASV", token);
        EnsurePositive(reply, "FTP сервер не включил пассивный режим.");

        var endpoint = ParsePassiveEndpoint(reply.Message);
        var dataClient = new TcpClient { NoDelay = true };
        await dataClient.ConnectAsync(endpoint.Address, endpoint.Port, token);
        return dataClient;
    }

    private IPEndPoint ParsePassiveEndpoint(string message)
    {
        var match = Regex.Match(message, @"\((?<data>\d+,\d+,\d+,\d+,\d+,\d+)\)");
        if (!match.Success)
        {
            throw new InvalidOperationException("FTP сервер вернул непонятный PASV-ответ: " + message);
        }

        var numbers = match.Groups["data"].Value.Split(',').Select(int.Parse).ToArray();
        var port = numbers[4] * 256 + numbers[5];
        var address = new IPAddress(new byte[] { (byte)numbers[0], (byte)numbers[1], (byte)numbers[2], (byte)numbers[3] });
        if (IPAddress.Any.Equals(address) || IPAddress.IPv6Any.Equals(address))
        {
            address = ((IPEndPoint?)_client?.Client.RemoteEndPoint)?.Address ?? IPAddress.Loopback;
        }

        return new IPEndPoint(address, port);
    }

    private async Task<FtpReply> SendCommandAsync(string command, CancellationToken token)
    {
        if (_writer is null)
        {
            throw new InvalidOperationException("FTP подключение не открыто.");
        }

        _lastControlActivityUtc = DateTime.UtcNow;
        await _writer.WriteLineAsync(command.AsMemory(), token);
        await _writer.FlushAsync(token);
        var reply = await ReadReplyAsync(token);
        _lastControlActivityUtc = DateTime.UtcNow;
        return reply;
    }

    private bool ValidateServerCertificate(object sender, X509Certificate? certificate, X509Chain? chain, SslPolicyErrors errors)
    {
        return _options?.AcceptAnyCertificate == true || errors == SslPolicyErrors.None;
    }

    private async Task<FtpReply?> TryCommandAsync(string command, CancellationToken token)
    {
        try
        {
            return await SendCommandAsync(command, token);
        }
        catch
        {
            return null;
        }
    }

    private async Task<FtpReply> ReadReplyAsync(CancellationToken token)
    {
        if (_reader is null)
        {
            throw new InvalidOperationException("FTP подключение не открыто.");
        }

        var firstLine = await _reader.ReadLineAsync(token);
        if (firstLine is null)
        {
            throw new IOException("FTP сервер закрыл соединение.");
        }

        if (firstLine.Length < 3 || !int.TryParse(firstLine[..3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
        {
            throw new InvalidDataException("FTP сервер прислал непонятный ответ: " + firstLine);
        }

        var lines = new List<string> { firstLine };
        if (firstLine.Length > 3 && firstLine[3] == '-')
        {
            var terminator = code.ToString(CultureInfo.InvariantCulture) + " ";
            while (true)
            {
                var line = await _reader.ReadLineAsync(token);
                if (line is null)
                {
                    throw new IOException("FTP сервер закрыл соединение.");
                }

                lines.Add(line);
                if (line.StartsWith(terminator, StringComparison.Ordinal))
                {
                    break;
                }
            }
        }

        return new FtpReply(code, lines);
    }

    private static void EnsurePositive(FtpReply reply, string message)
    {
        if (!reply.IsPositive)
        {
            throw new InvalidOperationException(message + Environment.NewLine + reply.Message);
        }
    }

    private static IReadOnlyList<FtpRemoteEntry> ParseMlsd(string data, string directory)
    {
        var entries = new List<FtpRemoteEntry>();
        foreach (var rawLine in SplitDataLines(data))
        {
            var separator = rawLine.IndexOf(' ');
            if (separator <= 0)
            {
                continue;
            }

            var facts = rawLine[..separator];
            var name = rawLine[(separator + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
            {
                continue;
            }

            var factMap = facts
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Split('=', 2))
                .Where(part => part.Length == 2)
                .ToDictionary(part => part[0], part => part[1], StringComparer.OrdinalIgnoreCase);

            factMap.TryGetValue("type", out var type);
            factMap.TryGetValue("size", out var sizeText);
            factMap.TryGetValue("modify", out var modifyText);

            entries.Add(new FtpRemoteEntry
            {
                Name = name,
                FullPath = CombineRemotePath(directory, name),
                IsDirectory = string.Equals(type, "dir", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(type, "cdir", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(type, "pdir", StringComparison.OrdinalIgnoreCase),
                Size = long.TryParse(sizeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) ? size : null,
                Modified = TryParseMlstDate(modifyText)
            });
        }

        return SortEntries(entries);
    }

    private static IReadOnlyList<FtpRemoteEntry> ParseList(string data, string directory)
    {
        var entries = new List<FtpRemoteEntry>();
        foreach (var rawLine in SplitDataLines(data))
        {
            var entry = ParseUnixListLine(rawLine, directory) ?? ParseDosListLine(rawLine, directory);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return SortEntries(entries);
    }

    private static FtpRemoteEntry? ParseUnixListLine(string line, string directory)
    {
        if (line.Length < 10 || (line[0] != 'd' && line[0] != '-' && line[0] != 'l'))
        {
            return null;
        }

        var parts = new Regex(@"\s+").Split(line, 9);
        if (parts.Length < 9)
        {
            return null;
        }

        var name = parts[8];
        var linkSeparator = name.IndexOf(" -> ", StringComparison.Ordinal);
        if (linkSeparator >= 0)
        {
            name = name[..linkSeparator];
        }

        if (name is "." or "..")
        {
            return null;
        }

        return new FtpRemoteEntry
        {
            Name = name,
            FullPath = CombineRemotePath(directory, name),
            IsDirectory = line[0] == 'd',
            Size = long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) ? size : null,
            Modified = TryParseUnixDate(parts[5], parts[6], parts[7])
        };
    }

    private static FtpRemoteEntry? ParseDosListLine(string line, string directory)
    {
        var match = Regex.Match(line, @"^(?<date>\d{2}-\d{2}-\d{2})\s+(?<time>\d{2}:\d{2}[AP]M)\s+(?<kind><DIR>|\d+)\s+(?<name>.+)$", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        var name = match.Groups["name"].Value.Trim();
        if (name is "." or "..")
        {
            return null;
        }

        var kind = match.Groups["kind"].Value;
        return new FtpRemoteEntry
        {
            Name = name,
            FullPath = CombineRemotePath(directory, name),
            IsDirectory = string.Equals(kind, "<DIR>", StringComparison.OrdinalIgnoreCase),
            Size = long.TryParse(kind, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) ? size : null,
            Modified = DateTime.TryParseExact(
                match.Groups["date"].Value + " " + match.Groups["time"].Value,
                "MM-dd-yy hh:mmtt",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var modified) ? modified : null
        };
    }

    private static IReadOnlyList<string> SplitDataLines(string data)
    {
        return data
            .Replace("\r\n", "\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static IReadOnlyList<FtpRemoteEntry> SortEntries(IEnumerable<FtpRemoteEntry> entries)
    {
        return entries
            .OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static DateTime? TryParseMlstDate(string? text)
    {
        if (DateTime.TryParseExact(
            text,
            "yyyyMMddHHmmss",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var date))
        {
            return date.ToLocalTime();
        }

        return null;
    }

    private static DateTime? TryParseUnixDate(string month, string day, string timeOrYear)
    {
        var year = DateTime.Now.Year.ToString(CultureInfo.InvariantCulture);
        var text = $"{month} {day} {timeOrYear}";
        if (timeOrYear.Contains(':', StringComparison.Ordinal))
        {
            text += " " + year;
            return DateTime.TryParseExact(text, "MMM d HH:mm yyyy", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date)
                ? date
                : null;
        }

        return DateTime.TryParseExact(text, "MMM d yyyy", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var yearDate)
            ? yearDate
            : null;
    }

    public static string CombineRemotePath(string directory, string name)
    {
        var cleanDirectory = NormalizeRemotePath(directory);
        var cleanName = CleanArgument(name).Replace('\\', '/').Trim('/');
        return cleanDirectory == "/" ? "/" + cleanName : cleanDirectory.TrimEnd('/') + "/" + cleanName;
    }

    public static string ParentRemotePath(string path)
    {
        var clean = NormalizeRemotePath(path).TrimEnd('/');
        if (clean == string.Empty)
        {
            return "/";
        }

        var separator = clean.LastIndexOf('/');
        return separator <= 0 ? "/" : clean[..separator];
    }

    public static string NormalizeRemotePath(string path)
    {
        var clean = CleanArgument(path).Replace('\\', '/').Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return "/";
        }

        if (!clean.StartsWith("/", StringComparison.Ordinal))
        {
            clean = "/" + clean;
        }

        var parts = new List<string>();
        foreach (var part in clean.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
            {
                continue;
            }

            if (part == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }
                continue;
            }

            parts.Add(part);
        }

        return "/" + string.Join('/', parts);
    }

    private static string CleanArgument(string? value)
    {
        return (value ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
    }

    private async Task CopyStreamAsync(
        Stream source,
        Stream destination,
        IProgress<RemoteTransferProgress>? progress,
        string label,
        long initialBytes,
        long? totalBytes,
        CancellationToken token)
    {
        var buffer = new byte[1024 * 128];
        var copied = initialBytes;
        var transferredThisRun = 0L;
        var stopwatch = Stopwatch.StartNew();
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), token)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), token);
            copied += read;
            transferredThisRun += read;
            progress?.Report(new RemoteTransferProgress(copied, totalBytes, label));
            var limit = _options?.SpeedLimitKbps ?? 0;
            if (limit > 0)
            {
                var expected = TimeSpan.FromSeconds(transferredThisRun / (limit * 1024D));
                var delay = expected - stopwatch.Elapsed;
                if (delay > TimeSpan.FromMilliseconds(2)) await Task.Delay(delay, token);
            }
        }
    }
}
