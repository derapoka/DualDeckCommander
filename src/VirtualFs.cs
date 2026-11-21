namespace DualDeckCommander;

/// <summary>Egy fájl vagy mappa egy lemezképen / archívumon belül.</summary>
internal sealed class VEntry
{
    public string Name { get; init; } = "";
    /// <summary>Belső útvonal "\" elválasztóval ("" = gyökér).</summary>
    public string Path { get; init; } = "";
    public bool IsDirectory { get; init; }
    public long Size { get; set; }
    public long PackedSize { get; set; } = -1;
    public DateTime Modified { get; set; }
    public FileAttributes Attributes { get; set; } = FileAttributes.Normal;
    /// <summary>Formátumfüggő adat (szektor, archívum-bejegyzés, eltolás...).</summary>
    public object? Data { get; set; }
    internal List<VEntry>? Children;
}

/// <summary>Csak olvasható, mappaként böngészhető tároló (lemezkép vagy archívum).</summary>
internal interface IVirtualFs : IDisposable
{
    string FilePath { get; }
    /// <summary>Rövid típusnév, pl. "ZIP archívum", "Xbox 360 lemezkép".</summary>
    string KindName { get; }
    /// <summary>Egysoros leírás az állapotsorhoz.</summary>
    string Summary { get; }
    VEntry Root { get; }
    List<VEntry> GetChildren(VEntry dir);
    VEntry? Find(string innerPath);
    VEntry? GetParent(VEntry entry);
    long GetTotalSize(VEntry entry, CancellationToken ct = default);
    Stream OpenFile(VEntry entry);

    /// <summary>
    /// A kért fájlok tartalma sorban. Tömör (solid) archívumoknál ez sokkal
    /// gyorsabb, mint fájlonként <see cref="OpenFile"/>.
    /// </summary>
    IEnumerable<(VEntry Entry, Stream Data)> ReadSequential(IReadOnlyCollection<VEntry> files, CancellationToken ct);

    /// <summary>Információs sorok (Alt+Enter).</summary>
    IEnumerable<string> InfoLines();
}

internal abstract class VirtualFsBase : IVirtualFs
{
    protected VirtualFsBase(string filePath)
    {
        FilePath = filePath;
    }

    public string FilePath { get; }
    public abstract string KindName { get; }
    public virtual string Summary => KindName;
    public VEntry Root { get; protected set; } = new() { IsDirectory = true };

    public virtual List<VEntry> GetChildren(VEntry dir) => dir.Children ?? new List<VEntry>();

    public VEntry? Find(string innerPath)
    {
        var parts = innerPath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        var cur = Root;
        foreach (var part in parts)
        {
            var next = GetChildren(cur).FirstOrDefault(c => c.Name.Equals(part, StringComparison.OrdinalIgnoreCase));
            if (next == null) return null;
            cur = next;
        }
        return cur;
    }

    public VEntry? GetParent(VEntry entry)
    {
        if (entry.Path.Length == 0) return null;
        int i = entry.Path.LastIndexOf('\\');
        return i < 0 ? Root : Find(entry.Path[..i]);
    }

