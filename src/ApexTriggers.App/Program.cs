using ApexTriggers.Core;
using ApexTriggers.Core.Config;

namespace ApexTriggers.App;

internal static class Program
{
    private const string InstanceName = "ApexTriggers.SingleInstance";
    private const string ShowEventName = "ApexTriggers.ShowWindow";

    /// <summary>From Directory.Build.props, or from the git tag in release builds.</summary>
    public static string Version => Application.ProductVersion;

    public static readonly string LogDirectory = Path.Combine(ConfigStore.Directory, "logs");

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(true, InstanceName, out var first);
        if (!first)
        {
            // Already running: ask that instance to show its window.
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch (WaitHandleCannotBeOpenedException) { }
            return;
        }
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        ApplicationConfiguration.Initialize();
        Application.SetColorMode(SystemColorMode.Dark);
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());

        var firstRun = !File.Exists(ConfigStore.FilePath);
        var config = ConfigStore.Load();
        if (firstRun) ConfigStore.Save(config);
        L.Set(config.Settings.Language);

        var log = new PacketLog(Path.Combine(LogDirectory, "packets.log"));
        log.Info($"Apex Triggers {Version} started");
        var controller = new TriggerController(config, log);
        var minimized = args.Contains("--minimized") && !firstRun;
        var app = new TrayApp(controller, minimized);

        // Second launches signal this event; bring the window up on the UI thread.
        var ui = SynchronizationContext.Current!;
        ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) => ui.Post(_ => app.ShowMain(), null), null, Timeout.Infinite, executeOnlyOnce: false);

        Application.Run(app);
        log.Info("stopped");
    }
}
