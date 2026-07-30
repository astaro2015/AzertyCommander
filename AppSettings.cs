namespace AzertyCommander;

internal sealed class AppSettings
{
    public AppThemeSettings Theme { get; set; } = new();
    public AppWindowSettings Window { get; set; } = new();
    public AppPanelSettings LeftPanel { get; set; } = new();
    public AppPanelSettings RightPanel { get; set; } = new();
    public List<FtpConnectionProfile> FtpConnections { get; set; } = new();
    public List<string> FtpConnectionGroups { get; set; } = new();
}

internal sealed class AppThemeSettings
{
    public const int DefaultRowHeight = 30;

    public string FileFontFamily { get; set; } = "Segoe UI";
    public float FileFontSize { get; set; } = 9.75F;
    public int FileFontStyle { get; set; } = (int)FontStyle.Regular;
    public string FolderFontFamily { get; set; } = "Segoe UI";
    public float FolderFontSize { get; set; } = 9.75F;
    public int FolderFontStyle { get; set; } = (int)FontStyle.Regular;
    public int RowHeight { get; set; } = DefaultRowHeight;
    public string FileTextColor { get; set; } = "#000000";
    public string FolderTextColor { get; set; } = "#000000";
    public string MarkedTextColor { get; set; } = "#FF0000";
    public string ListBackgroundColor { get; set; } = "#FFFFFF";
    public string SelectedBackgroundColor { get; set; } = "#0078D7";
    public string SelectedTextColor { get; set; } = "#FFFFFF";
    public string ActivePanelBackgroundColor { get; set; } = "#D4E8F7";
    public string ActivePathBackgroundColor { get; set; } = "#E8F6FF";

    public AppThemeSettings Clone()
    {
        return new AppThemeSettings
        {
            FileFontFamily = FileFontFamily,
            FileFontSize = FileFontSize,
            FileFontStyle = FileFontStyle,
            FolderFontFamily = FolderFontFamily,
            FolderFontSize = FolderFontSize,
            FolderFontStyle = FolderFontStyle,
            RowHeight = RowHeight,
            FileTextColor = FileTextColor,
            FolderTextColor = FolderTextColor,
            MarkedTextColor = MarkedTextColor,
            ListBackgroundColor = ListBackgroundColor,
            SelectedBackgroundColor = SelectedBackgroundColor,
            SelectedTextColor = SelectedTextColor,
            ActivePanelBackgroundColor = ActivePanelBackgroundColor,
            ActivePathBackgroundColor = ActivePathBackgroundColor
        };
    }
}

internal sealed class AppWindowSettings
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximized { get; set; }
    public double SplitterRatio { get; set; }
}

internal sealed class AppPanelSettings
{
    public string Path { get; set; } = string.Empty;
    public Dictionary<string, int> ColumnWidths { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
