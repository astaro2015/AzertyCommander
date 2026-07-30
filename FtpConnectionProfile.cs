namespace AzertyCommander;

internal sealed class FtpConnectionProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Anonymous";
    public string Group { get; set; } = string.Empty;
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 2121;
    public bool Anonymous { get; set; } = true;
    public string UserName { get; set; } = "anonymous";
    public string Password { get; set; } = "guest@";
    public string RemoteDirectory { get; set; } = string.Empty;
    public string LocalDirectory { get; set; } = string.Empty;
    public bool PassiveMode { get; set; } = true;

    public FtpConnectionProfile Clone()
    {
        return new FtpConnectionProfile
        {
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id,
            Name = Name,
            Group = Group,
            Host = Host,
            Port = Port,
            Anonymous = Anonymous,
            UserName = UserName,
            Password = Password,
            RemoteDirectory = RemoteDirectory,
            LocalDirectory = LocalDirectory,
            PassiveMode = PassiveMode
        };
    }

    public static FtpConnectionProfile CreateDefault(string localDirectory = "")
    {
        return new FtpConnectionProfile
        {
            Name = "Anonymous",
            Host = "127.0.0.1",
            Port = 2121,
            Anonymous = true,
            UserName = "anonymous",
            Password = "guest@",
            LocalDirectory = localDirectory,
            PassiveMode = true
        };
    }
}
