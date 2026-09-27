using TCNet.Text;

namespace TCNet;

/// <summary>One layer of a Time packet.</summary>
public sealed class TimeLayer
{
    internal TimeLayer(int index) => Index = index;

    /// <summary>0-based wire index (0 = layer 1 … 7 = layer C).</summary>
    public int Index { get; }
    public string Label => TCNetText.LayerLabel(Index);

    public uint TimeMs { get; set; }
    public uint TotalMs { get; set; }
    /// <summary>0 = unknown, 1–4.</summary>
    public byte BeatMarker { get; set; }
    public LayerState State { get; set; }
    /// <summary>0 = use the general mode (byte 105).</summary>
    public SmpteMode SmpteMode { get; set; }
    public TimecodeState TimecodeState { get; set; }
    public Timecode Timecode { get; set; }
    /// <summary>Fader position 0–255; ≥ 1 = on air.</summary>
    public byte OnAir { get; set; }

    public bool IsOnAir => OnAir >= 1;
    public uint RemainingMs => TotalMs > TimeMs ? TotalMs - TimeMs : 0;
}

/// <summary>
/// Type 254 · Time (162 bytes): broadcast to 60001 every 1–40 ms and unicast to local nodes.
/// Times 24 + 4i, totals 56 + 4i, beat markers 88 + i, states 96 + i, general SMPTE 105,
/// per-layer SMPTE/state/HH/MM/SS/FF 106 + 6i, on-air 154 + i.
/// </summary>
/// <remarks>The table prints LC Time at 48 and LC Beat Marker at 94; the sequence gives 52 and 95.</remarks>
public sealed class TimePacket : TCNetPacket
{
    public TimePacket()
    {
        for (int i = 0; i < 8; i++) Layers[i] = new TimeLayer(i);
    }

    public override MessageType MessageType => MessageType.Time;
    public override string Name => "Time";
    public override int Length => TCNetConstants.TimeLength;

    public TimeLayer[] Layers { get; } = new TimeLayer[8];
    public TimeLayer this[Layer layer] => Layers[(int)layer - 1];

    /// <summary>General SMPTE mode (byte 105).</summary>
    public SmpteMode SmpteMode { get; set; } = SmpteMode.Fps30;

    public SmpteMode ModeOf(TimeLayer l) => l.SmpteMode == SmpteMode.General ? SmpteMode : l.SmpteMode;

    /// <summary>Sets a layer's timecode from its time using its effective mode.</summary>
    public void ComputeTimecode(TimeLayer l) => l.Timecode = Timecode.FromMilliseconds(l.TimeMs, ModeOf(l));

    protected override void Encode(Span<byte> p)
    {
        for (int i = 0; i < 8; i++)
        {
            var l = Layers[i];
            Wire.PutU32(p, 24 + 4 * i, l.TimeMs);
            Wire.PutU32(p, 56 + 4 * i, l.TotalMs);
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

    protected internal override void Decode(ReadOnlySpan<byte> p, int datagramLength)
    {
        for (int i = 0; i < 8; i++)
        {
            var l = Layers[i];
            l.TimeMs = Wire.U32(p, 24 + 4 * i);
            l.TotalMs = Wire.U32(p, 56 + 4 * i);
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
        foreach (var l in Layers) f.Add(new(24 + 4 * l.Index, 4, $"L{l.Label} Time", $"{l.TimeMs} ms", TCNetUnits.Ms(l.TimeMs)));
        foreach (var l in Layers) f.Add(new(56 + 4 * l.Index, 4, $"L{l.Label} Total Time", $"{l.TotalMs} ms", TCNetUnits.Ms(l.TotalMs)));
        foreach (var l in Layers) f.Add(new(88 + l.Index, 1, $"L{l.Label} Beat Marker", l.BeatMarker.ToString(), l.BeatMarker == 0 ? "unknown" : $"beat {l.BeatMarker}"));
        foreach (var l in Layers) f.Add(new(96 + l.Index, 1, $"L{l.Label} Layer State", ((byte)l.State).ToString(), TCNetText.Describe(l.State)));
        f.Add(new(105, 1, "SMPTE Mode", ((byte)SmpteMode).ToString(), TCNetText.Describe(SmpteMode)));
        foreach (var l in Layers)
        {
            int t = 106 + 6 * l.Index;
            f.Add(new(t, 1, $"L{l.Label} SMPTE Mode", ((byte)l.SmpteMode).ToString(), TCNetText.Describe(l.SmpteMode)));
            f.Add(new(t + 1, 1, $"L{l.Label} Time Code State", ((byte)l.TimecodeState).ToString(), TCNetText.Describe(l.TimecodeState)));
            f.Add(new(t + 2, 4, $"L{l.Label} Time Code", l.Timecode.ToString(), "hours:minutes:seconds:frames"));
        }
        foreach (var l in Layers) f.Add(new(154 + l.Index, 1, $"L{l.Label} Layer OnAir", l.OnAir.ToString(), l.IsOnAir ? "on air" : "not on air"));
    }

    public override string Summary =>
        string.Join("  ", Layers.Where(l => l.State != LayerState.Idle || l.TimeMs != 0).Select(l => $"{l.Label}:{TCNetUnits.Ms(l.TimeMs)} {TCNetText.Short(l.State)}"));
}
