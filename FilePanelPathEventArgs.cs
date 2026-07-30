namespace AzertyCommander;

internal sealed class FilePanelPathEventArgs : EventArgs
{
    public FilePanelPathEventArgs(string path)
    {
        Path = path;
    }

    public string Path { get; }
}
