using TCNet.Text;

namespace TCNet;

/// <summary>Type 200 – Data packet. Byte 24 is the data type, byte 25 the layer (or mixer ID).</summary>
public abstract class DataPacket : TCNetPacket
{
    public override MessageType MessageType => MessageType.Data;

    /// <summary>Data type at byte 24.</summary>
    public abstract DataType DataType { get; }

    /// <summary>Raw layer byte at 25 (1–8).</summary>
    public byte LayerId { get; set; }

    /// <summary>Layer at byte 25.</summary>
    public TCNetLayer Layer
    {
        get => (TCNetLayer)LayerId;
        set => LayerId = (byte)value;
    }

    protected sealed override void WriteBody(Span<byte> p)
    {
        p[24] = (byte)DataType;
        p[25] = LayerId;
        WriteData(p);
    }

    protected internal sealed override void ReadBody(ReadOnlySpan<byte> p, int receivedLength)
    {
        LayerId = p[25];
        ReadData(p, receivedLength);
    }

    protected sealed override void DescribeBody(List<TCNetField> f)
    {
        f.Add(new(24, 1, "Data Type", ((byte)DataType).ToString(), TCNetText.Describe(DataType)));
        f.Add(new(25, 1, LayerFieldName, LayerId.ToString(), this is MixerDataPacket ? null : TCNetText.LayerName(LayerId)));
        DescribeData(f);
    }

    protected virtual string LayerFieldName => "Layer ID";

    protected abstract void WriteData(Span<byte> p);
    protected abstract void ReadData(ReadOnlySpan<byte> p, int receivedLength);
    protected abstract void DescribeData(List<TCNetField> f);
}

/// <summary>Data type 2 – Metrics (122 bytes). Unicast when the cache changes, or on request.</summary>
public sealed class MetricsDataPacket : DataPacket
{
    public override DataType DataType => DataType.Metrics;
    public override string Name => "Data – Metrics";
    public override int Length => TCNetConstants.MetricsDataSize;

    public LayerState LayerState { get; set; }

    /// <summary>0 = slave, 1 = master.</summary>
    public byte SyncMaster { get; set; }

    /// <summary>Beat marker 1–4 (0 = unknown).</summary>
    public byte BeatMarker { get; set; }

    /// <summary>Total track length in ms.</summary>
    public uint TrackLength { get; set; }

    /// <summary>Play head position in ms.</summary>
    public uint CurrentPosition { get; set; }

    /// <summary>Play head speed, 32768 = 100 %.</summary>
    public uint Speed { get; set; }

    public uint BeatNumber { get; set; }

    /// <summary>BPM × 100 (e.g. 12800 = 128.00 BPM).</summary>
    public uint Bpm { get; set; }

    /// <summary>Pitch / speed bend, 32768 = 100 %.</summary>
    public ushort PitchBend { get; set; }

    public uint TrackId { get; set; }

    public double BpmValue
    {
        get => Bpm / 100.0;
        set => Bpm = (uint)Math.Round(value * 100);
    }

    /// <summary>Speed as a ratio (1.0 = 100 %).</summary>
    public double SpeedRatio => Speed / (double)TCNetConstants.SpeedUnity;

    /// <summary>Pitch bend as a ratio (1.0 = 100 %).</summary>
    public double PitchBendRatio => PitchBend / (double)TCNetConstants.SpeedUnity;

    public bool IsSyncMaster => SyncMaster == 1;

    protected override void WriteData(Span<byte> p)
    {
        p[27] = (byte)LayerState;
        p[29] = SyncMaster;
        p[31] = BeatMarker;
        Wire.U32(p, 32, TrackLength);
        Wire.U32(p, 36, CurrentPosition);
        Wire.U32(p, 40, Speed);
        Wire.U32(p, 57, BeatNumber);
        Wire.U32(p, 112, Bpm);
        Wire.U16(p, 116, PitchBend);
        Wire.U32(p, 118, TrackId);
    }

