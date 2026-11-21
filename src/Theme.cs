using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DualDeckCommander;

/// <summary>Színséma. Két beépített: <see cref="Light"/> és <see cref="Dark"/>.</summary>
internal sealed class Theme
{
    public required string Name { get; init; }
    public required bool IsDark { get; init; }

    public required Color Window { get; init; }         // fájllista háttér
    public required Color RowAlt { get; init; }         // minden második sor
    public required Color Surface { get; init; }        // eszköztár, menü, állapotsor
    public required Color SurfaceAlt { get; init; }     // oszlopfejléc, legördülő menü
    public required Color Border { get; init; }
    public required Color Text { get; init; }
    public required Color TextDim { get; init; }
    public required Color Accent { get; init; }
    public required Color AccentText { get; init; }
    public required Color Selection { get; init; }      // kurzor sor (aktív panel)
    public required Color SelectionInactive { get; init; }
    public required Color CursorInactive { get; init; }   // kurzor sor (inaktív panel)
    public required Color Marked { get; init; }         // kijelölt fájl szövege
    public required Color Folder { get; init; }
    public required Color Iso { get; init; }            // böngészhető lemezkép
    public required Color HeaderInactive { get; init; }
    public required Color HeaderInactiveText { get; init; }
    public required Color ButtonBack { get; init; }
    public required Color ButtonHover { get; init; }
    public required Color InputBack { get; init; }

    public static readonly Theme Light = new()
    {
        Name = "light",
        IsDark = false,
        Window = Color.FromArgb(255, 255, 255),
        RowAlt = Color.FromArgb(248, 249, 251),
        Surface = Color.FromArgb(243, 244, 246),
        SurfaceAlt = Color.FromArgb(234, 236, 240),
        Border = Color.FromArgb(213, 216, 221),
        Text = Color.FromArgb(31, 35, 40),
        TextDim = Color.FromArgb(107, 114, 128),
        Accent = Color.FromArgb(37, 99, 235),
        AccentText = Color.White,
        Selection = Color.FromArgb(204, 226, 255),
        SelectionInactive = Color.FromArgb(150, 156, 166),
        CursorInactive = Color.FromArgb(229, 232, 237),
        Marked = Color.FromArgb(217, 45, 32),
        Folder = Color.FromArgb(146, 98, 0),
        Iso = Color.FromArgb(16, 124, 16),
        HeaderInactive = Color.FromArgb(226, 229, 234),
        HeaderInactiveText = Color.FromArgb(55, 65, 81),
        ButtonBack = Color.FromArgb(255, 255, 255),
        ButtonHover = Color.FromArgb(229, 237, 250),
        InputBack = Color.White,
    };

    public static readonly Theme Dark = new()
    {
        Name = "dark",
        IsDark = true,
        Window = Color.FromArgb(30, 31, 34),
        RowAlt = Color.FromArgb(35, 36, 40),
        Surface = Color.FromArgb(43, 45, 49),
        SurfaceAlt = Color.FromArgb(49, 51, 56),
        Border = Color.FromArgb(62, 65, 71),
        Text = Color.FromArgb(227, 229, 232),
        TextDim = Color.FromArgb(142, 147, 155),
        Accent = Color.FromArgb(59, 130, 246),
        AccentText = Color.White,
        Selection = Color.FromArgb(38, 79, 120),
        SelectionInactive = Color.FromArgb(95, 100, 108),
        CursorInactive = Color.FromArgb(52, 55, 61),
        Marked = Color.FromArgb(255, 107, 107),
        Folder = Color.FromArgb(240, 198, 116),
        Iso = Color.FromArgb(111, 208, 120),
        HeaderInactive = Color.FromArgb(49, 51, 56),
        HeaderInactiveText = Color.FromArgb(170, 174, 181),
        ButtonBack = Color.FromArgb(49, 51, 56),
        ButtonHover = Color.FromArgb(62, 65, 72),
        InputBack = Color.FromArgb(30, 31, 34),
    };

    public static Theme Current { get; private set; } = Light;

    /// <summary>Témaváltáskor (minden nyitott ablak már frissítve).</summary>
    public static event EventHandler? Changed;

    /// <summary>"light", "dark" vagy "system".</summary>
    public static Theme FromSetting(string? setting) => setting switch
    {
        "dark" => Dark,
        "light" => Light,
        _ => SystemPrefersDark() ? Dark : Light,
    };

