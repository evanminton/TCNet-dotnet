using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TCNet.Native;
using TCNet.Networking;

namespace TCNet.Tests;

/// <summary>The C callback contract: set_callback (and destroy) return only after running callbacks have finished.</summary>
[Collection(nameof(NativeCallbackTests))]
[CollectionDefinition(nameof(NativeCallbackTests), DisableParallelization = true)]
public class NativeCallbackTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    // State for the static C callbacks (tests in this class don't run in parallel).
    private static int _calls, _freed, _usedAfterFree, _sawInCallback;
    private static NativeNode? _node;
    private static Barrier? _both;

    private static byte[] OptIn(ushort id) => new OptInPacket { NodeId = id, NodeName = $"PEER{id}" }.ToArray();

    private static async Task Until(Func<bool> done)
    {
        var until = DateTime.UtcNow + Limit;
        while (!done())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("The callback never ran.");
            await Task.Delay(1);
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void SlowCallback(nint user, int kind, byte* json)
    {
        if (NativeNode.InCallback) Interlocked.Exchange(ref _sawInCallback, 1);
        Interlocked.Increment(ref _calls);
        Thread.Sleep(300);
        if (Volatile.Read(ref _freed) != 0) Interlocked.Exchange(ref _usedAfterFree, 1);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void CountingCallback(nint user, int kind, byte* json) => Interlocked.Increment(ref _calls);

    /// <summary>Both callbacks are running at once, then each replaces the callback.</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void ReplacingCallback(nint user, int kind, byte* json)
    {
        _both!.SignalAndWait(Limit);
        _node!.SetCallback(1 << 4, ReplacingPointer(), 0);
        Interlocked.Increment(ref _calls);
    }

    /// <summary>Makes a request that never times out, like a C callback calling tcnet_node_request_json(..., -1).</summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void RequestingCallback(nint user, int kind, byte* json)
    {
        Interlocked.Increment(ref _calls);
        try { _node!.Request("GHOST", 2 /* metrics */, 1, -1); }
        catch (Exception) { Interlocked.Exchange(ref _freed, 1); }
    }

    private static unsafe nint SlowPointer() => (nint)(delegate* unmanaged[Cdecl]<nint, int, byte*, void>)&SlowCallback;
    private static unsafe nint CountingPointer() => (nint)(delegate* unmanaged[Cdecl]<nint, int, byte*, void>)&CountingCallback;
    private static unsafe nint ReplacingPointer() => (nint)(delegate* unmanaged[Cdecl]<nint, int, byte*, void>)&ReplacingCallback;
    private static unsafe nint RequestingPointer() => (nint)(delegate* unmanaged[Cdecl]<nint, int, byte*, void>)&RequestingCallback;

    [Fact]
    public async Task SetCallback_WaitsForRunningCallback()
    {
        _calls = _freed = _usedAfterFree = _sawInCallback = 0;
        var n = new NativeNode(new NodeSettings { NodeName = "CBTEST" });
        n.SetCallback(1 << 4 /* nodeDiscovered */, SlowPointer(), 0);
        var inject = Task.Run(() => n.Inject(OptIn(7), "10.0.0.7", 60000));
        await Until(() => Volatile.Read(ref _calls) > 0);

        n.SetCallback(0, 0, 0);                 // must not return while SlowCallback is still running
        Interlocked.Exchange(ref _freed, 1);    // what C code would do next: free its user data
        await inject.WaitAsync(Limit);
        Assert.Equal(0, Volatile.Read(ref _usedAfterFree));
        Assert.Equal(1, Volatile.Read(ref _sawInCallback));
        Assert.False(NativeNode.InCallback);
    }

    [Fact]
    public async Task SetCallback_WaitsOnlyForTheOldCallback()
    {
        _calls = _freed = _usedAfterFree = 0;
        var n = new NativeNode(new NodeSettings { NodeName = "CBBUSY" });
        n.SetCallback(1 << 4, SlowPointer(), 0);
        var first = Task.Run(() => n.Inject(OptIn(1), "10.0.0.1", 60000));
        await Until(() => Volatile.Read(ref _calls) > 0);

        // The new callback keeps getting calls while the old one finishes; that must not keep SetCallback waiting.
        using var stop = new CancellationTokenSource();
        var busy = Task.Run(() =>
        {
            for (ushort id = 100; !stop.IsCancellationRequested; id++) n.Inject(OptIn(id), "10.0.1.1", 60000);
        });
        var replace = Task.Run(() => n.SetCallback(1 << 4, CountingPointer(), 0));
        await replace.WaitAsync(Limit);
        stop.Cancel();
        await Task.WhenAll(first, busy).WaitAsync(Limit);
    }

    [Fact]
    public async Task TwoCallbacksReplacingTheCallback_DoNotDeadlock()
    {
        _calls = 0;
        _both = new Barrier(2);
        _node = new NativeNode(new NodeSettings { NodeName = "CBBOTH" });
        _node.SetCallback(1 << 4, ReplacingPointer(), 0);
        var a = Task.Run(() => _node.Inject(OptIn(1), "10.0.0.1", 60000));
        var b = Task.Run(() => _node.Inject(OptIn(2), "10.0.0.2", 60000));
        await Task.WhenAll(a, b).WaitAsync(Limit);
        Assert.Equal(2, Volatile.Read(ref _calls));
    }

    [Fact]
    public async Task StopEndsAnEndlessRequestInsideACallback()
    {
        _calls = _freed = 0;
        _node = new NativeNode(new NodeSettings
        {
            NodeName = "CBREQ", LocalAddress = System.Net.IPAddress.Loopback, BroadcastAddress = System.Net.IPAddress.Loopback,
            ListenOnBroadcastPorts = false, AutoTimeSync = false,
        });
        _node.Start();
        _node.SetCallback(1 << 4, RequestingPointer(), 0);
        var ghost = new OptInPacket { NodeId = 8, NodeName = "GHOST", ListenerPort = 65535 }.ToArray();
        var inject = Task.Run(() => _node.Inject(ghost, "127.0.0.1", 65535));
        await Until(() => Volatile.Read(ref _calls) > 0);

        // What tcnet_node_destroy does: stop (ending the request), then remove the callback.
        await Task.Run(() =>
        {
            _node.Stop();
            _node.SetCallback(0, 0, 0);
        }).WaitAsync(Limit);
        await inject.WaitAsync(Limit);
        Assert.Equal(1, Volatile.Read(ref _freed));   // the request failed instead of waiting forever
    }
}