    protected override void ReadData(ReadOnlySpan<byte> p, int receivedLength)
    {
        LayerState = (LayerState)p[27];
        SyncMaster = p[29];
        BeatMarker = p[31];
        TrackLength = Wire.U32(p, 32);
        CurrentPosition = Wire.U32(p, 36);
        Speed = Wire.U32(p, 40);
        BeatNumber = Wire.U32(p, 57);
        Bpm = Wire.U32(p, 112);
        PitchBend = Wire.U16(p, 116);
        TrackId = Wire.U32(p, 118);
    }

    protected override void DescribeData(List<TCNetField> f)
    {
        f.Add(new(27, 1, "Layer State", ((byte)LayerState).ToString(), TCNetText.Describe(LayerState)));
        f.Add(new(29, 1, "Sync Master", SyncMaster.ToString(), SyncMaster == 1 ? "Master" : "Slave"));
        f.Add(new(31, 1, "Beat Marker", BeatMarker.ToString()));
        f.Add(new(32, 4, "Track Length", $"{TrackLength} ms", TCNetUnits.FormatMs(TrackLength)));
        f.Add(new(36, 4, "Current Position", $"{CurrentPosition} ms", TCNetUnits.FormatMs(CurrentPosition)));
        f.Add(new(40, 4, "Speed", Speed.ToString(), TCNetUnits.FormatPercent(SpeedRatio)));
        f.Add(new(57, 4, "Beat Number", BeatNumber.ToString()));
        f.Add(new(112, 4, "BPM", Bpm.ToString(), $"{BpmValue:0.00} BPM"));
        f.Add(new(116, 2, "Pitch Bend", PitchBend.ToString(), TCNetUnits.FormatPercent(PitchBendRatio)));
        f.Add(new(118, 4, "Track ID", TrackId.ToString()));
    }

    public override string Summary =>
        $"L{TCNetText.LayerName(LayerId)} {TCNetText.Short(LayerState)} {TCNetUnits.FormatMs(CurrentPosition)}/{TCNetUnits.FormatMs(TrackLength)} {BpmValue:0.00} BPM beat {BeatMarker}";
}

/// <summary>How metadata strings are encoded.</summary>
public enum MetadataEncoding
{
    /// <summary>UTF-16LE when the packet's protocol version is ≥ 3.5, otherwise UTF-8.</summary>
    Auto,
    Utf8,
    Utf16,
}

/// <summary>Data type 4 – Metadata (548 bytes). Artist and title are 256-byte fields.</summary>
public sealed class MetadataPacket : DataPacket
{
    public override DataType DataType => DataType.Metadata;
    public override string Name => "Data – Metadata";
    public override int Length => TCNetConstants.MetadataSize;

    public string TrackArtist { get; set; } = "";
    public string TrackTitle { get; set; } = "";
    public ushort TrackKey { get; set; }
    public uint TrackId { get; set; }

    /// <summary>String encoding. Auto follows the header protocol version (V3.5.0+ = UTF-16).</summary>
    public MetadataEncoding Encoding { get; set; } = MetadataEncoding.Auto;

    /// <summary>The encoding actually used for the current header version.</summary>
    public MetadataEncoding EffectiveEncoding =>
        Encoding != MetadataEncoding.Auto ? Encoding : UsesUtf16Text ? MetadataEncoding.Utf16 : MetadataEncoding.Utf8;

    protected override void WriteData(Span<byte> p)
    {
        if (EffectiveEncoding == MetadataEncoding.Utf16)
        {
            Wire.Utf16(p, 29, TCNetConstants.MetadataTextSize, TrackArtist);
            Wire.Utf16(p, 285, TCNetConstants.MetadataTextSize, TrackTitle);
        }
        else
        {
            Wire.Utf8(p, 29, TCNetConstants.MetadataTextSize, TrackArtist);
            Wire.Utf8(p, 285, TCNetConstants.MetadataTextSize, TrackTitle);
        }
        Wire.U16(p, 541, TrackKey);
        Wire.U32(p, 543, TrackId);
    }

