namespace ApexTriggers.Core.Protocol;

/// <summary>
/// Trigger modes as Space Station names them. Wire numbers live only in <see cref="TriggerEffect"/>:
/// the SDK names modes 2 and 3 the other way round (Sniper = 2, Recoil = 3), and the pad's behaviour
/// follows Space Station's labels, so the UI names are the ones used here.
/// </summary>
public enum TriggerMode
{
    Normal,
    Race,
    Recoil,
    Sniper,
    Lock,
    Vibration,
}

/// <summary>One slider or switch of a mode, as Space Station shows it. Display labels belong to the UI, keyed by mode and <see cref="Key"/>.</summary>
public sealed record TriggerParam(string Key, int Min, int Max, int Default, bool IsSwitch = false);

public static class TriggerModes
{
    private static TriggerParam Switch(string key) => new(key, 0, 1, 1, IsSwitch: true);

    /// <summary>Parameters per mode, in Space Station's on-screen order.</summary>
    public static readonly IReadOnlyDictionary<TriggerMode, TriggerParam[]> Params = new Dictionary<TriggerMode, TriggerParam[]>
    {
        [TriggerMode.Normal] = [],
        [TriggerMode.Race] =
        [
            new("start", 0, 192, 10),
            new("strength", 1, 255, 30),
        ],
        [TriggerMode.Recoil] =
        [
            new("start", 0, 192, 30),
            new("press", 1, 255, 50),
            new("strength", 1, 255, 50),
            new("frequency", 1, 255, 17),
            Switch("match"),
        ],
        [TriggerMode.Sniper] =
        [
            new("start", 0, 192, 50),
            new("travel", 1, 255, 60),
            new("resistance", 1, 255, 60),
            Switch("match"),
        ],
        [TriggerMode.Lock] =
        [
            new("start", 20, 200, 80),
        ],
        [TriggerMode.Vibration] =
        [
            new("scale", 0, 200, 50),
            new("threshold", 1, 255, 10),
            new("stroke", 1, 200, 1),
            new("frequency", 1, 255, 128),
        ],
    };
}

/// <summary>A mode plus its parameter values, in <see cref="TriggerModes.Params"/> order.</summary>
public sealed record TriggerEffect(TriggerMode Mode, int[] Values)
{
    public static TriggerEffect Normal { get; } = new(TriggerMode.Normal, []);

    public static TriggerEffect Default(TriggerMode mode) =>
        new(mode, TriggerModes.Params[mode].Select(p => p.Default).ToArray());

    public int this[string key]
    {
        get
        {
            var defs = TriggerModes.Params[Mode];
            var index = Array.FindIndex(defs, p => p.Key == key);
            if (index < 0) throw new KeyNotFoundException($"{Mode} has no parameter '{key}'");
            return Math.Clamp(index < Values.Length ? Values[index] : defs[index].Default, defs[index].Min, defs[index].Max);
        }
    }

    /// <summary>
    /// Packets that put this effect on one trigger. Vibration is command 82 and nothing else; every
    /// other mode is command 81, preceded (when <paramref name="clearVibration"/>) by an 82 that
    /// switches the vibration route off so a previous Vibration preset does not linger — the bind
    /// survives an 81.
    /// </summary>
    public IReadOnlyList<Packet> ToPackets(TriggerSide side, bool apply = true, bool clearVibration = true)
    {
        if (Mode == TriggerMode.Vibration)
        {
            // Space Station's stored Vibration block: filter = threshold, scale = intensity,
            // bind params = stroke, 1, 1, frequency. bindType is always 2 on the wire.
            return [Packet.TriggerGripBind(side, 2, (byte)this["threshold"], (byte)this["scale"],
                (byte)this["stroke"], 1, 1, (byte)this["frequency"])];
        }
        return clearVibration ? [VibrationOff(side), ForceTrigger(side, apply)] : [ForceTrigger(side, apply)];
    }

    /// <summary>
    /// Command 82 with the route suppressed: filter 255, scale 0, zero params — the setting OpenFlydigi
    /// measured as "no rumble reaches the trigger". Unconfirmed as the pad's idle state; tester test 6.
    /// </summary>
    public static Packet VibrationOff(TriggerSide side) => Packet.TriggerGripBind(side, 2, 255, 0, 0, 0, 0, 0);

    private Packet ForceTrigger(TriggerSide side, bool apply)
    {
        // The builders clamp "min 1" parameters up rather than refusing them; the indexer does the
        // same through each parameter's Min.
        byte b(string key) => (byte)this[key];
        switch (Mode)
        {
            case TriggerMode.Normal:
                return Packet.ForceTrigger(apply, side, 0, []);
            case TriggerMode.Race:
            {
                // Space Station shows no input-matching switch for Racing; its stored block leaves the
                // slot at 0, so the live command carries 0. (Its live path also forces 0 when start
                // is 0 — the same result.)
                return Packet.ForceTrigger(apply, side, 1, [b("start"), b("strength"), 0]);
            }
            case TriggerMode.Recoil: // wire mode 2, SDK "Sniper"
                return Packet.ForceTrigger(apply, side, 2, [b("start"), b("press"), b("strength"), b("frequency"), b("match")]);
            case TriggerMode.Sniper: // wire mode 3, SDK "Recoil"; slot 4 is genuinely empty
                return Packet.ForceTrigger(apply, side, 3, [b("start"), b("travel"), b("resistance"), 0, b("match")]);
            case TriggerMode.Lock:
                // Strength 255 and input matching on in every call Flydigi makes.
                return Packet.ForceTrigger(apply, side, 4, [b("start"), 255, 1]);
            default:
                throw new ArgumentOutOfRangeException(nameof(Mode), Mode, null);
        }
    }

    /// <summary>Language-neutral, e.g. <c>Race(start=10, strength=30)</c>: it goes to the log and serves as a dedupe key.</summary>
    public override string ToString()
    {
        var defs = TriggerModes.Params[Mode];
        if (defs.Length == 0) return Mode.ToString();
        return $"{Mode}({string.Join(", ", defs.Select(p => $"{p.Key}={this[p.Key]}"))})";
    }
}
