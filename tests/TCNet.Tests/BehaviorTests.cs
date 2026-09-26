using System.Net;
using TCNet.Networking;
using TCNet.Text;

namespace TCNet.Tests;

public class AssemblyTests
{
    [Fact]
    public void Split_And_Reassemble_OutOfOrder()
    {
        var data = Enumerable.Range(0, 10_000).Select(i => (byte)i).ToArray();
        var packets = ChunkedDataPacket.Split(data, 2, () => new BigWaveformPacket());
        Assert.Equal(3, packets.Count);
        Assert.All(packets, p => Assert.Equal(10_000u, p.DataSize));
        Assert.Equal(4800, packets[0].Payload.Length);
        Assert.Equal(400, packets[2].Payload.Length);

        var asm = new TCNetChunkAssembler();
        var src = new IPEndPoint(IPAddress.Loopback, 65023);
        Assert.Null(asm.Add(Reparse(packets[2]), src));
        Assert.Null(asm.Add(Reparse(packets[0]), src));
        var done = asm.Add(Reparse(packets[1]), src);
        Assert.NotNull(done);
        Assert.Equal(data, done!.Data);
        Assert.Equal(DataType.BigWaveform, done.DataType);
        Assert.Equal(0, asm.PendingCount);
    }

    [Fact]
    public void Reassemble_OneBasedNumbering()
    {
        var packets = ChunkedDataPacket.Split(new byte[5000], 1, () => new LowResArtworkPacket());
        foreach (var p in packets) p.PacketNumber++;
        var asm = new TCNetChunkAssembler();
        Assert.Null(asm.Add(packets[0]));
        var done = asm.Add(packets[1]);
        Assert.NotNull(done);
        Assert.Equal(new byte[5000], done!.Data);
    }

    [Fact]
    public void BeatGrid_OffsetFormula()
    {
        // OFFSET = (beat × 8) − (packet × 2400): beat 300 is the first entry of packet 1.
        var grid = new BeatGrid(Enumerable.Range(0, 400).Select(b => new BeatGridEntry((ushort)b, b % 4 == 0 ? BeatType.DownBeat : BeatType.UpBeat, (uint)(b * 500))).ToList());
        var packets = ChunkedDataPacket.Split(grid.Encode(), 1, () => new BeatGridDataPacket());
        Assert.Equal(2, packets.Count);
        var first = packets[1].Entries[0];
        Assert.Equal(300, first.BeatNumber);
        Assert.Equal(150_000u, first.TimestampMs);

        var bytes = packets[0].ToArray();
        Assert.Equal(2442, bytes.Length);
        Assert.Equal(20, bytes[42 + 2]);  // beat 0 downbeat at 44
        Assert.Equal(500u, BitConverter.ToUInt32(bytes, 46 + 8));
    }

    [Fact]
    public void ApplicationData_Reassembles()
    {
        var asm = new TCNetChunkAssembler();
        var a = new ApplicationDataPacket { ApplicationCode = 0x0ABC, DataSize = 4, TotalPackets = 2, PacketNumber = 0, Payload = [1, 2] };
        var b = new ApplicationDataPacket { ApplicationCode = 0x0ABC, DataSize = 4, TotalPackets = 2, PacketNumber = 1, Payload = [3, 4] };
        Assert.Null(asm.Add(a));
        var done = asm.Add(b);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, done!.Data);
        Assert.Equal(0x0ABC, done.ApplicationCode);
    }

    private static T Reparse<T>(T p) where T : TCNetPacket => (T)TCNetPacket.Parse(p.ToArray());
}

public class TimingTests
{
    [Fact]
    public void TimeSync_SpecFormula()
    {
        // Initiator sent at 100000, remote replied with its timer 700000, response arrives at 100400.
        var s = TimeSync.Compute(remoteTimestamp: 700_000, echoedLocalTimestamp: 100_000, localNow: 100_400);
        Assert.Equal(200u, s.DelayMicros);
        Assert.Equal(700_200u, s.RemoteTimeAtReceive);
        Assert.Equal(TCNetClock.Difference(700_200, 100_400), s.OffsetMicros);
    }

    [Fact]
    public void TimeSync_HandlesWrap()
    {
        var s = TimeSync.Compute(remoteTimestamp: 999_900, echoedLocalTimestamp: 999_800, localNow: 200);
        Assert.Equal(200u, s.DelayMicros);
        Assert.Equal(100u, s.RemoteTimeAtReceive);
        Assert.Equal(-100, s.OffsetMicros);
    }

    [Fact]
    public void Clock_Arithmetic()
    {
        Assert.Equal(300u, TCNetClock.Forward(999_900, 200));
        Assert.Equal(50u, TCNetClock.Add(999_950, 100));
        Assert.Equal(999_950u, TCNetClock.Add(50, -100));
        Assert.Equal(-300, TCNetClock.Difference(999_900, 200));
        var c = new TCNetClock();
        Assert.InRange(c.Timestamp, 0u, 999_999u);
        Assert.InRange(c.Uptime, (ushort)0, (ushort)43_199);
    }

