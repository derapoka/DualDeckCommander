using System.Collections.Specialized;
using System.Diagnostics;
using System.IO.Enumeration;

namespace DualDeckCommander;

/// <summary>
/// Egy fájlpanel: meghajtógombok, útvonal-fejléc, fájllista és állapotsor.
/// Valódi mappákat, valamint lemezképeket és archívumokat (csak olvasható) is megjelenít.
/// </summary>
internal sealed class FilePanel : UserControl, IThemed
{
    // ---- Események a főablak felé -----------------------------------------
    public event EventHandler? PanelActivated;
    public event Action<string>? CommandRequested;             // "copy", "move", "isoask", "vopen", ...
    public event Action<char>? CharTyped;                      // gépelés -> parancssor
    public event Action<string[], string, bool>? FilesDropped; // forrás, célmappa, áthelyezés?
    public event EventHandler? PathChanged;

    // ---- Vezérlők -------------------------------------------------------------
    private readonly ToolStrip _driveBar;
    private readonly ToolStripLabel _freeSpaceLabel;
    private readonly ToolStripButton _upButton;
    private readonly ToolStripButton _rootButton;
    private readonly Label _header;
    private readonly TabBar _tabBar;
    private readonly PanelListView _list;
    private readonly Label _status;
    private readonly IconCache _icons;
    private readonly ImageList _rowSizer;
    private readonly ContextMenuStrip _contextMenu;
    private readonly System.Windows.Forms.Timer _reloadTimer;
    private readonly System.Windows.Forms.Timer _longPressTimer;
    private FileSystemWatcher? _watcher;

    // ---- Állapot --------------------------------------------------------------
    private List<FileEntry> _entries = new();
    private readonly Stack<string> _history = new();
    private bool _isActive;
    private int _sortColumn;
    private bool _sortDescending;
    private int _anchorIndex = -1;
    private IVirtualFs? _vfs;
    private VEntry? _vdir;
    private Theme _theme = Theme.Current;
    private bool _fittingColumns;
    private readonly List<string> _tabs = new() { "" };
    private int _tabIndex;

    // jobb egérgombos jelölés
    private bool _rightDown;
    private bool _rightMoved;
    private int _rightStart = -1;
    private int _rightLast = -1;
    private bool _rightState;

    public string CurrentPath { get; private set; } = "";
    public bool ShowHidden { get; set; }
    public int SortColumn => _sortColumn;
    public bool SortDescending => _sortDescending;
    public IReadOnlyList<FileEntry> Entries => _entries;

    /// <summary>Igaz, ha a panel egy lemezkép vagy archívum belsejét mutatja.</summary>
    public bool InArchive => _vfs != null;
    public IVirtualFs? Archive => _vfs;

    /// <summary>A lemez fájlrendszerén lévő mappa (archívumnál az azt tartalmazó mappa).</summary>
    public string RealDirectory => _vfs != null ? Path.GetDirectoryName(_vfs.FilePath) ?? CurrentPath : CurrentPath;

    private static readonly string[] ColumnTitles = { "Név", "Kit.", "Méret", "Dátum", "Attr." };

    private static int S(int v) => UiScale.S(v);

    public FilePanel()
    {
        Font = new Font("Segoe UI", 9F);
        _icons = new IconCache(S(16));
        _rowSizer = new ImageList { ImageSize = new Size(1, S(22)) }; // sormagasság

        // Fájllista
        _list = new PanelListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            VirtualMode = true,
            OwnerDraw = true,
            BorderStyle = BorderStyle.None,
            HeaderStyle = ColumnHeaderStyle.Clickable,
            SmallImageList = _rowSizer,
            AllowDrop = true,
        };
        _list.Columns.Add(ColumnTitles[0], S(250));
        _list.Columns.Add(ColumnTitles[1], S(52));
        _list.Columns.Add(ColumnTitles[2], S(95), HorizontalAlignment.Right);
        _list.Columns.Add(ColumnTitles[3], S(118));
        _list.Columns.Add(ColumnTitles[4], S(48));

        _list.RetrieveVirtualItem += List_RetrieveVirtualItem;
        _list.DrawColumnHeader += List_DrawColumnHeader;
        _list.DrawItem += (_, _) => { };
        _list.DrawSubItem += List_DrawSubItem;
        _list.ColumnClick += List_ColumnClick;
        _list.KeyDown += List_KeyDown;
        _list.KeyPress += List_KeyPress;
        _list.MouseDown += List_MouseDown;
        _list.MouseDoubleClick += List_MouseDoubleClick;
        _list.GotFocus += (_, _) => PanelActivated?.Invoke(this, EventArgs.Empty);
        _list.SelectedIndexChanged += (_, _) => UpdateStatus();
        _list.ItemDrag += List_ItemDrag;
        _list.DragEnter += List_DragOver;
        _list.DragOver += List_DragOver;
        _list.DragDrop += List_DragDrop;
        _list.HandleCreated += (_, _) => ThemeHelper.SetDarkScrollbars(_list, _theme.IsDark);
        _list.ClientSizeChanged += (_, _) => FitNameColumn();
        _list.ColumnWidthChanged += (_, e) => { if (e.ColumnIndex != 0) FitNameColumn(); };
        _list.RightDown += List_RightDown;
        _list.RightMove += List_RightMove;
        _list.RightUp += () => { _longPressTimer!.Stop(); _rightDown = false; };
        _list.KeyboardContextMenu += ShowContextMenuAtCursor;

