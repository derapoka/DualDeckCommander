using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace DualDeckCommander;

/// <summary>ZIP archívum (beépített .NET támogatással).</summary>
internal sealed class ZipVfs : VirtualFsBase
{
    private readonly Encoding _nameEncoding;

    public ZipVfs(string path) : base(path)
    {
        // A régi (nem UTF-8 jelölésű) ZIP-ek a DOS kódlapot használják – magyar Windowson 852.
        int oem = CultureInfo.CurrentCulture.TextInfo.OEMCodePage;
        try { _nameEncoding = Encoding.GetEncoding(oem); }
        catch { _nameEncoding = Encoding.GetEncoding(437); }

        // Kétféle olvasat: UTF-8 (jelölt / modern ZIP) és DOS kódlap (régi ZIP); bejegyzésenként
        // az érvényes UTF-8 nevet választjuk, különben a DOS kódlapos nevet.
        var tb = new TreeBuilder();
        using (var fs1 = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var fs2 = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var utf = new ZipArchive(fs1, ZipArchiveMode.Read, false, Encoding.UTF8))
        using (var dos = new ZipArchive(fs2, ZipArchiveMode.Read, false, _nameEncoding))
        {
            for (int i = 0; i < utf.Entries.Count; i++)
            {
                var e = utf.Entries[i];
                bool useUtf = !e.FullName.Contains('\uFFFD');
                string name = useUtf ? e.FullName : dos.Entries[i].FullName;
                var time = e.LastWriteTime.LocalDateTime;
                if (name.EndsWith('/') || name.EndsWith('\\'))
                    tb.GetDir(name, time);
                else
                    tb.AddFile(name, e.Length, time, i, e.CompressedLength);
            }
        }
        Root = tb.Finish();
    }

    public override string KindName => "ZIP archívum";

    public override Stream OpenFile(VEntry entry)
    {
        var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
        try
        {
            var zip = new ZipArchive(fs, ZipArchiveMode.Read, false, Encoding.UTF8);
            int index = (int)entry.Data!;
            if (index >= zip.Entries.Count) throw new FileNotFoundException("Nincs az archívumban: " + entry.Path);
            var ze = zip.Entries[index];
            return new LengthStream(ze.Open(), entry.Size, zip, fs);
        }
        catch
        {
            fs.Dispose();
            throw;
        }
    }
}

