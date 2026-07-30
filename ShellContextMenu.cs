using System.Runtime.InteropServices;

namespace AzertyCommander;

internal sealed class ShellContextMenu : IDisposable
{
    private static readonly Guid ShellFolderId = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid ContextMenuId = new("000214E4-0000-0000-C000-000000000046");

    private readonly List<IntPtr> _absolutePidls;
    private readonly object _contextMenuObject;
    private readonly IShellFolder _parentFolder;
    private readonly IContextMenu _contextMenu;
    private readonly IContextMenu2? _contextMenu2;
    private readonly IContextMenu3? _contextMenu3;

    private const uint CommandFirst = 1;
    private const uint CommandLast = 0x7FFF;
    private const uint CmfNormal = 0x00000000;
    private const uint CmfExplore = 0x00000004;
    private const uint CmfCanRename = 0x00000010;
    private const uint CmfExtendedVerbs = 0x00000100;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCommand = 0x0100;
    private const uint CmicMaskUnicode = 0x00004000;
    private const uint CmicMaskPtInvoke = 0x20000000;
    private const int SwShowNormal = 1;

    private ShellContextMenu(
        IShellFolder parentFolder,
        object contextMenuObject,
        List<IntPtr> absolutePidls)
    {
        _parentFolder = parentFolder;
        _contextMenuObject = contextMenuObject;
        _contextMenu = (IContextMenu)contextMenuObject;
        _contextMenu2 = contextMenuObject as IContextMenu2;
        _contextMenu3 = contextMenuObject as IContextMenu3;
        _absolutePidls = absolutePidls;
    }

    public static bool Show(IWin32Window owner, IReadOnlyList<string> paths, Point screenLocation)
    {
        if (owner is null)
        {
            throw new ArgumentNullException(nameof(owner));
        }

        using var menu = Create(owner.Handle, paths);
        return menu.Show(owner.Handle, screenLocation);
    }

    public static bool CanCreateForPaths(IReadOnlyList<string> paths)
    {
        using var menu = Create(IntPtr.Zero, paths);
        return menu is not null;
    }

    public void Dispose()
    {
        foreach (var pidl in _absolutePidls)
        {
            if (pidl != IntPtr.Zero)
            {
                CoTaskMemFree(pidl);
            }
        }

        if (Marshal.IsComObject(_contextMenuObject))
        {
            Marshal.FinalReleaseComObject(_contextMenuObject);
        }

        if (Marshal.IsComObject(_parentFolder))
        {
            Marshal.FinalReleaseComObject(_parentFolder);
        }
    }

