namespace AzertyCommander;

internal sealed class FilePanelDropEventArgs : EventArgs
{
    public FilePanelDropEventArgs(IReadOnlyList<string> paths, string targetDirectory, DragDropEffects effect)
    {
        Paths = paths;
        TargetDirectory = targetDirectory;
        Effect = effect;
    }

    public IReadOnlyList<string> Paths { get; }

    public string TargetDirectory { get; }

    public DragDropEffects Effect { get; }
}
