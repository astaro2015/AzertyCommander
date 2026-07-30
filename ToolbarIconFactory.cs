namespace AzertyCommander;

internal static class ToolbarIconFactory
{
    public static Image Refresh() => Create(Color.FromArgb(44, 152, 72), (g, pen, brush) =>
    {
        g.DrawArc(pen, 4, 4, 14, 14, 35, 285);
        g.FillPolygon(brush, new[] { new Point(17, 3), new Point(21, 8), new Point(15, 9) });
    });

    public static Image Up() => Create(Color.FromArgb(84, 105, 130), (g, pen, brush) =>
    {
        g.FillPolygon(brush, new[] { new Point(12, 3), new Point(21, 12), new Point(16, 12), new Point(16, 21), new Point(8, 21), new Point(8, 12), new Point(3, 12) });
    });

    public static Image View() => Create(Color.FromArgb(52, 128, 184), (g, pen, brush) =>
    {
        g.DrawRectangle(pen, 5, 3, 12, 18);
        g.DrawLine(pen, 8, 8, 16, 8);
        g.DrawLine(pen, 8, 12, 16, 12);
        g.DrawLine(pen, 8, 16, 14, 16);
    });

    public static Image Copy() => Create(Color.FromArgb(49, 116, 171), (g, pen, brush) =>
    {
        using var paleBrush = new SolidBrush(Color.FromArgb(214, 231, 246));
        using var whiteBrush = new SolidBrush(Color.White);
        g.FillRectangle(paleBrush, 5, 7, 10, 12);
        g.DrawRectangle(pen, 5, 7, 10, 12);
        g.FillRectangle(whiteBrush, 9, 3, 10, 12);
        g.DrawRectangle(pen, 9, 3, 10, 12);
    });

    public static Image Move() => Create(Color.FromArgb(197, 130, 36), (g, pen, brush) =>
    {
        g.DrawLine(pen, 3, 12, 19, 12);
        g.FillPolygon(brush, new[] { new Point(19, 6), new Point(23, 12), new Point(19, 18) });
    });

    public static Image NewFolder() => Create(Color.FromArgb(214, 155, 31), (g, pen, brush) =>
    {
        using var folderBrush = new SolidBrush(Color.FromArgb(250, 198, 72));
        g.FillRectangle(folderBrush, 3, 8, 18, 12);
        g.FillRectangle(folderBrush, 5, 5, 8, 5);
        g.DrawRectangle(pen, 3, 8, 18, 12);
        g.DrawLine(pen, 14, 14, 20, 14);
        g.DrawLine(pen, 17, 11, 17, 17);
    });

    public static Image Delete() => Create(Color.FromArgb(178, 62, 58), (g, pen, brush) =>
    {
        g.DrawRectangle(pen, 7, 8, 11, 12);
        g.DrawLine(pen, 6, 7, 19, 7);
        g.DrawLine(pen, 10, 4, 15, 4);
        g.DrawLine(pen, 11, 11, 11, 17);
        g.DrawLine(pen, 15, 11, 15, 17);
    });

    public static Image Search() => Create(Color.FromArgb(74, 112, 157), (g, pen, brush) =>
    {
        g.DrawEllipse(pen, 4, 4, 11, 11);
        g.DrawLine(pen, 13, 13, 20, 20);
    });

    public static Image Compare() => Create(Color.FromArgb(47, 154, 205), (g, pen, brush) =>
    {
        g.DrawRectangle(pen, 4, 4, 7, 16);
        g.DrawRectangle(pen, 13, 4, 7, 16);
        g.DrawLine(pen, 6, 9, 9, 9);
        g.DrawLine(pen, 15, 9, 18, 9);
        g.DrawLine(pen, 6, 14, 9, 14);
        g.DrawLine(pen, 15, 14, 18, 14);
        g.FillEllipse(brush, 10, 11, 4, 4);
    });

    public static Image ZipPack() => Create(Color.FromArgb(90, 126, 63), (g, pen, brush) =>
    {
        g.DrawRectangle(pen, 6, 3, 12, 18);
        g.DrawLine(pen, 10, 3, 10, 21);
        for (var y = 5; y <= 17; y += 4)
        {
            g.FillRectangle(brush, 10, y, 3, 2);
        }
        g.DrawLine(pen, 14, 16, 20, 16);
        g.DrawLine(pen, 17, 13, 17, 19);
    });

    public static Image ZipExtract() => Create(Color.FromArgb(90, 126, 63), (g, pen, brush) =>
    {
        g.DrawRectangle(pen, 6, 3, 12, 18);
        g.DrawLine(pen, 10, 3, 10, 21);
        for (var y = 5; y <= 17; y += 4)
        {
            g.FillRectangle(brush, 10, y, 3, 2);
        }
        g.DrawLine(pen, 14, 16, 20, 16);
    });

    public static Image FtpClient() => Create(Color.FromArgb(55, 119, 166), (g, pen, brush) =>
    {
        g.DrawEllipse(pen, 4, 4, 16, 16);
        g.DrawLine(pen, 4, 12, 20, 12);
        g.DrawArc(pen, 8, 4, 8, 16, 90, 180);
        g.DrawArc(pen, 8, 4, 8, 16, 270, 180);
        g.FillPolygon(brush, new[] { new Point(15, 15), new Point(22, 15), new Point(22, 18), new Point(15, 18), new Point(15, 21), new Point(10, 16), new Point(15, 11) });
    });

    public static Image FtpServer() => Create(Color.FromArgb(93, 126, 67), (g, pen, brush) =>
    {
        g.DrawRectangle(pen, 5, 4, 14, 16);
        g.DrawLine(pen, 5, 9, 19, 9);
        g.DrawLine(pen, 5, 14, 19, 14);
        g.FillEllipse(brush, 8, 6, 2, 2);
        g.FillEllipse(brush, 8, 11, 2, 2);
        g.FillEllipse(brush, 8, 16, 2, 2);
        g.DrawLine(pen, 13, 18, 13, 22);
        g.DrawLine(pen, 9, 22, 17, 22);
    });

    private static Image Create(Color color, Action<Graphics, Pen, SolidBrush> draw)
    {
        var bitmap = new Bitmap(24, 24);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new Pen(color, 2F) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
        using var brush = new SolidBrush(color);
        draw(graphics, pen, brush);
        return bitmap;
    }
}