    [Theory]
    [InlineData(SmpteMode.Fps25, 3_723_040u, "01:02:03:01")]
    [InlineData(SmpteMode.Fps30, 1_000u, "00:00:01:00")]
    [InlineData(SmpteMode.Fps24, 500u, "00:00:00:12")]
    public void Timecode_FromMilliseconds(SmpteMode mode, uint ms, string expected) =>
        Assert.Equal(expected, Timecode.FromMilliseconds(ms, mode).ToString());

    [Fact]
    public void Timecode_ParseAndBack()
    {
        var tc = Timecode.Parse("00:01:00:00");
        Assert.Equal(60_000u, tc.ToMilliseconds(SmpteMode.Fps25));
    }

    [Fact]
    public void Election_PicksHighestUptimeAuto()
    {
        var now = DateTime.UtcNow;
        TCNetRemoteNode Node(ushort id, NodeType type, ushort uptime) =>
            new(IPAddress.Loopback, id) { NodeType = type, Uptime = uptime, LastOptIn = now, NodeName = $"N{id}" };

        Assert.True(TCNetMasterElection.ShouldPromote(100, 5, "ME", [], now));
        Assert.True(TCNetMasterElection.ShouldPromote(100, 5, "ME", [Node(1, NodeType.Auto, 50)], now));
        Assert.False(TCNetMasterElection.ShouldPromote(100, 5, "ME", [Node(1, NodeType.Auto, 200)], now));
        Assert.False(TCNetMasterElection.ShouldPromote(100, 5, "ME", [Node(1, NodeType.Master, 10)], now));
        // Within tolerance: lower Node ID wins, and both sides agree.
        Assert.False(TCNetMasterElection.ShouldPromote(100, 5, "ME", [Node(1, NodeType.Auto, 101)], now));
        Assert.True(TCNetMasterElection.ShouldPromote(101, 1, "N1", [Node(5, NodeType.Auto, 100)], now));
        Assert.Equal(1, TCNetMasterElection.ChooseMaster([Node(5, NodeType.Auto, 100), Node(1, NodeType.Auto, 101)], now)!.NodeId);
        // Two elected masters: only the lower-ranked one steps back.
        Assert.True(TCNetMasterElection.ShouldDemote(100, 5, "ME", Node(1, NodeType.Master, 101), now));
        Assert.False(TCNetMasterElection.ShouldDemote(101, 1, "N1", Node(5, NodeType.Master, 100), now));
    }

    [Theory]
    [InlineData(SmpteMode.Fps24)]
    [InlineData(SmpteMode.Fps25)]
    [InlineData(SmpteMode.Fps29_97)]
    [InlineData(SmpteMode.Fps30)]
    public void Timecode_RoundTripsEveryFrame(SmpteMode mode)
    {
        int fps = Timecode.FramesPerSecond(mode);
        for (int s = 0; s < 120; s++)
            for (int f = 0; f < fps; f++)
            {
                var tc = new Timecode(0, (byte)(s / 60), (byte)(s % 60), (byte)f);
                Assert.Equal(tc, Timecode.FromMilliseconds(tc.ToMilliseconds(mode), mode));
            }
    }

    [Fact]
    public void Playback_AnswersRequests()
    {
        var pb = new TCNetPlayback();
        pb[1].Load(42, "Artist", "Title", 180_000, 120);
        pb[1].State = LayerState.Playing;
        var md = Assert.IsType<MetadataPacket>(Assert.Single(pb.HandleRequest(new RequestPacket { DataType = DataType.Metadata, Layer = 1 })!));
        Assert.Equal("Title", md.TrackTitle);
        var grid = pb.HandleRequest(new RequestPacket { DataType = DataType.BeatGrid, Layer = 1 })!;
        Assert.Equal(360 * 8, grid.Cast<BeatGridDataPacket>().Sum(p => p.Payload.Length));
        Assert.Null(pb.HandleRequest(new RequestPacket { DataType = DataType.Metrics, Layer = 0 }));
        Assert.Equal(NotificationCode.Ok, pb.Apply(new ControlPacket(ControlCommand.SetLayerState(1, LayerState.Stopped))));
        Assert.Equal(LayerState.Stopped, pb[1].State);
    }
}

public class TextTests
{
    [Fact]
    public void Describe_KnownAndUnknown()
    {
        Assert.Equal("PLAYING", TCNetText.Describe(LayerState.Playing));
        Assert.Equal("Unknown value 99", TCNetText.Describe((LayerState)99));
        Assert.Equal("SUPPORTS TCNCM + DND", TCNetText.DescribeFlags(NodeOptions.SupportsControlMessages | NodeOptions.DoNotDisturb));
        Assert.Equal("None", TCNetText.DescribeFlags(NodeOptions.None));
        Assert.Equal("ShowKontrol", TCNetText.ApplicationVendor(0x0AAA)!.Split(" / ")[1]);
    }

