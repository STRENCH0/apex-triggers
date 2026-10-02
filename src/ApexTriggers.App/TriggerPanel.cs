using ApexTriggers.App.Controls;
using ApexTriggers.App.Resources;
using ApexTriggers.Core.Config;
using ApexTriggers.Core.Protocol;

namespace ApexTriggers.App;

/// <summary>Editor card for one trigger: mode picker, travel zone, parameter sliders.</summary>
internal sealed class TriggerPanel : Panel
{
    private readonly TableLayoutPanel _layout;
    private readonly Dictionary<TriggerMode, FlatButton> _modeButtons = new();
    private readonly ZoneBar _zone = new() { Dock = DockStyle.Fill, Margin = new Padding(0) };
    private readonly Label _caption = Theme.Label("", Theme.Small, Theme.Muted);
    private readonly FlowLayoutPanel _params;
    private TriggerSetting _setting = new();

    public TriggerPanel(string title, string code)
    {
        BackColor = Theme.Panel;
        Padding = new Padding(16);
        Margin = new Padding(0);
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);

        _layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            BackColor = Theme.Panel,
            AutoScroll = true,
            Margin = new Padding(0),
        };
        _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(_layout);

        var header = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 12), BackColor = Theme.Panel };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(Theme.Label(title, Theme.Heading), 0, 0);
        var codeLabel = Theme.Label(code, Theme.Mono, Theme.Muted);
        codeLabel.Anchor = AnchorStyles.Right;
        header.Controls.Add(codeLabel, 1, 0);
        Add(header);

        var modes = new TableLayoutPanel { ColumnCount = 3, RowCount = 2, Dock = DockStyle.Fill, Height = 80, Margin = new Padding(0, 0, 0, 14), BackColor = Theme.Panel };
        for (var i = 0; i < 3; i++) modes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        modes.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        modes.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        var index = 0;
        foreach (var mode in Enum.GetValues<TriggerMode>())
        {
            var label = mode == TriggerMode.Vibration ? L.Mode(mode) + " β" : L.Mode(mode);
            var button = new FlatButton(label, ButtonKind.Toggle)
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(index % 3 == 0 ? 0 : 3, 2, index % 3 == 2 ? 0 : 3, 2),
                Padding = new Padding(4, 0, 4, 0),
                Radius = 7,
                AccessibleName = $"{title}: {L.Mode(mode)}",
            };
            var m = mode;
            button.Click += (_, _) => SetMode(m);
            _modeButtons[mode] = button;
            modes.Controls.Add(button, index % 3, index / 3);
            index++;
        }
        Add(modes);

        var zoneHost = new Panel { Height = 26, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 6), BackColor = Theme.Panel };
        zoneHost.Controls.Add(_zone);
        Add(zoneHost);

        var captionRow = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 0, 0, 14), BackColor = Theme.Panel };
        captionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        captionRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        captionRow.Controls.Add(_caption, 0, 0);
        captionRow.Controls.Add(Theme.Label(Strings.Trigger_Travel, Theme.Mono, Theme.Muted), 1, 0);
        Add(captionRow);

        _params = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            BackColor = Theme.Panel,
        };
        Add(_params);
        _layout.Resize += (_, _) => FitParams();
    }

    /// <summary>Raised on every user change (mode or parameter).</summary>
    public event Action? Changed;

    public TriggerSetting Setting
    {
        get => _setting.Clone();
        set
        {
            _setting = value.Clone();
            Rebuild();
        }
    }

    private void Add(Control c)
    {
        _layout.RowCount++;
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.Controls.Add(c, 0, _layout.RowCount - 1);
    }

    private void SetMode(TriggerMode mode)
    {
        if (_setting.Mode == mode) return;
        _setting = TriggerSetting.From(TriggerEffect.Default(mode));
        Rebuild();
        Changed?.Invoke();
    }

    private void Rebuild()
    {
        foreach (var (mode, button) in _modeButtons) button.Checked = mode == _setting.Mode;
        UpdateZone();

        _params.SuspendLayout();
        foreach (Control c in _params.Controls) c.Dispose();
        _params.Controls.Clear();
        var effect = _setting.ToEffect();
        var defs = TriggerModes.Params[_setting.Mode];
        for (var i = 0; i < defs.Length; i++)
        {
            var def = defs[i];
            var index = i;
            if (def.IsSwitch)
            {
                var check = Theme.Check(L.Param(_setting.Mode, def), effect.Values[i] == 1);
                check.ForeColor = Theme.TextSoft;
                check.Font = Theme.Small;
                check.CheckedChanged += (_, _) => SetValue(index, check.Checked ? 1 : 0);
                _params.Controls.Add(check);
            }
            else
            {
                var row = new ParamRow(L.Param(_setting.Mode, def), def.Min, def.Max, effect.Values[i]);
                row.ValueChanged += v => SetValue(index, v);
                _params.Controls.Add(row);
            }
        }
        if (defs.Length == 0)
            _params.Controls.Add(Note(Strings.Trigger_NoParams, Theme.Muted));
        if (_setting.Mode == TriggerMode.Vibration)
            _params.Controls.Add(Note(Strings.Trigger_VibrationExperimental, Theme.Warn));
        _params.ResumeLayout();
        FitParams();
    }

    private Label Note(string text, Color color)
    {
        var label = Theme.Label(text, Theme.Small, color);
        label.AutoSize = false;
        label.Height = 40;
        label.Margin = new Padding(0, 6, 0, 0);
        return label;
    }

    private void SetValue(int index, int value)
    {
        var values = _setting.ToEffect().Values;
        values[index] = value;
        _setting.Values = values;
        UpdateZone();
        Changed?.Invoke();
    }

    private void UpdateZone()
    {
        var effect = _setting.ToEffect();
        _zone.Effect = effect;
        _caption.Text = ZoneBar.Caption(effect);
    }

    private void FitParams()
    {
        var width = _layout.ClientSize.Width - (_layout.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0) - 2;
        foreach (Control c in _params.Controls)
            if (c is ParamRow || c is Label { AutoSize: false })
                c.Width = Math.Max(120, width);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Theme.Back(this));
        Theme.FillRounded(e.Graphics, Theme.Panel, Theme.Border, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 10);
    }

    /// <summary>Label and value on one line, slider below.</summary>
    private sealed class ParamRow : Panel
    {
        private readonly Label _value;

        public event Action<int>? ValueChanged;

        public ParamRow(string label, int min, int max, int value)
        {
            Height = 52;
            Margin = new Padding(0, 0, 0, 6);
            BackColor = Theme.Panel;
            var name = Theme.Label(label, Theme.Body);
            name.Location = new Point(0, 2);
            _value = Theme.Label(value.ToString(), Theme.Mono, Theme.AccentSoft);
            _value.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            var slider = new Slider(min, max, value)
            {
                Location = new Point(0, 26),
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                AccessibleName = label,
                AccessibleDescription = value.ToString(),
            };
            slider.ValueChanged += (_, _) =>
            {
                _value.Text = slider.Value.ToString();
                PlaceValue();
                ValueChanged?.Invoke(slider.Value);
            };
            Controls.Add(name);
            Controls.Add(_value);
            Controls.Add(slider);
            Resize += (_, _) =>
            {
                slider.Width = Width;
                PlaceValue();
            };
        }

        private void PlaceValue() => _value.Location = new Point(Width - _value.PreferredWidth, 3);
    }
}
