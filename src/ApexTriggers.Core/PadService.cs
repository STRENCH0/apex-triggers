using ApexTriggers.Core.Hid;
using ApexTriggers.Core.Protocol;

namespace ApexTriggers.Core;

public enum PadStatus
{
    NotConnected,
    /// <summary>Attached, but not handed to Steam (flag off, or firmware too old).</summary>
    Connected,
    HandedToSteam,
}

public sealed record PadState(PadStatus Status, PadInfo? Info = null, TransportState? Transport = null)
{
    public static PadState None { get; } = new(PadStatus.NotConnected);
}

/// <summary>
/// Owns the pad: notices it arriving and leaving, hands it to Steam once per connection, and sends
/// trigger effects. The HID handle is held only while talking to the pad — an open handle receives
/// the ~1000 Hz input stream, and reading that for nothing is the opposite of "CPU near 0%".
/// </summary>
public sealed class PadService : IDisposable
{
    private static readonly TimeSpan WatchInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan IdleClose = TimeSpan.FromSeconds(3);

    private readonly PacketLog _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private HidDeviceInfo? _device;
    private ApexPad? _pad;
    private DateTime _lastUse;
    private DateTime _lastRefresh;
    private int _silentRefreshes;
    private DateTime _retryAfter;
    private Task? _loop;

    public PadService(PacketLog log) => _log = log;

    public PadState State { get; private set; } = PadState.None;

    /// <summary>Hand the pad to Steam when it connects with the flag off.</summary>
    public bool AutoHandover { get; set; } = true;

    /// <summary>A running game wants plain XInput: the flag stays off, across reconnects too, until released.</summary>
    public bool HoldXInput { get; private set; }

    /// <summary>
    /// Another program (ApexSenseBridge) is driving the pad: no handle, no polling, no effects. Its own
    /// exchanges share the vendor interface, and our replies are broadcast to it.
    /// </summary>
    public bool Yielding { get; private set; }

    public event Action<PadState>? StateChanged;

    /// <summary>The pad (re)appeared and the handshake is done: send the current preset again.</summary>
    public event Action? Connected;

