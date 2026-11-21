namespace DualDeckCommander;

/// <summary>Egy átalakítandó lemezkép adatai (a párbeszédablak előtt olvassuk be).</summary>
internal sealed record GodSource(string Path, string Name, string? Title, uint TitleId, bool Xbox360, string Platform);

/// <summary>„Átalakítás Games on Demand formátumra” párbeszédablak.</summary>
internal sealed class GodForm : Form
{
    private readonly TextBox _dest;
    private readonly TextBox? _title;
    private readonly CheckBox _trim;
    private readonly CheckBox _show;

    public string Destination => _dest.Text.Trim().Trim('"');
    /// <summary>Egy lemezképnél a megadott játékcím (üres = nincs cím a fejlécben).</summary>
    public string? GameTitle => _title?.Text.Trim();
    public bool Trim => _trim.Checked;
    public bool ShowResult => _show.Checked;

    public GodForm(IReadOnlyList<GodSource> sources, string defaultDest, bool trim, bool showResult)
    {
        Text = "Átalakítás Games on Demand (GoD) formátumra";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Font;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(14);

        int w = UiScale.S(560);
        var layout = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        Label Lbl(string t, bool bold = false, Padding? m = null) => new()
        {
            Text = t,
            AutoSize = true,
            MaximumSize = new Size(w, 0),
            Margin = m ?? new Padding(0, 0, 0, 4),
            Font = bold ? new Font(Font, FontStyle.Bold) : Font,
        };
        void Full(Control c) { layout.Controls.Add(c); layout.SetColumnSpan(c, 2); }

        // ---- forrás ----
        if (sources.Count == 1)
        {
            var s = sources[0];
            Full(Lbl(s.Name, true));
            Full(Lbl($"{s.Platform}   •   Title ID: {s.TitleId:X8}" + (s.Title != null ? $"   •   {s.Title}" : ""),
                m: new Padding(0, 0, 0, 12)));
        }
        else
        {
            Full(Lbl($"{sources.Count} lemezkép átalakítása:", true));
            string list = string.Join("\n", sources.Take(8).Select(s => "•  " + s.Name + (s.Title != null ? $"  –  {s.Title}" : "")));
            if (sources.Count > 8) list += $"\n…és még {sources.Count - 8}";
            Full(Lbl(list, m: new Padding(0, 0, 0, 12)));
        }

        // ---- célmappa ----
        Full(Lbl("Célmappa:"));
        _dest = new TextBox { Text = defaultDest, Width = w - UiScale.S(100), Margin = new Padding(0, 0, 6, 10) };
        var browse = new Button { Text = "Tallózás...", AutoSize = true, Margin = new Padding(0, 0, 0, 10) };
        browse.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog
            {
                Description = "Hová kerüljön a GoD kimenet?",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true,
            };
            if (Directory.Exists(Destination)) d.SelectedPath = Destination;
            if (d.ShowDialog(this) == DialogResult.OK) _dest.Text = d.SelectedPath;
        };
        layout.Controls.Add(_dest);
        layout.Controls.Add(browse);

        // ---- játékcím ----
        if (sources.Count == 1)
        {
            Full(Lbl("Játék neve (ez látszik a konzol menüjében):"));
            _title = new TextBox { Text = sources[0].Title ?? "", Width = w - UiScale.S(100), Margin = new Padding(0, 0, 0, 10) };
            Full(_title);
        }
        else
        {
            Full(Lbl("A játékok neve a beépített címadatbázisból kerül a fejlécbe.", m: new Padding(0, 0, 0, 10)));
        }

        // ---- beállítások ----
        _trim = new CheckBox
        {
            Text = "Üres terület levágása a lemezkép végéről (kisebb kimenet)",
            Checked = trim, AutoSize = true, Margin = new Padding(0, 0, 0, 2),
        };
        _show = new CheckBox
        {
            Text = "Kész után a kimenet megnyitása a másik panelen",
            Checked = showResult, AutoSize = true, Margin = new Padding(0, 0, 0, 12),
        };
        Full(_trim);
        Full(_show);

        var info = Lbl(
            "A kimenet szerkezete:  <cél>\\<Title ID>\\00007000\\…  (eredeti Xbox játéknál 00005000).\n" +
            "A Title ID mappát másold a konzol merevlemezén vagy USB-meghajtóján ide:\n" +
            "Content\\0000000000000000\\   – a játék a „Játékok” között jelenik meg.\n" +
            "Az eredeti lemezkép nem változik. Szükséges hely: kb. a lemezkép mérete.",
            m: new Padding(0, 0, 0, 14));
        info.ForeColor = Theme.Current.TextDim;
        Full(info);

        // ---- gombok ----
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty,
        };
        var cancel = new Button { Text = "Mégse", DialogResult = DialogResult.Cancel, AutoSize = true };
        var ok = new Button { Text = "Átalakítás", AutoSize = true };
        ok.Click += (_, _) =>
        {
            if (Destination.Length == 0)
            {
                Msg.Show(this, "Add meg a célmappát.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DialogResult = DialogResult.OK;
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        Full(buttons);

        Controls.Add(layout);
        AcceptButton = ok;
        CancelButton = cancel;
        Shown += (_, _) => { if (_title != null) { _title.Focus(); _title.SelectAll(); } else ok.Focus(); };
        ThemeHelper.Apply(this);
        info.ForeColor = Theme.Current.TextDim;
    }
}
