using System.Security.Cryptography;
using System.Text;

namespace DualDeckCommander;

/// <summary>
/// Xbox 360 / Xbox lemezkép (ISO) átalakítása Games on Demand (GoD) formátumra.
/// A kimenet az iso2god-rs (Ilia Pozdnyakov, MIT licenc) programéval bájtra azonos:
///   &lt;cél&gt;\&lt;TitleID&gt;\00007000\&lt;MediaID&gt;          – LIVE fejléc
///   &lt;cél&gt;\&lt;TitleID&gt;\00007000\&lt;MediaID&gt;.data\Data0000, Data0001, ...
/// (eredeti Xbox játéknál 00005000 és a Title ID a mappanév).
/// </summary>
internal static class GodConverter
{
    public const long BlockSize = 0x1000;
    public const long BlocksPerPart = 0xA1C4;
    public const long BlocksPerSubpart = 0xCC;
    public const int SubpartsPerPart = 0xCB;
    public const long SubpartSize = BlockSize * BlocksPerSubpart;
    public const long PartDataSize = BlocksPerPart * BlockSize;

    /// <summary>Az átalakítás terve: mi hová kerül, mekkora lesz.</summary>
    internal sealed record Plan(
        string IsoPath,
        long RootOffset,
        long SourceLength,
        long DataSize,
        long BlockCount,
        int PartCount,
        XboxExecInfo Exec,
        bool Xbox360,
        string? GameTitle,
        string HeaderPath,
        string DataDir)
    {
        public uint ContentType => Xbox360 ? 0x7000u : 0x5000u;

        /// <summary>A bemásolandó adat mérete (a Data fájlok a forrás végéig olvasnak).</summary>
        public long BytesToProcess => Math.Max(0, Math.Min(SourceLength - RootOffset, PartCount * PartDataSize));

        /// <summary>A kimenet várható mérete (hash-táblákkal együtt).</summary>
        public long EstimatedOutputSize =>
            BytesToProcess + (long)PartCount * BlockSize + (BytesToProcess + SubpartSize - 1) / SubpartSize * BlockSize;
    }

    public static Plan CreatePlan(XboxIso iso, string destDir, string? gameTitle, bool trim)
    {
        var exec = iso.Exec ?? throw new InvalidDataException(
            "A lemezképen nincs default.xex vagy default.xbe – nem játéklemez, nem alakítható át.");
        long sourceLength = new FileInfo(iso.FilePath).Length;
        long dataSize = trim ? iso.GetUsedDataSize() : sourceLength - iso.PartitionOffset;
        if (dataSize <= 0) throw new InvalidDataException("A lemezkép üres.");
        long blockCount = (dataSize + BlockSize - 1) / BlockSize;
        int partCount = (int)((blockCount + BlocksPerPart - 1) / BlocksPerPart);

        bool x360 = iso.IsXbox360Title;
        string titleId = exec.TitleId.ToString("X8");
        string contentDir = (x360 ? 0x7000 : 0x5000).ToString("X8");
        string mediaId = (x360 ? exec.MediaId : exec.TitleId).ToString("X8");
        string baseDir = Path.Combine(destDir, titleId, contentDir);
        return new Plan(iso.FilePath, iso.PartitionOffset, sourceLength, dataSize, blockCount, partCount, exec, x360,
            string.IsNullOrWhiteSpace(gameTitle) ? null : gameTitle.Trim(),
            Path.Combine(baseDir, mediaId), Path.Combine(baseDir, mediaId + ".data"));
    }

    /// <summary>
    /// Az átalakítás futtatása (háttérszálon). A haladást az <paramref name="ops"/> mezőibe írja
    /// (a TotalBytes / ItemsTotal összegeket a hívó állítja be előre).
    /// </summary>
    public static void Run(Plan p, FileOperations ops, CancellationToken ct)
    {
        ops.CurrentSource = p.IsoPath;

        if (Directory.Exists(p.DataDir)) Directory.Delete(p.DataDir, true);
        Directory.CreateDirectory(p.DataDir);
        bool ok = false;
        try
        {
            var buffer = new byte[SubpartSize];
            for (int part = 0; part < p.PartCount; part++)
            {
                ct.ThrowIfCancellationRequested();
                string partPath = PartPath(p, part);
                ops.CurrentTarget = partPath;
                ops.FileBytesDone = 0;
                ops.FileBytesTotal = Math.Min(PartDataSize, Math.Max(0, p.SourceLength - p.RootOffset - part * PartDataSize));
                WritePart(p, part, partPath, buffer, ops, ct);
                ops.ItemsDone++;
            }

            // MHT lánc: minden rész fő hash-listája tartalmazza a következő rész listájának kivonatát
            ops.CurrentTarget = "Hash-lánc számítása…";
            var mht = HashList.Read(PartPath(p, p.PartCount - 1));
            for (int prev = p.PartCount - 2; prev >= 0; prev--)
            {
                ct.ThrowIfCancellationRequested();
                var prevMht = HashList.Read(PartPath(p, prev));
                prevMht.AddHash(mht.Digest());
                prevMht.Write(PartPath(p, prev));
                mht = prevMht;
            }

            long lastPartSize = new FileInfo(PartPath(p, p.PartCount - 1)).Length;
            ops.CurrentTarget = p.HeaderPath;
            var header = BuildHeader(p, mht.Digest(), lastPartSize + (p.PartCount - 1) * BlockSize * 0xA290);
            File.WriteAllBytes(p.HeaderPath, header);
            ok = true;
        }
        finally
        {
            if (!ok)
            {
                // megszakítás / hiba: a félkész kimenet ne maradjon ott
                try { if (Directory.Exists(p.DataDir)) Directory.Delete(p.DataDir, true); } catch { }
                try { if (File.Exists(p.HeaderPath)) File.Delete(p.HeaderPath); } catch { }
            }
        }
    }

