using System.Text;

namespace DualDeckCommander;

/// <summary>
/// Xbox / Xbox 360 lemezkép (XDVDFS, más néven GDF) csak olvasható elérése.
/// Támogatott: XGD1 (eredeti Xbox), XGD2, XGD3 teljes lemezképek, valamint
/// az újraépített / "extract-xiso" képek (a játékpartíció 0-s eltolással).
/// </summary>
internal sealed class XboxIso : VirtualFsBase
{
    public const int SectorSize = 2048;
    private const int MaxTableSize = 64 * 1024 * 1024;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("MICROSOFT*XBOX*MEDIA");

    /// <summary>Ismert játékpartíció-eltolások (a sorrend számít: a 0 az újraépített kép).</summary>
    private static readonly (long Offset, string Name)[] Layouts =
    {
        (0x0, "XISO (újraépített)"),
        (0xFD90000, "XGD2"),
        (0x2080000, "XGD3"),
        (0x18300000, "XGD1"),
    };

    private readonly FileStream _stream;
    private readonly object _lock = new();

    public long PartitionOffset { get; }
    public string LayoutName { get; }
    public DateTime Created { get; }
    public long ImageSize => _stream.Length;

    /// <summary>default.xex / default.xbe adatai (ha kiolvasható).</summary>
    public string? TitleId { get; private set; }
    public string? MediaId { get; private set; }
    public string? TitleName { get; private set; }
    public int DiscNumber { get; private set; }
    public int DiscCount { get; private set; }
    /// <summary>"Xbox 360" vagy "Xbox".</summary>
    public string Platform { get; private set; } = "Xbox";

    /// <summary>A default.xex / default.xbe végrehajtási adatai (GoD átalakításhoz).</summary>
    public XboxExecInfo? Exec { get; private set; }
    /// <summary>Van-e default.xex (Xbox 360) – különben eredeti Xbox (default.xbe).</summary>
    public bool IsXbox360Title { get; private set; }

    public uint RootSector { get; }
    public uint RootSize { get; }

    /// <summary>A játékpartíció ténylegesen használt mérete (a végén lévő üres terület nélkül).</summary>
    public long GetUsedDataSize()
    {
        long max = Math.Max(0x21L * SectorSize, (long)RootSector * SectorSize + RootSize);
        var stack = new Stack<VEntry>();
        stack.Push(Root);
        while (stack.Count > 0)
        {
            foreach (var c in GetChildren(stack.Pop()))
            {
                max = Math.Max(max, (long)(uint)c.Data! * SectorSize + c.Size);
                if (c.IsDirectory) stack.Push(c);
            }
        }
        return max;
    }

    private XboxIso(string path, FileStream stream, long offset, string layout, uint rootSector, uint rootSize, DateTime created)
        : base(path)
    {
        _stream = stream;
        PartitionOffset = offset;
        LayoutName = layout;
        Created = created;
        Root = new VEntry { Name = "", Path = "", IsDirectory = true, Data = rootSector, Size = rootSize, Modified = created };
        RootSector = rootSector;
        RootSize = rootSize;
        if (offset is 0xFD90000 or 0x2080000) Platform = "Xbox 360";
        ReadExecutableInfo();
    }

