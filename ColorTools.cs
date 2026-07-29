namespace AzertyCommander;

internal static class ColorTools
{
    public static Color FromHtml(string? value, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        try
        {
            return ColorTranslator.FromHtml(value);
        }
        catch
        {
            return fallback;
        }
    }

    public static string ToHtml(Color color)
    {
        return ColorTranslator.ToHtml(Color.FromArgb(color.R, color.G, color.B));
    }
}
