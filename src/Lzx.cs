using System.Text;

namespace DualDeckCommander;

/// <summary>
/// Amiga LZX archívum (Jonathan Forbes és Tomi Poutanen LZX programja, Aminet).
/// A "merged" csoportokban több fájl egyetlen tömörített folyamban van:
/// ilyenkor a csoportot egyszer bontjuk ki, és fájlonként szeleteljük.
/// A dekódoló az unlzx.c (Erik Meusel, Dan Fraser) működését követi.
/// </summary>
internal sealed class LzxVfs : VirtualFsBase
{
    private sealed class Group
    {
        public long Offset;
        public long PackedSize;
        public int PackMode;
        public long TotalSize;
    }

    private sealed record Member(Group Group, long OffsetInGroup, long Size, uint Crc);

    private readonly object _cacheLock = new();
    private Group? _cachedGroup;
    private byte[]? _cachedData;

    public LzxVfs(string path) : base(path)
    {
        var tb = new TreeBuilder();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var info = new byte[10];
        if (LhaVfs.ReadFully(fs, info, 10) < 10 || info[0] != 'L' || info[1] != 'Z' || info[2] != 'X')
            throw new InvalidDataException("Nem Amiga LZX archívum.");

        var hdr = new byte[31];
        var pending = new List<(string Name, long Size, DateTime Time, uint Crc)>();
        int count = 0;
        while (true)
        {
            int got = LhaVfs.ReadFully(fs, hdr, 31);
            if (got == 0) break;
            if (got < 31) throw new InvalidDataException("Csonka LZX fejléc.");

            long size = BitConverter.ToUInt32(hdr, 2);
            long packed = BitConverter.ToUInt32(hdr, 6);
            int packMode = hdr[11];
            bool merged = (hdr[12] & 1) != 0;
            int commentLen = hdr[14];
            uint dataCrc = BitConverter.ToUInt32(hdr, 22);
            uint headerCrc = BitConverter.ToUInt32(hdr, 26);
            int nameLen = hdr[30];

            var nameBytes = new byte[nameLen];
            var comment = new byte[commentLen];
            if (LhaVfs.ReadFully(fs, nameBytes, nameLen) < nameLen || LhaVfs.ReadFully(fs, comment, commentLen) < commentLen)
                throw new InvalidDataException("Csonka LZX fejléc.");

            var crc = new Crc32();
            hdr[26] = hdr[27] = hdr[28] = hdr[29] = 0;
            crc.Update(hdr, 0, 31);
            crc.Update(nameBytes, 0, nameLen);
            crc.Update(comment, 0, commentLen);
            if (crc.Value != headerCrc) throw new InvalidDataException("LZX fejléc CRC hiba.");

            string name = Encoding.Latin1.GetString(nameBytes);
            pending.Add((name, size, AmigaDate(hdr, 18), dataCrc));
            count++;

            // A csoport utolsó tagjánál (vagy önálló fájlnál) jön a tömörített adat
            if (!merged || packed != 0)
            {
                var group = new Group
                {
                    Offset = fs.Position,
                    PackedSize = packed,
                    PackMode = packMode,
                    TotalSize = pending.Sum(p => p.Size),
                };
                long off = 0;
                foreach (var p in pending)
                {
                    // csak az utolsó (vagy önálló) tag mutatja a valódi tömörített méretet
                    tb.AddFile(p.Name, p.Size, p.Time, new Member(group, off, p.Size, p.Crc),
                        pending.Count == 1 ? packed : -1);
                    off += p.Size;
                }
                pending.Clear();
                fs.Position += packed;
                if (fs.Position > fs.Length) throw new InvalidDataException("Csonka LZX archívum.");
            }
        }
        if (pending.Count > 0) throw new InvalidDataException("Csonka LZX archívum (befejezetlen csoport).");
        if (count == 0) throw new InvalidDataException("Üres LZX archívum.");
        Root = tb.Finish();
    }

    public override string KindName => "Amiga LZX archívum";

    public override Stream OpenFile(VEntry entry)
    {
        var m = (Member)entry.Data!;
        byte[] data = GetGroupData(m.Group);
        if (m.OffsetInGroup + m.Size > data.Length) throw new InvalidDataException("Hibás LZX csoport: " + entry.Path);

        var crc = new Crc32();
        crc.Update(data, (int)m.OffsetInGroup, (int)m.Size);
        if (crc.Value != m.Crc) throw new InvalidDataException("CRC hiba (sérült archívum): " + entry.Path);
        return new MemoryStream(data, (int)m.OffsetInGroup, (int)m.Size, false);
    }

