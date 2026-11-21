namespace DualDeckCommander;

internal enum OperationKind { Copy, Move, Delete, Recycle }

/// <summary>
/// Másolás / áthelyezés / törlés háttérszálon. A haladást mezőkben teszi közzé,
/// amelyeket a <see cref="ProgressForm"/> időzítővel olvas. Kérdéseknél
/// (felülírás, hiba) a UI szálon jelenít meg párbeszédablakot.
/// </summary>
internal sealed class FileOperations
{
    private const int BufferSize = 1 << 20; // 1 MB

    private enum OverwriteMode { Ask, All, SkipAll, Older }

    private readonly Control _ui;
    private readonly CancellationToken _ct;
    private readonly IntPtr _ownerHandle;
    private readonly byte[] _buffer = new byte[BufferSize];
    private OverwriteMode _overwriteMode = OverwriteMode.Ask;
    private bool _skipAllErrors;

    // ---- haladás (a UI szál olvassa) ---------------------------------------
    public volatile string CurrentSource = "";
    public volatile string CurrentTarget = "";
    public long FileBytesDone;
    public long FileBytesTotal;
    public long TotalBytesDone;
    public long TotalBytes;
    public int ItemsDone;
    public int ItemsTotal;
    public volatile bool Scanning;
    public readonly List<string> Errors = new();

    public FileOperations(Control ui, CancellationToken ct)
    {
        _ui = ui;
        _ct = ct;
        _ownerHandle = ui.Handle;
    }

    public void Run(OperationKind kind, IReadOnlyList<string> sources, string? targetDir, string? newName)
    {
        switch (kind)
        {
            case OperationKind.Copy:
                RunCopyMove(false, sources, targetDir!, newName);
                break;
            case OperationKind.Move:
                RunCopyMove(true, sources, targetDir!, newName);
                break;
            case OperationKind.Delete:
                RunDelete(sources);
                break;
            case OperationKind.Recycle:
                RunRecycle(sources);
                break;
        }
    }

    // =========================================================================
    //  Másolás / áthelyezés
    // =========================================================================

    private sealed record PlanItem(string Source, string Target, long Size, bool IsDirectory);

    private void RunCopyMove(bool move, IReadOnlyList<string> sources, string targetDir, string? newName)
    {
        Scanning = true;
        if (!Directory.Exists(targetDir))
        {
            if (!Try(() => Directory.CreateDirectory(targetDir), "Célmappa létrehozása: " + targetDir))
                return;
        }

        var plan = new List<PlanItem>();
        int directMoves = 0;

        foreach (var rawSrc in sources)
        {
            _ct.ThrowIfCancellationRequested();
            string src = rawSrc.TrimEnd('\\');
            bool isDir = Directory.Exists(src);
            if (!isDir && !File.Exists(src))
            {
                Errors.Add("Nem található: " + src);
                continue;
            }

            string name = newName ?? Path.GetFileName(src);
            string dst = Path.Combine(targetDir, name);

            if (string.Equals(Path.GetFullPath(src), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase))
            {
                if (move) continue;                 // önmagára áthelyezés: semmi teendő
                dst = MakeUnique(dst, isDir);        // önmagára másolás: "név (2)"
            }

            if (isDir && IsSubPath(dst, src))
            {
                Errors.Add("Mappa nem másolható önmagába: " + src);
                continue;
            }

            // Ugyanazon a köteten áthelyezés = gyors átnevezés
            if (move && SameVolume(src, targetDir) && !File.Exists(dst) && !Directory.Exists(dst))
            {
                CurrentSource = src;
                CurrentTarget = dst;
                try
                {
                    if (isDir) Directory.Move(src, dst);
                    else File.Move(src, dst);
                    directMoves++;
                    continue;
                }
                catch
                {
                    // nem sikerült (pl. zárolt fájl a mappában) -> másolás + törlés
                }
            }

            Scan(src, dst, isDir, plan, 0);
        }

        TotalBytes = plan.Where(p => !p.IsDirectory).Sum(p => p.Size);
        ItemsTotal = plan.Count(p => !p.IsDirectory) + directMoves;
        ItemsDone = directMoves;
        Scanning = false;

        var createdDirs = new List<PlanItem>();
        foreach (var item in plan)
        {
            _ct.ThrowIfCancellationRequested();
            if (item.IsDirectory)
            {
                CurrentSource = item.Source;
                CurrentTarget = item.Target;
                if (File.Exists(item.Target))
                {
                    Errors.Add("A célban azonos nevű fájl van, a mappa kimarad: " + item.Target);
                    continue;
                }
                if (Try(() => Directory.CreateDirectory(item.Target), "Mappa létrehozása: " + item.Target))
                    createdDirs.Add(item);
            }
            else
            {
                CopyOrMoveFile(item, move);
                ItemsDone++;
            }
        }

        // Mappa dátumok átvétele, áthelyezésnél üres forrásmappák törlése
        for (int i = createdDirs.Count - 1; i >= 0; i--)
        {
            var d = createdDirs[i];
            try { Directory.SetLastWriteTimeUtc(d.Target, Directory.GetLastWriteTimeUtc(d.Source)); } catch { }
            if (move)
            {
                try
                {
                    new DirectoryInfo(d.Source).Attributes = FileAttributes.Normal;
                    Directory.Delete(d.Source, false);
                }
                catch
                {
                    // nem üres (pl. kihagyott fájl maradt benne) – szándékosan megtartjuk
                }
            }
        }
    }

