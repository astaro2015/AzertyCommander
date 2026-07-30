using System.Text.Json;

namespace AzertyCommander;

internal static class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(AppPaths.SettingsPath))
            {
                return new AppSettings();
            }

            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsPath), JsonOptions) ?? new AppSettings();
            Normalize(settings);
            return settings;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        Normalize(settings);
        Directory.CreateDirectory(AppPaths.ConfigDirectory);
        File.WriteAllText(AppPaths.SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    private static void Normalize(AppSettings settings)
    {
        if (settings.Theme.RowHeight == 36)
        {
            settings.Theme.RowHeight = AppThemeSettings.DefaultRowHeight;
        }

        settings.Theme.RowHeight = Math.Clamp(settings.Theme.RowHeight, 24, 96);
    }
}