    public static bool HasIsoExtension(string path)
    {
        string ext = System.IO.Path.GetExtension(path);
        return ext.Equals(".iso", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".xiso", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Megnyitja a képet; null, ha nem Xbox lemezkép (pl. normál Windows ISO).</summary>
    public static XboxIso? TryOpen(string path)
    {
        FileStream? fs = null;
        try
        {
            fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024);
            var header = new byte[0x800];
            foreach (var (offset, name) in Layouts)
            {
                long volumePos = offset + 32L * SectorSize;
                if (fs.Length < volumePos + SectorSize) continue;
                fs.Position = volumePos;
                if (ReadFully(fs, header, header.Length) != header.Length) continue;
                if (!StartsWithMagic(header, 0)) continue;

                uint rootSector = BitConverter.ToUInt32(header, 20);
                uint rootSize = BitConverter.ToUInt32(header, 24);
                long fileTime = BitConverter.ToInt64(header, 28);
                DateTime created;
                try { created = fileTime > 0 ? DateTime.FromFileTimeUtc(fileTime).ToLocalTime() : File.GetLastWriteTime(path); }
                catch { created = File.GetLastWriteTime(path); }

                if (offset + (long)rootSector * SectorSize > fs.Length) continue;
                return new XboxIso(path, fs, offset, name, rootSector, rootSize, created);
            }
        }
        catch
        {
            // olvasási hiba -> nem kezeljük Xbox képként
        }
        fs?.Dispose();
        return null;
    }

    private static bool StartsWithMagic(byte[] buf, int pos)
    {
        for (int i = 0; i < Magic.Length; i++)
            if (buf[pos + i] != Magic[i]) return false;
        return true;
    }

    // =========================================================================
    //  Könyvtárak
    // =========================================================================

    /// <summary>A mappa tartalma (gyorsítótárazva), mappák elöl, név szerint rendezve.</summary>
    public override List<VEntry> GetChildren(VEntry dir)
    {
        if (!dir.IsDirectory) return new List<VEntry>();
        if (dir.Children != null) return dir.Children;

        var result = new List<VEntry>();
        if (dir.Size > 0)
        {
            int size = (int)Math.Min(dir.Size, MaxTableSize);
            byte[] table = ReadAt(PartitionOffset + (long)Sector(dir) * SectorSize, size);
            ParseDirectoryTable(table, dir.Path, Created, result);
        }
        result.Sort((a, b) =>
            a.IsDirectory != b.IsDirectory ? (a.IsDirectory ? -1 : 1)
                : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        dir.Children = result;
        return result;
    }

    /// <summary>
    /// A könyvtártábla bináris fa: minden bejegyzés bal/jobb részfára mutat
    /// (4 bájtos egységekben). 0xFFFF = nincs gyermek; a szektorvégek 0xFF-fel töltöttek.
    /// </summary>
    private static uint Sector(VEntry e) => (uint)e.Data!;

    private static FileAttributes MapAttributes(byte attr, bool isDir)
    {
        FileAttributes a = 0;
        if ((attr & 0x01) != 0) a |= FileAttributes.ReadOnly;
        if ((attr & 0x02) != 0) a |= FileAttributes.Hidden;
        if ((attr & 0x04) != 0) a |= FileAttributes.System;
        if ((attr & 0x20) != 0) a |= FileAttributes.Archive;
        if (isDir) a |= FileAttributes.Directory;
        return a == 0 ? FileAttributes.Normal : a;
    }

    private static void ParseDirectoryTable(byte[] table, string parentPath, DateTime time, List<VEntry> result)
    {
        var visited = new HashSet<int>();
        var stack = new Stack<int>();
        stack.Push(0);
        while (stack.Count > 0)
        {
            int off = stack.Pop();
            if (off < 0 || off + 14 > table.Length || !visited.Add(off)) continue;

            ushort left = BitConverter.ToUInt16(table, off);
            ushort right = BitConverter.ToUInt16(table, off + 2);
            uint sector = BitConverter.ToUInt32(table, off + 4);
            uint size = BitConverter.ToUInt32(table, off + 8);
            byte attr = table[off + 12];
            int nameLen = table[off + 13];

            if (left == 0xFFFF && right == 0xFFFF && sector == 0xFFFFFFFF) continue; // kitöltés
            if (nameLen == 0 || off + 14 + nameLen > table.Length) continue;

            string name = Encoding.Latin1.GetString(table, off + 14, nameLen);
            if (name.IndexOfAny(new[] { '\\', '/', '\0' }) < 0)
            {
                bool isDir = (attr & 0x10) != 0;
                result.Add(new VEntry
                {
                    Name = name,
                    Path = parentPath.Length == 0 ? name : parentPath + "\\" + name,
                    IsDirectory = isDir,
                    Data = sector,
                    Size = size,
                    Modified = time,
                    Attributes = MapAttributes(attr, isDir),
                });
            }

            if (left != 0 && left != 0xFFFF) stack.Push(left * 4);
            if (right != 0 && right != 0xFFFF) stack.Push(right * 4);
        }
    }

    // =========================================================================
    //  Fájlok olvasása
    // =========================================================================

    /// <summary>Csak olvasható adatfolyam egy fájl tartalmára (saját fájlleíróval, szálbiztos).</summary>
    public override Stream OpenFile(VEntry entry)
    {
        if (entry.IsDirectory) throw new IOException("Ez egy mappa: " + entry.Path);
        long start = PartitionOffset + (long)Sector(entry) * SectorSize;
        var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20, FileOptions.SequentialScan);
        if (start + entry.Size > fs.Length)
        {
            fs.Dispose();
            throw new IOException("A lemezkép csonka, a fájl túlnyúlik a végén: " + entry.Path);
        }
        return new SubStream(fs, start, entry.Size);
    }

    private byte[] ReadAt(long position, int count)
    {
        lock (_lock)
        {
            if (position + count > _stream.Length) count = (int)Math.Max(0, _stream.Length - position);
            var buf = new byte[count];
            _stream.Position = position;
            int read = ReadFully(_stream, buf, count);
            if (read < count) Array.Resize(ref buf, read);
            return buf;
        }
    }

    private static int ReadFully(Stream s, byte[] buf, int count)
    {
        int total = 0;
        while (total < count)
        {
            int r = s.Read(buf, total, count - total);
            if (r == 0) break;
            total += r;
        }
        return total;
    }

    // =========================================================================
    //  default.xex (Xbox 360) / default.xbe (Xbox) fejléc
    // =========================================================================

    private void ReadExecutableInfo()
    {
        try
        {
            var children = GetChildren(Root);
            var xex = children.FirstOrDefault(c => c.Name.Equals("default.xex", StringComparison.OrdinalIgnoreCase));
            if (xex != null && xex.Size >= 0x18)
            {
                Platform = "Xbox 360";
                ParseXex(ReadAt(PartitionOffset + (long)Sector(xex) * SectorSize, (int)Math.Min(xex.Size, 256 * 1024)));
                return;
            }
            var xbe = children.FirstOrDefault(c => c.Name.Equals("default.xbe", StringComparison.OrdinalIgnoreCase));
            if (xbe != null && xbe.Size >= 0x200)
            {
                Platform = "Xbox";
                ParseXbe(ReadAt(PartitionOffset + (long)Sector(xbe) * SectorSize, (int)Math.Min(xbe.Size, 64 * 1024)));
            }
        }
        catch
        {
            // nem kötelező információ
        }
    }

    private static uint BE32(byte[] b, int o) =>
        (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);

    private void ParseXex(byte[] h)
    {
        if (h.Length < 0x18 || h[0] != 'X' || h[1] != 'E' || h[2] != 'X' || h[3] != '2') return;
        uint count = BE32(h, 0x14);
        for (int i = 0; i < count && i < 64; i++)
        {
            int p = 0x18 + i * 8;
            if (p + 8 > h.Length) break;
            uint key = BE32(h, p);
            uint value = BE32(h, p + 4);
            if (key == 0x00040006 && value + 24 <= h.Length) // végrehajtási információ
            {
                int v = (int)value;
                MediaId = BE32(h, v).ToString("X8");
                TitleId = BE32(h, v + 12).ToString("X8");
                DiscNumber = h[v + 18];
                DiscCount = h[v + 19];
                Exec = new XboxExecInfo(BE32(h, v), BE32(h, v + 4), BE32(h, v + 8), BE32(h, v + 12),
                    h[v + 16], h[v + 17], h[v + 18], h[v + 19]);
                IsXbox360Title = true;
                TitleName ??= XboxTitles.Find(BE32(h, v + 12));
            }
        }
    }

    private void ParseXbe(byte[] h)
    {
        if (h.Length < 0x120 || h[0] != 'X' || h[1] != 'B' || h[2] != 'E' || h[3] != 'H') return;
        uint baseAddr = BitConverter.ToUInt32(h, 0x104);
        uint certAddr = BitConverter.ToUInt32(h, 0x118);
        long cert = (long)certAddr - baseAddr;
        if (cert < 0 || cert + 0x5C > h.Length) return;
        uint tid = BitConverter.ToUInt32(h, (int)cert + 8);
        TitleId = tid.ToString("X8");
        uint version = cert + 180 <= h.Length ? BitConverter.ToUInt32(h, (int)cert + 176) : 0;
        Exec = new XboxExecInfo(0, version, 0, tid, 0, 0, 1, 1);
        string name = Encoding.Unicode.GetString(h, (int)cert + 0x0C, 80);
        int nul = name.IndexOf('\0');
        TitleName = (nul >= 0 ? name[..nul] : name).Trim();
        if (TitleName.Length == 0) TitleName = XboxTitles.Find(tid);
    }

    public override string KindName => Platform + " lemezkép";

    /// <summary>Rövid leírás az állapotsorhoz.</summary>
    public override string Summary
    {
        get
        {
            var parts = new List<string> { Platform + " lemezkép", LayoutName };
            if (TitleName != null) parts.Add(TitleName);
            if (TitleId != null) parts.Add("Title ID " + TitleId);
            if (DiscCount > 1) parts.Add($"{DiscNumber}/{DiscCount}. lemez");
            return string.Join("  •  ", parts);
        }
    }

    public override IEnumerable<string> InfoLines()
    {
        int files = 0, dirs = 0;
        long total = 0;
        try
        {
            var stack = new Stack<VEntry>();
            stack.Push(Root);
            while (stack.Count > 0)
            {
                foreach (var c in GetChildren(stack.Pop()))
                {
                    if (c.IsDirectory) { dirs++; stack.Push(c); }
                    else { files++; total += c.Size; }
                }
            }
        }
        catch { /* részleges adat is jó */ }

        yield return "Fájl:  " + FilePath;
        yield return "";
        yield return "Platform:  " + Platform;
        yield return "Formátum:  " + LayoutName + $"   (játékpartíció: 0x{PartitionOffset:X})";
        yield return "Lemezkép mérete:  " + FilePanel.FormatBytes(ImageSize);
        yield return $"Tartalom:  {files:N0} fájl, {dirs:N0} mappa, {FilePanel.FormatBytes(total)}";
        yield return "Létrehozva:  " + Created.ToString("yyyy.MM.dd HH:mm");
        if (TitleName != null) yield return "Cím:  " + TitleName;
        if (TitleId != null) yield return "Title ID:  " + TitleId;
        if (MediaId != null) yield return "Media ID:  " + MediaId;
        if (DiscCount > 0) yield return $"Lemez:  {DiscNumber} / {DiscCount}";
    }

    public override void Dispose() => _stream.Dispose();
}

/// <summary>Egy nagyobb adatfolyam csak olvasható szelete.</summary>
internal sealed class SubStream : Stream
{
    private readonly Stream _base;
    private readonly long _start;
    private readonly long _length;
    private long _pos;

