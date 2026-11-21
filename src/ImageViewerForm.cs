using System.Drawing.Drawing2D;

namespace DualDeckCommander;

/// <summary>Egy megjeleníthető kép (lemezről vagy archívumból).</summary>
internal sealed record ImageItem(string Name, Func<Stream> Open, string? RealPath);

/// <summary>
/// Beépített képnézegető: csak a kép, sötét háttéren. Lapozás: nyilak, PgUp/PgDn, szóköz,
/// görgő, kattintás a kép szélén. Nagyítás: Ctrl+görgő, +/-, 1 (eredeti méret), 0 (ablakhoz).
/// F11 / dupla kattintás: teljes képernyő. R: forgatás. Esc: bezárás.
/// </summary>
internal sealed class ImageViewerForm : Form
{
    private static readonly Color Back = Color.FromArgb(17, 17, 19);

    private readonly List<ImageItem> _items;
    private int _index;
    private Image? _image;
    private string? _error;
    private bool _loading;
    private readonly Dictionary<int, Task<Image>> _cache = new();
    private CancellationTokenSource _cts = new();

    // nézet
    private bool _fit = true;
    private float _zoom = 1f;
    private PointF _pan;
    private Point _dragStart;
    private PointF _panStart;
    private bool _dragging;
    private bool _dragMoved;
    private Bitmap? _scaled;          // előre kicsinyített kép (gyors újrarajzolás ablakhoz igazításnál)
    private Size _scaledFor;
    private bool _animating;

    // teljes képernyő
    private bool _fullscreen;
    private FormWindowState _prevState;
    private Rectangle _prevBounds;
    private readonly System.Windows.Forms.Timer _hideCursorTimer = new() { Interval = 2000 };
    private readonly System.Windows.Forms.Timer _osdTimer = new() { Interval = 1300 };
    private bool _cursorHidden;
    private bool _showOsd;

    private static int S(int v) => UiScale.S(v);

    public ImageViewerForm(List<ImageItem> items, int startIndex)
    {
        _items = items;
        _index = Math.Clamp(startIndex, 0, Math.Max(0, items.Count - 1));

        Text = "Képnézegető";
        Icon = AppAssets.AppIcon;
        BackColor = Back;
        DoubleBuffered = true;
        KeyPreview = true;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = UiScale.S(320, 240);
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Bounds = new Rectangle(wa.X + wa.Width / 14, wa.Y + wa.Height / 14, wa.Width * 6 / 7, wa.Height * 6 / 7);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

        _hideCursorTimer.Tick += (_, _) =>
        {
            _hideCursorTimer.Stop();
            if (_fullscreen && !_cursorHidden) { Cursor.Hide(); _cursorHidden = true; }
        };
        _osdTimer.Tick += (_, _) => { _osdTimer.Stop(); _showOsd = false; Invalidate(); };

        HandleCreated += (_, _) => ThemeHelper.SetDarkTitleBar(this, true);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        ShowImage(_index);
    }

    // =========================================================================
    //  Betöltés és lapozás
    // =========================================================================

    private Task<Image> GetImage(int i)
    {
        if (_cache.TryGetValue(i, out var t)) return t;
        var item = _items[i];
        t = Task.Run(() => ImageLoader.Load(item.Name, item.Open, item.RealPath));
        _cache[i] = t;
        return t;
    }

    private async void ShowImage(int index)
    {
        if (_items.Count == 0) return;
        index = (index % _items.Count + _items.Count) % _items.Count;
        _index = index;
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        StopAnimation();
        _fit = true;
        _zoom = 1f;
        _pan = PointF.Empty;
        _error = null;
        DropScaled();
        UpdateTitle(null);

        var task = GetImage(index);
        if (!task.IsCompleted)
        {
            _loading = true;
            _image = null;
            Invalidate();
        }

        Image? img = null;
        try
        {
            img = await task;
        }
        catch (Exception ex)
        {
            _cache.Remove(index);
            if (ct.IsCancellationRequested || IsDisposed) return;
            _loading = false;
            _image = null;
            _error = $"{_items[index].Name}\n\nA kép nem nyitható meg:\n{(ex is AggregateException ae ? ae.InnerException?.Message : ex.Message)}";
            Invalidate();
            return;
        }
        if (ct.IsCancellationRequested || IsDisposed) return;

        _loading = false;
        _image = img;
        UpdateTitle(img);
        StartAnimation();
        ShowOsd();
        Invalidate();
        Prefetch();
    }

