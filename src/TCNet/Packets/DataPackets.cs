using TCNet.Text;

namespace TCNet;

/// <summary>Type 200 · Data. Byte 24 = data type, byte 25 = layer (mixer ID for mixer data).</summary>
public abstract class DataPacket : TCNetPacket
{
    public override MessageType MessageType => MessageType.Data;
    public abstract DataType DataType { get; }

    /// <summary>Layer 1–8 at byte 25.</summary>
    public byte LayerId { get; set; }

    protected sealed override void Encode(Span<byte> p)
    {
        p[24] = (byte)DataType;
        p[25] = LayerId;
        EncodeData(p);
    }

    protected internal sealed override void Decode(ReadOnlySpan<byte> p, int datagramLength)
    {
        LayerId = p[25];
        DecodeData(p, datagramLength);
    }

    protected sealed override void DescribeBody(List<TCNetField> f)
    {
        f.Add(new(24, 1, "Data Type", ((byte)DataType).ToString(), TCNetText.Describe(DataType)));
        f.Add(this is MixerDataPacket
            ? new(25, 1, "Mixer ID", LayerId.ToString())
            : new(25, 1, "Layer ID", LayerId.ToString(), $"layer {TCNetText.LayerName(LayerId)}"));
        DescribeData(f);
    }

    protected abstract void EncodeData(Span<byte> p);
    protected abstract void DecodeData(ReadOnlySpan<byte> p, int datagramLength);
    protected abstract void DescribeData(List<TCNetField> f);

    public override string Summary => $"layer {TCNetText.LayerName(LayerId)}";
}

/// <summary>Data type 2 · Metrics (122 bytes).</summary>
public sealed class MetricsPacket : DataPacket
{
    public override DataType DataType => DataType.Metrics;
    public override string Name => "Data · Metrics";
    public override int Length => TCNetConstants.MetricsLength;

    public LayerState State { get; set; }
    /// <summary>0 = slave, 1 = master.</summary>
    public byte SyncMaster { get; set; }
    /// <summary>1–4 (0 = unknown).</summary>
    public byte BeatMarker { get; set; }
    public uint TrackLengthMs { get; set; }
    public uint PositionMs { get; set; }
    /// <summary>32768 = 100 %.</summary>
    public uint Speed { get; set; }
    public uint BeatNumber { get; set; }
    /// <summary>BPM × 100.</summary>
    public uint BpmX100 { get; set; }
    /// <summary>32768 = 100 %.</summary>
    public ushort PitchBend { get; set; }
    public uint TrackId { get; set; }

    public double Bpm
    {
        get => BpmX100 / 100.0;
        set => BpmX100 = (uint)Math.Round(value * 100);
    }

    public double SpeedRatio => Speed / (double)TCNetConstants.SpeedUnity;
    public double PitchRatio => PitchBend / (double)TCNetConstants.SpeedUnity;

    protected override void EncodeData(Span<byte> p)
    {
        p[27] = (byte)State;
        p[29] = SyncMaster;
        p[31] = BeatMarker;
        Wire.PutU32(p, 32, TrackLengthMs);
        Wire.PutU32(p, 36, PositionMs);
        Wire.PutU32(p, 40, Speed);
        Wire.PutU32(p, 57, BeatNumber);
        Wire.PutU32(p, 112, BpmX100);
        Wire.PutU16(p, 116, PitchBend);
        Wire.PutU32(p, 118, TrackId);
    }

    protected override void DecodeData(ReadOnlySpan<byte> p, int datagramLength)
    {
        State = (LayerState)p[27];
        SyncMaster = p[29];
        BeatMarker = p[31];
        TrackLengthMs = Wire.U32(p, 32);
        PositionMs = Wire.U32(p, 36);
        Speed = Wire.U32(p, 40);
        BeatNumber = Wire.U32(p, 57);
        BpmX100 = Wire.U32(p, 112);
        PitchBend = Wire.U16(p, 116);
        TrackId = Wire.U32(p, 118);
    }

