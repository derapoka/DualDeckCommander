using System.Text;

namespace DualDeckCommander;

/// <summary>
/// LHA / LZH archívum (Amiga LhA, LHarc, LHa for UNIX, Windows LHA).
/// Fejlécszintek: 0, 1, 2. Tömörítési módok: -lh0- -lhd- (tárolt / mappa),
/// -lh1- (dinamikus Huffman), -lh4- -lh5- -lh6- -lh7- (statikus Huffman + LZSS).
/// </summary>
internal sealed class LhaVfs : VirtualFsBase
{
    private sealed record Member(string Method, long DataOffset, long PackedSize, long Size, ushort Crc);

    public LhaVfs(string path) : base(path)
    {
        var tb = new TreeBuilder();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        long pos = FindFirstHeader(fs);
        if (pos < 0) throw new InvalidDataException("Nem LHA/LZH archívum.");

        var buf = new byte[65536 + 4];
        int count = 0;
        while (pos < fs.Length)
        {
            fs.Position = pos;
            int got = ReadFully(fs, buf, 24);
            if (got < 1 || buf[0] == 0) break;          // archívum vége
            if (got < 22) break;

            string method = Encoding.ASCII.GetString(buf, 2, 5);
            if (method[0] != '-' || method[4] != '-') break;
            int level = buf[20];
            long packed = BitConverter.ToUInt32(buf, 7);
            long size = BitConverter.ToUInt32(buf, 11);
            DateTime time;
            string name = "", dir = "";
            ushort crc;
            long dataStart;
            byte os = 0;

            if (level is 0 or 1)
            {
                int hsize = buf[0] + 2;
                fs.Position = pos;
                if (ReadFully(fs, buf, hsize) < hsize) break;
                int nameLen = buf[21];
                if (22 + nameLen + 2 > hsize) break;
                name = DecodeName(buf, 22, nameLen, 0);
                crc = BitConverter.ToUInt16(buf, 22 + nameLen);
                time = DosTime(BitConverter.ToUInt32(buf, 15));
                dataStart = pos + hsize;
                if (level == 1)
                {
                    if (24 + nameLen < hsize) os = buf[24 + nameLen];
                    int next = BitConverter.ToUInt16(buf, hsize - 2);
                    var ext = new byte[65536];
                    while (next > 0)
                    {
                        fs.Position = dataStart;
                        if (ReadFully(fs, ext, next) < next) break;
                        ParseExtended(ext, next, ref name, ref dir, ref time, os);
                        dataStart += next;
                        packed -= next; // 1-es szinten a tömörített méret tartalmazza a kiegészítő fejléceket
                        next = BitConverter.ToUInt16(ext, next - 2);
                    }
                }
            }
            else if (level == 2)
            {
                int hsize = BitConverter.ToUInt16(buf, 0);
                fs.Position = pos;
                if (hsize < 26 || ReadFully(fs, buf, hsize) < hsize) break;
                time = UnixTime(BitConverter.ToUInt32(buf, 15));
                crc = BitConverter.ToUInt16(buf, 21);
                os = buf[23];
                int next = BitConverter.ToUInt16(buf, 24);
                int off = 26;
                while (next > 0 && off + next <= hsize)
                {
                    var ext = new byte[next];
                    Array.Copy(buf, off, ext, 0, next);
                    ParseExtended(ext, next, ref name, ref dir, ref time, os);
                    off += next;
                    next = BitConverter.ToUInt16(ext, next - 2);
                }
                dataStart = pos + hsize;
            }
            else
            {
                throw new InvalidDataException($"Nem támogatott LHA fejlécszint: {level}");
            }

            string full = (dir + name).Replace('\\', '/');
            if (full.Length > 1 && full[1] == ':') full = full[2..];   // meghajtóbetű
            int colon = full.IndexOf(':');                              // Amiga kötet, pl. "Work:"
            if (colon >= 0) full = full[(colon + 1)..];

            if (method == "-lhd-")
                tb.GetDir(full, time);
            else if (full.Trim('/').Length > 0)
                tb.AddFile(full, size, time, new Member(method, dataStart, packed, size, crc), packed);

            count++;
            pos = dataStart + Math.Max(0, packed);
        }

        if (count == 0) throw new InvalidDataException("Üres vagy sérült LHA archívum.");
        Root = tb.Finish();
    }

    public override string KindName => "LHA archívum";