    [Fact]
    public void EveryEnumValue_HasADescription()
    {
        foreach (var set in TCNetText.OptionSets)
        {
            var values = Enum.GetValues(set.EnumType).Cast<object>().Select(o => Convert.ToInt64(o)).Where(v => !(set.IsFlags && v == 0));
            foreach (var v in values) Assert.NotNull(set.Find(v));
        }
    }

    [Fact]
    public void Catalog_CoversEveryPacketAndRendersMarkdown()
    {
        Assert.Equal(20, TCNetOptionCatalog.Packets.Count);
        foreach (var p in TCNetOptionCatalog.Packets) Assert.True(p.Layout.Count >= 9, p.Name);
        var md = TCNetOptionCatalog.ToMarkdown();
        Assert.Contains("| 254 | Time |", md);
        Assert.Contains("Registered application codes", md);
        Assert.NotEmpty(TCNetOptionCatalog.Search("beat marker"));
    }

    [Fact]
    public void Units_Format()
    {
        Assert.Equal("1:01.500", TCNetUnits.FormatMs(61_500));
        Assert.Equal("1:00:00.000", TCNetUnits.FormatMs(3_600_000));
        Assert.Equal("100.00 %", TCNetUnits.FormatPercent(1.0));
    }

    [Fact]
    public void HexDump_And_ParseHex()
    {
        var bytes = Wire.ParseHex("0x54 43 4E");
        Assert.Equal("TCN"u8.ToArray(), bytes);
        Assert.Contains("TCN", Wire.HexDump(bytes));
        Assert.Equal("TCN"u8.ToArray(), Wire.ParseHex("54:43-4e"));
        Assert.Throws<FormatException>(() => Wire.ParseHex("zz"));
        Assert.Throws<FormatException>(() => Wire.ParseHex("543"));
    }
}

public class NodeTests
{
    [Fact]
    public async Task TwoNodes_RequestAndControl_OverLoopback()
    {
        var masterSettings = new TCNetNodeSettings
        {
            NodeId = 101, NodeName = "MASTER", NodeType = NodeType.Master, LocalAddress = IPAddress.Loopback,
            BroadcastAddress = IPAddress.Loopback, ListenOnBroadcastPorts = false, AutoTimeSync = false,
        };
        var slaveSettings = new TCNetNodeSettings
        {
            NodeId = 102, NodeName = "SLAVE", LocalAddress = IPAddress.Loopback, BroadcastAddress = IPAddress.Loopback,
            ListenOnBroadcastPorts = false, AutoTimeSync = false,
        };
        var playback = new TCNetPlayback();
        playback[2].Load(7, "A", "Song", 200_000, 125);

        await using var master = new TCNetNode(masterSettings);
        await using var slave = new TCNetNode(slaveSettings);
        master.RequestHandler = (rq, _) => playback.HandleRequest(rq);
        master.ControlHandler = (cp, _) => playback.Apply(cp);
        master.Start();
        slave.Start();

        // Opt-IN directly to each other's listener ports (no broadcast ports in this test).
        var masterEp = new IPEndPoint(IPAddress.Loopback, master.ListenerPort);
        var slaveEp = new IPEndPoint(IPAddress.Loopback, slave.ListenerPort);
        await slave.SendAsync(slave.CreateOptIn(), masterEp);
        await master.SendAsync(master.CreateOptIn(), slaveEp);

        var node = await WaitFor(() => slave.Nodes.FirstOrDefault(n => n.NodeName == "MASTER"));
        Assert.Equal(NodeType.Master, node.NodeType);
        Assert.Equal(master.ListenerPort, node.ListenerPort);

        var md = await slave.RequestMetadataAsync(node, 2);
        Assert.Equal("Song", md?.TrackTitle);

        var grid = await slave.RequestBeatGridAsync(node, 2);
        Assert.NotNull(grid);
        Assert.True(grid!.Entries.Count > 400);

        var empty = await slave.RequestAsync(node, DataType.Metrics, 0);
        Assert.Equal(NotificationCode.RequestDataEmpty, empty.Notification?.Code);

        var ack = await slave.SendControlAsync(node, ControlCommand.SetLayerState(2, LayerState.Playing));
        Assert.Equal(NotificationCode.Ok, ack?.Code);
        Assert.Equal(LayerState.Playing, playback[2].State);

        var sync = await slave.TimeSyncAsync(node, rounds: 2);
        Assert.InRange(sync.DelayMicros, 0u, 500_000u);
    }

    private static async Task<T> WaitFor<T>(Func<T?> probe) where T : class
    {
        for (int i = 0; i < 100; i++)
        {
            if (probe() is { } v) return v;
            await Task.Delay(20);
        }
        throw new TimeoutException();
    }
}
