using System.IO.Enumeration;
using System.Text;

namespace DualDeckCommander;

/// <summary>Fájlkeresés (Alt+F7): név minta és opcionálisan tartalom alapján.</summary>
internal sealed class SearchForm : Form
{
    private readonly TextBox _where;
    private readonly TextBox _pattern;
    private readonly TextBox _content;
    private readonly CheckBox _subdirs;
    private readonly CheckBox _caseSensitive;
    private readonly Button _start;
    private readonly Button _goTo;
    private readonly ListBox _results;
    private readonly Label _status;
    private CancellationTokenSource? _cts;

    /// <summary>A kiválasztott találat: a főablak ide navigál.</summary>
    public event Action<string>? GoToRequested;

    public SearchForm(string startDir)
    {
        Text = "Fájlok keresése";
        Font = new Font("Segoe UI", 9F);
        StartPosition = FormStartPosition.CenterParent;
        Size = UiScale.S(760, 560);
        MinimumSize = UiScale.S(500, 400);
        ShowIcon = false;
        ShowInTaskbar = false;

        var grid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, AutoSize = true, Padding = new Padding(8) };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _where = new TextBox { Text = startDir, Dock = DockStyle.Fill };
        _pattern = new TextBox { Text = "*.*", Dock = DockStyle.Fill };
        _content = new TextBox { Dock = DockStyle.Fill };
        _subdirs = new CheckBox { Text = "Almappákban is", Checked = true, AutoSize = true };
        _caseSensitive = new CheckBox { Text = "Kis- és nagybetű számít", AutoSize = true };

        void Row(string label, Control c)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 8, 0) });
            grid.Controls.Add(c);
        }
        Row("Keresés helye:", _where);
        Row("Fájlnév (pl. *.txt;*.doc):", _pattern);
        Row("Tartalmazza a szöveget:", _content);
        var opts = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        opts.Controls.Add(_subdirs);
        opts.Controls.Add(_caseSensitive);
        grid.Controls.Add(new Label());
        grid.Controls.Add(opts);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8, 0, 8, 4) };
        _start = new Button { Text = "Keresés indítása", AutoSize = true };
        _goTo = new Button { Text = "Ugrás a fájlhoz", AutoSize = true, Enabled = false };
        var close = new Button { Text = "Bezárás", AutoSize = true };
        _start.Click += (_, _) => { if (_cts != null) _cts.Cancel(); else StartSearch(); };
        _goTo.Click += (_, _) => GoToSelected();
        close.Click += (_, _) => Close();
        buttons.Controls.Add(_start);
        buttons.Controls.Add(_goTo);
        buttons.Controls.Add(close);

        _results = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, HorizontalScrollbar = true };
        _results.DoubleClick += (_, _) => GoToSelected();
        _results.SelectedIndexChanged += (_, _) => _goTo.Enabled = _results.SelectedIndex >= 0;
        _results.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) GoToSelected(); };

        _status = new Label { Dock = DockStyle.Bottom, Height = UiScale.S(22), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Padding = new Padding(8, 0, 0, 0) };

        Controls.Add(_results);
        Controls.Add(_status);
        Controls.Add(buttons);
        Controls.Add(grid);
        AcceptButton = _start;
        _status.Tag = "dim";
        ThemeHelper.Apply(this);
    }

    private void StartSearch()
    {
        string root = _where.Text.Trim().Trim('"');
        if (!Directory.Exists(root))
        {
            Msg.Show(this, "A mappa nem létezik.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var patterns = _pattern.Text.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim() == "*.*" ? "*" : p.Trim()).Where(p => p.Length > 0).ToArray();
        if (patterns.Length == 0) patterns = new[] { "*" };
        string text = _content.Text;
        bool recurse = _subdirs.Checked;
        var cmp = _caseSensitive.Checked ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        _results.Items.Clear();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _start.Text = "Leállítás";
        _status.Text = "Keresés...";

        int found = 0;
        var batch = new List<string>();
        var lastFlush = DateTime.UtcNow;
        string currentDir = root;

        void Flush()
        {
            if (batch.Count == 0) return;
            var items = batch.ToArray();
            batch.Clear();
            BeginInvoke(() =>
            {
                _results.BeginUpdate();
                _results.Items.AddRange(items);
                _results.EndUpdate();
                _status.Text = $"{_results.Items.Count} találat   •   {currentDir}";
            });
        }

        Task.Run(() =>
        {
            var stack = new Stack<string>();
            stack.Push(root);
            var opts = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = true };
            while (stack.Count > 0 && !ct.IsCancellationRequested)
            {
                currentDir = stack.Pop();
                IEnumerable<FileSystemInfo> entries;
                try { entries = new DirectoryInfo(currentDir).EnumerateFileSystemInfos("*", opts).ToList(); }
                catch { continue; }

                foreach (var e in entries)
                {
                    if (ct.IsCancellationRequested) break;
                    bool isDir = (e.Attributes & FileAttributes.Directory) != 0;
                    if (isDir && recurse && (e.Attributes & FileAttributes.ReparsePoint) == 0)
                        stack.Push(e.FullName);

                    if (!patterns.Any(p => FileSystemName.MatchesSimpleExpression(p, e.Name, ignoreCase: true)))
                        continue;
                    if (text.Length > 0 && (isDir || !FileContains(e.FullName, text, cmp, ct)))
                        continue;

                    found++;
                    batch.Add(isDir ? e.FullName + "\\" : e.FullName);
                    if ((DateTime.UtcNow - lastFlush).TotalMilliseconds > 200)
                    {
                        Flush();
                        lastFlush = DateTime.UtcNow;
                    }
                }
            }
        }, ct).ContinueWith(_ =>
        {
            if (IsDisposed) return;
            Flush();
            BeginInvoke(() =>
            {
                _status.Text = (ct.IsCancellationRequested ? "Megszakítva. " : "Kész. ") + $"{found} találat.";
                _start.Text = "Keresés indítása";
                _cts?.Dispose();
                _cts = null;
            });
        });
    }

    private static bool FileContains(string path, string text, StringComparison cmp, CancellationToken ct)
    {
        try
        {
            // soronként olvasunk, átfedéssel a blokkhatárok miatt
            using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var buf = new char[64 * 1024];
            string carry = "";
            int read;
            while ((read = reader.Read(buf, 0, buf.Length)) > 0)
            {
                if (ct.IsCancellationRequested) return false;
                string chunk = carry + new string(buf, 0, read);
                if (chunk.Contains(text, cmp)) return true;
                carry = chunk.Length > text.Length ? chunk[^text.Length..] : chunk;
            }
        }
        catch
        {
            // olvashatatlan fájl
        }
        return false;
    }

    private void GoToSelected()
    {
        if (_results.SelectedItem is string path) GoToRequested?.Invoke(path);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _cts?.Cancel();
        base.OnFormClosing(e);
    }
}
