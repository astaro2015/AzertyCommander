namespace AzertyCommander;

internal sealed class FilePanelEntryEventArgs : EventArgs
{
    public FilePanelEntryEventArgs(FileSystemEntry entry)
    {
        Entry = entry;
    }

    public FileSystemEntry Entry { get; }
}
