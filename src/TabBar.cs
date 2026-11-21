using System.Drawing.Drawing2D;

namespace DualDeckCommander;

/// <summary>Saját rajzolású fülsáv egy panel tetején (téma-kompatibilis).</summary>
internal sealed class TabBar : Control
{
    public event Action<int>? TabSelected;
    public event Action<int>? TabCloseRequested;
    public event Action? NewTabRequested;
    public event Action<int, Point>? TabMenuRequested;

    private readonly List<(string Title, string Tip)> _tabs = new();
    private readonly List<Rectangle> _tabRects = new();
    private readonly List<Rectangle> _closeRects = new();
    private Rectangle _plusRect;
    private int _selected;
    private int _hover = -1;
    private bool _hoverClose;
    private bool _hoverPlus;
    private readonly ToolTip _tip = new();
    private string _lastTip = "";

    private static int S(int v) => UiScale.S(v);

    public TabBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        Height = S(30);
        Font = new Font("Segoe UI", 9F);
    }

    public void SetTabs(IEnumerable<(string Title, string Tip)> tabs, int selected)
    {
        _tabs.Clear();
        _tabs.AddRange(tabs);
        _selected = selected;
        Invalidate();
    }

    private void LayoutTabs(Graphics g)
    {
        _tabRects.Clear();
        _closeRects.Clear();
        int x = S(6);
        int h = Height - S(4);
        int closeW = S(16);
        int maxW = Math.Max(S(90), (Width - S(40)) / Math.Max(1, _tabs.Count));
        maxW = Math.Min(maxW, S(220));
        foreach (var (title, _) in _tabs)
        {
            int textW = TextRenderer.MeasureText(g, title, Font, Size.Empty, TextFormatFlags.NoPrefix).Width;
            int w = Math.Min(maxW, textW + S(20) + closeW);
            var r = new Rectangle(x, S(4), w, h);
            _tabRects.Add(r);
            _closeRects.Add(new Rectangle(r.Right - closeW - S(4), r.Y + (r.Height - closeW) / 2, closeW, closeW));
            x += w + S(2);
        }
        _plusRect = new Rectangle(x + S(2), S(6), S(22), h - S(4));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var t = Theme.Current;
        var g = e.Graphics;
        g.Clear(t.Surface);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        LayoutTabs(g);

        using (var line = new Pen(t.Border)) g.DrawLine(line, 0, Height - 1, Width, Height - 1);

        for (int i = 0; i < _tabs.Count; i++)
        {
            var r = _tabRects[i];
            bool sel = i == _selected;
            bool hov = i == _hover;
            Color back = sel ? t.Window : hov ? t.ButtonHover : t.Surface;
            using (var path = TopRounded(r, S(5)))
            using (var b = new SolidBrush(back))
            {
                g.FillPath(b, path);
                if (sel)
                {
                    using var p = new Pen(t.Border);
                    g.DrawPath(p, path);
                }
            }
            if (sel)
            {
                using var accent = new SolidBrush(t.Accent);
                g.FillRectangle(accent, r.X + S(4), r.Y, r.Width - S(8), S(2));
                using var cover = new SolidBrush(t.Window);
                g.FillRectangle(cover, r.X + 1, r.Bottom - 1, r.Width - 2, 2); // összeolvad a lista hátterével
            }

            var textRect = new Rectangle(r.X + S(10), r.Y, r.Width - S(10) - S(22), r.Height);
            TextRenderer.DrawText(g, _tabs[i].Title, Font, textRect, sel ? t.Text : t.TextDim,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            if (sel || hov)
            {
                var c = _closeRects[i];
                if (hov && _hoverClose)
                {
                    using var cb = new SolidBrush(t.Border);
                    g.FillEllipse(cb, c);
                }
                using var xp = new Pen(t.TextDim, Math.Max(1f, S(1) * 1.2f));
                int m = S(5);
                g.DrawLine(xp, c.X + m, c.Y + m, c.Right - m, c.Bottom - m);
                g.DrawLine(xp, c.Right - m, c.Y + m, c.X + m, c.Bottom - m);
            }
        }

        // "+" új fül
        if (_hoverPlus)
        {
            using var hb = new SolidBrush(t.ButtonHover);
            g.FillRectangle(hb, _plusRect);
        }
        using (var pp = new Pen(t.TextDim, Math.Max(1f, S(1) * 1.4f)))
        {
            int cx = _plusRect.X + _plusRect.Width / 2, cy = _plusRect.Y + _plusRect.Height / 2, a = S(5);
            g.DrawLine(pp, cx - a, cy, cx + a, cy);
            g.DrawLine(pp, cx, cy - a, cx, cy + a);
        }
    }

    private static GraphicsPath TopRounded(Rectangle r, int radius)
    {
        var p = new GraphicsPath();
        int d = radius * 2;
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
        p.CloseFigure();
        return p;
    }

    private int HitTab(Point p)
    {
        for (int i = 0; i < _tabRects.Count; i++) if (_tabRects[i].Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = HitTab(e.Location);
        bool hc = h >= 0 && _closeRects[h].Contains(e.Location);
        bool hp = _plusRect.Contains(e.Location);
        if (h != _hover || hc != _hoverClose || hp != _hoverPlus)
        {
            _hover = h;
            _hoverClose = hc;
            _hoverPlus = hp;
            Invalidate();
        }
        string tip = h >= 0 ? _tabs[h].Tip : hp ? "Új fül (Ctrl+T)" : "";
        if (tip != _lastTip)
        {
            _lastTip = tip;
            _tip.SetToolTip(this, tip);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = -1;
        _hoverClose = _hoverPlus = false;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        int h = HitTab(e.Location);
        switch (e.Button)
        {
            case MouseButtons.Left when h >= 0 && _closeRects[h].Contains(e.Location):
                TabCloseRequested?.Invoke(h);
                break;
            case MouseButtons.Left when h >= 0:
                TabSelected?.Invoke(h);
                break;
            case MouseButtons.Left when _plusRect.Contains(e.Location):
                NewTabRequested?.Invoke();
                break;
            case MouseButtons.Middle when h >= 0:
                TabCloseRequested?.Invoke(h);
                break;
            case MouseButtons.Right when h >= 0:
                TabMenuRequested?.Invoke(h, e.Location);
                break;
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left && HitTab(e.Location) < 0 && !_plusRect.Contains(e.Location))
            NewTabRequested?.Invoke();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }
}
