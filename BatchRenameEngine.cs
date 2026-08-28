using System.Globalization;
using System.Text.RegularExpressions;

namespace AzertyCommander;

internal enum BatchRenameCaseMode
{
    Unchanged,
    Lower,
    Upper,
    Title
}

internal sealed record BatchRenameOptions(
    string NameMask,
    string SearchText,
    string ReplaceText,
    string Prefix,
    string Suffix,
    BatchRenameCaseMode CaseMode,
    int CounterStart,
    int CounterDigits,
    bool UseRegex = false);

internal sealed class BatchRenamePlan
{
    public BatchRenamePlan(string sourcePath, string destinationPath, string oldName, string newName, bool isDirectory, string status, bool isValid)
    {
        SourcePath = sourcePath;
        DestinationPath = destinationPath;
        OldName = oldName;
        NewName = newName;
        IsDirectory = isDirectory;
        Status = status;
        IsValid = isValid;
    }

    public string SourcePath { get; }
    public string DestinationPath { get; set; }
    public string OldName { get; }
    public string NewName { get; set; }
    public bool IsDirectory { get; }
    public string Status { get; set; }
    public bool IsValid { get; set; }
    public bool HasChange => IsValid && !string.Equals(SourcePath, DestinationPath, StringComparison.Ordinal);
}

