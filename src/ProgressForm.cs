using System.Diagnostics;

namespace DualDeckCommander;

/// <summary>Modális haladásjelző ablak, amely háttérszálon futtat egy fájlműveletet.</summary>
internal sealed class ProgressForm : Form
{
    private readonly Action<FileOperations> _work;
    private readonly bool _isDelete;

    private readonly Label _lblSource;
    private readonly Label _lblTarget;
    private readonly ProgressBar _barFile;
    private readonly ProgressBar _barTotal;
    private readonly Label _lblStats;
    private readonly Button _btnCancel;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly CancellationTokenSource _cts = new();
    private readonly Stopwatch _watch = new();
    private FileOperations? _ops;
    private bool _finished;

    public bool WasCancelled { get; private set; }
    public IReadOnlyList<string> Errors => _ops?.Errors ?? new List<string>();

    public static bool Run(IWin32Window owner, OperationKind kind, IReadOnlyList<string> sources,
        string? targetDir = null, string? newName = null)
    {
        string title = kind switch
        {
            OperationKind.Copy => "Másolás",
            OperationKind.Move => "Áthelyezés",
            OperationKind.Recycle => "Lomtárba helyezés",
            _ => "Törlés",
        };
        bool isDelete = kind is OperationKind.Delete or OperationKind.Recycle;
        return Show(owner, title, isDelete, ops => ops.Run(kind, sources, targetDir, newName));
    }

    /// <summary>Kibontás lemezképből / archívumból. Igaz, ha nem szakították meg.</summary>
    public static bool RunExtract(IWin32Window owner, IVirtualFs vfs, IReadOnlyList<VEntry> entries,
        string targetDir, string? newName = null) =>
        Show(owner, "Kibontás", false, ops => ops.RunExtract(vfs, entries, targetDir, newName));

    /// <summary>Teljes archívumok kibontása.</summary>
    public static bool RunExtractArchives(IWin32Window owner, IReadOnlyList<string> archives, string targetDir, bool subfolders) =>
        Show(owner, "Kibontás", false, ops => ops.RunExtractArchives(archives, targetDir, subfolders));

    /// <summary>Átalakítás Games on Demand formátumra.</summary>
    public static bool RunGod(IWin32Window owner, IReadOnlyList<GodConverter.Plan> plans) =>
        Show(owner, "Átalakítás GoD formátumra", false, ops => ops.RunGod(plans));

    private static bool Show(IWin32Window owner, string title, bool isDelete, Action<FileOperations> work)
    {
        using var f = new ProgressForm(title, isDelete, work);
        f.ShowDialog(owner);

        if (f.Errors.Count > 0)
        {
            var list = string.Join("\n", f.Errors.Take(15));
            if (f.Errors.Count > 15) list += $"\n... és még {f.Errors.Count - 15} hiba";
            Msg.Show(owner, list, $"{f.Errors.Count} elem nem sikerült",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        return !f.WasCancelled;
    }

    private ProgressForm(string title, bool isDelete, Action<FileOperations> work)
    {
        _work = work;
        _isDelete = isDelete;
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Font = new Font("Segoe UI", 9F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = UiScale.S(560, 210);
        Padding = new Padding(12);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
        _lblSource = new Label { Dock = DockStyle.Fill, AutoEllipsis = true, Text = "Előkészítés..." };
        _lblTarget = new Label { Dock = DockStyle.Fill, AutoEllipsis = true };
        _barFile = new ProgressBar { Dock = DockStyle.Fill, Maximum = 1000, Height = UiScale.S(18) };
        _barTotal = new ProgressBar { Dock = DockStyle.Fill, Maximum = 1000, Height = UiScale.S(18) };
        _lblStats = new Label { Dock = DockStyle.Fill };
        _btnCancel = new Button { Text = "Mégse", AutoSize = true, Anchor = AnchorStyles.Right };
        _btnCancel.Click += (_, _) => Cancel();

        layout.Controls.Add(_lblSource);
        layout.Controls.Add(_lblTarget);
        layout.Controls.Add(_barFile);
        layout.Controls.Add(_barTotal);
        layout.Controls.Add(_lblStats);
        layout.Controls.Add(_btnCancel);
        for (int i = 0; i < 6; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);

        _barFile.Visible = !isDelete;
        _lblTarget.Visible = !isDelete;

        _timer = new System.Windows.Forms.Timer { Interval = 100 };
        _timer.Tick += (_, _) => UpdateUi();
        CancelButton = _btnCancel;
        ThemeHelper.Apply(this);
        _lblStats.ForeColor = Theme.Current.TextDim;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _ops = new FileOperations(this, _cts.Token);
        _watch.Start();
        _timer.Start();

        var thread = new Thread(() =>
        {
            try
            {
                _work(_ops);
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
            }
            catch (Exception ex)
            {
                _ops.Errors.Add(ex.Message);
            }
            finally
            {
                _finished = true;
                try { BeginInvoke(Close); } catch { /* ablak már zárva */ }
            }
        })
        {
            IsBackground = true,
            Name = "FileOperation",
        };
        thread.SetApartmentState(ApartmentState.STA); // a Lomtár API-hoz
        thread.Start();
    }

    private void Cancel()
    {
        _btnCancel.Enabled = false;
        _btnCancel.Text = "Megszakítás...";
        _cts.Cancel();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_finished)
        {
            e.Cancel = true;
            Cancel();
            return;
        }
        _timer.Stop();
        base.OnFormClosing(e);
    }

    private void UpdateUi()
    {
        var o = _ops;
        if (o == null) return;

        if (o.Scanning)
        {
            _lblSource.Text = "Fájlok összegyűjtése: " + o.CurrentSource;
            _barTotal.Style = ProgressBarStyle.Marquee;
            return;
        }
        _barTotal.Style = ProgressBarStyle.Continuous;

        _lblSource.Text = (_isDelete ? "Törlés: " : "Forrás: ") + o.CurrentSource;
        _lblTarget.Text = "Cél: " + o.CurrentTarget;

        _barFile.Value = Permille(o.FileBytesDone, o.FileBytesTotal);
        _barTotal.Value = o.TotalBytes > 0
            ? Permille(o.TotalBytesDone, o.TotalBytes)
            : Permille(o.ItemsDone, o.ItemsTotal);

        double secs = Math.Max(0.1, _watch.Elapsed.TotalSeconds);
        string speed = o.TotalBytes > 0 ? $"   •   {FilePanel.FormatBytes((long)(o.TotalBytesDone / secs))}/s" : "";
        string bytes = o.TotalBytes > 0
            ? $"   •   {FilePanel.FormatBytes(o.TotalBytesDone)} / {FilePanel.FormatBytes(o.TotalBytes)}"
            : "";
        _lblStats.Text = $"{o.ItemsDone} / {o.ItemsTotal} elem{bytes}{speed}";
    }

    private static int Permille(long done, long total) =>
        total <= 0 ? 0 : (int)Math.Clamp(done * 1000 / total, 0, 1000);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _cts.Dispose();
        }
        base.Dispose(disposing);
    }
}
