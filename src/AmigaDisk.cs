using System.IO.Compression;
using System.Text;

namespace DualDeckCommander;

/// <summary>
/// Amiga lemezképek: ADF (floppy), ADZ (gzip-elt ADF), DMS (DiskMasher) és HDF (merevlemez,
/// RDB-partíciókkal vagy anélkül). AmigaDOS fájlrendszer: OFS, FFS, INTL, DIRCACHE.
/// </summary>
internal sealed class AmigaDiskVfs : VirtualFsBase
{
    private readonly Stream _stream;
    private readonly object _lock = new();
    private readonly string _kind;
    private readonly List<AmigaVolume> _volumes = new();
    private readonly byte[]? _dmsImage;      // DMS esetén a kibontott ADF
    private readonly string? _notes;

    private sealed record DmsImageFile;

    public AmigaDiskVfs(string path) : base(path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        switch (ext)
        {
            case ".dms":
                _kind = "Amiga DMS lemezkép";
                _dmsImage = DmsDecoder.Decode(File.ReadAllBytes(path), out var dmsInfo);
                _notes = dmsInfo;
                _stream = new MemoryStream(_dmsImage, false);
                break;
            case ".adz":
                _kind = "Amiga floppy (ADZ)";
                var ms = new MemoryStream();
                using (var fs = File.OpenRead(path))
                using (var gz = new GZipStream(fs, CompressionMode.Decompress))
                    gz.CopyTo(ms);
                ms.Position = 0;
                _stream = ms;
                break;
            default:
                _stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
                _kind = ext == ".hdf" || _stream.Length > 4 * 1024 * 1024 ? "Amiga merevlemezkép (HDF)" : "Amiga floppy (ADF)";
                break;
        }

        try
        {
            DetectVolumes();
        }
        catch
        {
            _stream.Dispose();
            throw;
        }

        var root = new VEntry { IsDirectory = true, Children = new List<VEntry>(), Modified = File.GetLastWriteTime(path) };
        if (_dmsImage != null)
        {
            // a kibontott lemezkép is elérhető (pl. DMS -> ADF átalakításhoz)
            root.Children.Add(new VEntry
            {
                Name = Path.GetFileNameWithoutExtension(path) + ".adf",
                Path = Path.GetFileNameWithoutExtension(path) + ".adf",
                Size = _dmsImage.Length,
                Modified = root.Modified,
                Attributes = FileAttributes.Archive,
                Data = new DmsImageFile(),
            });
        }

        if (_volumes.Count == 1 && _dmsImage == null)
        {
            // egyetlen kötet: a gyökér maga a kötet
            root = _volumes[0].CreateRootEntry("");
        }
        else
        {
            foreach (var v in _volumes)
            {
                // DMS-ben a kötet neve, merevlemezképen "DH0 (Work)" formájú mappa
                string folder = _dmsImage != null && v.VolumeName.Length > 0 ? v.VolumeName : v.FolderName;
                folder = string.Concat(folder.Split(Path.GetInvalidFileNameChars())).Trim();
                if (folder.Length == 0) folder = v.DisplayName;
                root.Children.Add(v.CreateRootEntry(folder));
            }
        }

        if (_volumes.Count == 0 && _dmsImage == null)
            throw new InvalidDataException(
                "Nem AmigaDOS formátumú lemez (pl. saját formátumú játéklemez), ezért nem böngészhető.");
        Root = root;
    }

    public override string KindName => _kind;

    public override string Summary
    {
        get
        {
            var parts = new List<string> { _kind };
            if (_volumes.Count == 1) parts.Add(_volumes[0].Describe());
            else if (_volumes.Count > 1) parts.Add($"{_volumes.Count} partíció");
            else parts.Add("nem AmigaDOS lemez");
            return string.Join("  •  ", parts);
        }
    }

    public override IEnumerable<string> InfoLines()
    {
        foreach (var l in base.InfoLines()) yield return l;
        yield return "";
        if (_notes != null) yield return _notes;
        if (_volumes.Count == 0) yield return "A lemez nem AmigaDOS formátumú – csak a kibontott .adf érhető el.";
        foreach (var v in _volumes) yield return v.DisplayName + ":  " + v.Describe();
    }

    // ---- kötetek felderítése ---------------------------------------------------

