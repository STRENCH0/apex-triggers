using System.Drawing.Drawing2D;

namespace ApexTriggers.App.Controls;

/// <summary>Horizontal slider in the mockup's style. Arrow keys step by 1, PageUp/PageDown by 10.</summary>
internal sealed class Slider : Control
{
    private int _value;
    private bool _dragging;

    public Slider(int min, int max, int value)
    {
        Minimum = min;
        Maximum = max;
        _value = Math.Clamp(value, min, max);
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
        TabStop = true;
        Height = 24;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.Slider;
    }

    public int Minimum { get; }
    public int Maximum { get; }

    public int Value
    {
        get => _value;
        set
        {
            var v = Math.Clamp(value, Minimum, Maximum);
            if (v == _value) return;
            _value = v;
            AccessibleDescription = v.ToString();
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? ValueChanged;

    private const int ThumbRadius = 8;

    private float TrackLeft => ThumbRadius + 1;
    private float TrackWidth => Math.Max(1, Width - 2 * (ThumbRadius + 1));

    private float ThumbX => TrackLeft + TrackWidth * (Maximum == Minimum ? 0 : (float)(_value - Minimum) / (Maximum - Minimum));

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Back(this));
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var cy = Height / 2f;
        var track = new RectangleF(TrackLeft, cy - 2, TrackWidth, 4);
        Theme.FillRounded(g, Theme.Border, null, track, 2);
        var fill = new RectangleF(TrackLeft, cy - 2, ThumbX - TrackLeft, 4);
        if (fill.Width > 0) Theme.FillRounded(g, Enabled ? Theme.Accent : Theme.Muted, null, fill, 2);

        var thumb = new RectangleF(ThumbX - ThumbRadius, cy - ThumbRadius, ThumbRadius * 2, ThumbRadius * 2);
        using (var brush = new SolidBrush(Enabled ? Theme.Accent : Theme.Muted)) g.FillEllipse(brush, thumb);
        using (var pen = new Pen(Theme.Bg, 2)) g.DrawEllipse(pen, RectangleF.Inflate(thumb, -1, -1));
        if (Focused && ShowFocusCues)
        {
            using var ring = new Pen(Theme.AccentSoft, 2);
            g.DrawEllipse(ring, RectangleF.Inflate(thumb, 2.5f, 2.5f));
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        _dragging = true;
        Capture = true;
        SetFromX(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) SetFromX(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _dragging = false;
        Capture = false;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Focused) Value += Math.Sign(e.Delta);
    }

    private void SetFromX(int x)
    {
        var t = Math.Clamp((x - TrackLeft) / TrackWidth, 0, 1);
        Value = Minimum + (int)Math.Round(t * (Maximum - Minimum));
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Left or Keys.Down: Value -= 1; break;
            case Keys.Right or Keys.Up: Value += 1; break;
            case Keys.PageDown: Value -= 10; break;
            case Keys.PageUp: Value += 10; break;
            case Keys.Home: Value = Minimum; break;
            case Keys.End: Value = Maximum; break;
            default: return;
        }
        e.Handled = true;
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
}
