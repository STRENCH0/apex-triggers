using ApexTriggers.App.Resources;

namespace ApexTriggers.App.Controls;

/// <summary>
/// Checkbox painted like the mockups. The stock CheckBox under the app's dark color mode draws a
/// solid white box whether checked or not, so the state is unreadable.
/// </summary>
internal sealed class DarkCheck : Control
{
    private bool _checked;
    private bool _hover;

    public DarkCheck(string text, bool value)
    {
        Text = text;
        _checked = value;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.Selectable | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        ForeColor = Theme.Text;
        BackColor = Color.Transparent;
        Font = Theme.Body;
        Margin = new Padding(0, 5, 0, 5);
        AccessibleRole = AccessibleRole.CheckButton;
        AutoSize = true;
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value) return;
            _checked = value;
            AccessibleDescription = value ? Strings.Common_Checked : Strings.Common_Unchecked;
            Invalidate();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? CheckedChanged;

    /// <summary>Wrap the text at this width; 0 keeps it on one line.</summary>
    public int WrapWidth
    {
        get => _wrapWidth;
        set
        {
            if (_wrapWidth == value) return;
            _wrapWidth = value;
            if (AutoSize) Size = GetPreferredSize(Size.Empty);
            Invalidate();
        }
    }

    private int _wrapWidth;

    private const int Box = 16;
    private const int Gap = 10;

    public override Size GetPreferredSize(Size proposedSize)
    {
        var flags = TextFormatFlags.NoPrefix | (WrapWidth > 0 ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine);
        var text = TextRenderer.MeasureText(Text, Font, new Size(WrapWidth > 0 ? WrapWidth - Box - Gap : int.MaxValue, int.MaxValue), flags);
        return new Size(Box + Gap + text.Width + 4, Math.Max(26, text.Height + 6));
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        if (AutoSize) Size = GetPreferredSize(Size.Empty);
        AccessibleName = Text;
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        if (AutoSize) Size = GetPreferredSize(Size.Empty);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Back(this));
        var box = new RectangleF(0.5f, (Height - Box) / 2f, Box, Box);
        if (_checked)
        {
            Theme.FillRounded(g, Theme.Accent, null, box, 4);
            Glyphs.Check(g, RectangleF.Inflate(box, -1.5f, -1.5f), Theme.AccentInk);
        }
        else
        {
            Theme.FillRounded(g, _hover ? Theme.Raised : Theme.Bg, Theme.BorderStrong, box, 4);
        }
        if (Focused && ShowFocusCues)
            Theme.FillRounded(g, Color.Transparent, Theme.AccentSoft, RectangleF.Inflate(box, 2, 2), 5, 1.5f);

        var flags = TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | (WrapWidth > 0 ? TextFormatFlags.WordBreak : TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(Box + Gap, 0, Width - Box - Gap, Height), Enabled ? ForeColor : Theme.Muted, flags);
    }

    protected override void OnClick(EventArgs e)
    {
        Focus();
        Checked = !Checked;
        base.OnClick(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode == Keys.Space) OnClick(EventArgs.Empty);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
}
