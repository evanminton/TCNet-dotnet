using System.Buffers.Binary;
using System.Text;

namespace TCNet.Tests;

/// <summary>Byte offsets and sizes checked against the tables of TCNet V3.5.1B.</summary>
public class PacketTests
{
    private static T RoundTrip<T>(T packet) where T : TCNetPacket
    {
        var bytes = packet.ToArray();
        Assert.Equal(packet.Length, bytes.Length);
        var parsed = TCNetPacket.Parse(bytes);
        return Assert.IsType<T>(parsed);
    }

    private static ushort U16(byte[] b, int o) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o));
    private static uint U32(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));

    [Fact]
    public void ManagementHeader_Layout()
    {
        var p = new OptInPacket
        {
            NodeId = 0x1234, NodeName = "ABCDEFGHIJ", Sequence = 7, NodeType = NodeType.Master,
            NodeOptions = NodeOptions.SupportsControlMessages | NodeOptions.DoNotDisturb, Timestamp = 999_999,
        };
        var b = p.ToArray();
        Assert.Equal(0x34, b[0]);
        Assert.Equal(0x12, b[1]);
        Assert.Equal(3, b[2]);
        Assert.Equal(5, b[3]);
        Assert.Equal("TCN"u8.ToArray(), b[4..7]);
        Assert.Equal(2, b[7]);
        Assert.Equal("ABCDEFGH", Encoding.ASCII.GetString(b, 8, 8));
        Assert.Equal(7, b[16]);
        Assert.Equal(2, b[17]);
        Assert.Equal(10, U16(b, 18));
        Assert.Equal(999_999u, U32(b, 20));
    }

    [Fact]
    public void OptIn_Is68Bytes_WithFieldsAtSpecOffsets()
    {
        var p = new OptInPacket
        {
            NodeCount = 3, ListenerPort = 65100, Uptime = 43199, VendorName = "Vendor", ApplicationName = "App",
            ApplicationMajorVersion = 1, ApplicationMinorVersion = 2, ApplicationBugVersion = 3,
        };
        var b = p.ToArray();
        Assert.Equal(68, b.Length);
        Assert.Equal(3, U16(b, 24));
        Assert.Equal(65100, U16(b, 26));
        Assert.Equal(43199, U16(b, 28));
        Assert.Equal("Vendor", Encoding.ASCII.GetString(b, 32, 6));
        Assert.Equal("App", Encoding.ASCII.GetString(b, 48, 3));
        Assert.Equal(new byte[] { 1, 2, 3 }, b[64..67]);

        var r = RoundTrip(p);
        Assert.Equal("Vendor", r.VendorName);
        Assert.Equal("1.2.3", r.ApplicationVersion);
        Assert.Equal(65100, r.ListenerPort);
    }

    [Fact]
    public void OptOut_Is28Bytes()
    {
        var b = new OptOutPacket { NodeCount = 2, ListenerPort = 65023 }.ToArray();
        Assert.Equal(28, b.Length);
        Assert.Equal(3, b[7]);
        Assert.Equal(65023, U16(b, 26));
    }

    [Fact]
    public void Status_Is300Bytes_LayersAtSpecOffsets()
    {
        var p = new StatusPacket { NodeCount = 1, ListenerPort = 65024, SmpteMode = SmpteMode.Fps25, AutoMasterMode = AutoMasterMode.LinkMaster };
        for (int i = 0; i < 8; i++)
        {
            p.Layers[i].Source = (byte)(i + 1);
            p.Layers[i].State = LayerState.Playing;
            p.Layers[i].TrackId = 1000u + (uint)i;
            p.Layers[i].Name = $"Deck {i}";
        }
        var b = p.ToArray();
        Assert.Equal(300, b.Length);
        Assert.Equal(5, b[7]);
        Assert.Equal(1, b[34]);
        Assert.Equal(8, b[41]);
        Assert.Equal(3, b[42]);
        Assert.Equal(3, b[49]);
        Assert.Equal(1000u, U32(b, 50));
        Assert.Equal(1007u, U32(b, 78));
        Assert.Equal(25, b[83]);
        Assert.Equal(2, b[84]);
        Assert.Equal("Deck 0", Encoding.ASCII.GetString(b, 172, 6));
        Assert.Equal("Deck 7", Encoding.ASCII.GetString(b, 284, 6));

        var r = RoundTrip(p);
        Assert.Equal("Deck 3", r[TCNetLayer.Layer4].Name);
        Assert.Equal(1005u, r[TCNetLayer.LayerB].TrackId);
    }

    [Fact]
    public void TimeSync_Is32Bytes()
    {
        var b = new TimeSyncPacket { Step = SyncStep.Response, ListenerPort = 65030, RemoteTimestamp = 123456 }.ToArray();
        Assert.Equal(32, b.Length);
        Assert.Equal(10, b[7]);
        Assert.Equal(1, b[24]);
        Assert.Equal(65030, U16(b, 26));
        Assert.Equal(123456u, U32(b, 28));
    }

    [Fact]
    public void ErrorNotification_Is30Bytes()
    {
        var p = new ErrorNotificationPacket { DataType = 16, LayerId = 2, Code = NotificationCode.RequestDataEmpty, RequestMessageType = 20 };
        var b = p.ToArray();
        Assert.Equal(30, b.Length);
        Assert.Equal(13, b[7]);
        Assert.Equal(16, b[24]);
        Assert.Equal(2, b[25]);
        Assert.Equal(14, U16(b, 26));
        Assert.Equal(20, U16(b, 28));
        Assert.Equal(NotificationCode.RequestDataEmpty, RoundTrip(p).Code);
    }

    [Fact]
    public void Request_Is26Bytes()
    {
        var b = new RequestPacket { DataType = DataType.SmallWaveform, Layer = 3 }.ToArray();
        Assert.Equal(26, b.Length);
        Assert.Equal(20, b[7]);
        Assert.Equal(16, b[24]);
        Assert.Equal(3, b[25]);
    }

    [Fact]
    public void Control_HasDataSizeAndPathAt42()
    {
        var p = new ControlPacket(ControlCommand.SetLayerState(2, LayerState.Playing), ControlCommand.Resync(2));
        Assert.Equal("layer/2/state=3; layer/2/resync;", p.Text);
        var b = p.ToArray();
        Assert.Equal(42 + p.Text.Length, b.Length);
        Assert.Equal(101, b[7]);
        Assert.Equal((uint)p.Text.Length, U32(b, 26));
        Assert.Equal(p.Text, Encoding.ASCII.GetString(b, 42, p.Text.Length));

        var r = RoundTrip(p);
        Assert.Equal(2, r.Commands.Count);
        Assert.Equal(2, r.Commands[0].LayerNumber);
        Assert.Equal("3", r.Commands[0].Value);
        Assert.Null(r.Commands[1].Value);
    }

    [Fact]
    public void ControlCommand_ParsesSpecExamples()
    {
        var c = ControlCommand.ParseAll("layer/7/source=5;");
        Assert.Single(c);
        Assert.Equal("layer/7/source", c[0].Path);
        Assert.Equal("5", c[0].Value);
        Assert.Equal(7, c[0].LayerNumber);
    }

    [Fact]
    public void TextData_RoundTrips()
    {
        var r = RoundTrip(new TextDataPacket("Hello TCNet"));
        Assert.Equal("Hello TCNet", r.Text);
        Assert.Equal(MessageType.TextData, r.MessageType);
    }

    [Fact]
    public void Keyboard_Is44Bytes()
    {
        var p = KeyboardDataPacket.ForKey('A');
        var b = p.ToArray();
        Assert.Equal(44, b.Length);
        Assert.Equal(132, b[7]);
        Assert.Equal(2u, U32(b, 26));
        Assert.Equal((byte)'A', b[42]);
        Assert.Equal(0, b[43]);
    }

    [Fact]
    public void Metrics_Is122Bytes_WithFieldsAtSpecOffsets()
    {
        var p = new MetricsDataPacket
        {
            LayerId = 2, LayerState = LayerState.Looping, SyncMaster = 1, BeatMarker = 3, TrackLength = 300_000,
            CurrentPosition = 12_345, Speed = 32768, BeatNumber = 42, BpmValue = 128.5, PitchBend = 32768, TrackId = 0xDEADBEEF,
        };
        var b = p.ToArray();
        Assert.Equal(122, b.Length);
        Assert.Equal(200, b[7]);
        Assert.Equal(2, b[24]);
        Assert.Equal(2, b[25]);
        Assert.Equal(4, b[27]);
        Assert.Equal(1, b[29]);
        Assert.Equal(3, b[31]);
        Assert.Equal(300_000u, U32(b, 32));
        Assert.Equal(12_345u, U32(b, 36));
        Assert.Equal(32768u, U32(b, 40));
        Assert.Equal(42u, U32(b, 57));
        Assert.Equal(12850u, U32(b, 112));
        Assert.Equal(32768, U16(b, 116));
        Assert.Equal(0xDEADBEEFu, U32(b, 118));

        var r = RoundTrip(p);
        Assert.Equal(128.5, r.BpmValue);
        Assert.Equal(1.0, r.SpeedRatio);
        Assert.True(r.IsSyncMaster);
    }

    [Fact]
    public void Metadata_Utf16_ForVersion35()
    {
        var p = new MetadataPacket { LayerId = 1, TrackArtist = "Artïst ✓", TrackTitle = "Title", TrackKey = 7, TrackId = 99 };
        var b = p.ToArray();
        Assert.Equal(548, b.Length);
        Assert.Equal(4, b[24]);
        Assert.Equal("Artïst ✓", Encoding.Unicode.GetString(b, 29, 16));
        Assert.Equal("Title", Encoding.Unicode.GetString(b, 285, 10));
        Assert.Equal(7, U16(b, 541));
        Assert.Equal(99u, U32(b, 543));
        var r = RoundTrip(p);
        Assert.Equal("Artïst ✓", r.TrackArtist);
        Assert.Equal(MetadataEncoding.Utf16, r.EffectiveEncoding);
    }

    [Fact]
    public void Metadata_Utf8_ForOlderVersions()
    {
        var p = new MetadataPacket { ProtocolMajor = 3, ProtocolMinor = 4, TrackArtist = "Café", TrackTitle = "T" };
        var b = p.ToArray();
        Assert.Equal("Café", Encoding.UTF8.GetString(b, 29, 5));
        var r = RoundTrip(p);
        Assert.Equal(MetadataEncoding.Utf8, r.EffectiveEncoding);
        Assert.Equal("Café", r.TrackArtist);
    }

    [Fact]
    public void Metadata_TruncatesUtf16At256Bytes()
    {
        var p = new MetadataPacket { TrackTitle = new string('x', 300) };
        var r = RoundTrip(p);
        Assert.Equal(128, r.TrackTitle.Length);
    }

    [Fact]
    public void CueData_SpecLayout_Cue1At47_Stride22()
    {
        var p = new CueDataPacket { LayerId = 1, Layout = CueTableLayout.Specification, LoopIn = 1000 };
        p.Cues[0].Type = 1; p.Cues[0].InTime = 5000; p.Cues[0].OutTime = 6000; p.Cues[0].Color = new CueColor(255, 128, 0);
        p.Cues[17].Type = 2; p.Cues[17].InTime = 9000; p.Cues[17].Color = new CueColor(1, 2, 3);
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

        var r = TCNetPacket.Parse(b) as CueDataPacket;
        Assert.NotNull(r);
        Assert.Equal(5000u, r!.Cues[0].InTime);
        Assert.Equal(new CueColor(1, 2, 3), r.Cues[17].Color);
    }

    [Fact]
    public void CueData_AfterLoopLayout_KeepsLoopOut()
    {
        var p = new CueDataPacket { Layout = CueTableLayout.AfterLoop, LoopIn = 1, LoopOut = 0x01020304 };
        p.Cues[0].Type = 9;
        var b = p.ToArray();
        Assert.Equal(446, b.Length);
        Assert.Equal(0x01020304u, U32(b, 46));
        Assert.Equal(9, b[50]);
    }

    [Fact]
    public void SmallWaveform_Is2442Bytes_LevelThenColor()
    {
        var p = new SmallWaveformPacket { LayerId = 4 };
        var bars = Enumerable.Range(0, 1200).Select(i => new WaveformBar((byte)i, (byte)(255 - (i & 0xFF)))).ToArray();
        p.SetBars(bars);
        var b = p.ToArray();
        Assert.Equal(2442, b.Length);
        Assert.Equal(16, b[24]);
        Assert.Equal(2400u, U32(b, 26));
        Assert.Equal(0u, U32(b, 38));
        Assert.Equal(0, b[42]);
        Assert.Equal(255, b[43]);
        Assert.Equal(1, b[44]);
        var r = RoundTrip(p);
        Assert.Equal(1200, r.Waveform.Bars.Count);
        Assert.Equal(bars[700], r.Waveform.Bars[700]);
    }

    [Fact]
    public void Mixer_Is270Bytes_WithChannelStride24()
    {
        var p = new MixerDataPacket { MixerId = 0, MixerType = MixerType.Extended, MixerName = "DJM", MasterAudioLevel = 200, CrossFader = 128 };
        p.Channels[0].FaderLevel = 11;
        p.Channels[5].CrossfaderAssign = CrossfaderAssign.B;
        p.Channels[1].SourceSelect = ChannelSource.Phono;
        var b = p.ToArray();
        Assert.Equal(270, b.Length);
        Assert.Equal(150, b[24]);
        Assert.Equal(2, b[26]);
        Assert.Equal("DJM", Encoding.ASCII.GetString(b, 29, 3));
        Assert.Equal(200, b[61]);
        Assert.Equal(128, b[99]);
        Assert.Equal(255, b[92]);
        Assert.Equal(11, b[127]);
        Assert.Equal(4, b[149]);
        Assert.Equal(2, b[258]);

        var r = RoundTrip(p);
        Assert.Equal("DJM", r.MixerName);
        Assert.Equal(ChannelSource.Phono, r.Channels[1].SourceSelect);
        Assert.Equal(CrossfaderAssign.B, r.Channels[5].CrossfaderAssign);
    }

    [Fact]
    public void Time_Is162Bytes_WithSequentialLayerOffsets()
    {
        var p = new TimePacket { SmpteMode = SmpteMode.Fps25 };
        for (int i = 0; i < 8; i++)
        {
            var l = p.Layers[i];
            l.CurrentTimeMs = 1000u * (uint)(i + 1);
            l.TotalTimeMs = 100_000u + (uint)i;
            l.BeatMarker = (byte)(i % 4 + 1);
            l.State = LayerState.Playing;
            l.SmpteMode = SmpteMode.Fps30;
            l.TimecodeState = TimecodeState.Running;
            l.Timecode = new Timecode(1, 2, 3, (byte)i);
            l.OnAir = (byte)(i * 10);
        }
        var b = p.ToArray();
        Assert.Equal(162, b.Length);
        Assert.Equal(254, b[7]);
        Assert.Equal(1000u, U32(b, 24));
        Assert.Equal(7000u, U32(b, 48));
        Assert.Equal(8000u, U32(b, 52));   // LC time (printed as 48)
        Assert.Equal(100_000u, U32(b, 56));
        Assert.Equal(100_007u, U32(b, 84));
        Assert.Equal(1, b[88]);
        Assert.Equal(4, b[95]);            // LC beat marker (printed as 94)
        Assert.Equal(3, b[96]);
        Assert.Equal(3, b[103]);
        Assert.Equal(25, b[105]);
        Assert.Equal(new byte[] { 30, 1, 1, 2, 3, 0 }, b[106..112]);
        Assert.Equal(new byte[] { 30, 1, 1, 2, 3, 7 }, b[148..154]);
        Assert.Equal(0, b[154]);
        Assert.Equal(70, b[161]);

        var r = RoundTrip(p);
        Assert.Equal(new Timecode(1, 2, 3, 5), r[TCNetLayer.LayerB].Timecode);
        Assert.True(r.Layers[7].IsOnAir);
    }

    [Fact]
    public void ApplicationData_SignatureAndIdentifiers()
    {
        var p = new ApplicationDataPacket { ApplicationCode = 0x0AAA, Payload = [1, 2, 3] };
        var b = p.ToArray();
        Assert.Equal(45, b.Length);
        Assert.Equal(30, b[7]);
        Assert.Equal(0x0A, b[24]);
        Assert.Equal(0xAA, b[25]);
        Assert.Equal(3u, U32(b, 26));
        Assert.Equal(178260640u, U32(b, 38));
        var r = RoundTrip(p);
        Assert.Equal(new byte[] { 1, 2, 3 }, r.Payload);

        p.SetMessageType(MessageType.ApplicationSpecificData);
        Assert.Equal(213, p.ToArray()[7]);
        Assert.Equal(MessageType.ApplicationSpecificData, RoundTrip(p).MessageType);
    }

    [Fact]
    public void Artwork_IsType204()
    {
        var p = new LowResArtworkPacket { LayerId = 1, Payload = [0xFF, 0xD8, 0xFF] };
        var b = p.ToArray();
        Assert.Equal(204, b[7]);
        Assert.Equal(128, b[24]);
        Assert.Equal(4800u, U32(b, 38));
        Assert.IsType<LowResArtworkPacket>(TCNetPacket.Parse(b));
    }

    [Fact]
    public void Parser_RejectsNonTCNet()
    {
        Assert.False(TCNetPacket.TryParse(new byte[10], out _, out var e1));
        Assert.Contains("short", e1);
        var junk = new byte[30];
        Assert.False(TCNetPacket.TryParse(junk, out _, out var e2));
        Assert.Contains("TCN", e2);
    }

    [Fact]
    public void Parser_PadsShortPackets()
    {
        var full = new OptInPacket { VendorName = "V" }.ToArray();
        var shortened = full[..32];  // pre-V3-2 Opt-IN without vendor fields
        var p = Assert.IsType<OptInPacket>(TCNetPacket.Parse(shortened));
        Assert.True(p.WasPadded);
        Assert.Equal("", p.VendorName);
    }

    [Fact]
    public void Parser_KeepsUnknownTypes()
    {
        var b = new byte[30];
        "TCN"u8.CopyTo(b.AsSpan(4));
        b[7] = 77;
        var p = Assert.IsType<UnknownPacket>(TCNetPacket.Parse(b));
        Assert.Equal(6, p.Body.Length);

        b[7] = 200; b[24] = 99;
        Assert.IsType<UnknownDataPacket>(TCNetPacket.Parse(b));
    }

    [Theory]
    [InlineData(MessageType.OptIn, (byte)0)]
    [InlineData(MessageType.OptOut, (byte)0)]
    [InlineData(MessageType.Status, (byte)0)]
    [InlineData(MessageType.TimeSync, (byte)0)]
    [InlineData(MessageType.ErrorNotification, (byte)0)]
    [InlineData(MessageType.Request, (byte)0)]
    [InlineData(MessageType.ApplicationData, (byte)0)]
    [InlineData(MessageType.Control, (byte)0)]
    [InlineData(MessageType.TextData, (byte)0)]
    [InlineData(MessageType.KeyboardData, (byte)0)]
    [InlineData(MessageType.Data, (byte)DataType.Metrics)]
    [InlineData(MessageType.Data, (byte)DataType.Metadata)]
    [InlineData(MessageType.Data, (byte)DataType.BeatGrid)]
    [InlineData(MessageType.Data, (byte)DataType.CueData)]
    [InlineData(MessageType.Data, (byte)DataType.SmallWaveform)]
    [InlineData(MessageType.Data, (byte)DataType.BigWaveform)]
    [InlineData(MessageType.Data, (byte)DataType.Mixer)]
    [InlineData(MessageType.DataFile, (byte)DataType.LowResArtwork)]
    [InlineData(MessageType.ApplicationSpecificData, (byte)0)]
    [InlineData(MessageType.Time, (byte)0)]
    public void EveryPacket_RoundTripsAndDescribes(MessageType type, byte dataType)
    {
        var p = TCNetPacketParser.Create(type, (DataType)dataType);
        p.NodeName = "TEST";
        var bytes = p.ToArray();
        var r = TCNetPacket.Parse(bytes);
        Assert.Equal(p.GetType(), r.GetType());
        Assert.Equal(bytes, r.ToArray());
        var fields = r.Describe();
        Assert.True(fields.Count >= 9);
        Assert.False(string.IsNullOrWhiteSpace(r.ToDisplayString()));
    }
}
