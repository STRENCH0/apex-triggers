using ApexTriggers.App.Resources;
using ApexTriggers.Core.Config;

namespace ApexTriggers.App.Controls;

internal sealed record GameRow(GamePreset? Game, string Name, string Summary, bool Running, Image? Icon)
{
    public override string ToString() => Name;
}

/// <summary>Owner-drawn game list: icon tile, name, trigger summary, "Running" badge.</summary>
internal sealed class GameList : ListBox
{
    public GameList()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = 58;
        BorderStyle = BorderStyle.None;
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        IntegralHeight = false;
        SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
    }

    public GameRow? SelectedRow => SelectedItem as GameRow;

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count) return;
        var row = (GameRow)Items[e.Index];
        var g = e.Graphics;
        using (var bg = new SolidBrush(BackColor)) g.FillRectangle(bg, e.Bounds);

        var selected = (e.State & DrawItemState.Selected) != 0;
        var card = new RectangleF(e.Bounds.X + 0.5f, e.Bounds.Y + 1.5f, e.Bounds.Width - 1, e.Bounds.Height - 3);
        if (selected) Theme.FillRounded(g, Theme.Raised, Theme.Accent, card, 8);

        var icon = new RectangleF(card.X + 10, card.Y + (card.Height - 36) / 2, 36, 36);
        GameIcons.Draw(g, row.Icon, row.Game is null ? "—" : row.Name, icon, 8, Theme.SmallBold);

        var badgeWidth = 0;
        if (row.Running)
        {
            var text = Strings.Common_Running;
            var size = TextRenderer.MeasureText(text, Theme.Small);
            badgeWidth = size.Width + 16;
            var badge = new RectangleF(card.Right - badgeWidth - 10, card.Y + (card.Height - 22) / 2, badgeWidth, 22);
            Theme.FillRounded(g, Theme.AccentWash, null, badge, 11);
            TextRenderer.DrawText(g, text, Theme.Small, Rectangle.Round(badge), Theme.AccentSoft,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            badgeWidth += 8;
        }

        var textLeft = (int)icon.Right + 12;
        var textWidth = (int)card.Right - textLeft - 10 - badgeWidth;
        var flags = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        TextRenderer.DrawText(g, row.Name, Theme.BodyBold, new Rectangle(textLeft, (int)card.Y + 9, textWidth, 20), Theme.Text, flags);
        TextRenderer.DrawText(g, row.Summary, Theme.Small, new Rectangle(textLeft, (int)card.Y + 30, textWidth, 18), Theme.Muted, flags);

        if ((e.State & DrawItemState.Focus) != 0 && Focused && ShowFocusCues && !selected)
            Theme.FillRounded(g, Color.Transparent, Theme.BorderStrong, card, 8);
    }
}