    public long GetTotalSize(VEntry entry, CancellationToken ct = default)
    {
        if (!entry.IsDirectory) return entry.Size;
        long total = 0;
        var stack = new Stack<VEntry>();
        stack.Push(entry);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var c in GetChildren(stack.Pop()))
            {
                if (c.IsDirectory) stack.Push(c);
                else total += c.Size;
            }
        }
        return total;
    }

    public abstract Stream OpenFile(VEntry entry);

    public virtual IEnumerable<(VEntry Entry, Stream Data)> ReadSequential(IReadOnlyCollection<VEntry> files, CancellationToken ct)
    {
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            yield return (f, OpenFile(f));
        }
    }

    public virtual IEnumerable<string> InfoLines()
    {
        int files = 0, dirs = 0;
        long size = 0, packed = 0;
        bool hasPacked = true;
        var stack = new Stack<VEntry>();
        stack.Push(Root);
        while (stack.Count > 0)
        {
            foreach (var c in GetChildren(stack.Pop()))
            {
                if (c.IsDirectory) { dirs++; stack.Push(c); continue; }
                files++;
                size += c.Size;
                if (c.PackedSize >= 0) packed += c.PackedSize;
                else hasPacked = false;
            }
        }
        long fileSize = 0;
        try { fileSize = new FileInfo(FilePath).Length; } catch { }

        yield return "Fájl:  " + FilePath;
        yield return "";
        yield return "Típus:  " + KindName;
        yield return $"Tartalom:  {files:N0} fájl, {dirs:N0} mappa";
        yield return "Kibontott méret:  " + FilePanel.FormatBytes(size);
        yield return "Archívum mérete:  " + FilePanel.FormatBytes(fileSize);
        if (hasPacked && size > 0 && files > 0)
            yield return $"Tömörítési arány:  {100.0 * packed / size:0.#}%";
    }

    public virtual void Dispose() { }

    /// <summary>Lapos útvonallistából mappafát épít (a hiányzó mappákat is létrehozza).</summary>
    protected sealed class TreeBuilder
    {
        private readonly Dictionary<string, VEntry> _dirs = new(StringComparer.OrdinalIgnoreCase);
        public VEntry Root { get; } = new() { IsDirectory = true, Children = new List<VEntry>() };

        public TreeBuilder() { _dirs[""] = Root; }

        public static string Normalize(string path) =>
            string.Join("\\", path.Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries)
                .Where(p => p != "." && p != ".."));

        public VEntry GetDir(string path, DateTime? time = null)
        {
            path = Normalize(path);
            if (_dirs.TryGetValue(path, out var d))
            {
                if (time.HasValue) d.Modified = time.Value;
                return d;
            }
            int i = path.LastIndexOf('\\');
            var parent = GetDir(i < 0 ? "" : path[..i]);
            d = new VEntry
            {
                Name = i < 0 ? path : path[(i + 1)..],
                Path = path,
                IsDirectory = true,
                Size = -1,
                Modified = time ?? DateTime.MinValue,
                Attributes = FileAttributes.Directory,
                Children = new List<VEntry>(),
            };
            parent.Children!.Add(d);
            _dirs[path] = d;
            return d;
        }

        /// <summary>Fájl hozzáadása; a meglévő azonos nevűt lecseréli.</summary>
        public VEntry AddFile(string path, long size, DateTime time, object? data, long packed = -1)
        {
            path = Normalize(path);
            int i = path.LastIndexOf('\\');
            var parent = GetDir(i < 0 ? "" : path[..i]);
            var e = new VEntry
            {
                Name = i < 0 ? path : path[(i + 1)..],
                Path = path,
                IsDirectory = false,
                Size = size,
                PackedSize = packed,
                Modified = time,
                Attributes = FileAttributes.Archive,
                Data = data,
            };
            parent.Children!.RemoveAll(c => !c.IsDirectory && c.Name.Equals(e.Name, StringComparison.OrdinalIgnoreCase));
            parent.Children.Add(e);
            return e;
        }

        /// <summary>Rendezés (mappák elöl) és a mappadátumok kitöltése.</summary>
        public VEntry Finish()
        {
            void Walk(VEntry d)
            {
                d.Children!.Sort((a, b) =>
                    a.IsDirectory != b.IsDirectory ? (a.IsDirectory ? -1 : 1)
                        : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
                foreach (var c in d.Children.Where(c => c.IsDirectory))
                {
                    Walk(c);
                    if (c.Modified == DateTime.MinValue)
                        c.Modified = c.Children!.Count > 0 ? c.Children.Max(x => x.Modified) : DateTime.MinValue;
                }
            }
            Walk(Root);
            return Root;
        }
    }
}

/// <summary>Formátum-felismerés és megnyitás.</summary>
internal static class VirtualFs
{
    public static readonly string[] ArchiveExtensions =
        { ".zip", ".rar", ".7z", ".lha", ".lzh", ".lzx", ".adf", ".adz", ".dms", ".hdf" };

    public static bool IsArchive(string path) =>
        ArchiveExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static bool IsDiscImage(string path) => XboxIso.HasIsoExtension(path);

    /// <summary>Böngészhető (lemezkép vagy archívum)?</summary>
    public static bool IsBrowsable(string path) => IsArchive(path) || IsDiscImage(path);

    /// <summary>Archívum megnyitása. Hibánál kivételt dob (érthető üzenettel).</summary>
    public static IVirtualFs OpenArchive(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            return ext switch
            {
                ".zip" => new ZipVfs(path),
                ".lha" or ".lzh" => new LhaVfs(path),
                ".lzx" => new LzxVfs(path),
                ".adf" or ".adz" or ".dms" or ".hdf" => new AmigaDiskVfs(path),
                _ => new SharpCompressVfs(path),
            };
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException("Az archívum sérült vagy nem támogatott:\n" + ex.Message, ex);
        }
    }

    /// <summary>
    /// Egy adatfolyam tartalma memóriába (vagy nagy fájlnál ideiglenes fájlba) másolva,
    /// hogy az archívum lezárása után is olvasható legyen.
    /// </summary>
    public static Stream Buffer(Stream source, long sizeHint)
    {
        Stream target = sizeHint > 256L * 1024 * 1024
            ? new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None,
                1 << 16, FileOptions.DeleteOnClose)
            : new MemoryStream(sizeHint > 0 ? (int)Math.Min(sizeHint, int.MaxValue) : 0);
        source.CopyTo(target);
        target.Position = 0;
        return target;
    }
}

/// <summary>
/// Nem kereshető adatfolyam ismert hosszal (pl. kitömörítő stream), amely
/// lezáráskor a hozzá tartozó erőforrásokat (archívum, fájl) is lezárja.
/// </summary>
internal sealed class LengthStream : Stream
{
    private readonly Stream _inner;
    private readonly long _length;
    private readonly IDisposable[] _owned;
    private long _pos;

    public LengthStream(Stream inner, long length, params IDisposable[] owned)
    {
        _inner = inner;
        _length = length;
        _owned = owned;
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position { get => _pos; set => throw new NotSupportedException(); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int r = _inner.Read(buffer, offset, count);
        _pos += r;
        return r;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
            foreach (var d in _owned) d.Dispose();
        }
        base.Dispose(disposing);
    }
}