    public override Stream OpenFile(VEntry entry)
    {
        var m = (Member)entry.Data!;
        byte[] packed = new byte[m.PackedSize];
        using (var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            fs.Position = m.DataOffset;
            if (ReadFully(fs, packed, packed.Length) < packed.Length)
                throw new InvalidDataException("Csonka archívum: " + entry.Path);
        }

        byte[] data = m.Method switch
        {
            "-lh0-" or "-lz4-" => packed.Length == m.Size ? packed : throw new InvalidDataException("Hibás tárolt méret"),
            "-lh1-" => Lh1Decoder.Decode(packed, m.Size),
            "-lh4-" => LhNewDecoder.Decode(packed, m.Size, 12),
            "-lh5-" => LhNewDecoder.Decode(packed, m.Size, 13),
            "-lh6-" => LhNewDecoder.Decode(packed, m.Size, 15),
            "-lh7-" => LhNewDecoder.Decode(packed, m.Size, 16),
            _ => throw new NotSupportedException($"Nem támogatott LHA tömörítési mód: {m.Method}"),
        };

        if (Crc16.Compute(data) != m.Crc)
            throw new InvalidDataException("CRC hiba (sérült archívum): " + entry.Path);
        return new MemoryStream(data, false);
    }

    // ---- segédek -------------------------------------------------------------

    private static long FindFirstHeader(FileStream fs)
    {
        var head = new byte[(int)Math.Min(fs.Length, 256 * 1024)];
        fs.Position = 0;
        int n = ReadFully(fs, head, head.Length);
        for (int i = 0; i + 22 <= n; i++)
        {
            // "-l??-" a 2. bájttól; az önkicsomagoló (SFX) archívumoknál beljebb kezdődik
            if (head[i + 2] == '-' && head[i + 3] == 'l' && head[i + 6] == '-' && head[i + 20] <= 2 &&
                (head[i + 20] == 2 || head[i] >= 20))
                return i;
        }
        return -1;
    }

    private static void ParseExtended(byte[] ext, int size, ref string name, ref string dir, ref DateTime time, byte os)
    {
        if (size < 3) return;
        int type = ext[0];
        int len = size - 3;
        switch (type)
        {
            case 0x01:
                name = DecodeName(ext, 1, len, os);
                break;
            case 0x02:
                var raw = new byte[len];
                Array.Copy(ext, 1, raw, 0, len);
                for (int i = 0; i < raw.Length; i++) if (raw[i] == 0xFF) raw[i] = (byte)'/';
                dir = DecodeName(raw, 0, raw.Length, os);
                if (dir.Length > 0 && !dir.EndsWith('/')) dir += "/";
                break;
            case 0x54 when len >= 4:
                time = UnixTime(BitConverter.ToUInt32(ext, 1));
                break;
        }
    }

    private static string DecodeName(byte[] b, int offset, int count, byte os)
    {
        // Amiga LhA a fájlmegjegyzést (filenote) nullbájt után tárolja a névben
        int nul = Array.IndexOf(b, (byte)0, offset, count);
        if (nul >= 0) count = nul - offset;
        if (os != 'A') // Amiga: mindig Latin-1
        {
            try { return new UTF8Encoding(false, true).GetString(b, offset, count); }
            catch (DecoderFallbackException) { }
        }
        return Encoding.Latin1.GetString(b, offset, count);
    }

    private static DateTime DosTime(uint v)
    {
        try
        {
            int t = (int)(v & 0xFFFF), d = (int)(v >> 16);
            return new DateTime(1980 + (d >> 9), Math.Max(1, (d >> 5) & 15), Math.Max(1, d & 31),
                t >> 11, (t >> 5) & 63, Math.Min(59, (t & 31) * 2));
        }
        catch { return DateTime.MinValue; }
    }

    private static DateTime UnixTime(uint v) => DateTimeOffset.FromUnixTimeSeconds(v).LocalDateTime;

    internal static int ReadFully(Stream s, byte[] buf, int count)
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
}

/// <summary>CRC-16/ARC (LHA, ARC).</summary>
internal static class Crc16
{
    private static readonly ushort[] Table = Build();

    private static ushort[] Build()
    {
        var t = new ushort[256];
        for (int i = 0; i < 256; i++)
        {
            int c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? (c >> 1) ^ 0xA001 : c >> 1;
            t[i] = (ushort)c;
        }
        return t;
    }

    public static ushort Compute(byte[] data)
    {
        int crc = 0;
        foreach (byte b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return (ushort)crc;
    }
}

/// <summary>MSB-először bitolvasó (LHA).</summary>
internal sealed class MsbBitReader
{
    private readonly byte[] _data;
    private int _pos;
    private ulong _acc;
    private int _count;