    public static void Set(Theme theme)
    {
        Current = theme;
        ToolStripManager.Renderer = new ThemedRenderer(theme);
        foreach (Form f in Application.OpenForms) ThemeHelper.Apply(f);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static bool SystemPrefersDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>Saját magát színező vezérlő (pl. fájlpanel) – a rekurzió itt megáll.</summary>
internal interface IThemed
{
    void ApplyTheme(Theme theme);
}

internal static class ThemeHelper
{
    // ---- Natív Windows sötét mód ---------------------------------------------
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? appName, string? idList);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private const int LVM_GETHEADER = 0x101F;
    private static readonly ConditionalWeakTable<Control, object> Hooked = new();

    /// <summary>Sötét címsor (Windows 10 1809+ / Windows 11).</summary>
    public static void SetDarkTitleBar(Control form, bool dark)
    {
        if (!form.IsHandleCreated) return;
        try
        {
            int v = dark ? 1 : 0;
            if (DwmSetWindowAttribute(form.Handle, 20, ref v, 4) != 0)
                DwmSetWindowAttribute(form.Handle, 19, ref v, 4); // régebbi Windows 10
        }
        catch { /* régi Windows */ }
    }

    /// <summary>Sötét görgetősáv és fejléc natív vezérlőkön.</summary>
    public static void SetDarkScrollbars(Control c, bool dark)
    {
        if (!c.IsHandleCreated) return;
        try
        {
            SetWindowTheme(c.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
            if (c is ListView)
            {
                IntPtr header = SendMessage(c.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
                if (header != IntPtr.Zero) SetWindowTheme(header, dark ? "DarkMode_ItemsView" : "ItemsView", null);
            }
        }
        catch { /* régi Windows */ }
    }

    /// <summary>Gondoskodik róla, hogy a natív beállítások a handle létrejöttekor is lefussanak.</summary>
    private static void HookHandle(Control c, Action<Control> onCreated)
    {
        if (Hooked.TryGetValue(c, out _)) return;
        Hooked.Add(c, new object());
        c.HandleCreated += (s, _) => onCreated((Control)s!);
    }

    /// <summary>Rekurzívan alkalmazza az aktuális témát.</summary>
    public static void Apply(Control root)
    {
        var t = Theme.Current;

        if (root is Form form)
        {
            form.BackColor = t.Surface;
            form.ForeColor = t.Text;
            HookHandle(form, f => SetDarkTitleBar(f, Theme.Current.IsDark));
            SetDarkTitleBar(form, t.IsDark);
        }

        if (root is IThemed themed)
        {
            themed.ApplyTheme(t);
            return;
        }

        switch (root)
        {
            case ToolStrip ts:
                ts.BackColor = t.Surface;
                ts.ForeColor = t.Text;
                ts.RenderMode = ToolStripRenderMode.ManagerRenderMode;
                break;
            case Button b:
                b.FlatStyle = FlatStyle.Flat;
                b.BackColor = t.ButtonBack;
                b.ForeColor = t.Text;
                b.FlatAppearance.BorderColor = t.Border;
                b.FlatAppearance.MouseOverBackColor = t.ButtonHover;
                b.FlatAppearance.MouseDownBackColor = t.Selection;
                b.UseVisualStyleBackColor = false;
                break;
            case TextBox tb:
                tb.BackColor = t.InputBack;
                tb.ForeColor = t.Text;
                tb.BorderStyle = BorderStyle.FixedSingle;
                HookScroll(tb);
                break;
            case ComboBox cb:
                cb.BackColor = t.InputBack;
                cb.ForeColor = t.Text;
                cb.FlatStyle = FlatStyle.Flat;
                HookScroll(cb);
                break;
            case ListBox lb:
                lb.BackColor = t.InputBack;
                lb.ForeColor = t.Text;
                lb.BorderStyle = BorderStyle.FixedSingle;
                HookScroll(lb);
                break;
            case CheckBox chk:
                chk.ForeColor = t.Text;
                chk.FlatStyle = t.IsDark ? FlatStyle.Flat : FlatStyle.Standard;
                break;
            case Label l when l.Tag as string == "dim":
                l.ForeColor = t.TextDim;
                break;
            case Label l:
                l.ForeColor = t.Text;
                break;
            case PictureBox:
            case ProgressBar:
                break;
            case SplitContainer sc:
                sc.BackColor = t.Border;
                break;
            case Panel:
                root.BackColor = t.Surface;
                root.ForeColor = t.Text;
                break;
        }

        foreach (Control child in root.Controls) Apply(child);
    }

    private static void HookScroll(Control c)
    {
        HookHandle(c, x => SetDarkScrollbars(x, Theme.Current.IsDark));
        SetDarkScrollbars(c, Theme.Current.IsDark);
    }
}

/// <summary>Menük, eszköztárak és helyi menük témázott kirajzolása.</summary>
internal sealed class ThemedRenderer : ToolStripProfessionalRenderer
{
    private readonly Theme _t;

    public ThemedRenderer(Theme t) : base(new ThemedColorTable(t))
    {
        _t = t;
        RoundedEdges = false;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = !e.Item.Enabled ? _t.TextDim
            : e.Item.Tag as string == "dim" ? _t.TextDim
            : _t.Text;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = _t.TextDim;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is ToolStripDropDown)
        {
            using var pen = new Pen(_t.Border);
            var r = e.AffectedBounds;
            e.Graphics.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
        }
        else
        {
            using var pen = new Pen(_t.Border);
            e.Graphics.DrawLine(pen, 0, e.ToolStrip.Height - 1, e.ToolStrip.Width, e.ToolStrip.Height - 1);
        }
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(_t.Border);
        var b = e.Item.ContentRectangle;
        if (e.Vertical)
        {
            int x = b.Left + b.Width / 2;
            e.Graphics.DrawLine(pen, x, b.Top + 4, x, b.Bottom - 4);
        }
        else
        {
            int y = b.Top + b.Height / 2;
            e.Graphics.DrawLine(pen, b.Left + 28, y, b.Right - 4, y);
        }
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item is ToolStripButton { Checked: true } b && !b.Selected)
        {
            var r = new Rectangle(1, 1, e.Item.Width - 2, e.Item.Height - 2);
            using var brush = new SolidBrush(_t.Selection);
            e.Graphics.FillRectangle(brush, r);
            return;
        }
        base.OnRenderButtonBackground(e);
    }
}

internal sealed class ThemedColorTable : ProfessionalColorTable
{
    private readonly Theme _t;
    public ThemedColorTable(Theme t) { _t = t; UseSystemColors = false; }

