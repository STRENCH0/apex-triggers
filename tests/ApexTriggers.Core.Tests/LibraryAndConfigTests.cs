using System.Text.Json;
using ApexTriggers.Core.Config;
using ApexTriggers.Core.Games;
using ApexTriggers.Core.Protocol;
using ApexTriggers.Core.Steam;

namespace ApexTriggers.Core.Tests;

public class VdfTests
{
    [Fact]
    public void Parses_nested_keys_escapes_and_comments()
    {
        const string text = """
            "libraryfolders"
            {
                // a comment Steam never writes, but users do
                "0"
                {
                    "path"		"C:\\Program Files (x86)\\Steam"
                    "apps" { "570" "123" }
                }
                "1" { "path" "D:\\SteamLibrary" }
            }
            """;
        var root = Vdf.Parse(text).Child("libraryfolders")!;
        Assert.Equal(@"C:\Program Files (x86)\Steam", root.Child("0")!.Value("path"));
        Assert.Equal("123", root.Child("0")!.Child("apps")!.Value("570"));
        Assert.Equal(@"D:\SteamLibrary", root.Child("1")!.Value("path"));
    }

    [Fact]
    public void Keys_are_case_insensitive()
    {
        var state = Vdf.Parse("\"AppState\" { \"appid\" \"1145350\" \"name\" \"Hades II\" }").Child("appstate")!;
        Assert.Equal("1145350", state.Value("AppID"));
    }

    [Fact]
    public void Unterminated_block_is_an_error()
    {
        Assert.Throws<FormatException>(() => Vdf.Parse("\"AppState\" { \"appid\" \"1\""));
    }
}

public class GameMatchTests
{
    private static readonly GamePreset Steam = new() { Name = "Dead Space", SteamAppId = 1693980, InstallDir = @"C:\Steam\steamapps\common\Dead Space (2023)" };
    private static readonly GamePreset Manual = new() { Name = "Ryujinx", ExePath = @"D:\Emulators\Ryujinx\Ryujinx.exe" };

    [Theory]
    [InlineData(@"C:\Steam\steamapps\common\Dead Space (2023)\Dead Space.exe", true)]
    [InlineData(@"c:\steam\STEAMAPPS\common\dead space (2023)\bin\Launcher.exe", true)]
    [InlineData(@"C:\Steam\steamapps\common\Dead Space (2023) Demo\Dead Space.exe", false)]
    [InlineData(@"C:\Steam\steamapps\common\Other\Game.exe", false)]
    public void Steam_game_matches_any_exe_inside_its_folder(string exe, bool expected)
    {
        Assert.Equal(expected, GameMonitor.Matches(Steam, exe));
    }

    [Theory]
    [InlineData(@"D:\Emulators\Ryujinx\Ryujinx.exe", true)]
    [InlineData(@"d:\emulators\ryujinx\RYUJINX.EXE", true)]
    [InlineData(@"D:\Emulators\Ryujinx\Updater.exe", false)]
    public void Manual_game_matches_only_its_exe(string exe, bool expected)
    {
        Assert.Equal(expected, GameMonitor.Matches(Manual, exe));
    }
}

public class ConfigTests
{
    [Fact]
    public void Round_trips_through_json()
    {
        var config = new AppConfig();
        config.Settings.Language = "en";
        config.Games.Add(new GamePreset
        {
            Id = "steam:1458140",
            Name = "Pacific Drive",
            SteamAppId = 1458140,
            InstallDir = @"C:\Steam\steamapps\common\Pacific Drive",
            Left = TriggerSetting.From(new TriggerEffect(TriggerMode.Race, [20, 90])),
            Right = TriggerSetting.From(new TriggerEffect(TriggerMode.Sniper, [50, 60, 140, 1])),
        });
        var path = Path.Combine(Path.GetTempPath(), $"apex-triggers-test-{Guid.NewGuid():N}.json");
        try
        {
            ConfigStore.Save(config, path);
            var json = File.ReadAllText(path);
            Assert.Contains("\"mode\": \"race\"", json);
            var loaded = JsonSerializer.Deserialize<AppConfig>(json, ConfigStore.Json)!;
            var game = Assert.Single(loaded.Games);
            Assert.Equal(TriggerMode.Sniper, game.Right.Mode);
            Assert.Equal([50, 60, 140, 1], game.Right.Values);
            Assert.Equal("en", loaded.Settings.Language);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Out_of_range_values_from_a_hand_edited_file_are_clamped()
    {
        var setting = new TriggerSetting { Mode = TriggerMode.Race, Values = [999, -5] };
        Assert.Equal([192, 1], setting.ToEffect().Values);
    }

    [Fact]
    public void Missing_values_fall_back_to_defaults()
    {
        var effect = new TriggerSetting { Mode = TriggerMode.Lock }.ToEffect();
        Assert.Equal(TriggerModes.Params[TriggerMode.Lock].Single().Default, effect["start"]);
    }
}
