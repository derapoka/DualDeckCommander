using System.Diagnostics;

namespace DualDeckCommander;

/// <summary>Beállítások ablak: kategóriák bal oldalt, beállítások jobb oldalt.</summary>
internal sealed class SettingsForm : Form
{
    private readonly Settings _work;
    private readonly ListBox _categories;
    private readonly Panel _pageHost;
    private readonly List<(string Name, Control Page)> _pages = new();
    private readonly List<Action> _loaders = new();

    /// <summary>A módosított beállítások (OK után).</summary>
    public Settings Result => _work;

    private static int S(int v) => UiScale.S(v);

    public static readonly (string Text, string Value)[] ThemeOptions =
        { ("A Windows beállítása szerint", "system"), ("Világos", "light"), ("Sötét", "dark") };
    public static readonly (string Text, string Value)[] FontOptions =
        { ("Kicsi (8 pont)", "8"), ("Normál (9 pont)", "9"), ("Nagy (10,5 pont)", "10.5"), ("Nagyon nagy (12 pont)", "12") };
    public static readonly (string Text, string Value)[] RowOptions =
        { ("Tömör", "compact"), ("Normál", "normal"), ("Tágas", "roomy") };
    public static readonly (string Text, string Value)[] SizeOptions =
        { ("Bájtban (1 234 567)", "bytes"), ("Rövidítve (1,2 MB)", "short") };
    public static readonly (string Text, string Value)[] RightClickOptions =
        { ("Kijelölés (Total Commander-stílus)", "select"), ("Helyi menü (Windows-stílus)", "menu") };
    public static readonly (string Text, string Value)[] IsoOptions =
        { ("Kérdezzen rá", "ask"), ("Böngészés (Xbox lemezképnél)", "browse"), ("Csatolás meghajtóként", "mount") };
    public static readonly (string Text, string Value)[] ImageOptions =
        { ("Beépített képnézegető", "internal"), ("A Windows társított programja", "shell") };
    public static readonly (string Text, string Value)[] ArchiveOptions =
        { ("Belenézés (mint egy mappába)", "browse"), ("Megnyitás a társított programmal", "shell") };

