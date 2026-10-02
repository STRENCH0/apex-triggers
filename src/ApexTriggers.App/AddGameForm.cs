using System.Diagnostics;
using ApexTriggers.App.Controls;
using ApexTriggers.App.Resources;
using ApexTriggers.Core.Config;
using ApexTriggers.Core.Steam;

namespace ApexTriggers.App;

/// <summary>Search the Steam library and add games; or pick an exe for a game outside Steam.</summary>
internal sealed class AddGameForm : Form
{
    private readonly AppConfig _config;
    private readonly TextBox _search;
    private readonly Label _count = Theme.Label("", Theme.Small, Theme.Muted);
    private readonly LibraryList _list;
    private List<SteamGame> _library = [];

    public List<GamePreset> Added { get; } = [];

    public AddGameForm(AppConfig config)
    {
        _config = config;
        Theme.Style(this);
        Text = Strings.AddGame_Title;
        ClientSize = new Size(600, 620);
        MinimumSize = new Size(480, 420);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        MaximizeBox = false;
        BackColor = Theme.Panel;

        var top = new Panel { Dock = DockStyle.Top, Height = 104, BackColor = Theme.Panel, Padding = new Padding(20, 16, 20, 0) };
        var label = Theme.Label(Strings.AddGame_Search, Theme.Small, Theme.Muted);
        label.Location = new Point(20, 14);
        var box = new Panel { Location = new Point(20, 36), Height = 42, BackColor = Theme.Bg, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
        box.Paint += (_, e) =>
        {
            Theme.FillRounded(e.Graphics, Theme.Bg, _search!.Focused ? Theme.Accent : Theme.BorderStrong, new RectangleF(0.5f, 0.5f, box.Width - 1, box.Height - 1), 8);
            Glyphs.Search(e.Graphics, new RectangleF(12, 13, 16, 16), Theme.Muted);
        };
        _search = new TextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Theme.Bg,
            ForeColor = Theme.Text,
            Font = Theme.Body,
            PlaceholderText = Strings.AddGame_Placeholder,
            Location = new Point(38, 11),
            Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
            AccessibleName = label.Text,
        };
        _search.TextChanged += (_, _) => Filter();
        _search.GotFocus += (_, _) => box.Invalidate();
        _search.LostFocus += (_, _) => box.Invalidate();
        _search.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Down && _list!.Items.Count > 0) { _list.Focus(); _list.SelectedIndex = 0; e.Handled = true; }
            if (e.KeyCode == Keys.Enter && _list!.Items.Count > 0) { AddRow((LibraryRow)_list.Items[0]); e.SuppressKeyPress = true; }
        };
        box.Controls.Add(_search);
        box.Resize += (_, _) => _search.Width = box.Width - 50;
        _count.Location = new Point(20, 84);
        top.Controls.AddRange([label, box, _count]);
        top.Resize += (_, _) => box.Width = top.Width - 40;

        _list = new LibraryList { Dock = DockStyle.Fill };
        _list.AddClicked += AddRow;
        var listHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 6, 12, 6), BackColor = Theme.Panel };
        listHost.Controls.Add(_list);

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 76, BackColor = Theme.Panel, Padding = new Padding(20, 0, 20, 0) };
        footer.Paint += (_, e) => { using var pen = new Pen(Theme.Border); e.Graphics.DrawLine(pen, 0, 0, footer.Width, 0); };
        var notInSteam = Theme.Label(Strings.AddGame_NotInSteam, Theme.BodyBold);
        notInSteam.Location = new Point(20, 16);
        var hint = Theme.Label(Strings.AddGame_NotInSteamHint, Theme.Small, Theme.Muted);
        hint.Location = new Point(20, 40);
        var pick = new FlatButton(Strings.AddGame_ChooseExe, ButtonKind.Accent) { Height = 40, Glyph = Glyphs.Folder, Anchor = AnchorStyles.Right | AnchorStyles.Top };
        pick.Width = pick.PreferredWidth;
        pick.Click += (_, _) => PickExe();
        footer.Controls.AddRange([notInSteam, hint, pick]);
        footer.Resize += (_, _) => pick.Location = new Point(footer.Width - pick.Width - 20, 18);

        Controls.Add(listHost);
        Controls.Add(footer);
        Controls.Add(top);

        _count.Text = Strings.AddGame_Reading;
        Shown += async (_, _) =>
        {
            _search.Focus();
            _library = await Task.Run(SteamLibrary.InstalledGames);
            if (IsDisposed) return;
            Filter();
        };
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
    }

    private void Filter()
    {
        var q = _search.Text.Trim();
        var rows = _library
            .Where(g => q.Length == 0 || g.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase))
            .Select(g => new LibraryRow(g, IsAdded(g)))
            .ToList();
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var r in rows) _list.Items.Add(r);
        _list.EndUpdate();
        var libraries = _library.Select(g => g.LibraryPath).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        _count.Text = SteamLibrary.SteamPath() is null
            ? Strings.AddGame_SteamNotFound
            : L.F(L.One(libraries) ? Strings.AddGame_Found_One : Strings.AddGame_Found_Many, rows.Count, libraries);
    }

    private bool IsAdded(SteamGame g) => _config.Games.Any(p => p.SteamAppId == g.AppId);

    private void AddRow(LibraryRow row)
    {
        if (row.Added) return;
        var preset = new GamePreset
        {
            Id = $"steam:{row.Game.AppId}",
            Name = row.Game.Name,
            SteamAppId = row.Game.AppId,
            InstallDir = row.Game.InstallDir,
        };
        _config.Games.Add(preset);
        Added.Add(preset);
        var index = _list.Items.IndexOf(row);
        if (index >= 0) _list.Items[index] = row with { Added = true };
    }

    private void PickExe()
    {
        using var dialog = new OpenFileDialog
        {
            Title = Strings.AddGame_ExeTitle,
            Filter = Strings.AddGame_ExeFilter,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var path = Path.GetFullPath(dialog.FileName);
        if (_config.Games.Any(g => string.Equals(g.ExePath, path, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, Strings.AddGame_AlreadyListed, Text);
            return;
        }
        var version = FileVersionInfo.GetVersionInfo(path);
        var name = new[] { version.ProductName, version.FileDescription }.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))?.Trim()
                   ?? Path.GetFileNameWithoutExtension(path);
        var preset = new GamePreset { Id = "exe:" + path.ToLowerInvariant(), Name = name, ExePath = path };
        _config.Games.Add(preset);
        Added.Add(preset);
        Close();
    }

    private sealed record LibraryRow(SteamGame Game, bool Added)
    {
        public override string ToString() => Game.Name;
    }

    /// <summary>Owner-drawn rows; the "Add" button at the right is drawn in and hit-tested by hand.</summary>
    private sealed class LibraryList : ListBox
    {
        private int _hoverButton = -1;

        public event Action<LibraryRow>? AddClicked;

        public LibraryList()
        {
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 58;
            BorderStyle = BorderStyle.None;
            BackColor = Theme.Panel;
            IntegralHeight = false;
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        }

        private Rectangle ButtonRect(Rectangle bounds) => new(bounds.Right - 104, bounds.Y + 12, 96, 34);

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= Items.Count) return;
            var row = (LibraryRow)Items[e.Index];
            var g = e.Graphics;
            var selected = (e.State & DrawItemState.Selected) != 0;
            using (var bg = new SolidBrush(Theme.Panel)) g.FillRectangle(bg, e.Bounds);
            if (selected) Theme.FillRounded(g, Theme.Raised, null, new RectangleF(e.Bounds.X, e.Bounds.Y + 1, e.Bounds.Width, e.Bounds.Height - 2), 8);

            var icon = new RectangleF(e.Bounds.X + 8, e.Bounds.Y + 11, 36, 36);
            GameIcons.Draw(g, GameIcons.Get(row.Game), row.Game.Name, icon, 8, Theme.SmallBold);
            var textLeft = (int)icon.Right + 12;
            var button = ButtonRect(e.Bounds);
            var width = button.X - textLeft - 8;
            var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(g, row.Game.Name, Theme.BodyBold, new Rectangle(textLeft, e.Bounds.Y + 9, width, 20), Theme.Text, flags);
            TextRenderer.DrawText(g, row.Game.InstallDir, Theme.Mono, new Rectangle(textLeft, e.Bounds.Y + 31, width, 18), Theme.Muted,
                flags | TextFormatFlags.PathEllipsis);

            if (row.Added)
            {
                Glyphs.Check(g, new RectangleF(button.X + 10, button.Y + 10, 14, 14), Theme.Muted);
                TextRenderer.DrawText(g, Strings.AddGame_InList, Theme.Small, new Rectangle(button.X + 28, button.Y, button.Width - 28, button.Height),
                    Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
            else
            {
                var hover = _hoverButton == e.Index;
                Theme.FillRounded(g, hover ? Theme.Border : Theme.Raised, Theme.Border, new RectangleF(button.X + 0.5f, button.Y + 0.5f, button.Width - 1, button.Height - 1), 7);
                TextRenderer.DrawText(g, Strings.Common_Add, Theme.Small, button, Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            var index = IndexFromPoint(e.Location);
            var over = index >= 0 && ButtonRect(GetItemRectangle(index)).Contains(e.Location) ? index : -1;
            Cursor = over >= 0 ? Cursors.Hand : Cursors.Default;
            if (over == _hoverButton) return;
            _hoverButton = over;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverButton = -1;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            var index = IndexFromPoint(e.Location);
            if (index >= 0 && ButtonRect(GetItemRectangle(index)).Contains(e.Location))
                AddClicked?.Invoke((LibraryRow)Items[index]);
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);
            if (SelectedItem is LibraryRow row) AddClicked?.Invoke(row);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode is Keys.Enter or Keys.Space && SelectedItem is LibraryRow row)
            {
                AddClicked?.Invoke(row);
                e.Handled = true;
            }
        }
    }
}
