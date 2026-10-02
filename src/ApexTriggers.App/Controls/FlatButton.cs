using ApexTriggers.App.Resources;

namespace ApexTriggers.App.Controls;

internal enum ButtonKind
{
    /// <summary>Outlined, transparent.</summary>
    Secondary,
    /// <summary>Filled orange, dark text.</summary>
    Primary,
    /// <summary>Raised fill, no border — "Добавить" in the game list.</summary>
    Subtle,
    /// <summary>Mode picker: outlined, filled orange when <see cref="FlatButton.Checked"/>.</summary>
    Toggle,
    /// <summary>Orange outline, orange text — "Выбрать exe…".</summary>
    Accent,
    /// <summary>Segment of a segmented switch: no border, filled orange when checked.</summary>
    Segment,
    /// <summary>Warning outline — the Space Station banner.</summary>
    Warning,
}

/// <summary>
/// A button painted like the mockups. Not derived from Button: under the app's dark color mode the
/// stock button paints itself and ignores UserPaint, losing the rounded shape, kinds and glyphs.
/// Focus, Enter/Space and the accessible role are provided here instead.
/// </summary>
internal sealed class FlatButton : Control
{
    private bool _hover;
    private bool _checked;

    public FlatButton(string text, ButtonKind kind = ButtonKind.Secondary)
    {
        Text = text;
        Kind = kind;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.SupportsTransparentBackColor | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
        BackColor = Color.Transparent;
        Font = Theme.Body;
        Cursor = Cursors.Hand;
        Height = 38;
        Padding = new Padding(14, 0, 14, 0);
        Margin = new Padding(0);
        AutoSize = false;
    }

    public ButtonKind Kind { get; set; }

    /// <summary>Optional glyph drawn left of the text, in the text color.</summary>
    public Action<Graphics, RectangleF, Color>? Glyph { get; set; }

    public bool Checked
    {
        get => _checked;
        set
        {
            _checked = value;
            AccessibleDescription = value ? Strings.Common_Selected : null;
            Invalidate();
        }
    }

    public int Radius { get; set; } = 8;

    /// <summary>Width that fits the text, glyph and padding.</summary>
    public int PreferredWidth
    {
        get
        {
            var text = TextRenderer.MeasureText(Text, Font).Width;
            // A few pixels of slack: TextRenderer's measure and draw disagree slightly at
            // fractional DPI, and without it the text gets an ellipsis.
            return text + Padding.Horizontal + (Glyph is null ? 0 : 24) + 6;
        }
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        AccessibleName ??= Text;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && CanFocus) Focus();
        base.OnMouseDown(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Enter or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode is Keys.Space or Keys.Enter && Enabled)
        {
            OnClick(EventArgs.Empty);
            e.Handled = true;
        }
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Back(this));
        var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);

        var (fill, border, fore) = Kind switch
        {
            ButtonKind.Primary => (Theme.Accent, Theme.Accent, Theme.AccentInk),
            ButtonKind.Subtle => (Theme.Raised, (Color?)null, Theme.Text),
            ButtonKind.Toggle or ButtonKind.Segment when _checked => (Theme.Accent, Theme.Accent, Theme.AccentInk),
            ButtonKind.Segment => (Color.Transparent, (Color?)null, Theme.Text),
            ButtonKind.Accent => (Color.Transparent, Theme.Accent, Theme.AccentSoft),
            ButtonKind.Warning => (Color.Transparent, Theme.Blend(Theme.Warn, Theme.Bg, 0.5), Theme.WarnText),
            _ => (Color.Transparent, Theme.Border, Theme.Text),
        };
        if (_hover && Enabled && !(Kind == ButtonKind.Toggle && _checked))
            fill = Kind == ButtonKind.Primary ? Theme.AccentSoft : Theme.Raised;
        if (!Enabled)
        {
            fore = Theme.Blend(fore, Theme.Bg, 0.45);
            if (Kind == ButtonKind.Primary)
            {
                fill = Theme.Blend(Theme.Accent, Theme.Bg, 0.35);
                border = fill;
            }
        }

        Theme.FillRounded(g, fill == Color.Transparent ? Theme.Back(this) : fill, border, r, Radius);

        var content = new Rectangle(Padding.Left, 0, Width - Padding.Horizontal, Height);
        if (Glyph is not null)
        {
            var textWidth = TextRenderer.MeasureText(Text, Font).Width;
            var total = 16 + 8 + textWidth;
            var x = Math.Max(Padding.Left, (Width - total) / 2f);
            Glyph(g, new RectangleF(x, (Height - 16) / 2f, 16, 16), fore);
            content = new Rectangle((int)(x + 24), 0, textWidth + 2, Height);
        }
        TextRenderer.DrawText(g, Text, Font, content, fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        if (Focused && ShowFocusCues)
        {
            using var pen = new Pen(Theme.Accent, 2);
            using var path = Theme.Rounded(RectangleF.Inflate(r, -2, -2), Radius - 1);
            g.DrawPath(pen, path);
        }
    }
}