    private static ShellContextMenu Create(IntPtr ownerHandle, IReadOnlyList<string> paths)
    {
        var validPaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path)))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (validPaths.Count == 0)
        {
            throw new InvalidOperationException("Нет файла или папки для меню Windows.");
        }

        var parentPath = GetShellParentPath(validPaths[0]);
        if (validPaths.Any(path => !string.Equals(GetShellParentPath(path), parentPath, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("Меню Windows для нескольких элементов работает только внутри одной папки.");
        }

        var absolutePidls = new List<IntPtr>();
        var childPidls = new List<IntPtr>();
        IShellFolder? parentFolder = null;

        try
        {
            for (var index = 0; index < validPaths.Count; index++)
            {
                var parseResult = SHParseDisplayName(validPaths[index], IntPtr.Zero, out var absolutePidl, 0, out _);
                ThrowIfFailed(parseResult, "Windows не распознала путь: " + validPaths[index]);
                absolutePidls.Add(absolutePidl);

                var shellFolderId = ShellFolderId;
                var bindResult = SHBindToParent(absolutePidl, ref shellFolderId, out var itemParentFolder, out var childPidl);
                ThrowIfFailed(bindResult, "Windows не открыла родительскую папку для меню.");

                if (index == 0)
                {
                    parentFolder = itemParentFolder;
                }
                else if (Marshal.IsComObject(itemParentFolder))
                {
                    Marshal.FinalReleaseComObject(itemParentFolder);
                }

                childPidls.Add(childPidl);
            }

            if (parentFolder is null)
            {
                throw new InvalidOperationException("Windows не создала меню.");
            }

            var contextMenuId = ContextMenuId;
            var getResult = parentFolder.GetUIObjectOf(
                ownerHandle,
                (uint)childPidls.Count,
                childPidls.ToArray(),
                ref contextMenuId,
                IntPtr.Zero,
                out var contextMenuPointer);
            ThrowIfFailed(getResult, "Windows не создала контекстное меню.");

            var contextMenuObject = Marshal.GetObjectForIUnknown(contextMenuPointer);
            Marshal.Release(contextMenuPointer);
            return new ShellContextMenu(parentFolder, contextMenuObject, absolutePidls);
        }
        catch
        {
            foreach (var pidl in absolutePidls)
            {
                if (pidl != IntPtr.Zero)
                {
                    CoTaskMemFree(pidl);
                }
            }

            if (parentFolder is not null && Marshal.IsComObject(parentFolder))
            {
                Marshal.FinalReleaseComObject(parentFolder);
            }

            throw;
        }
    }

    private bool Show(IntPtr ownerHandle, Point screenLocation)
    {
        var menuHandle = CreatePopupMenu();
        if (menuHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException("Windows не создала меню.");
        }

        try
        {
            var queryFlags = CmfNormal | CmfExplore | CmfCanRename;
            if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift)
            {
                queryFlags |= CmfExtendedVerbs;
            }

            var queryResult = _contextMenu.QueryContextMenu(menuHandle, 0, CommandFirst, CommandLast, queryFlags);
            ThrowIfFailed(queryResult, "Windows не заполнила контекстное меню.");

            using var messageHook = new ShellContextMenuMessageHook(ownerHandle, _contextMenu2, _contextMenu3);
            var command = TrackPopupMenuEx(
                menuHandle,
                TpmReturnCommand | TpmRightButton,
                screenLocation.X,
                screenLocation.Y,
                ownerHandle,
                IntPtr.Zero);

            if (command == 0)
            {
                return false;
            }

            InvokeCommand(ownerHandle, screenLocation, command - CommandFirst);
            return true;
        }
        finally
        {
            DestroyMenu(menuHandle);
        }
    }

    private void InvokeCommand(IntPtr ownerHandle, Point screenLocation, uint commandOffset)
    {
        var commandPointer = new IntPtr(commandOffset);
        var commandInfo = new CminvokeCommandInfoEx
        {
            cbSize = Marshal.SizeOf<CminvokeCommandInfoEx>(),
            fMask = CmicMaskUnicode | CmicMaskPtInvoke,
            hwnd = ownerHandle,
            lpVerb = commandPointer,
            lpVerbW = commandPointer,
            nShow = SwShowNormal,
            ptInvoke = new NativePoint(screenLocation.X, screenLocation.Y)
        };

        var invokeResult = _contextMenu.InvokeCommand(ref commandInfo);
        ThrowIfFailed(invokeResult, "Windows не выполнила команду меню.");
    }

    private static string GetShellParentPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var trimmedPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(trimmedPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            return parent;
        }

        var root = Path.GetPathRoot(fullPath);
        return string.IsNullOrWhiteSpace(root) ? fullPath : root;
    }

    private static void ThrowIfFailed(int hresult, string message)
    {
        if (hresult < 0)
        {
            throw new InvalidOperationException(message + Environment.NewLine + Marshal.GetExceptionForHR(hresult)?.Message);
        }
    }

    private sealed class ShellContextMenuMessageHook : NativeWindow, IDisposable
    {
        private readonly IContextMenu2? _contextMenu2;
        private readonly IContextMenu3? _contextMenu3;

        private const int WmDrawItem = 0x002B;
        private const int WmMeasureItem = 0x002C;
        private const int WmInitMenuPopup = 0x0117;
        private const int WmMenuChar = 0x0120;

        public ShellContextMenuMessageHook(IntPtr ownerHandle, IContextMenu2? contextMenu2, IContextMenu3? contextMenu3)
        {
            _contextMenu2 = contextMenu2;
            _contextMenu3 = contextMenu3;
            AssignHandle(ownerHandle);
        }

        public void Dispose()
        {
            ReleaseHandle();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg is WmDrawItem or WmMeasureItem or WmInitMenuPopup or WmMenuChar)
            {
                if (_contextMenu3 is not null)
                {
                    var result = _contextMenu3.HandleMenuMsg2((uint)m.Msg, m.WParam, m.LParam, out var handledResult);
                    if (result >= 0)
                    {
                        m.Result = handledResult;
                        return;
                    }
                }
                else if (_contextMenu2 is not null)
                {
                    var result = _contextMenu2.HandleMenuMsg((uint)m.Msg, m.WParam, m.LParam);
                    if (result >= 0)
                    {
                        m.Result = IntPtr.Zero;
                        return;
                    }
                }
            }

            base.WndProc(ref m);
        }
    }

    [ComImport]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig]
        int ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, ref uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);

        [PreserveSig]
        int EnumObjects(IntPtr hwnd, uint grfFlags, out IntPtr ppenumIDList);

        [PreserveSig]
        int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);

        [PreserveSig]
        int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);

        [PreserveSig]
        int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);

        [PreserveSig]
        int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);

        [PreserveSig]
        int GetAttributesOf(uint cidl, IntPtr apidl, ref uint rgfInOut);

        [PreserveSig]
        int GetUIObjectOf(
            IntPtr hwndOwner,
            uint cidl,
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] apidl,
            ref Guid riid,
            IntPtr rgfReserved,
            out IntPtr ppv);

        [PreserveSig]
        int GetDisplayNameOf(IntPtr pidl, uint uFlags, out IntPtr pName);

        [PreserveSig]
        int SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
    }

    [ComImport]
    [Guid("000214E4-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

        [PreserveSig]
        int InvokeCommand(ref CminvokeCommandInfoEx pici);

        [PreserveSig]
        int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
    }

    [ComImport]
    [Guid("000214F4-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu2
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

        [PreserveSig]
        int InvokeCommand(ref CminvokeCommandInfoEx pici);

        [PreserveSig]
        int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);

        [PreserveSig]
        int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
    }

    [ComImport]
    [Guid("BCFCE0A0-EC17-11D0-8D10-00A0C90F2719")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu3
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

        [PreserveSig]
        int InvokeCommand(ref CminvokeCommandInfoEx pici);

        [PreserveSig]
        int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);

        [PreserveSig]
        int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);

        [PreserveSig]
        int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr plResult);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CminvokeCommandInfoEx
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr lpTitle;
        public IntPtr lpVerbW;
        public IntPtr lpParametersW;
        public IntPtr lpDirectoryW;
        public IntPtr lpTitleW;
        public NativePoint ptInvoke;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint
    {
        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }

        public int X { get; }

        public int Y { get; }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszName,
        IntPtr pbc,
        out IntPtr ppidl,
        uint sfgaoIn,
        out uint psfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(
        IntPtr pidl,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellFolder ppv,
        out IntPtr ppidlLast);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr pv);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenuEx(IntPtr hmenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);
}
