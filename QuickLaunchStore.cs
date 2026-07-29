using System.Text.Json;

namespace AzertyCommander;

internal static class QuickLaunchStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string StorePath
    {
        get => AppPaths.QuickLaunchPath;
    }

    public static List<QuickLaunchEntry> Load()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return new List<QuickLaunchEntry>();
            }

            var items = JsonSerializer.Deserialize<List<QuickLaunchEntry>>(File.ReadAllText(StorePath), JsonOptions);
            return items?
                .Where(item => !string.IsNullOrWhiteSpace(item.Path))
                .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => new QuickLaunchEntry { Path = group.First().Path })
                .ToList() ?? new List<QuickLaunchEntry>();
        }
        catch
        {
            return new List<QuickLaunchEntry>();
        }
    }

    public static void Save(IEnumerable<QuickLaunchEntry> entries)
    {
        var path = StorePath;
        Directory.CreateDirectory(AppPaths.ConfigDirectory);
        var cleaned = entries
            .Where(item => !string.IsNullOrWhiteSpace(item.Path))
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => new QuickLaunchEntry { Path = group.First().Path })
            .ToList();
        File.WriteAllText(path, JsonSerializer.Serialize(cleaned, JsonOptions));
    }
}
