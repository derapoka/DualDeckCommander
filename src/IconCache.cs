using System.Runtime.InteropServices;
using static DualDeckCommander.NativeMethods;

namespace DualDeckCommander;

/// <summary>Rendszerikonok gyorsítótára (kiterjesztésenként, exe/ico/lnk esetén fájlonként).</summary>
internal sealed class IconCache
{
    private static readonly HashSet<string> PerFileExtensions =
        new(StringComparer.OrdinalIgnoreCase) { "exe", "ico", "lnk", "url", "cur", "scr", "msc" };

    private readonly Dictionary<string, int> _map = new(StringComparer.OrdinalIgnoreCase);

    public ImageList Images { get; }

    public IconCache(int size)
    {
        Images = new ImageList { ColorDepth = ColorDepth.Depth32Bit, ImageSize = new Size(size, size) };
    }

    private readonly Dictionary<string, Image?> _drives = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Meghajtó ikonja (pl. "C:\\").</summary>
    public Image? GetDriveIcon(string root)
    {
        if (_drives.TryGetValue(root, out var img)) return img;
        img = null;
        try
        {
            var info = new SHFILEINFO();
            SHGetFileInfo(root, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_SMALLICON);
            if (info.hIcon != IntPtr.Zero)
            {
                using (var icon = Icon.FromHandle(info.hIcon))
                    img = new Bitmap(icon.ToBitmap(), Images.ImageSize);
                DestroyIcon(info.hIcon);
            }
        }
        catch { }
        _drives[root] = img;
        return img;
    }

    public int GetIndex(FileEntry e)
    {
        string key;
        string path;
        uint attr;
        uint flags = SHGFI_ICON | SHGFI_SMALLICON;

        if (e.IsDirectory)
        {
            key = "<dir>";
            path = "folder";
            attr = FILE_ATTRIBUTE_DIRECTORY;
            flags |= SHGFI_USEFILEATTRIBUTES;
        }
        else if (!e.IsVirtual && PerFileExtensions.Contains(e.Extension))
        {
            key = e.FullPath;
            path = e.FullPath;
            attr = 0;
        }
        else
        {
            key = "." + e.Extension;
            path = "file." + e.Extension;
            attr = FILE_ATTRIBUTE_NORMAL;
            flags |= SHGFI_USEFILEATTRIBUTES;
        }

        if (_map.TryGetValue(key, out int idx)) return idx;

        idx = -1;
        try
        {
            var info = new SHFILEINFO();
            SHGetFileInfo(path, attr, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), flags);
            if (info.hIcon != IntPtr.Zero)
            {
                using (var icon = Icon.FromHandle(info.hIcon))
                    Images.Images.Add(icon.ToBitmap());
                DestroyIcon(info.hIcon);
                idx = Images.Images.Count - 1;
            }
        }
        catch
        {
            // ikon nélkül is működik
        }
        _map[key] = idx;
        return idx;
    }
}