    private byte[] GetGroupData(Group g)
    {
        lock (_cacheLock)
        {
            if (_cachedGroup == g && _cachedData != null) return _cachedData;

            var packed = new byte[g.PackedSize];
            using (var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                fs.Position = g.Offset;
                if (LhaVfs.ReadFully(fs, packed, packed.Length) < packed.Length)
                    throw new InvalidDataException("Csonka LZX archívum.");
            }

            byte[] data = g.PackMode switch
            {
                0 => packed,
                2 => LzxDecoder.Decode(packed, (int)g.TotalSize),
                _ => throw new NotSupportedException($"Nem támogatott LZX tömörítési mód: {g.PackMode}"),
            };
            _cachedGroup = g;
            _cachedData = data;
            return data;
        }
    }

    /// <summary>Amiga LZX dátum: nap(5) hónap(4) év(6) óra(5) perc(6) mp(6), nagy-endián.</summary>
    private static DateTime AmigaDate(byte[] h, int o)
    {
        uint t = (uint)(h[o] << 24 | h[o + 1] << 16 | h[o + 2] << 8 | h[o + 3]);
        int yf = (int)((t >> 17) & 63);
        int year = yf <= 7 ? yf + 2034 : yf <= 29 ? yf + 1970 : yf <= 57 ? yf + 1976 : yf + 1942;
        try
        {
            return new DateTime(year, (int)((t >> 23) & 15) + 1, Math.Max(1, (int)((t >> 27) & 31)),
                (int)((t >> 12) & 31), (int)((t >> 6) & 63), (int)(t & 63));
        }
        catch
        {
            return DateTime.MinValue;
        }
    }
}

/// <summary>CRC-32 (ZIP / LZX).</summary>
internal sealed class Crc32
{
    private static readonly uint[] Table = Build();
    private uint _crc = 0xFFFFFFFF;

    private static uint[] Build()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    public void Update(byte[] buf, int offset, int count)
    {
        uint c = _crc;
        for (int i = offset; i < offset + count; i++) c = Table[(c ^ buf[i]) & 0xFF] ^ (c >> 8);
        _crc = c;
    }

    public uint Value => ~_crc;
}

/// <summary>Amiga LZX tömörített folyam dekódolása (64 KB ablak, blokktípus 1–3).</summary>
internal sealed class LzxDecoder
{
    private const int WindowSize = 0x10000;
    private const int WindowMask = WindowSize - 1;
    private const int MainSymbols = 768;
    private const int LiteralSymbols = 256;
    private const int AlignedSymbols = 8;
    private const int PretreeSymbols = 20;
    private const int MainTableBits = 12;
    private const int AlignedTableBits = 7;
    private const int PretreeTableBits = 6;

    private static readonly int[] TableOne =
    {
        0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14,
    };

    private static readonly int[] TableTwo =
    {
        0, 1, 2, 3, 4, 6, 8, 12, 16, 24, 32, 48, 64, 96, 128, 192, 256, 384, 512, 768, 1024, 1536,
        2048, 3072, 4096, 6144, 8192, 12288, 16384, 24576, 32768, 49152,
    };

    // ---- bitolvasó: 16 bites nagy-endián szavak, LSB-először fogyasztva ----
    private readonly byte[] _in;
    private int _inPos;
    private uint _control;
    private int _bitCount;

    private readonly byte[] _literalLen = new byte[MainSymbols];
    private readonly byte[] _offsetLen = new byte[AlignedSymbols];
    private ushort[] _literalTable = Array.Empty<ushort>();
    private ushort[] _offsetTable = Array.Empty<ushort>();
    private int _method;
    private uint _blockRemaining;
    private uint _lastOffset = 1;
    private bool _primed;

    private LzxDecoder(byte[] input) { _in = input; }

