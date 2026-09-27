using System.Net;
using TCNet.Networking;
using TCNet.Text;

namespace TCNet.Tests;

public class AssemblyTests
{
    private static readonly IPEndPoint Src = new(IPAddress.Loopback, 65023);

    private static T Reparse<T>(T p) where T : TCNetPacket => (T)TCNetPacket.Parse(p.ToArray());

    [Fact]
    public void SplitAndReassemble_AnyOrder()
    {
        var data = Enumerable.Range(0, 10_000).Select(i => (byte)i).ToArray();
        var parts = ChunkedPacket.Split(data, 2, () => new BigWaveformPacket());
        Assert.Equal(3, parts.Count);
        Assert.Equal(new[] { 4800, 4800, 400 }, parts.Select(p => p.Payload.Length));
        var asm = new ChunkAssembler();
        Assert.Null(asm.Add(Reparse(parts[2]), Src));
        Assert.Null(asm.Add(Reparse(parts[0]), Src));
        var done = asm.Add(Reparse(parts[1]), Src);
        Assert.Equal(data, done!.Data);
        Assert.Equal(DataType.BigWaveform, done.DataType);
        Assert.Equal(0, asm.Pending);
    }

    [Fact]
    public void OneBasedNumbering()
    {
        var parts = ChunkedPacket.Split(new byte[6000], 1, () => new ArtworkPacket());
        foreach (var p in parts) p.PacketNumber++;
        var asm = new ChunkAssembler();
        Assert.Null(asm.Add(parts[1]));
        Assert.NotNull(asm.Add(parts[0]));
    }

    [Fact]
    public void Gap_DoesNotComplete_And_OutOfRange_IsIgnored()
    {
        var asm = new ChunkAssembler();
        BigWaveformPacket Part(uint n) => new() { TotalPackets = 3, PacketNumber = n, TotalSize = 3, Payload = [(byte)n] };
        Assert.Null(asm.Add(Part(0)));
        Assert.Null(asm.Add(Part(2)));
        Assert.Null(asm.Add(Part(9)));
        Assert.Null(asm.Add(Part(3)));
        Assert.Equal(new byte[] { 0, 1, 2 }, asm.Add(Part(1))!.Data);
    }

    [Fact]
    public void RepeatedNumberWithNewContent_StartsOver()
    {
        var asm = new ChunkAssembler();
        Assert.Null(asm.Add(new BigWaveformPacket { TotalPackets = 2, PacketNumber = 0, Payload = [1] }));
        Assert.Null(asm.Add(new BigWaveformPacket { TotalPackets = 2, PacketNumber = 0, Payload = [7] }));
        Assert.Equal(new byte[] { 7, 8 }, asm.Add(new BigWaveformPacket { TotalPackets = 2, PacketNumber = 1, Payload = [8] })!.Data);
    }

    [Fact]
    public void Limits_BoundMemory()
    {
        var asm = new ChunkAssembler { MaxPendingTransfers = 2, MaxTransferBytes = 10, MaxTotalPackets = 4 };
        Assert.Null(asm.Add(new BigWaveformPacket { TotalPackets = 100, Payload = [1] }));
        Assert.Equal(0, asm.Pending);
        for (byte l = 1; l <= 5; l++) asm.Add(new BigWaveformPacket { LayerId = l, TotalPackets = 2, Payload = [1] });
        Assert.Equal(2, asm.Pending);

        // A chunk larger than the transfer limit is rejected without opening (or evicting) a transfer.
        Assert.Null(asm.Add(new BigWaveformPacket { LayerId = 8, TotalPackets = 2, Payload = new byte[11] }));
        Assert.Equal(2, asm.Pending);
        Assert.Null(asm.Add(new BigWaveformPacket { LayerId = 8, TotalPackets = 2, PacketNumber = 1, Payload = [2] }));
        Assert.Null(asm.Add(new BigWaveformPacket { LayerId = 8, TotalPackets = 2, PacketNumber = 0, Payload = new byte[11] }));

        // Chunks that together exceed the limit drop the transfer; the last chunk then can't complete it.
        var big = new ChunkAssembler { MaxTransferBytes = 10 };
        BigWaveformPacket Part(uint n) => new() { TotalPackets = 3, PacketNumber = n, Payload = new byte[6] };
        Assert.Null(big.Add(Part(0)));
        Assert.Equal(1, big.Pending);
        Assert.Null(big.Add(Part(1)));
        Assert.Equal(0, big.Pending);
        Assert.Equal(0, big.BufferedBytes);
        Assert.Null(big.Add(Part(2)));

        // Within the limits a transfer still completes.
        Assert.Null(asm.Add(new BigWaveformPacket { LayerId = 9, TotalPackets = 2, PacketNumber = 0, Payload = [1] }));
        Assert.Equal(new byte[] { 1, 2 }, asm.Add(new BigWaveformPacket { LayerId = 9, TotalPackets = 2, PacketNumber = 1, Payload = [2] })!.Data);
    }

