using ApexTriggers.App.Controls;
using ApexTriggers.App.Resources;
using ApexTriggers.Core;
using ApexTriggers.Core.Config;
using ApexTriggers.Core.Protocol;

namespace ApexTriggers.App;

/// <summary>Main window: game list on the left, the selected game's preset editor on the right.</summary>
internal sealed class MainForm : Form
{
    private readonly TriggerController _controller;
    private readonly Pill _padPill = new();
    private readonly Pill _handoverPill = new();
    private readonly GameList _list = new() { Dock = DockStyle.Fill };
    private readonly PictureBox _icon = new() { Size = new Size(52, 52), Location = new Point(24, 20) };
    private readonly Label _name = Theme.Label("", Theme.Title);
    private readonly Label _sub = Theme.Label("", Theme.Mono, Theme.Muted);
    private readonly Pill _runningPill = new() { Height = 32, Anchor = AnchorStyles.Top | AnchorStyles.Right };
    private readonly Panel _editorHost = new() { Dock = DockStyle.Fill, Padding = new Padding(24, 0, 24, 0), BackColor = Theme.Bg };
    private readonly TriggerPanel _left = new(Strings.Main_LeftTrigger, "LT") { Dock = DockStyle.Fill };
    private readonly TriggerPanel _right = new(Strings.Main_RightTrigger, "RT") { Dock = DockStyle.Fill };
    private readonly TableLayoutPanel _editor;
    private readonly Panel _defaultInfo;
    private readonly DarkCheck _preview;
    private readonly DarkCheck _xinput = Theme.Check(Strings.Main_XInputMode, false);
    private readonly ToolTip _tips = Theme.ToolTip();
    private readonly Label _dirtyLabel = Theme.Label(Strings.Main_Unsaved, Theme.Small, Theme.Warn);
    private readonly FlatButton _copy = new(Strings.Main_CopyPreset);
    private readonly FlatButton _reset = new(Strings.Main_Reset);
    private readonly FlatButton _save = new(Strings.Main_Save, ButtonKind.Primary);
    private GamePreset? _selected;
    private bool _dirty;
    private bool _loading;
    private readonly System.Windows.Forms.Timer _previewDelay = new() { Interval = 400 };

    public MainForm(TriggerController controller)
    {
        _controller = controller;
        Theme.Style(this);
        Text = "Apex Triggers";
        ClientSize = new Size(1180, 760);
        MinimumSize = new Size(980, 640);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        // Right side: header, editor, bottom bar.
        var right = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        _editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = Theme.Bg, Margin = new Padding(0) };
        _editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _left.Margin = new Padding(0, 0, 8, 0);
        _right.Margin = new Padding(8, 0, 0, 0);
        _editor.Controls.Add(_left, 0, 0);
        _editor.Controls.Add(_right, 1, 0);
        _left.Changed += OnEdited;
        _right.Changed += OnEdited;
        _defaultInfo = DefaultInfo();
        _editorHost.Controls.Add(_editor);
        _editorHost.Controls.Add(_defaultInfo);

