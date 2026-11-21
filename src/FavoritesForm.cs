namespace DualDeckCommander;

/// <summary>Kedvenc mappák szerkesztése: átnevezés, útvonal, sorrend, törlés.</summary>
internal sealed class FavoritesForm : Form
{
    private readonly ListView _list;
    private readonly List<FavoriteDir> _items;

    public List<FavoriteDir> Result => _items;

    private static int S(int v) => UiScale.S(v);

    public FavoritesForm(IEnumerable<FavoriteDir> favorites)
    {
        _items = favorites.Select(f => new FavoriteDir { Name = f.Name, Path = f.Path }).ToList();

        Text = "Kedvenc mappák";
        Font = new Font("Segoe UI", 9F);
        StartPosition = FormStartPosition.CenterParent;
        Size = UiScale.S(680, 420);
        MinimumSize = UiScale.S(480, 300);
        ShowIcon = false;
        ShowInTaskbar = false;
        MinimizeBox = false;
        Padding = new Padding(S(10));

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            BorderStyle = BorderStyle.FixedSingle,
        };
        _list.Columns.Add("Név", S(180));
        _list.Columns.Add("Mappa", S(400));
        _list.DoubleClick += (_, _) => Rename();
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete) Remove();
            else if (e.KeyCode == Keys.F2) Rename();
            else if (e.Alt && e.KeyCode == Keys.Up) MoveItem(-1);
            else if (e.Alt && e.KeyCode == Keys.Down) MoveItem(1);
        };

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            Padding = new Padding(S(10), 0, 0, 0),
        };
        Button B(string text, Action a)
        {
            var b = new Button { Text = text, Width = S(130), Height = S(30), Margin = new Padding(0, 0, 0, S(6)) };
            b.Click += (_, _) => { a(); _list.Focus(); };
            buttons.Controls.Add(b);
            return b;
        }
        B("Átnevezés...", Rename);
        B("Útvonal...", EditPath);
        B("Fel", () => MoveItem(-1));
        B("Le", () => MoveItem(1));
        B("Törlés", Remove);
        var spacer = new Label { Height = S(20), Width = S(10) };
        buttons.Controls.Add(spacer);
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = S(130), Height = S(30), Margin = new Padding(0, 0, 0, S(6)) };
        var cancel = new Button { Text = "Mégse", DialogResult = DialogResult.Cancel, Width = S(130), Height = S(30) };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;

        Controls.Add(_list);
        Controls.Add(buttons);
        Fill(0);
        ThemeHelper.Apply(this);
        _list.BackColor = Theme.Current.InputBack;
        _list.ForeColor = Theme.Current.Text;
        _list.HandleCreated += (_, _) => ThemeHelper.SetDarkScrollbars(_list, Theme.Current.IsDark);
    }

    private int Selected => _list.SelectedIndices.Count > 0 ? _list.SelectedIndices[0] : -1;

    private void Fill(int select)
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var f in _items) _list.Items.Add(new ListViewItem(new[] { f.Name, f.Path }));
        _list.EndUpdate();
        if (_items.Count > 0)
        {
            select = Math.Clamp(select, 0, _items.Count - 1);
            _list.Items[select].Selected = true;
            _list.Items[select].Focused = true;
            _list.EnsureVisible(select);
        }
    }

    private void Rename()
    {
        int i = Selected;
        if (i < 0) return;
        var n = InputDialog.Show(this, "Átnevezés", "A kedvenc neve:", _items[i].Name);
        if (!string.IsNullOrWhiteSpace(n)) { _items[i].Name = n.Trim(); Fill(i); }
    }

    private void EditPath()
    {
        int i = Selected;
        if (i < 0) return;
        var p = InputDialog.Show(this, "Útvonal", "A mappa (archívum belseje is lehet):", _items[i].Path);
        if (!string.IsNullOrWhiteSpace(p)) { _items[i].Path = p.Trim().Trim('"'); Fill(i); }
    }

    private void MoveItem(int delta)
    {
        int i = Selected, j = i + delta;
        if (i < 0 || j < 0 || j >= _items.Count) return;
        (_items[i], _items[j]) = (_items[j], _items[i]);
        Fill(j);
    }

    private void Remove()
    {
        int i = Selected;
        if (i < 0) return;
        _items.RemoveAt(i);
        Fill(i);
    }
}
