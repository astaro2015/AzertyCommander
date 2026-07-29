using System.Globalization;

namespace AzertyCommander;

internal sealed class FtpRemoteEntry
{
    public string Name { get; init; } = string.Empty;

    public string FullPath { get; init; } = "/";

    public bool IsDirectory { get; init; }

    public bool IsParent { get; init; }

    public long? Size { get; init; }

    public DateTime? Modified { get; init; }

    public string DisplayName => IsParent ? "[..]" : Name;

    public string TypeText
    {
        get
        {
            if (IsDirectory)
            {
                return "<Папка>";
            }

            var extension = Path.GetExtension(Name).TrimStart('.');
            return string.IsNullOrWhiteSpace(extension) ? "файл" : extension.ToLowerInvariant();
        }
    }

    public string SizeText => IsDirectory || Size is null
        ? string.Empty
        : Size.Value.ToString("N0", CultureInfo.InvariantCulture).Replace(",", " ");

    public string DateText => Modified?.ToString("dd.MM.yyyy HH:mm") ?? string.Empty;
}
