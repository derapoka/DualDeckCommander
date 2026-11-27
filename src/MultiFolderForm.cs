using System.Text;
using System.Text.RegularExpressions;

namespace DualDeckCommander;

/// <summary>Szövegből (soronként egy név) mappalistát készít: tisztítás, hibás karakterek, ismétlődések.</summary>
internal static class FolderList
{
    internal sealed record Options(bool StripBullets, bool FixInvalid, bool SplitSemicolon, bool Numbering);

    internal sealed record Result(List<string> Folders, List<string> Problems);

    private static readonly Regex Bullet = new(@"^(?:[-*•·–—>]+|\(?\d{1,4}[.)]|[a-zA-Z][.)])\s+", RegexOptions.Compiled);
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };
    private const string InvalidChars = "<>:\"|?*";

    public static Result Parse(string text, Options o)
    {
        var folders = new List<string>();
        var problems = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var separators = o.SplitSemicolon ? new[] { '\n', ';', '\t' } : new[] { '\n' };
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split(separators))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            if (o.StripBullets) line = Bullet.Replace(line, "").Trim();
            if (line.Length == 0) continue;
            if (Regex.IsMatch(line, @"^([a-zA-Z]:[\\/]|\\\\)"))
            {
                problems.Add($"„{line}” – teljes útvonal; csak mappanevet adj meg");
                continue;
            }

            // a \ és / almappát jelent: „Játékok\Xbox 360” → két szint
            var parts = new List<string>();
            string? error = null;
            foreach (var seg in line.Split('\\', '/'))
            {
                string s = CleanSegment(seg, o.FixInvalid, out var segError);
                if (segError != null) { error = segError; break; }
                if (s.Length > 0) parts.Add(s);
            }
            if (error == null && parts.Count == 0) error = "üres név";
            if (error != null) { problems.Add($"„{raw.Trim()}” – {error}"); continue; }

            string path = string.Join("\\", parts);
            if (seen.Add(path)) folders.Add(path);
        }

        if (o.Numbering && folders.Count > 0)
        {
            int width = Math.Max(2, folders.Count.ToString().Length);
            for (int i = 0; i < folders.Count; i++)
                folders[i] = (i + 1).ToString().PadLeft(width, '0') + " - " + folders[i];
        }
        return new Result(folders, problems);
    }

    private static string CleanSegment(string seg, bool fixInvalid, out string? error)
    {
        error = null;
        var sb = new StringBuilder(seg.Length);
        if (seg.Trim() is "." or "..") { error = "érvénytelen név"; return ""; }
        foreach (char c in seg)
        {
            if (c == '\t') { sb.Append(' '); continue; }
            if (c < 32 || InvalidChars.IndexOf(c) >= 0)
            {
                if (!fixInvalid) { error = $"nem engedett karakter: {(c < 32 ? "vezérlőkarakter" : c.ToString())}"; return ""; }
                // a kettőspontot (pl. „Halo 3: ODST”) olvashatóbban cseréljük
                if (c == ':') { sb.Append(" -"); continue; }
                if (c < 32) continue;
                sb.Append('_');
            }
            else sb.Append(c);
        }
        // a Windows levágja a záró pontokat és szóközöket – mi is, hogy a név egyezzen
        string s = Regex.Replace(sb.ToString(), @"\s{2,}", " ").Trim().TrimEnd('.', ' ');
        if (s is "." or "..") { error = "érvénytelen név"; return ""; }
        string stem = s.Contains('.') ? s[..s.IndexOf('.')] : s;
        if (Reserved.Contains(stem.TrimEnd()))
        {
            if (!fixInvalid) { error = "a Windows által foglalt név"; return ""; }
            s = "_" + s;
        }
        if (s.Length > 240) { error = "túl hosszú név"; return ""; }
        return s;
    }
}

/// <summary>Több mappa létrehozása beillesztett szövegből: soronként egy mappa.</summary>
internal sealed class MultiFolderForm : Form
{
    private readonly string _baseDir;
    private readonly TextBox _text;
    private readonly CheckBox _bullets, _fix, _semicolon, _numbering;
    private readonly ListBox _preview;
    private readonly Label _status;
    private readonly Button _ok;
    private FolderList.Result _result = new(new(), new());

    /// <summary>A létrehozandó mappák (relatív útvonalak) – OK után érvényes.</summary>
    public IReadOnlyList<string> Folders => _result.Folders;

    public MultiFolderForm(string baseDir, Settings settings)
    {
        _baseDir = baseDir;
        Text = "Több mappa létrehozása";
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 9F);
        ClientSize = new Size(UiScale.S(820), UiScale.S(560));
        MinimumSize = new Size(UiScale.S(560), UiScale.S(400));
        Padding = new Padding(UiScale.S(12));
        KeyPreview = true;

