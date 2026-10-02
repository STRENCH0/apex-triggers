using System.Reflection;
using ApexTriggers.Core;
using ApexTriggers.Core.Protocol;

namespace ApexTriggers.Core.Tests;

public class PacketTests
{
    /// <summary>32 bytes: the given prefix, then zeros.</summary>
    private static byte[] Bytes(params byte[] prefix)
    {
        var buffer = new byte[Packet.Length];
        prefix.CopyTo(buffer, 0);
        return buffer;
    }

    [Fact]
    public void Whitelist_is_exactly_the_harmless_commands()
    {
        Assert.Equal(new byte[] { 1, 16, 17, 18, 81, 82 }, Packet.AllowedCommands.Order().ToArray());
    }

    [Theory]
    [InlineData(31)]   // chip upgrade mode
    [InlineData(166)]  // save config to flash
    [InlineData(171)]  // save to a Switch-bank slot
    [InlineData(253)]  // factory reset
    public void Dangerous_commands_cannot_be_built(byte command)
    {
        Assert.Throws<InvalidOperationException>(() => Packet.EnsureAllowed(command));
    }

    [Fact]
    public void Every_public_factory_builds_an_allowed_command()
    {
        // A future factory that forgets the rule would still hit the constructor gate; this catches it in review.
        var factories = typeof(Packet).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == typeof(Packet));
        foreach (var factory in factories)
        {
            var args = factory.GetParameters().Select(p => p.ParameterType == typeof(bool) ? (object)true : (byte)0).ToArray();
            var packet = (Packet)factory.Invoke(null, args)!;
            Assert.Contains(packet.Command, Packet.AllowedCommands);
        }
    }

    [Fact]
    public void Info_request_carries_length_and_checksum()
    {
        Assert.Equal(Bytes(0x03, 0x5A, 0xA5, 0x01, 0x02, 0x03), Packet.GetInfo().ToArray());
    }

    [Fact]
    public void Transport_read_carries_length_and_checksum()
    {
        Assert.Equal(Bytes(0x03, 0x5A, 0xA5, 0x10, 0x02, 0x12), Packet.ReadTransport().ToArray());
    }

    [Fact]
    public void Handover_write_touches_only_the_third_party_flag()
    {
        // 0xFF = "leave alone" for controller data, raw data, keyboard and mouse; checksum over [3, 10).
        Assert.Equal(Bytes(0x03, 0x5A, 0xA5, 0x11, 0x07, 0xFF, 0xFF, 0xFF, 0xFF, 0x01, 0x15), Packet.SetThirdPartyControl(true).ToArray());
        Assert.Equal(Bytes(0x03, 0x5A, 0xA5, 0x11, 0x07, 0xFF, 0xFF, 0xFF, 0xFF, 0x00, 0x14), Packet.SetThirdPartyControl(false).ToArray());
    }

    [Fact]
    public void Racing_matches_the_packet_measured_on_hardware()
    {
        // Captured by the stage-0 tester on an Apex 5 and felt as resistance.
        var packets = new TriggerEffect(TriggerMode.Race, [10, 200]).ToPackets(TriggerSide.Left, clearVibration: false);
        Assert.Equal(Bytes(0x03, 0x5A, 0xA5, 0x51, 0x0A, 0x01, 0x01, 0x01, 0x0A, 0xC8, 0x00), Assert.Single(packets).ToArray());
    }

    [Fact]
    public void Preview_clears_the_apply_flag()
    {
        var packet = new TriggerEffect(TriggerMode.Race, [10, 200]).ToPackets(TriggerSide.Right, apply: false, clearVibration: false).Single();
        Assert.Equal(0, packet.Bytes[5]);
        Assert.Equal((byte)TriggerSide.Right, packet.Bytes[6]);
    }

    [Fact]
    public void Recoil_is_wire_mode_2_and_raises_zero_to_one()
    {
        var packet = new TriggerEffect(TriggerMode.Recoil, [30, 0, 0, 0, 1]).ToPackets(TriggerSide.Right, clearVibration: false).Single();
        Assert.Equal(Bytes(0x03, 0x5A, 0xA5, 0x51, 0x0A, 0x01, 0x02, 0x02, 30, 1, 1, 1, 1), packet.ToArray());
    }

    [Fact]
    public void Sniper_is_wire_mode_3_with_an_empty_fourth_slot()
    {
        var packet = new TriggerEffect(TriggerMode.Sniper, [50, 60, 140, 1]).ToPackets(TriggerSide.Left, clearVibration: false).Single();
        Assert.Equal(Bytes(0x03, 0x5A, 0xA5, 0x51, 0x0A, 0x01, 0x01, 0x03, 50, 60, 140, 0, 1), packet.ToArray());
    }

    [Fact]
    public void Lock_always_sends_full_strength_and_input_matching()
    {
        var packet = new TriggerEffect(TriggerMode.Lock, [80]).ToPackets(TriggerSide.Left, clearVibration: false).Single();
        Assert.Equal(Bytes(0x03, 0x5A, 0xA5, 0x51, 0x0A, 0x01, 0x01, 0x04, 80, 255, 1), packet.ToArray());
    }

    [Fact]
    public void Lock_position_is_clamped_to_the_slider_range()
    {
        var packet = new TriggerEffect(TriggerMode.Lock, [5]).ToPackets(TriggerSide.Left, clearVibration: false).Single();
        Assert.Equal(20, packet.Bytes[8]);
    }

    [Fact]
    public void Vibration_is_a_grip_bind_and_never_mode_5()
    {
        var packets = TriggerEffect.Default(TriggerMode.Vibration).ToPackets(TriggerSide.Right);
        var packet = Assert.Single(packets);
        // side, bindType 2, filter = threshold 10, scale = intensity 50, stroke 1, 1, 1, frequency 128.
        Assert.Equal(Bytes(0x03, 0x5A, 0xA5, 0x52, 0x0B, 0x02, 0x02, 10, 50, 1, 1, 1, 128), packet.ToArray());
    }

    [Fact]
    public void Other_modes_switch_the_vibration_route_off_first()
    {
        var packets = TriggerEffect.Normal.ToPackets(TriggerSide.Right);
        Assert.Equal([Packet.CmdTriggerGripBind, Packet.CmdForceTrigger], packets.Select(p => p.Command));
        Assert.Equal(Bytes(0x03, 0x5A, 0xA5, 0x52, 0x0B, 0x02, 0x02, 0xFF), packets[0].ToArray());
    }

    [Fact]
    public void Both_triggers_send_every_82_before_any_81()
    {
        // Measured: an 82 sent after the other trigger's 81 reset that trigger's effect.
        var packets = ApexPad.BothTriggerPackets(new TriggerEffect(TriggerMode.Race, [10, 200]), new TriggerEffect(TriggerMode.Race, [10, 200]));
        Assert.Equal([82, 82, 81, 81], packets.Select(p => (int)p.Command));
        Assert.Equal([1, 2, 1, 2], packets.Select(p => (int)p.Bytes[p.Command == 82 ? 5 : 6]));
    }

    [Fact]
    public void Both_triggers_never_use_side_3()
    {
        var packets = ApexPad.BothTriggerPackets(TriggerEffect.Default(TriggerMode.Vibration), TriggerEffect.Normal);
        Assert.DoesNotContain(packets, p => p.Bytes[p.Command == 82 ? 5 : 6] == 3);
    }
}
