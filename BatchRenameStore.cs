using System.Text.Json;

namespace AzertyCommander;

internal sealed record BatchRenamePreset(string Name, BatchRenameOptions Options);
internal sealed record BatchRenameHistoryEntry(string SourcePath, string DestinationPath, bool IsDirectory);
internal sealed record BatchRenameHistory(DateTime CreatedUtc, IReadOnlyList<BatchRenameHistoryEntry> Entries);

internal static class BatchRenameStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static IReadOnlyList<BatchRenamePreset> LoadPresets()
    {
        try
        {
            return File.Exists(AppPaths.BatchRenamePresetsPath)
                ? JsonSerializer.Deserialize<List<BatchRenamePreset>>(File.ReadAllText(AppPaths.BatchRenamePresetsPath), Options) ?? []
                : [];
        }
        catch { return []; }
    }

    public static void SavePresets(IEnumerable<BatchRenamePreset> presets)
    {
        Directory.CreateDirectory(AppPaths.ConfigDirectory);
        File.WriteAllText(AppPaths.BatchRenamePresetsPath, JsonSerializer.Serialize(presets, Options));
    }

    public static void SaveHistory(IReadOnlyList<BatchRenamePlan> plans)
    {
        var history = new BatchRenameHistory(DateTime.UtcNow, plans.Where(plan => plan.HasChange)
            .Select(plan => new BatchRenameHistoryEntry(plan.SourcePath, plan.DestinationPath, plan.IsDirectory)).ToList());
        Directory.CreateDirectory(AppPaths.ConfigDirectory);
        File.WriteAllText(AppPaths.BatchRenameHistoryPath, JsonSerializer.Serialize(history, Options));
    }

    public static BatchRenameHistory? LoadHistory()
    {
        try
        {
            return File.Exists(AppPaths.BatchRenameHistoryPath)
                ? JsonSerializer.Deserialize<BatchRenameHistory>(File.ReadAllText(AppPaths.BatchRenameHistoryPath), Options)
                : null;
        }
        catch { return null; }
    }

    public static void ClearHistory()
    {
        try { File.Delete(AppPaths.BatchRenameHistoryPath); } catch { }
    }
}
