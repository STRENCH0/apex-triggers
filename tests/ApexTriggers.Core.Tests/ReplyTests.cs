using System.Text;
using ApexTriggers.Core.Protocol;

namespace ApexTriggers.Core.Tests;

public class ReplyTests
{
    private static byte[] Report(params byte[] prefix)
    {
        var buffer = new byte[32];
        prefix.CopyTo(buffer, 0);
        return buffer;
    }

    // Command-1 reply from an Apex 5 on the 2.4G dongle, main firmware 7.0.4.5, battery full on the charger.
    private static readonly byte[] InfoFromPad = Report(
        0x04, 0x5A, 0xA5, 0x01, 0x01, 0x00, 0x80, 0x02, 0x00, 0x00, 0x00, 0x00, 0x25, 0x45, 0x01, 0x00,
        0x70, 0x45, 0x21, 0x31, 0x45, 0x25, 0x00, 0x00, 0x01, 0x28, 0x00, 0x00, 0x11, 0x31, 0x1F);

    [Fact]
    public void Info_decodes_type_connection_and_battery()
    {
        var info = Replies.ParseInfo(InfoFromPad)!;
        Assert.Equal(128, info.DeviceType);
        Assert.True(info.IsApex5);
        Assert.Equal(2, info.ConnectionRaw);
        Assert.True(info.Charged);
        Assert.False(info.Charging);
        Assert.Equal(5, info.BatteryLevel);
    }

    [Fact]
    public void Info_decodes_bcd_firmware_versions_and_nulls_absent_chips()
    {
        var info = Replies.ParseInfo(InfoFromPad)!;
        Assert.Equal("7.0.4.5", info.Versions["main"]);
        Assert.Equal("2.1.3.1", info.Versions["dongle"]);
        Assert.Equal("0.1.2.8", info.Versions["screen"]);
        Assert.Null(info.Versions["trigger"]);
        Assert.Null(info.Versions["adc"]);
    }

    [Theory]
    [InlineData(0x70, 0x31, true)]   // 7.0.3.1 — SDL's minimum
    [InlineData(0x70, 0x45, true)]
    [InlineData(0x70, 0x30, false)]  // 7.0.3.0 — Space Station offers the switch, Steam refuses the pad
    [InlineData(0x61, 0x99, false)]
    public void Handover_needs_firmware_sdl_accepts(byte hi, byte lo, bool supported)
    {
        var report = (byte[])InfoFromPad.Clone();
        report[16] = hi;
        report[17] = lo;
        Assert.Equal(supported, Replies.ParseInfo(report)!.SupportsHandover);
    }

    [Fact]
    public void Transport_decodes_flags_and_owner()
    {
        var report = Report(0x04, 0x5A, 0xA5, 0x10, 0x01, 0x00, 0x00, 0x01, 0x00, 0x00, 0x01);
        Encoding.ASCII.GetBytes("SDL").CopyTo(report, 11);
        var state = Replies.ParseTransport(report)!;
        Assert.False(state.ControllerData);
        Assert.True(state.RawData);
        Assert.True(state.ThirdParty);
        Assert.Equal("SDL", state.ControlBy);
    }

    [Fact]
    public void Ack_success_byte_is_raw_index_6()
    {
        // 04 5A A5 <cmd> 01 00 <success> <echo…> — measured; [5] is always 0.
        Assert.True(Replies.AckOk(Report(0x04, 0x5A, 0xA5, 0x51, 0x01, 0x00, 0x01, 0x01, 0x0A, 0xC8), Packet.CmdForceTrigger));
        Assert.False(Replies.AckOk(Report(0x04, 0x5A, 0xA5, 0x51, 0x01, 0x00, 0x00), Packet.CmdForceTrigger));
    }

    [Fact]
    public void Ack_must_echo_the_same_command()
    {
        // Steam's own heartbeat replies arrive on the same handle.
        Assert.False(Replies.AckOk(Report(0x04, 0x5A, 0xA5, 0x01, 0x01, 0x00, 0x01), Packet.CmdForceTrigger));
    }

    [Fact]
    public void Rumble_reply_has_no_success_byte()
    {
        Assert.True(Replies.AckOk(Report(0x04, 0x5A, 0xA5, 0x12, 0x01, 0x00, 0xA0), Packet.CmdGripRumble));
    }

    [Fact]
    public void Input_stream_reports_are_not_replies()
    {
        var stream = Report(0x04, 0x5A, 0xA5, 0xEF, 0x00, 0x01);
        Assert.True(Replies.IsInputStream(stream));
        Assert.Null(Replies.ParseInfo(stream));
    }
}
