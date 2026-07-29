using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace AzertyCommander;

internal sealed class SimpleFtpServer : IDisposable
{
    private readonly SimpleFtpServerOptions _options;
    private readonly ConcurrentBag<TcpClient> _clients = new();
    private readonly Encoding _encoding = new UTF8Encoding(false);
    private CancellationTokenSource? _cancellation;
    private TcpListener? _listener;
    private Task? _acceptTask;

    public SimpleFtpServer(SimpleFtpServerOptions options)
    {
        _options = options;
    }

    public event EventHandler<string>? LogReceived;

    public bool IsRunning => _listener is not null;

    public int ActualPort { get; private set; }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        if (!Directory.Exists(_options.RootDirectory))
        {
            throw new DirectoryNotFoundException("Папка FTP сервера не найдена.");
        }

        _cancellation = new CancellationTokenSource();
        _listener = new TcpListener(_options.ListenAddress, _options.Port);
        _listener.Start();
        ActualPort = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptTask = Task.Run(() => AcceptLoopAsync(_cancellation.Token));
        Log($"Сервер запущен. Папка: {Path.GetFullPath(_options.RootDirectory)}");
    }

    public void Stop()
    {
        var listener = _listener;
        if (listener is null)
        {
            return;
        }

        _cancellation?.Cancel();
        try
        {
            listener.Stop();
        }
        catch
        {
            // Listener may already be stopped.
        }

        while (_clients.TryTake(out var client))
        {
            try
            {
                client.Close();
            }
            catch
            {
                // Closing network clients is best effort.
            }
        }

        _listener = null;
        Log("Сервер остановлен.");
    }

    public void Dispose()
    {
        Stop();
        _cancellation?.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener is not null)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log("Ошибка приема клиента: " + ex.Message);
                continue;
            }

            client.NoDelay = true;
            _clients.Add(client);
            _ = Task.Run(() => HandleClientAsync(client, token), CancellationToken.None);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        Log("Подключение: " + remote);

        try
        {
            using (client)
            using (var stream = client.GetStream())
            using (var reader = new StreamReader(stream, _encoding, detectEncodingFromByteOrderMarks: false, leaveOpen: true))
            using (var writer = new StreamWriter(stream, _encoding) { NewLine = "\r\n", AutoFlush = true })
            {
                var session = new Session(this, client, reader, writer);
                await session.RunAsync(token);
            }
        }
        catch (OperationCanceledException)
        {
            // Server is stopping.
        }
        catch (IOException)
        {
            Log("Клиент отключился: " + remote);
        }
        catch (Exception ex)
        {
            Log("Ошибка клиента " + remote + ": " + ex.Message);
        }
    }

    private void Log(string message)
    {
        LogReceived?.Invoke(this, DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "  " + message);
    }

    private sealed class Session
    {
        private readonly SimpleFtpServer _server;
        private readonly TcpClient _controlClient;
        private readonly StreamReader _reader;
        private readonly StreamWriter _writer;
        private readonly string _rootPath;
        private string _currentVirtualPath = "/";
        private string? _pendingUser;
        private bool _authenticated;
        private TcpListener? _passiveListener;
        private string? _renameFromPath;

        public Session(SimpleFtpServer server, TcpClient controlClient, StreamReader reader, StreamWriter writer)
        {
            _server = server;
            _controlClient = controlClient;
            _reader = reader;
            _writer = writer;
            _rootPath = Path.GetFullPath(server._options.RootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public async Task RunAsync(CancellationToken token)
        {
            await ReplyAsync(220, "AZERTY Commander FTP server ready");

            while (!token.IsCancellationRequested)
            {
                var line = await _reader.ReadLineAsync(token);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var spaceIndex = line.IndexOf(' ');
                var command = (spaceIndex >= 0 ? line[..spaceIndex] : line).Trim().ToUpperInvariant();
                var argument = spaceIndex >= 0 ? line[(spaceIndex + 1)..].Trim() : string.Empty;

                if (!_authenticated && command is not ("USER" or "PASS" or "QUIT" or "FEAT" or "SYST" or "OPTS" or "NOOP"))
                {
                    await ReplyAsync(530, "Login with USER and PASS first.");
                    continue;
                }

                if (await HandleCommandAsync(command, argument, token))
                {
                    break;
                }
            }
        }

        private async Task<bool> HandleCommandAsync(string command, string argument, CancellationToken token)
        {
            switch (command)
            {
                case "USER":
                    _pendingUser = argument;
                    if (IsAnonymousUser(argument) && _server._options.AllowAnonymous)
                    {
                        await ReplyAsync(331, "Anonymous login ok, send any password.");
                    }
                    else if (string.Equals(argument, _server._options.UserName, StringComparison.Ordinal))
                    {
                        await ReplyAsync(331, "User name ok, need password.");
                    }
                    else
                    {
                        await ReplyAsync(331, "User received, password required.");
                    }
                    return false;

                case "PASS":
                    if (IsAnonymousUser(_pendingUser) && _server._options.AllowAnonymous)
                    {
                        _authenticated = true;
                        await ReplyAsync(230, "Anonymous logged in.");
                    }
                    else if (string.Equals(_pendingUser, _server._options.UserName, StringComparison.Ordinal) &&
                        string.Equals(argument, _server._options.Password, StringComparison.Ordinal))
                    {
                        _authenticated = true;
                        await ReplyAsync(230, "User logged in.");
                    }
                    else
                    {
                        await ReplyAsync(530, "Login incorrect.");
                    }
                    return false;

                case "SYST":
                    await ReplyAsync(215, "UNIX Type: L8");
                    return false;

                case "FEAT":
                    await _writer.WriteLineAsync("211-Features");
                    await _writer.WriteLineAsync(" UTF8");
                    await _writer.WriteLineAsync(" MLST type*;size*;modify*;");
                    await _writer.WriteLineAsync("211 End");
                    return false;

                case "OPTS":
                    await ReplyAsync(200, "Option accepted.");
                    return false;

                case "TYPE":
                    await ReplyAsync(200, "Type set.");
                    return false;

                case "NOOP":
                    await ReplyAsync(200, "OK");
                    return false;

                case "PWD":
                case "XPWD":
                    await ReplyAsync(257, $"\"{_currentVirtualPath}\" is current directory.");
                    return false;

                case "CWD":
                    await ChangeDirectoryAsync(argument);
                    return false;

                case "CDUP":
                    await ChangeDirectoryAsync("..");
                    return false;

                case "PASV":
                    await EnterPassiveModeAsync(extended: false);
                    return false;

                case "EPSV":
                    await EnterPassiveModeAsync(extended: true);
                    return false;

                case "PORT":
                case "EPRT":
                    await ReplyAsync(502, "Active mode is not supported. Use passive mode.");
                    return false;

                case "LIST":
                    await SendListAsync(argument, machineReadable: false, token);
                    return false;

                case "MLSD":
                    await SendListAsync(argument, machineReadable: true, token);
                    return false;

                case "RETR":
                    await RetrieveAsync(argument, token);
                    return false;

                case "STOR":
                    await StoreAsync(argument, token);
                    return false;

                case "DELE":
                    await DeleteFileAsync(argument);
                    return false;

                case "MKD":
                case "XMKD":
                    await CreateDirectoryAsync(argument);
                    return false;

                case "RMD":
                case "XRMD":
                    await RemoveDirectoryAsync(argument);
                    return false;

                case "RNFR":
                    await RenameFromAsync(argument);
                    return false;

                case "RNTO":
                    await RenameToAsync(argument);
                    return false;

                case "SIZE":
                    await SizeAsync(argument);
                    return false;

                case "MDTM":
                    await ModifiedTimeAsync(argument);
                    return false;

                case "QUIT":
                    await ReplyAsync(221, "Bye.");
                    return true;

                default:
                    await ReplyAsync(502, "Command not implemented.");
                    return false;
            }
        }

        private async Task ChangeDirectoryAsync(string argument)
        {
            var targetPath = MapPath(argument);
            if (!Directory.Exists(targetPath))
            {
                await ReplyAsync(550, "Directory not found.");
                return;
            }

            _currentVirtualPath = PhysicalToVirtualPath(targetPath);
            await ReplyAsync(250, "Directory changed.");
        }

        private async Task EnterPassiveModeAsync(bool extended)
        {
            ClosePassiveListener();

            var bindAddress = IPAddress.Any;
            if (IPAddress.IsLoopback(_server._options.ListenAddress))
            {
                bindAddress = IPAddress.Loopback;
            }

            _passiveListener = CreatePassiveListener(bindAddress);
            var port = ((IPEndPoint)_passiveListener.LocalEndpoint).Port;

            if (extended)
            {
                await ReplyAsync(229, $"Entering Extended Passive Mode (|||{port}|).");
                return;
            }

            var replyAddress = GetReplyAddress();
            var bytes = replyAddress.GetAddressBytes();
            await ReplyAsync(227, $"Entering Passive Mode ({bytes[0]},{bytes[1]},{bytes[2]},{bytes[3]},{port / 256},{port % 256}).");
        }

        private async Task SendListAsync(string argument, bool machineReadable, CancellationToken token)
        {
            var directory = string.IsNullOrWhiteSpace(argument) ? MapPath(_currentVirtualPath) : MapPath(argument);
            if (!Directory.Exists(directory))
            {
                await ReplyAsync(550, "Directory not found.");
                return;
            }

            await ReplyAsync(150, "Opening data connection.");
            using var dataClient = await AcceptPassiveClientAsync(token);
            using var stream = dataClient.GetStream();
            using var dataWriter = new StreamWriter(stream, _server._encoding) { NewLine = "\r\n", AutoFlush = true };

            foreach (var entry in Directory.EnumerateFileSystemEntries(directory).OrderBy(path => path, StringComparer.CurrentCultureIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                var info = new FileInfo(entry);
                var attributes = File.GetAttributes(entry);
                var isDirectory = attributes.HasFlag(FileAttributes.Directory);
                var fileSystemInfo = isDirectory ? new DirectoryInfo(entry) : info as FileSystemInfo;

                if (machineReadable)
                {
                    await dataWriter.WriteLineAsync(FormatMlsdLine(fileSystemInfo, isDirectory));
                }
                else
                {
                    await dataWriter.WriteLineAsync(FormatListLine(fileSystemInfo, isDirectory));
                }
            }

            await ReplyAsync(226, "Transfer complete.");
        }

        private async Task RetrieveAsync(string argument, CancellationToken token)
        {
            var filePath = MapPath(argument);
            if (!File.Exists(filePath))
            {
                await ReplyAsync(550, "File not found.");
                return;
            }

            await ReplyAsync(150, "Opening data connection.");
            using var dataClient = await AcceptPassiveClientAsync(token);
            await using (var dataStream = dataClient.GetStream())
            await using (var file = File.OpenRead(filePath))
            {
                await file.CopyToAsync(dataStream, token);
            }

            _server.Log("Скачан файл: " + filePath);
            await ReplyAsync(226, "Transfer complete.");
        }

        private async Task StoreAsync(string argument, CancellationToken token)
        {
            if (_server._options.ReadOnly)
            {
                await ReplyAsync(550, "Server is read only.");
                return;
            }

            var filePath = MapPath(argument);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath) ?? _rootPath);

            await ReplyAsync(150, "Opening data connection.");
            using var dataClient = await AcceptPassiveClientAsync(token);
            await using (var dataStream = dataClient.GetStream())
            await using (var file = File.Create(filePath))
            {
                await dataStream.CopyToAsync(file, token);
            }

            _server.Log("Загружен файл: " + filePath);
            await ReplyAsync(226, "Transfer complete.");
        }

        private async Task DeleteFileAsync(string argument)
        {
            if (_server._options.ReadOnly)
            {
                await ReplyAsync(550, "Server is read only.");
                return;
            }

            var filePath = MapPath(argument);
            if (!File.Exists(filePath))
            {
                await ReplyAsync(550, "File not found.");
                return;
            }

            File.Delete(filePath);
            _server.Log("Удален файл: " + filePath);
            await ReplyAsync(250, "File deleted.");
        }

        private async Task CreateDirectoryAsync(string argument)
        {
            if (_server._options.ReadOnly)
            {
                await ReplyAsync(550, "Server is read only.");
                return;
            }

            var directory = MapPath(argument);
            Directory.CreateDirectory(directory);
            _server.Log("Создана папка: " + directory);
            await ReplyAsync(257, $"\"{PhysicalToVirtualPath(directory)}\" created.");
        }

        private async Task RemoveDirectoryAsync(string argument)
        {
            if (_server._options.ReadOnly)
            {
                await ReplyAsync(550, "Server is read only.");
                return;
            }

            var directory = MapPath(argument);
            if (!Directory.Exists(directory))
            {
                await ReplyAsync(550, "Directory not found.");
                return;
            }

            Directory.Delete(directory);
            _server.Log("Удалена папка: " + directory);
            await ReplyAsync(250, "Directory removed.");
        }

        private async Task RenameFromAsync(string argument)
        {
            if (_server._options.ReadOnly)
            {
                await ReplyAsync(550, "Server is read only.");
                return;
            }

            var source = MapPath(argument);
            if (!File.Exists(source) && !Directory.Exists(source))
            {
                await ReplyAsync(550, "Source not found.");
                return;
            }

            _renameFromPath = source;
            await ReplyAsync(350, "Ready for RNTO.");
        }

        private async Task RenameToAsync(string argument)
        {
            if (_server._options.ReadOnly)
            {
                await ReplyAsync(550, "Server is read only.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_renameFromPath))
            {
                await ReplyAsync(503, "Use RNFR first.");
                return;
            }

            var destination = MapPath(argument);
            if (Directory.Exists(_renameFromPath))
            {
                Directory.Move(_renameFromPath, destination);
            }
            else
            {
                File.Move(_renameFromPath, destination, overwrite: true);
            }

            _server.Log("Переименовано: " + _renameFromPath + " -> " + destination);
            _renameFromPath = null;
            await ReplyAsync(250, "Rename complete.");
        }

        private async Task SizeAsync(string argument)
        {
            var filePath = MapPath(argument);
            if (!File.Exists(filePath))
            {
                await ReplyAsync(550, "File not found.");
                return;
            }

            await ReplyAsync(213, new FileInfo(filePath).Length.ToString(CultureInfo.InvariantCulture));
        }

        private async Task ModifiedTimeAsync(string argument)
        {
            var path = MapPath(argument);
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                await ReplyAsync(550, "Path not found.");
                return;
            }

            var modified = File.GetLastWriteTimeUtc(path).ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            await ReplyAsync(213, modified);
        }

        private async Task<TcpClient> AcceptPassiveClientAsync(CancellationToken token)
        {
            if (_passiveListener is null)
            {
                throw new IOException("Passive data connection is not opened.");
            }

            try
            {
                var client = await _passiveListener.AcceptTcpClientAsync(token);
                client.NoDelay = true;
                return client;
            }
            finally
            {
                ClosePassiveListener();
            }
        }

        private TcpListener CreatePassiveListener(IPAddress bindAddress)
        {
            var start = _server._options.PassivePortStart;
            var end = _server._options.PassivePortEnd;
            if (start <= 0 || end <= 0)
            {
                var randomListener = new TcpListener(bindAddress, 0);
                randomListener.Start();
                return randomListener;
            }

            if (end < start)
            {
                (start, end) = (end, start);
            }

            for (var port = start; port <= end; port++)
            {
                try
                {
                    var listener = new TcpListener(bindAddress, port);
                    listener.Start();
                    return listener;
                }
                catch (SocketException)
                {
                    // Try the next passive port.
                }
            }

            throw new IOException($"Нет свободного пассивного FTP порта в диапазоне {start}-{end}.");
        }

        private void ClosePassiveListener()
        {
            try
            {
                _passiveListener?.Stop();
            }
            catch
            {
                // Passive listener may already be closed.
            }
            finally
            {
                _passiveListener = null;
            }
        }

        private string MapPath(string argument)
        {
            var virtualPath = NormalizeVirtualPath(argument);
            var relative = virtualPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var physical = Path.GetFullPath(Path.Combine(_rootPath, relative));

            if (!IsInsideRoot(physical))
            {
                throw new UnauthorizedAccessException("FTP path is outside server root.");
            }

            return physical;
        }

        private string NormalizeVirtualPath(string argument)
        {
            var clean = (argument ?? string.Empty)
                .Trim()
                .Trim('"')
                .Replace('\\', '/');

            if (string.IsNullOrWhiteSpace(clean))
            {
                clean = _currentVirtualPath;
            }
            else if (!clean.StartsWith("/", StringComparison.Ordinal))
            {
                clean = _currentVirtualPath.TrimEnd('/') + "/" + clean;
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

        private bool IsInsideRoot(string path)
        {
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(fullPath, _rootPath, StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(_rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private string PhysicalToVirtualPath(string path)
        {
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(fullPath, _rootPath, StringComparison.OrdinalIgnoreCase))
            {
                return "/";
            }

            var relative = Path.GetRelativePath(_rootPath, fullPath).Replace(Path.DirectorySeparatorChar, '/');
            return "/" + relative.Trim('/');
        }

        private IPAddress GetReplyAddress()
        {
            if (_controlClient.Client.LocalEndPoint is IPEndPoint local &&
                local.Address.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.Any.Equals(local.Address))
            {
                return local.Address;
            }

            return IPAddress.Loopback;
        }

        private static string FormatMlsdLine(FileSystemInfo info, bool isDirectory)
        {
            var type = isDirectory ? "dir" : "file";
            var size = isDirectory ? 0 : ((FileInfo)info).Length;
            var modified = info.LastWriteTimeUtc.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            return $"type={type};size={size};modify={modified}; {info.Name}";
        }

        private static string FormatListLine(FileSystemInfo info, bool isDirectory)
        {
            var kind = isDirectory ? 'd' : '-';
            var size = isDirectory ? 0 : ((FileInfo)info).Length;
            var modified = info.LastWriteTime;
            var month = modified.ToString("MMM", CultureInfo.InvariantCulture);
            var day = modified.Day.ToString(CultureInfo.InvariantCulture).PadLeft(2);
            var timeOrYear = modified.Year == DateTime.Now.Year
                ? modified.ToString("HH:mm", CultureInfo.InvariantCulture)
                : modified.Year.ToString(CultureInfo.InvariantCulture);
            return $"{kind}rw-r--r-- 1 owner group {size,12} {month} {day} {timeOrYear,5} {info.Name}";
        }

        private async Task ReplyAsync(int code, string message)
        {
            await _writer.WriteLineAsync(code.ToString(CultureInfo.InvariantCulture) + " " + message);
        }

        private static bool IsAnonymousUser(string? user)
        {
            return string.Equals(user, "anonymous", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(user, "ftp", StringComparison.OrdinalIgnoreCase);
        }
    }
}
