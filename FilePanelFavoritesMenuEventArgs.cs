namespace AzertyCommander;

internal sealed class FilePanelFavoritesMenuEventArgs : EventArgs
{
    public FilePanelFavoritesMenuEventArgs(Point screenLocation)
    {
        ScreenLocation = screenLocation;
    }

    public Point ScreenLocation { get; }
}
