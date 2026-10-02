using System.Globalization;
using ApexTriggers.App.Resources;
using ApexTriggers.Core.Protocol;

namespace ApexTriggers.App;

/// <summary>
/// Language switch over <see cref="Strings"/> (Resources/Strings.resx is English, Strings.ru.resx Russian).
/// Lookups follow <see cref="Strings.Culture"/>, not the thread culture; a language switch rebuilds the windows.
/// </summary>
internal static class L
{
    public static bool Ru { get; private set; } = true;

    public static string Code => Ru ? "ru" : "en";

    /// <summary>"ru", "en", or null for the system language.</summary>
    public static void Set(string? language)
    {
        Ru = (language ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) == "ru";
        Strings.Culture = CultureInfo.GetCultureInfo(Code);
    }

    /// <summary>Fills a format string from <see cref="Strings"/> in the UI language.</summary>
    public static string F(string format, params object?[] args) => string.Format(Strings.Culture, format, args);

    /// <summary>
    /// Whether <paramref name="n"/> takes the singular resource form. Russian uses it for 1, 21, 31… but
    /// not 11; two forms are enough for the "in N Steam libraries" phrase, whose Russian noun has no third.
    /// </summary>
    public static bool One(int n) => Ru ? n % 10 == 1 && n % 100 != 11 : n == 1;

    public static string Mode(TriggerMode mode) => Get("Mode_" + mode) ?? mode.ToString();

    /// <summary>Labels differ per mode (Sniper's "start" is not Race's), so the key carries both.</summary>
    public static string Param(TriggerMode mode, TriggerParam p) => Get($"Param_{mode}_{p.Key}") ?? p.Key;

    public static string Summary(TriggerEffect left, TriggerEffect right) => $"LT {Mode(left.Mode)} · RT {Mode(right.Mode)}";

    private static string? Get(string key) => Strings.ResourceManager.GetString(key, Strings.Culture);
}