    /// <summary>A szomszédos képeket előre betölti, a távolabbiakat kiüríti a memóriából.</summary>
    private void Prefetch()
    {
        int n = _items.Count;
        var keep = new HashSet<int> { _index, (_index + 1) % n, (_index - 1 + n) % n };
        foreach (var k in _cache.Keys.ToList())
        {
            if (keep.Contains(k)) continue;
            var t = _cache[k];
            _cache.Remove(k);
            t.ContinueWith(x => { if (x.Status == TaskStatus.RanToCompletion) x.Result.Dispose(); });
        }
        if (n > 1)
        {
            GetImage((_index + 1) % n);
            GetImage((_index - 1 + n) % n);
        }
    }

    private void UpdateTitle(Image? img)
    {
        var item = _items[_index];
        string size = img != null ? $"   {img.Width} × {img.Height}" : "";
        string zoom = img != null && !_fit ? $"   {_zoom * 100:0}%" : "";
        Text = $"{item.Name}   –   {_index + 1} / {_items.Count}{size}{zoom}";
    }

    private void Next() { if (_items.Count > 1) ShowImage(_index + 1); }
    private void Prev() { if (_items.Count > 1) ShowImage(_index - 1); }

    // ---- animált GIF ----
    private void StartAnimation()
    {
        if (_image != null && ImageAnimator.CanAnimate(_image))
        {
            ImageAnimator.Animate(_image, OnFrameChanged);
            _animating = true;
        }
    }

    private void StopAnimation()
    {
        if (_animating && _image != null) ImageAnimator.StopAnimate(_image, OnFrameChanged);
        _animating = false;
    }

    private void OnFrameChanged(object? sender, EventArgs e)
    {
        if (!IsDisposed && IsHandleCreated) BeginInvoke(new Action(() => Invalidate()));
    }

    // =========================================================================
    //  Rajzolás
    // =========================================================================

    private float FitScale(Image img)
    {
        var c = ClientSize;
        if (img.Width == 0 || img.Height == 0) return 1f;
        return Math.Min(1f, Math.Min((float)c.Width / img.Width, (float)c.Height / img.Height));
    }

    private float CurrentScale(Image img) => _fit ? FitScale(img) : _zoom;

    private RectangleF ImageRect(Image img)
    {
        float s = CurrentScale(img);
        float w = img.Width * s, h = img.Height * s;
        var c = ClientSize;
        float x = (c.Width - w) / 2f + _pan.X;
        float y = (c.Height - h) / 2f + _pan.Y;
        return new RectangleF(x, y, w, h);
    }

    private void DropScaled()
    {
        _scaled?.Dispose();
        _scaled = null;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Back);

