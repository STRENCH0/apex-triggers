using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ApexTriggers.App;

/// <summary>The trigger glyph from the mockups, drawn at runtime; tray variants carry a status dot.</summary>
internal static class AppIcon
{
    public static readonly Icon Window = Make(null, 32);

    public static Icon Tray(Color? dot) => Make(dot, 32);

    private static Icon Make(Color? dot, int size)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRounded(g, Theme.Raised, null, new RectangleF(0, 0, size, size), size * 0.22f);
            var s = size / 16f;
            using var pen = new Pen(Theme.Accent, 1.9f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            // Mockup glyph: M4 2h5a3 3 0 0 1 3 3v9 · M4 2v5 · M8 8v6, nudged to sit centered.
            using var path = new GraphicsPath();
            path.AddLine(4 * s, 2.5f * s, 8.5f * s, 2.5f * s);
            path.AddArc(5.5f * s, 2.5f * s, 6 * s, 6 * s, 270, 90);
            path.AddLine(11.5f * s, 5.5f * s, 11.5f * s, 13.5f * s);
            g.DrawPath(pen, path);
            g.DrawLine(pen, 4 * s, 2.5f * s, 4 * s, 7 * s);
            g.DrawLine(pen, 7.8f * s, 8 * s, 7.8f * s, 13.5f * s);
            if (dot is { } c)
            {
                var d = size * 0.42f;
                using var ring = new SolidBrush(Theme.Bg);
                g.FillEllipse(ring, size - d - 0.5f, size - d - 0.5f, d + 0.5f, d + 0.5f);
                using var brush = new SolidBrush(c);
                g.FillEllipse(brush, size - d + 1.5f, size - d + 1.5f, d - 3.5f, d - 3.5f);
            }
        }
        var handle = bmp.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
}
