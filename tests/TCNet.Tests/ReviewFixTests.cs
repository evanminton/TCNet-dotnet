using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TCNet.Native;
using TCNet.Networking;

namespace TCNet.Tests;

/// <summary>Findings of the 2026-09-29 full review; each test failed on the code before the fix.</summary>
public class ReviewFixTests
{
    private static NodeSettings Loop(ushort id, string name, NodeType type) => new()
    {
        NodeId = id, NodeName = name, NodeType = type, LocalAddress = IPAddress.Loopback, BroadcastAddress = IPAddress.Loopback,
        ListenOnBroadcastPorts = false, AutoTimeSync = false,
    };

    [Fact]
    public async Task SetNodeType_IsNotOverriddenByAConcurrentElection()
    {
        var from = new IPEndPoint(IPAddress.Parse("10.9.9.9"), 65023);
        var hello = new OptInPacket { NodeId = 7, NodeName = "MASTER", NodeType = NodeType.Master, ListenerPort = 65023 }.ToArray();
        var bye = new OptOutPacket { NodeId = 7, NodeName = "MASTER", NodeType = NodeType.Master }.ToArray();
        for (int i = 0; i < 2000; i++)
        {
            var s = Loop(400, "RACE", NodeType.Auto);
            s.AutoMasterElection = true;
            var node = new TCNetNode(s);
            var roles = new List<NodeType>();
            node.RoleChanged += (_, r) => { lock (roles) roles.Add(r); };
            node.Inject(hello, from);
            using var go = new Barrier(2);
            var election = Task.Run(() => { go.SignalAndWait(); node.Inject(bye, from); });
            var user = Task.Run(() => { go.SignalAndWait(); node.SetNodeType(NodeType.Slave); });
            await Task.WhenAll(election, user);
            Assert.Equal(NodeType.Slave, node.NodeType);
            Assert.Equal(NodeType.Slave, roles[^1]);
        }
    }

    [Fact]
    public void PopulationList_IsCapped()
    {
        var s = Loop(401, "FLOOD", NodeType.Slave);
        s.MaxNodes = 100;
        var node = new TCNetNode(s);
        var warnings = new List<string>();
        node.Warning += (_, w) => warnings.Add(w);
        var from = new IPEndPoint(IPAddress.Parse("10.1.1.1"), 65023);
        for (int i = 1; i <= 300; i++)
            node.Inject(new OptInPacket { NodeId = (ushort)i, NodeName = "X", ListenerPort = 65023 }.ToArray(), from);
        Assert.Equal(100, node.Nodes.Count);
        Assert.Single(warnings);
        // Known nodes keep being updated.
        node.Inject(new OptInPacket { NodeId = 1, NodeName = "RENAMED", ListenerPort = 65023 }.ToArray(), from);
        Assert.NotNull(node.FindNode("RENAMED"));
    }

    [Theory]
    [InlineData(53, 0)]
    [InlineData(65022, 0)]
    [InlineData(65023, 65023)]
    [InlineData(65535, 65535)]
    public void ListenerPort_OutsideTheUnicastRange_IsIgnored(int port, int expected)
    {
        var node = new TCNetNode(Loop(402, "PORTS", NodeType.Slave));
        node.Inject(new OptInPacket { NodeId = 9, NodeName = "PEER", ListenerPort = (ushort)port }.ToArray(), new IPEndPoint(IPAddress.Parse("10.1.1.2"), 40000));
        Assert.Equal(expected, node.FindNode("PEER")!.ListenerPort);
    }

    [Theory]
    [InlineData("BOOTH ")]
    [InlineData("DJ ★")]
    [InlineData("BOOTH")]
    public async Task OwnPackets_AreRecognised_WhateverTheName(string name)
    {
        await using var node = new TCNetNode(Loop(403, name, NodeType.Slave));
        await node.StartAsync();
        var p = node.CreateOptIn();
        node.Stamp(p);
        node.Inject(p.ToArray(), new IPEndPoint(IPAddress.Loopback, node.ListenerPort));
        Assert.Empty(node.Nodes);
    }

    [Fact]
    public async Task TimeStream_NeedsARunningNode_AndDoesNotOutliveIt()
    {
        await using var node = new TCNetNode(Loop(404, "STREAM", NodeType.Master));
        Assert.Throws<InvalidOperationException>(() => node.StartTimeStream(() => new TimePacket(), TimeSpan.FromMilliseconds(5)));
        Assert.False(node.IsStreaming);

        await node.StartAsync();
        node.StartTimeStream(() => new TimePacket(), TimeSpan.FromMilliseconds(5));
        await node.StopAsync();
        await node.StartAsync();
        int sent = 0;
        node.PacketSent += (_, e) => { if (e.Packet is TimePacket) Interlocked.Increment(ref sent); };
        await Task.Delay(100);
        Assert.False(node.IsStreaming);
        Assert.Equal(0, sent);
    }