    private void Scan(string src, string dst, bool isDir, List<PlanItem> plan, int depth)
    {
        _ct.ThrowIfCancellationRequested();
        CurrentSource = src;
        if (!isDir)
        {
            long size = 0;
            if (Try(() => size = new FileInfo(src).Length, "Fájl olvasása: " + src))
                plan.Add(new PlanItem(src, dst, size, false));
            return;
        }

        plan.Add(new PlanItem(src, dst, 0, true));
        var di = new DirectoryInfo(src);

        // Beágyazott csomópontokat (junction/symlink) nem követünk, hogy elkerüljük a végtelen ciklust
        if (depth > 0 && (di.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            Errors.Add("Csatolási pont kihagyva (csak üres mappa jön létre): " + src);
            return;
        }

        FileSystemInfo[] children = Array.Empty<FileSystemInfo>();
        var opts = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false, RecurseSubdirectories = false };
        if (!Try(() => children = di.GetFileSystemInfos("*", opts), "Mappa olvasása: " + src))
            return;

        foreach (var c in children)
        {
            bool childIsDir = (c.Attributes & FileAttributes.Directory) != 0;
            string childDst = Path.Combine(dst, c.Name);
            if (childIsDir) Scan(c.FullName, childDst, true, plan, depth + 1);
            else plan.Add(new PlanItem(c.FullName, childDst, ((FileInfo)c).Length, false));
        }
    }

    private void CopyOrMoveFile(PlanItem item, bool move)
    {
        string src = item.Source;
        string dst = item.Target;
        long baseTotal = TotalBytesDone;
        CurrentSource = src;
        CurrentTarget = dst;
        FileBytesDone = 0;
        FileBytesTotal = item.Size;

        try
        {
            if (Directory.Exists(dst))
            {
                Errors.Add("A célban azonos nevű mappa van, a fájl kimarad: " + dst);
                return;
            }

            bool overwrite = false;
            if (File.Exists(dst))
            {
                var si = new FileInfo(src);
                switch (DecideOverwrite(src, si.Length, si.LastWriteTimeUtc, dst))
                {
                    case OverwriteChoice.Skip:
                        return;
                    case OverwriteChoice.Rename:
                        dst = MakeUnique(dst, false);
                        CurrentTarget = dst;
                        break;
                    default:
                        overwrite = true;
                        break;
                }
            }

            if (move && SameVolume(src, dst))
            {
                Try(() =>
                {
                    if (overwrite) ClearSpecialAttributes(dst);
                    File.Move(src, dst, overwrite);
                }, "Áthelyezés: " + src);
                return;
            }

            bool copied = Try(() =>
            {
                TotalBytesDone = baseTotal;
                FileBytesDone = 0;
                CopyFileWithProgress(src, dst);
            }, "Másolás: " + src);

            if (copied && move)
            {
                Try(() =>
                {
                    ClearSpecialAttributes(src);
                    File.Delete(src);
                }, "Forrás törlése: " + src);
            }
        }
        finally
        {
            TotalBytesDone = baseTotal + item.Size;
        }
    }

