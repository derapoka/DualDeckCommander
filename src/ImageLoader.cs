using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DualDeckCommander;

/// <summary>
/// Képbetöltés a képnézegetőhöz: GDI+ (JPEG, PNG, BMP, GIF, TIFF, ICO), saját TGA-olvasó,
/// és a Windows képkodekjei (WIC) a WebP / HEIC / AVIF formátumokhoz.
/// </summary>
internal static class ImageLoader
{
    private static readonly HashSet<string> GdiExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".bmp", ".dib", ".gif", ".tif", ".tiff", ".ico" };

    private static readonly HashSet<string> WicExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".webp", ".heic", ".heif", ".avif", ".jxr", ".wdp" };

    public static bool IsImage(string name)
    {
        string ext = Path.GetExtension(name);
        return GdiExtensions.Contains(ext) || WicExtensions.Contains(ext) ||
               ext.Equals(".tga", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Kép betöltése. A <paramref name="realPath"/> a lemezen lévő fájl (archívumból null).</summary>
    public static Image Load(string name, Func<Stream> open, string? realPath)
    {
        string ext = Path.GetExtension(name);
        if (ext.Equals(".tga", StringComparison.OrdinalIgnoreCase))
        {
            using var s = open();
            return TgaDecoder.Decode(ReadAll(s));
        }
        if (WicExtensions.Contains(ext)) return LoadWithWic(open, realPath, ext);

        byte[] data;
        using (var s = open()) data = ReadAll(s);
        try
        {
            var img = Image.FromStream(new MemoryStream(data));
            ApplyExifOrientation(img);
            return img;
        }
        catch (Exception)
        {
            // pl. ritka JPEG / TIFF változatok: a Windows kodekjei hátha elbírják
            return LoadWithWic(() => new MemoryStream(data), realPath, ext);
        }
    }

    private static byte[] ReadAll(Stream s)
    {
        if (s is MemoryStream ms0 && ms0.TryGetBuffer(out var seg) && seg.Offset == 0 && seg.Count == ms0.Length)
            return seg.Array!;
        var ms = new MemoryStream(s.CanSeek ? (int)Math.Min(s.Length, int.MaxValue) : 0);
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>A fényképezőgépek / telefonok EXIF tájolását alkalmazza (álló képek).</summary>
    private static void ApplyExifOrientation(Image img)
    {
        const int OrientationId = 0x0112;
        try
        {
            if (Array.IndexOf(img.PropertyIdList, OrientationId) < 0) return;
            var prop = img.GetPropertyItem(OrientationId);
            if (prop?.Value == null || prop.Value.Length < 2) return;
            int o = BitConverter.ToUInt16(prop.Value, 0);
            RotateFlipType? r = o switch
            {
                2 => RotateFlipType.RotateNoneFlipX,
                3 => RotateFlipType.Rotate180FlipNone,
                4 => RotateFlipType.Rotate180FlipX,
                5 => RotateFlipType.Rotate90FlipX,
                6 => RotateFlipType.Rotate90FlipNone,
                7 => RotateFlipType.Rotate270FlipX,
                8 => RotateFlipType.Rotate270FlipNone,
                _ => null,
            };
            if (r != null)
            {
                img.RotateFlip(r.Value);
                img.RemovePropertyItem(OrientationId);
            }
        }
        catch
        {
            // nincs vagy hibás EXIF
        }
    }

    // =========================================================================
    //  WIC (Windows Imaging Component) – WebP, HEIC, AVIF, JPEG XR
    // =========================================================================

    private static Image LoadWithWic(Func<Stream> open, string? realPath, string ext)
    {
        string path = realPath ?? "";
        string? temp = null;
        if (realPath == null)
        {
            temp = Path.Combine(Path.GetTempPath(), "DualDeckCommander", "img" + Guid.NewGuid().ToString("N")[..8] + ext);
            Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
            using (var src = open())
            using (var dst = File.Create(temp))
                src.CopyTo(dst);
            path = temp;
        }
        try
        {
            return Wic.Decode(path);
        }
        catch (COMException ex) when ((uint)ex.HResult == 0x88982F50) // WINCODEC_ERR_COMPONENTNOTFOUND
        {
            throw new NotSupportedException(
                $"A Windowson nincs telepítve {ext.TrimStart('.').ToUpperInvariant()} képkodek.\n" +
                "A Microsoft Store-ból ingyen telepíthető (pl. „WebP Image Extensions”, „HEIF Image Extensions”).");
        }
        finally
        {
            if (temp != null)
            {
                try { File.Delete(temp); } catch { }
            }
        }
    }

    private static class Wic
    {
        private static readonly Guid ClsidFactory = new("cacaf262-9370-4615-a13b-9f5539da4c0a");
        private static Guid _bgra = new("6fddc324-4e03-4bfe-b185-3d77768dc90f"); // 32bppBGRA

        public static Image Decode(string path)
        {
            var type = Type.GetTypeFromCLSID(ClsidFactory) ?? throw new NotSupportedException("WIC nem érhető el.");
            var factory = (IWICImagingFactory)Activator.CreateInstance(type)!;
            IWICBitmapDecoder? decoder = null;
            IWICBitmapFrameDecode? frame = null;
            IWICFormatConverter? conv = null;
            try
            {
                decoder = factory.CreateDecoderFromFilename(path, IntPtr.Zero, 0x80000000 /*GENERIC_READ*/, 0);
                frame = decoder.GetFrame(0);
                conv = factory.CreateFormatConverter();
                conv.Initialize(frame, ref _bgra, 0, IntPtr.Zero, 0.0, 0);
                conv.GetSize(out uint w, out uint h);
                var bmp = new Bitmap((int)w, (int)h, PixelFormat.Format32bppArgb);
                var data = bmp.LockBits(new Rectangle(0, 0, (int)w, (int)h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    conv.CopyPixels(IntPtr.Zero, (uint)data.Stride, (uint)(data.Stride * (int)h), data.Scan0);
                }
                finally
                {
                    bmp.UnlockBits(data);
                }
                return bmp;
            }
            finally
            {
                if (conv != null) Marshal.ReleaseComObject(conv);
                if (frame != null) Marshal.ReleaseComObject(frame);
                if (decoder != null) Marshal.ReleaseComObject(decoder);
                Marshal.ReleaseComObject(factory);
            }
        }

        // A COM interfészeknél a metódusok sorrendje a vtable sorrendje – nem szabad átrendezni!
        [ComImport, Guid("ec5ec8a9-c395-4314-9c77-54d7a935ff70"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IWICImagingFactory
        {
            IWICBitmapDecoder CreateDecoderFromFilename([MarshalAs(UnmanagedType.LPWStr)] string filename,
                IntPtr vendor, uint access, int options);
            void CreateDecoderFromStream();
            void CreateDecoderFromFileHandle();
            void CreateComponentInfo();
            void CreateDecoder();
            void CreateEncoder();
            void CreatePalette();
            IWICFormatConverter CreateFormatConverter();
        }

        [ComImport, Guid("9edde9e7-8dee-47ea-99df-e6faf2ed44bf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IWICBitmapDecoder
        {
            void QueryCapability();
            void Initialize();
            void GetContainerFormat();
            void GetDecoderInfo();
            void CopyPalette();
            void GetMetadataQueryReader();
            void GetPreview();
            void GetColorContexts();
            void GetThumbnail();
            uint GetFrameCount();
            IWICBitmapFrameDecode GetFrame(uint index);
        }

        [ComImport, Guid("3b16811b-6a43-4ec9-a813-3d930c13b940"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IWICBitmapFrameDecode
        {
            void GetSize(out uint width, out uint height);
            void GetPixelFormat(out Guid format);
            void GetResolution(out double dpiX, out double dpiY);
            void CopyPalette(IntPtr palette);
            void CopyPixels(IntPtr rect, uint stride, uint bufferSize, IntPtr buffer);
        }

        [ComImport, Guid("00000301-a8f2-4877-ba0a-fd2b6645fb94"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IWICFormatConverter
        {
            void GetSize(out uint width, out uint height);
            void GetPixelFormat(out Guid format);
            void GetResolution(out double dpiX, out double dpiY);
            void CopyPalette(IntPtr palette);
            void CopyPixels(IntPtr rect, uint stride, uint bufferSize, IntPtr buffer);
            void Initialize(IWICBitmapFrameDecode source, ref Guid dstFormat, int dither, IntPtr palette,
                double alphaThreshold, int paletteTranslate);
        }
    }
}

/// <summary>Truevision TGA olvasó: színpalettás, true-color és szürke; tömörítetlen és RLE.</summary>
internal static class TgaDecoder
{
    /// <summary>Képpontok BGRA sorrendben, felülről lefelé.</summary>
    public static byte[] DecodePixels(byte[] d, out int width, out int height)
    {
        if (d.Length < 18) throw new InvalidDataException("Hibás TGA fájl.");
        int idLen = d[0], cmapType = d[1], type = d[2];
        int cmapFirst = d[3] | d[4] << 8, cmapLen = d[5] | d[6] << 8, cmapBits = d[7];
        width = d[12] | d[13] << 8;
        height = d[14] | d[15] << 8;
        int bpp = d[16], desc = d[17];
        bool topDown = (desc & 0x20) != 0;
        bool rightToLeft = (desc & 0x10) != 0;
        bool rle = type >= 9;
        int baseType = rle ? type - 8 : type;
        if (baseType is not (1 or 2 or 3) || width == 0 || height == 0 || width > 32768 || height > 32768)
            throw new InvalidDataException("Nem támogatott TGA változat.");

        int pos = 18 + idLen;
        // paletta
        var palette = Array.Empty<byte>();
        if (cmapType == 1)
        {
            int entryBytes = (cmapBits + 7) / 8;
            palette = new byte[(cmapFirst + cmapLen) * 4];
            for (int i = 0; i < cmapLen; i++)
            {
                ReadColor(d, pos + i * entryBytes, cmapBits, palette, (cmapFirst + i) * 4);
            }
            pos += cmapLen * entryBytes;
        }

        int pixBytes = (bpp + 7) / 8;
        int count = width * height;
        var raw = new byte[count * pixBytes];
        if (!rle)
        {
            if (pos + raw.Length > d.Length) throw new InvalidDataException("Csonka TGA fájl.");
            Array.Copy(d, pos, raw, 0, raw.Length);
        }
        else
        {
            int o = 0;
            while (o < raw.Length)
            {
                if (pos >= d.Length) throw new InvalidDataException("Csonka TGA fájl.");
                int h = d[pos++];
                int n = (h & 0x7F) + 1;
                if ((h & 0x80) != 0)
                {
                    for (int k = 0; k < n && o < raw.Length; k++)
                    {
                        Array.Copy(d, pos, raw, o, pixBytes);
                        o += pixBytes;
                    }
                    pos += pixBytes;
                }
                else
                {
                    int len = Math.Min(n * pixBytes, raw.Length - o);
                    Array.Copy(d, pos, raw, o, len);
                    o += len;
                    pos += n * pixBytes;
                }
            }
        }

        var outp = new byte[count * 4];
        for (int y = 0; y < height; y++)
        {
            int srcRow = topDown ? y : height - 1 - y;
            for (int x = 0; x < width; x++)
            {
                int srcX = rightToLeft ? width - 1 - x : x;
                int si = (srcRow * width + srcX) * pixBytes;
                int di = (y * width + x) * 4;
                switch (baseType)
                {
                    case 1:
                    {
                        int idx = pixBytes == 2 ? raw[si] | raw[si + 1] << 8 : raw[si];
                        if (idx * 4 + 3 < palette.Length) Array.Copy(palette, idx * 4, outp, di, 4);
                        break;
                    }
                    case 2:
                        ReadColor(raw, si, bpp, outp, di);
                        break;
                    case 3:
                        outp[di] = outp[di + 1] = outp[di + 2] = raw[si];
                        outp[di + 3] = pixBytes == 2 ? raw[si + 1] : (byte)255;
                        break;
                }
            }
        }
        // sok program 32 bites TGA-ba 0 alfát ír: ilyenkor a kép nem átlátszó, csak nincs alfa
        if (baseType == 2 && bpp == 32)
        {
            bool anyAlpha = false;
            for (int i = 3; i < outp.Length && !anyAlpha; i += 4) anyAlpha = outp[i] != 0;
            if (!anyAlpha) for (int i = 3; i < outp.Length; i += 4) outp[i] = 255;
        }
        return outp;
    }

    private static void ReadColor(byte[] s, int i, int bits, byte[] o, int di)
    {
        switch (bits)
        {
            case 15:
            case 16:
            {
                int v = s[i] | s[i + 1] << 8;
                o[di] = (byte)((v & 0x1F) * 255 / 31);
                o[di + 1] = (byte)(((v >> 5) & 0x1F) * 255 / 31);
                o[di + 2] = (byte)(((v >> 10) & 0x1F) * 255 / 31);
                o[di + 3] = 255;
                break;
            }
            case 24:
                o[di] = s[i];
                o[di + 1] = s[i + 1];
                o[di + 2] = s[i + 2];
                o[di + 3] = 255;
                break;
            case 32:
                o[di] = s[i];
                o[di + 1] = s[i + 1];
                o[di + 2] = s[i + 2];
                o[di + 3] = s[i + 3];
                break;
            default:
                throw new InvalidDataException("Nem támogatott TGA színmélység: " + bits);
        }
    }

    public static Image Decode(byte[] data)
    {
        var px = DecodePixels(data, out int w, out int h);
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var bd = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < h; y++)
                Marshal.Copy(px, y * w * 4, bd.Scan0 + y * bd.Stride, w * 4);
        }
        finally
        {
            bmp.UnlockBits(bd);
        }
        return bmp;
    }
}