    [Fact]
    public void BeatGrid_OffsetFormula()
    {
        var grid = new BeatGrid(Enumerable.Range(0, 400).Select(b => new Beat((ushort)b, b % 4 == 0 ? BeatType.DownBeat : BeatType.UpBeat, (uint)b * 500)).ToList());
        var parts = ChunkedPacket.Split(grid.Encode(), 1, () => new BeatGridPacket());
        Assert.Equal(2, parts.Count);
        Assert.Equal(new Beat(300, BeatType.DownBeat, 150_000), parts[1].Beats[0]);
        var bytes = parts[0].ToArray();
        Assert.Equal(2442, bytes.Length);
        Assert.Equal(20, bytes[44]);
        Assert.Equal(500u, BitConverter.ToUInt32(bytes, 54));
        Assert.Equal(new Beat(3, BeatType.UpBeat, 1500), grid.At(1700));
    }

    [Fact]
    public void ApplicationData_Reassembles()
    {
        var asm = new ChunkAssembler();
        Assert.Null(asm.Add(new ApplicationDataPacket { ApplicationCode = 0x0ABC, TotalSize = 4, TotalPackets = 2, PacketNumber = 0, Payload = [1, 2] }));
        var d = asm.Add(new ApplicationDataPacket { ApplicationCode = 0x0ABC, TotalSize = 4, TotalPackets = 2, PacketNumber = 1, Payload = [3, 4] });
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, d!.Data);
        Assert.Equal(0x0ABC, d.ApplicationCode);
    }
}

public class TimingTests
{
    [Fact]
    public void SpecFormula()
    {
        var r = TimeSync.Compute(remoteTimestamp: 700_000, echoedLocal: 100_000, localNow: 100_400);
        Assert.Equal(200u, r.DelayMicros);
        Assert.Equal(700_200u, r.RemoteNow);
        Assert.Equal(TCNetClock.Difference(700_200, 100_400), r.OffsetMicros);
    }

    [Fact]
    public void Wraps()
    {
        var r = TimeSync.Compute(999_900, 999_800, 200);
        Assert.Equal(200u, r.DelayMicros);
        Assert.Equal(100u, r.RemoteNow);
        Assert.Equal(-100, r.OffsetMicros);
        Assert.Equal(300u, TCNetClock.Forward(999_900, 200));
        Assert.Equal(999_950u, TCNetClock.Add(50, -100));
        Assert.Equal(-300, TCNetClock.Difference(999_900, 200));
    }

    [Fact]
    public void Average_AcrossWrap()
    {
        var avg = TimeSync.Average([new(100, 0, 499_990), new(300, 0, -499_990)]);
        Assert.Equal(200u, avg.DelayMicros);
        Assert.True(Math.Abs(avg.OffsetMicros) > 499_000);
    }

    [Theory]
    [InlineData(SmpteMode.Fps25, 3_723_040u, "01:02:03:01")]
    [InlineData(SmpteMode.Fps24, 500u, "00:00:00:12")]
    [InlineData(SmpteMode.Fps30, 1_000u, "00:00:01:00")]
    public void Timecode_FromMs(SmpteMode mode, uint ms, string text) => Assert.Equal(text, Timecode.FromMilliseconds(ms, mode).ToString());

    [Fact]
    public void Timecode_Parse() => Assert.Equal(60_000u, Timecode.Parse("00:01:00:00").ToMilliseconds(SmpteMode.Fps25));