internal static class BatchRenameEngine
{
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static IReadOnlyList<BatchRenamePlan> BuildPlans(IReadOnlyList<FileSystemEntry> entries, BatchRenameOptions options)
    {
        var plans = entries
            .Where(entry => !entry.IsParent)
            .Select((entry, index) => BuildPlan(entry, options, index))
            .ToList();

        var duplicateDestinations = plans
            .Where(plan => plan.IsValid)
            .GroupBy(plan => plan.DestinationPath, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourcePaths = plans
            .Select(plan => plan.SourcePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < plans.Count; index++)
        {
            var plan = plans[index];
            if (!plan.IsValid)
            {
                continue;
            }

            if (duplicateDestinations.Contains(plan.DestinationPath))
            {
                plan.Status = "Одинаковое новое имя";
                plan.IsValid = false;
                continue;
            }

            var destinationExists = File.Exists(plan.DestinationPath) || Directory.Exists(plan.DestinationPath);
            if (destinationExists && !sourcePaths.Contains(plan.DestinationPath))
            {
                plan.Status = "Такое имя уже существует";
                plan.IsValid = false;
            }
        }

        return plans;
    }

    public static Task ApplyAsync(IReadOnlyList<BatchRenamePlan> plans, IProgress<OperationProgress> progress, CancellationToken token)
    {
        return Task.Run(() =>
        {
            Apply(plans, progress, token);
            BatchRenameStore.SaveHistory(plans);
        }, token);
    }

    public static void ValidateManualPlans(IReadOnlyList<BatchRenamePlan> plans)
    {
        foreach (var plan in plans)
        {
            var parent = Path.GetDirectoryName(plan.SourcePath) ?? string.Empty;
            plan.DestinationPath = Path.Combine(parent, plan.NewName ?? string.Empty);
            var error = ValidateName(plan.NewName ?? string.Empty);
            plan.IsValid = error is null;
            plan.Status = error ?? (string.Equals(plan.SourcePath, plan.DestinationPath, StringComparison.Ordinal) ? "Без изменений" : "Готово");
        }

        var duplicateDestinations = plans.Where(plan => plan.IsValid)
            .GroupBy(plan => plan.DestinationPath, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sources = plans.Select(plan => plan.SourcePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in plans.Where(plan => plan.IsValid))
        {
            if (duplicateDestinations.Contains(plan.DestinationPath))
            {
                plan.IsValid = false;
                plan.Status = "Одинаковое новое имя";
            }
            else if ((File.Exists(plan.DestinationPath) || Directory.Exists(plan.DestinationPath)) && !sources.Contains(plan.DestinationPath))
            {
                plan.IsValid = false;
                plan.Status = "Такое имя уже существует";
            }
        }
    }

    public static Task UndoAsync(IReadOnlyList<BatchRenameHistoryEntry> entries, IProgress<OperationProgress> progress, CancellationToken token)
    {
        var plans = entries.Select(entry => new BatchRenamePlan(
            entry.DestinationPath,
            entry.SourcePath,
            Path.GetFileName(entry.DestinationPath),
            Path.GetFileName(entry.SourcePath),
            entry.IsDirectory,
            "Готово",
            true)).ToList();
        return Task.Run(() => Apply(plans, progress, token), token);
    }

    private static BatchRenamePlan BuildPlan(FileSystemEntry entry, BatchRenameOptions options, int index)
    {
        var oldName = entry.Name;
        var extension = entry.IsDirectory ? string.Empty : Path.GetExtension(oldName);
        var baseName = entry.IsDirectory ? oldName : Path.GetFileNameWithoutExtension(oldName);

        if (!string.IsNullOrEmpty(options.SearchText))
        {
            baseName = options.UseRegex
                ? Regex.Replace(baseName, options.SearchText, options.ReplaceText ?? string.Empty, RegexOptions.IgnoreCase)
                : baseName.Replace(options.SearchText, options.ReplaceText ?? string.Empty, StringComparison.CurrentCultureIgnoreCase);
        }

        baseName = ApplyCase(baseName, options.CaseMode);
        var counter = Math.Max(0, options.CounterStart + index)
            .ToString(new string('0', Math.Clamp(options.CounterDigits, 1, 9)), CultureInfo.InvariantCulture);
        var mask = string.IsNullOrWhiteSpace(options.NameMask) ? "[N]" : options.NameMask;
        var containsExtensionToken = mask.Contains("[E]", StringComparison.OrdinalIgnoreCase);
        var generated = ReplaceToken(mask, "[N]", baseName);
        generated = ReplaceToken(generated, "[C]", counter);
        generated = ReplaceToken(generated, "[E]", extension.TrimStart('.'));
        var newName = (options.Prefix ?? string.Empty) + generated + (options.Suffix ?? string.Empty);
        if (!entry.IsDirectory && !containsExtensionToken)
        {
            newName += extension;
        }

        var directory = Path.GetDirectoryName(entry.FullPath) ?? string.Empty;
        var destination = Path.Combine(directory, newName);
        var error = ValidateName(newName);
        if (error is not null)
        {
            return new BatchRenamePlan(entry.FullPath, destination, oldName, newName, entry.IsDirectory, error, false);
        }

        var unchanged = string.Equals(entry.FullPath, destination, StringComparison.Ordinal);
        return new BatchRenamePlan(
            entry.FullPath,
            destination,
            oldName,
            newName,
            entry.IsDirectory,
            unchanged ? "Без изменений" : "Готово",
            true);
    }

    private static void Apply(IReadOnlyList<BatchRenamePlan> plans, IProgress<OperationProgress> progress, CancellationToken token)
    {
        var changed = plans.Where(plan => plan.HasChange).ToList();
        if (changed.Count == 0)
        {
            return;
        }

        if (plans.Any(plan => !plan.IsValid))
        {
            throw new InvalidOperationException("В списке переименования есть ошибки.");
        }

        var staged = new List<(BatchRenamePlan Plan, string TemporaryPath)>();
        var completed = new List<BatchRenamePlan>();
        try
        {
            for (var index = 0; index < changed.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var plan = changed[index];
                var parent = Path.GetDirectoryName(plan.SourcePath) ?? throw new InvalidOperationException("Не удалось определить папку элемента.");
                var temporaryPath = Path.Combine(parent, ".azerty-rename-" + Guid.NewGuid().ToString("N") + ".tmp");
                MovePath(plan.SourcePath, temporaryPath, plan.IsDirectory);
                staged.Add((plan, temporaryPath));
                progress.Report(new OperationProgress(index, changed.Count * 2, "Подготовка: " + plan.OldName));
            }

            for (var index = 0; index < staged.Count; index++)
            {
                token.ThrowIfCancellationRequested();
                var item = staged[index];
                MovePath(item.TemporaryPath, item.Plan.DestinationPath, item.Plan.IsDirectory);
                completed.Add(item.Plan);
                progress.Report(new OperationProgress(changed.Count + index + 1, changed.Count * 2, item.Plan.NewName));
            }
        }
        catch
        {
            foreach (var plan in completed.AsEnumerable().Reverse())
            {
                TryRollback(plan.DestinationPath, plan.SourcePath, plan.IsDirectory);
            }

            foreach (var item in staged.AsEnumerable().Reverse())
            {
                if (File.Exists(item.TemporaryPath) || Directory.Exists(item.TemporaryPath))
                {
                    TryRollback(item.TemporaryPath, item.Plan.SourcePath, item.Plan.IsDirectory);
                }
            }

            throw;
        }
    }

    private static void MovePath(string source, string destination, bool isDirectory)
    {
        if (isDirectory)
        {
            Directory.Move(source, destination);
        }
        else
        {
            File.Move(source, destination);
        }
    }

    private static void TryRollback(string source, string destination, bool isDirectory)
    {
        try
        {
            if (!File.Exists(destination) && !Directory.Exists(destination))
            {
                MovePath(source, destination, isDirectory);
            }
        }
        catch
        {
            // Preserve the original exception; remaining temporary names are visible and recoverable.
        }
    }

    private static string ApplyCase(string value, BatchRenameCaseMode mode)
    {
        return mode switch
        {
            BatchRenameCaseMode.Lower => value.ToLower(CultureInfo.CurrentCulture),
            BatchRenameCaseMode.Upper => value.ToUpper(CultureInfo.CurrentCulture),
            BatchRenameCaseMode.Title => value.Length == 0
                ? value
                : char.ToUpper(value[0], CultureInfo.CurrentCulture) + value[1..].ToLower(CultureInfo.CurrentCulture),
            _ => value
        };
    }

    private static string ReplaceToken(string value, string token, string replacement)
    {
        return value.Replace(token, replacement, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Пустое имя";
        }

        if (name is "." or ".." || name.EndsWith(' ') || name.EndsWith('.'))
        {
            return "Недопустимое имя";
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return "Недопустимый символ";
        }

        var stem = Path.GetFileNameWithoutExtension(name);
        return ReservedNames.Contains(stem) ? "Служебное имя Windows" : null;
    }
}