        var header = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Theme.Bg };
        _icon.Paint += (_, e) => GameIcons.Draw(e.Graphics, _selected is null ? null : GameIcons.Get(_selected),
            _selected?.Name ?? "—", new RectangleF(0, 0, 52, 52), 12, Theme.Heading);
        _icon.BackColor = Theme.Bg;
        _name.Location = new Point(90, 22);
        _sub.Location = new Point(92, 56);
        _sub.AutoSize = false;
        _sub.AutoEllipsis = true;
        _sub.Height = 18;
        _xinput.Font = Theme.Small;
        _xinput.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _tips.SetToolTip(_xinput, Strings.Main_XInputModeHint);
        _xinput.CheckedChanged += (_, _) =>
        {
            if (!_loading && _selected is not null) MarkDirty();
        };
        header.Controls.AddRange([_icon, _name, _sub, _runningPill, _xinput]);
        header.Resize += (_, _) => LayoutHeader(header);

        // Top padding of 1 keeps the children off the divider line painted at y = 0.
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 64, BackColor = Theme.Bg, Padding = new Padding(24, 1, 24, 0) };
        _preview = Theme.Check(Strings.Main_LivePreview, controller.Config.Settings.LivePreview);
        _preview.Font = Theme.Small;
        _preview.Anchor = AnchorStyles.Left;
        _preview.CheckedChanged += (_, _) =>
        {
            _controller.Config.Settings.LivePreview = _preview.Checked;
            ConfigStore.Save(_controller.Config);
            if (_preview.Checked && _dirty) PushPreview();
            else if (!_preview.Checked) _controller.EndPreview();
        };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, BackColor = Theme.Bg, Padding = new Padding(0, 13, 0, 0) };
        foreach (var b in new[] { _copy, _reset, _save })
        {
            b.Width = b.PreferredWidth;
            b.Margin = new Padding(8, 0, 0, 0);
        }
        _dirtyLabel.Margin = new Padding(0, 11, 8, 0);
        buttons.Controls.AddRange([_dirtyLabel, _copy, _reset, _save]);
        var previewHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        previewHost.Resize += (_, _) => _preview.Location = new Point(0, (previewHost.Height - _preview.Height) / 2 + 1);
        previewHost.Controls.Add(_preview);
        bottom.Controls.Add(previewHost);
        bottom.Controls.Add(buttons);
        bottom.Paint += (_, e) => { using var pen = new Pen(Theme.Divider); e.Graphics.DrawLine(pen, 0, 0, bottom.Width, 0); };
        _copy.Click += (_, _) => ShowCopyMenu();
        _reset.Click += (_, _) => ResetPreset();
        _save.Click += (_, _) => Save();

        var spacer = new Panel { Dock = DockStyle.Bottom, Height = 16, BackColor = Theme.Bg };
        right.Controls.Add(_editorHost);
        right.Controls.Add(spacer);
        right.Controls.Add(bottom);
        right.Controls.Add(header);

        // Left: game list.
        var left = new Panel { Dock = DockStyle.Left, Width = 300, BackColor = Theme.Bg, Padding = new Padding(12, 16, 12, 12) };
        var listHeader = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Bg };
        var caps = Theme.Label(Strings.Main_Games, Theme.Caps, Theme.Muted);
        caps.Location = new Point(8, 8);
        var add = new FlatButton(Strings.Common_Add, ButtonKind.Subtle) { Height = 32, Radius = 7, Padding = new Padding(10, 0, 12, 0), Glyph = Glyphs.Plus };
        add.Width = add.PreferredWidth;
        add.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        listHeader.Controls.AddRange([caps, add]);
        listHeader.Resize += (_, _) => add.Location = new Point(listHeader.Width - add.Width - 4, 0);
        add.Click += (_, _) => AddGame();
        left.Controls.Add(_list);
        left.Controls.Add(listHeader);
        left.Paint += (_, e) => { using var pen = new Pen(Theme.Divider); e.Graphics.DrawLine(pen, left.Width - 1, 0, left.Width - 1, left.Height); };
        _list.SelectedIndexChanged += (_, _) => OnListSelection();
        _list.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            var index = _list.IndexFromPoint(e.Location);
            if (index >= 0) _list.SelectedIndex = index;
        };
        _list.ContextMenuStrip = ListMenu();

        // Top bar: pad status and settings.
        var top = new Panel { Dock = DockStyle.Top, Height = 60, BackColor = Theme.Bg, Padding = new Padding(20, 12, 20, 12) };
        var pills = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Theme.Bg, WrapContents = false };
        pills.Controls.AddRange([_padPill, _handoverPill]);
        var settings = new FlatButton(Strings.Common_Settings) { Dock = DockStyle.Right, Height = 36, Glyph = Glyphs.Gear };
        settings.Width = settings.PreferredWidth;
        settings.Click += (_, _) => OpenSettings();
        top.Controls.Add(pills);
        top.Controls.Add(settings);
        top.Paint += (_, e) => { using var pen = new Pen(Theme.Divider); e.Graphics.DrawLine(pen, 0, top.Height - 1, top.Width, top.Height - 1); };

        Controls.Add(right);
        Controls.Add(left);
        Controls.Add(top);

        _previewDelay.Tick += (_, _) => OnPreviewDelay();
        _controller.Changed += OnControllerChanged;
        RefreshList();
        RefreshStatus();
        SelectGame(null);
    }

    /// <summary>Running pill on the title line, the XInput check on the subtitle line, both at the right edge.</summary>
    private void LayoutHeader(Control header)
    {
        _runningPill.Location = new Point(header.Width - _runningPill.Width - 24, 16);
        _xinput.Location = new Point(header.Width - _xinput.Width - 24, 52);
        _sub.Width = Math.Max(100, header.Width - 92 - 24 - (_xinput.Visible ? _xinput.Width + 16 : 0));
    }

    private Panel DefaultInfo()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg, Visible = false };
        var card = new Panel { Location = new Point(0, 0), Size = new Size(600, 120), BackColor = Theme.Bg };
        var title = Theme.Label(Strings.Common_DefaultPreset, Theme.Heading);
        title.Location = new Point(20, 18);
        var text = Theme.Label(Strings.Main_DefaultPresetText, Theme.Body, Theme.Muted);
        text.AutoSize = false;
        text.Location = new Point(20, 46);
        text.Size = new Size(560, 60);
        card.Controls.AddRange([title, text]);
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(Theme.BorderStrong) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash };
            using var path = Theme.Rounded(new RectangleF(0.5f, 0.5f, card.Width - 1, card.Height - 1), 10);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.DrawPath(pen, path);
        };
        panel.Controls.Add(card);
        return panel;
    }

    private ContextMenuStrip ListMenu()
    {
        var menu = new ContextMenuStrip { Renderer = new Theme.MenuRenderer() };
        var remove = new ToolStripMenuItem(Strings.Main_RemoveFromList);
        remove.Click += (_, _) => RemoveSelected();
        menu.Items.Add(remove);
        menu.Opening += (_, e) => e.Cancel = _list.SelectedRow?.Game is null;
        return menu;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Delete && _list.Focused) RemoveSelected();
        if (e.Control && e.KeyCode == Keys.S && _dirty) Save();
    }

    // ---- list ----

    private void RefreshList()
    {
        var running = _controller.RunningGames;
        var rows = new List<GameRow>
        {
            new(null, Strings.Common_OutsideGames, Strings.Main_DefaultSummary, false, null),
        };
        rows.AddRange(_controller.Config.Games.Select(g => new GameRow(g, g.Name,
            L.Summary(g.Left.ToEffect(), g.Right.ToEffect()), running.Contains(g), GameIcons.Get(g))));

        _loading = true;
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var row in rows) _list.Items.Add(row);
        var index = rows.FindIndex(r => ReferenceEquals(r.Game, _selected));
        _list.SelectedIndex = Math.Max(0, index);
        _list.EndUpdate();
        _loading = false;
    }

    private void OnListSelection()
    {
        if (_loading) return;
        var row = _list.SelectedRow;
        if (row is null || ReferenceEquals(row.Game, _selected)) return;
        if (!ConfirmLeave())
        {
            _loading = true;
            _list.SelectedIndex = Math.Max(0, _list.Items.Cast<GameRow>().ToList().FindIndex(r => ReferenceEquals(r.Game, _selected)));
            _loading = false;
            return;
        }
        SelectGame(row.Game);
    }

    /// <summary>Unsaved edits: save, discard, or stay. False means stay.</summary>
    public bool ConfirmLeave()
    {
        if (!_dirty) return true;
        var answer = MessageBox.Show(this,
            L.F(Strings.Main_ConfirmSave, _selected?.Name),
            "Apex Triggers", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Cancel) return false;
        if (answer == DialogResult.Yes) Save();
        else Discard();
        return true;
    }

    private void SelectGame(GamePreset? game)
    {
        _selected = game;
        _dirty = false;
        _previewDelay.Stop();
        _controller.EndPreview();
        var isDefault = game is null;
        _editor.Visible = !isDefault;
        _defaultInfo.Visible = isDefault;
        _copy.Enabled = _reset.Enabled = !isDefault;
        _xinput.Visible = !isDefault;
        _save.Enabled = false;
        _dirtyLabel.Visible = false;

        _name.Text = game?.Name ?? Strings.Common_OutsideGames;
        _sub.Text = game is null
            ? Strings.Main_DefaultSubtitle
            : (game.IsSteam ? "Steam · " + game.InstallDir : L.F(Strings.Main_ManualSource, game.ExePath));
        _icon.Invalidate();

        if (game is not null)
        {
            _left.Setting = game.Left;
            _right.Setting = game.Right;
            SetXInputCheck(game.XInputMode);
        }
        LayoutHeader(_xinput.Parent!);
        RefreshRunning();
    }

    // ---- editing ----

    private void OnEdited()
    {
        if (_selected is null) return;
        MarkDirty();
        if (!_preview.Checked) return;
        _previewDelay.Stop();
        _previewDelay.Start();
    }

    private void MarkDirty()
    {
        _dirty = true;
        _save.Enabled = true;
        _dirtyLabel.Visible = true;
    }

    private void SetXInputCheck(bool value)
    {
        _loading = true;
        _xinput.Checked = value;
        _loading = false;
    }

    /// <summary>
    /// Each effect command re-seats the trigger motors, so a slider drag must not stream them:
    /// the preview goes out once the slider has been let go and left alone for the delay.
    /// </summary>
    private void OnPreviewDelay()
    {
        if (MouseButtons.HasFlag(MouseButtons.Left)) return; // still dragging; keep waiting
        _previewDelay.Stop();
        if (_dirty && _preview.Checked) PushPreview();
    }

    private void PushPreview()
    {
        if (_controller.Pad.State.Status == PadStatus.NotConnected) return;
        _controller.SetPreview(_left.Setting.ToEffect(), _right.Setting.ToEffect());
    }

    private void Save()
    {
        if (_selected is null) return;
        _selected.Left = _left.Setting;
        _selected.Right = _right.Setting;
        _selected.XInputMode = _xinput.Checked;
        _dirty = false;
        _previewDelay.Stop();
        _save.Enabled = false;
        _dirtyLabel.Visible = false;
        _controller.EndPreview();
        _controller.ConfigChanged();
        RefreshList();
    }

    private void Discard()
    {
        _dirty = false;
        _previewDelay.Stop();
        _controller.EndPreview();
        if (_selected is not null)
        {
            _left.Setting = _selected.Left;
            _right.Setting = _selected.Right;
            SetXInputCheck(_selected.XInputMode);
        }
        _save.Enabled = false;
        _dirtyLabel.Visible = false;
    }

    private void ResetPreset()
    {
        _left.Setting = new TriggerSetting();
        _right.Setting = new TriggerSetting();
        OnEdited();
    }

    private void ShowCopyMenu()
    {
        var menu = new ContextMenuStrip { Renderer = new Theme.MenuRenderer() };
        foreach (var game in _controller.Config.Games.Where(g => !ReferenceEquals(g, _selected)))
        {
            var item = new ToolStripMenuItem($"{game.Name}  —  {L.Summary(game.Left.ToEffect(), game.Right.ToEffect())}");
            var source = game;
            item.Click += (_, _) =>
            {
                _left.Setting = source.Left;
                _right.Setting = source.Right;
                OnEdited();
            };
            menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new ToolStripMenuItem(Strings.Main_NoOtherGames) { Enabled = false });
        menu.Closed += (_, _) => BeginInvoke(menu.Dispose);
        menu.Show(_copy, new Point(0, -menu.PreferredSize.Height - 4));
    }

    // ---- games ----

    private void AddGame()
    {
        if (!ConfirmLeave()) return;
        using var dialog = new AddGameForm(_controller.Config);
        dialog.ShowDialog(this);
        if (dialog.Added.Count == 0) return;
        _controller.ConfigChanged();
        _selected = dialog.Added[^1];
        RefreshList();
        SelectGame(_selected);
    }

    private void RemoveSelected()
    {
        var game = _list.SelectedRow?.Game;
        if (game is null) return;
        var answer = MessageBox.Show(this, L.F(Strings.Main_ConfirmRemove, game.Name),
            "Apex Triggers", MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (answer != DialogResult.OK) return;
        _dirty = false;
        _controller.Config.Games.Remove(game);
        _controller.ConfigChanged();
        _selected = null;
        RefreshList();
        SelectGame(null);
    }

    private void OpenSettings()
    {
        using var dialog = new SettingsForm(_controller);
        dialog.ShowDialog(this);
        if (dialog.LanguageChanged) LanguageChanged?.Invoke();
        else if (dialog.ImportedGames)
        {
            RefreshList();
            SelectGame(_selected);
        }
    }

    /// <summary>The settings dialog switched the language; the tray app rebuilds the window.</summary>
    public event Action? LanguageChanged;

    // ---- status ----

    private void OnControllerChanged()
    {
        if (!IsHandleCreated || IsDisposed) return;
        BeginInvoke(() =>
        {
            RefreshStatus();
            RefreshRunning();
        });
    }

    private void RefreshRunning()
    {
        var running = _controller.RunningGames;
        _loading = true;
        var selectedIndex = _list.SelectedIndex;
        for (var i = 0; i < _list.Items.Count; i++)
        {
            var row = (GameRow)_list.Items[i];
            var isRunning = row.Game is not null && running.Contains(row.Game);
            if (isRunning != row.Running) _list.Items[i] = row with { Running = isRunning };
        }
        if (_list.SelectedIndex != selectedIndex) _list.SelectedIndex = selectedIndex;
        _loading = false;

        var active = _selected is not null && ReferenceEquals(_controller.ActiveGame, _selected);
        var isRunningSelected = _selected is not null && running.Contains(_selected);
        _runningPill.Visible = isRunningSelected;
        _runningPill.Update(p =>
        {
            p.Fill = Theme.AccentWash;
            p.Outline = null;
            p.Dot = Theme.Accent;
            p.TextColor = Theme.AccentSoft;
            p.PrimaryFont = Theme.Small;
            p.Primary = active ? Strings.Main_RunningApplied : Strings.Common_Running;
        });
        LayoutHeader(_runningPill.Parent!);
    }

    private void RefreshStatus()
    {
        var state = _controller.Pad.State;
        _padPill.Update(p =>
        {
            p.Fill = Theme.Panel;
            p.Outline = Theme.Border;
            if (state.Info is { } info)
            {
                p.Dot = Theme.Green;
                p.Primary = Status.Model(info);
                p.Secondary = $"{Status.Connection(info)} · {Status.Battery(info)}";
            }
            else
            {
                p.Dot = Theme.Muted;
                p.Primary = Strings.Common_NoController;
                p.Secondary = Strings.Main_PlugIn;
            }
        });
        var pad = _controller.Pad;
        _handoverPill.Visible = state.Status != PadStatus.NotConnected || pad.Yielding;
        _handoverPill.Update(p =>
        {
            p.Outline = null;
            p.PrimaryFont = Theme.Small;
            if (pad.Yielding || pad.HoldXInput && state.Status == PadStatus.Connected)
            {
                p.Fill = Theme.AccentWash;
                p.TextColor = Theme.AccentSoft;
                p.CheckMark = false;
                p.Dot = Theme.Accent;
                p.Primary = pad.Yielding ? Strings.Status_Bridge : Strings.Status_XInputHold;
            }
            else if (state.Status == PadStatus.HandedToSteam)
            {
                p.Fill = Theme.GreenWash;
                p.TextColor = Theme.GreenText;
                p.CheckMark = true;
                p.Dot = null;
                p.Primary = Strings.Status_Handed;
            }
            else
            {
                p.Fill = Theme.WarnWash;
                p.TextColor = Theme.WarnText;
                p.CheckMark = false;
                p.Dot = Theme.Warn;
                p.Primary = state.Info is { SupportsHandover: false }
                    ? Strings.Status_FirmwareCantHandOver
                    : Strings.Status_NotHanded;
            }
        });
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !ConfirmLeave())
        {
            e.Cancel = true;
            return;
        }
        _controller.EndPreview();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _controller.Changed -= OnControllerChanged;
            _previewDelay.Dispose();
            _tips.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>Status phrases shared by the window, settings and tray.</summary>
internal static class Status
{
    public static string Model(PadInfo info) => info.IsApex5 ? info.ModelName : L.F(Strings.Status_UnknownDevice, info.DeviceType);

    public static string Connection(PadInfo info) => info.ConnectionRaw switch
    {
        1 => Strings.Status_Cable,
        2 => Strings.Status_Dongle,
        _ => Strings.Status_ConnectionUnknown,
    };

    public static string Battery(PadInfo info) =>
        info.Charged ? Strings.Status_Charged
        : info.Charging ? Strings.Status_Charging
        : L.F(Strings.Status_Battery, Math.Min(100, info.BatteryLevel * 20));
}
