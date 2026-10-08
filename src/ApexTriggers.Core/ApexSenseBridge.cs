using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ApexTriggers.Core;

/// <summary>
/// Detects an ApexSenseBridge session. While one runs the bridge drives the triggers from the game's own
/// DualSense effects and clears them at both ends, so anything we send would fight it.
/// </summary>
public static class ApexSenseBridge
{
    // The engine holds this mutex for the whole session and closes it only after its own cleanup (triggers
    // cleared, controller visibility and Apex profile restored), so its disappearance means "safe to send".
    // Only its existence is checked: acquiring it, even for a moment, makes an engine starting right then
    // refuse to run.
    private const string SessionMutex = @"Local\ApexSenseBridge.ActiveSession.Owner.v1";
    private const string EngineProcess = "ApexSenseBridge";

    public static bool SessionActive()
    {
        // The bridge's tray opens the mutex for a moment while probing; only a running engine means a session.
        if (!EngineRunning()) return false;
        var handle = OpenMutex(Synchronize, false, SessionMutex);
        if (handle != IntPtr.Zero)
        {
            CloseHandle(handle);
            return true;
        }
        // An engine running elevated can deny us the handle; the name still exists then.
        return Marshal.GetLastWin32Error() == ErrorAccessDenied;
    }

    private static bool EngineRunning()
    {
        var processes = Process.GetProcessesByName(EngineProcess);
        foreach (var process in processes) process.Dispose();
        return processes.Length > 0;
    }

    private const uint Synchronize = 0x00100000;
    private const int ErrorAccessDenied = 5;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenMutex(uint access, bool inherit, string name);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
