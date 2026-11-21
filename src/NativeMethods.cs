using System.Runtime.InteropServices;

namespace DualDeckCommander;

internal static class NativeMethods
{
    // ---- Ikonok -------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    public const uint SHGFI_ICON = 0x100;
    public const uint SHGFI_SMALLICON = 0x1;
    public const uint SHGFI_USEFILEATTRIBUTES = 0x10;
    public const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    public const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);

    // ---- Tulajdonságok ablak ------------------------------------------------
    public const uint SHOP_FILEPATH = 0x2;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool SHObjectProperties(IntPtr hwnd, uint shopObjectType, string pszObjectName, string? pszPropertyPage);

    // ---- Természetes rendezés (file2 < file10) ------------------------------
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string psz1, string psz2);

    public static int NaturalCompare(string a, string b)
    {
        try { return StrCmpLogicalW(a, b); }
        catch { return string.Compare(a, b, StringComparison.CurrentCultureIgnoreCase); }
    }

    // ---- Lomtárba törlés (SHFileOperation) ----------------------------------
    // 64 bites Windowson 8 bájtos, 32 bitesen 1 bájtos igazítás kell.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT64
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    private struct SHFILEOPSTRUCT32
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation64(ref SHFILEOPSTRUCT64 op);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation32(ref SHFILEOPSTRUCT32 op);

    private const uint FO_DELETE = 0x3;
    private const ushort FOF_SILENT = 0x4;
    private const ushort FOF_NOCONFIRMATION = 0x10;
    private const ushort FOF_ALLOWUNDO = 0x40;
    private const ushort FOF_NOERRORUI = 0x400;

    /// <summary>Lomtárba helyezi a megadott elemet. Hibakód esetén kivételt dob.</summary>
    public static void SendToRecycleBin(string path, IntPtr owner)
    {
        string from = path + "\0\0"; // dupla null-lezárás kötelező
        ushort flags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI;
        int result;
        bool aborted;
        if (IntPtr.Size == 8)
        {
            var op = new SHFILEOPSTRUCT64 { hwnd = owner, wFunc = FO_DELETE, pFrom = from, fFlags = flags };
            result = SHFileOperation64(ref op);
            aborted = op.fAnyOperationsAborted != 0;
        }
        else
        {
            var op = new SHFILEOPSTRUCT32 { hwnd = owner, wFunc = FO_DELETE, pFrom = from, fFlags = flags };
            result = SHFileOperation32(ref op);
            aborted = op.fAnyOperationsAborted != 0;
        }
        if (result != 0)
            throw new IOException($"Nem sikerült a Lomtárba helyezni (kód: 0x{result:X}): {path}");
        if (aborted)
            throw new OperationCanceledException();
    }

    // ---- Eszközváltozás (USB beszúrás) ---------------------------------------
    public const int WM_DEVICECHANGE = 0x0219;
}