    protected override void ReadData(ReadOnlySpan<byte> p, int receivedLength)
    {
        if (EffectiveEncoding == MetadataEncoding.Utf16)
        {
            TrackArtist = Wire.Utf16(p, 29, TCNetConstants.MetadataTextSize);
            TrackTitle = Wire.Utf16(p, 285, TCNetConstants.MetadataTextSize);
        }
        else
        {
            TrackArtist = Wire.Utf8(p, 29, TCNetConstants.MetadataTextSize);
            TrackTitle = Wire.Utf8(p, 285, TCNetConstants.MetadataTextSize);
        }
        TrackKey = Wire.U16(p, 541);
        TrackId = Wire.U32(p, 543);
    }

    protected override void DescribeData(List<TCNetField> f)
    {
        var enc = EffectiveEncoding == MetadataEncoding.Utf16 ? "UTF-16" : "UTF-8";
        f.Add(new(29, 256, "Track Artist", TrackArtist, enc));
        f.Add(new(285, 256, "Track Title", TrackTitle, enc));
        f.Add(new(541, 2, "Track Key", TrackKey.ToString()));
        f.Add(new(543, 4, "Track ID", TrackId.ToString()));
    }

    public override string Summary => $"L{TCNetText.LayerName(LayerId)} {TrackArtist} – {TrackTitle} (#{TrackId})";
}

/// <summary>A cue colour (byte 1 = red, 2 = green, 3 = blue).</summary>
public readonly record struct CueColor(byte Red, byte Green, byte Blue)
{
    public override string ToString() => $"#{Red:X2}{Green:X2}{Blue:X2}";
}

/// <summary>One hot/memory cue in a Cue Data packet.</summary>
public sealed class CuePoint
{
    public byte Type { get; set; }
    public uint InTime { get; set; }
    public uint OutTime { get; set; }
    public CueColor Color { get; set; }

    public bool IsEmpty => Type == 0 && InTime == 0 && OutTime == 0 && Color == default;
}

/// <summary>
/// Data type 12 – Cue Data. Loop IN (42), Loop OUT (46) and 18 cues of 22 bytes each:
/// type (+0), in time (+2), out time (+6), colour RGB (+11).
/// </summary>
/// <remarks>
/// The spec places cue 1 at byte 47, which overlaps Loop OUT (46–49); see <see cref="CueTableLayout"/>.
/// </remarks>
public sealed class CueDataPacket : DataPacket
{
    public const int CueCount = 18;
    public const int CueStride = 22;

    /// <summary>Layout used by new instances and by the parser. Defaults to the literal spec offsets.</summary>
    public static CueTableLayout DefaultLayout { get; set; } = CueTableLayout.Specification;

    public CueDataPacket()
    {
        for (int i = 0; i < Cues.Length; i++) Cues[i] = new CuePoint();
    }

    public override DataType DataType => DataType.CueData;
    public override string Name => "Data – Cue Data";

    public CueTableLayout Layout { get; set; } = DefaultLayout;

    /// <summary>Offset of cue 1's type byte for the current layout (47 or 50).</summary>
    public int CueTableOffset => Layout == CueTableLayout.AfterLoop ? 50 : 47;

    public override int Length => CueTableOffset + CueCount * CueStride;

    public uint LoopIn { get; set; }
    public uint LoopOut { get; set; }
    public CuePoint[] Cues { get; } = new CuePoint[CueCount];