    private void CopyFileWithProgress(string src, string dst)
    {
        var srcInfo = new FileInfo(src);
        if (File.Exists(dst)) ClearSpecialAttributes(dst);

        bool completed = false;
        try
        {
            using (var input = new FileStream(src, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete, BufferSize, FileOptions.SequentialScan))
            using (var output = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize))
            {
                int read;
                while ((read = input.Read(_buffer, 0, _buffer.Length)) > 0)
                {
                    _ct.ThrowIfCancellationRequested();
                    output.Write(_buffer, 0, read);
                    FileBytesDone += read;
                    TotalBytesDone += read;
                }
            }

            File.SetCreationTimeUtc(dst, srcInfo.CreationTimeUtc);
            File.SetLastWriteTimeUtc(dst, srcInfo.LastWriteTimeUtc);
            const FileAttributes keep = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System |
                                        FileAttributes.Archive | FileAttributes.NotContentIndexed;
            var attrs = srcInfo.Attributes & keep;
            File.SetAttributes(dst, attrs == 0 ? FileAttributes.Normal : attrs);
            completed = true;
        }
        finally
        {
            if (!completed)
            {
                try { File.Delete(dst); } catch { /* félkész fájl */ }
            }
        }
    }

    private OverwriteChoice DecideOverwrite(string srcLabel, long srcSize, DateTime srcTimeUtc, string dst)
    {
        switch (_overwriteMode)
        {
            case OverwriteMode.All:
                return OverwriteChoice.Overwrite;
            case OverwriteMode.SkipAll:
                return OverwriteChoice.Skip;
            case OverwriteMode.Older:
                return File.GetLastWriteTimeUtc(dst) < srcTimeUtc
                    ? OverwriteChoice.Overwrite
                    : OverwriteChoice.Skip;
        }

        var choice = OnUi(() => Dialogs.AskOverwrite(_ui, srcLabel, srcSize, srcTimeUtc.ToLocalTime(), dst));
        switch (choice)
        {
            case OverwriteChoice.OverwriteAll:
                _overwriteMode = OverwriteMode.All;
                return OverwriteChoice.Overwrite;
            case OverwriteChoice.SkipAll:
                _overwriteMode = OverwriteMode.SkipAll;
                return OverwriteChoice.Skip;
            case OverwriteChoice.OverwriteOlder:
                _overwriteMode = OverwriteMode.Older;
                return DecideOverwrite(srcLabel, srcSize, srcTimeUtc, dst);
            case OverwriteChoice.Cancel:
                throw new OperationCanceledException();
            default:
                return choice;
        }
    }

    // =========================================================================
    //  Kibontás lemezképből / archívumból
    // =========================================================================

    public void RunExtract(IVirtualFs vfs, IReadOnlyList<VEntry> entries, string targetDir, string? newName)
    {
        Scanning = true;
        if (!Directory.Exists(targetDir))
        {
            if (!Try(() => Directory.CreateDirectory(targetDir), "Célmappa létrehozása: " + targetDir))
                return;
        }

        var dirs = new List<string>();
        var files = new List<VEntry>();
        var targets = new Dictionary<VEntry, string>();
        void Scan(VEntry e, string dst)
        {
            _ct.ThrowIfCancellationRequested();
            CurrentSource = vfs.FilePath + "\\" + e.Path;
            if (!e.IsDirectory)
            {
                files.Add(e);
                targets[e] = dst;
                return;
            }
            dirs.Add(dst);
            List<VEntry> children = new();
            if (!Try(() => children = vfs.GetChildren(e), "Mappa olvasása: " + e.Path)) return;
            foreach (var c in children) Scan(c, Path.Combine(dst, c.Name));
        }
        foreach (var e in entries) Scan(e, Path.Combine(targetDir, newName ?? e.Name));

        TotalBytes += files.Sum(f => f.Size);
        ItemsTotal += files.Count;
        Scanning = false;

        foreach (var d in dirs)
        {
            _ct.ThrowIfCancellationRequested();
            if (File.Exists(d)) Errors.Add("A célban azonos nevű fájl van, a mappa kimarad: " + d);
            else Try(() => Directory.CreateDirectory(d), "Mappa létrehozása: " + d);
        }

        foreach (var (entry, data) in vfs.ReadSequential(files, _ct))
        {
            using (data)
            {
                ExtractOne(vfs, entry, data, targets[entry]);
            }
        }
    }

    private void ExtractOne(IVirtualFs vfs, VEntry entry, Stream data, string target)
    {
        string label = vfs.FilePath + "\\" + entry.Path;
        long baseTotal = TotalBytesDone;
        CurrentSource = label;
        CurrentTarget = target;
        FileBytesDone = 0;
        FileBytesTotal = entry.Size;
        try
        {
            string dst = target;
            if (Directory.Exists(dst))
            {
                Errors.Add("A célban azonos nevű mappa van, a fájl kimarad: " + dst);
                return;
            }
            if (File.Exists(dst))
            {
                var time = entry.Modified == DateTime.MinValue ? DateTime.UtcNow : entry.Modified.ToUniversalTime();
                var choice = DecideOverwrite(label, entry.Size, time, dst);
                if (choice == OverwriteChoice.Skip) return;
                if (choice == OverwriteChoice.Rename) { dst = MakeUnique(dst, false); CurrentTarget = dst; }
            }

            bool first = true;
            Try(() =>
            {
                TotalBytesDone = baseTotal;
                FileBytesDone = 0;
                // újrapróbálásnál a már (részben) elolvasott adatfolyam helyett újat nyitunk
                Stream src = first ? data : vfs.OpenFile(entry);
                first = false;
                try { CopyStreamToFile(src, dst); }
                finally { if (src != data) src.Dispose(); }
                if (entry.Modified != DateTime.MinValue)
                {
                    try { File.SetLastWriteTime(dst, entry.Modified); } catch { }
                }
            }, "Kibontás: " + label);
        }
        finally
        {
            TotalBytesDone = baseTotal + entry.Size;
            ItemsDone++;
        }
    }

    /// <summary>Teljes archívumok kibontása (Alt+F9). Több archívumnál mindegyik saját almappába kerül.</summary>
    /// <summary>Xbox lemezképek átalakítása GoD formátumra (a tervek előre elkészültek).</summary>
    public void RunGod(IReadOnlyList<GodConverter.Plan> plans)
    {
        TotalBytes = plans.Sum(p => p.BytesToProcess);
        ItemsTotal = plans.Sum(p => p.PartCount);
        foreach (var plan in plans)
        {
            _ct.ThrowIfCancellationRequested();
            try
            {
                GodConverter.Run(plan, this, _ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Errors.Add($"{Path.GetFileName(plan.IsoPath)}: {ex.Message}");
            }
        }
    }

    public void RunExtractArchives(IReadOnlyList<string> archives, string targetDir, bool subfolders)
    {
        foreach (var path in archives)
        {
            _ct.ThrowIfCancellationRequested();
            CurrentSource = path;
            IVirtualFs? vfs = null;
            if (!Try(() =>
                {
                    vfs = VirtualFs.IsDiscImage(path)
                        ? XboxIso.TryOpen(path) ?? throw new InvalidDataException("Nem Xbox lemezkép – csak csatolni lehet.")
                        : VirtualFs.OpenArchive(path);
                }, "Megnyitás: " + path) || vfs == null)
                continue;

            using (vfs)
            {
                string dst = subfolders ? Path.Combine(targetDir, Path.GetFileNameWithoutExtension(path)) : targetDir;
                RunExtract(vfs, vfs.GetChildren(vfs.Root), dst, null);
            }
        }
    }

    private void CopyStreamToFile(Stream input, string dst)
    {
        if (File.Exists(dst)) ClearSpecialAttributes(dst);
        bool completed = false;
        try
        {
            using (var output = new FileStream(dst, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize))
            {
                int read;
                while ((read = input.Read(_buffer, 0, _buffer.Length)) > 0)
                {
                    _ct.ThrowIfCancellationRequested();
                    output.Write(_buffer, 0, read);
                    FileBytesDone += read;
                    TotalBytesDone += read;
                }
            }
            completed = true;
        }
        finally
        {
            if (!completed)
            {
                try { File.Delete(dst); } catch { /* félkész fájl */ }
            }
        }
    }

    // =========================================================================
    //  Törlés
    // =========================================================================

    private void RunDelete(IReadOnlyList<string> sources)
    {
        Scanning = true;
        var items = new List<(string Path, bool IsDir)>();
        foreach (var s in sources)
        {
            _ct.ThrowIfCancellationRequested();
            CollectForDelete(s.TrimEnd('\\'), items);
        }
        ItemsTotal = items.Count;
        Scanning = false;

        foreach (var (path, isDir) in items)
        {
            _ct.ThrowIfCancellationRequested();
            CurrentSource = path;
            Try(() =>
            {
                if (isDir)
                {
                    new DirectoryInfo(path).Attributes = FileAttributes.Normal;
                    Directory.Delete(path, false);
                }
                else
                {
                    ClearSpecialAttributes(path);
                    File.Delete(path);
                }
            }, "Törlés: " + path);
            ItemsDone++;
        }
    }

    /// <summary>Utólagos (post-order) bejárás: előbb a tartalom, aztán maga a mappa.</summary>
    private void CollectForDelete(string path, List<(string, bool)> items)
    {
        CurrentSource = path;
        if (!Directory.Exists(path))
        {
            if (File.Exists(path)) items.Add((path, false));
            return;
        }

        var di = new DirectoryInfo(path);
        if ((di.Attributes & FileAttributes.ReparsePoint) == 0)
        {
            FileSystemInfo[] children = Array.Empty<FileSystemInfo>();
            var opts = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false };
            Try(() => children = di.GetFileSystemInfos("*", opts), "Mappa olvasása: " + path);
            foreach (var c in children)
            {
                if ((c.Attributes & FileAttributes.Directory) != 0) CollectForDelete(c.FullName, items);
                else items.Add((c.FullName, false));
            }
        }
        // csatolási pontnál csak maga a link törlődik, a cél tartalma nem
        items.Add((path, true));
    }

    private void RunRecycle(IReadOnlyList<string> sources)
    {
        ItemsTotal = sources.Count;
        foreach (var s in sources)
        {
            _ct.ThrowIfCancellationRequested();
            CurrentSource = s;
            Try(() => NativeMethods.SendToRecycleBin(s, _ownerHandle), "Lomtárba helyezés: " + s);
            ItemsDone++;
        }
    }

    // =========================================================================
    //  Segédek
    // =========================================================================

    /// <summary>Hibakezelés: Újra / Kihagyás / Mindet kihagy / Mégse.</summary>
    private bool Try(Action action, string context)
    {
        while (true)
        {
            _ct.ThrowIfCancellationRequested();
            try
            {
                action();
                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                string msg = context + "\n\n" + ex.Message;
                if (_skipAllErrors)
                {
                    Errors.Add(msg.Replace("\n\n", " – "));
                    return false;
                }
                switch (OnUi(() => Dialogs.AskError(_ui, msg)))
                {
                    case ErrorChoice.Retry:
                        continue;
                    case ErrorChoice.SkipAll:
                        _skipAllErrors = true;
                        Errors.Add(msg.Replace("\n\n", " – "));
                        return false;
                    case ErrorChoice.Skip:
                        Errors.Add(msg.Replace("\n\n", " – "));
                        return false;
                    default:
                        throw new OperationCanceledException();
                }
            }
        }
    }

    private T OnUi<T>(Func<T> f) => (T)_ui.Invoke(f)!;

    private static void ClearSpecialAttributes(string path)
    {
        var a = File.GetAttributes(path);
        if ((a & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != 0)
            File.SetAttributes(path, a & ~(FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System));
    }

    public static string MakeUnique(string path, bool isDir)
    {
        string dir = Path.GetDirectoryName(path) ?? "";
        string name = isDir ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path);
        string ext = isDir ? "" : Path.GetExtension(path);
        for (int i = 2; ; i++)
        {
            string candidate = Path.Combine(dir, $"{name} ({i}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
        }
    }

    public static bool IsSubPath(string child, string parent)
    {
        string c = Path.GetFullPath(child).TrimEnd('\\') + "\\";
        string p = Path.GetFullPath(parent).TrimEnd('\\') + "\\";
        return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameVolume(string a, string b) =>
        string.Equals(Path.GetPathRoot(Path.GetFullPath(a)), Path.GetPathRoot(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);

    public static long GetDirectorySize(string path, CancellationToken ct)
    {
        long total = 0;
        var stack = new Stack<string>();
        stack.Push(path);
        var opts = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true };
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            string d = stack.Pop();
            try
            {
                foreach (var fsi in new DirectoryInfo(d).EnumerateFileSystemInfos("*", opts))
                {
                    if ((fsi.Attributes & FileAttributes.Directory) != 0)
                    {
                        if ((fsi.Attributes & FileAttributes.ReparsePoint) == 0) stack.Push(fsi.FullName);
                    }
                    else
                    {
                        total += ((FileInfo)fsi).Length;
                    }
                }
            }
            catch
            {
                // elérhetetlen mappa kihagyása
            }
        }
        return total;
    }
}