        if (_image == null)
        {
            string msg = _error ?? (_loading ? "Betöltés…" : _items.Count == 0 ? "Nincs megjeleníthető kép." : "");
            TextRenderer.DrawText(g, msg, Font, ClientRectangle, Color.FromArgb(150, 150, 155),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        var img = _image;
        var r = ImageRect(img);
        if (_animating) ImageAnimator.UpdateFrames(img);

        float scale = CurrentScale(img);
        if (_fit && scale < 1f && !_animating)
        {
            // kicsinyített példány egyszer, jó minőségben – utána gyors a rajzolás
            var target = new Size(Math.Max(1, (int)Math.Round(r.Width)), Math.Max(1, (int)Math.Round(r.Height)));
            if (_scaled == null || _scaledFor != target)
            {
                DropScaled();
                _scaled = new Bitmap(target.Width, target.Height);
                using var sg = Graphics.FromImage(_scaled);
                sg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                sg.CompositingQuality = CompositingQuality.HighQuality;
                using var attrs = new System.Drawing.Imaging.ImageAttributes();
                attrs.SetWrapMode(WrapMode.TileFlipXY); // nincs sötét szegély a széleken
                sg.DrawImage(img, new Rectangle(Point.Empty, target), 0, 0, img.Width, img.Height, GraphicsUnit.Pixel, attrs);
                _scaledFor = target;
            }
            g.DrawImageUnscaled(_scaled, (int)Math.Round(r.X), (int)Math.Round(r.Y));
        }
        else
        {
            // nagyításnál a pixelek élesek maradnak (retró / emulátoros képernyőképek)
            g.InterpolationMode = scale >= 2f ? InterpolationMode.NearestNeighbor
                : scale < 1f ? InterpolationMode.HighQualityBicubic : InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(img, r);
        }

        if (_showOsd && _fullscreen)
        {
            string osd = $"{_items[_index].Name}   {_index + 1} / {_items.Count}";
            using var f = new Font("Segoe UI", 10.5f);
            var size = TextRenderer.MeasureText(osd, f);
            var box = new Rectangle((ClientSize.Width - size.Width) / 2 - S(14), ClientSize.Height - size.Height - S(40),
                size.Width + S(28), size.Height + S(12));
            using (var b = new SolidBrush(Color.FromArgb(170, 0, 0, 0))) g.FillRectangle(b, box);
            TextRenderer.DrawText(g, osd, f, box, Color.FromArgb(230, 230, 230),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    private void ShowOsd()
    {
        if (!_fullscreen) return;
        _showOsd = true;
        _osdTimer.Stop();
        _osdTimer.Start();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    // =========================================================================
    //  Nagyítás, mozgatás, forgatás
    // =========================================================================

    private void ZoomTo(float newZoom, Point? anchor = null)
    {
        if (_image == null) return;
        float old = CurrentScale(_image);
        newZoom = Math.Clamp(newZoom, 0.02f, 32f);
        var a = anchor ?? new Point(ClientSize.Width / 2, ClientSize.Height / 2);
        var r = ImageRect(_image);
        // a kurzor alatti képpont maradjon a helyén
        float ix = (a.X - r.X) / old, iy = (a.Y - r.Y) / old;
        _fit = false;
        _zoom = newZoom;
        float w = _image.Width * newZoom, h = _image.Height * newZoom;
        float baseX = (ClientSize.Width - w) / 2f, baseY = (ClientSize.Height - h) / 2f;
        _pan = new PointF(a.X - ix * newZoom - baseX, a.Y - iy * newZoom - baseY);
        ClampPan();
        UpdateTitle(_image);
        Invalidate();
    }

    private void FitToWindow()
    {
        _fit = true;
        _pan = PointF.Empty;
        if (_image != null) UpdateTitle(_image);
        Invalidate();
    }

    private void ClampPan()
    {
        if (_image == null) return;
        float s = CurrentScale(_image);
        float maxX = Math.Max(0, (_image.Width * s - ClientSize.Width) / 2f);
        float maxY = Math.Max(0, (_image.Height * s - ClientSize.Height) / 2f);
        _pan = new PointF(Math.Clamp(_pan.X, -maxX, maxX), Math.Clamp(_pan.Y, -maxY, maxY));
    }

    private void Rotate(bool clockwise)
    {
        if (_image == null || _animating) return;
        _image.RotateFlip(clockwise ? RotateFlipType.Rotate90FlipNone : RotateFlipType.Rotate270FlipNone);
        DropScaled();
        FitToWindow();
    }

    // =========================================================================
    //  Teljes képernyő
    // =========================================================================

    private void ToggleFullscreen()
    {
        if (!_fullscreen)
        {
            _prevState = WindowState;
            _prevBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            _fullscreen = true;
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.None;
            Bounds = Screen.FromControl(this).Bounds;
            TopMost = true;
            _hideCursorTimer.Start();
            ShowOsd();
        }
        else
        {
            _fullscreen = false;
            TopMost = false;
            FormBorderStyle = FormBorderStyle.Sizable;
            Bounds = _prevBounds;
            WindowState = _prevState;
            ShowCursorAgain();
        }
        DropScaled();
        Invalidate();
    }

    private void ShowCursorAgain()
    {
        _hideCursorTimer.Stop();
        if (_cursorHidden) { Cursor.Show(); _cursorHidden = false; }
    }

    // =========================================================================
    //  Bemenet
    // =========================================================================

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Escape:
                if (_fullscreen) ToggleFullscreen();
                else Close();
                return true;
            case Keys.Right:
            case Keys.Down:
            case Keys.PageDown:
            case Keys.Space:
            case Keys.N:
                Next();
                return true;
            case Keys.Left:
            case Keys.Up:
            case Keys.PageUp:
            case Keys.Back:
            case Keys.P:
                Prev();
                return true;
            case Keys.Home:
                ShowImage(0);
                return true;
            case Keys.End:
                ShowImage(_items.Count - 1);
                return true;
            case Keys.F11:
            case Keys.F:
            case Keys.Enter:
                ToggleFullscreen();
                return true;
            case Keys.Add:
            case Keys.Oemplus:
            case Keys.Control | Keys.Add:
            case Keys.Control | Keys.Oemplus:
                if (_image != null) ZoomTo(CurrentScale(_image) * 1.25f);
                return true;
            case Keys.Subtract:
            case Keys.OemMinus:
            case Keys.Control | Keys.Subtract:
            case Keys.Control | Keys.OemMinus:
                if (_image != null) ZoomTo(CurrentScale(_image) / 1.25f);
                return true;
            case Keys.D1:
            case Keys.NumPad1:
                ZoomTo(1f);
                return true;
            case Keys.D0:
            case Keys.NumPad0:
            case Keys.Multiply:
                FitToWindow();
                return true;
            case Keys.R:
                Rotate(true);
                return true;
            case Keys.Shift | Keys.R:
            case Keys.L:
                Rotate(false);
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if ((ModifierKeys & Keys.Control) != 0 && _image != null)
        {
            ZoomTo(CurrentScale(_image) * (e.Delta > 0 ? 1.2f : 1 / 1.2f), e.Location);
            return;
        }
        if (e.Delta < 0) Next();
        else Prev();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            _dragMoved = false;
            _dragStart = e.Location;
            _panStart = _pan;
        }
        else if (e.Button == MouseButtons.Middle)
        {
            Close();
        }
        else if (e.Button == MouseButtons.XButton1) Prev();
        else if (e.Button == MouseButtons.XButton2) Next();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_fullscreen)
        {
            ShowCursorAgain();
            _hideCursorTimer.Start();
        }
        if (!_dragging || _image == null) return;
        int dx = e.X - _dragStart.X, dy = e.Y - _dragStart.Y;
        if (Math.Abs(dx) + Math.Abs(dy) > S(4)) _dragMoved = true;
        bool zoomedIn = CurrentScale(_image) * _image.Width > ClientSize.Width || CurrentScale(_image) * _image.Height > ClientSize.Height;
        if (_dragMoved && zoomedIn)
        {
            if (_fit) { _zoom = CurrentScale(_image); _fit = false; }
            _pan = new PointF(_panStart.X + dx, _panStart.Y + dy);
            ClampPan();
            Cursor = Cursors.SizeAll;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        _dragging = false;
        Cursor = Cursors.Default;
        if (_dragMoved) return;
        // kattintás a bal / jobb harmadra: lapozás
        if (e.X < ClientSize.Width / 3) Prev();
        else if (e.X > ClientSize.Width * 2 / 3) Next();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        if (e.Button == MouseButtons.Left && e.X >= ClientSize.Width / 3 && e.X <= ClientSize.Width * 2 / 3)
            ToggleFullscreen();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        ShowCursorAgain();
        StopAnimation();
        _cts.Cancel();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hideCursorTimer.Dispose();
            _osdTimer.Dispose();
            DropScaled();
            foreach (var t in _cache.Values)
                t.ContinueWith(x => { if (x.Status == TaskStatus.RanToCompletion) x.Result.Dispose(); });
            _cache.Clear();
        }
        base.Dispose(disposing);
    }
}
