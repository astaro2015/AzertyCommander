using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace AzertyCommander;

internal static class ShellIconProvider
{
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiSmallIcon = 0x000000001;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeNormal = 0x00000080;
    private static readonly ConcurrentDictionary<string, Image> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Image GetSmallIcon(string path, bool isDirectory, bool isParent)
    {
        var extension = Path.GetExtension(path);
        var useActualFileIcon = File.Exists(path) &&
            (string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(extension, ".ico", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase));
        var key = isParent || isDirectory
            ? "folder"
            : useActualFileIcon ? "file:" + path : extension;
        if (string.IsNullOrWhiteSpace(key))
        {
            key = "file";
        }

        return Cache.GetOrAdd(key, _ => LoadSmallIcon(path, isDirectory || isParent, useActualFileIcon));
    }

    private static Image LoadSmallIcon(string path, bool isDirectory, bool useActualFileIcon)
    {
        var attributes = isDirectory ? FileAttributeDirectory : FileAttributeNormal;
        var iconPath = isDirectory ? Environment.GetFolderPath(Environment.SpecialFolder.Windows) : Path.GetExtension(path);
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            iconPath = "file";
        }

        var flags = ShgfiIcon | ShgfiSmallIcon;
        if (!useActualFileIcon)
        {
            flags |= ShgfiUseFileAttributes;
        }

        var info = new ShFileInfo();
        var result = SHGetFileInfo(
            useActualFileIcon ? path : iconPath,
            attributes,
            ref info,
            (uint)Marshal.SizeOf<ShFileInfo>(),
            flags);

        if (result == IntPtr.Zero || info.IconHandle == IntPtr.Zero)
        {
            return (isDirectory ? SystemIcons.WinLogo : SystemIcons.Application).ToBitmap();
        }

        try
        {
            using var icon = Icon.FromHandle(info.IconHandle);
            return icon.ToBitmap();
        }
        finally
        {
            DestroyIcon(info.IconHandle);
        }
    }

    [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref ShFileInfo psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("User32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileInfo
    {
        public IntPtr IconHandle;
        public int IconIndex;
        public uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string TypeName;
    }
}
