namespace DualDeckCommander;

/// <summary>Egy sor a fájllistában.</summary>
internal sealed class FileEntry
{
    public string Name { get; init; } = "";
    public string FullPath { get; init; } = "";
    public bool IsDirectory { get; init; }
    public bool IsParent { get; init; }
    /// <summary>Bájtban; mappánál -1, amíg nincs kiszámolva.</summary>
    public long Size { get; set; } = -1;
    public DateTime Modified { get; init; }
    public FileAttributes Attributes { get; init; }
    public bool Marked { get; set; }

    /// <summary>Ha nem null, a bejegyzés egy lemezképen / archívumon belül van (csak olvasható).</summary>
    public VEntry? Inner { get; init; }
    public bool IsVirtual => Inner != null;
    /// <summary>Böngészhető tároló a lemezen (.iso, .zip, .rar, .7z, .lha, .lzh, .lzx).</summary>
    public bool IsBrowsable => !IsDirectory && !IsVirtual && VirtualFs.IsBrowsable(Name);
    public bool IsDiscImage => !IsDirectory && !IsVirtual && VirtualFs.IsDiscImage(Name);

    public bool IsHidden => (Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0;

    /// <summary>Kiterjesztés pont nélkül (".gitignore"-nál üres).</summary>
    public string Extension
    {
        get
        {
            if (IsDirectory) return "";
            int dot = Name.LastIndexOf('.');
            return dot > 0 ? Name[(dot + 1)..] : "";
        }
    }

    public string BaseName
    {
        get
        {
            if (IsDirectory) return Name;
            int dot = Name.LastIndexOf('.');
            return dot > 0 ? Name[..dot] : Name;
        }
    }

    public string AttributeText =>
        string.Concat(
            (Attributes & FileAttributes.ReadOnly) != 0 ? "r" : "-",
            (Attributes & FileAttributes.Archive) != 0 ? "a" : "-",
            (Attributes & FileAttributes.Hidden) != 0 ? "h" : "-",
            (Attributes & FileAttributes.System) != 0 ? "s" : "-");
}