    [Fact]
    public async Task PacketSent_EachEventHasItsOwnPacket()
    {
        await using var node = new TCNetNode(Loop(405, "SEQ", NodeType.Slave));
        await node.StartAsync();
        node.Inject(new OptInPacket { NodeId = 9, NodeName = "PEER", ListenerPort = 65535 }.ToArray(), new IPEndPoint(IPAddress.Loopback, 65535));
        var seen = new List<(TCNetPacket P, byte Seq)>();
        node.PacketSent += (_, e) => { if (e.Packet is OptInPacket) lock (seen) seen.Add((e.Packet, e.Packet.Sequence)); };
        // One Opt-IN object goes to the broadcast address and then to the peer.
        await node.SendOptInAsync();
        lock (seen)
        {
            Assert.True(seen.Count >= 2);
            Assert.Equal(seen.Count, seen.Select(x => x.P).Distinct().Count());
            Assert.All(seen, x => Assert.Equal(x.Seq, x.P.Sequence));
        }
    }

    [Fact]
    public void PacketSnapshot_KeepsCueLayout()
    {
        var cue = new CueDataPacket { Layout = CueLayout.AfterLoop, LayerId = 1, LoopOutMs = 1234 };
        cue.Cues[0].InMs = 500;
        var snapshot = (CueDataPacket)typeof(TCNetNode).GetMethod("Snapshot", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, [cue, cue.ToArray()])!;
        Assert.Equal(CueLayout.AfterLoop, snapshot.Layout);
        Assert.Equal(1234u, snapshot.LoopOutMs);
        Assert.Equal(500u, snapshot.Cues[0].InMs);
    }

    [Theory]
    [InlineData(0, 5000)]
    [InlineData(-1, 5000)]
    [InlineData(1000, 0)]
    public async Task Start_RejectsTimersThatCannotRun(int optInMs, int timeoutMs)
    {
        var s = Loop(406, "ZERO", NodeType.Slave);
        s.OptInInterval = TimeSpan.FromMilliseconds(optInMs);
        s.NodeTimeout = TimeSpan.FromMilliseconds(timeoutMs);
        await using var node = new TCNetNode(s);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(node.StartAsync);
        Assert.False(node.IsRunning);
    }

    [Fact]
    public async Task RoleChangedHandler_CanRestartTheNodeWhileItStops()
    {
        var s = Loop(407, "ROLE", NodeType.Auto);
        s.AutoMasterElection = true;
        await using var node = new TCNetNode(s);
        await node.StartAsync();
        var from = new IPEndPoint(IPAddress.Parse("10.9.9.9"), 65023);
        node.Inject(new OptInPacket { NodeId = 7, NodeName = "MASTER", NodeType = NodeType.Master, ListenerPort = 65023 }.ToArray(), from);
        node.Inject(new OptOutPacket { NodeId = 7, NodeName = "MASTER", NodeType = NodeType.Master }.ToArray(), from);
        Assert.Equal(NodeType.Master, node.NodeType);
        node.RoleChanged += (_, r) => { if (r == NodeType.Auto) node.StartAsync().Wait(TimeSpan.FromSeconds(5)); };
        await node.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(node.IsRunning);
    }

    [Fact]
    public async Task ControlQueues_AreRemovedWhenIdle()
    {
        await using var node = new TCNetNode(Loop(408, "QUEUE", NodeType.Slave));
        await node.StartAsync();
        for (int i = 1; i <= 20; i++)
        {
            node.Inject(new OptInPacket { NodeId = (ushort)i, NodeName = "P", ListenerPort = 65535 }.ToArray(), new IPEndPoint(IPAddress.Loopback, 65535));
            await node.SendControlAsync(node.Nodes.Single(n => n.NodeId == i), "x;", TimeSpan.FromMilliseconds(1));
        }
        var queues = (System.Collections.ICollection)typeof(TCNetNode).GetField("_controlQueues", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(node)!;
        Assert.Empty(queues);
    }
}

[Collection(nameof(NativeCallbackTests))]
public unsafe class ReviewFixNativeTests
{
    private static void* _handle;
    private static int _result = int.MinValue;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void StartingCallback(nint user, int kind, byte* json)
    {
        delegate* unmanaged[Cdecl]<void*, int> start = &Exports.NodeStart;
        Interlocked.CompareExchange(ref _result, start(_handle), int.MinValue);
    }

    [Fact]
    public void Start_InsideACallback_Fails()
    {
        delegate* unmanaged[Cdecl]<byte*, void*> create = &Exports.NodeCreate;
        delegate* unmanaged[Cdecl]<void*, int, nint, void*, int> setCallback = &Exports.NodeSetCallback;
        delegate* unmanaged[Cdecl]<void*, byte*, int, byte*, int, int> inject = &Exports.NodeInject;
        delegate* unmanaged[Cdecl]<void*, void> destroy = &Exports.NodeDestroy;
        delegate* unmanaged[Cdecl]<byte*> lastError = &Exports.LastError;

        fixed (byte* s = "{\"listenOnBroadcastPorts\":false,\"localAddress\":\"127.0.0.1\",\"autoTimeSync\":false}\0"u8)
            _handle = create(s);
        try
        {
            delegate* unmanaged[Cdecl]<nint, int, byte*, void> callback = &StartingCallback;
            Assert.Equal(0, setCallback(_handle, 1 << 4, (nint)callback, null));
            var hello = new OptInPacket { NodeId = 3, NodeName = "PEER" }.ToArray();
            fixed (byte* d = hello) fixed (byte* ip = "10.0.0.2\0"u8)
                Assert.Equal(0, inject(_handle, d, hello.Length, ip, 65023));   // the callback runs on this thread
            Assert.Equal(-1, _result);
            Assert.Contains("tcnet_node_start", Marshal.PtrToStringUTF8((nint)lastError()));
        }
        finally { destroy(_handle); }
    }
}