    protected override void DescribeData(List<TCNetField> f)
    {
        f.Add(new(27, 1, "Layer State", ((byte)State).ToString(), TCNetText.Describe(State)));
        f.Add(new(29, 1, "Sync Master", SyncMaster.ToString(), SyncMaster == 1 ? "Master" : "Slave"));
        f.Add(new(31, 1, "Beat Marker", BeatMarker.ToString(), BeatMarker == 0 ? "unknown" : $"beat {BeatMarker} of 4"));
        f.Add(new(32, 4, "Track Length", $"{TrackLengthMs} ms", TCNetUnits.Ms(TrackLengthMs)));
        f.Add(new(36, 4, "Current Position", $"{PositionMs} ms", TCNetUnits.Ms(PositionMs)));
        f.Add(new(40, 4, "Speed", Speed.ToString(), TCNetUnits.Percent(SpeedRatio)));
        f.Add(new(57, 4, "Beat Number", BeatNumber.ToString()));
        f.Add(new(112, 4, "BPM", BpmX100.ToString(), $"{Bpm:0.00} BPM"));
        f.Add(new(116, 2, "Pitch Bend", PitchBend.ToString(), TCNetUnits.Percent(PitchRatio)));
        f.Add(new(118, 4, "Track ID", TrackId.ToString()));
    }

    public override string Summary =>
        $"L{TCNetText.LayerName(LayerId)} {TCNetText.Short(State)} {TCNetUnits.Ms(PositionMs)}/{TCNetUnits.Ms(TrackLengthMs)} {Bpm:0.00} BPM beat {BeatMarker}";
}

/// <summary>Metadata text encoding.</summary>
public enum TextEncoding
{
    /// <summary>UTF-16LE for protocol ≥ 3.5, UTF-8 before (from the packet's header version).</summary>
    Auto,
    Utf8,
    Utf16,
}

/// <summary>Data type 4 · Metadata (548 bytes). Artist 29–284, title 285–540, key 541, track ID 543.</summary>
public sealed class MetadataPacket : DataPacket
{
    public override DataType DataType => DataType.Metadata;
    public override string Name => "Data · Metadata";
    public override int Length => TCNetConstants.MetadataLength;

    public string Artist { get; set; } = "";
    public string Title { get; set; } = "";
    public ushort Key { get; set; }
    public uint TrackId { get; set; }
    public TextEncoding Encoding { get; set; }

    public TextEncoding EffectiveEncoding => Encoding != TextEncoding.Auto ? Encoding : IsUtf16Era ? TextEncoding.Utf16 : TextEncoding.Utf8;

    protected override void EncodeData(Span<byte> p)
    {
        if (EffectiveEncoding == TextEncoding.Utf16)
        {
            Wire.PutUtf16(p, 29, 256, Artist);
            Wire.PutUtf16(p, 285, 256, Title);
        }
        else
        {
            Wire.PutUtf8(p, 29, 256, Artist);
            Wire.PutUtf8(p, 285, 256, Title);
        }
        Wire.PutU16(p, 541, Key);
        Wire.PutU32(p, 543, TrackId);
    }

    protected override void DecodeData(ReadOnlySpan<byte> p, int datagramLength)
    {
        bool utf16 = EffectiveEncoding == TextEncoding.Utf16;
        Artist = utf16 ? Wire.Utf16(p, 29, 256) : Wire.Utf8(p, 29, 256);
        Title = utf16 ? Wire.Utf16(p, 285, 256) : Wire.Utf8(p, 285, 256);
        Key = Wire.U16(p, 541);
        TrackId = Wire.U32(p, 543);
    }

    protected override void DescribeData(List<TCNetField> f)
    {
        string enc = EffectiveEncoding == TextEncoding.Utf16 ? "UTF-16" : "UTF-8";
        f.Add(new(29, 256, "Track Artist", Artist, enc));
        f.Add(new(285, 256, "Track Title", Title, enc));
        f.Add(new(541, 2, "Track Key", Key.ToString()));
        f.Add(new(543, 4, "Track ID", TrackId.ToString()));
    }

    public override string Summary => $"L{TCNetText.LayerName(LayerId)} {Artist} – {Title} (#{TrackId})";
}

public readonly record struct CueColor(byte R, byte G, byte B)
{
    public override string ToString() => $"#{R:X2}{G:X2}{B:X2}";
}

public sealed class Cue
{
    public byte Type { get; set; }
    public uint InMs { get; set; }
    public uint OutMs { get; set; }
    public CueColor Color { get; set; }
    public bool IsEmpty => Type == 0 && InMs == 0 && OutMs == 0 && Color == default;
}

