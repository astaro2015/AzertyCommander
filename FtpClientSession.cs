using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace AzertyCommander;

internal sealed class FtpClientSession : IDisposable
{
    private readonly Encoding _encoding = new UTF8Encoding(false);
    private TcpClient? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public string CurrentDirectory { get; private set; } = "/";

    public bool Connected => _client?.Connected == true;

    public async Task ConnectAsync(FtpConnectionOptions options, CancellationToken token)
    {
        Disconnect();

        _client = new TcpClient { NoDelay = true };
        await _client.ConnectAsync(options.Host, options.Port, token);

        var stream = _client.GetStream();
        _reader = new StreamReader(stream, _encoding, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        _writer = new StreamWriter(stream, _encoding) { NewLine = "\r\n", AutoFlush = true };

        var welcome = await ReadReplyAsync(token);
        EnsurePositive(welcome, "FTP сервер не принял подключение.");

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
        CurrentDirectory = await GetWorkingDirectoryAsync(token);
    }

    public void Disconnect()
    {
        try
        {
            if (Connected)
            {
                _ = SendCommandAsync("QUIT", CancellationToken.None);
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
        CurrentDirectory = "/";
    }

    public async Task<IReadOnlyList<FtpRemoteEntry>> ListAsync(CancellationToken token)
    {
        return await ListAsync(CurrentDirectory, token);
    }

    public async Task<IReadOnlyList<FtpRemoteEntry>> ListAsync(string remoteDirectory, CancellationToken token)
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
        var reply = await SendCommandAsync("CWD " + NormalizeRemotePath(path), token);
        EnsurePositive(reply, "FTP сервер не открыл папку.");
        CurrentDirectory = await GetWorkingDirectoryAsync(token);
    }

    public async Task CreateDirectoryAsync(string path, CancellationToken token)
    {
        var reply = await SendCommandAsync("MKD " + NormalizeRemotePath(path), token);
        EnsurePositive(reply, "FTP сервер не создал папку.");
    }

    public async Task DeleteFileAsync(string path, CancellationToken token)
    {
        var reply = await SendCommandAsync("DELE " + NormalizeRemotePath(path), token);
        EnsurePositive(reply, "FTP сервер не удалил файл.");
    }

    public async Task RemoveDirectoryAsync(string path, CancellationToken token)
    {
        var reply = await SendCommandAsync("RMD " + NormalizeRemotePath(path), token);
        EnsurePositive(reply, "FTP сервер не удалил папку.");
    }

    public async Task RenameAsync(string oldPath, string newPath, CancellationToken token)
    {
        var fromReply = await SendCommandAsync("RNFR " + NormalizeRemotePath(oldPath), token);
        EnsurePositive(fromReply, "FTP сервер не начал переименование.");
        var toReply = await SendCommandAsync("RNTO " + NormalizeRemotePath(newPath), token);
        EnsurePositive(toReply, "FTP сервер не переименовал элемент.");
    }

    public async Task DownloadFileAsync(string remotePath, string localPath, IProgress<string>? progress, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(localPath) ?? ".");
        using var file = File.Create(localPath);
        await ExecuteDataStreamCommandAsync(
            "RETR " + NormalizeRemotePath(remotePath),
            async stream =>
            {
                await CopyStreamAsync(stream, file, progress, Path.GetFileName(localPath), token);
            },
            token);
    }

    public async Task UploadFileAsync(string localPath, string remotePath, IProgress<string>? progress, CancellationToken token)
    {
        using var file = File.OpenRead(localPath);
        await ExecuteDataStreamCommandAsync(
            "STOR " + NormalizeRemotePath(remotePath),
            async stream =>
            {
                await CopyStreamAsync(file, stream, progress, Path.GetFileName(localPath), token);
            },
            token);
    }

    public void Dispose()
    {
        Disconnect();
    }

    private async Task<string> GetWorkingDirectoryAsync(CancellationToken token)
    {
        var reply = await SendCommandAsync("PWD", token);
        EnsurePositive(reply, "FTP сервер не сообщил текущую папку.");

        var match = Regex.Match(reply.Message, "\"(?<path>[^\"]+)\"");
        return match.Success ? NormalizeRemotePath(match.Groups["path"].Value) : "/";
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

        using (var dataStream = dataClient.GetStream())
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

        await _writer.WriteLineAsync(command.AsMemory(), token);
        await _writer.FlushAsync(token);
        return await ReadReplyAsync(token);
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

    private static async Task CopyStreamAsync(Stream source, Stream destination, IProgress<string>? progress, string label, CancellationToken token)
    {
        var buffer = new byte[1024 * 128];
        long copied = 0;
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), token)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), token);
            copied += read;
            progress?.Report($"{label}: {copied / 1024:N0} Кб");
        }
    }
}