    protected override void WriteData(Span<byte> p)
    {
        // Loop fields first, then cues: with the spec layout cue 1 wins the overlapping bytes 47–49.
        Wire.U32(p, 42, LoopIn);
        Wire.U32(p, 46, LoopOut);
        for (int i = 0; i < CueCount; i++)
        {
            int o = CueTableOffset + i * CueStride;
            var c = Cues[i];
            p[o] = c.Type;
            Wire.U32(p, o + 2, c.InTime);
            Wire.U32(p, o + 6, c.OutTime);
            p[o + 11] = c.Color.Red;
            p[o + 12] = c.Color.Green;
            p[o + 13] = c.Color.Blue;
        }
    }

    protected override void ReadData(ReadOnlySpan<byte> p, int receivedLength)
    {
        LoopIn = Wire.U32(p, 42);
        LoopOut = Wire.U32(p, 46);
        for (int i = 0; i < CueCount; i++)
        {
            int o = CueTableOffset + i * CueStride;
            var c = Cues[i];
            c.Type = p[o];
            c.InTime = Wire.U32(p, o + 2);
            c.OutTime = Wire.U32(p, o + 6);
            c.Color = new CueColor(p[o + 11], p[o + 12], p[o + 13]);
        }
    }

    protected override void DescribeData(List<TCNetField> f)
    {
        f.Add(new(42, 4, "Loop IN", $"{LoopIn} ms", TCNetUnits.FormatMs(LoopIn)));
        f.Add(new(46, 4, "Loop OUT", $"{LoopOut} ms", TCNetUnits.FormatMs(LoopOut)));
        for (int i = 0; i < CueCount; i++)
        {
            var c = Cues[i];
            if (c.IsEmpty) continue;
            int o = CueTableOffset + i * CueStride;
            f.Add(new(o, 1, $"CUE {i + 1} Type", c.Type.ToString()));
            f.Add(new(o + 2, 4, $"CUE {i + 1} IN", $"{c.InTime} ms", TCNetUnits.FormatMs(c.InTime)));
            f.Add(new(o + 6, 4, $"CUE {i + 1} OUT", $"{c.OutTime} ms", TCNetUnits.FormatMs(c.OutTime)));
            f.Add(new(o + 11, 3, $"CUE {i + 1} Color", c.Color.ToString()));
        }
    }

    public override string Summary => $"L{TCNetText.LayerName(LayerId)} loop {LoopIn}–{LoopOut} ms, {Cues.Count(c => !c.IsEmpty)} cues";
}

/// <summary>One of the six mixer channel strips (24 bytes from 125 + 24 × index).</summary>
public sealed class MixerChannel
{
    private readonly byte[] _raw;
    private readonly int _o;

    internal MixerChannel(byte[] raw, int number)
    {
        _raw = raw;
        Number = number;
        _o = 125 + 24 * (number - 1);
    }

    /// <summary>Channel number 1–6.</summary>
    public int Number { get; }
    internal int Offset => _o;

    public ChannelSource SourceSelect { get => (ChannelSource)_raw[_o]; set => _raw[_o] = (byte)value; }
    public byte AudioLevel { get => _raw[_o + 1]; set => _raw[_o + 1] = value; }
    public byte FaderLevel { get => _raw[_o + 2]; set => _raw[_o + 2] = value; }
    public byte TrimLevel { get => _raw[_o + 3]; set => _raw[_o + 3] = value; }
    public byte CompLevel { get => _raw[_o + 4]; set => _raw[_o + 4] = value; }
    public byte EqHi { get => _raw[_o + 5]; set => _raw[_o + 5] = value; }
    public byte EqHiMid { get => _raw[_o + 6]; set => _raw[_o + 6] = value; }
    public byte EqLowMid { get => _raw[_o + 7]; set => _raw[_o + 7] = value; }
    public byte EqLow { get => _raw[_o + 8]; set => _raw[_o + 8] = value; }
    public byte FilterColor { get => _raw[_o + 9]; set => _raw[_o + 9] = value; }
    public byte Send { get => _raw[_o + 10]; set => _raw[_o + 10] = value; }
    public bool CueA { get => _raw[_o + 11] != 0; set => _raw[_o + 11] = value ? (byte)1 : (byte)0; }
    public bool CueB { get => _raw[_o + 12] != 0; set => _raw[_o + 12] = value ? (byte)1 : (byte)0; }
    public CrossfaderAssign CrossfaderAssign { get => (CrossfaderAssign)_raw[_o + 13]; set => _raw[_o + 13] = (byte)value; }
}

