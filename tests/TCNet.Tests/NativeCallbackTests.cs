using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TCNet.Native;
using TCNet.Networking;

namespace TCNet.Tests;

/// <summary>The C callback contract: set_callback (and destroy) return only after running callbacks have finished.</summary>
public class NativeCallbackTests
{
    private static int _calls, _freed, _usedAfterFree;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void SlowCallback(nint user, int kind, byte* json)
    {
        Interlocked.Increment(ref _calls);
        Thread.Sleep(300);
        if (Volatile.Read(ref _freed) != 0) Interlocked.Exchange(ref _usedAfterFree, 1);
    }

    private static unsafe nint SlowCallbackPointer() => (nint)(delegate* unmanaged[Cdecl]<nint, int, byte*, void>)&SlowCallback;

    [Fact]
    public async Task SetCallback_WaitsForRunningCallback()
    {
        _calls = _freed = _usedAfterFree = 0;
        var n = new NativeNode(new NodeSettings { NodeName = "CBTEST" });
        n.SetCallback(1 << 4 /* nodeDiscovered */, SlowCallbackPointer(), 0);
        var inject = Task.Run(() => n.Inject(new OptInPacket { NodeId = 7, NodeName = "PEER" }.ToArray(), "10.0.0.7", 60000));
        while (Volatile.Read(ref _calls) == 0) await Task.Delay(1);

        n.SetCallback(0, 0, 0);                 // must not return while SlowCallback is still running
        Interlocked.Exchange(ref _freed, 1);    // what C code would do next: free its user data
        await inject;
        Assert.Equal(0, Volatile.Read(ref _usedAfterFree));
    }
}
