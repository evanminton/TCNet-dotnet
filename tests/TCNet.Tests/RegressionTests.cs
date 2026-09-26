using System.Net;
using TCNet.Networking;

namespace TCNet.Tests;

public class AssemblerLimitTests
{
    private static ApplicationDataPacket Chunk(uint total, uint number, byte fill, uint dataSize = 0, ushort nodeId = 1) => new()
    {
        NodeId = nodeId, ApplicationCode = 1, DataSize = dataSize, TotalPackets = total, PacketNumber = number, Payload = [fill, fill],
    };

    [Fact]
    public void OutOfRangePacketNumbers_AreIgnored()
    {
        var asm = new TCNetChunkAssembler();
        Assert.Null(asm.Add(Chunk(3, 0, 1)));
        Assert.Null(asm.Add(Chunk(3, 7, 7)));
        Assert.Null(asm.Add(Chunk(3, 1000, 9)));
        Assert.Null(asm.Add(Chunk(3, 1, 2)));
        Assert.Equal(new byte[] { 1, 1, 2, 2, 3, 3 }, asm.Add(Chunk(3, 2, 3))!.Data);
    }

    [Fact]
    public void GapInNumbering_DoesNotComplete()
    {
        // 0, 1 and 3 of a 3-packet transfer: 3 is valid for 1-based senders, but the set is not contiguous.
        var asm = new TCNetChunkAssembler();
        Assert.Null(asm.Add(Chunk(3, 0, 1)));
        Assert.Null(asm.Add(Chunk(3, 1, 2)));
        Assert.Null(asm.Add(Chunk(3, 3, 4)));
    }

    [Fact]
    public void RepeatedNumberWithNewContent_StartsNewTransfer()
    {
        var asm = new TCNetChunkAssembler();
        Assert.Null(asm.Add(Chunk(2, 0, 1)));      // first transfer loses packet 1
        Assert.Null(asm.Add(Chunk(2, 0, 5)));      // new transfer
        Assert.Equal(new byte[] { 5, 5, 6, 6 }, asm.Add(Chunk(2, 1, 6))!.Data);
    }

    [Fact]
    public void Limits_BoundMemory()
    {
        var asm = new TCNetChunkAssembler { MaxPendingTransfers = 10, MaxTotalPackets = 100 };
        Assert.Null(asm.Add(Chunk(uint.MaxValue, 0, 1)));
        Assert.Equal(0, asm.PendingCount);
        for (ushort id = 0; id < 50; id++) asm.Add(Chunk(2, 0, 1, nodeId: id));
        Assert.Equal(10, asm.PendingCount);

        var small = new TCNetChunkAssembler { MaxTransferBytes = 3 };
        Assert.Null(small.Add(Chunk(3, 0, 1)));
        Assert.Null(small.Add(Chunk(3, 1, 1)));
        Assert.Equal(0, small.PendingCount);
    }
}

public class PacketRegressionTests
{
    [Fact]
    public void CueData_LoopOutSurvivesWithEmptyCue1()
    {
        var p = new CueDataPacket { LayerId = 1, LoopIn = 1000, LoopOut = 60_000 };
        p.Cues[1].Type = 1;
        p.Cues[1].InTime = 5000;
        var back = (CueDataPacket)TCNetPacket.Parse(p.ToArray());
        Assert.Equal(60_000u, back.LoopOut);
        Assert.True(back.Cues[0].IsEmpty);
        Assert.Equal(5000u, back.Cues[1].InTime);
    }

    [Fact]
    public void CueData_Cue1StillWinsWhenSet()
    {
        var p = new CueDataPacket { LayerId = 1, LoopOut = 60_000 };
        p.Cues[0].Type = 2;
        p.Cues[0].InTime = 123_456;
        p.Cues[0].OutTime = 200_000;
        var back = (CueDataPacket)TCNetPacket.Parse(p.ToArray());
        Assert.Equal(2, back.Cues[0].Type);
        Assert.Equal(123_456u, back.Cues[0].InTime);
        Assert.Equal(200_000u, back.Cues[0].OutTime);
    }

    [Fact]
    public void TextData_NonUtf8Payload_RoundTripsByteForByte()
    {
        var bytes = new TextDataPacket("x").ToArray();
        bytes[42] = 0xFF;
        var p = (TextDataPacket)TCNetPacket.Parse(bytes);
        Assert.False(p.WasPadded);
        Assert.Equal("ÿ", p.Text);
        Assert.Equal(bytes, p.ToArray());
    }

    [Fact]
    public void Mixer_IndexerRejectsNameBytes()
    {
        var m = new MixerDataPacket();
        Assert.Throws<ArgumentOutOfRangeException>(() => m[30] = 1);
        m[59] = 7;
        Assert.Equal(7, m[59]);
    }
}