    public MsbBitReader(byte[] data) { _data = data; }

    private void Ensure(int n)
    {
        while (_count < n)
        {
            _acc = (_acc << 8) | (_pos < _data.Length ? _data[_pos] : 0u);
            _pos++;
            _count += 8;
        }
    }

    public uint Peek(int n)
    {
        if (n == 0) return 0;
        Ensure(n);
        return (uint)(_acc >> (_count - n)) & ((1u << n) - 1);
    }

    public void Skip(int n)
    {
        Ensure(n);
        _count -= n;
    }

    public uint Get(int n)
    {
        uint v = Peek(n);
        _count -= n;
        return v;
    }

    /// <summary>Igaz, ha az olvasás már jócskán túlment a bemeneten (sérült adat).</summary>
    public bool Overrun => _pos > _data.Length + 8;
}

/// <summary>-lh4- ... -lh7- dekódoló (Okumura ar002 alapú LHA algoritmus).</summary>
internal sealed class LhNewDecoder
{
    private const int NC = 255 + 256 + 2 - 3; // 510 karakter/hossz kód
    private const int NT = 19;
    private const int TBIT = 5;
    private const int CBIT = 9;
    private const int NPT = 0x80;

    private readonly MsbBitReader _in;
    private readonly int _np;
    private readonly int _pbit;
    private readonly byte[] _cLen = new byte[NC];
    private readonly byte[] _ptLen = new byte[NPT];
    private readonly ushort[] _cTable = new ushort[4096];
    private readonly ushort[] _ptTable = new ushort[256];
    private readonly ushort[] _left = new ushort[2 * NC - 1];
    private readonly ushort[] _right = new ushort[2 * NC - 1];
    private int _blockSize;

    private LhNewDecoder(byte[] input, int dicbit)
    {
        _in = new MsbBitReader(input);
        // LHa for UNIX: lh4 és lh5 ugyanazt a pozíciókódolást használja
        _np = dicbit <= 13 ? 14 : dicbit + 1;
        _pbit = dicbit <= 13 ? 4 : 5;
    }

    public static byte[] Decode(byte[] input, long size, int dicbit)
    {
        var d = new LhNewDecoder(input, dicbit);
        var output = new byte[size];
        int dicSize = 1 << dicbit, mask = dicSize - 1;
        var window = new byte[dicSize];
        Array.Fill(window, (byte)' ');
        int r = 0;
        long count = 0;

        while (count < size)
        {
            if (d._in.Overrun) throw new InvalidDataException("Sérült tömörített adat.");
            int c = d.DecodeC();
            if (c <= 255)
            {
                output[count++] = (byte)c;
                window[r] = (byte)c;
                r = (r + 1) & mask;
            }
            else
            {
                int len = c - 253;
                int src = (r - d.DecodeP() - 1) & mask;
                for (int k = 0; k < len && count < size; k++)
                {
                    byte b = window[src];
                    src = (src + 1) & mask;
                    output[count++] = b;
                    window[r] = b;
                    r = (r + 1) & mask;
                }
            }
        }
        return output;
    }

    private int DecodeC()
    {
        if (_blockSize == 0)
        {
            _blockSize = (int)_in.Get(16);
            ReadPtLen(NT, TBIT, 3);
            ReadCLen();
            ReadPtLen(_np, _pbit, -1);
        }
        _blockSize--;
        uint bb = _in.Peek(16);
        int j = _cTable[bb >> 4];
        if (j >= NC)
        {
            uint mask = 1u << 3;
            do
            {
                j = (bb & mask) != 0 ? _right[j] : _left[j];
                mask >>= 1;
            } while (j >= NC && mask != 0);
            if (j >= NC) throw new InvalidDataException("Hibás Huffman-kód.");
        }
        _in.Skip(_cLen[j]);
        return j;
    }

    private int DecodeP()
    {
        uint bb = _in.Peek(16);
        int j = _ptTable[bb >> 8];
        if (j >= _np)
        {
            uint mask = 1u << 7;
            do
            {
                j = (bb & mask) != 0 ? _right[j] : _left[j];
                mask >>= 1;
            } while (j >= _np && mask != 0);
            if (j >= _np) throw new InvalidDataException("Hibás Huffman-kód.");
        }
        _in.Skip(_ptLen[j]);
        if (j != 0) j = (1 << (j - 1)) + (int)_in.Get(j - 1);
        return j;
    }

