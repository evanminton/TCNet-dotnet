using System.Buffers.Binary;
using System.Text;

namespace TCNet.Tests;

/// <summary>Byte offsets and sizes from the tables of the V3.5.1B PDF.</summary>
public class LayoutTests
{
    private static ushort U16(byte[] b, int o) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o));
    private static uint U32(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));

    private static T Roundtrip<T>(T p) where T : TCNetPacket
    {
        var b = p.ToArray();
        Assert.Equal(p.Length, b.Length);
        return Assert.IsType<T>(TCNetPacket.Parse(b));
    }

    [Fact]
    public void Header()
    {
        var b = new OptInPacket
        {
            NodeId = 0xBEEF, NodeName = "ABCDEFGHIJK", Sequence = 9, NodeType = NodeType.Repeater,
            NodeOptions = NodeOptions.SupportsControl | NodeOptions.SupportsApplicationData, Timestamp = 999_999,
        }.ToArray();
        Assert.Equal(0xBEEF, U16(b, 0));
        Assert.Equal(new byte[] { 3, 5 }, b[2..4]);
        Assert.Equal("TCN", Encoding.ASCII.GetString(b, 4, 3));
        Assert.Equal(2, b[7]);
        Assert.Equal("ABCDEFGH", Encoding.ASCII.GetString(b, 8, 8));
        Assert.Equal(9, b[16]);
        Assert.Equal(8, b[17]);
        Assert.Equal(6, U16(b, 18));
        Assert.Equal(999_999u, U32(b, 20));
    }

    [Fact]
    public void OptIn_68()
    {
        var p = new OptInPacket { NodeCount = 4, ListenerPort = 65100, Uptime = 43_199, VendorName = "Vendor", DeviceName = "Device", DeviceMajor = 1, DeviceMinor = 2, DeviceBug = 3 };
        var b = p.ToArray();
        Assert.Equal(68, b.Length);
        Assert.Equal(4, U16(b, 24));
        Assert.Equal(65100, U16(b, 26));
        Assert.Equal(43199, U16(b, 28));
        Assert.Equal("Vendor", Encoding.ASCII.GetString(b, 32, 6));
        Assert.Equal("Device", Encoding.ASCII.GetString(b, 48, 6));
        Assert.Equal(new byte[] { 1, 2, 3 }, b[64..67]);
        var r = Roundtrip(p);
        Assert.Equal("1.2.3", r.DeviceVersion);
        Assert.Equal("Vendor", r.VendorName);
    }

    [Fact]
    public void OptOut_28()
    {
        var b = new OptOutPacket { NodeCount = 2, ListenerPort = 65030 }.ToArray();
        Assert.Equal(28, b.Length);
        Assert.Equal(3, b[7]);
        Assert.Equal(65030, U16(b, 26));
    }

    [Fact]
    public void Status_300()
    {
        var p = new StatusPacket { NodeCount = 1, ListenerPort = 65024, SmpteMode = SmpteMode.Fps25, AutoMasterMode = AutoMasterMode.HtpMaster };
        for (int i = 0; i < 8; i++)
        {
            p.Layers[i].Source = (byte)(10 + i);
            p.Layers[i].State = LayerState.Paused;
            p.Layers[i].TrackId = 500u + (uint)i;
            p.Layers[i].Name = $"Name{i}";
        }
        p.AppSpecific[0] = 0xAB;
        var b = p.ToArray();
        Assert.Equal(300, b.Length);
        Assert.Equal(10, b[34]);
        Assert.Equal(17, b[41]);
        Assert.Equal(5, b[42]);
        Assert.Equal(5, b[49]);
        Assert.Equal(500u, U32(b, 50));
        Assert.Equal(507u, U32(b, 78));
        Assert.Equal(25, b[83]);
        Assert.Equal(1, b[84]);
        Assert.Equal(0xAB, b[100]);
        Assert.Equal("Name0", Encoding.ASCII.GetString(b, 172, 5));
        Assert.Equal("Name7", Encoding.ASCII.GetString(b, 284, 5));
        var r = Roundtrip(p);
        Assert.Equal("Name5", r[Layer.B].Name);
        Assert.Equal(0xAB, r.AppSpecific[0]);
    }

    [Fact]
    public void TimeSync_32()
    {
        var b = new TimeSyncPacket { Step = Step.Response, ListenerPort = 65040, RemoteTimestamp = 123_456 }.ToArray();
        Assert.Equal(32, b.Length);
        Assert.Equal(10, b[7]);
        Assert.Equal(1, b[24]);
        Assert.Equal(65040, U16(b, 26));
        Assert.Equal(123_456u, U32(b, 28));
    }

    [Fact]
    public void ErrorNotification_30()
    {
        var p = new ErrorNotificationPacket { DataType = 16, LayerId = 3, Code = NotificationCode.RequestNotPossible, RequestType = 20 };
        var b = p.ToArray();
        Assert.Equal(30, b.Length);
        Assert.Equal(13, b[7]);
        Assert.Equal(16, b[24]);
        Assert.Equal(3, b[25]);
        Assert.Equal(13, U16(b, 26));
        Assert.Equal(20, U16(b, 28));
    }

    [Fact]
    public void Request_26()
    {
        var b = new RequestPacket { DataType = DataType.BeatGrid, Layer = 2 }.ToArray();
        Assert.Equal(26, b.Length);
        Assert.Equal(20, b[7]);
        Assert.Equal(8, b[24]);
        Assert.Equal(2, b[25]);
    }

    [Fact]
    public void Control_SpecExamples()
    {
        var p = new ControlPacket(ControlCommand.SetState(2, LayerState.Playing), ControlCommand.Resync(2));
        Assert.Equal("layer/2/state=3; layer/2/resync;", p.Text);
        var b = p.ToArray();
        Assert.Equal(42 + p.Text.Length, b.Length);
        Assert.Equal(101, b[7]);
        Assert.Equal((uint)p.Text.Length, U32(b, 26));
        Assert.Equal(p.Text, Encoding.ASCII.GetString(b, 42, p.Text.Length));
        var r = Roundtrip(p);
        Assert.Equal(2, r.Commands.Count);
        Assert.Equal("state", r.Commands[0].Leaf);
        Assert.Equal(2, r.Commands[1].LayerNumber);

        var ex = ControlCommand.Parse("layer/7/source=5;");
        Assert.Equal("layer/7/source", ex[0].Path);
        Assert.Equal("5", ex[0].Value);
        Assert.Equal("layer/1/state=6;", ControlCommand.SetState(1, LayerState.Stopped).ToString());
    }

    [Fact]
    public void Text_RawBytesSurvive()
    {
        var p = new TextDataPacket { Payload = [0x41, 0xE9, 0xFF, 0x42] };
        var r = Roundtrip(p);
        Assert.Equal(p.Payload, r.Payload);
        Assert.Equal("AéÿB", r.Text);
        Assert.Equal("héllo", Roundtrip(new TextDataPacket("héllo")).Text);
    }

    [Fact]
    public void Keyboard_44()
    {
        var b = KeyboardDataPacket.For('Z').ToArray();
        Assert.Equal(44, b.Length);
        Assert.Equal(132, b[7]);
        Assert.Equal(2u, U32(b, 26));
        Assert.Equal((byte)'Z', b[42]);
    }

    [Fact]
    public void Metrics_122()
    {
        var p = new MetricsPacket
        {
            LayerId = 3, State = LayerState.Playing, SyncMaster = 1, BeatMarker = 2, TrackLengthMs = 400_000, PositionMs = 1_234,
            Speed = 32768, BeatNumber = 77, Bpm = 124.5, PitchBend = 32000, TrackId = 0xCAFEBABE,
        };
        var b = p.ToArray();
        Assert.Equal(122, b.Length);
        Assert.Equal(200, b[7]);
        Assert.Equal(2, b[24]);
        Assert.Equal(3, b[25]);
        Assert.Equal(3, b[27]);
        Assert.Equal(1, b[29]);
        Assert.Equal(2, b[31]);
        Assert.Equal(400_000u, U32(b, 32));
        Assert.Equal(1_234u, U32(b, 36));
        Assert.Equal(32768u, U32(b, 40));
        Assert.Equal(77u, U32(b, 57));
        Assert.Equal(12450u, U32(b, 112));
        Assert.Equal(32000, U16(b, 116));
        Assert.Equal(0xCAFEBABEu, U32(b, 118));
        Assert.Equal(124.5, Roundtrip(p).Bpm);
    }

    [Fact]
    public void Metadata_548_Utf16()
    {
        var p = new MetadataPacket { LayerId = 1, Artist = "Björk ✓", Title = "Jóga", Key = 9, TrackId = 42 };
        var b = p.ToArray();
        Assert.Equal(548, b.Length);
        Assert.Equal(4, b[24]);
        Assert.Equal("Björk ✓", Encoding.Unicode.GetString(b, 29, 14));
        Assert.Equal("Jóga", Encoding.Unicode.GetString(b, 285, 8));
        Assert.Equal(9, U16(b, 541));
        Assert.Equal(42u, U32(b, 543));
        Assert.Equal("Björk ✓", Roundtrip(p).Artist);
    }

    [Fact]
    public void Metadata_Utf8_BeforeV35()
    {
        var p = new MetadataPacket { VersionMinor = 4, Artist = "Café" };
        Assert.Equal("Café", Encoding.UTF8.GetString(p.ToArray(), 29, 5));
        var r = Roundtrip(p);
        Assert.Equal(TextEncoding.Utf8, r.EffectiveEncoding);
        Assert.Equal("Café", r.Artist);
    }

    [Fact]
    public void Metadata_TruncatesAt256Bytes()
    {
        Assert.Equal(128, Roundtrip(new MetadataPacket { Title = new string('x', 500) }).Title.Length);
        Assert.Equal(256, Roundtrip(new MetadataPacket { VersionMinor = 1, Title = new string('y', 500) }).Title.Length);
    }

    [Fact]
    public void CueData_PrintedOffsets()
    {
        var p = new CueDataPacket { LayerId = 2, LoopInMs = 1000 };
        p.Cues[0].Type = 1; p.Cues[0].InMs = 5000; p.Cues[0].OutMs = 6000; p.Cues[0].Color = new CueColor(255, 128, 0);
        p.Cues[17].Type = 2; p.Cues[17].InMs = 9000; p.Cues[17].Color = new CueColor(1, 2, 3);
        var b = p.ToArray();
        Assert.Equal(443, b.Length);
        Assert.Equal(12, b[24]);
        Assert.Equal(1000u, U32(b, 42));
        Assert.Equal(1, b[47]);
        Assert.Equal(5000u, U32(b, 49));
        Assert.Equal(6000u, U32(b, 53));
        Assert.Equal(new byte[] { 255, 128, 0 }, b[58..61]);
        Assert.Equal(2, b[421]);
        Assert.Equal(9000u, U32(b, 423));
        Assert.Equal(new byte[] { 1, 2, 3 }, b[432..435]);
        var r = Roundtrip(p);
        Assert.Equal(5000u, r.Cues[0].InMs);
        Assert.True(r.LoopOutOverlapped);
    }

    [Theory]
    [InlineData(0u, 0u)]
    [InlineData(0u, 1500u)]
    [InlineData(12_000u, 20_000u)]
    [InlineData(1u, 0x01020304u)]
    public void CueData_LoopSurvivesEmptyCue1(uint loopIn, uint loopOut)
    {
        var p = new CueDataPacket { LoopInMs = loopIn, LoopOutMs = loopOut };
        p.Cues[3].Type = 1;
        p.Cues[3].InMs = 777;
        var r = Roundtrip(p);
        Assert.Equal(loopIn, r.LoopInMs);
        Assert.Equal(loopOut, r.LoopOutMs);
        Assert.True(r.Cues[0].IsEmpty);
        Assert.Equal(777u, r.Cues[3].InMs);
    }

    [Fact]
    public void CueData_HotCueNearZero_IsCue()
    {
        var p = new CueDataPacket();
        p.Cues[0].Type = 3;
        p.Cues[0].InMs = 5;
        var r = Roundtrip(p);
        Assert.Equal(3, r.Cues[0].Type);
        Assert.Equal(5u, r.Cues[0].InMs);
        Assert.Equal(0u, r.LoopOutMs);
    }

    [Fact]
    public void CueData_AfterLoopLayout()
    {
        var p = new CueDataPacket { Layout = CueLayout.AfterLoop, LoopOutMs = 0x0A0B0C0D };
        p.Cues[0].Type = 7;
        var b = p.ToArray();
        Assert.Equal(446, b.Length);
        Assert.Equal(0x0A0B0C0Du, U32(b, 46));
        Assert.Equal(7, b[50]);
    }

    [Fact]
    public void SmallWaveform_2442()
    {
        var p = new SmallWaveformPacket { LayerId = 4 };
        p.SetBars(Enumerable.Range(0, 1200).Select(i => new WaveformBar((byte)i, (byte)~i)).ToArray());
        var b = p.ToArray();
        Assert.Equal(2442, b.Length);
        Assert.Equal(16, b[24]);
        Assert.Equal(2400u, U32(b, 26));
        Assert.Equal(0u, U32(b, 38));
        Assert.Equal(new byte[] { 0, 255, 1, 254 }, b[42..46]);
        var r = Roundtrip(p);
        Assert.Equal(1200, r.Waveform.Bars.Count);
        Assert.Equal(new WaveformBar(200, 55), r.Waveform.Bars[200]);
    }

    [Fact]
    public void Mixer_270()
    {
        var p = new MixerDataPacket { MixerType = MixerType.Extended, MixerName = "DJM-V10", MasterFaderLevel = 250, CrossFader = 127, BeatFxOn = true };
        p.Channels[0].FaderLevel = 11;
        p.Channels[1].Source = ChannelSource.Phono;
        p.Channels[5].CrossfaderAssign = CrossfaderAssign.B;
        var b = p.ToArray();
        Assert.Equal(270, b.Length);
        Assert.Equal(150, b[24]);
        Assert.Equal(2, b[26]);
        Assert.Equal("DJM-V10", Encoding.ASCII.GetString(b, 29, 7));
        Assert.Equal(250, b[62]);
        Assert.Equal(255, b[92]);
        Assert.Equal(127, b[99]);
        Assert.Equal(1, b[100]);
        Assert.Equal(255, b[102]);
        Assert.Equal(11, b[127]);
        Assert.Equal(4, b[149]);
        Assert.Equal(2, b[258]);
        var r = Roundtrip(p);
        Assert.Equal("DJM-V10", r.MixerName);
        Assert.Equal(ChannelSource.Phono, r.Channels[1].Source);
        Assert.Throws<ArgumentOutOfRangeException>(() => p[30] = 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => p[7] = 1);
        p[199] = 99;
        Assert.Equal(99, p.Channels[3].FaderLevel);
    }

    [Fact]
    public void Time_162()
    {
        var p = new TimePacket { SmpteMode = SmpteMode.Fps25 };
        foreach (var l in p.Layers)
        {
            l.TimeMs = 1000u * (uint)(l.Index + 1);
            l.TotalMs = 100_000u + (uint)l.Index;
            l.BeatMarker = (byte)(l.Index % 4 + 1);
            l.State = LayerState.Playing;
            l.SmpteMode = SmpteMode.Fps30;
            l.TimecodeState = TimecodeState.Running;
            l.Timecode = new Timecode(1, 2, 3, (byte)l.Index);
            l.OnAir = (byte)(l.Index * 10);
        }
        var b = p.ToArray();
        Assert.Equal(162, b.Length);
        Assert.Equal(254, b[7]);
        Assert.Equal(1000u, U32(b, 24));
        Assert.Equal(7000u, U32(b, 48));
        Assert.Equal(8000u, U32(b, 52));
        Assert.Equal(100_000u, U32(b, 56));
        Assert.Equal(100_007u, U32(b, 84));
        Assert.Equal(1, b[88]);
        Assert.Equal(4, b[95]);
        Assert.Equal(3, b[96]);
        Assert.Equal(3, b[103]);
        Assert.Equal(25, b[105]);
        Assert.Equal(new byte[] { 30, 1, 1, 2, 3, 0 }, b[106..112]);
        Assert.Equal(new byte[] { 30, 1, 1, 2, 3, 7 }, b[148..154]);
        Assert.Equal(70, b[161]);
        var r = Roundtrip(p);
        Assert.Equal(new Timecode(1, 2, 3, 5), r[Layer.B].Timecode);
    }

    [Fact]
    public void ApplicationData_30_And_213()
    {
        var p = new ApplicationDataPacket { ApplicationCode = 0x0AAA, Payload = [1, 2, 3] };
        var b = p.ToArray();
        Assert.Equal(45, b.Length);
        Assert.Equal(30, b[7]);
        Assert.Equal(new byte[] { 0x0A, 0xAA }, b[24..26]);
        Assert.Equal(3u, U32(b, 26));
        Assert.Equal(178_260_640u, U32(b, 38));
        p.UseType(MessageType.ApplicationSpecificData);
        var r = Roundtrip(p);
        Assert.Equal(MessageType.ApplicationSpecificData, r.MessageType);
        Assert.Equal(new byte[] { 1, 2, 3 }, r.Payload);
    }

    [Fact]
    public void Artwork_204()
    {
        var b = new ArtworkPacket { LayerId = 1, Payload = [0xFF, 0xD8, 0xFF] }.ToArray();
        Assert.Equal(204, b[7]);
        Assert.Equal(128, b[24]);
        Assert.Equal(4800u, U32(b, 38));
        Assert.IsType<ArtworkPacket>(TCNetPacket.Parse(b));
    }

    [Fact]
    public void Parser_Rejects_Pads_And_KeepsUnknown()
    {
        Assert.False(TCNetPacket.TryParse(new byte[5], out _, out var e1));
        Assert.Contains("short", e1);
        Assert.False(TCNetPacket.TryParse(new byte[40], out _, out var e2));
        Assert.Contains("TCN", e2);

        var shortOptIn = new OptInPacket { VendorName = "X" }.ToArray()[..30];
        var p = Assert.IsType<OptInPacket>(TCNetPacket.Parse(shortOptIn));
        Assert.True(p.WasPadded);
        Assert.Equal("", p.VendorName);

        TCNetParser.Strict = true;
        try { Assert.False(TCNetPacket.TryParse(shortOptIn, out _, out _)); }
        finally { TCNetParser.Strict = false; }

        var raw = new byte[30];
        "TCN"u8.CopyTo(raw.AsSpan(4));
        raw[7] = 99;
        Assert.Equal(6, Assert.IsType<UnknownPacket>(TCNetPacket.Parse(raw)).Body.Length);
        raw[7] = 200;
        raw[24] = 77;
        Assert.IsType<UnknownDataPacket>(TCNetPacket.Parse(raw));
    }

    public static TheoryData<MessageType, byte> AllTypes => new()
    {
        { MessageType.OptIn, 0 }, { MessageType.OptOut, 0 }, { MessageType.Status, 0 }, { MessageType.TimeSync, 0 },
        { MessageType.ErrorNotification, 0 }, { MessageType.Request, 0 }, { MessageType.ApplicationData, 0 },
        { MessageType.Control, 0 }, { MessageType.TextData, 0 }, { MessageType.KeyboardData, 0 },
        { MessageType.Data, 2 }, { MessageType.Data, 4 }, { MessageType.Data, 8 }, { MessageType.Data, 12 },
        { MessageType.Data, 16 }, { MessageType.Data, 32 }, { MessageType.Data, 150 }, { MessageType.DataFile, 128 },
        { MessageType.ApplicationSpecificData, 0 }, { MessageType.Time, 0 },
    };

    [Theory]
    [MemberData(nameof(AllTypes))]
    public void EveryType_RoundTripsAndDescribes(MessageType type, byte dataType)
    {
        var p = TCNetParser.Create(type, (DataType)dataType);
        p.NodeName = "T";
        var bytes = p.ToArray();
        var r = TCNetPacket.Parse(bytes);
        Assert.Equal(p.GetType(), r.GetType());
        Assert.Equal(bytes, r.ToArray());
        Assert.True(r.Describe().Count >= 9);
        Assert.Contains(p.Name, r.ToDisplayString());
    }
}