    public void Start() => _loop ??= Task.Run(LoopAsync);

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                await TickAsync();
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _log.Info("pad loop: " + e.Message);
                await DisconnectAsync();
            }
            try { await Task.Delay(WatchInterval, _stop.Token); } catch (OperationCanceledException) { }
        }
    }

    private async Task TickAsync()
    {
        if (Yielding)
        {
            await _gate.WaitAsync();
            try { CloseHandle(); } finally { _gate.Release(); }
            return;
        }

        if (_device is null)
        {
            if (DateTime.UtcNow < _retryAfter) return;
            var device = ApexPad.FindVendorInterfaces().FirstOrDefault();
            if (device is not null) await ConnectAsync(device);
            return;
        }

        // Gone from the bus: unplugged, dongle pulled, or asleep (a sleeping Apex 5 leaves the bus).
        if (!HidDeviceInfo.InterfacePaths().Contains(_device.Path, StringComparer.OrdinalIgnoreCase))
        {
            _log.Info("pad left the bus");
            await DisconnectAsync();
            return;
        }

        await _gate.WaitAsync();
        try
        {
            if (_pad is not null && DateTime.UtcNow - _lastUse > IdleClose) CloseHandle();
        }
        finally
        {
            _gate.Release();
        }

        if (DateTime.UtcNow - _lastRefresh > RefreshInterval) await RefreshAsync();
    }

    private async Task ConnectAsync(HidDeviceInfo device)
    {
        await _gate.WaitAsync();
        try
        {
            _device = device;
            var pad = Pad();
            var info = await pad.GetInfoAsync();
            if (info is null)
            {
                // Collection present but the pad does not answer (dongle without a pad, waking up).
                _device = null;
                CloseHandle();
                _retryAfter = DateTime.UtcNow.AddSeconds(5);
                return;
            }
            var transport = await pad.ReadTransportAsync();
            if (HoldXInput && transport is { ThirdParty: true })
            {
                _log.Info("handover flag on while a game wants XInput, disabling");
                await pad.SetThirdPartyControlAsync(false);
                transport = await DelayedReadAsync(pad) ?? transport;
            }
            else if (!HoldXInput && AutoHandover && transport is { ThirdParty: false } && info.SupportsHandover)
            {
                _log.Info("handover flag off, enabling");
                await pad.SetThirdPartyControlAsync(true);
                transport = await WaitForOwnerAsync(pad) ?? transport;
            }
            _lastRefresh = DateTime.UtcNow;
            _silentRefreshes = 0;
            SetState(Describe(info, transport));
            _log.Info($"connected: {info.ModelName}, {info.Connection}, fw {info.MainFirmware}, third_party={transport?.ThirdParty}, control_by='{transport?.ControlBy}'");
        }
        finally
        {
            _gate.Release();
        }
        Connected?.Invoke();
    }

    /// <summary>SDL claims the pad by itself once it sees the flag; give it a few seconds to show up as the owner.</summary>
    private static async Task<TransportState?> WaitForOwnerAsync(ApexPad pad)
    {
        TransportState? state = null;
        for (var i = 0; i < 6; i++)
        {
            await Task.Delay(500);
            state = await pad.ReadTransportAsync();
            if (state is { ThirdParty: true, ControlBy.Length: > 0 }) break;
        }
        return state;
    }

    private static async Task<TransportState?> DelayedReadAsync(ApexPad pad)
    {
        await Task.Delay(1000);
        return await pad.ReadTransportAsync();
    }

    private async Task RefreshAsync()
    {
        await _gate.WaitAsync();
        PadState? state = null;
        var lost = false;
        try
        {
            if (_device is null) return;
            _lastRefresh = DateTime.UtcNow;
            var pad = Pad();
            var info = await pad.GetInfoAsync();
            if (info is null)
            {
                // Still on the bus but silent: a pad that went to sleep behind a dongle that stayed.
                lost = ++_silentRefreshes >= 2;
                return;
            }
            _silentRefreshes = 0;
            var transport = await pad.ReadTransportAsync();
            if (HoldXInput && transport is { ThirdParty: true })
            {
                // The first try failed or something else turned the flag back on mid-game.
                _log.Info("handover flag on while a game wants XInput, disabling");
                await pad.SetThirdPartyControlAsync(false);
                transport = await DelayedReadAsync(pad) ?? transport;
            }
            state = Describe(info, transport);
        }
        finally
        {
            _gate.Release();
        }
        if (lost)
        {
            _log.Info("pad silent, treating as disconnected");
            await DisconnectAsync();
        }
        else if (state is not null) SetState(state);
    }

    private async Task DisconnectAsync()
    {
        await _gate.WaitAsync();
        try
        {
            CloseHandle();
            _device = null;
        }
        finally
        {
            _gate.Release();
        }
        SetState(PadState.None);
    }

    private static PadState Describe(PadInfo info, TransportState? transport) =>
        new(transport is { ThirdParty: true } ? PadStatus.HandedToSteam : PadStatus.Connected, info, transport);

    /// <summary>Put effects on both triggers. False when no pad is attached or it did not acknowledge.</summary>
    public async Task<bool> ApplyAsync(TriggerEffect left, TriggerEffect right, bool apply = true)
    {
        await _gate.WaitAsync();
        try
        {
            if (_device is null || Yielding) return false;
            var ok = await Pad().SetTriggersAsync(left, right, apply);
            _log.Info($"{(apply ? "apply" : "preview")} L={left} R={right} ack={ok}");
            return ok;
        }
        catch (IOException e)
        {
            _log.Info("apply failed: " + e.Message);
            CloseHandle();
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Command 17. Turning it off returns the pad to ordinary XInput mode.</summary>
    public async Task<bool> SetHandoverAsync(bool enabled)
    {
        PadState? state = null;
        await _gate.WaitAsync();
        try
        {
            if (_device is null) return false;
            var pad = Pad();
            var ok = await pad.SetThirdPartyControlAsync(enabled);
            var transport = enabled ? await WaitForOwnerAsync(pad) : await DelayedReadAsync(pad);
            if (State.Info is { } info) state = Describe(info, transport);
            return ok;
        }
        finally
        {
            _gate.Release();
            if (state is not null) SetState(state);
        }
    }

    /// <summary>
    /// Start or stop holding the pad in XInput mode for a game. Starting turns the flag off now; stopping turns
    /// it back on only if handover is enabled. Sent only when the flag differs, and also while yielding:
    /// a bridge that started before the flag went off is waiting for exactly this.
    /// </summary>
    public async Task SetHoldXInputAsync(bool hold)
    {
        if (HoldXInput == hold) return;
        HoldXInput = hold;
        _log.Info(hold ? "holding XInput mode for a game" : "XInput hold released");
        if (!hold && !AutoHandover) return;

        var enabled = !hold;
        PadState? state = null;
        await _gate.WaitAsync();
        try
        {
            if (_device is null) return; // the next connect applies the hold
            var pad = Pad();
            var transport = await pad.ReadTransportAsync();
            if (transport is null || transport.ThirdParty == enabled) return;
            if (enabled && State.Info is { SupportsHandover: false }) return;
            await pad.SetThirdPartyControlAsync(enabled);
            transport = (enabled ? await WaitForOwnerAsync(pad) : await DelayedReadAsync(pad)) ?? transport;
            if (State.Info is { } info) state = Describe(info, transport);
        }
        catch (IOException e)
        {
            _log.Info("handover change failed: " + e.Message);
            CloseHandle();
        }
        finally
        {
            _gate.Release();
            if (state is not null) SetState(state);
        }
    }

    /// <summary>Stand aside for another program driving the pad, or come back. Coming back changes nothing on the pad.</summary>
    public void SetYielding(bool yielding)
    {
        if (Yielding == yielding) return;
        Yielding = yielding;
        _log.Info(yielding ? "ApexSenseBridge session started, standing aside" : "ApexSenseBridge session ended");
        // Refresh soon after coming back: the bridge may have changed the flag or the pad may have slept.
        if (!yielding) _lastRefresh = DateTime.MinValue;
    }

    private ApexPad Pad()
    {
        _lastUse = DateTime.UtcNow;
        if (_pad is { IsBroken: true }) CloseHandle();
        return _pad ??= ApexPad.Open(_device!, _log);
    }

    private void CloseHandle()
    {
        _pad?.Dispose();
        _pad = null;
    }

    private void SetState(PadState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch (AggregateException) { }
        CloseHandle();
        _stop.Dispose();
    }
}
