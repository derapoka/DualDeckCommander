namespace DualDeckCommander;

internal enum OverwriteChoice { Overwrite, OverwriteAll, OverwriteOlder, Skip, SkipAll, Rename, Cancel }
internal enum ErrorChoice { Retry, Skip, SkipAll, Cancel }

/// <summary>Egyszerű szövegbekérő ablak.</summary>
internal static class InputDialog
{
    public static string? Show(IWin32Window? owner, string title, string prompt, string defaultValue,
        int selectLength = -1)
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            Font = new Font("Segoe UI", 9F),
            AutoScaleMode = AutoScaleMode.Font,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
        };

        var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill };
        var label = new Label { Text = prompt, AutoSize = true, Margin = new Padding(0, 0, 0, 6) };
        var box = new TextBox { Text = defaultValue, Width = UiScale.S(520), Margin = new Padding(0, 0, 0, 12) };
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
        };
        var cancel = new Button { Text = "Mégse", DialogResult = DialogResult.Cancel, AutoSize = true };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        layout.Controls.Add(label);
        layout.Controls.Add(box);
        layout.Controls.Add(buttons);
        form.Controls.Add(layout);
        form.AcceptButton = ok;
        form.CancelButton = cancel;

        form.Shown += (_, _) =>
        {
            box.Focus();
            if (selectLength >= 0) box.Select(0, Math.Min(selectLength, box.Text.Length));
            else box.SelectAll();
        };

        ThemeHelper.Apply(form);
        return form.ShowDialog(owner) == DialogResult.OK ? box.Text : null;
    }
}

/// <summary>Többgombos kérdés (felülírás, hiba, megerősítés).</summary>
internal static class Dialogs
{
    public static int Choose(IWin32Window? owner, string title, string message, string[] buttons,
        int defaultIndex = 0, int cancelIndex = -1, Icon? icon = null)
    {
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            Font = new Font("Segoe UI", 9F),
            AutoScaleMode = AutoScaleMode.Font,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(12),
        };

        var layout = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        if (icon != null)
        {
            var pic = new PictureBox { Image = icon.ToBitmap(), SizeMode = PictureBoxSizeMode.AutoSize, Margin = new Padding(0, 0, 12, 0) };
            layout.Controls.Add(pic, 0, 0);
        }
        var label = new Label { Text = message, AutoSize = true, MaximumSize = new Size(UiScale.S(640), 0), Margin = new Padding(0, 0, 0, 14) };
        layout.Controls.Add(label, 1, 0);

        var flow = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, MaximumSize = new Size(UiScale.S(660), 0) };
        int result = cancelIndex;
        for (int i = 0; i < buttons.Length; i++)
        {
            int idx = i;
            var b = new Button { Text = buttons[i], AutoSize = true, MinimumSize = new Size(UiScale.S(90), 0) };
            b.Click += (_, _) => { result = idx; form.DialogResult = DialogResult.OK; };
            flow.Controls.Add(b);
            if (i == defaultIndex) form.AcceptButton = b;
        }
        layout.Controls.Add(flow, 1, 1);
        form.Controls.Add(layout);

        form.KeyPreview = true;
        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape && cancelIndex >= 0) { result = cancelIndex; form.DialogResult = DialogResult.Cancel; }
        };
        form.Shown += (_, _) => (form.AcceptButton as Button)?.Focus();

        ThemeHelper.Apply(form);
        form.ShowDialog(owner);
        return result;
    }

    public static OverwriteChoice AskOverwrite(IWin32Window owner, string srcLabel, long srcSize, DateTime srcTime, string dst)
    {
        string existing;
        try
        {
            var fi = new FileInfo(dst);
            existing = $"{fi.Length:N0} bájt, {fi.LastWriteTime:yyyy.MM.dd HH:mm:ss}";
        }
        catch { existing = "?"; }

        string msg = $"A célfájl már létezik:\n{dst}\n\n" +
                     $"Új:        {srcSize:N0} bájt, {srcTime:yyyy.MM.dd HH:mm:ss}\n" +
                     $"Meglévő: {existing}\n\nForrás: {srcLabel}\n\nFelülírod?";
        var options = new[]
        {
            "&Felülírás", "Mindet f&elülírja", "&Régebbieket felülírja",
            "&Kihagyás", "Mindet ki&hagyja", "Át&nevezés", "Mégse",
        };
        var values = new[]
        {
            OverwriteChoice.Overwrite, OverwriteChoice.OverwriteAll, OverwriteChoice.OverwriteOlder,
            OverwriteChoice.Skip, OverwriteChoice.SkipAll, OverwriteChoice.Rename, OverwriteChoice.Cancel,
        };
        int r = Choose(owner, "Fájl már létezik", msg, options, 0, 6, SystemIcons.Question);
        return r < 0 ? OverwriteChoice.Cancel : values[r];
    }

    public static ErrorChoice AskError(IWin32Window owner, string message)
    {
        var options = new[] { "Ú&jra", "&Kihagyás", "Mindet ki&hagyja", "Mégse" };
        int r = Choose(owner, "Hiba", message, options, 0, 3, SystemIcons.Error);
        return r switch
        {
            0 => ErrorChoice.Retry,
            1 => ErrorChoice.Skip,
            2 => ErrorChoice.SkipAll,
            _ => ErrorChoice.Cancel,
        };
    }
}