    [Fact]
    public void Election()
    {
        var older = new MasterElection.Candidate(9, 500, 0);
        var younger = new MasterElection.Candidate(1, 100, 0);
        Assert.True(MasterElection.Beats(older, younger));
        var tieLow = new MasterElection.Candidate(3, 501, 0);
        Assert.True(MasterElection.Beats(tieLow, older));
        Assert.Equal((ushort)3, MasterElection.Winner([older, younger, tieLow])!.Value.NodeId);
    }

    [Fact]
    public void Playback_AnswersAndApplies()
    {
        var pb = Playback.Demo();
        var md = Assert.IsType<MetadataPacket>(Assert.Single(pb.Answer(new RequestPacket { DataType = DataType.Metadata, Layer = 1 })!));
        Assert.Equal("First Light", md.Title);
        var grid = pb.Answer(new RequestPacket { DataType = DataType.BeatGrid, Layer = 2 })!;
        Assert.True(grid.Count >= 2);
        Assert.Null(pb.Answer(new RequestPacket { DataType = DataType.Metrics, Layer = 0 }));
        Assert.Null(pb.Answer(new RequestPacket { DataType = DataType.Mixer, Layer = 1 }));
        Assert.Equal(NotificationCode.Ok, pb.Apply(new ControlPacket(ControlCommand.SetState(2, LayerState.Playing))));
        Assert.Equal(LayerState.Playing, pb[2].State);
        Assert.Equal(NotificationCode.RequestNotPossible, pb.Apply(new ControlPacket { Text = "mixer/fader=1;" }));
        var t = pb.BuildTime();
        Assert.Equal(LayerState.Playing, t.Layers[0].State);
        Assert.Equal(255, t.Layers[0].OnAir);
    }
}

public class TextTests
{
    [Fact]
    public void Describe()
    {
        Assert.Equal("PLAYING", TCNetText.Describe(LayerState.Playing));
        Assert.Equal("Unknown (99)", TCNetText.Describe((LayerState)99));
        Assert.Equal("SUPPORTS TCNCM + DND", TCNetText.DescribeFlags(NodeOptions.SupportsControl | NodeOptions.DoNotDisturb));
        Assert.Equal("None", TCNetText.DescribeFlags(NodeOptions.None));
        Assert.Contains("unknown 0x10", TCNetText.DescribeFlags((NodeOptions)0x12));
        Assert.Equal("Resolume Software", TCNetText.ApplicationVendor(0x0ABC));
        Assert.Equal("M", TCNetText.LayerName(7));
    }

    [Fact]
    public void EveryEnumValue_IsDescribed()
    {
        foreach (var set in TCNetText.OptionSets)
            foreach (var v in Enum.GetValues(set.EnumType).Cast<object>().Select(o => Convert.ToInt64(o)))
                if (!(set.IsFlags && v == 0)) Assert.NotNull(set.Find(v));
    }

    [Fact]
    public void Catalog()
    {
        Assert.Equal(20, TCNetCatalog.Packets.Count);
        Assert.All(TCNetCatalog.Packets, p => Assert.True(p.Layout.Count >= 9, p.Name));
        var md = TCNetCatalog.ToMarkdown();
        Assert.Contains("| 254 | Time |", md);
        Assert.Contains("## Spec notes", md);
        Assert.NotEmpty(TCNetCatalog.Search("beat marker"));
    }

    [Fact]
    public void Units_And_Hex()
    {
        Assert.Equal("1:01.500", TCNetUnits.Ms(61_500));
        Assert.Equal("1:00:00.000", TCNetUnits.Ms(3_600_000));
        Assert.Equal("-0:01.0", TCNetUnits.Remaining(9_000, 10_000));
        Assert.Equal("TCN"u8.ToArray(), TCNet.Wire.ParseHex("0x54 43 4e"));
        Assert.Contains("TCN", TCNet.Wire.HexDump("TCN"u8));
    }
}