/// <summary>
/// Data type 12 · Cue Data: Loop IN (42), Loop OUT (46) and 18 cues of 22 bytes (type +0, in +2, out +6, RGB +11).
/// </summary>
/// <remarks>
/// The spec prints cue 1 at 47, overlapping Loop OUT (46–49). With <see cref="CueLayout.Printed"/> an empty cue 1 is not
/// written so Loop OUT survives; a set cue 1 owns bytes 47–49. When reading, bytes 47–49 count as cue 1 when cue 1 has other
/// non-zero bytes, or when they look like a cue on their own (bytes 46 and 48 zero, a type at 47); otherwise they are Loop OUT.
/// </remarks>
public sealed class CueDataPacket : DataPacket
{
    public const int CueCount = 18, Stride = 22;

    /// <summary>Layout for new instances (and the parser).</summary>
    public static CueLayout DefaultLayout { get; set; } = CueLayout.Printed;

    public CueDataPacket()
    {
        for (int i = 0; i < CueCount; i++) Cues[i] = new Cue();
    }

    public override DataType DataType => DataType.CueData;
    public override string Name => "Data · Cue Data";
    public CueLayout Layout { get; set; } = DefaultLayout;
    public int FirstCueOffset => Layout == CueLayout.AfterLoop ? 50 : 47;
    public override int Length => FirstCueOffset + CueCount * Stride;

    public uint LoopInMs { get; set; }
    public uint LoopOutMs { get; set; }
    public Cue[] Cues { get; } = new Cue[CueCount];

    /// <summary>True when a set cue 1 occupied bytes 47–49, so Loop OUT could not be read.</summary>
    public bool LoopOutOverlapped { get; private set; }

    protected override void EncodeData(Span<byte> p)
    {
        Wire.PutU32(p, 42, LoopInMs);
        Wire.PutU32(p, 46, LoopOutMs);
        for (int i = 0; i < CueCount; i++)
        {
            var c = Cues[i];
            if (i == 0 && Layout == CueLayout.Printed && c.IsEmpty) continue;
            int o = FirstCueOffset + i * Stride;
            p[o] = c.Type;
            p[o + 1] = 0;
            Wire.PutU32(p, o + 2, c.InMs);
            Wire.PutU32(p, o + 6, c.OutMs);
            p[o + 11] = c.Color.R;
            p[o + 12] = c.Color.G;
            p[o + 13] = c.Color.B;
        }
    }

    protected override void DecodeData(ReadOnlySpan<byte> p, int datagramLength)
    {
        LoopInMs = Wire.U32(p, 42);
        LoopOutMs = Wire.U32(p, 46);
        LoopOutOverlapped = false;
        for (int i = 0; i < CueCount; i++)
        {
            int o = FirstCueOffset + i * Stride;
            var c = Cues[i];
            if (i == 0 && Layout == CueLayout.Printed)
            {
                bool restSet = p.Slice(50, 19).IndexOfAnyExcept((byte)0) >= 0;
                bool looksLikeCue = p[46] == 0 && p[47] != 0 && p[48] == 0;
                if (!restSet && !looksLikeCue)
                {
                    c.Type = 0; c.InMs = 0; c.OutMs = 0; c.Color = default;
                    continue;
                }
                LoopOutOverlapped = restSet;
                if (!restSet) LoopOutMs = 0;
            }
            c.Type = p[o];
            c.InMs = Wire.U32(p, o + 2);
            c.OutMs = Wire.U32(p, o + 6);
            c.Color = new CueColor(p[o + 11], p[o + 12], p[o + 13]);
        }
    }

    protected override void DescribeData(List<TCNetField> f)
    {
        f.Add(new(42, 4, "Loop IN", $"{LoopInMs} ms", TCNetUnits.Ms(LoopInMs)));
        f.Add(new(46, 4, "Loop OUT", $"{LoopOutMs} ms", LoopOutOverlapped ? "shares bytes 47–49 with cue 1" : TCNetUnits.Ms(LoopOutMs)));
        for (int i = 0; i < CueCount; i++)
        {
            var c = Cues[i];
            if (c.IsEmpty) continue;
            int o = FirstCueOffset + i * Stride;
            f.Add(new(o, 1, $"CUE {i + 1} Type", c.Type.ToString()));
            f.Add(new(o + 2, 4, $"CUE {i + 1} IN", $"{c.InMs} ms", TCNetUnits.Ms(c.InMs)));
            f.Add(new(o + 6, 4, $"CUE {i + 1} OUT", $"{c.OutMs} ms", TCNetUnits.Ms(c.OutMs)));
            f.Add(new(o + 11, 3, $"CUE {i + 1} Color", c.Color.ToString(), "red, green, blue"));
        }
    }

