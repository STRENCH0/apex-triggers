using ApexTriggers.Core.Config;
using ApexTriggers.Core.Games;
using ApexTriggers.Core.Protocol;

namespace ApexTriggers.Core;

/// <summary>
/// Decides which effects belong on the triggers right now and keeps the pad there:
/// live preview from the editor, else the latest-started game's preset, else the default (Normal).
/// Sends are latest-wins: dragging a slider queues one send, not one per pixel.
/// </summary>
public sealed class TriggerController : IDisposable
{
    private readonly GameMonitor _monitor = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly object _sync = new();
    private (TriggerEffect L, TriggerEffect R)? _preview;
    private string? _lastSent;
    private bool _forceSend;
    private Task? _sender;
    private Task? _poller;

    public TriggerController(AppConfig config, PacketLog log)
    {
        Config = config;
        Log = log;
        Pad = new PadService(log) { AutoHandover = config.Settings.Handover };
        Pad.Connected += () =>
        {
            if (Config.Settings.ReapplyOnReconnect) RequestSend(force: true);
        };
        Pad.StateChanged += _ => Changed?.Invoke();
    }

    public AppConfig Config { get; }
    public PacketLog Log { get; }
    public PadService Pad { get; }

    /// <summary>Running games that have a preset, latest-started first.</summary>
    public IReadOnlyList<GamePreset> RunningGames { get; private set; } = [];

    /// <summary>The game whose preset is in force; null outside games or while auto-apply is off/paused.</summary>
    public GamePreset? ActiveGame { get; private set; }

    public bool Paused { get; private set; }

    /// <summary>Any state the UI shows changed. Raised on a background thread.</summary>
    public event Action? Changed;

    /// <summary>The game in force changed (null = default preset). Raised on a background thread.</summary>
    public event Action<GamePreset?>? ActiveGameChanged;

    public static readonly TriggerEffect DefaultEffect = TriggerEffect.Normal;

    public void Start()
    {
        Pad.Start();
        _sender ??= Task.Run(SendLoopAsync);
        _poller ??= Task.Run(PollLoopAsync);
    }

    public (TriggerEffect L, TriggerEffect R) EffectsFor(GamePreset? game) =>
        game is null ? (DefaultEffect, DefaultEffect) : (game.Left.ToEffect(), game.Right.ToEffect());

    public void SetPaused(bool paused)
    {
        Paused = paused;
        UpdateActiveGame();
        RequestSend();
    }

    /// <summary>Editor preview: sent with applyFlag 0 until <see cref="EndPreview"/>.</summary>
    public void SetPreview(TriggerEffect left, TriggerEffect right)
    {
        lock (_sync) _preview = (left, right);
        RequestSend();
    }

    /// <summary>Leave preview and put back whatever should be in force now.</summary>
    public void EndPreview()
    {
        lock (_sync)
        {
            if (_preview is null) return;
            _preview = null;
        }
        RequestSend(force: true);
    }

    /// <summary>Config changed (preset saved, game added or removed): re-evaluate and resend if needed.</summary>
    public void ConfigChanged()
    {
        ConfigStore.Save(Config);
        UpdateActiveGame();
        RequestSend();
    }

    /// <summary>Put Normal on both triggers now; the next game start applies its preset as usual.</summary>
    public async Task ResetTriggersAsync()
    {
        if (await Pad.ApplyAsync(DefaultEffect, DefaultEffect))
            _lastSent = $"True|{DefaultEffect}|{DefaultEffect}";
    }

    /// <summary>
    /// The "return the pad to normal mode" button: flag off, and it stays off across reconnects
    /// until the user turns it back on.
    /// </summary>
    public async Task<bool> SetHandoverAsync(bool enabled)
    {
        Config.Settings.Handover = enabled;
        Pad.AutoHandover = enabled;
        ConfigStore.Save(Config);
        return await Pad.SetHandoverAsync(enabled);
    }

    public void RequestSend(bool force = false)
    {
        lock (_sync) _forceSend |= force;
        try { _wake.Release(); } catch (SemaphoreFullException) { }
    }

    private async Task SendLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try { await _wake.WaitAsync(_stop.Token); } catch (OperationCanceledException) { return; }

            (TriggerEffect L, TriggerEffect R) effects;
            bool apply, force;
            lock (_sync)
            {
                apply = _preview is null;
                effects = _preview ?? EffectsFor(ActiveGame);
                force = _forceSend;
                _forceSend = false;
            }
            var key = $"{apply}|{effects.L}|{effects.R}";
            if (!force && key == _lastSent) continue;
            if (await Pad.ApplyAsync(effects.L, effects.R, apply)) _lastSent = key;
            else _lastSent = null;
        }
    }

    private async Task PollLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                RunningGames = _monitor.Scan(Config.Games.ToList());
                if (UpdateActiveGame()) RequestSend();
            }
            catch (Exception e)
            {
                Log.Info("game monitor: " + e.Message);
            }
            var seconds = Math.Clamp(Config.Settings.PollSeconds, 1, 10);
            try { await Task.Delay(TimeSpan.FromSeconds(seconds), _stop.Token); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Returns true when the game in force changed.</summary>
    private bool UpdateActiveGame()
    {
        var next = Config.Settings.AutoApply && !Paused ? RunningGames.FirstOrDefault() : null;
        if (next is not null && !Config.Games.Contains(next)) next = null;
        var changed = !ReferenceEquals(next, ActiveGame);
        ActiveGame = next;
        if (changed)
        {
            Log.Info($"active game: {next?.Name ?? "(none)"}");
            ActiveGameChanged?.Invoke(next);
        }
        Changed?.Invoke();
        return changed;
    }

    public void Dispose()
    {
        _stop.Cancel();
        Pad.Dispose();
        _stop.Dispose();
    }
}