    private void DetectVolumes()
    {
        long len = _stream.Length;
        var first = ReadBytes(0, Math.Min(512, (int)len));
        if (first.Length >= 4 && first[0] == 'R' && first[1] == 'D' && first[2] == 'S' && first[3] == 'K')
        {
            ReadRdb(0);
            return;
        }
        // Az RDB nem mindig a 0. blokkban van (az első 16 blokkban kereshető)
        for (int b = 1; b < 16 && (b + 1) * 512L <= len; b++)
        {
            var blk = ReadBytes(b * 512L, 4);
            if (blk[0] == 'R' && blk[1] == 'D' && blk[2] == 'S' && blk[3] == 'K')
            {
                ReadRdb(b);
                return;
            }
        }

        // Partíció nélküli kép (floppy vagy "hardfile")
        var vol = AmigaVolume.TryOpen(this, 0, len / 512, 512, 2, null, "DF0");
        if (vol != null) _volumes.Add(vol);
    }

    private void ReadRdb(long rdbBlock)
    {
        var rdb = ReadBytes(rdbBlock * 512, 512);
        uint blockSize = BE32(rdb, 16);
        if (blockSize is < 256 or > 65536) blockSize = 512;
        uint part = BE32(rdb, 28);
        var seen = new HashSet<uint>();
        int index = 0;
        while (part != 0xFFFFFFFF && seen.Add(part) && seen.Count < 64)
        {
            var pb = ReadBytes(part * (long)blockSize, (int)blockSize);
            if (pb.Length < 256 || pb[0] != 'P' || pb[1] != 'A' || pb[2] != 'R' || pb[3] != 'T') break;
            uint next = BE32(pb, 16);
            int nameLen = Math.Min((int)pb[36], 31);
            string name = Encoding.Latin1.GetString(pb, 37, nameLen);
            if (name.Length == 0) name = "DH" + index;

            // DosEnvec a 128-as eltolástól
            int env = 128;
            uint sizeBlockLongs = BE32(pb, env + 4);
            uint surfaces = BE32(pb, env + 12);
            uint secPerBlock = Math.Max(1, BE32(pb, env + 16));
            uint blocksPerTrack = BE32(pb, env + 20);
            uint reserved = BE32(pb, env + 24);
            uint lowCyl = BE32(pb, env + 36);
            uint highCyl = BE32(pb, env + 40);
            uint dosType = BE32(pb, env + 64);

            int fsBlock = (int)(sizeBlockLongs * 4 * secPerBlock);
            if (fsBlock < 256) fsBlock = 512;
            long sectorsPerCyl = (long)surfaces * blocksPerTrack;
            long start = lowCyl * sectorsPerCyl * sizeBlockLongs * 4;
            long blocks = (highCyl - lowCyl + 1) * sectorsPerCyl / secPerBlock;

            var vol = AmigaVolume.TryOpen(this, start, blocks, fsBlock, (int)Math.Max(reserved, 1), dosType, name);
            if (vol != null) _volumes.Add(vol);
            else _volumes.Add(AmigaVolume.Unsupported(this, name, dosType));
            part = next;
            index++;
        }
    }

    // ---- nyers olvasás ----------------------------------------------------------

    internal byte[] ReadBytes(long offset, int count)
    {
        lock (_lock)
        {
            if (offset < 0 || offset >= _stream.Length) return new byte[count];
            var buf = new byte[count];
            _stream.Position = offset;
            int total = 0;
            while (total < count)
            {
                int r = _stream.Read(buf, total, count - total);
                if (r == 0) break;
                total += r;
            }
            return buf;
        }
    }

    internal static uint BE32(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);

    // ---- VirtualFs -----------------------------------------------------------------

    public override List<VEntry> GetChildren(VEntry dir)
    {
        if (dir.Children != null) return dir.Children;
        if (dir.Data is AmigaVolume.DirRef d)
        {
            dir.Children = d.Volume.ReadDirectory(d.Block, dir.Path);
            return dir.Children;
        }
        return dir.Children = new List<VEntry>();
    }

    public override Stream OpenFile(VEntry entry)
    {
        if (entry.Data is DmsImageFile && _dmsImage != null) return new MemoryStream(_dmsImage, false);
        if (entry.Data is AmigaVolume.FileRef f) return new MemoryStream(f.Volume.ReadFile(f.Block, entry.Path), false);
        throw new IOException("Nem fájl: " + entry.Path);
    }

    public override void Dispose() => _stream.Dispose();
}

/// <summary>Egy AmigaDOS kötet (OFS / FFS) egy lemezképen belül.</summary>
internal sealed class AmigaVolume
{
    internal sealed record DirRef(AmigaVolume Volume, uint Block);
    internal sealed record FileRef(AmigaVolume Volume, uint Block);

    private const int T_HEADER = 2, T_DATA = 8, T_LIST = 16;
    private const int ST_ROOT = 1, ST_USERDIR = 2, ST_FILE = -3, ST_LINKFILE = -4, ST_LINKDIR = 4;

    private readonly AmigaDiskVfs _disk;
    private readonly long _offset;
    private readonly int _bsize;
    private readonly long _numBlocks;
    private readonly uint _rootBlock;
    private readonly bool _ffs;
    private readonly int _flags;
    private readonly bool _supported = true;