    public override string Summary => $"L{TCNetText.LayerName(LayerId)} loop {LoopInMs}–{LoopOutMs} ms, {Cues.Count(c => !c.IsEmpty)} cues";
}

/// <summary>One of six mixer channel strips (24 bytes from 125 + 24 × (n − 1)).</summary>
public sealed class MixerChannel
{
    private readonly byte[] _b;

    internal MixerChannel(byte[] buffer, int number)
    {
        _b = buffer;
        Number = number;
        Offset = 125 + 24 * (number - 1);
    }

    public int Number { get; }
    public int Offset { get; }

    private byte this[int i] { get => _b[Offset + i]; set => _b[Offset + i] = value; }

    public ChannelSource Source { get => (ChannelSource)this[0]; set => this[0] = (byte)value; }
    public byte AudioLevel { get => this[1]; set => this[1] = value; }
    public byte FaderLevel { get => this[2]; set => this[2] = value; }
    public byte TrimLevel { get => this[3]; set => this[3] = value; }
    public byte CompLevel { get => this[4]; set => this[4] = value; }
    public byte EqHi { get => this[5]; set => this[5] = value; }
    public byte EqHiMid { get => this[6]; set => this[6] = value; }
    public byte EqLowMid { get => this[7]; set => this[7] = value; }
    public byte EqLow { get => this[8]; set => this[8] = value; }
    public byte FilterColor { get => this[9]; set => this[9] = value; }
    public byte Send { get => this[10]; set => this[10] = value; }
    public bool CueA { get => this[11] != 0; set => this[11] = value ? (byte)1 : (byte)0; }
    public bool CueB { get => this[12] != 0; set => this[12] = value ? (byte)1 : (byte)0; }
    public CrossfaderAssign CrossfaderAssign { get => (CrossfaderAssign)this[13]; set => this[13] = (byte)value; }

    internal static readonly string[] FieldNames =
    [
        "Source Select", "Audio Level", "Fader Level", "Trim Level", "Comp Level", "EQ Hi Level", "EQ Hi Mid Level",
        "EQ Low Mid Level", "EQ Low Level", "Filter/Color", "Send", "CUE A", "CUE B", "Crossfader Assign",
    ];
}

/// <summary>Data type 150 · Mixer Data (270 bytes). Byte 25 is the mixer ID.</summary>
public sealed class MixerDataPacket : DataPacket
{
    private readonly byte[] _b = new byte[TCNetConstants.MixerLength];

    public MixerDataPacket()
    {
        Channels = Enumerable.Range(1, 6).Select(n => new MixerChannel(_b, n)).ToArray();
        SendReturn3Source = MixerChannelSelect.None;
        SendReturn3Type = SendReturnType.None;
        BeatFxChannel = MixerChannelSelect.None;
    }

    public override DataType DataType => DataType.Mixer;
    public override string Name => "Data · Mixer";
    public override int Length => TCNetConstants.MixerLength;

    public byte MixerId { get => LayerId; set => LayerId = value; }
    public MixerType MixerType { get => (MixerType)_b[26]; set => _b[26] = (byte)value; }
    public string MixerName { get; set; } = "";
    public IReadOnlyList<MixerChannel> Channels { get; }

    private bool Flag(int o) => _b[o] != 0;
    private void Flag(int o, bool v) => _b[o] = v ? (byte)1 : (byte)0;