    private void ReadPtLen(int nn, int nbit, int iSpecial)
    {
        int n = (int)_in.Get(nbit);
        if (n == 0)
        {
            int c = (int)_in.Get(nbit);
            Array.Clear(_ptLen, 0, nn);
            Array.Fill(_ptTable, (ushort)c);
            return;
        }

        int i = 0;
        while (i < n && i < nn)
        {
            uint bb = _in.Peek(16);
            int c = (int)(bb >> 13);
            if (c == 7)
            {
                uint mask = 1u << 12;
                while ((mask & bb) != 0 && c < 16)
                {
                    mask >>= 1;
                    c++;
                }
            }
            _in.Skip(c < 7 ? 3 : c - 3);
            _ptLen[i++] = (byte)c;
            if (i == iSpecial)
            {
                int z = (int)_in.Get(2);
                while (--z >= 0 && i < nn) _ptLen[i++] = 0;
            }
        }
        while (i < nn) _ptLen[i++] = 0;
        MakeTable(nn, _ptLen, 8, _ptTable);
    }

    private void ReadCLen()
    {
        int n = (int)_in.Get(CBIT);
        if (n == 0)
        {
            int c = (int)_in.Get(CBIT);
            Array.Clear(_cLen);
            Array.Fill(_cTable, (ushort)c);
            return;
        }

        int i = 0;
        while (i < n && i < NC)
        {
            uint bb = _in.Peek(16);
            int c = _ptTable[bb >> 8];
            if (c >= NT)
            {
                uint mask = 1u << 7;
                do
                {
                    c = (bb & mask) != 0 ? _right[c] : _left[c];
                    mask >>= 1;
                } while (c >= NT && mask != 0);
                if (c >= NT) throw new InvalidDataException("Hibás Huffman-kód.");
            }
            _in.Skip(_ptLen[c]);
            if (c <= 2)
            {
                c = c == 0 ? 1 : c == 1 ? (int)_in.Get(4) + 3 : (int)_in.Get(CBIT) + 20;
                while (--c >= 0 && i < NC) _cLen[i++] = 0;
            }
            else
            {
                _cLen[i++] = (byte)(c - 2);
            }
        }
        while (i < NC) _cLen[i++] = 0;
        MakeTable(NC, _cLen, 12, _cTable);
    }

    private void MakeTable(int nchar, byte[] bitLen, int tableBits, ushort[] table)
    {
        var count = new int[17];
        var weight = new int[17];
        var start = new int[18];

        for (int i = 0; i < nchar; i++)
        {
            if (bitLen[i] > 16) throw new InvalidDataException("Hibás kódhossz.");
            count[bitLen[i]]++;
        }
        start[1] = 0;
        for (int i = 1; i <= 16; i++) start[i + 1] = start[i] + (count[i] << (16 - i));
        if (start[17] != 1 << 16) throw new InvalidDataException("Hibás Huffman-tábla.");

        int jutBits = 16 - tableBits;
        int k2;
        for (k2 = 1; k2 <= tableBits; k2++)
        {
            start[k2] >>= jutBits;
            weight[k2] = 1 << (tableBits - k2);
        }
        for (; k2 <= 16; k2++) weight[k2] = 1 << (16 - k2);

        int fill = start[tableBits + 1] >> jutBits;
        int tableSize = 1 << tableBits;
        while (fill < tableSize) table[fill++] = 0;

        int avail = nchar;
        int mask = 1 << (15 - tableBits);
        for (int ch = 0; ch < nchar; ch++)
        {
            int len = bitLen[ch];
            if (len == 0) continue;
            int nextCode = start[len] + weight[len];
            if (len <= tableBits)
            {
                for (int i = start[len]; i < nextCode; i++) table[i] = (ushort)ch;
            }
            else
            {
                int k = start[len];
                ref ushort p = ref table[k >> jutBits];
                int i = len - tableBits;
                while (i != 0)
                {
                    if (p == 0)
                    {
                        if (avail >= _left.Length) throw new InvalidDataException("Hibás Huffman-tábla.");
                        _right[avail] = _left[avail] = 0;
                        p = (ushort)avail++;
                    }
                    if ((k & mask) != 0) p = ref _right[p];
                    else p = ref _left[p];
                    k <<= 1;
                    i--;
                }
                p = (ushort)ch;
            }
            start[len] = nextCode;
        }
    }
}

/// <summary>-lh1- dekódoló: LZSS 4 KB ablakkal, adaptív (dinamikus) Huffman-kóddal (LZHUF / LHarc).</summary>
internal static class Lh1Decoder
{
    private const int N = 4096;
    private const int NChar = 314;          // 256 bájt + 58 hossz (3..60)
    private const int T = NChar * 2 - 1;    // fa mérete
    private const int R = T - 1;            // gyökér
    private const int MaxFreq = 0x8000;

