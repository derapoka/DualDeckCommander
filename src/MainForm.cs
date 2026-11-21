using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

namespace DualDeckCommander;

/// <summary>Főablak: két panel, parancssor és funkcióbillentyű-sáv.</summary>
internal sealed class MainForm : Form
{
    private readonly Settings _settings;
    private readonly FilePanel _left;
    private readonly FilePanel _right;
    private readonly SplitContainer _split;
    private readonly Label _cmdLabel;
    private readonly ComboBox _cmdBox;
    private readonly Panel _cmdPanel;
    private readonly TableLayoutPanel _fkeyBar;
    private readonly ToolStrip _toolbar;
    private readonly MenuStrip _menu;
    private readonly ToolStripMenuItem _hiddenMenuItem;
    private readonly ToolStripButton _hiddenButton;
    private readonly ToolStripButton _themeButton;
    private readonly ToolStripMenuItem _themeSystem;
    private readonly ToolStripMenuItem _themeLight;
    private readonly ToolStripMenuItem _themeDark;
    private readonly List<(ToolStripItem Item, char Glyph)> _glyphItems = new();
    private FilePanel _active;
    private double _splitRatio;
    private bool _loaded;

    private static readonly string TempRoot = Path.Combine(Path.GetTempPath(), "DualDeckCommander");

    private FilePanel Other => _active == _left ? _right : _left;

    private static int S(int v) => UiScale.S(v);

    private readonly string? _startPath;

    public MainForm(Settings settings, string? startPath = null)
    {
        _settings = settings;
        Settings.Current = settings;
        _startPath = startPath;
        Theme.Set(Theme.FromSetting(_settings.ThemeName));
        _splitRatio = _settings.SplitRatio is > 0.1 and < 0.9 ? _settings.SplitRatio : 0.5;

        SuspendLayout();
        Text = "Dual Deck Commander";
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.None;
        MinimumSize = UiScale.S(720, 420);
        Icon = AppAssets.AppIcon;
        RestoreWindowBounds();

        // ---- Panelek ---------------------------------------------------------
        _left = CreatePanel(_settings.LeftSortColumn, _settings.LeftSortDescending);
        _right = CreatePanel(_settings.RightSortColumn, _settings.RightSortDescending);
        _active = _left;

        _split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = S(4),
        };
        _split.Panel1.Controls.Add(_left);
        _split.Panel2.Controls.Add(_right);
        _split.SplitterMoved += (_, _) =>
        {
            if (_loaded && _split.Width > 0) _splitRatio = (double)_split.SplitterDistance / _split.Width;
        };
        _split.DoubleClick += (_, _) => { _splitRatio = 0.5; ApplySplitRatio(); };

