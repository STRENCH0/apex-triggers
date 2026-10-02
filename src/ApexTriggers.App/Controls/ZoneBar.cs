using System.Drawing.Drawing2D;
using ApexTriggers.App.Resources;
using ApexTriggers.Core.Protocol;

namespace ApexTriggers.App.Controls;

/// <summary>The trigger's travel (0–255) with the effect's zone drawn on it, as in the mockup.</summary>
internal sealed class ZoneBar : Control
{
    private TriggerEffect _effect = TriggerEffect.Normal;

    public ZoneBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);
        Height = 26;
    }

    public TriggerEffect Effect
    {
        get => _effect;
        set
        {
            _effect = value;
            Invalidate();
        }
    }

    /// <summary>One line describing the zone, shown under the bar.</summary>
    public static string Caption(TriggerEffect e) => e.Mode switch
    {
        TriggerMode.Race => L.F(Strings.Zone_Race, e["start"]),
        TriggerMode.Recoil => L.F(Strings.Zone_Recoil, e["start"]),
        TriggerMode.Sniper => L.F(Strings.Zone_Sniper, e["start"], e["start"] + e["travel"]),
        TriggerMode.Lock => L.F(Strings.Zone_Lock, e["start"]),
        TriggerMode.Vibration => Strings.Zone_Vibration,
        _ => Strings.Zone_Normal,
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Back(this));
        var outer = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        Theme.FillRounded(g, Theme.Bg, Theme.Border, outer, 6);

        var (from, to, strength, hatched) = Zone(_effect);
        if (to <= from) return;
        var inner = RectangleF.Inflate(outer, -1, -1);
        var zone = new RectangleF(inner.X + inner.Width * from, inner.Y, inner.Width * (to - from), inner.Height);
        var color = Color.FromArgb((int)(255 * (0.25 + 0.75 * strength)), Theme.Accent);

        using var clip = Theme.Rounded(inner, 5);
        g.SetClip(clip);
        if (hatched)
        {
            using var brush = new HatchBrush(HatchStyle.WideUpwardDiagonal, color, Color.Transparent);
            g.FillRectangle(brush, zone);
        }
        else
        {
            using var brush = new SolidBrush(color);
            g.FillRectangle(brush, zone);
        }
        g.ResetClip();
    }

    /// <summary>Zone as fractions of travel, strength 0–1, and whether it vibrates (hatched) rather than resists.</summary>
    private static (float From, float To, float Strength, bool Hatched) Zone(TriggerEffect e)
    {
        static float P(int v) => Math.Clamp(v / 255f, 0, 1);
        return e.Mode switch
        {
            TriggerMode.Race => (P(e["start"]), 1, e["strength"] / 255f, false),
            TriggerMode.Recoil => (P(e["start"]), 1, e["strength"] / 255f, true),
            TriggerMode.Sniper => (P(e["start"]), P(e["start"] + e["travel"]), e["resistance"] / 255f, false),
            TriggerMode.Lock => (P(e["start"]), 1, 1, false),
            // What "travel range" does on the pad is unknown and the rumble is felt along the whole pull,
            // so the whole bar is drawn, brightness from the intensity coefficient.
            TriggerMode.Vibration => (0, 1, e["scale"] / 200f, true),
            _ => (0, 0, 0, false),
        };
    }
}
