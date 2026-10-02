using System.Diagnostics;
using System.Text.Json;
using ApexTriggers.App.Controls;
using ApexTriggers.App.Resources;
using ApexTriggers.Core;
using ApexTriggers.Core.Config;

namespace ApexTriggers.App;

internal sealed class SettingsForm : Form
{
    private readonly TriggerController _controller;
    private readonly FlowLayoutPanel _content;
    private readonly string? _initialLanguage;

    public bool LanguageChanged => _controller.Config.Settings.Language != _initialLanguage;

    private AppSettings Settings => _controller.Config.Settings;

    public SettingsForm(TriggerController controller)
    {
        _controller = controller;
        _initialLanguage = controller.Config.Settings.Language;
        Theme.Style(this);
        Text = Strings.Common_Settings;
        ClientSize = new Size(760, 760);
        MinimumSize = new Size(640, 480);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };

        _content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(20),
            BackColor = Theme.Bg,
        };
        Controls.Add(_content);
        _content.Resize += (_, _) => FitWidths();

        if (SpaceStation.RunningProcesses().Count > 0) _content.Controls.Add(SpaceStationBanner());
        _content.Controls.Add(PadSection());
        _content.Controls.Add(AutoApplySection());
        _content.Controls.Add(AppSection());
        _content.Controls.Add(DataSection());
        _content.Controls.Add(VersionLabel());
        FitWidths();
    }

    private void FitWidths()
    {
        var width = _content.ClientSize.Width - _content.Padding.Horizontal - (_content.VerticalScroll.Visible ? 2 : 0);
        foreach (Control c in _content.Controls) c.Width = Math.Max(400, width);
    }

    private void Save() => ConfigStore.Save(_controller.Config);

    // ---- sections ----

    private Control SpaceStationBanner()
    {
        var banner = new Section(Theme.WarnWash, Theme.Blend(Theme.Warn, Theme.Bg, 0.35));
        var title = Theme.Label(Strings.Common_SpaceStationRunning, Theme.BodyBold, Theme.WarnText);
        var text = Theme.Label(Strings.Settings_SpaceStationText, Theme.Small, Theme.Hex("#D7CBB2"));
        var how = new FlatButton(Strings.Settings_HowToTurnOff, ButtonKind.Warning) { Height = 34 };
        how.Width = how.PreferredWidth;
        how.Click += (_, _) => MessageBox.Show(this, Strings.Settings_HowToSteps,
            title.Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        banner.AddRow(Stack(title, text), how);
        return banner;
    }

    private Control PadSection()
    {
        var section = new Section();
        section.AddTitle(Strings.Settings_Controller);
        var state = _controller.Pad.State;
        var info = state.Info;

        var grid = new TableLayoutPanel { ColumnCount = 3, AutoSize = true, Dock = DockStyle.Top, BackColor = Theme.Panel, Margin = new Padding(0, 0, 0, 14) };
        for (var i = 0; i < 3; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        grid.Controls.Add(Field(Strings.Settings_Model, info is null ? "—" : Status.Model(info)), 0, 0);
        grid.Controls.Add(Field(Strings.Settings_Connection, info is null ? Strings.Settings_NotConnected : Status.Connection(info)), 1, 0);
        grid.Controls.Add(Field(Strings.Settings_Firmware, info?.MainFirmware ?? "—", mono: true), 2, 0);
        section.AddControl(grid);

        var handed = state.Status == PadStatus.HandedToSteam;
        var dot = new Pill
        {
            Height = 22, Fill = Theme.Panel, Outline = null, Dot = handed ? Theme.Green : Theme.Muted, PrimaryFont = Theme.BodyBold, Margin = new Padding(0),
        };
        dot.Update(p => p.Primary = handed
            ? Strings.Settings_HandoverOn
            : info is { SupportsHandover: false }
                ? Strings.Status_FirmwareCantHandOver
                : Strings.Settings_HandoverOff);
        var owner = state.Transport?.ControlBy is { Length: > 0 } by ? by : "—";
        var note = Theme.Label(handed
                ? L.F(Strings.Settings_OwnerNote, owner)
                : info is { SupportsHandover: false }
                    ? L.F(Strings.Settings_FirmwareNote, Core.Protocol.PadInfo.MinHandoverFirmware)
                    : Strings.Settings_PlainXInput,
            Theme.Small, Theme.Muted);
        note.MaximumSize = new Size(440, 0);
        note.Margin = new Padding(0, 4, 0, 0);

        var toggle = new FlatButton(handed ? Strings.Settings_ReturnToNormal : Strings.Settings_HandToSteam)
        {
            Enabled = info is not null && (handed || info.SupportsHandover),
        };
        toggle.Width = toggle.PreferredWidth;
        toggle.Click += async (_, _) =>
        {
            if (MessageBox.Show(this, Strings.Settings_HandoverWarning, Text, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;
            toggle.Enabled = false;
            await _controller.SetHandoverAsync(!handed);
            Rebuild();
        };
        var divider = new Panel { Height = 1, Dock = DockStyle.Top, BackColor = Theme.Border, Margin = new Padding(0, 0, 0, 14) };
        section.AddControl(divider);
        section.AddRow(Stack(dot, note), toggle);
        return section;
    }

    private Control AutoApplySection()
    {
        var section = new Section();
        section.AddTitle(Strings.Settings_AutoApply);
        section.AddControl(Bound(Strings.Settings_ApplyOnGameStart,
            Settings.AutoApply, v => { Settings.AutoApply = v; _controller.ConfigChanged(); }));
        section.AddControl(Bound(Strings.Settings_ResendOnReconnect,
            Settings.ReapplyOnReconnect, v => { Settings.ReapplyOnReconnect = v; Save(); }));

        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Bg,
            ForeColor = Theme.Text,
            Width = 140,
            AccessibleName = Strings.Settings_PollInterval,
        };
        // One resource per choice: Russian takes a different plural form for each of 1, 2 and 5.
        int[] seconds = [1, 2, 5];
        combo.Items.AddRange([Strings.Settings_Poll1, Strings.Settings_Poll2, Strings.Settings_Poll5]);
        combo.SelectedIndex = Math.Max(0, Array.IndexOf(seconds, Settings.PollSeconds));
        combo.SelectedIndexChanged += (_, _) => { Settings.PollSeconds = seconds[combo.SelectedIndex]; Save(); };
        section.AddRow(Theme.Label(Strings.Settings_PollLabel), combo);
        return section;
    }

    private Control AppSection()
    {
        var section = new Section();
        section.AddTitle(Strings.Settings_App);

        var group = new Segmented();
        // Language names stay in their own language whatever the UI language, so they are not resources.
        var ru = new FlatButton("Русский", ButtonKind.Segment) { Height = 32, Radius = 6, Checked = L.Ru };
        var en = new FlatButton("English", ButtonKind.Segment) { Height = 32, Radius = 6, Checked = !L.Ru };
        foreach (var b in new[] { ru, en }) { b.Width = b.PreferredWidth; b.Margin = new Padding(0); }
        ru.Click += (_, _) => SetLanguage("ru");
        en.Click += (_, _) => SetLanguage("en");
        group.Controls.AddRange([ru, en]);
        section.AddRow(Theme.Label(Strings.Settings_Language), group);

        section.AddControl(Bound(Strings.Common_StartWithWindows, Autostart.IsEnabled, Autostart.Set));
        section.AddControl(Bound(Strings.Settings_MinimizeToTray,
            Settings.MinimizeToTray, v => { Settings.MinimizeToTray = v; Save(); }));
        section.AddControl(Bound(Strings.Settings_NotifyOnPresetChange,
            Settings.NotifyOnPresetChange, v => { Settings.NotifyOnPresetChange = v; Save(); }));
        return section;
    }

    private void SetLanguage(string code)
    {
        if (L.Code == code) return;
        Settings.Language = code;
        Save();
        L.Set(code);
        Rebuild();
    }

    /// <summary>Product name and version under the last card; the name is not translated.</summary>
    private static Control VersionLabel()
    {
        var label = Theme.Label($"Apex Triggers {Program.Version}", Theme.Small, Theme.Muted);
        label.Margin = new Padding(2, 0, 0, 8);
        return label;
    }

    private Control DataSection()
    {
        var section = new Section();
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Panel, Margin = new Padding(0) };
        foreach (var (text, action) in new (string, Action)[]
                 {
                     (Strings.Settings_Export, Export),
                     (Strings.Settings_Import, Import),
                     (Strings.Settings_LogFolder, () => Process.Start("explorer.exe", Program.LogDirectory)),
                 })
        {
            var b = new FlatButton(text) { Height = 36, Radius = 7, Margin = new Padding(8, 0, 0, 0) };
            b.Width = b.PreferredWidth;
            b.Click += (_, _) => action();
            buttons.Controls.Add(b);
        }
        section.AddRow(Theme.Label(Strings.Settings_Data, Theme.Heading), buttons);
        return section;
    }

    private void Export()
    {
        using var dialog = new SaveFileDialog { FileName = "apex-triggers.json", Filter = "JSON|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        ConfigStore.Save(_controller.Config, dialog.FileName);
    }

    private void Import()
    {
        using var dialog = new OpenFileDialog { Filter = "JSON|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        AppConfig? imported;
        try
        {
            imported = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(dialog.FileName), ConfigStore.Json);
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            MessageBox.Show(this, e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (imported is null) return;
        var added = 0;
        var updated = 0;
        foreach (var game in imported.Games)
        {
            var existing = _controller.Config.Games.FirstOrDefault(g => g.Id == game.Id);
            if (existing is null) { _controller.Config.Games.Add(game); added++; }
            else { existing.Left = game.Left; existing.Right = game.Right; updated++; }
        }
        _controller.ConfigChanged();
        MessageBox.Show(this, L.F(Strings.Settings_ImportResult, added, updated), Text);
        ImportedGames = true;
    }

    /// <summary>The main window reloads its list when this is set.</summary>
    public bool ImportedGames { get; private set; }

    private void Rebuild()
    {
        var scroll = _content.VerticalScroll.Value;
        _content.SuspendLayout();
        foreach (Control c in _content.Controls) c.Dispose();
        _content.Controls.Clear();
        Text = Strings.Common_Settings;
        if (SpaceStation.RunningProcesses().Count > 0) _content.Controls.Add(SpaceStationBanner());
        _content.Controls.Add(PadSection());
        _content.Controls.Add(AutoApplySection());
        _content.Controls.Add(AppSection());
        _content.Controls.Add(DataSection());
        _content.Controls.Add(VersionLabel());
        FitWidths();
        _content.ResumeLayout();
        _content.VerticalScroll.Value = Math.Min(scroll, _content.VerticalScroll.Maximum);
    }

    // ---- building blocks ----

    private static Control Field(string caption, string value, bool mono = false)
    {
        var stack = Stack(Theme.Label(caption, Theme.Small, Theme.Muted), Theme.Label(value, mono ? Theme.Mono : Theme.Body));
        stack.Margin = new Padding(0, 0, 12, 0);
        return stack;
    }

    private static FlowLayoutPanel Stack(params Control[] controls)
    {
        var stack = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, BackColor = Color.Transparent, Margin = new Padding(0) };
        foreach (var c in controls) c.Margin = new Padding(0, 0, 0, 3);
        stack.Controls.AddRange(controls);
        return stack;
    }

    private static DarkCheck Bound(string text, bool value, Action<bool> set)
    {
        var check = Theme.Check(text, value);
        check.CheckedChanged += (_, _) => set(check.Checked);
        return check;
    }

    /// <summary>Segmented switch: one rounded frame around borderless segments.</summary>
    private sealed class Segmented : FlowLayoutPanel
    {
        public Segmented()
        {
            AutoSize = true;
            WrapContents = false;
            BackColor = Theme.Bg;
            Padding = new Padding(3);
            Margin = new Padding(0);
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Back(this));
            Theme.FillRounded(e.Graphics, Theme.Bg, Theme.Border, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 8);
        }
    }

    /// <summary>Rounded card holding rows: a title, full-width controls, or "left … right" rows.</summary>
    private sealed class Section : Panel
    {
        private readonly TableLayoutPanel _rows;
        private readonly Color _fill;
        private readonly Color _border;

        public Section(Color? fill = null, Color? border = null)
        {
            _fill = fill ?? Theme.Panel;
            _border = border ?? Theme.Border;
            BackColor = _fill;
            Padding = new Padding(16, 14, 16, 14);
            Margin = new Padding(0, 0, 0, 16);
            // Width comes from the dialog (FitWidths), height from the rows. An auto-sized panel
            // around a docked table collapses to zero width, which is what broke this dialog.
            SetStyle(ControlStyles.ResizeRedraw, true);
            _rows = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Location = new Point(Padding.Left, Padding.Top),
                BackColor = _fill,
                Margin = new Padding(0),
            };
            _rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _rows.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Controls.Add(_rows);
            Resize += (_, _) => FitRows();
            _rows.SizeChanged += (_, _) => Height = _rows.Height + Padding.Vertical;
        }

        private void FitRows()
        {
            var width = Math.Max(100, Width - Padding.Horizontal);
            _rows.MinimumSize = new Size(width, 0);
            _rows.MaximumSize = new Size(width, 0);
            foreach (Control c in _rows.Controls)
                if (c is DarkCheck check) check.WrapWidth = width - 4;
        }

        public void AddTitle(string title)
        {
            var label = Theme.Label(title, Theme.Heading);
            label.Margin = new Padding(0, 0, 0, 10);
            AddControl(label);
        }

        public void AddControl(Control c)
        {
            var row = _rows.RowCount++;
            _rows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _rows.Controls.Add(c, 0, row);
            _rows.SetColumnSpan(c, 2);
            if (c.Dock == DockStyle.Top) c.Dock = DockStyle.Fill;
            else if (c is not DarkCheck) c.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        }

        public void AddRow(Control left, Control right)
        {
            var row = _rows.RowCount++;
            _rows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            left.Anchor = AnchorStyles.Left;
            right.Anchor = AnchorStyles.Right;
            right.Margin = new Padding(12, 2, 0, 2);
            _rows.Controls.Add(left, 0, row);
            _rows.Controls.Add(right, 1, row);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Back(this));
            Theme.FillRounded(e.Graphics, _fill, _border, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 10);
        }
    }
}