    public byte MicEqHi { get => _b[59]; set => _b[59] = value; }
    public byte MicEqLow { get => _b[60]; set => _b[60] = value; }
    public byte MasterAudioLevel { get => _b[61]; set => _b[61] = value; }
    public byte MasterFaderLevel { get => _b[62]; set => _b[62] = value; }
    public bool LinkCueA { get => Flag(67); set => Flag(67, value); }
    public bool LinkCueB { get => Flag(68); set => Flag(68, value); }
    public byte MasterFilter { get => _b[69]; set => _b[69] = value; }
    public bool MasterCueA { get => Flag(71); set => Flag(71, value); }
    public bool MasterCueB { get => Flag(72); set => Flag(72, value); }
    public bool MasterIsolatorOn { get => Flag(74); set => Flag(74, value); }
    public byte MasterIsolatorHi { get => _b[75]; set => _b[75] = value; }
    public byte MasterIsolatorMid { get => _b[76]; set => _b[76] = value; }
    public byte MasterIsolatorLow { get => _b[77]; set => _b[77] = value; }
    public byte FilterHpf { get => _b[79]; set => _b[79] = value; }
    public byte FilterLpf { get => _b[80]; set => _b[80] = value; }
    public byte FilterResonance { get => _b[81]; set => _b[81] = value; }
    public byte SendFxEffect { get => _b[84]; set => _b[84] = value; }
    public bool SendFxExt1 { get => Flag(85); set => Flag(85, value); }
    public bool SendFxExt2 { get => Flag(86); set => Flag(86, value); }
    public byte SendFxMasterMix { get => _b[87]; set => _b[87] = value; }
    public byte SendFxSizeFeedback { get => _b[88]; set => _b[88] = value; }
    public byte SendFxTime { get => _b[89]; set => _b[89] = value; }
    public byte SendFxHpf { get => _b[90]; set => _b[90] = value; }
    public byte SendFxLevel { get => _b[91]; set => _b[91] = value; }
    public MixerChannelSelect SendReturn3Source { get => (MixerChannelSelect)_b[92]; set => _b[92] = (byte)value; }
    public SendReturnType SendReturn3Type { get => (SendReturnType)_b[93]; set => _b[93] = (byte)value; }
    public bool SendReturn3On { get => Flag(94); set => Flag(94, value); }
    public byte SendReturn3Level { get => _b[95]; set => _b[95] = value; }
    public byte ChannelFaderCurve { get => _b[97]; set => _b[97] = value; }
    public byte CrossFaderCurve { get => _b[98]; set => _b[98] = value; }
    public byte CrossFader { get => _b[99]; set => _b[99] = value; }
    public bool BeatFxOn { get => Flag(100); set => Flag(100, value); }
    public byte BeatFxLevel { get => _b[101]; set => _b[101] = value; }
    public MixerChannelSelect BeatFxChannel { get => (MixerChannelSelect)_b[102]; set => _b[102] = (byte)value; }
    public byte BeatFxSelect { get => _b[103]; set => _b[103] = value; }
    public byte BeatFxFreqHi { get => _b[104]; set => _b[104] = value; }
    public byte BeatFxFreqMid { get => _b[105]; set => _b[105] = value; }
    public byte BeatFxFreqLow { get => _b[106]; set => _b[106] = value; }
    public byte HeadphonesPreEq { get => _b[107]; set => _b[107] = value; }
    public byte HeadphonesALevel { get => _b[108]; set => _b[108] = value; }
    public byte HeadphonesAMix { get => _b[109]; set => _b[109] = value; }
    public byte HeadphonesBLevel { get => _b[110]; set => _b[110] = value; }
    public byte HeadphonesBMix { get => _b[111]; set => _b[111] = value; }
    public byte BoothLevel { get => _b[112]; set => _b[112] = value; }
    public byte BoothEqHi { get => _b[113]; set => _b[113] = value; }
    public byte BoothEqLow { get => _b[114]; set => _b[114] = value; }

    /// <summary>Raw access to data bytes 26–269, except the mixer name (29–44), which has its own property.</summary>
    public byte this[int offset]
    {
        get => _b[offset];
        set
        {
            if (offset < 26 || offset >= TCNetConstants.MixerLength || offset is >= 29 and <= 44)
                throw new ArgumentOutOfRangeException(nameof(offset), "Use the header properties, MixerId or MixerName.");
            _b[offset] = value;
        }
    }

    protected override void EncodeData(Span<byte> p)
    {
        _b.AsSpan(26).CopyTo(p[26..]);
        Wire.PutAscii(p, 29, 16, MixerName);
    }

    protected override void DecodeData(ReadOnlySpan<byte> p, int datagramLength)
    {
        p[..TCNetConstants.MixerLength].CopyTo(_b);
        _b.AsSpan(0, 26).Clear();
        MixerName = Wire.Ascii(p, 29, 16);
        _b.AsSpan(29, 16).Clear();
    }