        // Állapotsor
        _status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = S(24),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(S(8), 0, 0, 0),
            AutoEllipsis = true,
        };

        // Útvonal fejléc
        _header = new Label
        {
            Dock = DockStyle.Top,
            Height = S(28),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(S(8), 0, S(8), 0),
            AutoEllipsis = true,
            Font = new Font("Segoe UI Semibold", 9.5F),
        };
        _header.MouseDown += (_, _) => FocusList();
        _header.DoubleClick += (_, _) => AskGoTo();

        // Meghajtó sáv
        _driveBar = new ToolStrip
        {
            Dock = DockStyle.Top,
            GripStyle = ToolStripGripStyle.Hidden,
            RenderMode = ToolStripRenderMode.ManagerRenderMode,
            CanOverflow = true,
            Padding = new Padding(S(4), S(2), S(4), S(2)),
            ImageScalingSize = new Size(S(16), S(16)),
        };
        _freeSpaceLabel = new ToolStripLabel { Tag = "dim", Margin = new Padding(S(6), 0, 0, 0) };
        _upButton = new ToolStripButton("..") { Alignment = ToolStripItemAlignment.Right, ToolTipText = "Szülőmappa (Backspace)" };
        _upButton.Click += (_, _) => { GoUp(); FocusList(); };
        _rootButton = new ToolStripButton("\\") { Alignment = ToolStripItemAlignment.Right, ToolTipText = "Gyökérmappa (Ctrl+\\)" };
        _rootButton.Click += (_, _) => { GoRoot(); FocusList(); };

        _contextMenu = BuildContextMenu();

        // Fülek (csak 2+ fülnél látszik)
        _tabBar = new TabBar { Dock = DockStyle.Top, Visible = false };
        _tabBar.TabSelected += i => { SelectTab(i); FocusList(); };
        _tabBar.TabCloseRequested += i => { CloseTab(i); FocusList(); };
        _tabBar.NewTabRequested += () => { NewTab(); FocusList(); };
        _tabBar.TabMenuRequested += ShowTabMenu;

        Controls.Add(_list);
        Controls.Add(_status);
        Controls.Add(_header);
        Controls.Add(_tabBar);
        Controls.Add(_driveBar);

        _reloadTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _reloadTimer.Tick += (_, _) => { _reloadTimer.Stop(); Reload(); };
        _longPressTimer = new System.Windows.Forms.Timer { Interval = 550 };
        _longPressTimer.Tick += LongPress_Tick;

        RefreshDrives();
        ApplyTheme(Theme.Current);
    }

    // =========================================================================
    //  Téma
    // =========================================================================

    public void ApplyTheme(Theme t)
    {
        _theme = t;
        BackColor = t.Surface;
        _list.BackColor = t.Window;
        _list.ForeColor = t.Text;
        _status.BackColor = t.Surface;
        _status.ForeColor = t.TextDim;
        _driveBar.BackColor = t.Surface;
        _driveBar.ForeColor = t.Text;
        _driveBar.RenderMode = ToolStripRenderMode.ManagerRenderMode;

        _upButton.Image = Glyphs.Render(Glyphs.Up, t.TextDim, S(16));
        _rootButton.Image = Glyphs.Render(Glyphs.Home, t.TextDim, S(16));
        bool glyphs = _upButton.Image != null;
        _upButton.DisplayStyle = _rootButton.DisplayStyle =
            glyphs ? ToolStripItemDisplayStyle.Image : ToolStripItemDisplayStyle.Text;

        ThemeHelper.SetDarkScrollbars(_list, t.IsDark);
        UpdateHeader();
        _list.Invalidate();
        _driveBar.Invalidate();
        _tabBar.Invalidate();
    }

    // =========================================================================
    //  Fülek
    // =========================================================================

    public IReadOnlyList<string> TabPaths => _tabs;
    public int ActiveTabIndex => _tabIndex;
    public int TabCount => _tabs.Count;

    private void RaisePathChanged()
    {
        if (_tabIndex >= 0 && _tabIndex < _tabs.Count) _tabs[_tabIndex] = CurrentPath;
        UpdateTabBar();
        PathChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string TabTitle(string path)
    {
        if (path.Length == 0) return "?";
        string trimmed = path.TrimEnd('\\');
        if (trimmed.Length <= 2 && trimmed.EndsWith(':')) return trimmed.ToUpperInvariant();
        string name = Path.GetFileName(trimmed);
        return name.Length > 0 ? name : path;
    }

    private void UpdateTabBar()
    {
        _tabBar.SetTabs(_tabs.Select(p => (TabTitle(p), p)), _tabIndex);
        bool show = _tabs.Count > 1;
        if (_tabBar.Visible != show) _tabBar.Visible = show;
    }

    private void GoToTabPath(string path)
    {
        if (Directory.Exists(path) || TrySplitVirtualPath(path, out _, out _)) Navigate(path, addHistory: false);
        else NavigateSafe(path);
    }

    /// <summary>Új fül a kurzor melletti helyen (alapból a jelenlegi mappával).</summary>
    public void NewTab(string? path = null)
    {
        _tabs[_tabIndex] = CurrentPath;
        _tabs.Insert(_tabIndex + 1, path ?? CurrentPath);
        _tabIndex++;
        GoToTabPath(_tabs[_tabIndex]);
        UpdateTabBar();
    }

    public void CloseTab(int? index = null)
    {
        if (_tabs.Count <= 1) return;
        int idx = index ?? _tabIndex;
        if (idx < 0 || idx >= _tabs.Count) return;
        _tabs.RemoveAt(idx);
        if (idx < _tabIndex)
        {
            _tabIndex--;
        }
        else if (idx == _tabIndex)
        {
            _tabIndex = Math.Min(_tabIndex, _tabs.Count - 1);
            GoToTabPath(_tabs[_tabIndex]);
        }
        UpdateTabBar();
    }

    public void CloseOtherTabs(int keep)
    {
        if (keep < 0 || keep >= _tabs.Count) return;
        string p = keep == _tabIndex ? CurrentPath : _tabs[keep];
        _tabs.Clear();
        _tabs.Add(p);
        _tabIndex = 0;
        GoToTabPath(p);
        UpdateTabBar();
    }

    public void SelectTab(int i)
    {
        if (i < 0 || i >= _tabs.Count || i == _tabIndex) return;
        _tabs[_tabIndex] = CurrentPath;
        _tabIndex = i;
        GoToTabPath(_tabs[i]);
        UpdateTabBar();
    }

    public void CycleTab(int delta)
    {
        if (_tabs.Count < 2) return;
        SelectTab(((_tabIndex + delta) % _tabs.Count + _tabs.Count) % _tabs.Count);
    }

    /// <summary>Indításkor a mentett fülek visszaállítása.</summary>
    public void RestoreTabs(IList<string>? paths, int index, string? fallback)
    {
        var list = paths?.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() ?? new List<string>();
        if (list.Count == 0) list.Add(fallback ?? "");
        _tabs.Clear();
        _tabs.AddRange(list);
        _tabIndex = Math.Clamp(index, 0, _tabs.Count - 1);
        NavigateSafe(_tabs[_tabIndex]);
        _tabs[_tabIndex] = CurrentPath;
        UpdateTabBar();
    }

    private void ShowTabMenu(int index, Point p)
    {
        var m = new ContextMenuStrip();
        m.Items.Add("Új fül", null, (_, _) => NewTab(index == _tabIndex ? CurrentPath : _tabs[index]));
        m.Items.Add("Fül bezárása", null, (_, _) => CloseTab(index)).Enabled = _tabs.Count > 1;
        m.Items.Add("Többi fül bezárása", null, (_, _) => CloseOtherTabs(index)).Enabled = _tabs.Count > 1;
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Hozzáadás a kedvencekhez", null, (_, _) =>
        {
            SelectTab(index);
            CommandRequested?.Invoke("favadd");
        });
        m.Show(_tabBar, p);
    }

    private static readonly int[] BaseColumnWidths = { 250, 52, 95, 118, 48 };

    /// <summary>Betűméret és sortávolság a beállításokból.</summary>
    public void ApplyViewOptions(float fontSize, string rowSpacing)
    {
        fontSize = Math.Clamp(fontSize, 7f, 16f);
        if (Math.Abs(_list.Font.Size - fontSize) > 0.01f) _list.Font = new Font("Segoe UI", fontSize);
        int pad = rowSpacing switch { "compact" => 1, "roomy" => 12, _ => 7 };
        int h = Math.Max(S(16), TextRenderer.MeasureText("Ág", _list.Font).Height + S(pad));
        if (_rowSizer.ImageSize.Height != h)
        {
            _rowSizer.ImageSize = new Size(1, h);
            _list.SmallImageList = null;          // a sormagasság csak újra-hozzárendeléskor frissül
            _list.SmallImageList = _rowSizer;
        }
        float k = fontSize / 9f;
        for (int i = 1; i < _list.Columns.Count; i++) _list.Columns[i].Width = (int)(S(BaseColumnWidths[i]) * k);
        FitNameColumn();
        _list.Invalidate();
    }

    private void FitNameColumn()
    {
        if (_fittingColumns || _list.Columns.Count < 5) return;
        _fittingColumns = true;
        try
        {
            int others = 0;
            for (int i = 1; i < _list.Columns.Count; i++) others += _list.Columns[i].Width;
            int w = Math.Max(S(120), _list.ClientSize.Width - others);
            if (_list.Columns[0].Width != w) _list.Columns[0].Width = w;
        }
        finally
        {
            _fittingColumns = false;
        }
    }

    // =========================================================================
    //  Nyilvános műveletek
    // =========================================================================

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            UpdateHeader();
            _list.Invalidate();
        }
    }

    public void FocusList() => _list.Focus();

    public bool ListHasFocus => _list.Focused;

    public void SetSort(int column, bool descending)
    {
        _sortColumn = Math.Clamp(column, 0, 3);
        _sortDescending = descending;
        UpdateColumnHeaders();
    }

    public FileEntry? CursorEntry
    {
        get
        {
            int i = CursorIndex;
            return i >= 0 && i < _entries.Count ? _entries[i] : null;
        }
    }

    private int CursorIndex => _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : -1;

    /// <summary>A megjelölt elemek, vagy ha nincs jelölés, a kurzor alatti elem.</summary>
    public List<FileEntry> GetSelection()
    {
        var marked = _entries.Where(e => e.Marked).ToList();
        if (marked.Count > 0) return marked;
        var cur = CursorEntry;
        return cur != null && !cur.IsParent ? new List<FileEntry> { cur } : new List<FileEntry>();
    }

    /// <summary>
    /// Navigálás egy mappába. Lemezképen/archívumon belüli útvonalat is elfogad,
    /// pl. "D:\Játékok\Halo 3.iso\media" vagy "D:\Amiga\demo.lha\c".
    /// </summary>
    public bool Navigate(string path, string? focusName = null, bool addHistory = true)
    {
        string full;
        try
        {
            full = NormalizeDir(path);
        }
        catch (Exception ex)
        {
            Msg.Show(FindForm(), ex.Message, "Dual Deck Commander", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (Directory.Exists(full)) return NavigateReal(full, focusName, addHistory);
        if (TrySplitVirtualPath(full, out var container, out var inner))
            return NavigateVirtual(container, inner, focusName, addHistory, showErrors: true);

        Msg.Show(FindForm(), $"A mappa nem létezik:\n{full}", "Dual Deck Commander", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    private bool NavigateReal(string full, string? focusName, bool addHistory)
    {
        if (InArchive || !string.Equals(full, CurrentPath, StringComparison.OrdinalIgnoreCase))
        {
            if (addHistory && CurrentPath.Length > 0) _history.Push(CurrentPath);
            CloseArchive();
            CurrentPath = full;
            Load(focusName, keepMarks: false);
            StartWatcher();
            RaisePathChanged();
        }
        else
        {
            Load(focusName, keepMarks: true);
        }
        return true;
    }

    /// <summary>Megnyit egy tárolót (lemezkép vagy archívum). Null, ha nem sikerült.</summary>
    private IVirtualFs? OpenContainer(string file, bool showErrors)
    {
        Cursor = Cursors.WaitCursor;
        try
        {
            if (VirtualFs.IsDiscImage(file))
            {
                var iso = XboxIso.TryOpen(file);
                if (iso == null && showErrors)
                    Msg.Show(FindForm(), "Ez nem Xbox / Xbox 360 lemezkép (XDVDFS), ezért nem böngészhető.\n" +
                                         "Meghajtóként csatolni tudod.\n\n" + file,
                        "Lemezkép", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return iso;
            }
            return VirtualFs.OpenArchive(file);
        }
        catch (Exception ex)
        {
            if (showErrors)
                Msg.Show(FindForm(), "Az archívum nem nyitható meg:\n" + ex.Message, "Archívum",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }

    /// <summary>Belépés egy lemezképbe / archívumba (vagy azon belüli mappába).</summary>
    public bool NavigateVirtual(string container, string inner, string? focusName, bool addHistory, bool showErrors)
    {
        var vfs = _vfs;
        bool opened = false;
        if (vfs == null || !string.Equals(vfs.FilePath, container, StringComparison.OrdinalIgnoreCase))
        {
            vfs = OpenContainer(container, showErrors);
            if (vfs == null) return false;
            opened = true;
        }

        VEntry? dir;
        try
        {
            dir = vfs.Find(inner);
        }
        catch (Exception ex)
        {
            if (opened) vfs.Dispose();
            Msg.Show(FindForm(), "Nem olvasható:\n" + ex.Message, "Archívum", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        if (dir == null || !dir.IsDirectory) dir = vfs.Root;

        string newPath = dir.Path.Length == 0 ? container : container + "\\" + dir.Path;
        bool same = string.Equals(newPath, CurrentPath, StringComparison.OrdinalIgnoreCase);
        if (addHistory && CurrentPath.Length > 0 && !same) _history.Push(CurrentPath);

        if (opened)
        {
            CloseArchive();
            _watcher?.Dispose();
            _watcher = null;
            _vfs = vfs;
        }
        _vdir = dir;
        CurrentPath = newPath;
        Load(focusName, keepMarks: same);
        if (!same) RaisePathChanged();
        return true;
    }

    /// <summary>A kurzor alatti archívum / Xbox lemezkép megnyitása böngészésre.</summary>
    public bool BrowseCursorContainer()
    {
        var e = CursorEntry;
        return e != null && e.IsBrowsable && NavigateVirtual(e.FullPath, "", null, true, showErrors: true);
    }

    private void CloseArchive()
    {
        _vfs?.Dispose();
        _vfs = null;
        _vdir = null;
    }

    /// <summary>"C:\x\game.iso\media\a" -> ("C:\x\game.iso", "media\a").</summary>
    public static bool TrySplitVirtualPath(string full, out string container, out string inner)
    {
        string? p = full;
        while (!string.IsNullOrEmpty(p))
        {
            if (VirtualFs.IsBrowsable(p) && File.Exists(p))
            {
                container = p;
                inner = full.Length > p.Length ? full[p.Length..].Trim('\\') : "";
                return true;
            }
            p = Path.GetDirectoryName(p);
        }
        container = inner = "";
        return false;
    }

    /// <summary>Indításkor: ha nem létezik, egy szinttel feljebb próbálkozik.</summary>
    public void NavigateSafe(string? path)
    {
        string? p = string.IsNullOrWhiteSpace(path) ? null : path;
        try
        {
            if (p != null && !Directory.Exists(p) && TrySplitVirtualPath(NormalizeDir(p), out var c, out var inner) &&
                NavigateVirtual(c, inner, null, false, false))
                return;
        }
        catch { }
        while (p != null && !Directory.Exists(p)) p = Path.GetDirectoryName(p);
        p ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        NavigateReal(NormalizeDir(p), null, false);
    }

    public void Reload()
    {
        if (CurrentPath.Length == 0) return;
        if (InArchive)
        {
            if (!File.Exists(_vfs!.FilePath)) { NavigateSafe(RealDirectory); return; }
            Load(CursorEntry?.Name, keepMarks: true);
            return;
        }
        if (!Directory.Exists(CurrentPath))
        {
            NavigateSafe(CurrentPath); // a mappát közben törölték
            return;
        }
        Load(CursorEntry?.Name, keepMarks: true);
    }

    public void ClearMarks()
    {
        foreach (var e in _entries) e.Marked = false;
        _list.Invalidate();
        UpdateStatus();
    }

    public void GoUp()
    {
        if (_vfs != null && _vdir != null)
        {
            if (_vdir.Path.Length == 0)
            {
                string file = _vfs.FilePath;
                NavigateReal(Path.GetDirectoryName(file)!, Path.GetFileName(file), true);
            }
            else
            {
                var parent = _vfs.GetParent(_vdir);
                NavigateVirtual(_vfs.FilePath, parent?.Path ?? "", _vdir.Name, true, true);
            }
            return;
        }
        var p = Directory.GetParent(CurrentPath);
        if (p != null) Navigate(p.FullName, Path.GetFileName(CurrentPath));
    }

    public void GoRoot()
    {
        var root = Path.GetPathRoot(RealDirectory);
        if (!string.IsNullOrEmpty(root)) Navigate(root);
    }

    public void GoBack()
    {
        while (_history.Count > 0)
        {
            var p = _history.Pop();
            if (Directory.Exists(p) || TrySplitVirtualPath(p, out _, out _))
            {
                Navigate(p, addHistory: false);
                return;
            }
        }
    }

    public void FocusName(string name)
    {
        int i = _entries.FindIndex(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));
        if (i >= 0) SetCursor(i);
    }

    public void ExecuteCursor()
    {
        var e = CursorEntry;
        if (e == null) return;
        if (e.IsParent) { GoUp(); return; }
        if (e.IsDirectory)
        {
            if (_vfs != null && e.Inner != null) NavigateVirtual(_vfs.FilePath, e.Inner.Path, null, true, true);
            else Navigate(e.FullPath);
            return;
        }
        if (ImageLoader.IsImage(e.Name) && (e.IsVirtual || Settings.Current.ImageOpenMode == "internal"))
        {
            CommandRequested?.Invoke("imageview");   // beépített képnézegető (archívumból mindig)
            return;
        }
        if (e.IsVirtual)
        {
            CommandRequested?.Invoke("vopen");       // ideiglenes kibontás és megnyitás
            return;
        }
        if (e.IsDiscImage)
        {
            CommandRequested?.Invoke("isoask");      // böngészés vagy csatolás?
            return;
        }
        if (e.IsBrowsable)
        {
            if (Settings.Current.ArchiveEnterAction == "shell") ShellOpen(e.FullPath, CurrentPath);
            else NavigateVirtual(e.FullPath, "", null, true, showErrors: true);
            return;
        }
        ShellOpen(e.FullPath, CurrentPath);
    }

    public static void ShellOpen(string file, string workDir)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true, WorkingDirectory = workDir });
        }
        catch (Exception ex)
        {
            Msg.Show(ex.Message, "Megnyitás", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    public void MarkAll(bool mark)
    {
        foreach (var e in _entries.Where(x => !x.IsParent)) e.Marked = mark;
        _list.Invalidate();
        UpdateStatus();
    }

    public void InvertMarks()
    {
        foreach (var e in _entries.Where(x => !x.IsParent)) e.Marked = !e.Marked;
        _list.Invalidate();
        UpdateStatus();
    }

    /// <summary>Csoport (minta) szerinti kijelölés, pl. "*.txt;*.doc".</summary>
    public void MarkByPattern(string patterns, bool mark)
    {
        var list = patterns.Split(new[] { ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var e in _entries.Where(x => !x.IsParent))
        {
            if (list.Any(p => FileSystemName.MatchesSimpleExpression(p == "*.*" ? "*" : p, e.Name, ignoreCase: true)))
                e.Marked = mark;
        }
        _list.Invalidate();
        UpdateStatus();
    }

    public void ShowDriveMenu()
    {
        var menu = new ContextMenuStrip();
        foreach (var d in SafeDrives())
        {
            var drive = d;
            string label = drive.Name;
            try
            {
                if (drive.IsReady && !string.IsNullOrEmpty(drive.VolumeLabel)) label += "  " + drive.VolumeLabel;
            }
            catch { }
            menu.Items.Add(label, _icons.GetDriveIcon(drive.Name), (_, _) => Navigate(drive.RootDirectory.FullName));
        }
        menu.Show(_list, new Point(0, 0));
    }

    public void RefreshDrives()
    {
        _driveBar.SuspendLayout();
        _driveBar.Items.Clear();
        foreach (var d in SafeDrives())
        {
            var drive = d;
            var btn = new DriveButton(drive.Name)
            {
                Text = drive.Name.TrimEnd('\\').TrimEnd(':').ToUpperInvariant(),
                ToolTipText = drive.Name + " (" + DriveTypeText(drive.DriveType) + ")",
                Image = _icons.GetDriveIcon(drive.Name),
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                Margin = new Padding(0, 0, S(2), 0),
                Padding = new Padding(S(3), S(1), S(3), S(5)),
            };
            btn.Click += (_, _) => { Navigate(drive.RootDirectory.FullName); FocusList(); };
            _driveBar.Items.Add(btn);
        }
        _driveBar.Items.Add(new ToolStripSeparator());
        _driveBar.Items.Add(_freeSpaceLabel);
        _driveBar.Items.Add(_rootButton);
        _driveBar.Items.Add(_upButton);
        _driveBar.ResumeLayout();
        UpdateHeader();
        UpdateDriveInfo();
    }

    public void AskGoTo()
    {
        var target = InputDialog.Show(FindForm(), "Ugrás mappára", "Mappa elérési útja (archívum belseje is lehet):", CurrentPath);
        if (target != null)
        {
            target = Environment.ExpandEnvironmentVariables(target.Trim().Trim('"'));
            if (target.Length > 0) Navigate(Path.Combine(RealDirectory, target));
        }
        FocusList();
    }

    // =========================================================================
    //  Betöltés és rendezés
    // =========================================================================

    private void Load(string? focusName, bool keepMarks)
    {
        var oldMarks = keepMarks
            ? new HashSet<string>(_entries.Where(e => e.Marked).Select(e => e.Name), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>();
        var oldSizes = keepMarks
            ? _entries.Where(e => e.IsDirectory && e.Size >= 0 && !e.IsParent)
                .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().Size, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        int oldIndex = CursorIndex;

        var result = _vfs != null && _vdir != null ? LoadVirtualEntries() : LoadRealEntries();
        foreach (var e in result)
        {
            if (oldMarks.Contains(e.Name) && !e.IsParent) e.Marked = true;
            if (e.IsDirectory && !e.IsParent && oldSizes.TryGetValue(e.Name, out long s)) e.Size = s;
        }

        result.Sort(CompareEntries);
        _entries = result;
        _list.VirtualListSize = _entries.Count;
        _list.Invalidate();

        int idx = -1;
        if (focusName != null)
            idx = _entries.FindIndex(e => string.Equals(e.Name, focusName, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) idx = keepMarks && oldIndex >= 0 ? Math.Min(oldIndex, _entries.Count - 1) : 0;
        SetCursor(idx);

        UpdateHeader();
        UpdateDriveInfo();
        UpdateStatus();
    }

    private List<FileEntry> LoadRealEntries()
    {
        var result = new List<FileEntry>();
        var dir = new DirectoryInfo(CurrentPath);
        if (dir.Parent != null)
            result.Add(new FileEntry { Name = "..", FullPath = dir.Parent.FullName, IsDirectory = true, IsParent = true });

        try
        {
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = false,
                AttributesToSkip = ShowHidden ? 0 : FileAttributes.Hidden | FileAttributes.System,
            };
            foreach (var fsi in dir.EnumerateFileSystemInfos("*", options))
            {
                bool isDir = (fsi.Attributes & FileAttributes.Directory) != 0;
                result.Add(new FileEntry
                {
                    Name = fsi.Name,
                    FullPath = fsi.FullName,
                    IsDirectory = isDir,
                    Size = isDir ? -1 : ((FileInfo)fsi).Length,
                    Modified = fsi.LastWriteTime,
                    Attributes = fsi.Attributes,
                });
            }
        }
        catch (Exception ex)
        {
            Msg.Show(FindForm(), ex.Message, "Mappa olvasása", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        return result;
    }

    private List<FileEntry> LoadVirtualEntries()
    {
        var vfs = _vfs!;
        var dir = _vdir!;
        var result = new List<FileEntry>();

        string parentPath = dir.Path.Length == 0
            ? Path.GetDirectoryName(vfs.FilePath) ?? vfs.FilePath
            : (vfs.GetParent(dir) is { } p && p.Path.Length > 0 ? vfs.FilePath + "\\" + p.Path : vfs.FilePath);
        result.Add(new FileEntry { Name = "..", FullPath = parentPath, IsDirectory = true, IsParent = true });

        try
        {
            foreach (var c in vfs.GetChildren(dir))
            {
                result.Add(new FileEntry
                {
                    Name = c.Name,
                    FullPath = vfs.FilePath + "\\" + c.Path,
                    IsDirectory = c.IsDirectory,
                    Size = c.IsDirectory ? -1 : c.Size,
                    Modified = c.Modified,
                    Attributes = c.Attributes,
                    Inner = c,
                });
            }
        }
        catch (Exception ex)
        {
            Msg.Show(FindForm(), "Nem olvasható:\n" + ex.Message, "Archívum", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        return result;
    }

    private int CompareEntries(FileEntry a, FileEntry b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a.IsParent) return -1;
        if (b.IsParent) return 1;
        if (a.IsDirectory != b.IsDirectory) return a.IsDirectory ? -1 : 1;

        int r = 0;
        if (!a.IsDirectory)
        {
            r = _sortColumn switch
            {
                1 => string.Compare(a.Extension, b.Extension, StringComparison.OrdinalIgnoreCase),
                2 => a.Size.CompareTo(b.Size),
                3 => a.Modified.CompareTo(b.Modified),
                _ => 0,
            };
        }
        else if (_sortColumn == 3)
        {
            r = a.Modified.CompareTo(b.Modified);
        }
        if (r == 0) r = NativeMethods.NaturalCompare(a.Name, b.Name);
        return _sortDescending ? -r : r;
    }

    private void List_ColumnClick(object? sender, ColumnClickEventArgs e)
    {
        if (e.Column > 3) return;
        if (_sortColumn == e.Column) _sortDescending = !_sortDescending;
        else { _sortColumn = e.Column; _sortDescending = e.Column >= 2; } // méret/dátum: csökkenő
        UpdateColumnHeaders();
        string? name = CursorEntry?.Name;
        _entries.Sort(CompareEntries);
        _list.Invalidate();
        if (name != null) FocusName(name);
    }

    private void UpdateColumnHeaders()
    {
        for (int i = 0; i < _list.Columns.Count; i++)
        {
            string arrow = i == _sortColumn ? (_sortDescending ? "  ▼" : "  ▲") : "";
            _list.Columns[i].Text = ColumnTitles[i] + arrow;
        }
    }

    private void SetCursor(int index)
    {
        if (_entries.Count == 0) return;
        index = Math.Clamp(index, 0, _entries.Count - 1);
        _list.SelectedIndices.Clear();
        _list.SelectedIndices.Add(index);
        _list.FocusedItem = _list.Items[index];
        _list.EnsureVisible(index);
        _list.Invalidate();
    }

    // =========================================================================
    //  Rajzolás
    // =========================================================================

    private void List_RetrieveVirtualItem(object? sender, RetrieveVirtualItemEventArgs e)
    {
        if (e.ItemIndex < 0 || e.ItemIndex >= _entries.Count)
        {
            e.Item = new ListViewItem("");
            return;
        }
        var f = _entries[e.ItemIndex];
        var item = new ListViewItem(f.IsParent ? ".." : (f.IsDirectory ? f.Name : f.BaseName));
        item.SubItems.Add(f.Extension);
        item.SubItems.Add(SizeText(f));
        item.SubItems.Add(f.IsParent || f.Modified == DateTime.MinValue ? "" : f.Modified.ToString("yyyy.MM.dd  HH:mm"));
        item.SubItems.Add(f.IsParent ? "" : f.AttributeText);
        e.Item = item;
    }

    private static string SizeText(FileEntry f)
    {
        if (f.IsParent) return "";
        if (f.IsDirectory && f.Size < 0) return "<MAPPA>";
        return Settings.Current.SizeFormat == "short" ? FormatBytes(f.Size) : f.Size.ToString("N0");
    }

    private void List_DrawColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        var t = _theme;
        using (var b = new SolidBrush(t.SurfaceAlt)) e.Graphics.FillRectangle(b, e.Bounds);
        using (var pen = new Pen(t.Border))
            e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top + S(5), e.Bounds.Right - 1, e.Bounds.Bottom - S(5));
        var r = e.Bounds;
        r.Inflate(-S(8), 0);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        if (e.ColumnIndex == 2) flags |= TextFormatFlags.Right;
        var color = e.ColumnIndex == _sortColumn ? t.Text : t.TextDim;
        TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? "", _list.Font, r, color, flags);
    }

    private void List_DrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        if (e.ItemIndex < 0 || e.ItemIndex >= _entries.Count || e.SubItem == null) return;
        var t = _theme;
        var f = _entries[e.ItemIndex];
        // A kurzort mindig az index alapján döntjük el (az ItemState virtuális módban megbízhatatlan)
        bool isCursor = e.ItemIndex == CursorIndex;
        var g = e.Graphics;

        var bounds = e.Bounds;
        if (e.ColumnIndex == 0) bounds.Width = _list.Columns[0].Width;

        Color back = isCursor ? (_isActive ? t.Selection : t.CursorInactive)
            : e.ItemIndex % 2 == 1 ? t.RowAlt
            : t.Window;
        using (var b = new SolidBrush(back)) g.FillRectangle(b, bounds);

        // szövegszín – a kijelölés (piros) csak a nevet színezi
        Color fore = e.ColumnIndex switch
        {
            0 => f.Marked ? t.Marked
                : f.IsParent ? t.TextDim
                : f.IsDirectory ? t.Folder
                : f.IsBrowsable ? t.Iso
                : f.IsHidden ? t.TextDim
                : t.Text,
            1 => f.IsBrowsable ? t.Iso : t.TextDim,
            2 => f.IsDirectory ? t.TextDim : t.Text,
            _ => t.TextDim,
        };

        var textRect = bounds;
        textRect.Inflate(-S(6), 0);
        if (e.ColumnIndex == 0)
        {
            if (isCursor && _isActive)
            {
                using var accent = new SolidBrush(t.Accent);
                g.FillRectangle(accent, bounds.X, bounds.Y + 1, S(3), bounds.Height - 2);
            }
            int iconSize = _icons.Images.ImageSize.Width;
            int iconIdx = _icons.GetIndex(f);
            int ix = bounds.X + S(8);
            if (iconIdx >= 0)
                g.DrawImage(_icons.Images.Images[iconIdx], ix, bounds.Y + (bounds.Height - iconSize) / 2, iconSize, iconSize);
            textRect.X = ix + iconSize + S(6);
            textRect.Width = bounds.Right - textRect.X - S(4);
        }

        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        if (e.ColumnIndex == 2) flags |= TextFormatFlags.Right;
        TextRenderer.DrawText(g, e.SubItem.Text, _list.Font, textRect, fore, flags);
    }

    private void UpdateHeader()
    {
        var t = _theme;
        string text = CurrentPath.Length == 0 ? "" : CurrentPath.TrimEnd('\\') + "\\";
        if (_vfs is XboxIso) text = "◉  " + text;
        else if (_vfs != null) text = "▣  " + text;
        _header.Text = text;
        _header.BackColor = _isActive ? t.Accent : t.HeaderInactive;
        _header.ForeColor = _isActive ? t.AccentText : t.HeaderInactiveText;

        string real = RealDirectory;
        foreach (ToolStripItem it in _driveBar.Items)
        {
            if (it is DriveButton b)
                b.Checked = real.StartsWith(b.Root, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void UpdateDriveInfo()
    {
        DriveButton? active = _driveBar.Items.OfType<DriveButton>().FirstOrDefault(b => b.Checked);
        foreach (var b in _driveBar.Items.OfType<DriveButton>()) b.Usage = -1;

        try
        {
            var root = Path.GetPathRoot(RealDirectory);
            if (!string.IsNullOrEmpty(root))
            {
                var d = new DriveInfo(root);
                if (d.IsReady && d.TotalSize > 0 && active != null)
                    active.Usage = 1.0 - (double)d.AvailableFreeSpace / d.TotalSize;

                if (_vfs != null)
                {
                    _freeSpaceLabel.Text = _vfs.Summary;
                }
                else if (!d.IsReady)
                {
                    _freeSpaceLabel.Text = "nem elérhető";
                }
                else
                {
                    string label = string.IsNullOrEmpty(d.VolumeLabel) ? "" : d.VolumeLabel + "  •  ";
                    _freeSpaceLabel.Text = $"{label}{FormatBytes(d.AvailableFreeSpace)} szabad / {FormatBytes(d.TotalSize)}";
                }
            }
            else
            {
                _freeSpaceLabel.Text = _vfs?.Summary ?? "";
            }
        }
        catch
        {
            _freeSpaceLabel.Text = _vfs?.Summary ?? "";
        }
        _driveBar.Invalidate();
    }

    private void UpdateStatus()
    {
        int files = 0, dirs = 0, mFiles = 0, mDirs = 0;
        long total = 0, mTotal = 0;
        foreach (var e in _entries)
        {
            if (e.IsParent) continue;
            if (e.IsDirectory)
            {
                dirs++;
                if (e.Marked) { mDirs++; if (e.Size > 0) mTotal += e.Size; }
            }
            else
            {
                files++;
                total += e.Size;
                if (e.Marked) { mFiles++; mTotal += e.Size; }
            }
        }
        string ro = InArchive ? "   •   csak olvasható" : "";
        _status.Text = $"Kijelölve: {mFiles} / {files} fájl, {mDirs} / {dirs} mappa   •   {FormatBytes(mTotal)} / {FormatBytes(total)}{ro}";
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return u == 0 ? $"{bytes:N0} B" : $"{v:0.#} {units[u]}";
    }

    // =========================================================================
    //  Billentyűzet és egér
    // =========================================================================

    private void List_KeyDown(object? sender, KeyEventArgs e)
    {
        int cur = CursorIndex;
        switch (e.KeyCode)
        {
            case Keys.Enter when e.Modifiers == Keys.None:
                ExecuteCursor();
                break;
            case Keys.Back:
                GoUp();
                break;
            case Keys.PageUp when e.Control:
                GoUp();
                break;
            case Keys.PageDown when e.Control:
                // Ctrl+PgDn: archívumba / Xbox lemezképbe mindig belép (kérdés nélkül)
                if (CursorEntry?.IsBrowsable == true) BrowseCursorContainer();
                else ExecuteCursor();
                break;
            case Keys.OemBackslash when e.Control:
            case Keys.Oem5 when e.Control:
                GoRoot();
                break;
            case Keys.Insert when e.Modifiers == Keys.None:
                ToggleMark(cur);
                if (cur + 1 < _entries.Count) SetCursor(cur + 1);
                break;
            case Keys.Space when e.Modifiers == Keys.None:
                ToggleMark(cur);
                if (cur >= 0 && _entries[cur].IsDirectory && !_entries[cur].IsParent && _entries[cur].Marked)
                    CalculateDirSize(_entries[cur]);
                break;
            case Keys.Add:
            {
                var p = InputDialog.Show(FindForm(), "Csoport kijelölése", "Minta (pl. *.txt;*.doc):", "*.*");
                if (p != null) MarkByPattern(p, true);
                break;
            }
            case Keys.Subtract:
            {
                var p = InputDialog.Show(FindForm(), "Kijelölés megszüntetése", "Minta:", "*.*");
                if (p != null) MarkByPattern(p, false);
                break;
            }
            case Keys.Multiply:
                InvertMarks();
                break;
            case Keys.A when e.Control && !e.Shift:
                MarkAll(true);
                break;
            case Keys.A when e.Control && e.Shift:
                MarkAll(false);
                break;
            case Keys.Left when e.Alt:
                GoBack();
                break;
            case Keys.Down when e.Alt:
                ShowHistoryMenu();
                break;
            default:
                return;
        }
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private void List_KeyPress(object? sender, KeyPressEventArgs e)
    {
        if (!char.IsControl(e.KeyChar))
        {
            CharTyped?.Invoke(e.KeyChar);
            e.Handled = true;
        }
    }

    private void List_MouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var hit = _list.HitTest(e.Location);
        int idx = hit.Item?.Index ?? -1;
        if (idx < 0) return;

        if ((ModifierKeys & Keys.Control) != 0)
        {
            ToggleMark(idx);
        }
        else if ((ModifierKeys & Keys.Shift) != 0 && _anchorIndex >= 0)
        {
            int a = Math.Min(_anchorIndex, idx), b = Math.Max(_anchorIndex, idx);
            for (int i = a; i <= b && i < _entries.Count; i++)
                if (!_entries[i].IsParent) _entries[i].Marked = true;
            _list.Invalidate();
            UpdateStatus();
        }
        else
        {
            _anchorIndex = idx;
        }
    }

    // ---- Jobb egérgomb: jelölés (húzva több elemen is), hosszan nyomva helyi menü ----

    private int IndexAt(Point p)
    {
        var hit = _list.HitTest(p);
        return hit.Item?.Index ?? -1;
    }

    private void List_RightDown(Point p)
    {
        PanelActivated?.Invoke(this, EventArgs.Empty);
        int idx = IndexAt(p);
        if (Settings.Current.RightClickMode == "menu")
        {
            // Windows-stílus: a jobb gomb a kurzort viszi oda és helyi menüt nyit
            if (idx >= 0) SetCursor(idx);
            _contextMenu.Show(_list, p);
            return;
        }
        if (idx < 0)
        {
            _contextMenu.Show(_list, p); // üres területen: menü
            return;
        }
        _rightDown = true;
        _rightMoved = false;
        _rightStart = _rightLast = idx;
        _rightState = !_entries[idx].Marked;
        if (!_entries[idx].IsParent)
        {
            _entries[idx].Marked = _rightState;
            _list.RedrawItems(idx, idx, false);
            UpdateStatus();
        }
        _longPressTimer.Stop();
        _longPressTimer.Start();
    }

    private void List_RightMove(Point p)
    {
        if (!_rightDown) return;
        int idx = IndexAt(p);
        if (idx < 0 || idx == _rightLast) return;
        _rightMoved = true;
        _longPressTimer.Stop();
        int a = Math.Min(_rightLast, idx), b = Math.Max(_rightLast, idx);
        for (int i = a; i <= b; i++)
            if (!_entries[i].IsParent) _entries[i].Marked = _rightState;
        _rightLast = idx;
        _list.Invalidate();
        UpdateStatus();
    }

    private void LongPress_Tick(object? sender, EventArgs e)
    {
        _longPressTimer.Stop();
        if (!_rightDown || _rightMoved || _rightStart < 0 || _rightStart >= _entries.Count) return;
        // hosszú nyomás: a jelölést visszavonjuk, és helyi menüt mutatunk
        if (!_entries[_rightStart].IsParent) _entries[_rightStart].Marked = !_rightState;
        _rightDown = false;
        _list.Capture = false;
        SetCursor(_rightStart);
        UpdateStatus();
        _contextMenu.Show(_list, _list.PointToClient(Cursor.Position));
    }

    private void List_MouseDoubleClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && (ModifierKeys & (Keys.Control | Keys.Shift)) == 0)
            ExecuteCursor();
    }

    private void ToggleMark(int index)
    {
        if (index < 0 || index >= _entries.Count || _entries[index].IsParent) return;
        _entries[index].Marked = !_entries[index].Marked;
        _anchorIndex = index;
        _list.RedrawItems(index, index, false);
        UpdateStatus();
    }

    private void CalculateDirSize(FileEntry entry)
    {
        _status.Text = "Méret számítása: " + entry.Name + " ...";
        var vfs = _vfs;
        Task.Run(() => entry.Inner != null && vfs != null
                ? vfs.GetTotalSize(entry.Inner)
                : FileOperations.GetDirectorySize(entry.FullPath, CancellationToken.None))
            .ContinueWith(t =>
            {
                if (IsDisposed) return;
                BeginInvoke(() =>
                {
                    if (t.Status == TaskStatus.RanToCompletion) entry.Size = t.Result;
                    _list.Invalidate();
                    UpdateStatus();
                });
            });
    }

    private void ShowHistoryMenu()
    {
        if (_history.Count == 0) return;
        var menu = new ContextMenuStrip();
        foreach (var p in _history.Distinct(StringComparer.OrdinalIgnoreCase).Take(20))
        {
            var path = p;
            menu.Items.Add(path, null, (_, _) => Navigate(path));
        }
        menu.Show(_header, new Point(0, _header.Height));
    }

    // =========================================================================
    //  Helyi menü
    // =========================================================================

    private ContextMenuStrip BuildContextMenu()
    {
        var m = new ContextMenuStrip();
        ToolStripMenuItem Add(string text, string cmd, string? keys = null)
        {
            var it = new ToolStripMenuItem(text) { ShortcutKeyDisplayString = keys };
            it.Click += (_, _) => CommandRequested?.Invoke(cmd);
            m.Items.Add(it);
            return it;
        }
        var open = new ToolStripMenuItem("Megnyitás") { Font = new Font(m.Font, FontStyle.Bold), ShortcutKeyDisplayString = "Enter" };
        open.Click += (_, _) => ExecuteCursor();
        m.Items.Add(open);
        var browse = Add("Belenézés (böngészés)", "browse", "Ctrl+PgDn");
        var extract = Add("Kibontás...", "extract", "Alt+F9");
        var mount = Add("Csatolás meghajtóként", "mount");
        var unmount = Add("Leválasztás", "unmount");
        var god = Add("Átalakítás GoD formátumra...", "god", "Ctrl+G");
        var openWith = Add("Megnyitás ezzel...", "openwith");
        Add("Nézőke", "view", "F3");
        Add("Szerkesztés", "edit", "F4");
        var info = Add("Archívum / lemezkép információ", "vinfo", "Alt+Enter");
        m.Items.Add(new ToolStripSeparator());
        var cut = Add("Kivágás", "cut", "Ctrl+X");
        var clip = Add("Másolás vágólapra", "clipcopy", "Ctrl+C");
        var paste = Add("Beillesztés", "paste", "Ctrl+V");
        m.Items.Add(new ToolStripSeparator());
        Add("Másolás a másik panelre...", "copy", "F5");
        var move = Add("Áthelyezés a másik panelre...", "move", "F6");
        var rename = Add("Átnevezés...", "rename", "Shift+F6");
        var del = Add("Törlés", "delete", "F8");
        m.Items.Add(new ToolStripSeparator());
        Add("Elérési út másolása", "copypath");
        Add("Parancssor itt", "cmdhere");
        var props = Add("Tulajdonságok", "properties", "Alt+Enter");

        m.Opening += (_, _) =>
        {
            bool inArc = InArchive;
            var cur = CursorEntry;
            bool browsable = cur?.IsBrowsable == true;
            bool disc = cur?.IsDiscImage == true;
            browse.Visible = browsable;
            extract.Visible = browsable || inArc;
            mount.Visible = unmount.Visible = god.Visible = disc;
            info.Visible = inArc || browsable;
            props.Visible = !inArc;
            foreach (var it in new[] { openWith, cut, clip, paste, move, rename, del })
                it.Enabled = !inArc;
        };
        return m;
    }

    private void ShowContextMenuAtCursor()
    {
        int i = CursorIndex;
        var pt = i >= 0 ? _list.GetItemRect(i).Location : Point.Empty;
        pt.Offset(S(24), S(12));
        _contextMenu.Show(_list, pt);
    }

    // =========================================================================
    //  Fogd és vidd
    // =========================================================================

    private void List_ItemDrag(object? sender, ItemDragEventArgs e)
    {
        if (InArchive) return; // archívumból F5-tel lehet kibontani
        var sel = GetSelection();
        if (sel.Count == 0) return;
        var data = new DataObject();
        var sc = new StringCollection();
        sc.AddRange(sel.Select(s => s.FullPath).ToArray());
        data.SetFileDropList(sc);
        _list.DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
    }

    private string DropTargetDir(int screenX, int screenY)
    {
        var pt = _list.PointToClient(new Point(screenX, screenY));
        int idx = IndexAt(pt);
        if (idx >= 0 && idx < _entries.Count && _entries[idx].IsDirectory && !_entries[idx].IsParent)
            return _entries[idx].FullPath;
        return CurrentPath;
    }

    private void List_DragOver(object? sender, DragEventArgs e)
    {
        if (InArchive || e.Data?.GetDataPresent(DataFormats.FileDrop) != true)
        {
            e.Effect = DragDropEffects.None;
            return;
        }
        bool shift = (e.KeyState & 4) != 0;
        e.Effect = shift ? DragDropEffects.Move : DragDropEffects.Copy;
        if ((e.AllowedEffect & e.Effect) == 0) e.Effect = e.AllowedEffect & (DragDropEffects.Copy | DragDropEffects.Move);
    }

    private void List_DragDrop(object? sender, DragEventArgs e)
    {
        if (InArchive) return;
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        string target = DropTargetDir(e.X, e.Y);

        if (files.All(f => string.Equals(Path.GetDirectoryName(f), target, StringComparison.OrdinalIgnoreCase)))
            return;
        if (files.Any(f => string.Equals(f, target, StringComparison.OrdinalIgnoreCase)))
            return;

        FilesDropped?.Invoke(files, target, e.Effect == DragDropEffects.Move);
    }

    // =========================================================================
    //  Mappa figyelése
    // =========================================================================

    private void StartWatcher()
    {
        try
        {
            _watcher?.Dispose();
            _watcher = new FileSystemWatcher(CurrentPath)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size |
                               NotifyFilters.LastWrite | NotifyFilters.Attributes,
                IncludeSubdirectories = false,
            };
            FileSystemEventHandler h = (_, _) => ScheduleReload();
            _watcher.Created += h;
            _watcher.Deleted += h;
            _watcher.Changed += h;
            _watcher.Renamed += (_, _) => ScheduleReload();
            _watcher.EnableRaisingEvents = true;
        }
        catch
        {
            _watcher = null; // pl. egyes hálózati meghajtók nem támogatják
        }
    }

    private void ScheduleReload()
    {
        if (IsDisposed || !IsHandleCreated) return;
        BeginInvoke(() => { _reloadTimer.Stop(); _reloadTimer.Start(); });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _watcher?.Dispose();
            _reloadTimer.Dispose();
            _longPressTimer.Dispose();
            CloseArchive();
        }
        base.Dispose(disposing);
    }

    // =========================================================================
    //  Segédfüggvények
    // =========================================================================

    public static string NormalizeDirSafe(string path)
    {
        try { return NormalizeDir(Environment.ExpandEnvironmentVariables(path)); }
        catch { return path; }
    }

    public static string NormalizeDir(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? "";
        return full.Length > root.Length ? full.TrimEnd('\\', '/') : full;
    }

    private static IEnumerable<DriveInfo> SafeDrives()
    {
        try { return DriveInfo.GetDrives(); }
        catch { return Array.Empty<DriveInfo>(); }
    }

    private static string DriveTypeText(DriveType t) => t switch
    {
        DriveType.Fixed => "helyi lemez",
        DriveType.Removable => "cserélhető",
        DriveType.Network => "hálózati",
        DriveType.CDRom => "CD/DVD",
        DriveType.Ram => "RAM-lemez",
        _ => "egyéb",
    };

    /// <summary>
    /// Villogásmentes ListView. A jobb egérgombot saját maga kezeli (a natív vezérlő
    /// különben kijelölné a sort és saját húzásfigyelő ciklusba lépne).
    /// </summary>
    private sealed class PanelListView : ListView
    {
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_RBUTTONDBLCLK = 0x0206;
        private const int WM_CONTEXTMENU = 0x007B;
        private const int MK_RBUTTON = 0x0002;

        public event Action<Point>? RightDown;
        public event Action<Point>? RightMove;
        public event Action? RightUp;
        public event Action? KeyboardContextMenu;

        public PanelListView() { DoubleBuffered = true; }

        private static Point PointFrom(IntPtr lParam)
        {
            long v = lParam.ToInt64();
            return new Point((short)(v & 0xFFFF), (short)((v >> 16) & 0xFFFF));
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_RBUTTONDOWN:
                case WM_RBUTTONDBLCLK:
                    Focus();
                    Capture = true;
                    RightDown?.Invoke(PointFrom(m.LParam));
                    return;
                case WM_RBUTTONUP:
                    Capture = false;
                    RightUp?.Invoke();
                    return;
                case WM_MOUSEMOVE when (m.WParam.ToInt64() & MK_RBUTTON) != 0:
                    RightMove?.Invoke(PointFrom(m.LParam));
                    return;
                case WM_CONTEXTMENU:
                    // billentyűzetről (Shift+F10, Menü gomb) érkező kérés
                    if (m.LParam.ToInt64() == -1) KeyboardContextMenu?.Invoke();
                    return;
            }
            base.WndProc(ref m);
        }
    }

    /// <summary>Meghajtógomb: a kiválasztott meghajtó alatt vékony foglaltságsávot rajzol.</summary>
    private sealed class DriveButton : ToolStripButton
    {
        public string Root { get; }
        public double Usage { get; set; } = -1;

        public DriveButton(string root) { Root = root; }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!Checked || Usage < 0) return;
            var t = Theme.Current;
            int h = Math.Max(2, S(3));
            int margin = S(4);
            var r = new Rectangle(margin, Height - h - S(2), Width - 2 * margin, h);
            using (var b = new SolidBrush(t.Border)) e.Graphics.FillRectangle(b, r);
            int w = (int)Math.Round(r.Width * Math.Clamp(Usage, 0, 1));
            if (w > 0)
            {
                using var b = new SolidBrush(Usage > 0.9 ? t.Marked : t.Accent);
                e.Graphics.FillRectangle(b, r.X, r.Y, w, h);
            }
        }
    }
}
