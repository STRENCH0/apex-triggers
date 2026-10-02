namespace ApexTriggers.Core.Protocol;

/// <summary>
/// An outgoing vendor packet: <c>03 5A A5 &lt;cmd&gt; &lt;len&gt; &lt;payload…&gt; [crc]</c>, 32 bytes.
///
/// The constructor is private and every factory below builds one fixed command, so a packet with a
/// command outside <see cref="AllowedCommands"/> cannot be created anywhere in the program. Flash
/// write (166), chip upgrade mode (31), factory reset (253) and the rest are not representable.
/// </summary>
public sealed class Packet
{
    public const int Length = 32;
    public const byte ReportIdOut = 0x03;
    public const byte ReportIdIn = 0x04;
    public const byte Magic1 = 0x5A;
    public const byte Magic2 = 0xA5;

    public const byte CmdGetInfo = 1;
    public const byte CmdReadTransport = 16;
    public const byte CmdWriteTransport = 17;
    public const byte CmdGripRumble = 18;
    public const byte CmdForceTrigger = 81;
    public const byte CmdTriggerGripBind = 82;

    /// <summary>
    /// The only commands this program may send. 18 (grip rumble) is the packet SDL itself sends for
    /// rumble; the tester uses it to check trigger vibration without a game.
    /// </summary>
    public static readonly IReadOnlySet<byte> AllowedCommands = new HashSet<byte>
    {
        CmdGetInfo, CmdReadTransport, CmdWriteTransport, CmdGripRumble, CmdForceTrigger, CmdTriggerGripBind,
    };

    private const byte Unchanged = 0xFF;

    private readonly byte[] _bytes;

    public byte Command => _bytes[3];
    public ReadOnlySpan<byte> Bytes => _bytes;
    public byte[] ToArray() => (byte[])_bytes.Clone();

    private Packet(byte command, byte length, ReadOnlySpan<byte> payload, bool checksum)
    {
        EnsureAllowed(command);
        _bytes = new byte[Length];
        _bytes[0] = ReportIdOut;
        _bytes[1] = Magic1;
        _bytes[2] = Magic2;
        _bytes[3] = command;
        _bytes[4] = length;
        payload.CopyTo(_bytes.AsSpan(5));
        // 8-bit sum over [3, 3 + len), placed right after that range. Flydigi's own builders for
        // 81, 82 and 18 write no checksum, and the pad accepts them that way.
        if (checksum) _bytes[3 + length] = Sum(_bytes, 3, 3 + length);
    }

    /// <summary>The gate every packet passes in its constructor.</summary>
    internal static void EnsureAllowed(byte command)
    {
        if (!AllowedCommands.Contains(command))
            throw new InvalidOperationException($"Command {command} is not allowed");
    }

    public static byte Sum(ReadOnlySpan<byte> data, int start, int end)
    {
        byte sum = 0;
        for (var i = start; i < end; i++) sum += data[i];
        return sum;
    }

    /// <summary>Command 1: device type, connection, battery, firmware versions.</summary>
    public static Packet GetInfo() => new(CmdGetInfo, 2, [], checksum: true);

    /// <summary>Command 16: transport flags, including "third-party apps may take over".</summary>
    public static Packet ReadTransport() => new(CmdReadTransport, 2, [], checksum: true);

    /// <summary>Command 17 touching only the third-party flag; the other four flags are sent as 0xFF, "leave alone".</summary>
    public static Packet SetThirdPartyControl(bool enabled) =>
        new(CmdWriteTransport, 7, [Unchanged, Unchanged, Unchanged, Unchanged, (byte)(enabled ? 1 : 0)], checksum: true);

    /// <summary>Command 18: grip motors, the same packet SDL sends for rumble. 0/0 stops.</summary>
    public static Packet GripRumble(byte left, byte right) => new(CmdGripRumble, 6, [left, right], checksum: false);

    /// <summary>Command 81. One trigger per packet: side 3 ("both") is ACKed and ignored by the pad.</summary>
    internal static Packet ForceTrigger(bool apply, TriggerSide side, byte wireMode, ReadOnlySpan<byte> parameters)
    {
        Span<byte> payload = stackalloc byte[8];
        payload[0] = (byte)(apply ? 1 : 0);
        payload[1] = (byte)side;
        payload[2] = wireMode;
        parameters[..Math.Min(parameters.Length, 5)].CopyTo(payload[3..]);
        return new Packet(CmdForceTrigger, 10, payload, checksum: false);
    }

    /// <summary>Command 82: route grip rumble into a trigger. No applyFlag, no mode byte.</summary>
    internal static Packet TriggerGripBind(TriggerSide side, byte bindType, byte filter, byte scale,
        byte stroke, byte pressure, byte strength, byte frequency) =>
        new(CmdTriggerGripBind, 11, [(byte)side, bindType, filter, scale, stroke, pressure, strength, frequency], checksum: false);

    public override string ToString() => Convert.ToHexString(_bytes, 0, 5 + Math.Max(0, _bytes[4] - 1)).ToLowerInvariant();
}

public enum TriggerSide : byte
{
    Left = 1,
    Right = 2,
}
