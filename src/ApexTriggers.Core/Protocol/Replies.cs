using System.Text;

namespace ApexTriggers.Core.Protocol;

/// <summary>
/// Decoders for vendor replies. Indices are raw, i.e. include the report-id byte 0x04 at [0];
/// Flydigi's SDK and SDL strip it, so their indices are one lower.
/// </summary>
public static class Replies
{
    /// <summary>Marker at [3] of the ~1000 Hz input stream that shares report id 0x04 with replies.</summary>
    public const byte InputStreamMarker = 0xEF;

    public static bool IsReply(byte[] data, byte command) =>
        data.Length >= 6 && data[0] == Packet.ReportIdIn && data[1] == Packet.Magic1 && data[2] == Packet.Magic2 && data[3] == command;

    public static bool IsInputStream(byte[] data) =>
        data.Length >= 4 && data[0] == Packet.ReportIdIn && data[1] == Packet.Magic1 && data[2] == Packet.Magic2 && data[3] == InputStreamMarker;

    /// <summary>
    /// Success byte of ACK-style replies (17, 81, 82): <c>04 5A A5 &lt;cmd&gt; 01 00 &lt;success&gt; &lt;echo…&gt;</c>,
    /// measured on an Apex 5. Command 18 has no success byte — it echoes the levels — so any reply counts.
    /// </summary>
    public static bool AckOk(byte[] data, byte command) =>
        IsReply(data, command) && (command == Packet.CmdGripRumble || data.Length > 6 && data[6] == 1);

    public static PadInfo? ParseInfo(byte[] data)
    {
        if (!IsReply(data, Packet.CmdGetInfo) || data.Length < 30) return null;
        var versions = new Dictionary<string, string?>();
        string[] names = ["main", "dongle", "switch", "trigger", "screen", "adc", "nearlink"];
        for (var i = 0; i < names.Length; i++)
            versions[names[i]] = Bcd(data[16 + 2 * i], data[17 + 2 * i]);
        var battery = data[12];
        return new PadInfo(
            DeviceType: data[6],
            ConnectionRaw: data[7],
            BatteryLevel: battery & 0x0F,
            Charging: battery >> 4 == 1,
            Charged: battery >> 4 == 2,
            Versions: versions,
            Raw: data);
    }

    public static TransportState? ParseTransport(byte[] data)
    {
        if (!IsReply(data, Packet.CmdReadTransport) || data.Length < 31) return null;
        var holder = Encoding.ASCII.GetString(data, 11, 20);
        var nul = holder.IndexOf('\0');
        return new TransportState(
            ControllerData: data[6] == 1,
            RawData: data[7] == 1,
            Keyboard: data[8] == 1,
            Mouse: data[9] == 1,
            ThirdParty: data[10] == 1,
            ControlBy: nul >= 0 ? holder[..nul] : holder,
            Raw: data);
    }

    /// <summary>Two BCD bytes, one version field per nibble: 0x70 0x45 is 7.0.4.5. All zero means absent.</summary>
    private static string? Bcd(byte hi, byte lo) =>
        hi == 0 && lo == 0 ? null : $"{hi >> 4}.{hi & 0xF}.{lo >> 4}.{lo & 0xF}";
}

public sealed record PadInfo(int DeviceType, int ConnectionRaw, int BatteryLevel, bool Charging, bool Charged,
    IReadOnlyDictionary<string, string?> Versions, byte[] Raw)
{
    /// <summary>Apex 5 variants: K5, K5Eva, K5Mm, K5Srs, K5GS, K5LZ.</summary>
    public static readonly IReadOnlyDictionary<int, string> Apex5Types = new Dictionary<int, string>
    {
        [128] = "Apex 5", [129] = "Apex 5 Eva", [133] = "Apex 5 Mm", [134] = "Apex 5 Srs", [135] = "Apex 5 GS", [136] = "Apex 5 LZ",
    };

    /// <summary>
    /// Steam's SDL driver refuses an Apex 5 below 0x7031 (7.0.3.1); Space Station offers the switch
    /// from 7.0.3.0. The handover is only useful when Steam accepts the pad, so SDL's bound wins.
    /// </summary>
    public static readonly Version MinHandoverFirmware = new(7, 0, 3, 1);

    public bool IsApex5 => Apex5Types.ContainsKey(DeviceType);

    /// <summary>The fallback is English because it goes to the log; the app localizes unknown types itself.</summary>
    public string ModelName => Apex5Types.TryGetValue(DeviceType, out var name) ? name : $"Unknown device {DeviceType}";

    public string? MainFirmware => Versions["main"];

    /// <summary>OpenFlydigi measured 1 = cable and 2 = dongle; SDL's "new architecture" branch uses 0/1.</summary>
    public string Connection => ConnectionRaw switch
    {
        1 => "cable",
        2 => "2.4G dongle",
        _ => $"unknown ({ConnectionRaw})",
    };

    public bool SupportsHandover =>
        MainFirmware is { } v && Version.TryParse(v, out var parsed) && parsed >= MinHandoverFirmware;
}

public sealed record TransportState(bool ControllerData, bool RawData, bool Keyboard, bool Mouse, bool ThirdParty,
    string ControlBy, byte[] Raw);
