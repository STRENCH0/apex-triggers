using System.Drawing.Drawing2D;

namespace ApexTriggers.App;

/// <summary>Stroke icons from the mockups, drawn into a 16×16 box in the given color.</summary>
internal static class Glyphs
{
    private static Pen Pen(Color color, RectangleF r, float width = 1.5f) =>
        new(color, width * r.Width / 16f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

    private static PointF P(RectangleF r, float x, float y) => new(r.X + x * r.Width / 16f, r.Y + y * r.Height / 16f);

    public static void Plus(Graphics g, RectangleF r, Color c)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = Pen(c, r, 1.6f);
        g.DrawLine(pen, P(r, 8, 3), P(r, 8, 13));
        g.DrawLine(pen, P(r, 3, 8), P(r, 13, 8));
    }

    public static void Gear(Graphics g, RectangleF r, Color c)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = Pen(c, r, 1.4f);
        var s = r.Width / 16f;
        g.DrawEllipse(pen, r.X + 5.8f * s, r.Y + 5.8f * s, 4.4f * s, 4.4f * s);
        (float, float, float, float)[] spokes =
            [(8, 1.5f, 8, 3.5f), (8, 12.5f, 8, 14.5f), (1.5f, 8, 3.5f, 8), (12.5f, 8, 14.5f, 8),
             (3.4f, 3.4f, 4.8f, 4.8f), (11.2f, 11.2f, 12.6f, 12.6f), (3.4f, 12.6f, 4.8f, 11.2f), (11.2f, 4.8f, 12.6f, 3.4f)];
        foreach (var (x1, y1, x2, y2) in spokes) g.DrawLine(pen, P(r, x1, y1), P(r, x2, y2));
    }

    public static void Folder(Graphics g, RectangleF r, Color c)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = Pen(c, r, 1.4f);
        g.DrawLines(pen, [P(r, 2, 4.5f), P(r, 2, 13), P(r, 14, 13), P(r, 14, 5), P(r, 8, 5), P(r, 6.5f, 3.5f), P(r, 2.5f, 3.5f), P(r, 2, 4.5f)]);
    }

    public static void Search(Graphics g, RectangleF r, Color c)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = Pen(c, r);
        var s = r.Width / 16f;
        g.DrawEllipse(pen, r.X + 2.5f * s, r.Y + 2.5f * s, 9 * s, 9 * s);
        g.DrawLine(pen, P(r, 10.5f, 10.5f), P(r, 14, 14));
    }

    public static void Warning(Graphics g, RectangleF r, Color c)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = Pen(c, r, 1.6f);
        g.DrawPolygon(pen, [P(r, 8, 2), P(r, 14.5f, 13.5f), P(r, 1.5f, 13.5f)]);
        g.DrawLine(pen, P(r, 8, 6.5f), P(r, 8, 9.5f));
        g.DrawLine(pen, P(r, 8, 11.6f), P(r, 8, 11.8f));
    }

    public static void Check(Graphics g, RectangleF r, Color c)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = Pen(c, r, 1.8f);
        g.DrawLines(pen, [P(r, 3, 8.5f), P(r, 6.5f, 12), P(r, 13, 4)]);
    }
}
