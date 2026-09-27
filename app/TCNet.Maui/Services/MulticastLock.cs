namespace TCNet.Maui.Services;

/// <summary>Android drops broadcast/multicast on Wi-Fi unless a multicast lock is held.</summary>
public static class MulticastLock
{
#if ANDROID
    private static Android.Net.Wifi.WifiManager.MulticastLock? _lock;
#endif

    public static void Acquire()
    {
#if ANDROID
        if (_lock is not null) return;
        var wifi = Android.App.Application.Context.GetSystemService(Android.Content.Context.WifiService) as Android.Net.Wifi.WifiManager;
        _lock = wifi?.CreateMulticastLock("tcnet");
        _lock?.SetReferenceCounted(false);
        _lock?.Acquire();
#endif
    }

    public static void Release()
    {
#if ANDROID
        _lock?.Release();
        _lock = null;
#endif
    }
}