    public static byte[] Decode(byte[] input, int expected)
    {
        var d = new LzxDecoder(input);
        var output = new byte[expected];
        var window = new byte[WindowSize];
        int wpos = 0;
        int count = 0;
        int matchRemaining = 0;

        while (count < expected)
        {
            if (d._blockRemaining == 0)
            {
                d.ReadBlockHeader();
                if (d._blockRemaining == 0) throw new InvalidDataException("Csonka LZX adat.");
            }

            if (matchRemaining > 0)
            {
                int n = Math.Min(matchRemaining, expected - count);
                for (int i = 0; i < n; i++)
                {
                    byte b = window[(wpos - (int)d._lastOffset) & WindowMask];
                    output[count++] = b;
                    window[wpos] = b;
                    wpos = (wpos + 1) & WindowMask;
                }
                matchRemaining -= n;
                d._blockRemaining -= (uint)n;
                continue;
            }

            int symbol = d.DecodeSymbol(d._literalTable, d._literalLen, MainSymbols, MainTableBits);
            if (symbol < LiteralSymbols)
            {
                output[count++] = (byte)symbol;
                window[wpos] = (byte)symbol;
                wpos = (wpos + 1) & WindowMask;
                d._blockRemaining--;
                continue;
            }

            int m = symbol - LiteralSymbols;
            int positionSlot = m & 31;
            int lengthSlot = (m >> 5) & 15;

            uint distance = (uint)TableTwo[positionSlot];
            int pbits = TableOne[positionSlot];
            if (pbits >= 3 && d._method == 3)
            {
                distance += d.ReadBits(pbits - 3) << 3;
                distance += (uint)d.DecodeSymbol(d._offsetTable, d._offsetLen, AlignedSymbols, AlignedTableBits);
            }
            else if (pbits > 0)
            {
                distance += d.ReadBits(pbits);
            }

            uint dist = distance == 0 ? d._lastOffset : distance;
            if (dist == 0) throw new InvalidDataException("Hibás LZX adat (nulla távolság).");
            d._lastOffset = dist;

            int lbits = TableOne[lengthSlot];
            int length = TableTwo[lengthSlot] + 3;
            if (lbits > 0) length += (int)d.ReadBits(lbits);
            if (length > d._blockRemaining) throw new InvalidDataException("Hibás LZX adat (túl hosszú egyezés).");
            matchRemaining = length;
        }
        return output;
    }

    private void Refill()
    {
        if (_inPos + 1 < _in.Length)
        {
            _control |= (uint)(_in[_inPos] << 8 | _in[_inPos + 1]) << _bitCount;
            _bitCount += 16;
            _inPos += 2;
        }
        else
        {
            _inPos = _in.Length;
        }
    }

    private void Ensure(int n)
    {
        while (_bitCount < n)
        {
            int before = _bitCount;
            Refill();
            if (_bitCount == before) break; // vége
        }
    }

    private uint PeekBits(int n, bool allowPadding)
    {
        if (n == 0) return 0;
        Ensure(n);
        if (_bitCount < n && (!allowPadding || _bitCount == 0)) throw new InvalidDataException("Csonka LZX adat.");
        return _control & ((1u << n) - 1);
    }

    private void Consume(int n)
    {
        if (n == 0) return;
        Ensure(n);
        if (_bitCount < n) throw new InvalidDataException("Csonka LZX adat.");
        _control >>= n;
        _bitCount -= n;
    }

    private uint ReadBits(int n)
    {
        uint v = PeekBits(n, false);
        Consume(n);
        return v;
    }

    private void ReadBlockHeader()
    {
        int method = (int)ReadBits(3);
        if (method is 0 or > 3) throw new InvalidDataException("Ismeretlen LZX blokktípus.");
        if (method == 1 && !_primed) throw new InvalidDataException("Hibás LZX adat.");
        _method = method;

        if (method == 3)
        {
            for (int i = 0; i < AlignedSymbols; i++) _offsetLen[i] = (byte)ReadBits(3);
            _offsetTable = MakeDecodeTable(AlignedSymbols, AlignedTableBits, _offsetLen);
        }

        uint len = ReadBits(8) << 16;
        len |= ReadBits(8) << 8;
        len |= ReadBits(8);
        _blockRemaining = len;

        if (method != 1)
        {
            DecodeSection(true, 0, LiteralSymbols);
            DecodeSection(false, LiteralSymbols, MainSymbols - LiteralSymbols);
            _literalTable = MakeDecodeTable(MainSymbols, MainTableBits, _literalLen);
        }
        _primed = true;
    }

