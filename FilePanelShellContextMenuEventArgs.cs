namespace AzertyCommander;

internal sealed class FilePanelShellContextMenuEventArgs : EventArgs
{
    public FilePanelShellContextMenuEventArgs(IReadOnlyList<string> paths, Point screenLocation)
    {
        Paths = paths;
        ScreenLocation = screenLocation;
    }

    public IReadOnlyList<string> Paths { get; }

    public Point ScreenLocation { get; }
}