    public override Color ToolStripGradientBegin => _t.Surface;
    public override Color ToolStripGradientMiddle => _t.Surface;
    public override Color ToolStripGradientEnd => _t.Surface;
    public override Color ToolStripBorder => _t.Border;
    public override Color ToolStripDropDownBackground => _t.SurfaceAlt;
    public override Color ToolStripContentPanelGradientBegin => _t.Surface;
    public override Color ToolStripContentPanelGradientEnd => _t.Surface;
    public override Color MenuStripGradientBegin => _t.Surface;
    public override Color MenuStripGradientEnd => _t.Surface;
    public override Color StatusStripGradientBegin => _t.Surface;
    public override Color StatusStripGradientEnd => _t.Surface;
    public override Color ImageMarginGradientBegin => _t.SurfaceAlt;
    public override Color ImageMarginGradientMiddle => _t.SurfaceAlt;
    public override Color ImageMarginGradientEnd => _t.SurfaceAlt;
    public override Color MenuBorder => _t.Border;
    public override Color MenuItemBorder => _t.ButtonHover;
    public override Color MenuItemSelected => _t.ButtonHover;
    public override Color MenuItemSelectedGradientBegin => _t.ButtonHover;
    public override Color MenuItemSelectedGradientEnd => _t.ButtonHover;
    public override Color MenuItemPressedGradientBegin => _t.SurfaceAlt;
    public override Color MenuItemPressedGradientMiddle => _t.SurfaceAlt;
    public override Color MenuItemPressedGradientEnd => _t.SurfaceAlt;
    public override Color ButtonSelectedHighlight => _t.ButtonHover;
    public override Color ButtonSelectedHighlightBorder => _t.ButtonHover;
    public override Color ButtonSelectedGradientBegin => _t.ButtonHover;
    public override Color ButtonSelectedGradientMiddle => _t.ButtonHover;
    public override Color ButtonSelectedGradientEnd => _t.ButtonHover;
    public override Color ButtonSelectedBorder => _t.ButtonHover;
    public override Color ButtonPressedHighlight => _t.Selection;
    public override Color ButtonPressedHighlightBorder => _t.Accent;
    public override Color ButtonPressedGradientBegin => _t.Selection;
    public override Color ButtonPressedGradientMiddle => _t.Selection;
    public override Color ButtonPressedGradientEnd => _t.Selection;
    public override Color ButtonPressedBorder => _t.Accent;
    public override Color ButtonCheckedHighlight => _t.Selection;
    public override Color ButtonCheckedHighlightBorder => _t.Accent;
    public override Color ButtonCheckedGradientBegin => _t.Selection;
    public override Color ButtonCheckedGradientMiddle => _t.Selection;
    public override Color ButtonCheckedGradientEnd => _t.Selection;
    public override Color CheckBackground => _t.Selection;
    public override Color CheckSelectedBackground => _t.Selection;
    public override Color CheckPressedBackground => _t.Selection;
    public override Color SeparatorDark => _t.Border;
    public override Color SeparatorLight => _t.Surface;
    public override Color GripDark => _t.Border;
    public override Color GripLight => _t.Surface;
    public override Color OverflowButtonGradientBegin => _t.Surface;
    public override Color OverflowButtonGradientMiddle => _t.Surface;
    public override Color OverflowButtonGradientEnd => _t.Surface;
}

/// <summary>Éles, színezhető ikonok a Windows beépített ikonbetűtípusából.</summary>
internal static class Glyphs
{
    public const char Refresh = '\uE72C';
    public const char Swap = '\uE8AB';
    public const char Folder = '\uE8B7';
    public const char Search = '\uE721';
    public const char Console = '\uE756';
    public const char Hidden = '\uE7B3';
    public const char Moon = '\uE708';
    public const char Sun = '\uE706';
    public const char Disc = '\uE958';
    public const char Up = '\uE74A';
    public const char Home = '\uE80F';
    public const char Back = '\uE72B';
    public const char Info = '\uE946';
    public const char Star = '\uE734';
    public const char NewTab = '\uECCD';