public class NodeTests
{
    private static NodeSettings Loop(ushort id, string name, NodeType type) => new()
    {
        NodeId = id, NodeName = name, NodeType = type, LocalAddress = IPAddress.Loopback, BroadcastAddress = IPAddress.Loopback,
        ListenOnBroadcastPorts = false, AutoTimeSync = false,
    };

    private static async Task<T> Until<T>(Func<T?> probe) where T : class
    {
        for (int i = 0; i < 150; i++)
        {
            if (probe() is { } v) return v;
            await Task.Delay(20);
        }
        throw new TimeoutException();
    }

    private static async Task<(TCNetNode Master, TCNetNode Slave, RemoteNode MasterSeen, Playback Pb)> Pair()
    {
        var pb = Playback.Demo();
        var master = new TCNetNode(Loop(201, "MASTER", NodeType.Master));
        var slave = new TCNetNode(Loop(202, "SLAVE", NodeType.Slave));
        master.RequestHandler = (rq, _) => pb.Answer(rq);
        master.ControlHandler = (cp, _) => pb.Apply(cp);
        await master.StartAsync();
        await slave.StartAsync();
        await slave.SendAsync(slave.CreateOptIn(), new IPEndPoint(IPAddress.Loopback, master.ListenerPort));
        await master.SendAsync(master.CreateOptIn(), new IPEndPoint(IPAddress.Loopback, slave.ListenerPort));
        var seen = await Until(() => slave.Nodes.FirstOrDefault(n => n.NodeName == "MASTER"));
        await Until(() => master.Nodes.FirstOrDefault(n => n.NodeName == "SLAVE"));
        return (master, slave, seen, pb);
    }

    [Fact]
    public async Task Requests_Control_Sync_OverLoopback()
    {
        var (master, slave, node, pb) = await Pair();
        await using var _m = master;
        await using var _s = slave;

        Assert.Equal(NodeType.Master, node.NodeType);
        Assert.Equal(master.ListenerPort, node.ListenerPort);
        Assert.Equal("First Light", (await slave.RequestMetadataAsync(node, 1))?.Title);
        Assert.NotNull(await slave.RequestBeatGridAsync(node, 1));
        Assert.Equal(1200, (await slave.RequestSmallWaveformAsync(node, 2))?.Bars.Count);
        Assert.NotNull(await slave.RequestBigWaveformAsync(node, 2));

        var empty = await slave.RequestAsync(node, DataType.Metrics, 0);
        Assert.Equal(NotificationCode.RequestDataEmpty, empty.Notification?.Code);

        var ack = await slave.SendControlAsync(node, ControlCommand.SetState(2, LayerState.Playing));
        Assert.Equal(NotificationCode.Ok, ack?.Code);
        Assert.Equal(LayerState.Playing, pb[2].State);

        var sync = await slave.TimeSyncAsync(node, rounds: 3);
        Assert.InRange(sync.DelayMicros, 0u, 500_000u);
    }

    [Fact]
    public async Task ConcurrentIdenticalRequests_BothSucceed_And_ControlsQueue()
    {
        var (master, slave, node, pb) = await Pair();
        await using var _m = master;
        await using var _s = slave;
        int controls = 0;
        master.PacketReceived += (_, e) => { if (e.Packet is ControlPacket) Interlocked.Increment(ref controls); };
        var a = slave.RequestAsync(node, DataType.Metadata, 1);
        var b = slave.RequestAsync(node, DataType.Metadata, 1);
        Assert.True((await a).Success);
        Assert.True((await b).Success);

        var c1 = slave.SendControlAsync(node, "layer/1/state=5;", TimeSpan.FromSeconds(2));
        var c2 = slave.SendControlAsync(node, "layer/1/state=3;", TimeSpan.FromSeconds(2));
        Assert.Equal(NotificationCode.Ok, (await c1)?.Code);
        Assert.Equal(NotificationCode.Ok, (await c2)?.Code);
        // Both controls share one key: without the queue the second would join the first and never be sent.
        Assert.Equal(2, controls);
        Assert.Equal(LayerState.Playing, pb[1].State);
    }