    private static string PartPath(Plan p, int index) => Path.Combine(p.DataDir, $"Data{index:D4}");

    private static void WritePart(Plan p, int partIndex, string partPath, byte[] buffer, FileOperations ops, CancellationToken ct)
    {
        using var src = new FileStream(p.IsoPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20, FileOptions.SequentialScan);
        src.Position = p.RootOffset + partIndex * PartDataSize;
        using var dst = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);

        var master = new HashList();
        master.WriteTo(dst); // helyfoglaló, a végén felülírjuk

        for (int sub = 0; sub < SubpartsPerPart; sub++)
        {
            ct.ThrowIfCancellationRequested();
            int read = ReadFully(src, buffer, (int)SubpartSize);
            if (read == 0) break;

            var subList = new HashList();
            for (int off = 0; off < read; off += (int)BlockSize)
                subList.AddHash(SHA1.HashData(new ReadOnlySpan<byte>(buffer, off, (int)Math.Min(BlockSize, read - off))));

            subList.WriteTo(dst);
            master.AddHash(SHA1.HashData(subList.Buffer));
            dst.Write(buffer, 0, read);

            ops.FileBytesDone += read;
            ops.TotalBytesDone += read;
            if (read < SubpartSize) break;
        }

        dst.Position = 0;
        master.WriteTo(dst);
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
    //  LIVE (CON) fejléc
    // =========================================================================

    private static byte[] LoadTemplate()
    {
        if (TemplateOverride != null) return (byte[])TemplateOverride.Clone();
        using var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("empty_live.bin")
                      ?? throw new InvalidOperationException("Hiányzik a GoD fejléc-sablon.");
        var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Teszteléshez: a sablon közvetlen megadása.</summary>
    public static byte[]? TemplateOverride { get; set; }

    private static byte[] BuildHeader(Plan p, byte[] mhtHash, long partsTotalSize)
    {
        var b = LoadTemplate();
        var e = p.Exec;

        // végrehajtási adatok
        BE32(b, 0x0354, e.MediaId);
        BE32(b, 0x0360, e.TitleId);
        b[0x0364] = e.Platform;
        b[0x0365] = e.ExecutableType;
        b[0x0366] = e.DiscNumber;
        b[0x0367] = e.DiscCount;

        // blokkszámok (24 bites BE + 16 bites BE)
        uint blocks = (uint)p.BlockCount;
        b[0x0392] = (byte)(blocks >> 16);
        b[0x0393] = (byte)(blocks >> 8);
        b[0x0394] = (byte)blocks;
        b[0x0395] = 0;
        b[0x0396] = 0;

        // adatrészek: darabszám kis-endiánként (sic), méret / 0x100 nagy-endiánként
        BitConverter.GetBytes((uint)p.PartCount).CopyTo(b, 0x03A0);
        BE32(b, 0x03A4, (uint)(partsTotalSize / 0x100));

        BE32(b, 0x0344, p.ContentType);
        Array.Copy(mhtHash, 0, b, 0x037D, 20);

        if (p.GameTitle != null)
        {
            Utf16Be(b, 0x0411, p.GameTitle);
            Utf16Be(b, 0x1691, p.GameTitle);
        }

        b[0x035B] = 0;
        b[0x035F] = 0;
        b[0x0391] = 0;
        var digest = SHA1.HashData(new ReadOnlySpan<byte>(b, 0x0344, 0xACBC));
        Array.Copy(digest, 0, b, 0x032C, 20);
        return b;
    }

    private static void BE32(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24);
        b[o + 1] = (byte)(v >> 16);
        b[o + 2] = (byte)(v >> 8);
        b[o + 3] = (byte)v;
    }

    private static void Utf16Be(byte[] b, int o, string s)
    {
        // legfeljebb 64 karakter fér el; utána nulla lezárás
        var chars = Encoding.BigEndianUnicode.GetBytes(s.Length > 63 ? s[..63] : s);
        Array.Copy(chars, 0, b, o, chars.Length);
        b[o + chars.Length] = 0;
        b[o + chars.Length + 1] = 0;
    }

    /// <summary>4096 bájtos SHA-1 lista (legfeljebb 204 kivonat).</summary>
    private sealed class HashList
    {
        public readonly byte[] Buffer = new byte[4096];
        private int _len;

        public void AddHash(byte[] hash)
        {
            Array.Copy(hash, 0, Buffer, _len, 20);
            _len += 20;
        }

        public byte[] Digest() => SHA1.HashData(Buffer);

        public void WriteTo(Stream s) => s.Write(Buffer, 0, Buffer.Length);

        public static HashList Read(string path)
        {
            var h = new HashList();
            using (var fs = File.OpenRead(path))
            {
                int total = 0;
                while (total < 4096)
                {
                    int r = fs.Read(h.Buffer, total, 4096 - total);
                    if (r == 0) break;
                    total += r;
                }
            }
            // a lista hossza: az első csupa nulla 20 bájtos bejegyzésig
            int len = 0;
            while (len + 20 <= 4096 && !h.Buffer.AsSpan(len, 20).SequenceEqual(new byte[20])) len += 20;
            h._len = len;
            return h;
        }

        public void Write(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Write);
            fs.Position = 0;
            WriteTo(fs);
        }
    }
}