/// <summary>Data type 150 – Mixer Data (270 bytes). Byte 25 is the mixer ID. Unicast to all slaves.</summary>
public sealed class MixerDataPacket : DataPacket
{
    private readonly byte[] _raw = new byte[TCNetConstants.MixerDataSize];

    public MixerDataPacket()
    {
        Channels = Enumerable.Range(1, 6).Select(n => new MixerChannel(_raw, n)).ToArray();
        SendReturn3Source = MixerChannelSelect.None;
        SendReturn3Type = SendReturnType.None;
        BeatFxChannel = MixerChannelSelect.None;
    }

    public override DataType DataType => DataType.Mixer;
    public override string Name => "Data – Mixer";
    public override int Length => TCNetConstants.MixerDataSize;
    protected override string LayerFieldName => "Mixer ID";

    /// <summary>Mixer ID (byte 25, standard 0). Same byte as <see cref="DataPacket.LayerId"/>.</summary>
    public byte MixerId { get => LayerId; set => LayerId = value; }

    public MixerType MixerType { get => (MixerType)_raw[26]; set => _raw[26] = (byte)value; }

    public string MixerName { get; set; } = "";

    public byte MicEqHi { get => _raw[59]; set => _raw[59] = value; }
    public byte MicEqLow { get => _raw[60]; set => _raw[60] = value; }
    public byte MasterAudioLevel { get => _raw[61]; set => _raw[61] = value; }
    public byte MasterFaderLevel { get => _raw[62]; set => _raw[62] = value; }
    public bool LinkCueA { get => _raw[67] != 0; set => _raw[67] = B(value); }
    public bool LinkCueB { get => _raw[68] != 0; set => _raw[68] = B(value); }
    public byte MasterFilter { get => _raw[69]; set => _raw[69] = value; }
    public bool MasterCueA { get => _raw[71] != 0; set => _raw[71] = B(value); }
    public bool MasterCueB { get => _raw[72] != 0; set => _raw[72] = B(value); }
    public bool MasterIsolatorOn { get => _raw[74] != 0; set => _raw[74] = B(value); }
    public byte MasterIsolatorHi { get => _raw[75]; set => _raw[75] = value; }
    public byte MasterIsolatorMid { get => _raw[76]; set => _raw[76] = value; }
    public byte MasterIsolatorLow { get => _raw[77]; set => _raw[77] = value; }
    public byte FilterHpf { get => _raw[79]; set => _raw[79] = value; }
    public byte FilterLpf { get => _raw[80]; set => _raw[80] = value; }
    public byte FilterResonance { get => _raw[81]; set => _raw[81] = value; }
    public byte SendFxEffect { get => _raw[84]; set => _raw[84] = value; }
    public bool SendFxExt1 { get => _raw[85] != 0; set => _raw[85] = B(value); }
    public bool SendFxExt2 { get => _raw[86] != 0; set => _raw[86] = B(value); }
    public byte SendFxMasterMix { get => _raw[87]; set => _raw[87] = value; }
    public byte SendFxSizeFeedback { get => _raw[88]; set => _raw[88] = value; }
    public byte SendFxTime { get => _raw[89]; set => _raw[89] = value; }
    public byte SendFxHpf { get => _raw[90]; set => _raw[90] = value; }
    public byte SendFxLevel { get => _raw[91]; set => _raw[91] = value; }
    public MixerChannelSelect SendReturn3Source { get => (MixerChannelSelect)_raw[92]; set => _raw[92] = (byte)value; }
    public SendReturnType SendReturn3Type { get => (SendReturnType)_raw[93]; set => _raw[93] = (byte)value; }
    public bool SendReturn3On { get => _raw[94] != 0; set => _raw[94] = B(value); }
    public byte SendReturn3Level { get => _raw[95]; set => _raw[95] = value; }
    public byte ChannelFaderCurve { get => _raw[97]; set => _raw[97] = value; }
    public byte CrossFaderCurve { get => _raw[98]; set => _raw[98] = value; }
    public byte CrossFader { get => _raw[99]; set => _raw[99] = value; }
    public bool BeatFxOn { get => _raw[100] != 0; set => _raw[100] = B(value); }
    public byte BeatFxLevelDepth { get => _raw[101]; set => _raw[101] = value; }
    public MixerChannelSelect BeatFxChannel { get => (MixerChannelSelect)_raw[102]; set => _raw[102] = (byte)value; }
    public byte BeatFxSelect { get => _raw[103]; set => _raw[103] = value; }
    public byte BeatFxFreqHi { get => _raw[104]; set => _raw[104] = value; }
    public byte BeatFxFreqMid { get => _raw[105]; set => _raw[105] = value; }
    public byte BeatFxFreqLow { get => _raw[106]; set => _raw[106] = value; }
    public byte HeadphonesPreEq { get => _raw[107]; set => _raw[107] = value; }
    public byte HeadphonesALevel { get => _raw[108]; set => _raw[108] = value; }
    public byte HeadphonesAMix { get => _raw[109]; set => _raw[109] = value; }
    public byte HeadphonesBLevel { get => _raw[110]; set => _raw[110] = value; }
    public byte HeadphonesBMix { get => _raw[111]; set => _raw[111] = value; }
    public byte BoothLevel { get => _raw[112]; set => _raw[112] = value; }
    public byte BoothEqHi { get => _raw[113]; set => _raw[113] = value; }
    public byte BoothEqLow { get => _raw[114]; set => _raw[114] = value; }

