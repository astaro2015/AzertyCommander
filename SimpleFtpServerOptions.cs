using System.Net;

namespace AzertyCommander;

internal sealed class SimpleFtpServerOptions
{
    public string RootDirectory { get; init; } = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    public IPAddress ListenAddress { get; init; } = IPAddress.Any;

    public int Port { get; init; } = 2121;

    public int PassivePortStart { get; init; } = 50000;

    public int PassivePortEnd { get; init; } = 50100;

    public bool AllowAnonymous { get; init; } = true;

    public string UserName { get; init; } = "azerty";

    public string Password { get; init; } = string.Empty;

    public bool ReadOnly { get; init; } = true;
}
