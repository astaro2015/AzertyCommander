namespace AzertyCommander;

internal sealed class FtpReply
{
    public FtpReply(int code, IReadOnlyList<string> lines)
    {
        Code = code;
        Lines = lines;
        Message = string.Join(Environment.NewLine, lines);
    }

    public int Code { get; }

    public IReadOnlyList<string> Lines { get; }

    public string Message { get; }

    public bool IsPositive => Code >= 200 && Code < 400;

    public bool IsPreliminary => Code >= 100 && Code < 200;
}
