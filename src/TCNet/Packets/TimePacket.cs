using TCNet.Text;

namespace TCNet;

/// <summary>Per-layer block of a Time packet.</summary>
public sealed class TimeLayer
{
    internal TimeLayer(int index) => Index = index;

    /// <summary>0-based wire index (0 = layer 1 … 7 = layer C).</summary>
    public int Index { get; }

    public TCNetLayer Layer => (TCNetLayer)(Index + 1);

    /// <summary>Current time in ms.</summary>
    public uint CurrentTimeMs { get; set; }

    /// <summary>Total time in ms.</summary>
    public uint TotalTimeMs { get; set; }

    /// <summary>0 = unknown, 1–4 = beat marker position.</summary>
    public byte BeatMarker { get; set; }

    public LayerState State { get; set; }

    /// <summary>0 = use the general SMPTE mode (byte 105).</summary>
    public SmpteMode SmpteMode { get; set; }

    public TimecodeState TimecodeState { get; set; }

    public Timecode Timecode { get; set; }

    /// <summary>On-air / fader position 0–255 (0 = not on air).</summary>
    public byte OnAir { get; set; }

    public bool IsOnAir => OnAir >= 1;

    /// <summary>Remaining time in ms.</summary>
    public uint RemainingMs => TotalTimeMs > CurrentTimeMs ? TotalTimeMs - CurrentTimeMs : 0;
}

/// <summary>
/// Type 254 – Time packet (162 bytes). Broadcast to 60001 every 1–40 ms (and unicast to local nodes).
/// Layer order 1, 2, 3, 4, A, B, M, C.
/// </summary>
/// <remarks>The spec prints LC Time at 48 and LC Beat Marker at 94; the sequence makes them 52 and 95.</remarks>
public sealed class TimePacket : TCNetPacket
{
    public TimePacket()
    {
        for (int i = 0; i < Layers.Length; i++) Layers[i] = new TimeLayer(i);
    }

    public override MessageType MessageType => MessageType.Time;
    public override string Name => "Time";
    public override int Length => TCNetConstants.TimeSize;

    public TimeLayer[] Layers { get; } = new TimeLayer[TCNetConstants.LayerCount];

    /// <summary>General SMPTE mode (byte 105).</summary>
    public SmpteMode SmpteMode { get; set; } = SmpteMode.Fps30;

    public TimeLayer this[TCNetLayer layer] => Layers[(int)layer - 1];

    /// <summary>Layer SMPTE mode, falling back to the general mode when it is 0.</summary>
    public SmpteMode EffectiveSmpteMode(TimeLayer layer) => layer.SmpteMode == SmpteMode.UseGeneral ? SmpteMode : layer.SmpteMode;

    /// <summary>Fills a layer's timecode from its current time using the effective SMPTE mode.</summary>
    public void UpdateTimecodeFromTime(TimeLayer layer) =>
        layer.Timecode = Timecode.FromMilliseconds(layer.CurrentTimeMs, EffectiveSmpteMode(layer));

    protected override void WriteBody(Span<byte> p)
    {
        for (int i = 0; i < 8; i++)
        {
            var l = Layers[i];
            Wire.U32(p, 24 + 4 * i, l.CurrentTimeMs);
            Wire.U32(p, 56 + 4 * i, l.TotalTimeMs);
            p[88 + i] = l.BeatMarker;
            p[96 + i] = (byte)l.State;
            int t = 106 + 6 * i;
            p[t] = (byte)l.SmpteMode;
            p[t + 1] = (byte)l.TimecodeState;
            p[t + 2] = l.Timecode.Hours;
            p[t + 3] = l.Timecode.Minutes;
            p[t + 4] = l.Timecode.Seconds;
            p[t + 5] = l.Timecode.Frames;
            p[154 + i] = l.OnAir;
        }
        p[105] = (byte)SmpteMode;
    }

    protected internal override void ReadBody(ReadOnlySpan<byte> p, int receivedLength)
    {
        for (int i = 0; i < 8; i++)
        {
            var l = Layers[i];
            l.CurrentTimeMs = Wire.U32(p, 24 + 4 * i);
            l.TotalTimeMs = Wire.U32(p, 56 + 4 * i);
            l.BeatMarker = p[88 + i];
            l.State = (LayerState)p[96 + i];
            int t = 106 + 6 * i;
            l.SmpteMode = (SmpteMode)p[t];
            l.TimecodeState = (TimecodeState)p[t + 1];
            l.Timecode = new Timecode(p[t + 2], p[t + 3], p[t + 4], p[t + 5]);
            l.OnAir = p[154 + i];
        }
        SmpteMode = (SmpteMode)p[105];
    }

    protected override void DescribeBody(List<TCNetField> f)
    {
        for (int i = 0; i < 8; i++) f.Add(new(24 + 4 * i, 4, $"L{TCNetText.LayerLabel(i)} Time", $"{Layers[i].CurrentTimeMs} ms", TCNetUnits.FormatMs(Layers[i].CurrentTimeMs)));
        for (int i = 0; i < 8; i++) f.Add(new(56 + 4 * i, 4, $"L{TCNetText.LayerLabel(i)} Total Time", $"{Layers[i].TotalTimeMs} ms", TCNetUnits.FormatMs(Layers[i].TotalTimeMs)));
        for (int i = 0; i < 8; i++) f.Add(new(88 + i, 1, $"L{TCNetText.LayerLabel(i)} Beat Marker", Layers[i].BeatMarker.ToString()));
        for (int i = 0; i < 8; i++) f.Add(new(96 + i, 1, $"L{TCNetText.LayerLabel(i)} Layer State", ((byte)Layers[i].State).ToString(), TCNetText.Describe(Layers[i].State)));
        f.Add(new(105, 1, "SMPTE Mode", ((byte)SmpteMode).ToString(), TCNetText.Describe(SmpteMode)));
        for (int i = 0; i < 8; i++)
        {
            var l = Layers[i];
            int t = 106 + 6 * i;
            f.Add(new(t, 1, $"L{TCNetText.LayerLabel(i)} SMPTE Mode", ((byte)l.SmpteMode).ToString(), TCNetText.Describe(l.SmpteMode)));
            f.Add(new(t + 1, 1, $"L{TCNetText.LayerLabel(i)} Time Code State", ((byte)l.TimecodeState).ToString(), TCNetText.Describe(l.TimecodeState)));
            f.Add(new(t + 2, 4, $"L{TCNetText.LayerLabel(i)} Time Code", l.Timecode.ToString()));
        }
        for (int i = 0; i < 8; i++) f.Add(new(154 + i, 1, $"L{TCNetText.LayerLabel(i)} Layer OnAir", Layers[i].OnAir.ToString(), Layers[i].IsOnAir ? "On air" : "Not on air"));
    }

    public override string Summary =>
        string.Join("  ", Layers.Where(l => l.State != LayerState.Idle || l.CurrentTimeMs != 0)
            .Select(l => $"{TCNetText.LayerLabel(l.Index)}:{TCNetUnits.FormatMs(l.CurrentTimeMs)} {TCNetText.Short(l.State)}"));
}