    public SubStream(Stream baseStream, long start, long length)
    {
        _base = baseStream;
        _start = start;
        _length = length;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get => _pos;
        set => _pos = Math.Clamp(value, 0, _length);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        long remaining = _length - _pos;
        if (remaining <= 0) return 0;
        if (count > remaining) count = (int)remaining;
        _base.Position = _start + _pos;
        int read = _base.Read(buffer, offset, count);
        _pos += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _pos + offset,
            _ => _length + offset,
        };
        return _pos;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _base.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Az Xbox / Xbox 360 futtatható fájl végrehajtási adatai.</summary>
internal sealed record XboxExecInfo(uint MediaId, uint Version, uint BaseVersion, uint TitleId,
    byte Platform, byte ExecutableType, byte DiscNumber, byte DiscCount);

/// <summary>Title ID → játéknév (az iso2god-rs listája alapján, kb. 4900 cím).</summary>
internal static class XboxTitles
{
    private static Dictionary<uint, string>? _map;
    private static readonly object Lock = new();

    /// <summary>Teszteléshez: a lista helye fájlként (különben beágyazott erőforrás).</summary>
    public static string? OverridePath { get; set; }

    public static string? Find(uint titleId)
    {
        lock (Lock)
        {
            _map ??= Load();
        }
        return _map.TryGetValue(titleId, out var n) ? n : null;
    }

    private static Dictionary<uint, string> Load()
    {
        var map = new Dictionary<uint, string>();
        try
        {
            using Stream? s = OverridePath != null
                ? File.OpenRead(OverridePath)
                : System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("xbox_titles.txt");
            if (s == null) return map;
            using var r = new StreamReader(s, Encoding.UTF8);
            string? line;
            while ((line = r.ReadLine()) != null)
            {
                int tab = line.IndexOf('\t');
                if (tab == 8 && uint.TryParse(line[..8], System.Globalization.NumberStyles.HexNumber, null, out uint id))
                    map[id] = line[(tab + 1)..];
            }
        }
        catch
        {
            // a név csak kényelmi adat
        }
        return map;
    }
}
