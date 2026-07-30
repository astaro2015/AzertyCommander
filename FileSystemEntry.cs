using System.Globalization;

namespace AzertyCommander;

internal sealed class FileSystemEntry
{
    public FileSystemEntry(string name, string fullPath, bool isDirectory, bool isParent, long? size, DateTime modified, FileAttributes attributes)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        IsParent = isParent;
        Size = size;
        Modified = modified;
        Attributes = attributes;
    }

    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public bool IsParent { get; }
    public long? Size { get; private set; }
    public DateTime Modified { get; }
    public FileAttributes Attributes { get; }

    public Image SmallIcon => ShellIconProvider.GetSmallIcon(FullPath, IsDirectory, IsParent);
    public string DisplayName => IsParent ? "[..]" : Name;
    public string TypeText => IsParent ? string.Empty : IsDirectory ? "<Папка>" : GetExtensionText();
    public string SizeText => IsParent || Size is null ? string.Empty : Size.Value.ToString("N0", CultureInfo.CurrentCulture);
    public string DateText => IsParent ? string.Empty : Modified.ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);
    public string AttributesText => IsParent ? string.Empty : BuildAttributesText();

    public void SetSize(long size)
    {
        Size = Math.Max(0, size);
    }

    private string GetExtensionText()
    {
        var ext = Path.GetExtension(Name);
        return string.IsNullOrWhiteSpace(ext) ? string.Empty : ext.TrimStart('.').ToLowerInvariant();
    }

    private string BuildAttributesText()
    {
        Span<char> chars = stackalloc char[4] { '-', '-', '-', '-' };

        if ((Attributes & FileAttributes.Archive) != 0)
        {
            chars[0] = 'a';
        }

        if ((Attributes & FileAttributes.ReadOnly) != 0)
        {
            chars[1] = 'r';
        }

        if ((Attributes & FileAttributes.Hidden) != 0)
        {
            chars[2] = 'h';
        }

        if ((Attributes & FileAttributes.System) != 0)
        {
            chars[3] = 's';
        }

        return new string(chars);
    }
}