    public string DisplayName { get; }
    public string VolumeName { get; private set; } = "";
    public DateTime Created { get; private set; }
    public uint DosType { get; }

    private AmigaVolume(AmigaDiskVfs disk, long offset, long numBlocks, int bsize, uint rootBlock, uint dosType, string name)
    {
        _disk = disk;
        _offset = offset;
        _numBlocks = numBlocks;
        _bsize = bsize;
        _rootBlock = rootBlock;
        DosType = dosType;
        _flags = (int)(dosType & 0xFF);
        _ffs = (_flags & 1) != 0;
        DisplayName = name;
    }

    private AmigaVolume(AmigaDiskVfs disk, string name, uint dosType)
    {
        _disk = disk;
        DisplayName = name;
        DosType = dosType;
        _supported = false;
    }

    public static AmigaVolume Unsupported(AmigaDiskVfs disk, string name, uint dosType) => new(disk, name, dosType);

    /// <summary>Kötet megnyitása; null, ha nem AmigaDOS (OFS/FFS) formátumú.</summary>
    public static AmigaVolume? TryOpen(AmigaDiskVfs disk, long offset, long numBlocks, int bsize, int reserved,
        uint? dosTypeHint, string name)
    {
        if (numBlocks < 4) return null;
        var boot = disk.ReadBytes(offset, 4);
        uint dosType = AmigaDiskVfs.BE32(boot, 0);
        if ((dosType & 0xFFFFFF00) != 0x444F5300) // "DOS"
        {
            if (dosTypeHint is { } h && (h & 0xFFFFFF00) == 0x444F5300) dosType = h;
            else return null;
        }
        if ((dosType & 0xFF) > 7) return null;

        // A gyökérblokk a kötet közepén van; néhány formázó eggyel eltér
        long mid = (numBlocks - 1 + reserved) / 2;
        foreach (long candidate in new[] { mid, numBlocks / 2, mid + 1, mid - 1 })
        {
            if (candidate <= 0 || candidate >= numBlocks) continue;
            var vol = new AmigaVolume(disk, offset, numBlocks, bsize, (uint)candidate, dosType, name);
            if (vol.ValidateRoot()) return vol;
        }
        return null;
    }

    private bool ValidateRoot()
    {
        var b = ReadBlock(_rootBlock);
        if (S32(b, 0) != T_HEADER || S32(b, _bsize - 4) != ST_ROOT || !ChecksumOk(b)) return false;
        VolumeName = ReadName(b);
        Created = AmigaDate(b, _bsize - 92);
        return true;
    }

    public string Describe()
    {
        if (!_supported)
        {
            string t = Encoding.Latin1.GetString(new[]
            {
                (byte)(DosType >> 24), (byte)(DosType >> 16), (byte)(DosType >> 8),
            });
            return $"nem támogatott fájlrendszer ({t}\\{DosType & 0xFF})";
        }
        string fs = _ffs ? "FFS" : "OFS";
        if ((_flags & 2) != 0 || (_flags & 4) != 0) fs += " INTL";
        if ((_flags & 4) != 0) fs += " DIRCACHE";
        return $"kötet: {VolumeName}  •  {fs}  •  {FilePanel.FormatBytes(_numBlocks * _bsize)}";
    }

    /// <summary>Mappanév a lemezkép gyökerében, pl. "DH0 (Work)".</summary>
    public string FolderName => _supported ? $"{DisplayName} ({VolumeName})" : $"{DisplayName} (nem támogatott)";

    /// <summary>A kötet gyökérmappája; a név és az útvonal azonos ("" = a lemezkép gyökere).</summary>
    public VEntry CreateRootEntry(string path)
    {
        if (!_supported)
            return new VEntry
            {
                Name = path, Path = path, IsDirectory = true, Size = -1,
                Attributes = FileAttributes.Directory, Children = new List<VEntry>(),
            };
        return new VEntry
        {
            Name = path,
            Path = path,
            IsDirectory = true,
            Size = -1,
            Modified = Created,
            Attributes = FileAttributes.Directory,
            Data = new DirRef(this, _rootBlock),
        };
    }

    // ---- blokkok ------------------------------------------------------------------

    private byte[] ReadBlock(uint block)
    {
        if (block >= _numBlocks) throw new InvalidDataException($"Hibás blokkszám: {block}");
        return _disk.ReadBytes(_offset + (long)block * _bsize, _bsize);
    }

    private static int S32(byte[] b, int o) => (int)AmigaDiskVfs.BE32(b, o);
    private static uint U32(byte[] b, int o) => AmigaDiskVfs.BE32(b, o);