public class NodeRegressionTests
{
    private static TCNetNode NewNode() => new(new TCNetNodeSettings { NodeId = 9, NodeName = "LOCAL", AutoTimeSync = false });

    private static byte[] OptIn(ushort id, ushort listenerPort) =>
        new OptInPacket { NodeId = id, NodeName = "PEER", NodeType = NodeType.Slave, ListenerPort = listenerPort }.ToArray();

    [Fact]
    public void ConcurrentFirstContact_RaisesOneDiscovery()
    {
        for (int trial = 0; trial < 100; trial++)
        {
            var node = NewNode();
            int discovered = 0;
            node.NodeDiscovered += (_, _) => Interlocked.Increment(ref discovered);
            var data = OptIn(1, 65100);
            var remote = new IPEndPoint(IPAddress.Parse("10.0.0.2"), 65100);
            Parallel.For(0, 4, _ => node.InjectDatagram(data, remote));
            Assert.Equal(1, discovered);
            Assert.Equal(4, node.PacketsReceived);
        }
    }

    [Fact]
    public void ZeroListenerPort_DoesNotWipeKnownPort()
    {
        var node = NewNode();
        var remote = new IPEndPoint(IPAddress.Parse("10.0.0.2"), 60000);
        node.InjectDatagram(OptIn(1, 65100), remote);
        node.InjectDatagram(new StatusPacket { NodeId = 1, NodeName = "PEER", ListenerPort = 0 }.ToArray(), remote);
        Assert.Equal(65100, node.Nodes.Single().ListenerPort);
    }

    [Fact]
    public async Task ThrowingStatusProvider_DoesNotStopOptIn()
    {
        var settings = new TCNetNodeSettings
        {
            NodeId = 11, NodeName = "HK", NodeType = NodeType.Master, LocalAddress = IPAddress.Loopback, BroadcastAddress = IPAddress.Loopback,
            ListenOnBroadcastPorts = false, AutoTimeSync = false, OptInInterval = TimeSpan.FromMilliseconds(50), SendStatus = true,
        };
        await using var node = new TCNetNode(settings);
        int calls = 0;
        node.StatusProvider = () => Interlocked.Increment(ref calls) == 2 ? throw new InvalidOperationException("boom") : new StatusPacket();
        node.Start();
        await Task.Delay(600);
        Assert.True(Volatile.Read(ref calls) >= 5, $"StatusProvider called {calls} times");
    }

    [Fact]
    public async Task ConcurrentStop_IsSafe()
    {
        var node = new TCNetNode(new TCNetNodeSettings
        {
            NodeId = 12, NodeName = "STOP", LocalAddress = IPAddress.Loopback, BroadcastAddress = IPAddress.Loopback,
            ListenOnBroadcastPorts = false, AutoTimeSync = false,
        });
        node.Start();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(node.StopAsync)));
        Assert.False(node.IsRunning);
        node.Start();
        Assert.True(node.IsRunning);
        await node.StopAsync();
        Assert.False(node.IsRunning);
    }

    [Fact]
    public async Task ConcurrentRequests_ForSameData_BothSucceed()
    {
        TCNetNodeSettings S(ushort id, string name) => new()
        {
            NodeId = id, NodeName = name, LocalAddress = IPAddress.Loopback, BroadcastAddress = IPAddress.Loopback,
            ListenOnBroadcastPorts = false, AutoTimeSync = false,
        };
        var playback = new TCNetPlayback();
        playback[1].Load(3, "A", "Song", 100_000, 120);
        await using var master = new TCNetNode(S(21, "M"));
        await using var slave = new TCNetNode(S(22, "S"));
        master.RequestHandler = (rq, _) => playback.HandleRequest(rq);
        master.ControlHandler = (cp, _) => playback.Apply(cp);
        master.Start();
        slave.Start();
        await slave.SendAsync(slave.CreateOptIn(), new IPEndPoint(IPAddress.Loopback, master.ListenerPort));
        await master.SendAsync(master.CreateOptIn(), new IPEndPoint(IPAddress.Loopback, slave.ListenerPort));
        TCNetRemoteNode? peer = null;
        for (int i = 0; i < 100 && (peer = slave.Nodes.FirstOrDefault(n => n.NodeName == "M")) is null; i++) await Task.Delay(20);
        Assert.NotNull(peer);

        var results = await Task.WhenAll(
            slave.RequestAsync(peer!, DataType.Metadata, 1),
            slave.RequestAsync(peer!, DataType.Metadata, 1));
        Assert.All(results, r => Assert.IsType<MetadataPacket>(r.Packet));

        var acks = await Task.WhenAll(
            slave.SendControlAsync(peer!, ControlCommand.SetLayerState(1, LayerState.Playing)),
            slave.SendControlAsync(peer!, ControlCommand.SetLayerState(2, LayerState.Playing)));
        Assert.All(acks, a => Assert.Equal(NotificationCode.Ok, a?.Code));
    }
}