        var top = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = UiScale.S(40),
            Text = $"Írd vagy illeszd be a mappaneveket, soronként egyet. Almappa: „Szülő\\Gyerek”.\n" +
                   $"Hely: {baseDir}",
            AutoEllipsis = true,
        };

        // ---- bal: szöveg, jobb: előnézet ----
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = UiScale.S(8),
        };
        _text = new TextBox
        {
            Multiline = true,
            AcceptsReturn = true,
            AcceptsTab = false,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 10F),
        };
        var leftHead = new Label { Text = "Nevek:", Dock = DockStyle.Top, Height = UiScale.S(20) };
        split.Panel1.Controls.Add(_text);
        split.Panel1.Controls.Add(leftHead);

        _preview = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true, SelectionMode = SelectionMode.None };
        var rightHead = new Label { Text = "Előnézet – ezek a mappák jönnek létre:", Dock = DockStyle.Top, Height = UiScale.S(20) };
        split.Panel2.Controls.Add(_preview);
        split.Panel2.Controls.Add(rightHead);

        // ---- beállítások ----
        CheckBox Opt(string t, bool v) => new() { Text = t, Checked = v, AutoSize = true, Margin = new Padding(0, 2, UiScale.S(18), 2) };
        _bullets = Opt("Felsorolásjelek és sorszámok levágása (- • 1. 2) …)", settings.MultiFolderStripBullets);
        _fix = Opt("Tiltott karakterek cseréje ( : ? * \" < > | )", settings.MultiFolderFixInvalid);
        _semicolon = Opt("Pontosvessző és tabulátor is elválaszt", settings.MultiFolderSplitSemicolon);
        _numbering = Opt("Sorszám a nevek elé (01 - …)", settings.MultiFolderNumbering);
        var opts = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(0, UiScale.S(8), 0, 0) };
        opts.Controls.AddRange(new Control[] { _bullets, _fix, _semicolon, _numbering });

        // ---- alsó sor: gombok ----
        var bottom = new TableLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 5, Padding = new Padding(0, UiScale.S(8), 0, 0) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var paste = new Button { Text = "Beillesztés", AutoSize = true };
        paste.Click += (_, _) =>
        {
            if (!Clipboard.ContainsText()) return;
            _text.SelectedText = Clipboard.GetText();
            _text.Focus();
        };
        var load = new Button { Text = "Betöltés fájlból...", AutoSize = true };
        load.Click += (_, _) =>
        {
            using var d = new OpenFileDialog { Filter = "Szövegfájl (*.txt;*.csv;*.lst)|*.txt;*.csv;*.lst|Minden fájl|*.*" };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            try { _text.Text = File.ReadAllText(d.FileName); }
            catch (Exception ex) { Msg.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        };
        _status = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(UiScale.S(12), 0, 0, 0) };
        _ok = new Button { Text = "Létrehozás", AutoSize = true, Enabled = false };
        var cancel = new Button { Text = "Mégse", AutoSize = true, DialogResult = DialogResult.Cancel };
        _ok.Click += (_, _) => { if (_result.Folders.Count > 0) { SaveOptions(settings); DialogResult = DialogResult.OK; } };
        bottom.Controls.Add(paste, 0, 0);
        bottom.Controls.Add(load, 1, 0);
        bottom.Controls.Add(_status, 2, 0);
        bottom.Controls.Add(_ok, 3, 0);
        bottom.Controls.Add(cancel, 4, 0);

        Controls.Add(split);
        Controls.Add(top);
        Controls.Add(opts);
        Controls.Add(bottom);
        CancelButton = cancel;

        _text.TextChanged += (_, _) => UpdatePreview();
        foreach (var c in new[] { _bullets, _fix, _semicolon, _numbering }) c.CheckedChanged += (_, _) => UpdatePreview();

        // Ctrl+Enter: létrehozás (az Enter a szövegben új sor)
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && e.Control && _ok.Enabled) { e.SuppressKeyPress = true; _ok.PerformClick(); }
        };
        Shown += (_, _) =>
        {
            split.SplitterDistance = split.Width / 2;
            // ha a vágólapon többsoros szöveg van, rögtön betöltjük
            try
            {
                if (Clipboard.ContainsText())
                {
                    var t = Clipboard.GetText();
                    if (t.Contains('\n') && t.Length < 200_000) _text.Text = t;
                }
            }
            catch { }
            _text.Focus();
            _text.SelectionStart = _text.TextLength;
        };
        ThemeHelper.Apply(this);
        UpdatePreview();
    }

    private void SaveOptions(Settings s)
    {
        s.MultiFolderStripBullets = _bullets.Checked;
        s.MultiFolderFixInvalid = _fix.Checked;
        s.MultiFolderSplitSemicolon = _semicolon.Checked;
        s.MultiFolderNumbering = _numbering.Checked;
    }

    private void UpdatePreview()
    {
        _result = FolderList.Parse(_text.Text,
            new FolderList.Options(_bullets.Checked, _fix.Checked, _semicolon.Checked, _numbering.Checked));

        int existing = 0;
        _preview.BeginUpdate();
        _preview.Items.Clear();
        foreach (var f in _result.Folders)
        {
            bool exists = Directory.Exists(Path.Combine(_baseDir, f));
            if (exists) existing++;
            _preview.Items.Add(exists ? f + "   (már létezik)" : f);
        }
        foreach (var p in _result.Problems) _preview.Items.Add("⚠ kimarad: " + p);
        _preview.EndUpdate();

        int toCreate = _result.Folders.Count - existing;
        var sb = new StringBuilder($"{toCreate} új mappa");
        if (existing > 0) sb.Append($", {existing} már létezik");
        if (_result.Problems.Count > 0) sb.Append($", {_result.Problems.Count} hibás sor kimarad");
        _status.Text = _result.Folders.Count == 0 && _result.Problems.Count == 0 ? "Nincs még név megadva." : sb.ToString();
        _ok.Enabled = toCreate > 0;
        _ok.Text = toCreate > 0 ? $"Létrehozás ({toCreate})" : "Létrehozás";
    }
}
