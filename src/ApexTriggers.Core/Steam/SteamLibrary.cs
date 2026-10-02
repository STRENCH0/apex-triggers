using Microsoft.Win32;

namespace ApexTriggers.Core.Steam;

public sealed record SteamGame(int AppId, string Name, string InstallDir, string LibraryPath);

/// <summary>Installed Steam games from libraryfolders.vdf and appmanifest_*.acf. Read-only, no network.</summary>
public static class SteamLibrary
{
    // Steam's own tools and runtimes show up as "apps" too; they are never games to add.
    private static readonly HashSet<int> NotGames = [228980, 1070560, 1391110, 1493710, 1628350, 1826330, 2180100, 1161040];

    public static string? SteamPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var path = key?.GetValue("SteamPath") as string;
        return path is null ? null : Path.GetFullPath(path);
    }

    public static List<SteamGame> InstalledGames()
    {
        var steam = SteamPath();
        if (steam is null) return [];
        var games = new List<SteamGame>();
        foreach (var library in LibraryFolders(steam))
        {
            var apps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var manifest in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                try
                {
                    var state = Vdf.Parse(File.ReadAllText(manifest)).Child("AppState");
                    if (state is null || !int.TryParse(state.Value("appid"), out var appId) || NotGames.Contains(appId)) continue;
                    var name = state.Value("name");
                    var dir = state.Value("installdir");
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(dir)) continue;
                    if (name.StartsWith("Steamworks", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("Proton", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("Steam Linux Runtime", StringComparison.OrdinalIgnoreCase))
                        continue;
                    games.Add(new SteamGame(appId, name, Path.Combine(apps, "common", dir), library));
                }
                catch (Exception e) when (e is IOException or FormatException or UnauthorizedAccessException)
                {
                    // A manifest Steam is rewriting right now; it will be there next time.
                }
            }
        }
        return games.DistinctBy(g => g.AppId).OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static IEnumerable<string> LibraryFolders(string steam)
    {
        // libraryfolders.vdf first: it keeps the path's real casing, while the registry's SteamPath is
        // lower-cased ("c:/program files (x86)/steam") and would win the case-insensitive Distinct below.
        var folders = new List<string>();
        var file = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        try
        {
            var root = Vdf.Parse(File.ReadAllText(file)).Child("libraryfolders");
            if (root is not null)
                foreach (var entry in root.Children.Values)
                    if (entry.Value("path") is { Length: > 0 } path)
                        folders.Add(Path.GetFullPath(path));
        }
        catch (Exception e) when (e is IOException or FormatException or UnauthorizedAccessException) { }
        folders.Add(steam);
        return folders.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The game's small icon from Steam's local cache: <c>appcache/librarycache/&lt;appid&gt;/&lt;sha1&gt;.jpg</c>
    /// in current Steam, <c>&lt;appid&gt;_icon.jpg</c> in older layouts. Null when not cached.
    /// </summary>
    public static string? CachedIconPath(int appId)
    {
        var steam = SteamPath();
        if (steam is null) return null;
        var cache = Path.Combine(steam, "appcache", "librarycache");
        var legacy = Path.Combine(cache, $"{appId}_icon.jpg");
        if (File.Exists(legacy)) return legacy;
        var dir = Path.Combine(cache, appId.ToString());
        if (!Directory.Exists(dir)) return null;
        return Directory.EnumerateFiles(dir, "*.jpg")
            .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f) is { Length: 40 } name && name.All(Uri.IsHexDigit));
    }
}

/// <summary>Minimal reader for Valve's text KeyValues format (VDF/ACF): quoted keys, quoted values, nested braces.</summary>
public sealed class Vdf
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Vdf> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? Value(string key) => Values.GetValueOrDefault(key);
    public Vdf? Child(string key) => Children.GetValueOrDefault(key);

    public static Vdf Parse(string text)
    {
        var pos = 0;
        var root = new Vdf();
        ParseBody(text, ref pos, root, topLevel: true);
        return root;
    }

    private static void ParseBody(string text, ref int pos, Vdf node, bool topLevel)
    {
        while (true)
        {
            var key = NextToken(text, ref pos);
            if (key is null)
            {
                if (topLevel) return;
                throw new FormatException("Unexpected end of VDF");
            }
            if (key == "}") return;
            var value = NextToken(text, ref pos) ?? throw new FormatException("Key without value in VDF");
            if (value == "{")
            {
                var child = new Vdf();
                ParseBody(text, ref pos, child, topLevel: false);
                node.Children[key] = child;
            }
            else
            {
                node.Values[key] = value;
            }
        }
    }

    private static string? NextToken(string text, ref int pos)
    {
        while (pos < text.Length)
        {
            var c = text[pos];
            if (char.IsWhiteSpace(c)) { pos++; continue; }
            if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '/')
            {
                while (pos < text.Length && text[pos] != '\n') pos++;
                continue;
            }
            if (c is '{' or '}') { pos++; return c.ToString(); }
            if (c == '"')
            {
                pos++;
                var sb = new System.Text.StringBuilder();
                while (pos < text.Length && text[pos] != '"')
                {
                    if (text[pos] == '\\' && pos + 1 < text.Length)
                    {
                        pos++;
                        sb.Append(text[pos] switch { 'n' => '\n', 't' => '\t', _ => text[pos] });
                    }
                    else sb.Append(text[pos]);
                    pos++;
                }
                pos++;
                return sb.ToString();
            }
            var start = pos;
            while (pos < text.Length && !char.IsWhiteSpace(text[pos]) && text[pos] is not '{' and not '}' and not '"') pos++;
            return text[start..pos];
        }
        return null;
    }
}