        // ---- Parancssor --------------------------------------------------------
        _cmdPanel = new Panel { Dock = DockStyle.Bottom, Height = S(34), Padding = new Padding(S(8), S(5), S(8), S(4)) };
        _cmdLabel = new Label
        {
            Dock = DockStyle.Left,
            AutoSize = true,
            MaximumSize = new Size(S(420), 0),
            TextAlign = ContentAlignment.MiddleRight,
            Padding = new Padding(0, S(4), S(4), 0),
            Tag = "dim",
        };
        _cmdBox = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown, IntegralHeight = false };
        foreach (var h in _settings.CommandHistory) _cmdBox.Items.Add(h);
        _cmdBox.KeyDown += CmdBox_KeyDown;
        _cmdPanel.Controls.Add(_cmdBox);
        _cmdPanel.Controls.Add(_cmdLabel);

        // ---- Funkcióbillentyű-sáv ------------------------------------------------
        _fkeyBar = BuildFunctionKeyBar();

        // ---- Eszköztár --------------------------------------------------------
        _toolbar = new ToolStrip
        {
            GripStyle = ToolStripGripStyle.Hidden,
            RenderMode = ToolStripRenderMode.ManagerRenderMode,
            Padding = new Padding(S(6), S(3), S(6), S(3)),
            ImageScalingSize = new Size(S(16), S(16)),
        };
        ToolStripButton Tool(char glyph, string text, string tip, Action a)
        {
            var b = new ToolStripButton(text)
            {
                ToolTipText = tip,
                DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
                Padding = new Padding(S(4), S(2), S(4), S(2)),
                Margin = new Padding(0, 0, S(2), 0),
            };
            b.Click += (_, _) => { a(); _active.FocusList(); };
            _toolbar.Items.Add(b);
            _glyphItems.Add((b, glyph));
            return b;
        }
        Tool(Glyphs.Refresh, "Frissítés", "Frissítés (Ctrl+R)", RefreshAll);
        Tool(Glyphs.Back, "Vissza", "Vissza az előző mappába (Alt+←)", () => _active.GoBack());
        _toolbar.Items.Add(new ToolStripSeparator());
        Tool(Glyphs.Swap, "Csere", "Panelek cseréje (Ctrl+U)", SwapPanels);
        Tool(Glyphs.Folder, "Azonos mappa", "A másik panel is ezt a mappát mutassa", () => Other.Navigate(_active.CurrentPath));
        _toolbar.Items.Add(new ToolStripSeparator());
        Tool(Glyphs.Star, "Kedvencek", "Kedvenc mappák (Ctrl+D)", () => ShowFavoritesMenu(_active));
        Tool(Glyphs.NewTab, "Új fül", "Új fül (Ctrl+T)", () => _active.NewTab());
        _toolbar.Items.Add(new ToolStripSeparator());
        Tool(Glyphs.Search, "Keresés", "Fájlok keresése (Alt+F7)", DoSearch);
        Tool(Glyphs.Console, "Parancssor", "Parancssor ebben a mappában", DoCmdHere);
        Tool(Glyphs.Disc, "Archívum info", "Archívum / lemezkép adatai (Alt+Enter)", ShowArchiveInfoForCursor);
        _toolbar.Items.Add(new ToolStripSeparator());
        _hiddenButton = Tool(Glyphs.Hidden, "Rejtett", "Rejtett és rendszerfájlok mutatása (Ctrl+H)", ToggleHidden);
        _hiddenButton.Checked = _settings.ShowHidden;
        _themeButton = Tool(Glyphs.Moon, "Sötét mód", "Váltás sötét és világos téma között", ToggleTheme);
        _themeButton.Alignment = ToolStripItemAlignment.Right;

        // ---- Menü ----------------------------------------------------------------
        _menu = new MenuStrip { Padding = new Padding(S(6), S(2), 0, S(2)) };
        _hiddenMenuItem = new ToolStripMenuItem("Rejtett fájlok mutatása") { ShortcutKeyDisplayString = "Ctrl+H", Checked = _settings.ShowHidden };
        _hiddenMenuItem.Click += (_, _) => ToggleHidden();
        _themeSystem = new ToolStripMenuItem("A Windows beállítása szerint");
        _themeLight = new ToolStripMenuItem("Világos");
        _themeDark = new ToolStripMenuItem("Sötét");
        _themeSystem.Click += (_, _) => SetTheme("system");
        _themeLight.Click += (_, _) => SetTheme("light");
        _themeDark.Click += (_, _) => SetTheme("dark");
        BuildMenu(_menu);
        MainMenuStrip = _menu;

        // Dokkolási sorrend: a később hozzáadott kerül kívülre
        Controls.Add(_split);
        Controls.Add(_cmdPanel);
        Controls.Add(_fkeyBar);
        Controls.Add(_toolbar);
        Controls.Add(_menu);

        _left.ShowHidden = _right.ShowHidden = _settings.ShowHidden;

        Theme.Changed += OnThemeChanged;
        SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        ApplyTheme();
        _toolbar.Visible = _settings.ShowToolbar;
        _fkeyBar.Visible = _settings.ShowFunctionKeys;
        _cmdPanel.Visible = _settings.ShowCommandLine;
        foreach (var p in new[] { _left, _right }) p.ApplyViewOptions(_settings.FontSize, _settings.RowSpacing);
        ResumeLayout(false);
        PerformLayout();
    }

    private FilePanel CreatePanel(int sortCol, bool sortDesc)
    {
        var p = new FilePanel { Dock = DockStyle.Fill };
        p.SetSort(sortCol, sortDesc);
        p.PanelActivated += (_, _) => Activate(p, false);
        p.PathChanged += (_, _) => UpdateCommandLabel();
        p.CommandRequested += cmd => RunPanelCommand(cmd);
        p.CharTyped += c =>
        {
            _cmdBox.Focus();
            _cmdBox.Text += c;
            _cmdBox.SelectionStart = _cmdBox.Text.Length;
        };
        p.FilesDropped += (files, target, move) =>
        {
            Activate(p, true);
            string def = target.EndsWith('\\') ? target : target + "\\";
            TransferWithDialog(files.ToList(), def, move, Path.GetDirectoryName(files[0]) ?? target);
        };
        return p;
    }

    // =========================================================================
    //  Téma
    // =========================================================================

    private void SetTheme(string name)
    {
        _settings.ThemeName = name;
        Theme.Set(Theme.FromSetting(name));
    }

    private void ToggleTheme() => SetTheme(Theme.Current.IsDark ? "light" : "dark");

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTheme();

    private void OnSystemPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (_settings.ThemeName != "system" || IsDisposed || !IsHandleCreated) return;
        var wanted = Theme.FromSetting("system");
        if (wanted != Theme.Current) BeginInvoke(() => Theme.Set(wanted));
    }

    private void ApplyTheme()
    {
        var t = Theme.Current;
        ThemeHelper.Apply(this);
        _split.BackColor = t.Border;
        _split.Panel1.BackColor = _split.Panel2.BackColor = t.Surface;
        _fkeyBar.BackColor = t.Surface;
        foreach (Control c in _fkeyBar.Controls) c.Invalidate();

        foreach (var (item, glyph) in _glyphItems)
        {
            char g = item == _themeButton ? (t.IsDark ? Glyphs.Sun : Glyphs.Moon) : glyph;
            item.Image?.Dispose();
            item.Image = Glyphs.Render(g, item == _themeButton ? t.Accent : t.TextDim, S(16));
        }
        _themeButton.Text = t.IsDark ? "Világos mód" : "Sötét mód";
        _themeSystem.Checked = _settings.ThemeName == "system";
        _themeLight.Checked = _settings.ThemeName == "light";
        _themeDark.Checked = _settings.ThemeName == "dark";
        Invalidate(true);
    }

    // =========================================================================
    //  Életciklus
    // =========================================================================

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_settings.Maximized) WindowState = FormWindowState.Maximized;
        _left.RestoreTabs(_settings.LeftTabs, _settings.LeftTabIndex, _settings.LeftPath);
        _right.RestoreTabs(_settings.RightTabs, _settings.RightTabIndex,
            string.IsNullOrEmpty(_settings.RightPath) ? _settings.LeftPath : _settings.RightPath);
        ApplySplitRatio();
        _loaded = true;
        if (!string.IsNullOrWhiteSpace(_startPath)) OpenStartPath(_startPath);
    }

    /// <summary>Parancssorból (pl. fájltársításból) kapott mappa vagy fájl megnyitása a bal panelen.</summary>
    private void OpenStartPath(string path)
    {
        try
        {
            path = Path.GetFullPath(path.Trim('"'));
            if (Directory.Exists(path))
            {
                _left.NewTab(path);
            }
            else if (File.Exists(path))
            {
                _left.NewTab(Path.GetDirectoryName(path));
                _left.FocusName(Path.GetFileName(path));
                if (VirtualFs.IsBrowsable(path)) _left.ExecuteCursor();
            }
        }
        catch
        {
            // érvénytelen paraméter: figyelmen kívül hagyjuk
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Activate(_left, true);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_loaded) ApplySplitRatio();
    }

    private void ApplySplitRatio()
    {
        if (_split.Width <= 0) return;
        int d = (int)(_split.Width * _splitRatio);
        d = Math.Clamp(d, _split.Panel1MinSize, Math.Max(_split.Panel1MinSize, _split.Width - _split.Panel2MinSize - _split.SplitterWidth));
        try { _split.SplitterDistance = d; } catch { /* túl kicsi ablak */ }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _settings.LeftPath = _left.CurrentPath;
        _settings.RightPath = _right.CurrentPath;
        _settings.LeftSortColumn = _left.SortColumn;
        _settings.LeftSortDescending = _left.SortDescending;
        _settings.RightSortColumn = _right.SortColumn;
        _settings.RightSortDescending = _right.SortDescending;
        _settings.SplitRatio = _splitRatio;
        _settings.Maximized = WindowState == FormWindowState.Maximized;
        var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _settings.WindowX = b.X;
        _settings.WindowY = b.Y;
        _settings.WindowWidth = b.Width;
        _settings.WindowHeight = b.Height;
        _settings.LeftTabs = _settings.RememberTabs ? _left.TabPaths.ToList() : new List<string> { _left.CurrentPath };
        _settings.LeftTabIndex = _settings.RememberTabs ? _left.ActiveTabIndex : 0;
        _settings.RightTabs = _settings.RememberTabs ? _right.TabPaths.ToList() : new List<string> { _right.CurrentPath };
        _settings.RightTabIndex = _settings.RememberTabs ? _right.ActiveTabIndex : 0;
        _settings.CommandHistory = _cmdBox.Items.Cast<object>().Select(o => o.ToString() ?? "").Take(30).ToList();
        _settings.Save();
        base.OnFormClosing(e);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        Theme.Changed -= OnThemeChanged;
        SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
        try { if (Directory.Exists(TempRoot)) Directory.Delete(TempRoot, true); } catch { /* még nyitva van valami */ }
        base.OnFormClosed(e);
    }

    private void RestoreWindowBounds()
    {
        var rect = new Rectangle(_settings.WindowX, _settings.WindowY, _settings.WindowWidth, _settings.WindowHeight);
        if (_settings.WindowX >= -10 && Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(rect)))
        {
            StartPosition = FormStartPosition.Manual;
            Bounds = rect;
        }
        else
        {
            StartPosition = FormStartPosition.CenterScreen;
            Size = new Size(Math.Max(S(1100), _settings.WindowWidth), Math.Max(S(680), _settings.WindowHeight));
        }
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == NativeMethods.WM_DEVICECHANGE && _loaded)
        {
            BeginInvoke(() =>
            {
                _left.RefreshDrives();
                _right.RefreshDrives();
            });
        }
    }

    // =========================================================================
    //  Aktív panel
    // =========================================================================

    private void Activate(FilePanel panel, bool focus)
    {
        _active = panel;
        _left.IsActive = panel == _left;
        _right.IsActive = panel == _right;
        UpdateCommandLabel();
        if (focus) panel.FocusList();
    }

    private void UpdateCommandLabel()
    {
        _cmdLabel.Text = _active.RealDirectory + ">";
    }

    // =========================================================================
    //  Billentyűk
    // =========================================================================

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        bool inList = _left.ListHasFocus || _right.ListHasFocus;
        bool inCmd = _cmdBox.Focused;

        switch (keyData)
        {
            case Keys.Tab when inList || inCmd:
            case Keys.Shift | Keys.Tab when inList || inCmd:
                Activate(Other, true);
                return true;
            case Keys.F3: DoView(); return true;
            case Keys.F4: DoEdit(); return true;
            case Keys.Shift | Keys.F4: DoNewTextFile(); return true;
            case Keys.F5: DoTransfer(false); return true;
            case Keys.F6: DoTransfer(true); return true;
            case Keys.Shift | Keys.F6: DoRename(); return true;
            case Keys.F7: DoNewFolder(); return true;
            case Keys.F8: DoDelete(false); return true;
            case Keys.Shift | Keys.F8: DoDelete(true); return true;
            case Keys.Delete when inList: DoDelete(false); return true;
            case Keys.Shift | Keys.Delete when inList: DoDelete(true); return true;
            case Keys.Alt | Keys.F1: Activate(_left, true); _left.ShowDriveMenu(); return true;
            case Keys.Alt | Keys.F2: Activate(_right, true); _right.ShowDriveMenu(); return true;
            case Keys.Alt | Keys.F7: DoSearch(); return true;
            case Keys.Alt | Keys.F9: DoExtractArchives(); return true;
            case Keys.Control | Keys.U: SwapPanels(); return true;
            case Keys.Control | Keys.R:
            case Keys.F2:
                RefreshAll();
                return true;
            case Keys.Control | Keys.H: ToggleHidden(); return true;
            case Keys.Control | Keys.Shift | Keys.T: ToggleTheme(); return true;
            case Keys.Control | Keys.L: _active.AskGoTo(); return true;
            case Keys.Control | Keys.O: ShowSettings(); return true;
            case Keys.Control | Keys.D: ShowFavoritesMenu(_active); return true;
            case Keys.Control | Keys.G when inList: DoConvertGod(); return true;
            case Keys.Control | Keys.T: _active.NewTab(); _active.FocusList(); return true;
            case Keys.Control | Keys.W: _active.CloseTab(); _active.FocusList(); return true;
            case Keys.Control | Keys.Tab: _active.CycleTab(1); _active.FocusList(); return true;
            case Keys.Control | Keys.Shift | Keys.Tab: _active.CycleTab(-1); _active.FocusList(); return true;
            case Keys.Control | Keys.Up when inList:
                if (_active.CursorEntry is { IsDirectory: true, IsParent: false } d) _active.NewTab(d.FullPath);
                return true;
            case Keys.Alt | Keys.Enter when inList: DoProperties(); return true;
            case Keys.Control | Keys.C when inList: ClipboardCopy(false); return true;
            case Keys.Control | Keys.X when inList: ClipboardCopy(true); return true;
            case Keys.Control | Keys.V when inList: ClipboardPaste(); return true;
            case Keys.Control | Keys.Right when inList:
                if (_active == _left) _right.Navigate(DirUnderCursor());
                return true;
            case Keys.Control | Keys.Left when inList:
                if (_active == _right) _left.Navigate(DirUnderCursor());
                return true;
            case Keys.Control | Keys.Enter when inList:
                InsertIntoCommandLine(_active.CursorEntry?.Name);
                return true;
            case Keys.Control | Keys.Shift | Keys.Enter when inList:
                InsertIntoCommandLine(_active.CursorEntry?.FullPath);
                return true;
            case Keys.Escape when inCmd:
                _cmdBox.Text = "";
                _active.FocusList();
                return true;
            case Keys.F1:
                ShowHelp();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private string DirUnderCursor()
    {
        var e = _active.CursorEntry;
        return e != null && e.IsDirectory ? e.FullPath : _active.CurrentPath;
    }

    private void InsertIntoCommandLine(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (text.Contains(' ')) text = "\"" + text + "\"";
        string cur = _cmdBox.Text;
        _cmdBox.Text = cur.Length == 0 || cur.EndsWith(' ') ? cur + text + " " : cur + " " + text + " ";
        _cmdBox.SelectionStart = _cmdBox.Text.Length;
    }

    // =========================================================================
    //  Fájlműveletek
    // =========================================================================

    private void RunPanelCommand(string cmd)
    {
        switch (cmd)
        {
            case "view": DoView(); break;
            case "edit": DoEdit(); break;
            case "copy": DoTransfer(false); break;
            case "move": DoTransfer(true); break;
            case "rename": DoRename(); break;
            case "delete": DoDelete(false); break;
            case "properties": DoProperties(); break;
            case "openwith": DoOpenWith(); break;
            case "copypath": DoCopyPath(); break;
            case "cmdhere": DoCmdHere(); break;
            case "cut": ClipboardCopy(true); break;
            case "clipcopy": ClipboardCopy(false); break;
            case "paste": ClipboardPaste(); break;
            case "vopen": OpenFromArchive(false); break;
            case "imageview": OpenImageViewer(); break;
            case "isoask": AskIsoAction(); break;
            case "browse": _active.BrowseCursorContainer(); break;
            case "extract": DoExtractArchives(); break;
            case "god": DoConvertGod(); break;
            case "mount": if (_active.CursorEntry is { IsDiscImage: true } m) MountImage(m.FullPath); break;
            case "unmount": if (_active.CursorEntry is { IsDiscImage: true } u) UnmountImage(u.FullPath); break;
            case "vinfo": ShowArchiveInfoForCursor(); break;
            case "favadd": AddFavorite(_active); break;
        }
    }

    private static string Describe(List<FileEntry> sel) =>
        sel.Count == 1 ? $"„{sel[0].Name}”" : $"{sel.Count} elem";

    /// <summary>Igaz, ha a panel archívumot / lemezképet mutat (és figyelmeztet).</summary>
    private bool BlockedByArchive(FilePanel panel)
    {
        if (!panel.InArchive) return false;
        Msg.Show(this,
            "Az archívum / lemezkép csak olvasható, ezért ez a művelet itt nem végezhető el.\n\n" +
            "Fájlokat az F5 billentyűvel tudsz kibontani belőle a másik panelre.",
            "Csak olvasható", MessageBoxButtons.OK, MessageBoxIcon.Information);
        panel.FocusList();
        return true;
    }

    private void DoTransfer(bool move)
    {
        if (_active.InArchive)
        {
            if (move) { BlockedByArchive(_active); return; }
            ExtractWithDialog();
            return;
        }
        if (BlockedByArchive(Other)) return;

        var sel = _active.GetSelection();
        if (sel.Count == 0) return;
        string def = Other.CurrentPath;
        if (!def.EndsWith('\\')) def += "\\";
        TransferWithDialog(sel.Select(s => s.FullPath).ToList(), def, move, _active.CurrentPath);
    }

    /// <summary>A beírt célt mappára és (egy elemnél) új névre bontja.</summary>
    private bool ResolveTarget(string dest, string baseDir, int count, out string targetDir, out string? newName)
    {
        targetDir = "";
        newName = null;
        dest = Environment.ExpandEnvironmentVariables(dest.Trim().Trim('"'));
        if (dest.EndsWith("\\*.*")) dest = dest[..^3];
        if (dest.Length == 0) return false;

        string full;
        try { full = Path.GetFullPath(Path.Combine(baseDir, dest)); }
        catch (Exception ex)
        {
            Msg.Show(this, ex.Message, "Hibás cél", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        bool endsWithSep = dest.EndsWith('\\') || dest.EndsWith('/');
        if (endsWithSep || Directory.Exists(full) || count > 1)
        {
            targetDir = full;
        }
        else
        {
            targetDir = Path.GetDirectoryName(full) ?? full;
            newName = Path.GetFileName(full);
        }

        if (FilePanel.TrySplitVirtualPath(targetDir, out _, out _))
        {
            Msg.Show(this, "Archívumba / lemezképbe nem lehet írni – válassz egy mappát célnak.", "Csak olvasható",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }
        return true;
    }

    private void TransferWithDialog(List<string> sources, string defaultDest, bool move, string baseDir)
    {
        string what = sources.Count == 1 ? $"„{Path.GetFileName(sources[0].TrimEnd('\\'))}”" : $"{sources.Count} elem";
        var dest = InputDialog.Show(this, move ? "Áthelyezés" : "Másolás",
            $"{what} {(move ? "áthelyezése" : "másolása")} ide:", defaultDest);
        if (dest == null || !ResolveTarget(dest, baseDir, sources.Count, out var targetDir, out var newName))
        {
            _active.FocusList();
            return;
        }

        ProgressForm.Run(this, move ? OperationKind.Move : OperationKind.Copy, sources, targetDir, newName);
        _active.ClearMarks();
        RefreshAll();
        _active.FocusList();
    }

    /// <summary>F5 archívumban / lemezképben: kijelölt fájlok/mappák kibontása.</summary>
    private void ExtractWithDialog()
    {
        var vfs = _active.Archive;
        var sel = _active.GetSelection().Where(e => e.Inner != null).ToList();
        if (vfs == null || sel.Count == 0) return;

        string def = Other.RealDirectory;
        if (!def.EndsWith('\\')) def += "\\";
        var dest = InputDialog.Show(this, "Kibontás", $"{Describe(sel)} kibontása ide:", def);
        if (dest == null || !ResolveTarget(dest, _active.RealDirectory, sel.Count, out var targetDir, out var newName))
        {
            _active.FocusList();
            return;
        }

        ProgressForm.RunExtract(this, vfs, sel.Select(e => e.Inner!).ToList(), targetDir, newName);
        _active.ClearMarks();
        RefreshAll();
        _active.FocusList();
    }

    /// <summary>Archívumban lévő fájl megnyitása: ideiglenes mappába bontja, majd megnyitja.</summary>
    private void OpenFromArchive(bool inEditor)
    {
        var vfs = _active.Archive;
        var e = _active.CursorEntry;
        if (vfs == null || e?.Inner == null || e.IsDirectory) return;

        string tempDir = Path.Combine(TempRoot, Guid.NewGuid().ToString("N")[..8]);
        if (!ProgressForm.RunExtract(this, vfs, new[] { e.Inner }, tempDir)) return;
        string path = Path.Combine(tempDir, e.Name);
        if (!File.Exists(path)) return;
        if (inEditor) OpenInEditor(path);
        else FilePanel.ShellOpen(path, tempDir);
    }

    private void DoDelete(bool permanent)
    {
        if (BlockedByArchive(_active)) return;
        var sel = _active.GetSelection();
        if (sel.Count == 0) return;
        bool recycleOk = SupportsRecycleBin(_active.CurrentPath);
        if (!permanent && !_settings.UseRecycleBin) permanent = true;
        if (!permanent && !recycleOk) permanent = true; // hálózati / USB meghajtón nincs Lomtár

        string msg = permanent
            ? $"Véglegesen törlöd: {Describe(sel)}?\n\n" +
              (recycleOk ? "" : "Ezen a meghajtón nincs Lomtár. ") +
              "Ez a művelet NEM vonható vissza!"
            : $"Lomtárba helyezed: {Describe(sel)}?";
        if (permanent || _settings.ConfirmDelete)
        {
            var answer = Msg.Show(this, msg, permanent ? "Végleges törlés" : "Törlés",
                MessageBoxButtons.YesNo, permanent ? MessageBoxIcon.Warning : MessageBoxIcon.Question,
                permanent ? MessageBoxDefaultButton.Button2 : MessageBoxDefaultButton.Button1);
            if (answer != DialogResult.Yes) { _active.FocusList(); return; }
        }

        ProgressForm.Run(this, permanent ? OperationKind.Delete : OperationKind.Recycle,
            sel.Select(s => s.FullPath).ToList());
        RefreshAll();
        _active.FocusList();
    }

    private static bool SupportsRecycleBin(string path)
    {
        try
        {
            if (path.StartsWith(@"\\")) return false;
            var root = Path.GetPathRoot(path);
            return !string.IsNullOrEmpty(root) && new DriveInfo(root).DriveType == DriveType.Fixed;
        }
        catch
        {
            return false;
        }
    }

    private void DoRename()
    {
        if (BlockedByArchive(_active)) return;
        var e = _active.CursorEntry;
        if (e == null || e.IsParent) return;
        var name = InputDialog.Show(this, "Átnevezés", "Új név:", e.Name, e.BaseName.Length);
        _active.FocusList();
        if (name == null) return;
        name = name.Trim();
        if (name.Length == 0 || name == e.Name) return;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            Msg.Show(this, "A név érvénytelen karaktert tartalmaz:  \\ / : * ? \" < > |", "Átnevezés",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string dir = Path.GetDirectoryName(e.FullPath)!;
        string dst = Path.Combine(dir, name);
        try
        {
            void Move(string a, string b)
            {
                if (e.IsDirectory) Directory.Move(a, b);
                else File.Move(a, b);
            }

            if (string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                // csak kis/nagybetű változik: ideiglenes néven át
                string tmp = Path.Combine(dir, name + ".~tc" + Guid.NewGuid().ToString("N")[..6]);
                Move(e.FullPath, tmp);
                Move(tmp, dst);
            }
            else
            {
                Move(e.FullPath, dst);
            }
            _active.Reload();
            _active.FocusName(name);
        }
        catch (Exception ex)
        {
            Msg.Show(this, ex.Message, "Átnevezés", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void DoNewFolder()
    {
        if (BlockedByArchive(_active)) return;
        var name = InputDialog.Show(this, "Új mappa", "Az új mappa neve:", "Új mappa");
        _active.FocusList();
        if (string.IsNullOrWhiteSpace(name)) return;
        name = name.Trim();
        try
        {
            Directory.CreateDirectory(Path.Combine(_active.CurrentPath, name));
            _active.Reload();
            _active.FocusName(name.Split('\\', '/')[0]);
        }
        catch (Exception ex)
        {
            Msg.Show(this, ex.Message, "Új mappa", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void DoNewTextFile()
    {
        if (BlockedByArchive(_active)) return;
        var name = InputDialog.Show(this, "Új fájl", "Az új fájl neve:", "új.txt", 3);
        _active.FocusList();
        if (string.IsNullOrWhiteSpace(name)) return;
        string path = Path.Combine(_active.CurrentPath, name.Trim());
        try
        {
            if (!File.Exists(path)) File.WriteAllText(path, "");
            _active.Reload();
            _active.FocusName(Path.GetFileName(path));
            OpenInEditor(path);
        }
        catch (Exception ex)
        {
            Msg.Show(this, ex.Message, "Új fájl", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>Beépített képnézegető a kurzor alatti képpel; a panel többi képe között lapozni lehet.</summary>
    private void OpenImageViewer()
    {
        var cur = _active.CursorEntry;
        if (cur == null || cur.IsDirectory) return;
        var vfs = _active.Archive;
        var list = new List<ImageItem>();
        int start = 0;
        foreach (var e in _active.Entries)
        {
            if (e.IsDirectory || !ImageLoader.IsImage(e.Name)) continue;
            if (ReferenceEquals(e, cur)) start = list.Count;
            if (e.Inner != null && vfs != null)
            {
                var inner = e.Inner;
                list.Add(new ImageItem(e.Name, () => vfs.OpenFile(inner), null));
            }
            else
            {
                string p = e.FullPath;
                list.Add(new ImageItem(e.Name,
                    () => new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), p));
            }
        }
        if (list.Count == 0) return;
        new ImageViewerForm(list, start).Show(this);
    }

    private void DoView()
    {
        var e = _active.CursorEntry;
        if (e == null || e.IsDirectory) return;
        if (ImageLoader.IsImage(e.Name))
        {
            OpenImageViewer();
            return;
        }
        if (e.Inner != null && _active.Archive is { } vfs)
        {
            var entry = e.Inner;
            new ViewerForm(e.FullPath, () => vfs.OpenFile(entry)).Show(this);
            return;
        }
        new ViewerForm(e.FullPath).Show(this);
    }

    private void DoEdit()
    {
        var e = _active.CursorEntry;
        if (e == null || e.IsDirectory) return;
        if (e.IsVirtual)
        {
            OpenFromArchive(true); // ideiglenes másolat – a változás nem kerül vissza az archívumba
            return;
        }
        OpenInEditor(e.FullPath);
    }

    private void OpenInEditor(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(_settings.Editor, "\"" + path + "\"")
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(path) ?? "",
            });
        }
        catch (Exception ex)
        {
            Msg.Show(this, $"A szerkesztő nem indítható ({_settings.Editor}):\n{ex.Message}", "Szerkesztés",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void DoProperties()
    {
        if (_active.InArchive)
        {
            ShowArchiveInfo(_active.Archive!);
            return;
        }
        var sel = _active.GetSelection();
        string path = sel.Count > 0 ? sel[0].FullPath : _active.CurrentPath;
        try { NativeMethods.SHObjectProperties(Handle, NativeMethods.SHOP_FILEPATH, path, null); }
        catch (Exception ex) { Msg.Show(this, ex.Message); }
    }

    private void ShowArchiveInfoForCursor()
    {
        if (_active.InArchive)
        {
            ShowArchiveInfo(_active.Archive!);
            return;
        }
        var e = _active.CursorEntry;
        if (e == null || !e.IsBrowsable)
        {
            Msg.Show(this, "Állj a kurzorral egy archívumra vagy lemezképre (.zip, .rar, .7z, .lha, .lzh, .lzx, .iso),\n" +
                           "vagy lépj be egybe.", "Archívum információ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        IVirtualFs? vfs;
        try
        {
            vfs = e.IsDiscImage ? XboxIso.TryOpen(e.FullPath) : VirtualFs.OpenArchive(e.FullPath);
        }
        catch (Exception ex)
        {
            Msg.Show(this, ex.Message, "Archívum információ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (vfs == null)
        {
            Msg.Show(this, "Ez nem Xbox / Xbox 360 lemezkép (XDVDFS).\nMeghajtóként csatolni tudod (Enter).",
                "Lemezkép információ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using (vfs) ShowArchiveInfo(vfs);
    }

    private void ShowArchiveInfo(IVirtualFs vfs)
    {
        string text = string.Join("\n", vfs.InfoLines());
        if (vfs is XboxIso { TitleId: { } titleId })
        {
            int r = Dialogs.Choose(this, vfs.KindName, text, new[] { "OK", "Title ID másolása" }, 0, 0, null);
            if (r == 1)
            {
                try { Clipboard.SetText(titleId); } catch { }
            }
        }
        else
        {
            Dialogs.Choose(this, vfs.KindName, text, new[] { "OK" }, 0, 0, null);
        }
        _active.FocusList();
    }

    // ---- Lemezképek: böngészés, csatolás, leválasztás ------------------------

    /// <summary>Enter egy .iso fájlon: megkérdezzük, mi legyen vele.</summary>
    private void AskIsoAction()
    {
        var e = _active.CursorEntry;
        if (e == null || !e.IsDiscImage) return;

        string? summary;
        using (var iso = XboxIso.TryOpen(e.FullPath)) summary = iso?.Summary;
        bool xbox = summary != null;

        // beállítás szerint kérdés nélkül
        if (_settings.IsoEnterAction == "mount" || (_settings.IsoEnterAction == "browse" && !xbox))
        {
            MountImage(e.FullPath);
            return;
        }
        if (_settings.IsoEnterAction == "browse")
        {
            _active.NavigateVirtual(e.FullPath, "", null, true, showErrors: true);
            return;
        }

        string msg = $"Mi történjen ezzel a lemezképpel?\n\n{e.Name}\n" +
                     (xbox ? summary : "Ez nem Xbox lemezkép, ezért csak meghajtóként csatolható.");
        var options = xbox
            ? new[] { "&Böngészés", "&GoD átalakítás...", "&Csatolás meghajtóként", "&Leválasztás", "Mégse" }
            : new[] { "&Csatolás meghajtóként", "&Leválasztás", "Mégse" };
        int r = Dialogs.Choose(this, "Lemezkép", msg, options, 0, options.Length - 1, SystemIcons.Question);
        string choice = r >= 0 ? options[r] : "Mégse";

        switch (choice)
        {
            case "&Böngészés":
                _active.NavigateVirtual(e.FullPath, "", null, true, showErrors: true);
                break;
            case "&GoD átalakítás...":
                DoConvertGod(e);
                return;
            case "&Csatolás meghajtóként":
                MountImage(e.FullPath);
                break;
            case "&Leválasztás":
                UnmountImage(e.FullPath);
                break;
        }
        _active.FocusList();
    }

    private static string PsQuote(string s) => "'" + s.Replace("'", "''") + "'";

    /// <summary>Csatolás a Windows beépített lemezkép-kezelőjével, majd a panel az új meghajtóra lép.</summary>
    private async void MountImage(string path)
    {
        var panel = _active;
        UseWaitCursor = true;
        try
        {
            string script =
                "$p = " + PsQuote(path) + "\n" +
                "$img = Get-DiskImage -ImagePath $p -ErrorAction SilentlyContinue\n" +
                "if (-not $img -or -not $img.Attached) { $img = Mount-DiskImage -ImagePath $p -PassThru -ErrorAction Stop }\n" +
                "Start-Sleep -Milliseconds 400\n" +
                "(Get-DiskImage -ImagePath $p | Get-Volume).DriveLetter";
            string output = await Task.Run(() => RunPowerShell(script));
            char letter = output.Trim().LastOrDefault(char.IsLetter);
            if (letter == default) throw new IOException("A Windows nem adott meghajtóbetűjelet a lemezképnek.");

            string root = char.ToUpperInvariant(letter) + ":\\";
            for (int i = 0; i < 20 && !Directory.Exists(root); i++) await Task.Delay(150);
            _left.RefreshDrives();
            _right.RefreshDrives();
            panel.Navigate(root);
            Activate(panel, true);
        }
        catch (Exception ex)
        {
            if (Msg.Show(this, "A csatolás nem sikerült:\n" + ex.Message + "\n\nMegpróbálod a Windows Intézővel?",
                    "Csatolás", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                FilePanel.ShellOpen(path, Path.GetDirectoryName(path) ?? "");
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private async void UnmountImage(string path)
    {
        UseWaitCursor = true;
        try
        {
            string script =
                "$p = " + PsQuote(path) + "\n" +
                "$l = (Get-DiskImage -ImagePath $p -ErrorAction Stop | Get-Volume).DriveLetter\n" +
                "Dismount-DiskImage -ImagePath $p -ErrorAction Stop | Out-Null\n" +
                "\"x$l\"";
            string output = await Task.Run(() => RunPowerShell(script));
            string o = output.Trim();
            char letter = o.Length >= 2 && char.IsLetter(o[^1]) ? o[^1] : default;
            // ha valamelyik panel a leválasztott meghajtón állt, visszalépünk a lemezkép mappájába
            foreach (var p in new[] { _left, _right })
            {
                if (letter != default && p.RealDirectory.StartsWith(letter + ":", StringComparison.OrdinalIgnoreCase))
                    p.Navigate(Path.GetDirectoryName(path) ?? "C:\\", Path.GetFileName(path));
            }
            _left.RefreshDrives();
            _right.RefreshDrives();
        }
        catch (Exception ex)
        {
            Msg.Show(this, "A leválasztás nem sikerült:\n" + ex.Message, "Leválasztás",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    private static string RunPowerShell(string script)
    {
        var psi = new ProcessStartInfo("powershell.exe",
            "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " +
            Convert.ToBase64String(Encoding.Unicode.GetBytes(script)))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi) ?? throw new IOException("A PowerShell nem indítható.");
        var outTask = p.StandardOutput.ReadToEndAsync();
        var errTask = p.StandardError.ReadToEndAsync();
        if (!p.WaitForExit(30000))
        {
            try { p.Kill(); } catch { }
            throw new TimeoutException("A PowerShell nem válaszolt.");
        }
        string output = outTask.Result;
        string err = errTask.Result.Trim();
        if (err.Length > 0 && (p.ExitCode != 0 || output.Trim().Length == 0))
            throw new IOException(err.Split('\n')[0].Trim());
        return output;
    }

    /// <summary>Alt+F9: kijelölt archívumok teljes kibontása a másik panelre.</summary>
    private void DoExtractArchives()
    {
        if (_active.InArchive)
        {
            ExtractWithDialog();
            return;
        }
        var sel = _active.GetSelection().Where(e => e.IsBrowsable).ToList();
        if (sel.Count == 0)
        {
            Msg.Show(this, "Jelölj ki egy vagy több archívumot\n(.zip, .rar, .7z, .lha, .lzh, .lzx vagy Xbox .iso).",
                "Kibontás", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (BlockedByArchive(Other)) return;

        string baseDir = Other.CurrentPath;
        if (!baseDir.EndsWith('\\')) baseDir += "\\";
        string def = sel.Count == 1 ? baseDir + Path.GetFileNameWithoutExtension(sel[0].Name) + "\\" : baseDir;
        string prompt = sel.Count == 1
            ? $"„{sel[0].Name}” kibontása ide:"
            : $"{sel.Count} archívum kibontása ide (mindegyik saját almappába):";
        var dest = InputDialog.Show(this, "Kibontás", prompt, def);
        if (dest == null) { _active.FocusList(); return; }

        dest = Environment.ExpandEnvironmentVariables(dest.Trim().Trim('"'));
        string full;
        try { full = Path.GetFullPath(Path.Combine(_active.CurrentPath, dest)); }
        catch (Exception ex) { Msg.Show(this, ex.Message, "Hibás cél", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (FilePanel.TrySplitVirtualPath(full, out _, out _))
        {
            Msg.Show(this, "Archívumba nem lehet kibontani – válassz egy mappát célnak.", "Kibontás",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ProgressForm.RunExtractArchives(this, sel.Select(s => s.FullPath).ToList(), full, subfolders: sel.Count > 1);
        _active.ClearMarks();
        RefreshAll();
        _active.FocusList();
    }

    /// <summary>Ctrl+G: kijelölt Xbox / Xbox 360 lemezképek átalakítása Games on Demand formátumra.</summary>
    private void DoConvertGod() => DoConvertGod(null);

    private void DoConvertGod(FileEntry? only)
    {
        var sel = only != null ? new List<FileEntry> { only }
            : _active.InArchive ? new List<FileEntry>()
            : _active.GetSelection().Where(e => e.IsDiscImage).ToList();
        if (sel.Count == 0)
        {
            Msg.Show(this, "Jelölj ki egy vagy több Xbox / Xbox 360 lemezképet (.iso).\n" +
                           "Archívumban lévő lemezképet előbb bonts ki.",
                "GoD átalakítás", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // lemezképek beolvasása: csak a játéklemezek alakíthatók át
        var sources = new List<GodSource>();
        var rejected = new List<string>();
        Cursor = Cursors.WaitCursor;
        try
        {
            foreach (var e in sel)
            {
                try
                {
                    using var iso = XboxIso.TryOpen(e.FullPath);
                    if (iso == null) { rejected.Add(e.Name + " – nem Xbox lemezkép"); continue; }
                    if (iso.Exec == null) { rejected.Add(e.Name + " – nincs rajta default.xex / default.xbe"); continue; }
                    sources.Add(new GodSource(e.FullPath, e.Name, iso.TitleName, iso.Exec.TitleId, iso.IsXbox360Title, iso.Platform));
                }
                catch (Exception ex) { rejected.Add(e.Name + " – " + ex.Message); }
            }
        }
        finally { Cursor = Cursors.Default; }

        if (sources.Count == 0)
        {
            Msg.Show(this, "Nincs átalakítható lemezkép:\n\n" + string.Join("\n", rejected),
                "GoD átalakítás", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (rejected.Count > 0 &&
            Msg.Show(this, "Ezek kimaradnak:\n\n" + string.Join("\n", rejected.Take(10)) + "\n\nFolytatod a többivel?",
                "GoD átalakítás", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        string defDest = _settings.GodOutputDir;
        if (string.IsNullOrEmpty(defDest) || !Directory.Exists(defDest))
            defDest = Other.InArchive || string.IsNullOrEmpty(Other.CurrentPath) ? _active.CurrentPath : Other.CurrentPath;

        using var form = new GodForm(sources, defDest, _settings.GodTrim, _settings.GodShowResult);
        if (form.ShowDialog(this) != DialogResult.OK) { _active.FocusList(); return; }

        string dest;
        try { dest = Path.GetFullPath(Path.Combine(_active.CurrentPath, Environment.ExpandEnvironmentVariables(form.Destination))); }
        catch (Exception ex) { Msg.Show(this, ex.Message, "Hibás cél", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (FilePanel.TrySplitVirtualPath(dest, out _, out _))
        {
            Msg.Show(this, "Archívumba nem lehet írni – válassz egy mappát célnak.", "GoD átalakítás",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _settings.GodOutputDir = dest;
        _settings.GodTrim = form.Trim;
        _settings.GodShowResult = form.ShowResult;
        _settings.Save();

        // tervek: célútvonalak, méretek
        var plans = new List<GodConverter.Plan>();
        Cursor = Cursors.WaitCursor;
        try
        {
            foreach (var s in sources)
            {
                using var iso = XboxIso.TryOpen(s.Path)!;
                string? title = sources.Count == 1 ? form.GameTitle : s.Title;
                plans.Add(GodConverter.CreatePlan(iso, dest, title, form.Trim));
            }
        }
        catch (Exception ex)
        {
            Cursor = Cursors.Default;
            Msg.Show(this, ex.Message, "GoD átalakítás", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        finally { Cursor = Cursors.Default; }

        // szabad hely
        try
        {
            long need = plans.Sum(p => p.EstimatedOutputSize);
            var drive = new DriveInfo(Path.GetPathRoot(dest)!);
            if (drive.IsReady && drive.AvailableFreeSpace < need &&
                Msg.Show(this,
                    $"Valószínűleg nem lesz elég hely a célmeghajtón.\n\nSzükséges: {FilePanel.FormatBytes(need)}\n" +
                    $"Szabad: {FilePanel.FormatBytes(drive.AvailableFreeSpace)}\n\nMégis megpróbálod?",
                    "GoD átalakítás", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
        }
        catch { /* hálózati meghajtó stb. – nem ellenőrizhető */ }

        // meglévő kimenet
        var existing = plans.Where(p => File.Exists(p.HeaderPath) || Directory.Exists(p.DataDir)).ToList();
        if (existing.Count > 0 &&
            Msg.Show(this,
                "Ezek a játékok már léteznek a célmappában:\n\n" +
                string.Join("\n", existing.Take(10).Select(p => Path.GetFileName(p.IsoPath))) +
                "\n\nFelülírod őket?",
                "GoD átalakítás", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;

        bool done = ProgressForm.RunGod(this, plans);
        _active.ClearMarks();
        if (done && form.ShowResult && !Other.InArchive)
        {
            string show = plans.Count == 1
                ? Path.GetDirectoryName(Path.GetDirectoryName(plans[0].HeaderPath))!   // <cél>\<TitleID>
                : dest;
            Other.Navigate(show);
        }
        RefreshAll();
        _active.FocusList();
    }

    private void DoOpenWith()
    {
        var e = _active.CursorEntry;
        if (e == null || e.IsDirectory || e.IsVirtual) return;
        try { Process.Start("rundll32.exe", "shell32.dll,OpenAs_RunDLL " + e.FullPath); }
        catch (Exception ex) { Msg.Show(this, ex.Message); }
    }

    private void DoCopyPath()
    {
        var sel = _active.GetSelection();
        string text = sel.Count > 0 ? string.Join("\r\n", sel.Select(s => s.FullPath)) : _active.CurrentPath;
        try { Clipboard.SetText(text); } catch { }
    }

    private void DoCmdHere()
    {
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe") { WorkingDirectory = _active.RealDirectory, UseShellExecute = true });
        }
        catch (Exception ex) { Msg.Show(this, ex.Message); }
    }

    private void ClipboardCopy(bool cut)
    {
        if (BlockedByArchive(_active)) return;
        var sel = _active.GetSelection();
        if (sel.Count == 0) return;
        var sc = new StringCollection();
        sc.AddRange(sel.Select(s => s.FullPath).ToArray());
        var data = new DataObject();
        data.SetFileDropList(sc);
        // 2 = áthelyezés, 5 = másolás (Intéző-kompatibilis)
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes(cut ? 2 : 5)));
        try { Clipboard.SetDataObject(data, true); } catch { }
    }

    private void ClipboardPaste()
    {
        if (BlockedByArchive(_active)) return;
        try
        {
            if (!Clipboard.ContainsFileDropList()) return;
            var files = Clipboard.GetFileDropList().Cast<string>().ToList();
            if (files.Count == 0) return;
            bool move = false;
            if (Clipboard.GetData("Preferred DropEffect") is MemoryStream ms)
            {
                var b = new byte[4];
                if (ms.Read(b, 0, 4) == 4) move = (BitConverter.ToInt32(b, 0) & 2) != 0;
            }
            ProgressForm.Run(this, move ? OperationKind.Move : OperationKind.Copy, files, _active.CurrentPath);
            if (move) Clipboard.Clear();
            RefreshAll();
        }
        catch (Exception ex)
        {
            Msg.Show(this, ex.Message, "Beillesztés", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // =========================================================================
    //  Egyéb parancsok
    // =========================================================================

    private void RefreshAll()
    {
        _left.Reload();
        _right.Reload();
    }

    private void SwapPanels()
    {
        string l = _left.CurrentPath, r = _right.CurrentPath;
        _left.Navigate(r);
        _right.Navigate(l);
    }

    private void ToggleHidden()
    {
        _settings.ShowHidden = !_settings.ShowHidden;
        _left.ShowHidden = _right.ShowHidden = _settings.ShowHidden;
        _hiddenMenuItem.Checked = _hiddenButton.Checked = _settings.ShowHidden;
        RefreshAll();
    }

    private void DoSearch()
    {
        var f = new SearchForm(_active.RealDirectory);
        f.GoToRequested += path =>
        {
            if (path.EndsWith('\\'))
            {
                _active.Navigate(path.TrimEnd('\\'));
            }
            else
            {
                string? dir = Path.GetDirectoryName(path);
                if (dir != null) _active.Navigate(dir, Path.GetFileName(path));
            }
            Activate(_active, true);
            BringToFront();
        };
        f.Show(this);
    }

    // =========================================================================
    //  Beállítások
    // =========================================================================

    private ToolStripMenuItem RadioMenu(string title, (string Text, string Value)[] options, Func<string> get, Action<string> set)
    {
        var menu = new ToolStripMenuItem(title);
        foreach (var o in options)
        {
            var opt = o;
            var it = new ToolStripMenuItem(opt.Text) { Tag = opt.Value };
            it.Click += (_, _) =>
            {
                set(opt.Value);
                ApplySettings();
                _active.FocusList();
            };
            menu.DropDownItems.Add(it);
        }
        menu.DropDownOpening += (_, _) =>
        {
            string cur = get();
            foreach (ToolStripMenuItem it in menu.DropDownItems) it.Checked = (string?)it.Tag == cur;
        };
        return menu;
    }

    private ToolStripMenuItem CheckMenu(string title, string? keys, Func<bool> get, Action<bool> set)
    {
        var it = new ToolStripMenuItem(title) { ShortcutKeyDisplayString = keys, Checked = get() };
        it.Click += (_, _) =>
        {
            set(!get());
            ApplySettings();
            _active.FocusList();
        };
        it.Paint += (_, _) => { if (it.Checked != get()) it.Checked = get(); };
        return it;
    }

    private void ShowSettings()
    {
        using var f = new SettingsForm(_settings);
        if (f.ShowDialog(this) != DialogResult.OK) { _active.FocusList(); return; }
        var r = f.Result;
        _settings.ThemeName = r.ThemeName;
        _settings.FontSize = r.FontSize;
        _settings.RowSpacing = r.RowSpacing;
        _settings.SizeFormat = r.SizeFormat;
        _settings.ShowToolbar = r.ShowToolbar;
        _settings.ShowFunctionKeys = r.ShowFunctionKeys;
        _settings.ShowCommandLine = r.ShowCommandLine;
        _settings.ShowHidden = r.ShowHidden;
        _settings.RightClickMode = r.RightClickMode;
        _settings.IsoEnterAction = r.IsoEnterAction;
        _settings.ArchiveEnterAction = r.ArchiveEnterAction;
        _settings.ImageOpenMode = r.ImageOpenMode;
        _settings.UseRecycleBin = r.UseRecycleBin;
        _settings.ConfirmDelete = r.ConfirmDelete;
        _settings.RememberTabs = r.RememberTabs;
        _settings.ShowSplash = r.ShowSplash;
        _settings.Editor = string.IsNullOrWhiteSpace(r.Editor) ? "notepad.exe" : r.Editor;
        ApplySettings();
        _active.FocusList();
    }

    /// <summary>A beállítások alkalmazása a felületre, és mentés.</summary>
    private void ApplySettings()
    {
        SuspendLayout();
        _toolbar.Visible = _settings.ShowToolbar;
        _fkeyBar.Visible = _settings.ShowFunctionKeys;
        _cmdPanel.Visible = _settings.ShowCommandLine;

        bool hiddenChanged = _left.ShowHidden != _settings.ShowHidden;
        _left.ShowHidden = _right.ShowHidden = _settings.ShowHidden;
        _hiddenMenuItem.Checked = _hiddenButton.Checked = _settings.ShowHidden;

        foreach (var p in new[] { _left, _right }) p.ApplyViewOptions(_settings.FontSize, _settings.RowSpacing);

        var wanted = Theme.FromSetting(_settings.ThemeName);
        if (wanted != Theme.Current) Theme.Set(wanted);
        else ApplyTheme();

        if (hiddenChanged && _loaded) RefreshAll();
        ResumeLayout(true);
        _settings.Save();
    }

    private void SetEditor()
    {
        var e = InputDialog.Show(this, "Szerkesztő", "Az F4-gyel indított szerkesztő program:", _settings.Editor);
        if (!string.IsNullOrWhiteSpace(e)) _settings.Editor = e.Trim().Trim('"');
    }

    private void CmdBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter && !_cmdBox.DroppedDown)
        {
            RunCommand(_cmdBox.Text, e.Shift);
            _cmdBox.Text = "";
            e.Handled = e.SuppressKeyPress = true;
        }
    }

    private void RunCommand(string text, bool keepOpen)
    {
        text = text.Trim();
        if (text.Length == 0) { _active.ExecuteCursor(); return; }

        // előzmények
        int existing = _cmdBox.Items.IndexOf(text);
        if (existing >= 0) _cmdBox.Items.RemoveAt(existing);
        _cmdBox.Items.Insert(0, text);
        while (_cmdBox.Items.Count > 30) _cmdBox.Items.RemoveAt(_cmdBox.Items.Count - 1);

        string dir = _active.RealDirectory;

        // cd parancs (lemezképen belül is működik)
        string lower = text.ToLowerInvariant();
        if (lower == "cd.." || lower == "cd\\" || lower.StartsWith("cd "))
        {
            string arg = lower == "cd.." ? ".." : lower == "cd\\" ? "\\" : text[3..].Trim().Trim('"');
            arg = Environment.ExpandEnvironmentVariables(arg);
            if (arg == "..") { _active.GoUp(); _active.FocusList(); return; }
            string target = arg == "\\" ? (Path.GetPathRoot(dir) ?? dir) : Path.GetFullPath(Path.Combine(_active.CurrentPath, arg));
            _active.Navigate(target);
            _active.FocusList();
            return;
        }
        // meghajtóváltás: "d:"
        if (text.Length == 2 && text[1] == ':' && char.IsLetter(text[0]))
        {
            _active.Navigate(text + "\\");
            _active.FocusList();
            return;
        }

        if (keepOpen)
        {
            StartConsole(text, dir);
            return;
        }

        var (file, args) = SplitCommand(text);
        string resolved = ResolveInDir(file, dir) ?? file;
        try
        {
            Process.Start(new ProcessStartInfo(resolved, args) { UseShellExecute = true, WorkingDirectory = dir });
        }
        catch (Win32Exception)
        {
            // belső cmd parancs (dir, copy...) vagy ismeretlen: konzolban futtatjuk
            StartConsole(text, dir);
        }
        _active.FocusList();
    }

    private static void StartConsole(string command, string dir)
    {
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe", "/k " + command) { WorkingDirectory = dir, UseShellExecute = true });
        }
        catch (Exception ex) { Msg.Show(ex.Message); }
    }

    private static (string File, string Args) SplitCommand(string text)
    {
        if (text.StartsWith('"'))
        {
            int end = text.IndexOf('"', 1);
            if (end > 0) return (text[1..end], text[(end + 1)..].Trim());
        }
        int sp = text.IndexOf(' ');
        return sp < 0 ? (text, "") : (text[..sp], text[(sp + 1)..].Trim());
    }

    private static string? ResolveInDir(string file, string dir)
    {
        try
        {
            if (Path.IsPathRooted(file)) return null;
            string p = Path.Combine(dir, file);
            if (File.Exists(p) || Directory.Exists(p)) return p;
            foreach (var ext in new[] { ".exe", ".bat", ".cmd", ".com" })
                if (File.Exists(p + ext)) return p + ext;
        }
        catch { }
        return null;
    }

    // =========================================================================
    //  Felület felépítése
    // =========================================================================

    private TableLayoutPanel BuildFunctionKeyBar()
    {
        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = S(38),
            ColumnCount = 7,
            RowCount = 1,
            Padding = new Padding(S(6), S(3), S(6), S(5)),
        };
        var keys = new (string Key, string Text, Action Action)[]
        {
            ("F3", "Nézőke", DoView),
            ("F4", "Szerkesztés", DoEdit),
            ("F5", "Másolás", () => DoTransfer(false)),
            ("F6", "Áthelyezés", () => DoTransfer(true)),
            ("F7", "Új mappa", DoNewFolder),
            ("F8", "Törlés", () => DoDelete(false)),
            ("Alt+F4", "Kilépés", Close),
        };
        for (int i = 0; i < keys.Length; i++)
        {
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / keys.Length));
            var action = keys[i].Action;
            var b = new FKeyButton(keys[i].Key, keys[i].Text)
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(S(2), 0, S(2), 0),
            };
            b.Click += (_, _) => { action(); if (!IsDisposed) _active.FocusList(); };
            bar.Controls.Add(b, i, 0);
        }
        return bar;
    }

    private void BuildMenu(MenuStrip menu)
    {
        ToolStripMenuItem Item(string text, string? keys, Action a)
        {
            var it = new ToolStripMenuItem(text) { ShortcutKeyDisplayString = keys };
            it.Click += (_, _) => { a(); _active.FocusList(); };
            return it;
        }

        var file = new ToolStripMenuItem("&Fájl");
        file.DropDownItems.AddRange(new ToolStripItem[]
        {
            Item("Nézőke", "F3", DoView),
            Item("Szerkesztés", "F4", DoEdit),
            Item("Új szövegfájl...", "Shift+F4", DoNewTextFile),
            new ToolStripSeparator(),
            Item("Másolás / kibontás...", "F5", () => DoTransfer(false)),
            Item("Archívum kibontása...", "Alt+F9", DoExtractArchives),
            Item("Áthelyezés...", "F6", () => DoTransfer(true)),
            Item("Átnevezés...", "Shift+F6", DoRename),
            Item("Új mappa...", "F7", DoNewFolder),
            Item("Törlés (Lomtárba)", "F8 / Del", () => DoDelete(false)),
            Item("Végleges törlés", "Shift+Del", () => DoDelete(true)),
            new ToolStripSeparator(),
            Item("Elérési út másolása", null, DoCopyPath),
            Item("Tulajdonságok", "Alt+Enter", DoProperties),
            new ToolStripSeparator(),
            Item("Kilépés", "Alt+F4", Close),
        });

        var mark = new ToolStripMenuItem("&Kijelölés");
        mark.DropDownItems.AddRange(new ToolStripItem[]
        {
            Item("Csoport kijelölése...", "Num +", () =>
            {
                var p = InputDialog.Show(this, "Csoport kijelölése", "Minta (pl. *.txt;*.doc):", "*.*");
                if (p != null) _active.MarkByPattern(p, true);
            }),
            Item("Csoport kijelölésének megszüntetése...", "Num -", () =>
            {
                var p = InputDialog.Show(this, "Kijelölés megszüntetése", "Minta:", "*.*");
                if (p != null) _active.MarkByPattern(p, false);
            }),
            Item("Összes kijelölése", "Ctrl+A", () => _active.MarkAll(true)),
            Item("Kijelölés megszüntetése", "Ctrl+Shift+A", () => _active.MarkAll(false)),
            Item("Kijelölés megfordítása", "Num *", () => _active.InvertMarks()),
        });

        var commands = new ToolStripMenuItem("&Parancsok");
        commands.DropDownItems.AddRange(new ToolStripItem[]
        {
            Item("Fájlok keresése...", "Alt+F7", DoSearch),
            Item("Ugrás mappára...", "Ctrl+L", () => _active.AskGoTo()),
            Item("Vissza az előző mappába", "Alt+←", () => _active.GoBack()),
            new ToolStripSeparator(),
            Item("Új fül", "Ctrl+T", () => _active.NewTab()),
            Item("Fül bezárása", "Ctrl+W", () => _active.CloseTab()),
            Item("Következő fül", "Ctrl+Tab", () => _active.CycleTab(1)),
            Item("Mappa megnyitása új fülön", "Ctrl+↑", () =>
            {
                if (_active.CursorEntry is { IsDirectory: true, IsParent: false } d) _active.NewTab(d.FullPath);
            }),
            new ToolStripSeparator(),
            Item("Panelek cseréje", "Ctrl+U", SwapPanels),
            Item("A másik panel is ezt mutassa", null, () => Other.Navigate(_active.CurrentPath)),
            Item("Frissítés", "Ctrl+R", RefreshAll),
            new ToolStripSeparator(),
            Item("Archívum / lemezkép információ", null, ShowArchiveInfoForCursor),
            Item("Xbox ISO átalakítása GoD formátumra...", "Ctrl+G", DoConvertGod),
            Item("Parancssor ebben a mappában", null, DoCmdHere),
        });

        var view = new ToolStripMenuItem("&Nézet");
        var theme = new ToolStripMenuItem("Téma");
        theme.DropDownItems.AddRange(new ToolStripItem[] { _themeSystem, _themeLight, _themeDark });
        view.DropDownItems.Add(theme);
        view.DropDownItems.Add(Item("Sötét / világos váltás", "Ctrl+Shift+T", ToggleTheme));
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(RadioMenu("Betűméret", SettingsForm.FontOptions,
            () => _settings.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            v => _settings.FontSize = float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)));
        view.DropDownItems.Add(RadioMenu("Sortávolság", SettingsForm.RowOptions, () => _settings.RowSpacing, v => _settings.RowSpacing = v));
        view.DropDownItems.Add(RadioMenu("Fájlméretek", SettingsForm.SizeOptions, () => _settings.SizeFormat, v => _settings.SizeFormat = v));
        view.DropDownItems.Add(new ToolStripSeparator());
        view.DropDownItems.Add(CheckMenu("Eszköztár", null, () => _settings.ShowToolbar, v => _settings.ShowToolbar = v));
        view.DropDownItems.Add(CheckMenu("Funkcióbillentyű-sáv", null, () => _settings.ShowFunctionKeys, v => _settings.ShowFunctionKeys = v));
        view.DropDownItems.Add(CheckMenu("Parancssor", null, () => _settings.ShowCommandLine, v => _settings.ShowCommandLine = v));
        view.DropDownItems.Add(_hiddenMenuItem);

        var favorites = new ToolStripMenuItem("Ke&dvencek");
        favorites.DropDownItems.Add(new ToolStripMenuItem("(üres)"));
        favorites.DropDownOpening += (_, _) => FillFavorites(favorites.DropDownItems, _active);

        var options = new ToolStripMenuItem("&Beállítások");
        options.DropDownItems.Add(Item("Beállítások...", "Ctrl+O", ShowSettings));
        options.DropDownItems.Add(new ToolStripSeparator());
        options.DropDownItems.Add(RadioMenu("Jobb egérgomb", SettingsForm.RightClickOptions, () => _settings.RightClickMode, v => _settings.RightClickMode = v));
        options.DropDownItems.Add(RadioMenu("Enter egy .iso fájlon", SettingsForm.IsoOptions, () => _settings.IsoEnterAction, v => _settings.IsoEnterAction = v));
        options.DropDownItems.Add(RadioMenu("Enter egy archívumon", SettingsForm.ArchiveOptions, () => _settings.ArchiveEnterAction, v => _settings.ArchiveEnterAction = v));
        options.DropDownItems.Add(RadioMenu("Képek megnyitása", SettingsForm.ImageOptions, () => _settings.ImageOpenMode, v => _settings.ImageOpenMode = v));
        options.DropDownItems.Add(new ToolStripSeparator());
        options.DropDownItems.Add(CheckMenu("Törlés a Lomtárba", null, () => _settings.UseRecycleBin, v => _settings.UseRecycleBin = v));
        options.DropDownItems.Add(CheckMenu("Megerősítés Lomtárba helyezés előtt", null, () => _settings.ConfirmDelete, v => _settings.ConfirmDelete = v));
        options.DropDownItems.Add(CheckMenu("Fülek megjegyzése kilépéskor", null, () => _settings.RememberTabs, v => _settings.RememberTabs = v));
        options.DropDownItems.Add(CheckMenu("Indítóképernyő induláskor", null, () => _settings.ShowSplash, v => _settings.ShowSplash = v));
        options.DropDownItems.Add(new ToolStripSeparator());
        options.DropDownItems.Add(Item("Szerkesztő program (F4)...", null, SetEditor));
        options.DropDownItems.Add(Item("Beállítások mappájának megnyitása", null, () =>
        {
            try
            {
                Directory.CreateDirectory(Settings.FolderPath);
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Settings.FolderPath + "\"") { UseShellExecute = true });
            }
            catch { }
        }));

        var help = new ToolStripMenuItem("&Súgó");
        help.DropDownItems.Add(Item("Billentyűparancsok", "F1", ShowHelp));
        help.DropDownItems.Add(new ToolStripSeparator());
        help.DropDownItems.Add(Item("Névjegy", null, () =>
        {
            using var about = new AboutForm();
            about.ShowDialog(this);
        }));

        menu.Items.AddRange(new ToolStripItem[] { file, mark, commands, favorites, view, options, help });
    }

    // =========================================================================
    //  Kedvenc mappák
    // =========================================================================

    private void FillFavorites(ToolStripItemCollection items, FilePanel panel)
    {
        items.Clear();
        int n = 0;
        foreach (var f in _settings.Favorites)
        {
            var fav = f;
            n++;
            string prefix = n <= 9 ? "&" + n + "  " : n == 10 ? "&0  " : "    ";
            var it = new ToolStripMenuItem(prefix + fav.Name.Replace("&", "&&"))
            {
                ToolTipText = fav.Path,
                ShortcutKeyDisplayString = ShortenPath(fav.Path, 48),
                Checked = string.Equals(FilePanel.NormalizeDirSafe(fav.Path), panel.CurrentPath, StringComparison.OrdinalIgnoreCase),
            };
            it.Click += (_, _) =>
            {
                Activate(panel, true);
                panel.Navigate(Environment.ExpandEnvironmentVariables(fav.Path));
                panel.FocusList();
            };
            items.Add(it);
        }
        if (n == 0) items.Add(new ToolStripMenuItem("(még nincs kedvenc mappa)") { Enabled = false });
        items.Add(new ToolStripSeparator());
        var add = new ToolStripMenuItem("A jelenlegi mappa hozzáadása...") { ShortcutKeyDisplayString = panel.CurrentPath.Length > 0 ? "" : null };
        add.Click += (_, _) => AddFavorite(panel);
        items.Add(add);
        var edit = new ToolStripMenuItem("Kedvencek szerkesztése...");
        edit.Click += (_, _) => EditFavorites();
        items.Add(edit);
    }

    private static string ShortenPath(string p, int max) =>
        p.Length <= max ? p : p[..(max / 2 - 2)] + " … " + p[^(max / 2 - 2)..];

    private void ShowFavoritesMenu(FilePanel panel)
    {
        Activate(panel, true);
        var menu = new ContextMenuStrip { ShowItemToolTips = true };
        FillFavorites(menu.Items, panel);
        var pt = panel.PointToScreen(new Point(UiScale.S(20), UiScale.S(60)));
        menu.Show(pt);
    }

    private void AddFavorite(FilePanel panel)
    {
        string path = panel.CurrentPath;
        if (_settings.Favorites.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
        {
            Msg.Show(this, "Ez a mappa már a kedvencek között van:\n" + path, "Kedvencek",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string defName = Path.GetFileName(path.TrimEnd('\\'));
        if (defName.Length == 0) defName = path;
        var name = InputDialog.Show(this, "Hozzáadás a kedvencekhez", "Név:\n" + path, defName);
        if (string.IsNullOrWhiteSpace(name)) { panel.FocusList(); return; }
        _settings.Favorites.Add(new FavoriteDir { Name = name.Trim(), Path = path });
        _settings.Save();
        panel.FocusList();
    }

    private void EditFavorites()
    {
        using var f = new FavoritesForm(_settings.Favorites);
        if (f.ShowDialog(this) == DialogResult.OK)
        {
            _settings.Favorites = f.Result;
            _settings.Save();
        }
        _active.FocusList();
    }

    private void ShowHelp()
    {
        const string text =
            "FÜLEK ÉS KEDVENCEK\n" +
            "Ctrl+T – új fül,  Ctrl+W – fül bezárása,  Ctrl+Tab / Ctrl+Shift+Tab – fülváltás\n" +
            "Ctrl+↑ – a kurzor alatti mappa új fülön,  középső gomb a fülön – bezárás\n" +
            "Ctrl+D – kedvenc mappák (1–9 gyorsbillentyűvel), hozzáadás és szerkesztés\n\n" +
            "NAVIGÁCIÓ\n" +
            "Enter – megnyitás / belépés,  Backspace – szülőmappa,  Tab – panelváltás\n" +
            "Alt+F1 / Alt+F2 – meghajtóválasztás,  Alt+← – vissza,  Alt+↓ – előzmények\n" +
            "Ctrl+L – ugrás mappára,  Ctrl+→ / Ctrl+← – mappa a másik panelen\n\n" +
            "KIJELÖLÉS\n" +
            "Insert – jelöl és lép,  Szóköz – jelöl (mappánál méretet számol)\n" +
            "Num+ / Num− / Num* – minta szerint / megszüntet / megfordít\n" +
            "Ctrl+A – mind,  Ctrl+Shift+A – egyik sem\n\n" +
            "MŰVELETEK\n" +
            "F3 nézőke,  F4 szerkesztés,  Shift+F4 új fájl,  F5 másolás,  F6 áthelyezés\n" +
            "Shift+F6 átnevezés,  F7 új mappa,  F8/Del Lomtárba,  Shift+Del végleges törlés\n" +
            "Ctrl+C / X / V – vágólap,  Alt+Enter – tulajdonságok,  Alt+F7 – keresés\n" +
            "Ctrl+U – panelcsere,  Ctrl+R – frissítés,  Ctrl+H – rejtett fájlok\n" +
            "Ctrl+Shift+T – sötét / világos téma\n\n" +
            "ARCHÍVUMOK (.zip .rar .7z .lha .lzh .lzx)\n" +
            "AMIGA LEMEZKÉPEK (.adf .adz .dms .hdf) – OFS/FFS, partíciók, DMS-ből .adf is kibontható\n" +
            "Enter – belenézés, mint egy mappába,  F3 – fájl megtekintése,  F5 – kibontás\n" +
            "Alt+F9 – teljes archívum kibontása,  Alt+Enter – archívum adatai\n\n" +
            "LEMEZKÉPEK (.iso)\n" +
            "Enter – kérdés: böngészés (Xbox / Xbox 360) vagy csatolás meghajtóként\n" +
            "Ctrl+PgDn – azonnali böngészés kérdés nélkül\n" +
            "Ctrl+G – Xbox / Xbox 360 ISO átalakítása Games on Demand (GoD) formátumra\n" +
            "Az archívumok és lemezképek csak olvashatók.\n\n" +
            "KÉPNÉZEGETŐ (jpg, jfif, png, bmp, gif, tif, ico, tga, webp, heic)\n" +
            "Enter / F3 egy képen – megnyitás,  ← → / görgő / kattintás a szélén – lapozás\n" +
            "Ctrl+görgő / + − – nagyítás, 1 – eredeti méret, 0 – ablakhoz,  R – forgatás\n" +
            "F11 / dupla kattintás – teljes képernyő,  Esc – bezárás\n\n" +
            "EGÉR\n" +
            "Jobb gomb – kijelölés (húzva több fájlon is),  hosszan nyomva – helyi menü\n" +
            "Ctrl/Shift+bal gomb – kijelölés,  dupla kattintás – megnyitás\n\n" +
            "PARANCSSOR\n" +
            "A listában gépelt szöveg a parancssorba kerül. Enter = futtatás,\n" +
            "Shift+Enter = konzolban marad, Ctrl+Enter = fájlnév beszúrása,\n" +
            "„cd mappa”, „cd..”, „d:” – navigáció.";
        Msg.Show(this, text, "Billentyűparancsok", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>Funkcióbillentyű-gomb: kiemelt billentyűnév + felirat, fókuszt nem vesz el.</summary>
    private sealed class FKeyButton : Control
    {
        private readonly string _key;
        private readonly string _label;
        private bool _hover;
        private bool _down;
        private static readonly Font KeyFont = new("Segoe UI Semibold", 9F);

        public FKeyButton(string key, string label)
        {
            _key = key;
            _label = label;
            Text = key + " " + label;
            SetStyle(ControlStyles.Selectable, false);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;
            Font = new Font("Segoe UI", 9F);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? t.Surface);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color back = _down ? t.Selection : _hover ? t.ButtonHover : t.ButtonBack;
            using (var path = RoundRect(r, UiScale.S(5)))
            {
                using (var b = new SolidBrush(back)) g.FillPath(b, path);
                using (var p = new Pen(_hover ? t.Accent : t.Border)) g.DrawPath(p, path);
            }

            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            var keySize = TextRenderer.MeasureText(g, _key, KeyFont, Size.Empty, flags);
            var labelSize = TextRenderer.MeasureText(g, _label, Font, Size.Empty, flags);
            int gap = UiScale.S(6);
            int total = keySize.Width + gap + labelSize.Width;
            int x = Math.Max(UiScale.S(6), (Width - total) / 2);
            int y = (Height - Math.Max(keySize.Height, labelSize.Height)) / 2;
            TextRenderer.DrawText(g, _key, KeyFont, new Point(x, y), t.Accent, flags);
            if (x + keySize.Width + gap < Width)
            {
                var labelRect = new Rectangle(x + keySize.Width + gap, y, Width - (x + keySize.Width + gap) - UiScale.S(4), labelSize.Height);
                TextRenderer.DrawText(g, _label, Font, labelRect, t.Text, flags | TextFormatFlags.EndEllipsis);
            }
        }

        private static System.Drawing.Drawing2D.GraphicsPath RoundRect(Rectangle r, int radius)
        {
            var p = new System.Drawing.Drawing2D.GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
