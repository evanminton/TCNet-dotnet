using System.Net;
using TCNet.Networking;

namespace TCNet.Tests;

public class TimecodeRoundTripTests
{
    [Theory]
    [InlineData(SmpteMode.Fps24)]
    [InlineData(SmpteMode.Fps25)]
    [InlineData(SmpteMode.Fps2997)]
    [InlineData(SmpteMode.Fps30)]
    public void EveryFrame_RoundTrips(SmpteMode mode)
    {
        int fps = Timecode.FrameCount(mode);
        for (int s = 0; s < 120; s++)
            for (int f = 0; f < fps; f++)
            {
                var tc = new Timecode(0, (byte)(s / 60), (byte)(s % 60), (byte)f);
                Assert.Equal(tc, Timecode.FromMilliseconds(tc.ToMilliseconds(mode), mode));
            }
    }
}

public class NodeFixTests
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

    [Fact]
    public async Task CancelledRequest_DoesNotBlockTheNextOne()
    {
        await using var node = new TCNetNode(Loop(301, "CANCEL", NodeType.Slave));
        await node.StartAsync();
        int requests = 0;
        node.PacketSent += (_, e) => { if (e.Packet is RequestPacket) Interlocked.Increment(ref requests); };
        node.Inject(new OptInPacket { NodeId = 5, NodeName = "GHOST", ListenerPort = 1 }.ToArray(), new IPEndPoint(IPAddress.Loopback, 1));
        var ghost = node.FindNode("GHOST")!;

        using var cts = new CancellationTokenSource(100);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => node.RequestAsync(ghost, DataType.Metrics, 1, Timeout.InfiniteTimeSpan, cts.Token));
        var again = await node.RequestAsync(ghost, DataType.Metrics, 1, TimeSpan.FromMilliseconds(100));
        Assert.True(again.TimedOut);
        Assert.Equal(2, requests);
    }

    [Fact]
    public async Task ShortTimeout_DoesNotDropAnswerForLongerWaiter()
    {
        var pb = Playback.Demo();
        await using var master = new TCNetNode(Loop(311, "MASTER", NodeType.Master));
        await using var slave = new TCNetNode(Loop(312, "SLAVE", NodeType.Slave));
        master.RequestHandler = (rq, _) => { Thread.Sleep(1500); return pb.Answer(rq); };
        await master.StartAsync();
        await slave.StartAsync();
        await slave.SendAsync(slave.CreateOptIn(), new IPEndPoint(IPAddress.Loopback, master.ListenerPort));
        var node = await Until(() => slave.Nodes.FirstOrDefault(n => n.NodeName == "MASTER"));

        var quick = slave.RequestAsync(node, DataType.Metadata, 1, TimeSpan.FromMilliseconds(100));
        var patient = slave.RequestAsync(node, DataType.Metadata, 1, TimeSpan.FromSeconds(15));
        var q = await quick;
        var r = await patient;
        // The quick caller normally times out; on a starved thread pool its timer can run after the answer is in,
        // and then it returns the answer. Either is fine: what matters is that the patient caller gets it.
        Assert.True(q.TimedOut || q.Success, $"quick: {q}");
        Assert.True(r.Success, $"patient: {r}");
    }

    [Fact]
    public async Task ThrowingHandlers_DoNotStopOptIn()
    {
        var settings = Loop(321, "HANDLERS", NodeType.Slave);
        settings.OptInInterval = TimeSpan.FromMilliseconds(50);
        settings.NodeTimeout = TimeSpan.FromMilliseconds(60);
        await using var node = new TCNetNode(settings);
        int optIns = 0;
        node.PacketSent += (_, _) => throw new InvalidOperationException("sent handler");
        node.PacketSent += (_, e) => { if (e.Packet is OptInPacket) Interlocked.Increment(ref optIns); };
        node.NodeLost += (_, _) => throw new InvalidOperationException("lost handler");
        node.Warning += (_, _) => throw new InvalidOperationException("warning handler");
        await node.StartAsync();
        // A peer that times out, so NodeLost fires from the housekeeping loop.
        node.Inject(new OptInPacket { NodeId = 9, NodeName = "BRIEF", ListenerPort = 0 }.ToArray(), new IPEndPoint(IPAddress.Parse("10.0.0.9"), 5000));
        await Task.Delay(500);
        Assert.True(Volatile.Read(ref optIns) >= 5, $"only {optIns} Opt-INs");
        Assert.Null(node.FindNode("BRIEF"));
    }

    [Fact]
    public async Task Election_SameNodeIdOnAnotherAddress_StillElects()
    {
        var settings = Loop(240, "AUTO", NodeType.Auto);
        settings.AutoMasterElection = true;
        await using var node = new TCNetNode(settings);
        await node.StartAsync();
        // A twin with the same Node ID and uptime on a higher address: 127.0.0.1 sorts first, so this node wins.
        node.Inject(new OptInPacket { NodeId = 240, NodeName = "TWIN", NodeType = NodeType.Auto, Uptime = node.Clock.Uptime }.ToArray(),
            new IPEndPoint(IPAddress.Parse("200.1.1.1"), 65023));
        var boss = new IPEndPoint(IPAddress.Parse("10.9.9.9"), 65023);
        node.Inject(new OptInPacket { NodeId = 1, NodeName = "BOSS", NodeType = NodeType.Master }.ToArray(), boss);
        node.Inject(new OptOutPacket { NodeId = 1, NodeName = "BOSS", NodeType = NodeType.Master }.ToArray(), boss);
        Assert.Equal(NodeType.Master, node.NodeType);
    }

    [Fact]
    public async Task ElectedMaster_YieldsToMasterSetByHandLater()
    {
        var settings = Loop(250, "AUTO", NodeType.Auto);
        settings.AutoMasterElection = true;
        settings.OptInInterval = TimeSpan.FromMilliseconds(20);
        await using var node = new TCNetNode(settings);
        await node.StartAsync();
        var peer = new IPEndPoint(IPAddress.Parse("10.9.9.8"), 65023);
        // Seen as Auto (low uptime, so this node wins the election), later switched to Master by hand; higher Node ID.
        node.Inject(new OptInPacket { NodeId = 900, NodeName = "DESK", NodeType = NodeType.Auto }.ToArray(), peer);
        var boss = new IPEndPoint(IPAddress.Parse("10.9.9.9"), 65023);
        node.Inject(new OptInPacket { NodeId = 1, NodeName = "BOSS", NodeType = NodeType.Master }.ToArray(), boss);
        // Both uptimes are about 0: a tie, and the lower Node ID (250 < 900) wins.
        node.Inject(new OptOutPacket { NodeId = 1, NodeName = "BOSS", NodeType = NodeType.Master }.ToArray(), boss);
        Assert.Equal(NodeType.Master, node.NodeType);

        await Task.Delay(300);
        node.Inject(new OptInPacket { NodeId = 900, NodeName = "DESK", NodeType = NodeType.Master }.ToArray(), peer);
        Assert.Equal(NodeType.Auto, node.NodeType);
    }

    [Fact]
    public async Task TimeStream_RejectsNonPositiveInterval()
    {
        await using var node = new TCNetNode(Loop(331, "STREAM", NodeType.Master));
        Assert.Throws<ArgumentOutOfRangeException>(() => node.StartTimeStream(() => new TimePacket(), TimeSpan.Zero));
    }

    [Fact]
    public async Task ElectedMaster_IsAutoAgainAfterRestart()
    {
        var settings = Loop(260, "AUTO", NodeType.Auto);
        settings.AutoMasterElection = true;
        await using var node = new TCNetNode(settings);
        await node.StartAsync();
        var boss = new IPEndPoint(IPAddress.Parse("10.9.9.9"), 65023);
        node.Inject(new OptInPacket { NodeId = 1, NodeName = "BOSS", NodeType = NodeType.Master }.ToArray(), boss);
        node.Inject(new OptOutPacket { NodeId = 1, NodeName = "BOSS", NodeType = NodeType.Master }.ToArray(), boss);
        Assert.Equal(NodeType.Master, node.NodeType);

        await node.StopAsync();
        Assert.Equal(NodeType.Auto, node.NodeType);
        await node.StartAsync();
        // The master is back: a node still advertising Master would compete with it, one that is Auto simply follows.
        node.Inject(new OptInPacket { NodeId = 1, NodeName = "BOSS", NodeType = NodeType.Master }.ToArray(), boss);
        Assert.Equal(NodeType.Auto, node.NodeType);
    }

    [Fact]
    public async Task ConfiguredMaster_StaysMasterAfterRestart()
    {
        await using var node = new TCNetNode(Loop(261, "BOSS", NodeType.Master));
        await node.StartAsync();
        await node.StopAsync();
        Assert.Equal(NodeType.Master, node.NodeType);
    }

    [Fact]
    public async Task EndlessRequest_EndsWhenTheNodeStops()
    {
        await using var node = new TCNetNode(Loop(302, "ENDLESS", NodeType.Slave));
        await node.StartAsync();
        node.Inject(new OptInPacket { NodeId = 6, NodeName = "GHOST", ListenerPort = 1 }.ToArray(), new IPEndPoint(IPAddress.Loopback, 1));
        var ghost = node.FindNode("GHOST")!;
        var request = node.RequestAsync(ghost, DataType.Metrics, 1, Timeout.InfiniteTimeSpan);
        var sync = node.TimeSyncAsync(ghost, 1, Timeout.InfiniteTimeSpan);
        await Task.Delay(50);

        await node.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request.WaitAsync(TimeSpan.FromSeconds(10)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sync.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task EndlessRequestInsideAHandler_DoesNotHangStop()
    {
        await using var node = new TCNetNode(Loop(303, "HANDLER", NodeType.Slave));
        await node.StartAsync();
        var blocked = new TaskCompletionSource();
        node.NodeDiscovered += (_, e) =>
        {
            blocked.TrySetResult();
            // Blocks the thread handling the datagram, as a blocking C call from a callback does.
            try { node.RequestAsync(e.Node, DataType.Metrics, 1, Timeout.InfiniteTimeSpan).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { }
        };
        var inject = Task.Run(() =>
            node.Inject(new OptInPacket { NodeId = 7, NodeName = "GHOST", ListenerPort = 1 }.ToArray(), new IPEndPoint(IPAddress.Loopback, 1)));
        await blocked.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await node.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await inject.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task TimeStream_HandlerCanStopTheStream()
    {
        await using var node = new TCNetNode(Loop(332, "STREAM", NodeType.Master));
        await node.StartAsync();
        var stopped = new TaskCompletionSource();
        node.PacketSent += (_, e) =>
        {
            if (e.Packet is not TimePacket || stopped.Task.IsCompleted) return;
            node.StopTimeStreamAsync().GetAwaiter().GetResult();   // on the stream's own thread
            stopped.TrySetResult();
        };
        node.StartTimeStream(() => new TimePacket(), TimeSpan.FromMilliseconds(5));
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Until(() => node.IsStreaming ? null : "");
        await node.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task TimeStream_HandlerCanRestartTheStream()
    {
        await using var node = new TCNetNode(Loop(333, "STREAM", NodeType.Master));
        await node.StartAsync();
        int restarts = 0, sent = 0;
        node.PacketSent += (_, e) =>
        {
            if (e.Packet is not TimePacket) return;
            int n = Interlocked.Increment(ref sent);
            if (n <= 3)
            {
                node.StartTimeStream(() => new TimePacket(), TimeSpan.FromMilliseconds(5));   // on the stream's own thread
                Interlocked.Increment(ref restarts);
            }
        };
        node.StartTimeStream(() => new TimePacket(), TimeSpan.FromMilliseconds(5));
        await Until(() => Volatile.Read(ref sent) > 10 ? "" : null);
        Assert.Equal(3, Volatile.Read(ref restarts));
        Assert.True(node.IsStreaming);
        await node.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(node.IsStreaming);
    }

    [Fact]
    public async Task Playback_LoadIsNotLostToAdvance()
    {
        var pb = new Playback();
        var deck = pb[1];
        deck.Load(1, "A", "T", 10_000, 120);
        using var stop = new CancellationTokenSource();
        var mover = Task.Run(() => { while (!stop.IsCancellationRequested) pb.Advance(); });
        try
        {
            for (int i = 0; i < 2000; i++)
            {
                deck.PositionMs = 5_000;
                deck.State = LayerState.Playing;
                deck.Load((uint)i, "A", "T", 10_000, 120);
                Assert.Equal(LayerState.Paused, deck.State);
                Assert.Equal(0, deck.PositionMs);
            }
        }
        finally
        {
            stop.Cancel();
            await mover;
        }
    }
}
