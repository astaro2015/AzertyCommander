namespace AzertyCommander;

internal static class AppPaths
{
    public static string ConfigDirectory
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(root, "AZERTY Commander");
        }
    }

    public static string SettingsPath => Path.Combine(ConfigDirectory, "settings.json");
    public static string QuickLaunchPath => Path.Combine(ConfigDirectory, "quick-launch.json");
    public static string BatchRenamePresetsPath => Path.Combine(ConfigDirectory, "batch-rename-presets.json");
    public static string BatchRenameHistoryPath => Path.Combine(ConfigDirectory, "batch-rename-history.json");
}