    private static readonly byte[] PosIndex = new byte[256];
    private static readonly byte[] PosLen = BuildPosLen(PosIndex);

    /// <summary>A pozíció felső 6 bitjének rögzített kódja: 1×3, 3×4, 8×5, 12×6, 24×7, 16×8 bit.</summary>
    private static byte[] BuildPosLen(byte[] index)
    {
        int[] dist = { 1, 3, 8, 12, 24, 16 };
        var len = new byte[64];
        int code = 0, b = 0;
        for (int l = 0; l < dist.Length; l++)
        {
            int bits = l + 3;
            for (int k = 0; k < dist[l]; k++, code++)
            {
                len[code] = (byte)bits;
                int span = 1 << (8 - bits);
                for (int s = 0; s < span; s++) index[b++] = (byte)code;
            }
        }
        return len;
    }

    public static byte[] Decode(byte[] input, long size)
    {
        var bits = new MsbBitReader(input);
        var freq = new int[T + 1];
        var prnt = new int[T + NChar];
        var son = new int[T];

        // StartHuff
        for (int i = 0; i < NChar; i++)
        {
            freq[i] = 1;
            son[i] = i + T;
            prnt[i + T] = i;
        }
        for (int i = 0, j = NChar; j <= R; i += 2, j++)
        {
            freq[j] = freq[i] + freq[i + 1];
            son[j] = i;
            prnt[i] = prnt[i + 1] = j;
        }
        freq[T] = 0xFFFF;
        prnt[R] = 0;

        void Reconst()
        {
            int j = 0;
            for (int i = 0; i < T; i++)
            {
                if (son[i] >= T)
                {
                    freq[j] = (freq[i] + 1) / 2;
                    son[j] = son[i];
                    j++;
                }
            }
            for (int i = 0, jj = NChar; jj < T; i += 2, jj++)
            {
                int f = freq[jj] = freq[i] + freq[i + 1];
                int k = jj - 1;
                while (f < freq[k]) k--;
                k++;
                int l = jj - k;
                Array.Copy(freq, k, freq, k + 1, l);
                freq[k] = f;
                Array.Copy(son, k, son, k + 1, l);
                son[k] = i;
            }
            for (int i = 0; i < T; i++)
            {
                int k = son[i];
                if (k >= T) prnt[k] = i;
                else prnt[k] = prnt[k + 1] = i;
            }
        }

        void Update(int c)
        {
            if (freq[R] == MaxFreq) Reconst();
            c = prnt[c + T];
            do
            {
                int k = ++freq[c];
                int l = c + 1;
                if (k > freq[l])
                {
                    while (k > freq[++l]) { }
                    l--;
                    freq[c] = freq[l];
                    freq[l] = k;

                    int i = son[c];
                    prnt[i] = l;
                    if (i < T) prnt[i + 1] = l;

                    int j = son[l];
                    son[l] = i;

                    prnt[j] = c;
                    if (j < T) prnt[j + 1] = c;
                    son[c] = j;

                    c = l;
                }
            } while ((c = prnt[c]) != 0);
        }

        var output = new byte[size];
        var window = new byte[N];
        Array.Fill(window, (byte)' ');
        int r = 0;
        long count = 0;
        while (count < size)
        {
            if (bits.Overrun) throw new InvalidDataException("Sérült tömörített adat.");

            int c = son[R];
            while (c < T) c = son[c + (int)bits.Get(1)];
            c -= T;
            Update(c);

            if (c < 256)
            {
                output[count++] = (byte)c;
                window[r] = (byte)c;
                r = (r + 1) & (N - 1);
            }
            else
            {
                int len = c - 253;
                int idx = PosIndex[bits.Peek(8)];
                bits.Skip(PosLen[idx]);
                int pos = (idx << 6) | (int)bits.Get(6);
                int src = (r - pos - 1) & (N - 1);
                for (int k = 0; k < len && count < size; k++)
                {
                    byte b = window[src];
                    src = (src + 1) & (N - 1);
                    output[count++] = b;
                    window[r] = b;
                    r = (r + 1) & (N - 1);
                }
            }
        }
        return output;
    }
}
