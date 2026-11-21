using System.Reflection;
using System.Runtime.InteropServices;

namespace DualDeckCommander;

/// <summary>Beágyazott képek és ikon.</summary>
internal static class AppAssets
{
    private static Icon? _icon;

    public static Image? LoadImage(string name)
    {
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (s == null) return null;
            var ms = new MemoryStream();
            s.CopyTo(ms);
            ms.Position = 0;
            return Image.FromStream(ms);
        }
        catch
        {
            return null;
        }
    }

    public static Icon AppIcon
    {
        get
        {
            if (_icon != null) return _icon;
            try
            {
                using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico");
                if (s != null) return _icon = new Icon(s);
            }
            catch { }
            return _icon = SystemIcons.Application;
        }
    }

    public static string Version
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v == null ? "" : $"{v.Major}.{v.Minor}" + (v.Build > 0 ? $".{v.Build}" : "");
        }
    }
}

/// <summary>
/// Indítóképernyő: keret nélküli, árnyékos ablak a beágyazott képpel. A verziószámot és
/// a betöltés állapotát futás közben rajzolja rá. A főablak megjelenése után elhalványul.
/// </summary>
internal sealed class SplashForm : Form
{
    private const int CS_DROPSHADOW = 0x00020000;
    private const int MinimumVisibleMs = 1400;

    private readonly Image? _image;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly System.Diagnostics.Stopwatch _shown = System.Diagnostics.Stopwatch.StartNew();
    private string _status = "Betöltés…";
    private float _progress;
    private bool _closing;

    private static int S(int v) => UiScale.S(v);

    public SplashForm()
    {
        _image = AppAssets.LoadImage("splash.png");
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = Color.FromArgb(13, 15, 21);
        ClientSize = new Size(S(640), S(380));
        DoubleBuffered = true;
        Icon = AppAssets.AppIcon;
        Text = "Dual Deck Commander";

        Region = RoundedRegion(ClientSize, S(14));
        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);

    private static Region? RoundedRegion(Size size, int radius)
    {
        try
        {
            IntPtr h = CreateRoundRectRgn(0, 0, size.Width + 1, size.Height + 1, radius * 2, radius * 2);
            return h == IntPtr.Zero ? null : Region.FromHrgn(h);
        }
        catch
        {
            return null;
        }
    }

    public void SetStatus(string status)
    {
        _status = status;
        Invalidate();
        Update();
    }

    /// <summary>Bezárás halványítással, de legalább a minimális ideig látszik.</summary>
    public void CloseWhenReady(Form owner)
    {
        _status = "Kész";
        int wait = Math.Max(0, MinimumVisibleMs - (int)_shown.ElapsedMilliseconds);
        var t = new System.Windows.Forms.Timer { Interval = Math.Max(1, wait) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            t.Dispose();
            _closing = true;
            if (!owner.IsDisposed) owner.Activate();
        };
        t.Start();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_closing)
        {
            Opacity -= 0.08;
            if (Opacity <= 0.05)
            {
                _timer.Stop();
                Close();
            }
            return;
        }
        // végtelenített, lassuló "betöltés" sáv
        _progress = Math.Min(0.92f, _progress + (0.95f - _progress) * 0.025f);
        Invalidate(new Rectangle(0, ClientSize.Height - S(70), ClientSize.Width, S(70)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        if (_image != null) g.DrawImage(_image, new Rectangle(Point.Empty, ClientSize));

        // jobb alsó sarok: verzió és állapot
        var dim = Color.FromArgb(148, 163, 184);
        var right = new Rectangle(ClientSize.Width / 2, ClientSize.Height - S(48), ClientSize.Width / 2 - S(32), S(20));
        using (var vf = new Font("Segoe UI Semibold", 10f))
            TextRenderer.DrawText(g, "Verzió " + AppAssets.Version, vf, right, Color.FromArgb(203, 213, 225),
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        right.Offset(0, S(20));
        using (var sf = new Font("Segoe UI", 9f))
            TextRenderer.DrawText(g, _status, sf, right, dim,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        // vékony haladássáv a kép alján
        int h = Math.Max(2, S(3));
        var bar = new Rectangle(0, ClientSize.Height - h, ClientSize.Width, h);
        using (var b = new SolidBrush(Color.FromArgb(30, 41, 59))) g.FillRectangle(b, bar);
        var fill = new Rectangle(0, bar.Y, (int)(bar.Width * (_closing ? 1f : _progress)), h);
        if (fill.Width > 0)
        {
            using var lg = new System.Drawing.Drawing2D.LinearGradientBrush(
                new Rectangle(0, bar.Y, Math.Max(1, bar.Width), h), Color.FromArgb(59, 130, 246), Color.FromArgb(16, 185, 129), 0f);
            g.FillRectangle(lg, fill);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _image?.Dispose();
        }
        base.Dispose(disposing);
    }
}