    public SettingsForm(Settings current)
    {
        _work = current.Clone();
        Text = "Beállítások";
        Font = new Font("Segoe UI", 9.5F);
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = UiScale.S(780, 500);
        MinimumSize = UiScale.S(640, 440);
        ShowIcon = false;
        ShowInTaskbar = false;
        MinimizeBox = false;
        KeyPreview = true;

        _categories = new ListBox
        {
            Dock = DockStyle.Left,
            Width = S(170),
            IntegralHeight = false,
            BorderStyle = BorderStyle.None,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = S(34),
        };
        _categories.DrawItem += DrawCategory;
        _categories.SelectedIndexChanged += (_, _) => ShowPage(_categories.SelectedIndex);

        _pageHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(S(24), S(16), S(24), S(8)), AutoScroll = true };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = S(52),
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(S(12), S(10), S(12), S(8)),
        };
        var cancel = new Button { Text = "Mégse", DialogResult = DialogResult.Cancel, Size = UiScale.S(100, 32) };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Size = UiScale.S(100, 32) };
        var reset = new Button { Text = "Alapértékek", Size = UiScale.S(120, 32), Margin = new Padding(S(3), S(3), S(220), S(3)) };
        reset.Click += (_, _) =>
        {
            if (Msg.Show(this, "Minden beállítás visszaáll az alapértékre?\n(A kedvencek, a fülek és az útvonalak megmaradnak.)",
                    "Alapértékek", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _work.ResetOptions();
                foreach (var l in _loaders) l();
            }
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        buttons.Controls.Add(reset);
        AcceptButton = ok;
        CancelButton = cancel;

        BuildPages();

        Controls.Add(_pageHost);
        Controls.Add(_categories);
        Controls.Add(buttons);

        foreach (var (name, _) in _pages) _categories.Items.Add(name);
        _categories.SelectedIndex = 0;

        ThemeHelper.Apply(this);
        var t = Theme.Current;
        _categories.BackColor = t.SurfaceAlt;
        _pageHost.BackColor = t.Window;
        foreach (var (_, page) in _pages)
        {
            ThemeHelper.Apply(page); // a nem látható oldalakat is
            StylePage(page, t);
        }
    }

    private static void StylePage(Control page, Theme t)
    {
        page.BackColor = t.Window;
        foreach (Control c in page.Controls)
        {
            if (c is Label { Tag: "heading" } h) { h.ForeColor = t.Text; h.BackColor = t.Window; }
            else if (c is Label { Tag: "dim" } d) { d.ForeColor = t.TextDim; d.BackColor = t.Window; }
            else if (c is Label or CheckBox) c.BackColor = t.Window;
            else if (c is TableLayoutPanel or FlowLayoutPanel) StylePage(c, t);
        }
    }

    private void DrawCategory(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var t = Theme.Current;
        bool sel = (e.State & DrawItemState.Selected) != 0;
        using (var b = new SolidBrush(sel ? t.Window : t.SurfaceAlt)) e.Graphics.FillRectangle(b, e.Bounds);
        if (sel)
        {
            using var a = new SolidBrush(t.Accent);
            e.Graphics.FillRectangle(a, e.Bounds.X, e.Bounds.Y + S(6), S(3), e.Bounds.Height - S(12));
        }
        var r = e.Bounds;
        r.X += S(16);
        r.Width -= S(16);
        using var f = new Font("Segoe UI", 10F, sel ? FontStyle.Bold : FontStyle.Regular);
        TextRenderer.DrawText(e.Graphics, _categories.Items[e.Index].ToString(), f, r, sel ? t.Text : t.TextDim,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);
    }

    private void ShowPage(int index)
    {
        if (index < 0 || index >= _pages.Count) return;
        _pageHost.SuspendLayout();
        _pageHost.Controls.Clear();
        _pageHost.Controls.Add(_pages[index].Page);
        _pageHost.ResumeLayout();
    }

    // =========================================================================
    //  Oldalak
    // =========================================================================

    private TableLayoutPanel NewPage(string name, string heading)
    {
        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            Padding = new Padding(0),
        };
        page.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        page.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var h = new Label
        {
            Text = heading,
            Tag = "heading",
            AutoSize = true,
            Font = new Font("Segoe UI", 13F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, S(14)),
        };
        page.Controls.Add(h);
        page.SetColumnSpan(h, 2);
        _pages.Add((name, page));
        return page;
    }

    private void Choice(TableLayoutPanel page, string label, (string Text, string Value)[] options,
        Func<string> get, Action<string> set)
    {
        var l = new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, S(6), S(16), S(6)) };
        var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(300), Margin = new Padding(0, S(3), 0, S(3)) };
        foreach (var o in options) cb.Items.Add(o.Text);
        void Load()
        {
            int i = Array.FindIndex(options, o => o.Value == get());
            cb.SelectedIndex = i >= 0 ? i : 0;
        }
        Load();
        _loaders.Add(Load);
        cb.SelectedIndexChanged += (_, _) => { if (cb.SelectedIndex >= 0) set(options[cb.SelectedIndex].Value); };
        page.Controls.Add(l);
        page.Controls.Add(cb);
    }

    private void Check(TableLayoutPanel page, string label, Func<bool> get, Action<bool> set, string? hint = null)
    {
        var c = new CheckBox { Text = label, AutoSize = true, Margin = new Padding(0, S(6), 0, hint == null ? S(4) : 0) };
        void Load() => c.Checked = get();
        Load();
        _loaders.Add(Load);
        c.CheckedChanged += (_, _) => set(c.Checked);
        page.Controls.Add(c);
        page.SetColumnSpan(c, 2);
        if (hint != null) Hint(page, hint);
    }

    private void Hint(TableLayoutPanel page, string text)
    {
        var h = new Label
        {
            Text = text,
            Tag = "dim",
            AutoSize = true,
            MaximumSize = new Size(S(500), 0),
            Margin = new Padding(S(20), 0, 0, S(6)),
        };
        page.Controls.Add(h);
        page.SetColumnSpan(h, 2);
    }

    private void Spacer(TableLayoutPanel page)
    {
        var s = new Label { Height = S(10), AutoSize = false, Width = 1 };
        page.Controls.Add(s);
        page.SetColumnSpan(s, 2);
    }

    private void BuildPages()
    {
        var w = _work;

        // ---- Megjelenés ----
        var look = NewPage("Megjelenés", "Megjelenés");
        Choice(look, "Téma:", ThemeOptions, () => w.ThemeName, v => w.ThemeName = v);
        Choice(look, "Betűméret a listákban:", FontOptions,
            () => w.FontSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            v => w.FontSize = float.Parse(v, System.Globalization.CultureInfo.InvariantCulture));
        Choice(look, "Sortávolság:", RowOptions, () => w.RowSpacing, v => w.RowSpacing = v);
        Choice(look, "Fájlméretek:", SizeOptions, () => w.SizeFormat, v => w.SizeFormat = v);
        Spacer(look);
        Check(look, "Eszköztár megjelenítése", () => w.ShowToolbar, v => w.ShowToolbar = v);
        Check(look, "Funkcióbillentyű-sáv megjelenítése (F3 … F8)", () => w.ShowFunctionKeys, v => w.ShowFunctionKeys = v);
        Check(look, "Parancssor megjelenítése", () => w.ShowCommandLine, v => w.ShowCommandLine = v);
        Check(look, "Rejtett és rendszerfájlok mutatása", () => w.ShowHidden, v => w.ShowHidden = v);

        // ---- Működés ----
        var work = NewPage("Működés", "Működés");
        Choice(work, "Jobb egérgomb:", RightClickOptions, () => w.RightClickMode, v => w.RightClickMode = v);
        Choice(work, "Enter egy .iso fájlon:", IsoOptions, () => w.IsoEnterAction, v => w.IsoEnterAction = v);
        Choice(work, "Enter egy archívumon:", ArchiveOptions, () => w.ArchiveEnterAction, v => w.ArchiveEnterAction = v);
        Choice(work, "Képek megnyitása:", ImageOptions, () => w.ImageOpenMode, v => w.ImageOpenMode = v);
        Spacer(work);
        Check(work, "Törlés a Lomtárba", () => w.UseRecycleBin, v => w.UseRecycleBin = v,
            "Kikapcsolva az F8 / Del véglegesen töröl. A Shift+Del mindig véglegesen töröl.");
        Check(work, "Megerősítés kérése Lomtárba helyezés előtt", () => w.ConfirmDelete, v => w.ConfirmDelete = v,
            "A végleges törlés mindig rákérdez.");
        Check(work, "Új mappánál (F7) kérdezzen: egy vagy több mappa", () => w.NewFolderAsk, v => w.NewFolderAsk = v,
            "Kikapcsolva az F7 egy mappát kér; több mappa: Shift+F7.");
        Check(work, "Fülek megjegyzése kilépéskor", () => w.RememberTabs, v => w.RememberTabs = v);
        Check(work, "Indítóképernyő megjelenítése induláskor", () => w.ShowSplash, v => w.ShowSplash = v);

        // ---- Programok ----
        var progs = NewPage("Programok", "Programok");
        var l = new Label { Text = "Szerkesztő (F4):", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, S(8), S(16), S(6)) };
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        var editor = new TextBox { Width = S(300), Margin = new Padding(0, S(4), S(6), S(4)) };
        var browse = new Button { Text = "Tallózás...", AutoSize = true, Margin = new Padding(0, S(2), 0, S(2)) };
        void LoadEditor() => editor.Text = w.Editor;
        LoadEditor();
        _loaders.Add(LoadEditor);
        editor.TextChanged += (_, _) => w.Editor = editor.Text.Trim().Trim('"');
        browse.Click += (_, _) =>
        {
            using var dlg = new OpenFileDialog { Filter = "Programok (*.exe)|*.exe|Minden fájl (*.*)|*.*", Title = "Szerkesztő kiválasztása" };
            if (dlg.ShowDialog(this) == DialogResult.OK) editor.Text = dlg.FileName;
        };
        row.Controls.Add(editor);
        row.Controls.Add(browse);
        progs.Controls.Add(l);
        progs.Controls.Add(row);
        Hint(progs, "Alapértelmezés: notepad.exe. Bármilyen program megadható, pl. Notepad++ vagy VS Code.");
        Spacer(progs);

        var folderLabel = new Label { Text = "Beállítások helye:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, S(8), S(16), S(6)) };
        var openFolder = new Button { Text = "Mappa megnyitása", AutoSize = true, Anchor = AnchorStyles.Left };
        openFolder.Click += (_, _) =>
        {
            try
            {
                Directory.CreateDirectory(Settings.FolderPath);
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + Settings.FolderPath + "\"") { UseShellExecute = true });
            }
            catch { }
        };
        progs.Controls.Add(folderLabel);
        progs.Controls.Add(openFolder);
        Hint(progs, Settings.FolderPath);
    }
}
