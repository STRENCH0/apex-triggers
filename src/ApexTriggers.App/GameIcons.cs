using System.Drawing.Drawing2D;
using System.Drawing.Text;
using ApexTriggers.Core.Config;
using ApexTriggers.Core.Steam;

namespace ApexTriggers.App;

/// <summary>
/// Game icons, all local: Steam's library cache, else the exe's own icon via Windows, else a tile
/// with the game's initials.
/// </summary>
internal static class GameIcons
{
    private static readonly Dictionary<string, Image?> Cache = new();

    public static Image? Get(GamePreset game) => Get(game.Id, game.SteamAppId, game.ExePath);

    public static Image? Get(SteamGame game) => Get($"steam:{game.AppId}", game.AppId, null);

    private static Image? Get(string key, int? appId, string? exe)
    {
        if (Cache.TryGetValue(key, out var image)) return image;
        image = null;
        try
        {
            if (appId is { } id && SteamLibrary.CachedIconPath(id) is { } file)
            {
                using var stream = File.OpenRead(file);
                using var loaded = Image.FromStream(stream);
                image = new Bitmap(loaded);
            }
            else if (exe is not null && File.Exists(exe) && Icon.ExtractAssociatedIcon(exe) is { } icon)
            {
                using (icon) image = icon.ToBitmap();
            }
        }
        catch (Exception e) when (e is IOException or ArgumentException or OutOfMemoryException or UnauthorizedAccessException)
        {
            image = null;
        }
        Cache[key] = image;
        return image;
    }

    public static string Initials(string name)
    {
        var words = name.Split([' ', ':', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries);
        var letters = words.Where(w => char.IsLetterOrDigit(w[0])).Select(w => char.ToUpperInvariant(w[0])).Take(3).ToArray();
        if (letters.Length == 1 && words[0].Length > 1) return words[0][..2].ToUpperInvariant();
        return letters.Length == 0 ? "?" : new string(letters);
    }

    /// <summary>Muted tile color derived from the name, so a game keeps its color between runs.</summary>
    public static Color Tint(string name)
    {
        Color[] tints = [Theme.Hex("#1F5F8B"), Theme.Hex("#8A7A12"), Theme.Hex("#6B5B3A"), Theme.Hex("#8B2A1F"),
            Theme.Hex("#3F4A5C"), Theme.Hex("#2F4A3A"), Theme.Hex("#5B2A6B"), Theme.Hex("#7A3A1F"), Theme.Hex("#9A5A12")];
        var hash = 0;
        foreach (var c in name) hash = hash * 31 + c;
        return tints[Math.Abs(hash % tints.Length)];
    }

    /// <summary>Draw the icon (or the initials tile) into a rounded square.</summary>
    public static void Draw(Graphics g, Image? image, string name, RectangleF r, float radius, Font font)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        using var path = Theme.Rounded(r, radius);
        if (image is not null)
        {
            var state = g.Save();
            g.SetClip(path);
            g.DrawImage(image, r);
            g.Restore(state);
            return;
        }
        using (var brush = new SolidBrush(name == "—" ? Theme.Border : Tint(name))) g.FillPath(brush, path);
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var text = name == "—" ? "—" : Initials(name);
        TextRenderer.DrawText(g, text, font, Rectangle.Round(r), Theme.Hex("#F4F5F7"),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
    }
}