    /// <summary>Kódhosszak delta-kódolt beolvasása egy 20 szimbólumos előfa segítségével.</summary>
    private void DecodeSection(bool literal, int start, int n)
    {
        var preLen = new byte[PretreeSymbols];
        for (int i = 0; i < PretreeSymbols; i++) preLen[i] = (byte)ReadBits(4);
        var preTable = MakeDecodeTable(PretreeSymbols, PretreeTableBits, preLen);

        int b17 = literal ? 4 : 3, b18 = literal ? 20 : 19, x18 = literal ? 5 : 6, b19 = literal ? 4 : 3;
        int i2 = 0;
        while (i2 < n)
        {
            int sym = DecodeSymbol(preTable, preLen, PretreeSymbols, PretreeTableBits);
            switch (sym)
            {
                case <= 16:
                    _literalLen[start + i2] = Delta(_literalLen[start + i2], sym);
                    i2++;
                    break;
                case 17:
                case 18:
                {
                    int run = sym == 17 ? b17 + (int)ReadBits(4) : b18 + (int)ReadBits(x18);
                    int end = Math.Min(i2 + run, n);
                    for (; i2 < end; i2++) _literalLen[start + i2] = 0;
                    break;
                }
                case 19:
                {
                    int run = b19 + (int)ReadBits(1);
                    int d = DecodeSymbol(preTable, preLen, PretreeSymbols, PretreeTableBits);
                    if (d > 16) throw new InvalidDataException("Hibás LZX előfa.");
                    byte v = Delta(_literalLen[start + i2], d);
                    int end = Math.Min(i2 + run, n);
                    for (; i2 < end; i2++) _literalLen[start + i2] = v;
                    break;
                }
                default:
                    throw new InvalidDataException("Hibás LZX előfa.");
            }
        }
    }

    private static byte Delta(byte prev, int sym) => (byte)(((prev - sym) % 17 + 17) % 17);

    private int DecodeSymbol(ushort[] table, byte[] lengths, int numSymbols, int tableBits)
    {
        uint root = PeekBits(tableBits, true);
        int sym = table[root];
        if (sym < numSymbols)
        {
            Consume(lengths[sym]);
            return sym;
        }
        Consume(tableBits);
        while (sym >= numSymbols)
        {
            uint bit = ReadBits(1);
            sym = table[(sym << 1) | (int)bit];
        }
        return sym;
    }

    /// <summary>Az unlzx.c make_decode_table függvényével azonos tábla (rövid kódok közvetlenül, hosszúak fában).</summary>
    private static ushort[] MakeDecodeTable(int numSymbols, int tableSize, byte[] lengths)
    {
        int tableMask = 1 << tableSize;
        var table = new ushort[tableMask + 4 * numSymbols];
        int pos = 0;
        int bitMask = tableMask >> 1;

        for (int bitNum = 1; bitNum <= tableSize; bitNum++)
        {
            for (int symbol = 0; symbol < numSymbols; symbol++)
            {
                if (lengths[symbol] != bitNum) continue;
                int reverse = pos, leaf = 0;
                for (int k = 0; k < tableSize; k++)
                {
                    leaf = (leaf << 1) | (reverse & 1);
                    reverse >>= 1;
                }
                pos += bitMask;
                if (pos > tableMask) throw new InvalidDataException("Hibás LZX Huffman-tábla.");
                int step = 1 << bitNum;
                for (int k = 0; k < bitMask; k++)
                {
                    table[leaf] = (ushort)symbol;
                    leaf += step;
                }
            }
            bitMask >>= 1;
        }

        if (pos == tableMask) return table;

        for (int symbol = pos; symbol < tableMask; symbol++)
        {
            int reverse = symbol, leaf = 0;
            for (int k = 0; k < tableSize; k++)
            {
                leaf = (leaf << 1) | (reverse & 1);
                reverse >>= 1;
            }
            table[leaf] = 0;
        }

        int nextSecondary = tableMask >> 1;
        long lpos = (long)pos << 16;
        long maskShifted = (long)tableMask << 16;
        int bm = 32768;
        for (int bitNum = tableSize + 1; bitNum <= 16; bitNum++)
        {
            for (int symbol = 0; symbol < numSymbols; symbol++)
            {
                if (lengths[symbol] != bitNum) continue;
                int reverse = (int)(lpos >> 16), leaf = 0;
                for (int k = 0; k < tableSize; k++)
                {
                    leaf = (leaf << 1) | (reverse & 1);
                    reverse >>= 1;
                }
                for (int fill = 0; fill < bitNum - tableSize; fill++)
                {
                    if (table[leaf] == 0)
                    {
                        int pair = nextSecondary << 1;
                        if (pair + 1 >= table.Length) throw new InvalidDataException("Hibás LZX Huffman-tábla.");
                        table[pair] = 0;
                        table[pair + 1] = 0;
                        table[leaf] = (ushort)nextSecondary++;
                    }
                    leaf = table[leaf] << 1;
                    leaf += (int)((lpos >> (15 - fill)) & 1);
                }
                table[leaf] = (ushort)symbol;
                lpos += bm;
                if (lpos > maskShifted) throw new InvalidDataException("Hibás LZX Huffman-tábla.");
            }
            bm >>= 1;
        }
        if (lpos != maskShifted) throw new InvalidDataException("Hibás LZX Huffman-tábla.");
        return table;
    }
}