    internal static readonly (int Offset, string Name)[] MasterFields =
    [
        (59, "Mic EQ Hi"), (60, "Mic EQ Low"), (61, "Master Audio Level"), (62, "Master Fader Level"), (67, "Link Cue A"),
        (68, "Link Cue B"), (69, "Master Filter"), (71, "Master CUE A"), (72, "Master CUE B"), (74, "Master Isolator ON/OFF"),
        (75, "Master Isolator Hi"), (76, "Master Isolator Mid"), (77, "Master Isolator Low"), (79, "Filter HPF"), (80, "Filter LPF"),
        (81, "Filter Resonance"), (84, "Send FX Effect"), (85, "Send FX Ext 1"), (86, "Send FX Ext 2"), (87, "Send FX Master Mix"),
        (88, "Send FX Size Feedback"), (89, "Send FX Time"), (90, "Send FX HPF"), (91, "Send FX Level"),
        (92, "Send Return 3 Source Select"), (93, "Send Return 3 Type"), (94, "Send Return 3 ON/OFF"), (95, "Send Return 3 Level"),
        (97, "Channel Fader Curve"), (98, "Cross Fader Curve"), (99, "Cross Fader"), (100, "BeatFX ON/OFF"),
        (101, "BeatFX Level/Depth"), (102, "BeatFX Channel Select"), (103, "BeatFX Select"), (104, "BeatFX Freq Hi"),
        (105, "BeatFX Freq Mid"), (106, "BeatFX Freq Low"), (107, "Headphones Pre EQ"), (108, "Headphones A Level"),
        (109, "Headphones A Mix"), (110, "Headphones B Level"), (111, "Headphones B Mix"), (112, "Booth Level"),
        (113, "Booth EQ Hi"), (114, "Booth EQ Low"),
    ];

    protected override void DescribeData(List<TCNetField> f)
    {
        f.Add(new(26, 1, "Mixer Type", _b[26].ToString(), TCNetText.Describe(MixerType)));
        f.Add(new(29, 16, "Mixer Name", MixerName));
        foreach (var (o, n) in MasterFields)
        {
            string? meaning = o switch
            {
                92 => TCNetText.Describe(SendReturn3Source),
                93 => TCNetText.Describe(SendReturn3Type),
                102 => TCNetText.Describe(BeatFxChannel),
                67 or 68 or 71 or 72 or 74 or 85 or 86 or 94 or 100 => _b[o] != 0 ? "On" : "Off",
                _ => null,
            };
            f.Add(new(o, 1, n, _b[o].ToString(), meaning));
        }
        foreach (var ch in Channels)
        {
            for (int i = 0; i < MixerChannel.FieldNames.Length; i++)
            {
                string? meaning = i switch
                {
                    0 => TCNetText.Describe(ch.Source),
                    11 => ch.CueA ? "On" : "Off",
                    12 => ch.CueB ? "On" : "Off",
                    13 => TCNetText.Describe(ch.CrossfaderAssign),
                    _ => null,
                };
                f.Add(new(ch.Offset + i, 1, $"Channel {ch.Number} {MixerChannel.FieldNames[i]}", _b[ch.Offset + i].ToString(), meaning));
            }
        }
    }

    public override string Summary =>
        $"{MixerName} master {MasterFaderLevel} xf {CrossFader} ch " + string.Join("/", Channels.Select(c => c.FaderLevel));
}

/// <summary>A type 200/204 packet with a data type this library doesn't model; bytes 26+ kept raw.</summary>
public sealed class UnknownDataPacket : DataPacket
{
    private MessageType _message;
    private DataType _data;

    public UnknownDataPacket() : this(MessageType.Data, 0) { }

    public UnknownDataPacket(MessageType message, DataType data)
    {
        _message = message;
        _data = data;
    }

    public override MessageType MessageType => _message;
    public override DataType DataType => _data;
    public override string Name => $"Data · unknown type {(byte)_data}";
    public override int Length => 26 + Payload.Length;
    public byte[] Payload { get; set; } = [];

    protected override void EncodeData(Span<byte> p) => Payload.CopyTo(p[26..]);

    protected override void DecodeData(ReadOnlySpan<byte> p, int datagramLength)
    {
        _message = (MessageType)p[7];
        _data = (DataType)p[24];
        Payload = p.Slice(26, Math.Max(0, datagramLength - 26)).ToArray();
    }

    protected override void DescribeData(List<TCNetField> f) => f.Add(new(26, Payload.Length, "Payload", Wire.Hex(Payload)));
}
