using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace ApexTriggers.App;

/// <summary>Palette and type from the UI mockups (dark, orange accent).</summary>
internal static class Theme
{
    public static readonly Color Bg = Hex("#0F1114");
    public static readonly Color Panel = Hex("#171A1F");
    public static readonly Color Raised = Hex("#1E2228");
    public static readonly Color Border = Hex("#2B3038");
    public static readonly Color BorderStrong = Hex("#3A404A");
    public static readonly Color Divider = Hex("#22262D");
    public static readonly Color Text = Hex("#E9EBEE");
    public static readonly Color TextSoft = Hex("#C9CDD3");
    public static readonly Color Muted = Hex("#A3A9B2");
    public static readonly Color Accent = Hex("#FF8A3D");
    public static readonly Color AccentSoft = Hex("#FFB07A");
    public static readonly Color AccentInk = Hex("#1A0E05");
    public static readonly Color AccentWash = Blend(Accent, Bg, 0.14);
    public static readonly Color Green = Hex("#4ADE80");
    public static readonly Color GreenText = Hex("#86EFAC");
    public static readonly Color GreenWash = Blend(Green, Bg, 0.10);
    public static readonly Color Warn = Hex("#F5B83D");
    public static readonly Color WarnText = Hex("#FBD38D");
    public static readonly Color WarnWash = Blend(Warn, Bg, 0.10);
    public static readonly Color Red = Hex("#F87171");

    public static readonly Font Body = new("Segoe UI", 10f);
    public static readonly Font BodyBold = new("Segoe UI Semibold", 10f);
    public static readonly Font Small = new("Segoe UI", 8.75f);
    public static readonly Font SmallBold = new("Segoe UI Semibold", 8.75f);
    public static readonly Font Caps = new("Segoe UI Semibold", 8.25f);
    public static readonly Font Title = new("Segoe UI Semibold", 15f);
    public static readonly Font Heading = new("Segoe UI Semibold", 11f);
    public static readonly Font Mono = new("Consolas", 9f);

    public static Color Hex(string hex) => ColorTranslator.FromHtml(hex);

    /// <summary>
    /// The first opaque background behind a control. Clearing with a transparent parent's BackColor
    /// paints black — the dark rim around chips inside transparent stacks.
    /// </summary>
    public static Color Back(Control control)
    {
        for (var c = control.Parent; c is not null; c = c.Parent)
            if (c.BackColor.A == 255) return c.BackColor;
        return Bg;
    }

    public static Color Blend(Color top, Color bottom, double alpha) => Color.FromArgb(
        (int)(top.R * alpha + bottom.R * (1 - alpha)),
        (int)(top.G * alpha + bottom.G * (1 - alpha)),
        (int)(top.B * alpha + bottom.B * (1 - alpha)));

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void FillRounded(Graphics g, Color fill, Color? border, RectangleF r, float radius, float borderWidth = 1)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var inset = border is null ? r : RectangleF.Inflate(r, -borderWidth / 2, -borderWidth / 2);
        using var path = Rounded(inset, radius);
        using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
        if (border is { } c)
        {
            using var pen = new Pen(c, borderWidth);
            g.DrawPath(pen, path);
        }
    }

    /// <summary>Dark native title bar (Windows 10 2004+ / 11).</summary>
    public static void DarkTitleBar(Form form)
    {
        var on = 1;
        DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int));
        var caption = ColorTranslator.ToWin32(Hex("#0B0D10"));
        DwmSetWindowAttribute(form.Handle, 35, ref caption, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Style(Form form)
    {
        form.BackColor = Bg;
        form.ForeColor = Text;
        form.Font = Body;
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.AutoScaleDimensions = new SizeF(96f, 96f);
        form.HandleCreated += (_, _) => DarkTitleBar(form);
        form.Icon = AppIcon.Window;
    }

    public static Label Label(string text, Font? font = null, Color? color = null) => new()
    {
        Text = text,
        AutoSize = true,
        Font = font ?? Body,
        ForeColor = color ?? Text,
        BackColor = Color.Transparent,
        Margin = new Padding(0),
    };

    public static Controls.DarkCheck Check(string text, bool value) => new(text, value);

    /// <summary>Menu renderer matching the panel colors, for the tray and context menus.</summary>
    public sealed class MenuRenderer() : ToolStripProfessionalRenderer(new MenuColors())
    {
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Text : Muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Muted;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = new Rectangle(e.ImageRectangle.X, e.ImageRectangle.Y, e.ImageRectangle.Width, e.ImageRectangle.Height);
            FillRounded(e.Graphics, Accent, null, r, 3);
            using var pen = new Pen(AccentInk, 1.8f);
            e.Graphics.DrawLines(pen, [
                new PointF(r.X + r.Width * 0.22f, r.Y + r.Height * 0.52f),
                new PointF(r.X + r.Width * 0.42f, r.Y + r.Height * 0.72f),
                new PointF(r.X + r.Width * 0.78f, r.Y + r.Height * 0.30f)]);
        }
    }

    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Panel;
        public override Color ImageMarginGradientBegin => Panel;
        public override Color ImageMarginGradientMiddle => Panel;
        public override Color ImageMarginGradientEnd => Panel;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Raised;
        public override Color MenuItemSelected => Raised;
        public override Color MenuItemSelectedGradientBegin => Raised;
        public override Color MenuItemSelectedGradientEnd => Raised;
        public override Color MenuItemPressedGradientBegin => Raised;
        public override Color MenuItemPressedGradientEnd => Raised;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Panel;
        public override Color CheckBackground => Panel;
        public override Color CheckSelectedBackground => Raised;
        public override Color CheckPressedBackground => Raised;
    }
}
