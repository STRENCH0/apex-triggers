using System.Text.Json;
using System.Text.Json.Serialization;
using ApexTriggers.Core.Protocol;

namespace ApexTriggers.Core.Config;

/// <summary>One trigger in config.json: the mode by name and its values in <see cref="TriggerModes.Params"/> order.</summary>
public sealed class TriggerSetting
{
    public TriggerMode Mode { get; set; } = TriggerMode.Normal;
    public int[] Values { get; set; } = [];

    public TriggerEffect ToEffect()
    {
        var defs = TriggerModes.Params[Mode];
        var values = defs.Select((p, i) => i < Values.Length ? Math.Clamp(Values[i], p.Min, p.Max) : p.Default).ToArray();
        return new TriggerEffect(Mode, values);
    }

    public static TriggerSetting From(TriggerEffect effect) => new() { Mode = effect.Mode, Values = (int[])effect.Values.Clone() };

    public TriggerSetting Clone() => new() { Mode = Mode, Values = (int[])Values.Clone() };
}

public sealed class GamePreset
{
    /// <summary>"steam:&lt;appid&gt;" or "exe:&lt;path&gt;"; stable across renames.</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int? SteamAppId { get; set; }
    /// <summary>Steam games: the install folder; any exe inside it counts as the game.</summary>
    public string? InstallDir { get; set; }
    /// <summary>Games added by hand: the exact exe.</summary>
    public string? ExePath { get; set; }
    public TriggerSetting Left { get; set; } = new();
    public TriggerSetting Right { get; set; } = new();

    [JsonIgnore]
    public bool IsSteam => SteamAppId is not null;
}

public sealed class AppSettings
{
    /// <summary>"ru", "en", or null for the system language.</summary>
    public string? Language { get; set; }
    /// <summary>Hand the pad to Steam Input on connect. Off after "return to normal mode".</summary>
    public bool Handover { get; set; } = true;
    public bool AutoApply { get; set; } = true;
    public bool ReapplyOnReconnect { get; set; } = true;
    public int PollSeconds { get; set; } = 2;
    public bool MinimizeToTray { get; set; } = true;
    public bool NotifyOnPresetChange { get; set; } = true;
    public bool LivePreview { get; set; } = true;
}

public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public AppSettings Settings { get; set; } = new();
    public List<GamePreset> Games { get; set; } = [];
}

/// <summary>%APPDATA%\ApexTriggers\config.json — human-readable, portable by copying.</summary>
public static class ConfigStore
{
    public static readonly string Directory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ApexTriggers");

    public static string FilePath => Path.Combine(Directory, "config.json");

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Json) ?? new AppConfig();
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            // A broken file is kept aside rather than overwritten, so nothing the user wrote is lost.
            try { File.Copy(FilePath, FilePath + ".broken", overwrite: true); } catch (IOException) { }
        }
        return new AppConfig();
    }

    public static void Save(AppConfig config) => Save(config, FilePath);

    public static void Save(AppConfig config, string path)
    {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(config, Json));
        File.Move(temp, path, overwrite: true);
    }
}
