namespace AzertyCommander;

internal sealed class FtpConnectionOptions
{
    public string Host { get; init; } = string.Empty;

    public int Port { get; init; } = 21;

    public string UserName { get; init; } = "anonymous";

    public string Password { get; init; } = "guest@";
}