    private static readonly string? FontName = FindFont();

    private static string? FindFont()
    {
        foreach (var name in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
        {
            try
            {
                using var f = new Font(name, 10f);
                if (f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return name;
            }
            catch { }
        }
        return null;
    }

    public static bool Available => FontName != null;

    public static Bitmap? Render(char glyph, Color color, int size)
    {
        if (FontName == null) return null;
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var font = new Font(FontName, size * 0.62f, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(glyph.ToString(), font, brush, new RectangleF(0, 0, size, size), fmt);
        return bmp;
    }
}

/// <summary>Témázott üzenetablak a MessageBox.Show helyett (azonos paraméterezéssel).</summary>
internal static class Msg
{
    public static DialogResult Show(string text) => Show(null, text, "Dual Deck Commander");

    public static DialogResult Show(string text, string caption,
        MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None) =>
        Show(null, text, caption, buttons, icon);

    public static DialogResult Show(IWin32Window? owner, string text) => Show(owner, text, "Dual Deck Commander");

    public static DialogResult Show(IWin32Window? owner, string text, string caption,
        MessageBoxButtons buttons = MessageBoxButtons.OK, MessageBoxIcon icon = MessageBoxIcon.None,
        MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
    {
        Icon? ic = icon switch
        {
            MessageBoxIcon.Error => SystemIcons.Error,
            MessageBoxIcon.Warning => SystemIcons.Warning,
            MessageBoxIcon.Question => SystemIcons.Question,
            MessageBoxIcon.Information => SystemIcons.Information,
            _ => null,
        };
        int def = defaultButton == MessageBoxDefaultButton.Button2 ? 1 : 0;
        switch (buttons)
        {
            case MessageBoxButtons.YesNo:
            {
                int r = Dialogs.Choose(owner, caption, text, new[] { "&Igen", "&Nem" }, def, 1, ic);
                return r == 0 ? DialogResult.Yes : DialogResult.No;
            }
            case MessageBoxButtons.OKCancel:
            {
                int r = Dialogs.Choose(owner, caption, text, new[] { "OK", "Mégse" }, def, 1, ic);
                return r == 0 ? DialogResult.OK : DialogResult.Cancel;
            }
            default:
                Dialogs.Choose(owner, caption, text, new[] { "OK" }, 0, 0, ic);
                return DialogResult.OK;
        }
    }
}