    private bool ChecksumOk(byte[] b)
    {
        uint sum = 0;
        for (int i = 0; i < _bsize; i += 4) sum += U32(b, i);
        return sum == 0;
    }

    private string ReadName(byte[] b)
    {
        int len = Math.Min((int)b[_bsize - 80], 30);
        return Encoding.Latin1.GetString(b, _bsize - 79, len);
    }

    private static DateTime AmigaDate(byte[] b, int o)
    {
        try
        {
            uint days = AmigaDiskVfs.BE32(b, o), mins = AmigaDiskVfs.BE32(b, o + 4), ticks = AmigaDiskVfs.BE32(b, o + 8);
            if (days > 100000 || mins >= 1440) return DateTime.MinValue;
            return new DateTime(1978, 1, 1).AddDays(days).AddMinutes(mins).AddSeconds(ticks / 50.0);
        }
        catch { return DateTime.MinValue; }
    }

    // ---- könyvtár ---------------------------------------------------------------

    public List<VEntry> ReadDirectory(uint dirBlock, string dirPath)
    {
        var result = new List<VEntry>();
        var b = ReadBlock(dirBlock);
        int htSize = _bsize / 4 - 56;
        var visited = new HashSet<uint>();
        for (int i = 0; i < htSize; i++)
        {
            uint blk = U32(b, 24 + i * 4);
            int guard = 0;
            while (blk != 0 && blk < _numBlocks && visited.Add(blk) && guard++ < 100000)
            {
                var h = ReadBlock(blk);
                if (S32(h, 0) != T_HEADER || !ChecksumOk(h)) break;
                int sec = S32(h, _bsize - 4);
                string name = ReadName(h);
                if (name.Length > 0 && name.IndexOfAny(new[] { '/', '\\', ':' }) < 0 &&
                    sec is ST_USERDIR or ST_FILE or ST_LINKDIR or ST_LINKFILE)
                {
                    uint target = blk;
                    if (sec is ST_LINKDIR or ST_LINKFILE)
                    {
                        // kemény link: a valódi bejegyzésre mutat
                        target = U32(h, _bsize - 44);
                        var t = target < _numBlocks ? ReadBlock(target) : null;
                        if (t == null || S32(t, 0) != T_HEADER) { blk = U32(h, _bsize - 16); continue; }
                        sec = S32(t, _bsize - 4);
                        h = t;
                    }
                    string path = dirPath.Length == 0 ? name : dirPath + "\\" + name;
                    var time = AmigaDate(h, _bsize - 92);
                    if (sec == ST_USERDIR)
                    {
                        result.Add(new VEntry
                        {
                            Name = name, Path = path, IsDirectory = true, Size = -1, Modified = time,
                            Attributes = FileAttributes.Directory, Data = new DirRef(this, target),
                        });
                    }
                    else if (sec == ST_FILE)
                    {
                        result.Add(new VEntry
                        {
                            Name = name, Path = path, IsDirectory = false, Size = U32(h, _bsize - 188), Modified = time,
                            Attributes = FileAttributes.Archive, Data = new FileRef(this, target),
                        });
                    }
                }
                blk = U32(h, _bsize - 16); // hash lánc
            }
        }
        result.Sort((x, y) =>
            x.IsDirectory != y.IsDirectory ? (x.IsDirectory ? -1 : 1)
                : string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    // ---- fájl -----------------------------------------------------------------------

    public byte[] ReadFile(uint headerBlock, string pathForErrors)
    {
        var h = ReadBlock(headerBlock);
        long size = U32(h, _bsize - 188);
        if (size > int.MaxValue) throw new IOException("Túl nagy fájl: " + pathForErrors);
        var output = new byte[size];
        if (size == 0) return output;

        int htSize = _bsize / 4 - 56;
        int payload = _ffs ? _bsize : _bsize - 24;
        long needed = (size + payload - 1) / payload;

        // adatblokkok listája: a fejlécben és a kiterjesztés-blokkokban, fordított sorrendben
        var blocks = new List<uint>((int)Math.Min(needed, 1 << 20));
        var cur = h;
        var seen = new HashSet<uint> { headerBlock };
        while (true)
        {
            int count = Math.Min(S32(cur, 8), htSize);
            for (int k = 0; k < count && blocks.Count < needed; k++)
                blocks.Add(U32(cur, 24 + (htSize - 1 - k) * 4));
            if (blocks.Count >= needed) break;
            uint ext = U32(cur, _bsize - 8);
            if (ext == 0 || !seen.Add(ext)) break;
            cur = ReadBlock(ext);
            if (S32(cur, 0) != T_LIST) throw new InvalidDataException("Sérült fájl (kiterjesztés-blokk): " + pathForErrors);
        }
        if (blocks.Count < needed) throw new InvalidDataException("Sérült fájl (hiányzó blokkok): " + pathForErrors);

        long pos = 0;
        foreach (uint blk in blocks)
        {
            var d = ReadBlock(blk);
            if (_ffs)
            {
                int n = (int)Math.Min(_bsize, size - pos);
                Array.Copy(d, 0, output, pos, n);
                pos += n;
            }
            else
            {
                if (S32(d, 0) != T_DATA) throw new InvalidDataException("Sérült OFS adatblokk: " + pathForErrors);
                int n = (int)Math.Min(Math.Min(U32(d, 12), (uint)payload), size - pos);
                Array.Copy(d, 24, output, pos, n);
                pos += n;
            }
        }
        if (pos != size) throw new InvalidDataException("Sérült fájl (méreteltérés): " + pathForErrors);
        return output;
    }
}

/// <summary>
/// DMS (DiskMasher) kitömörítő: NONE, SIMPLE, QUICK, MEDIUM, DEEP, HEAVY1, HEAVY2.
/// Teemu Suutari "ancient" könyvtárának DMS dekódolója alapján (BSD licenc).
/// Jelszóval védett (titkosított) DMS nem támogatott.
/// </summary>
internal static class DmsDecoder
{
    private sealed class Stop : Exception { }

    public static byte[] Decode(byte[] data, out string info)
    {
        if (data.Length < 56 || data[0] != 'D' || data[1] != 'M' || data[2] != 'S' || data[3] != '!')
            throw new InvalidDataException("Nem DMS fájl.");
        int flags = BE16(data, 10);
        if ((flags & 2) != 0) throw new InvalidDataException("Jelszóval védett DMS – nem támogatott.");
        if ((flags & 32) != 0) throw new InvalidDataException("MS-DOS lemez DMS-ben – nem támogatott.");
        bool hd = (flags & 16) != 0;
        int trackLen = hd ? 22528 : 11264;
        var image = new byte[trackLen * 80];

        var ctx = new byte[16384];
        var tmp = new byte[65536];
        int ctxPos = 0;
        bool initContext = true;
        AdaptiveHuffman? deep = null;
        CanonicalHuffman? heavySym = null, heavyOff = null;
        bool heavyLastInit = false;
        uint heavyLastOffset = 0;
        int tracks = 0;
        int modesUsed = 0;

        int offset = 56;
        int prevTrack = 0;
        while (offset + 20 <= data.Length)
        {
            if (data[offset] != 'T' || data[offset + 1] != 'R') break;
            int trackNo = BE16(data, offset + 2);
            int packedLen = BE16(data, offset + 6);
            int tmpLen = BE16(data, offset + 8);
            int rawLen = BE16(data, offset + 10);
            int tflags = data[offset + 12];
            int mode = data[offset + 13];
            int sum = BE16(data, offset + 14);
            int start = offset + 20;
            if (start + packedLen > data.Length) throw new InvalidDataException("Csonka DMS fájl.");
            offset = start + packedLen;

            if (trackNo >= 0x8000) continue;     // információs sáv (-1)
            if (trackNo < prevTrack) break;
            prevTrack = trackNo;
            if (trackNo >= 80) break;
            if (mode > 6) throw new InvalidDataException("Ismeretlen DMS tömörítés: " + mode);
            if (rawLen > trackLen) throw new InvalidDataException("Hibás DMS sáv.");
            modesUsed |= 1 << mode;

            if (initContext)
            {
                Array.Clear(ctx);
                ctxPos = 0;
                deep = null;
                initContext = false;
            }

            int outStart = trackNo * trackLen;
            var bits = new MsbStream(data, start, packedLen);

            // ---- dekódolók: kimenet egy (puffer, kezdet, hossz) tartományba ----
            void Quick(Out o)
            {
                while (!o.Full)
                {
                    if (bits.Bits(1) != 0)
                    {
                        o.Write(ctx[ctxPos++] = (byte)bits.Bits(8));
                        ctxPos &= 0xFF;
                    }
                    else
                    {
                        int count = (int)bits.Bits(2) + 2;
                        int off = (ctxPos - (int)bits.Bits(8) - 1) & 0xFF;
                        for (int i = 0; i < count; i++)
                        {
                            o.Write(ctx[ctxPos++] = ctx[(i + off) & 0xFF]);
                            ctxPos &= 0xFF;
                        }
                    }
                }
                ctxPos = (ctxPos + 5) & 0xFF;
            }

            void Medium(Out o)
            {
                while (!o.Full)
                {
                    if (bits.Bits(1) != 0)
                    {
                        o.Write(ctx[ctxPos++] = (byte)bits.Bits(8));
                        ctxPos &= 0x3FFF;
                    }
                    else
                    {
                        uint count = LengthCode(bits, (int)bits.Bits(4), n => bits.Bits(n));
                        uint c = count;
                        uint rawDist = LengthCode(bits, (int)((count >> 4) & 0xF),
                            n => ((c & 0xF) << (n - 4)) | bits.Bits(n - 4));
                        int len = (int)(count >> 8) + 3;
                        int off = (int)((ctxPos - rawDist - 1) & 0x3FFF);
                        for (int i = 0; i < len; i++)
                        {
                            o.Write(ctx[ctxPos++] = ctx[(i + off) & 0x3FFF]);
                            ctxPos &= 0x3FFF;
                        }
                    }
                }
                ctxPos = (ctxPos + 66) & 0x3FFF;
            }

            void Deep(Out o)
            {
                deep ??= new AdaptiveHuffman();
                while (!o.Full)
                {
                    int sym = deep.Decode(() => (int)bits.Bits(1));
                    if (sym < 256)
                    {
                        o.Write(ctx[ctxPos++] = (byte)sym);
                        ctxPos &= 0x3FFF;
                    }
                    else
                    {
                        int count = sym - 253;
                        int off = (int)((ctxPos - LengthCode(bits, (int)bits.Bits(4), n => bits.Bits(n)) - 1) & 0x3FFF);
                        for (int i = 0; i < count; i++)
                        {
                            o.Write(ctx[ctxPos++] = ctx[(i + off) & 0x3FFF]);
                            ctxPos &= 0x3FFF;
                        }
                    }
                }
                ctxPos = (ctxPos + 60) & 0x3FFF;
            }

            void Heavy(Out o, bool initTables, bool use8k)
            {
                if (!heavyLastInit)
                {
                    heavyLastOffset = use8k ? 0u : ~0u;
                    heavyLastInit = true;
                }
                if (initTables)
                {
                    heavySym = CanonicalHuffman.Read(bits, 9, 5);
                    heavyOff = CanonicalHuffman.Read(bits, 5, 4);
                }
                if (heavySym == null || heavyOff == null) throw new InvalidDataException("Hibás DMS (HEAVY tábla).");
                int mask = use8k ? 0x1FFF : 0xFFF;
                int bitLength = use8k ? 14 : 13;
                while (!o.Full)
                {
                    int sym = heavySym.Decode(bits);
                    if (sym < 256)
                    {
                        o.Write(ctx[ctxPos++] = (byte)sym);
                        ctxPos &= mask;
                    }
                    else
                    {
                        int count = sym - 253;
                        int offLen = heavyOff.Decode(bits);
                        uint raw = heavyLastOffset;
                        if (offLen != bitLength)
                        {
                            raw = offLen != 0 ? (1u << (offLen - 1)) | bits.Bits(offLen - 1) : 0;
                            heavyLastOffset = raw;
                        }
                        int off = (int)((ctxPos - raw - 1) & (uint)mask);
                        for (int i = 0; i < count; i++)
                        {
                            o.Write(ctx[ctxPos++] = ctx[(i + off) & mask]);
                            ctxPos &= mask;
                        }
                    }
                }
            }

            // ---- sáv feldolgozása ----
            var output = new Out(image, outStart, rawLen);
            void Run(Action<Out> f, Out o)
            {
                try { f(o); }
                catch (Stop) { /* elfogyott a bemenet vagy betelt a kimenet */ }
            }

            void TwoStage(Action<Out> f)
            {
                var t = new Out(tmp, 0, tmpLen);
                Run(f, t);
                var rle = new ByteSource(tmp, 0, t.Written);
                Run(o => UnRle(o, rle), output);
            }

            switch (mode)
            {
                case 0:
                    Array.Copy(data, start, image, outStart, Math.Min(packedLen, trackLen));
                    output.Written = Math.Min(packedLen, trackLen);
                    rawLen = output.Written;
                    break;
                case 1:
                    Run(o => UnRle(o, new ByteSource(data, start, packedLen)), output);
                    break;
                case 2: TwoStage(Quick); break;
                case 3: TwoStage(Medium); break;
                case 4: TwoStage(Deep); break;
                case 5:
                case 6:
                {
                    bool init = (tflags & 2) != 0;
                    bool use8k = mode == 6;
                    if ((tflags & 4) != 0) TwoStage(o => Heavy(o, init, use8k));
                    else Run(o => Heavy(o, init, use8k), output);
                    break;
                }
            }

            // HEAVY: legfeljebb 1 hiányzó bájt, az utolsó bájt az ellenőrzőösszegből javítható
            if (mode >= 5)
            {
                int missing = rawLen - output.Written;
                if (missing > 1) throw new InvalidDataException($"Sérült DMS sáv: {trackNo}");
                if (missing == 1) output.Write(0);
                int proto = Sum(image, outStart, rawLen);
                if ((proto & 0xFFFF) != sum && rawLen > 0)
                {
                    int last = image[outStart + rawLen - 1];
                    int fix = (sum - (proto - last)) & 0xFFFF;
                    if (fix < 0x100) image[outStart + rawLen - 1] = (byte)fix;
                }
            }

            if ((Sum(image, outStart, rawLen) & 0xFFFF) != sum)
                throw new InvalidDataException($"DMS ellenőrzőösszeg-hiba a(z) {trackNo}. sávon.");
            if ((tflags & 1) == 0) initContext = true;
            tracks++;
            if (trackNo >= 79) break;
        }

        if (tracks == 0) throw new InvalidDataException("Üres DMS fájl.");
        var names = new[] { "NONE", "SIMPLE", "QUICK", "MEDIUM", "DEEP", "HEAVY1", "HEAVY2" };
        info = $"DMS: {tracks} sáv, {(hd ? "HD" : "DD")}, tömörítés: " +
               string.Join(", ", Enumerable.Range(0, 7).Where(m => (modesUsed & (1 << m)) != 0).Select(m => names[m]));
        return image;
    }

    private static int BE16(byte[] b, int o) => b[o] << 8 | b[o + 1];

    private static int Sum(byte[] b, int start, int len)
    {
        int s = 0;
        for (int i = 0; i < len; i++) s += b[start + i];
        return s;
    }

    /// <summary>Változó hosszú kód: alap index (0..15) + extra bitek.</summary>
    private static readonly int[] VlBits = { 7, 7, 8, 8, 8, 9, 9, 9, 9, 10, 10, 10, 11, 11, 11, 12 };
    private static readonly uint[] VlOffsets = BuildOffsets();

    private static uint[] BuildOffsets()
    {
        var o = new uint[16];
        uint acc = 0;
        for (int i = 0; i < 16; i++) { o[i] = acc; acc += 1u << VlBits[i]; }
        return o;
    }

    private static uint LengthCode(MsbStream _, int baseIndex, Func<int, uint> reader) =>
        VlOffsets[baseIndex] + reader(VlBits[baseIndex]);

    private static void UnRle(Out o, ByteSource input)
    {
        while (!o.Full)
        {
            byte ch = input.Read();
            int count = 1;
            if (ch == 0x90)
            {
                count = input.Read();
                if (count == 0) count = 1;
                else ch = input.Read();
                if (count == 0xFF)
                {
                    count = input.Read() << 8;
                    count |= input.Read();
                }
            }
            for (int i = 0; i < count; i++) o.Write(ch);
        }
    }

    // ---- segédosztályok ----

    private sealed class Out
    {
        private readonly byte[] _buf;
        private readonly int _start;
        private readonly int _len;
        public int Written;

        public Out(byte[] buf, int start, int len) { _buf = buf; _start = start; _len = len; }
        public bool Full => Written >= _len;

        public void Write(byte b)
        {
            if (Written >= _len) throw new Stop();
            _buf[_start + Written++] = b;
        }
    }

    private sealed class ByteSource
    {
        private readonly byte[] _buf;
        private int _pos;
        private readonly int _end;
        public ByteSource(byte[] buf, int start, int len) { _buf = buf; _pos = start; _end = start + len; }

        public byte Read()
        {
            if (_pos >= _end) throw new Stop();
            return _buf[_pos++];
        }
    }

    internal sealed class MsbStream
    {
        private readonly byte[] _buf;
        private int _pos;
        private readonly int _end;
        private uint _acc;
        private int _count;

        public MsbStream(byte[] buf, int start, int len) { _buf = buf; _pos = start; _end = start + len; }

        public uint Bits(int n)
        {
            if (n == 0) return 0;
            while (_count < n)
            {
                if (_pos >= _end) throw new Stop();
                _acc = (_acc << 8) | _buf[_pos++];
                _count += 8;
            }
            _count -= n;
            return (_acc >> _count) & ((1u << n) - 1);
        }
    }

    /// <summary>Kanonikus Huffman-tábla (HEAVY mód); üres tábla esetén mindig ugyanazt adja.</summary>
    internal sealed class CanonicalHuffman
    {
        private readonly int[] _count = new int[33];
        private readonly int[] _first = new int[33];
        private readonly int[] _offset = new int[33];
        private readonly int[] _symbols;
        private readonly int _emptyValue = -1;
        private readonly int _maxLen;

        private CanonicalHuffman(int emptyValue) { _emptyValue = emptyValue; _symbols = Array.Empty<int>(); }

        private CanonicalHuffman(byte[] lengths, int n)
        {
            for (int i = 0; i < n; i++) _count[lengths[i]]++;
            _count[0] = 0;
            _maxLen = 0;
            for (int l = 1; l <= 32; l++) if (_count[l] > 0) _maxLen = l;
            if (_maxLen == 0) throw new InvalidDataException("Hibás DMS Huffman-tábla.");
            int code = 0, idx = 0;
            for (int l = 1; l <= 32; l++)
            {
                code = (code + (l > 1 ? _count[l - 1] : 0)) << (l > 1 ? 1 : 0);
                _first[l] = code;
                _offset[l] = idx;
                idx += _count[l];
            }
            _symbols = new int[idx];
            var fill = (int[])_offset.Clone();
            for (int i = 0; i < n; i++) if (lengths[i] > 0) _symbols[fill[lengths[i]]++] = i;
        }

        public static CanonicalHuffman Read(MsbStream bits, int countBits, int valueBits)
        {
            int count = (int)bits.Bits(countBits);
            if (count == 0) return new CanonicalHuffman((int)bits.Bits(countBits));
            var lengths = new byte[count];
            ulong kraft = 0;
            for (int i = 0; i < count; i++)
            {
                int l = (int)bits.Bits(valueBits);
                if (l > 0)
                {
                    kraft += 1UL << (32 - l);
                    if (kraft > 1UL << 32) throw new InvalidDataException("Hibás DMS Huffman-tábla.");
                }
                lengths[i] = (byte)l;
            }
            return new CanonicalHuffman(lengths, count);
        }

        public int Decode(MsbStream bits)
        {
            if (_emptyValue >= 0) return _emptyValue;
            int code = 0;
            for (int l = 1; l <= _maxLen; l++)
            {
                code = (code << 1) | (int)bits.Bits(1);
                int d = code - _first[l];
                if (d >= 0 && d < _count[l]) return _symbols[_offset[l] + d];
            }
            throw new InvalidDataException("Hibás DMS Huffman-kód.");
        }
    }
}

/// <summary>Adaptív Huffman-fa (LZHUF, 314 szimbólum) – a DMS DEEP módhoz.</summary>
internal sealed class AdaptiveHuffman
{
    private const int NChar = 314;
    private const int T = NChar * 2 - 1;
    private const int R = T - 1;
    private const int MaxFreq = 0x8000;
    private readonly int[] _freq = new int[T + 1];
    private readonly int[] _prnt = new int[T + NChar];
    private readonly int[] _son = new int[T];

    public AdaptiveHuffman()
    {
        for (int i = 0; i < NChar; i++)
        {
            _freq[i] = 1;
            _son[i] = i + T;
            _prnt[i + T] = i;
        }
        for (int i = 0, j = NChar; j <= R; i += 2, j++)
        {
            _freq[j] = _freq[i] + _freq[i + 1];
            _son[j] = i;
            _prnt[i] = _prnt[i + 1] = j;
        }
        _freq[T] = 0xFFFF;
        _prnt[R] = 0;
    }

    public int Decode(Func<int> bit)
    {
        int c = _son[R];
        while (c < T) c = _son[c + bit()];
        c -= T;
        Update(c);
        return c;
    }

    private void Reconst()
    {
        int j = 0;
        for (int i = 0; i < T; i++)
        {
            if (_son[i] >= T)
            {
                _freq[j] = (_freq[i] + 1) / 2;
                _son[j] = _son[i];
                j++;
            }
        }
        for (int i = 0, jj = NChar; jj < T; i += 2, jj++)
        {
            int f = _freq[jj] = _freq[i] + _freq[i + 1];
            int k = jj - 1;
            while (f < _freq[k]) k--;
            k++;
            int l = jj - k;
            Array.Copy(_freq, k, _freq, k + 1, l);
            _freq[k] = f;
            Array.Copy(_son, k, _son, k + 1, l);
            _son[k] = i;
        }
        for (int i = 0; i < T; i++)
        {
            int k = _son[i];
            if (k >= T) _prnt[k] = i;
            else _prnt[k] = _prnt[k + 1] = i;
        }
    }

    private void Update(int c)
    {
        if (_freq[R] == MaxFreq) Reconst();
        c = _prnt[c + T];
        do
        {
            int k = ++_freq[c];
            int l = c + 1;
            if (k > _freq[l])
            {
                while (k > _freq[++l]) { }
                l--;
                _freq[c] = _freq[l];
                _freq[l] = k;

                int i = _son[c];
                _prnt[i] = l;
                if (i < T) _prnt[i + 1] = l;

                int j = _son[l];
                _son[l] = i;

                _prnt[j] = c;
                if (j < T) _prnt[j + 1] = c;
                _son[c] = j;

                c = l;
            }
        } while ((c = _prnt[c]) != 0);
    }
}
