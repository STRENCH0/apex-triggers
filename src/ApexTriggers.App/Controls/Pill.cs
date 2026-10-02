namespace ApexTriggers.App.Controls;

/// <summary>Rounded status chip: optional dot or check, bold text, muted text.</summary>
internal sealed class Pill : Control
{
    public Pill()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);
        Height = 36;
        Margin = new Padding(0, 0, 10, 0);
    }

    public Color Fill { get; set; } = Theme.Panel;
    public Color? Outline { get; set; } = Theme.Border;
    public Color? Dot { get; set; }
    public bool CheckMark { get; set; }
    public Color TextColor { get; set; } = Theme.Text;
    public string Primary { get; set; } = "";
    public Font PrimaryFont { get; set; } = Theme.BodyBold;
    public string Secondary { get; set; } = "";

    public void Update(Action<Pill> change)
    {
        change(this);
        AccessibleName = $"{Primary} {Secondary}".Trim();
        Width = Measure();
        Invalidate();
    }

    private int Measure()
    {
        var w = 14 + 14;
        if (Dot is not null || CheckMark) w += 18;
        if (Primary.Length > 0) w += TextRenderer.MeasureText(Primary, PrimaryFont).Width;
        if (Secondary.Length > 0) w += 6 + TextRenderer.MeasureText(Secondary, Theme.Body).Width;
        return w;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Back(this));
        Theme.FillRounded(g, Fill, Outline, new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Height / 2f);
        var x = 14f;
        var cy = Height / 2f;
        if (CheckMark)
        {
            using var pen = new Pen(TextColor, 1.8f);
            g.DrawLines(pen, [new PointF(x + 1, cy), new PointF(x + 4.5f, cy + 3.5f), new PointF(x + 11, cy - 4)]);
            x += 18;
        }
        else if (Dot is { } dot)
        {
            using var brush = new SolidBrush(dot);
            g.FillEllipse(brush, x, cy - 4, 8, 8);
            x += 18;
        }
        if (Primary.Length > 0)
        {
            var size = TextRenderer.MeasureText(Primary, PrimaryFont);
            TextRenderer.DrawText(g, Primary, PrimaryFont, new Point((int)x, (int)(cy - size.Height / 2f)), TextColor, TextFormatFlags.NoPrefix);
            x += size.Width + 6;
        }
        if (Secondary.Length > 0)
        {
            var size = TextRenderer.MeasureText(Secondary, Theme.Body);
            TextRenderer.DrawText(g, Secondary, Theme.Body, new Point((int)x, (int)(cy - size.Height / 2f)), Theme.Muted, TextFormatFlags.NoPrefix);
        }
    }
}
