using System.Text;

namespace DualDeckCommander;

/// <summary>Nézőke (F3): szöveg, hexa és kép megjelenítés.</summary>
internal sealed class ViewerForm : Form
{
    private const int MaxTextBytes = 16 * 1024 * 1024;
    private const int MaxHexBytes = 2 * 1024 * 1024;

    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".tif", ".tiff" };

    private readonly string _path;
    private readonly Func<Stream> _open;
    private readonly TextBox _text;
    private readonly PictureBox _picture;
    private readonly StatusStrip _statusBar;
    private readonly ToolStripStatusLabel _statusLabel;
    private string _lastSearch = "";

    public ViewerForm(string path)
        : this(path, () => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
    {
    }

    /// <summary>Tetszőleges adatfolyam megjelenítése (pl. fájl egy Xbox lemezképen belül).</summary>
    public ViewerForm(string displayPath, Func<Stream> open)
    {
        _path = displayPath;
        _open = open;
        Text = "Nézőke – " + displayPath;
        Font = new Font("Segoe UI", 9F);
        StartPosition = FormStartPosition.CenterParent;
        Size = UiScale.S(1000, 700);
        KeyPreview = true;
        ShowIcon = false;

        _text = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 10F),
            BackColor = SystemColors.Window,
            MaxLength = 0,
            HideSelection = false,
        };
        _picture = new PictureBox
        {
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.FromArgb(45, 45, 45),
            Visible = false,
        };
        _statusLabel = new ToolStripStatusLabel { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _statusBar = new StatusStrip();
        _statusBar.Items.Add(_statusLabel);

        Controls.Add(_text);
        Controls.Add(_picture);
        Controls.Add(_statusBar);

        ThemeHelper.Apply(this);
        _text.BackColor = Theme.Current.Window;
        _picture.BackColor = Theme.Current.IsDark ? Color.FromArgb(24, 24, 26) : Color.FromArgb(45, 45, 45);

        if (ImageExtensions.Contains(Path.GetExtension(displayPath))) ShowImage();
        else ShowText();
    }

    private void SetStatus(string mode) =>
        _statusLabel.Text = $"{mode}   •   1 = szöveg, 3 = hexa, 4 = kép, W = sortörés, F7 = keresés, F3/N = következő, Esc = bezárás";

    private void ShowText()
    {
        _picture.Visible = false;
        _text.Visible = true;
        try
        {
            byte[] data = ReadHead(MaxTextBytes, out bool truncated);
            var enc = DetectEncoding(data, out int bomLength);
            string content = enc.GetString(data, bomLength, data.Length - bomLength).Replace("\0", " ");
            // A TextBox csak CRLF sortörést jelenít meg helyesen
            content = content.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
            if (truncated) content += "\r\n\r\n[... a fájl hosszabb, csak az első 16 MB látszik ...]";
            _text.Text = content;
            SetStatus("Szöveg (" + enc.WebName + ")");
        }
        catch (Exception ex)
        {
            _text.Text = "Nem olvasható: " + ex.Message;
        }
    }

    private void ShowHex()
    {
        _picture.Visible = false;
        _text.Visible = true;
        try
        {
            byte[] data = ReadHead(MaxHexBytes, out bool truncated);
            var sb = new StringBuilder(data.Length * 4 + 64);
            for (int off = 0; off < data.Length; off += 16)
            {
                sb.Append(off.ToString("X8")).Append("  ");
                for (int i = 0; i < 16; i++)
                {
                    if (off + i < data.Length) sb.Append(data[off + i].ToString("X2")).Append(' ');
                    else sb.Append("   ");
                    if (i == 7) sb.Append(' ');
                }
                sb.Append(" |");
                for (int i = 0; i < 16 && off + i < data.Length; i++)
                {
                    byte b = data[off + i];
                    sb.Append(b >= 32 && b < 127 ? (char)b : '.');
                }
                sb.Append("|\r\n");
            }
            if (truncated) sb.Append("\r\n[... csak az első 2 MB látszik ...]");
            _text.Text = sb.ToString();
            SetStatus("Hexa");
        }
        catch (Exception ex)
        {
            _text.Text = "Nem olvasható: " + ex.Message;
        }
    }

    private void ShowImage()
    {
        try
        {
            // memóriába töltjük, hogy a fájl ne maradjon zárolva
            var ms = new MemoryStream();
            using (var src = _open()) src.CopyTo(ms);
            ms.Position = 0;
            var old = _picture.Image;
            _picture.Image = Image.FromStream(ms);
            old?.Dispose();
            _text.Visible = false;
            _picture.Visible = true;
            SetStatus($"Kép ({_picture.Image.Width} × {_picture.Image.Height})");
        }
        catch
        {
            ShowText();
        }
    }

    private byte[] ReadHead(int max, out bool truncated)
    {
        using var fs = _open();
        long len = fs.Length;
        truncated = len > max;
        int toRead = (int)Math.Min(len, max);
        var buf = new byte[toRead];
        int total = 0;
        while (total < toRead)
        {
            int r = fs.Read(buf, total, toRead - total);
            if (r == 0) break;
            total += r;
        }
        if (total < toRead) Array.Resize(ref buf, total);
        return buf;
    }

    private static Encoding DetectEncoding(byte[] data, out int bomLength)
    {
        bomLength = 0;
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) { bomLength = 3; return Encoding.UTF8; }
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE) { bomLength = 2; return Encoding.Unicode; }
        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF) { bomLength = 2; return Encoding.BigEndianUnicode; }

        // érvényes UTF-8?
        try
        {
            var strict = new UTF8Encoding(false, true);
            strict.GetCharCount(data, 0, Math.Min(data.Length, 1 << 20));
            return Encoding.UTF8;
        }
        catch (DecoderFallbackException)
        {
            try { return Encoding.GetEncoding(1250); } // közép-európai Windows kódlap
            catch { return Encoding.Latin1; }
        }
    }

    private void Find(bool askNew)
    {
        if (!_text.Visible) return;
        if (askNew || _lastSearch.Length == 0)
        {
            var s = InputDialog.Show(this, "Keresés", "Keresett szöveg:", _lastSearch);
            if (string.IsNullOrEmpty(s)) return;
            _lastSearch = s;
        }
        int start = _text.SelectionStart + (_text.SelectionLength > 0 ? 1 : 0);
        int idx = _text.Text.IndexOf(_lastSearch, Math.Min(start, _text.TextLength), StringComparison.CurrentCultureIgnoreCase);
        if (idx < 0) idx = _text.Text.IndexOf(_lastSearch, StringComparison.CurrentCultureIgnoreCase); // körbe
        if (idx < 0)
        {
            Msg.Show(this, "Nem található: " + _lastSearch, "Keresés", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        _text.Select(idx, _lastSearch.Length);
        _text.ScrollToCaret();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Escape:
                Close();
                return true;
            case Keys.D1:
            case Keys.NumPad1:
                ShowText();
                return true;
            case Keys.D3:
            case Keys.NumPad3:
                ShowHex();
                return true;
            case Keys.D4:
            case Keys.NumPad4:
                ShowImage();
                return true;
            case Keys.W:
                _text.WordWrap = !_text.WordWrap;
                return true;
            case Keys.F7:
            case Keys.Control | Keys.F:
                Find(true);
                return true;
            case Keys.F3:
            case Keys.N:
                Find(false);
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _picture.Image?.Dispose();
        base.Dispose(disposing);
    }
}