    /// <summary>Channel strips 1–6.</summary>
    public IReadOnlyList<MixerChannel> Channels { get; }

    private static byte B(bool v) => v ? (byte)1 : (byte)0;

    /// <summary>Raw byte access (offsets as in the spec table).</summary>
    public byte this[int offset]
    {
        get => _raw[offset];
        set { if (offset < 27) throw new ArgumentOutOfRangeException(nameof(offset), "Header bytes are set through properties."); _raw[offset] = value; }
    }

    protected override void WriteData(Span<byte> p)
    {
        _raw.AsSpan(26).CopyTo(p[26..]);
        Wire.Ascii(p, 29, 16, MixerName);
    }

    protected override void ReadData(ReadOnlySpan<byte> p, int receivedLength)
    {
        p[..TCNetConstants.MixerDataSize].CopyTo(_raw);
        MixerName = Wire.Ascii(p, 29, 16);
    }

    private static readonly (int Offset, string Name)[] MasterFields =
    [
        (59, "Mic EQ Hi"), (60, "Mic EQ Low"), (61, "Master Audio Level"), (62, "Master Fader Level"),
        (67, "Link Cue A"), (68, "Link Cue B"), (69, "Master Filter"), (71, "Master CUE A"), (72, "Master CUE B"),
        (74, "Master Isolator ON/OFF"), (75, "Master Isolator Hi"), (76, "Master Isolator Mid"), (77, "Master Isolator Low"),
        (79, "Filter HPF"), (80, "Filter LPF"), (81, "Filter Resonance"), (84, "Send FX Effect"), (85, "Send FX Ext 1"),
        (86, "Send FX Ext 2"), (87, "Send FX Master Mix"), (88, "Send FX Size Feedback"), (89, "Send FX Time"),
        (90, "Send FX HPF"), (91, "Send FX Level"), (92, "Send Return 3 Source Select"), (93, "Send Return 3 Type"),
        (94, "Send Return 3 ON/OFF"), (95, "Send Return 3 Level"), (97, "Channel Fader Curve"), (98, "Cross Fader Curve"),
        (99, "Cross Fader"), (100, "BeatFX ON/OFF"), (101, "BeatFX Level/Depth"), (102, "BeatFX Channel Select"),
        (103, "BeatFX Select"), (104, "BeatFX Freq Hi"), (105, "BeatFX Freq Mid"), (106, "BeatFX Freq Low"),
        (107, "Headphones Pre EQ"), (108, "Headphones A Level"), (109, "Headphones A Mix"), (110, "Headphones B Level"),
        (111, "Headphones B Mix"), (112, "Booth Level"), (113, "Booth EQ Hi"), (114, "Booth EQ Low"),
    ];

