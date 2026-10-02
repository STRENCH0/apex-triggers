using ApexTriggers.Core.Hid;
using ApexTriggers.Core.Protocol;

namespace ApexTriggers.Core;

/// <summary>
/// The Apex 5 vendor interface: VID 0x37D7, a controller-family PID (top nibble 2), usage page 0xFFA0.
/// Steam opens the same collection; both sides see every reply, so a reply is matched by command id
/// and anything queued before a request is discarded.
/// </summary>
public sealed class ApexPad : IDisposable
{
    public const ushort VendorId = 0x37D7;
    public const ushort VendorUsagePage = 0xFFA0;

    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromMilliseconds(500);
    private const int Attempts = 3;

    private readonly HidConnection _hid;
    private readonly PacketLog? _log;
    private readonly SemaphoreSlim _exchange = new(1, 1);

    public HidDeviceInfo Device => _hid.Info;
    public bool IsBroken => _hid.IsBroken;

    private ApexPad(HidConnection hid, PacketLog? log)
    {
        _hid = hid;
        _log = log;
    }

    /// <summary>Vendor collections of every attached Flydigi controller. The CD2 dock (PID 0x6xxx) is excluded.</summary>
    public static List<HidDeviceInfo> FindVendorInterfaces() =>
        HidDeviceInfo.Enumerate(VendorId)
            .Where(d => d.ProductId >> 12 == 2 && d.UsagePage == VendorUsagePage)
            .ToList();

    public static ApexPad? OpenFirst(PacketLog? log = null)
    {
        var device = FindVendorInterfaces().FirstOrDefault();
        return device is null ? null : Open(device, log);
    }

    public static ApexPad Open(HidDeviceInfo device, PacketLog? log = null)
    {
        log?.Info($"open {device}");
        return new ApexPad(HidConnection.Open(device), log);
    }

    /// <summary>
    /// Send a packet and wait for the reply carrying the same command id. Returns null when the pad
    /// stays silent through every attempt (asleep, unplugged, or a command it ignores).
    /// </summary>
    public async Task<byte[]?> ExchangeAsync(Packet packet, CancellationToken ct = default)
    {
        await _exchange.WaitAsync(ct);
        try
        {
            for (var attempt = 1; attempt <= Attempts; attempt++)
            {
                _hid.Drain();
                var bytes = packet.ToArray();
                _log?.Tx(bytes);
                await _hid.WriteAsync(bytes, ct);

                var deadline = DateTime.UtcNow + ReplyTimeout;
                while (DateTime.UtcNow < deadline)
                {
                    var report = await _hid.ReadAsync(deadline - DateTime.UtcNow, ct);
                    if (report is null) break;
                    if (Replies.IsInputStream(report)) continue;
                    _log?.Rx(report);
                    if (Replies.IsReply(report, packet.Command)) return report;
                }
                if (_hid.IsBroken) break;
                _log?.Info($"no reply to {packet.Command}, attempt {attempt}/{Attempts}");
            }
            return null;
        }
        finally
        {
            _exchange.Release();
        }
    }

    public async Task<PadInfo?> GetInfoAsync(CancellationToken ct = default) =>
        await ExchangeAsync(Packet.GetInfo(), ct) is { } reply ? Replies.ParseInfo(reply) : null;

    public async Task<TransportState?> ReadTransportAsync(CancellationToken ct = default) =>
        await ExchangeAsync(Packet.ReadTransport(), ct) is { } reply ? Replies.ParseTransport(reply) : null;

    /// <summary>Command 17. True when the pad acknowledged it.</summary>
    public async Task<bool> SetThirdPartyControlAsync(bool enabled, CancellationToken ct = default) =>
        await ExchangeAsync(Packet.SetThirdPartyControl(enabled), ct) is { } reply && Replies.AckOk(reply, Packet.CmdWriteTransport);

    public async Task<bool> RumbleAsync(byte left, byte right, CancellationToken ct = default) =>
        await ExchangeAsync(Packet.GripRumble(left, right), ct) is { } reply && Replies.AckOk(reply, Packet.CmdGripRumble);

    /// <summary>
    /// Put an effect on one trigger. An ACK means the pad parsed the packet, nothing more: whether the
    /// effect is actually felt is only known by pulling the trigger.
    /// </summary>
    public async Task<bool> SetTriggerAsync(TriggerSide side, TriggerEffect effect, bool apply = true,
        bool clearVibration = true, CancellationToken ct = default)
    {
        var ok = true;
        foreach (var packet in effect.ToPackets(side, apply, clearVibration))
            ok &= await SendAsync(packet, ct);
        return ok;
    }

    /// <summary>
    /// Both triggers, one packet per side: side 3 ("both") is silently ignored by the pad.
    ///
    /// All 82s go before any 81. Measured on the tester: left 82+81 followed by right 82+81 left the
    /// left trigger loose, the reverse order worked — an 82 apparently resets the other trigger's
    /// 81 effect. Tester menu "x" checks this directly.
    /// </summary>
    public async Task<bool> SetTriggersAsync(TriggerEffect left, TriggerEffect right, bool apply = true,
        bool clearVibration = true, CancellationToken ct = default)
    {
        var ok = true;
        foreach (var packet in BothTriggerPackets(left, right, apply, clearVibration))
            ok &= await SendAsync(packet, ct);
        return ok;
    }

    /// <summary>The packets <see cref="SetTriggersAsync"/> sends, in send order: every 82, then every 81.</summary>
    public static IReadOnlyList<Packet> BothTriggerPackets(TriggerEffect left, TriggerEffect right, bool apply = true,
        bool clearVibration = true) =>
        left.ToPackets(TriggerSide.Left, apply, clearVibration)
            .Concat(right.ToPackets(TriggerSide.Right, apply, clearVibration))
            .OrderBy(p => p.Command == Packet.CmdTriggerGripBind ? 0 : 1)
            .ToList();

    /// <summary>Send one packet and report whether the pad acknowledged it.</summary>
    public async Task<bool> SendAsync(Packet packet, CancellationToken ct = default) =>
        await ExchangeAsync(packet, ct) is { } reply && Replies.AckOk(reply, packet.Command);

    /// <summary>
    /// Passively read for <paramref name="duration"/>: every non-stream report (including replies to
    /// Steam's own commands) goes to <paramref name="onReply"/> and the log. Returns how many input
    /// stream reports arrived, which shows whether the pad is in raw-report mode.
    /// </summary>
    public async Task<int> ListenAsync(TimeSpan duration, Action<byte[]>? onReply = null, CancellationToken ct = default)
    {
        await _exchange.WaitAsync(ct);
        try
        {
            _hid.Drain();
            var streamReports = 0;
            var deadline = DateTime.UtcNow + duration;
            while (DateTime.UtcNow < deadline && !_hid.IsBroken)
            {
                var report = await _hid.ReadAsync(deadline - DateTime.UtcNow, ct);
                if (report is null) continue;
                if (Replies.IsInputStream(report))
                {
                    streamReports++;
                    continue;
                }
                _log?.Rx(report);
                onReply?.Invoke(report);
            }
            return streamReports;
        }
        finally
        {
            _exchange.Release();
        }
    }

    public void Dispose()
    {
        _log?.Info("close");
        _hid.Dispose();
        _exchange.Dispose();
    }
}
