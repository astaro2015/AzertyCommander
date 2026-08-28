namespace AzertyCommander;

internal sealed class FtpConnectionOptions
{
    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 21;

    public string UserName { get; init; } = "anonymous";

    public string Password { get; init; } = "guest@";

    public bool UseTls { get; init; }

    public bool AcceptAnyCertificate { get; init; }

    public bool ResumeTransfers { get; init; } = true;

    public bool AutoReconnect { get; init; } = true;

    public int SpeedLimitKbps { get; init; }
}