    private static readonly string[] ChannelFieldNames =
    [
        "Source Select", "Audio Level", "Fader Level", "Trim Level", "Comp Level", "EQ Hi Level", "EQ Hi Mid Level",
        "EQ Low Mid Level", "EQ Low Level", "Filter/Color", "Send", "CUE A", "CUE B", "Crossfader Assign",
    ];

    protected override void DescribeData(List<TCNetField> f)
    {
        f.Add(new(26, 1, "Mixer Type", _raw[26].ToString(), TCNetText.Describe(MixerType)));
        f.Add(new(29, 16, "Mixer Name", MixerName));
        foreach (var (o, n) in MasterFields)
        {
            string? meaning = o switch
            {
                92 => TCNetText.Describe(SendReturn3Source),
                93 => TCNetText.Describe(SendReturn3Type),
                102 => TCNetText.Describe(BeatFxChannel),
                _ => null,
            };
            f.Add(new(o, 1, n, _raw[o].ToString(), meaning));
        }
        foreach (var ch in Channels)
        {
            for (int i = 0; i < ChannelFieldNames.Length; i++)
            {
                string? meaning = i switch
                {
                    0 => TCNetText.Describe(ch.SourceSelect),
                    13 => TCNetText.Describe(ch.CrossfaderAssign),
                    _ => null,
                };
                f.Add(new(ch.Offset + i, 1, $"Channel {ch.Number} {ChannelFieldNames[i]}", _raw[ch.Offset + i].ToString(), meaning));
            }
        }
    }

    public override string Summary =>
        $"{MixerName} master {MasterFaderLevel} xf {CrossFader} faders " + string.Join(" ", Channels.Select(c => c.FaderLevel));
}

/// <summary>A type 200 / 204 packet with a data type this library does not model. The payload (bytes 26…) is kept raw.</summary>
public sealed class UnknownDataPacket : DataPacket
{
    private MessageType _messageType = MessageType.Data;
    private DataType _dataType;

    public UnknownDataPacket() { }

    public UnknownDataPacket(MessageType messageType, DataType dataType)
    {
        _messageType = messageType;
        _dataType = dataType;
    }

    public override MessageType MessageType => _messageType;
    public override DataType DataType => _dataType;
    public override string Name => $"Data – unknown type {(byte)_dataType}";
    public override int Length => 26 + Payload.Length;

    /// <summary>Bytes 26 to the end of the datagram.</summary>
    public byte[] Payload { get; set; } = [];

    protected override void WriteData(Span<byte> p) => Payload.CopyTo(p[26..]);

    protected override void ReadData(ReadOnlySpan<byte> p, int receivedLength)
    {
        _messageType = (MessageType)p[7];
        _dataType = (DataType)p[24];
        Payload = p.Slice(26, Math.Max(0, receivedLength - 26)).ToArray();
    }

    protected override void DescribeData(List<TCNetField> f) =>
        f.Add(new(26, Payload.Length, "Payload", Convert.ToHexString(Payload.AsSpan(0, Math.Min(32, Payload.Length))) + (Payload.Length > 32 ? "…" : "")));
}
