using System.Diagnostics;

namespace ApexTriggers.Core;

/// <summary>Detects Flydigi Space Station, whose service can rewrite the handover flag and trigger effects.</summary>
public static class SpaceStation
{
    /// <summary>Names of running Space Station processes (UI and SpaceStationService), empty if none.</summary>
    public static List<string> RunningProcesses()
    {
        var found = new List<string>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                var name = process.ProcessName;
                if (name.Contains("SpaceStation", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Space Station", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Flydigi", StringComparison.OrdinalIgnoreCase))
                    found.Add(name);
            }
        }
        return found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
