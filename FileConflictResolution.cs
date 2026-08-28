namespace AzertyCommander;

internal enum FileConflictAction
{
    Replace,
    Skip,
    KeepBoth
}

internal sealed record FileConflict(
    string SourcePath,
    string DestinationPath,
    bool SourceIsDirectory,
    bool DestinationIsDirectory,
    long? SourceSize,
    long? DestinationSize,
    DateTime SourceModified,
    DateTime DestinationModified);

internal sealed class FileConflictPlan
{
    private readonly Dictionary<string, FileConflictAction> _actions;
    private readonly HashSet<string> _reservedDestinations = new(StringComparer.OrdinalIgnoreCase);

    public FileConflictPlan(IEnumerable<(string DestinationPath, FileConflictAction Action)> actions)
    {
        _actions = actions.ToDictionary(
            item => Normalize(item.DestinationPath),
            item => item.Action,
            StringComparer.OrdinalIgnoreCase);
    }

    public string? ResolveDestination(string destinationPath)
    {
        var normalized = Normalize(destinationPath);
        if (!_actions.TryGetValue(normalized, out var action))
        {
            _reservedDestinations.Add(normalized);
            return destinationPath;
        }

        if (action == FileConflictAction.Skip)
        {
            return null;
        }

        if (action == FileConflictAction.Replace)
        {
            _reservedDestinations.Add(normalized);
            return destinationPath;
        }

        var directory = Path.GetDirectoryName(destinationPath) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(destinationPath);
        var extension = Path.GetExtension(destinationPath);
        var index = 2;
        var candidate = Path.Combine(directory, name + " - копия" + extension);
        while (File.Exists(candidate) || Directory.Exists(candidate) || _reservedDestinations.Contains(Normalize(candidate)))
        {
            candidate = Path.Combine(directory, $"{name} - копия ({index++}){extension}");
        }

        _reservedDestinations.Add(Normalize(candidate));
        return candidate;
    }

    private static string Normalize(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
