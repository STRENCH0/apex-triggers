using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ApexTriggers.Core.Config;

namespace ApexTriggers.Core.Games;

/// <summary>
/// Finds running games by polling the process list. Each process is opened once with
/// PROCESS_QUERY_LIMITED_INFORMATION — the right Task Manager uses — to read its image path and start
/// time; nothing reads game memory or injects anything, which keeps anti-cheat out of the picture.
/// </summary>
public sealed class GameMonitor
{
    private readonly Dictionary<int, (string? Path, long Created)> _cache = new();

    /// <summary>Running preset games, latest-started first.</summary>
    public List<GamePreset> Scan(IReadOnlyList<GamePreset> games)
    {
        var alive = new HashSet<int>();
        var started = new Dictionary<GamePreset, long>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var pid = process.Id;
                if (pid <= 4) continue;
                alive.Add(pid);
                if (!_cache.TryGetValue(pid, out var info))
                {
                    info = Query(pid);
                    _cache[pid] = info;
                }
                if (info.Path is null) continue;
                foreach (var game in games)
                {
                    if (!Matches(game, info.Path)) continue;
                    if (!started.TryGetValue(game, out var t) || info.Created > t) started[game] = info.Created;
                }
            }
        }
        foreach (var pid in _cache.Keys.Where(p => !alive.Contains(p)).ToList()) _cache.Remove(pid);
        return started.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
    }

    public static bool Matches(GamePreset game, string exePath)
    {
        if (game.ExePath is { Length: > 0 } exe)
            return string.Equals(Path.GetFullPath(exe), exePath, StringComparison.OrdinalIgnoreCase);
        if (game.InstallDir is { Length: > 0 } dir)
        {
            var root = Path.GetFullPath(dir).TrimEnd('\\') + "\\";
            return exePath.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    private static (string? Path, long Created) Query(int pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero) return (null, 0);
        try
        {
            var buffer = new StringBuilder(1024);
            var size = buffer.Capacity;
            var path = QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
            var created = GetProcessTimes(handle, out var c, out _, out _, out _) ? c : 0;
            return (path, created);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private const uint ProcessQueryLimitedInformation = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
