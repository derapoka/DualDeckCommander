using SharpCompress.Archives;

namespace DualDeckCommander;

/// <summary>RAR (1.5 – 5.x) és 7-Zip archívum a SharpCompress könyvtárral.</summary>
internal sealed class SharpCompressVfs : VirtualFsBase
{
    private readonly IArchive _archive;
    private readonly bool _solid;
    private readonly bool _encrypted;
    private readonly string _kind;

    public SharpCompressVfs(string path) : base(path)
    {
        _kind = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".rar" => "RAR archívum",
            ".7z" => "7-Zip archívum",
            _ => "Archívum",
        };
        _archive = ArchiveFactory.OpenArchive(path);
        try
        {
            _solid = _archive.IsSolid;
            var fallbackTime = File.GetLastWriteTime(path);
            var tb = new TreeBuilder();
            foreach (var e in _archive.Entries)
            {
                if (string.IsNullOrEmpty(e.Key)) continue;
                if (e.IsEncrypted) _encrypted = true;
                var time = e.LastModifiedTime ?? fallbackTime;
                if (e.IsDirectory) tb.GetDir(e.Key, time);
                else tb.AddFile(e.Key, e.Size, time, e, e.CompressedSize);
            }
            Root = tb.Finish();
        }
        catch
        {
            _archive.Dispose();
            throw;
        }
    }

    public override string KindName => _kind + (_solid ? " (tömör)" : "") + (_encrypted ? " – jelszavas" : "");

    public override Stream OpenFile(VEntry entry)
    {
        CheckEncrypted();
        lock (_archive)
        {
            var e = (IArchiveEntry)entry.Data!;
            using var s = e.OpenEntryStream();
            return VirtualFs.Buffer(s, entry.Size);
        }
    }

    public override IEnumerable<(VEntry Entry, Stream Data)> ReadSequential(IReadOnlyCollection<VEntry> files, CancellationToken ct)
    {
        CheckEncrypted();
        if (!_solid || files.Count < 2)
        {
            foreach (var f in files)
            {
                ct.ThrowIfCancellationRequested();
                yield return (f, new LengthStream(((IArchiveEntry)f.Data!).OpenEntryStream(), f.Size));
            }
            yield break;
        }

        // Tömör archívum: egyetlen végigolvasással, különben minden fájlnál elölről kellene kezdeni
        var wanted = new Dictionary<string, VEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files) wanted[f.Path] = f;
        using var reader = _archive.ExtractAllEntries();
        while (wanted.Count > 0 && reader.MoveToNextEntry())
        {
            ct.ThrowIfCancellationRequested();
            var key = reader.Entry.Key;
            if (string.IsNullOrEmpty(key) || reader.Entry.IsDirectory) continue;
            if (!wanted.Remove(TreeBuilder.Normalize(key), out var v)) continue;
            yield return (v, new LengthStream(reader.OpenEntryStream(), v.Size));
        }
    }

    private void CheckEncrypted()
    {
        if (_encrypted)
            throw new IOException("Jelszóval védett archívum – ezt még nem tudom kibontani.");
    }

    public override void Dispose() => _archive.Dispose();
}