    [Fact]
    public async Task NoAnswer_TimesOut_And_InfiniteIsAccepted()
    {
        await using var slave = new TCNetNode(Loop(210, "LONELY", NodeType.Slave));
        await slave.StartAsync();
        slave.Inject(new OptInPacket { NodeId = 5, NodeName = "GHOST", ListenerPort = 1 }.ToArray(), new IPEndPoint(IPAddress.Loopback, 1));
        var ghost = slave.FindNode("GHOST")!;
        var r = await slave.RequestAsync(ghost, DataType.Metrics, 1, TimeSpan.FromMilliseconds(100));
        Assert.True(r.TimedOut);
        using var cts = new CancellationTokenSource(150);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => slave.RequestAsync(ghost, DataType.Metadata, 1, Timeout.InfiniteTimeSpan, cts.Token));
    }

    [Fact]
    public async Task Registry_OneDiscovery_KeepsPort_OptOutRemoves()
    {
        await using var node = new TCNetNode(Loop(220, "REG", NodeType.Slave));
        await node.StartAsync();
        int discovered = 0, lost = 0;
        node.NodeDiscovered += (_, _) => Interlocked.Increment(ref discovered);
        node.NodeLost += (_, _) => Interlocked.Increment(ref lost);
        var from = new IPEndPoint(IPAddress.Parse("10.1.2.3"), 5000);
        var hello = new OptInPacket { NodeId = 7, NodeName = "PEER", ListenerPort = 65100 }.ToArray();
        Parallel.For(0, 20, _ => node.Inject(hello, from));
        Assert.Equal(1, discovered);
        node.Inject(new StatusPacket { NodeId = 7, NodeName = "PEER", ListenerPort = 0 }.ToArray(), from);
        Assert.Equal(65100, node.FindNode("PEER")!.ListenerPort);
        node.Inject(new OptOutPacket { NodeId = 7, NodeName = "PEER" }.ToArray(), from);
        Assert.Null(node.FindNode("PEER"));
        Assert.Equal(1, lost);
    }

    [Fact]
    public async Task ThrowingStatusProvider_And_ConcurrentStop_AreSafe()
    {
        var node = new TCNetNode(Loop(230, "SAFE", NodeType.Master));
        node.Settings.OptInInterval = TimeSpan.FromMilliseconds(50);
        int sent = 0, calls = 0, warnings = 0;
        node.PacketSent += (_, e) => { if (e.Packet is OptInPacket) Interlocked.Increment(ref sent); };
        node.Warning += (_, w) => { if (w.Contains("boom")) Interlocked.Increment(ref warnings); };
        node.StatusProvider = () =>
        {
            Interlocked.Increment(ref calls);
            throw new InvalidOperationException("boom");
        };
        await node.StartAsync();
        // Opt-IN keeps going although the provider throws every time (polled: timing varies under load).
        for (int i = 0; i < 100 && (Volatile.Read(ref sent) < 3 || Volatile.Read(ref warnings) < 1); i++) await Task.Delay(50);
        Assert.True(Volatile.Read(ref sent) >= 3);
        Assert.True(Volatile.Read(ref calls) >= 1);
        Assert.True(Volatile.Read(ref warnings) >= 1);
        await Task.WhenAll(node.StopAsync(), node.StopAsync(), node.StopAsync());
        Assert.False(node.IsRunning);
    }

    [Fact]
    public async Task MasterElection_AutoPromotesWhenMasterLeaves()
    {
        var settings = Loop(240, "AUTO", NodeType.Auto);
        settings.AutoMasterElection = true;
        await using var node = new TCNetNode(settings);
        await node.StartAsync();
        var from = new IPEndPoint(IPAddress.Parse("10.9.9.9"), 65023);
        node.Inject(new OptInPacket { NodeId = 1, NodeName = "BOSS", NodeType = NodeType.Master }.ToArray(), from);
        node.Inject(new OptOutPacket { NodeId = 1, NodeName = "BOSS", NodeType = NodeType.Master }.ToArray(), from);
        Assert.Equal(NodeType.Master, node.NodeType);

        node.Inject(new OptInPacket { NodeId = 2, NodeName = "REAL", NodeType = NodeType.Master }.ToArray(), new IPEndPoint(IPAddress.Parse("10.9.9.8"), 65023));
        Assert.Equal(NodeType.Auto, node.NodeType);
    }
}
