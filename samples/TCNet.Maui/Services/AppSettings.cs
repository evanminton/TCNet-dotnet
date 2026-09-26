namespace TCNet.Maui.Services;

/// <summary>Persisted app settings (Preferences).</summary>
public sealed class AppSettings
{
    private static string S(string key, string def) => Preferences.Default.Get(key, def);
    private static int I(string key, int def) => Preferences.Default.Get(key, def);
    private static bool B(string key, bool def) => Preferences.Default.Get(key, def);

    public string NodeName { get => S(nameof(NodeName), "TCNETAPP"); set => Preferences.Default.Set(nameof(NodeName), value); }
    public int NodeId { get => I(nameof(NodeId), Random.Shared.Next(1, 65535)); set => Preferences.Default.Set(nameof(NodeId), value); }
    public NodeType Role { get => (NodeType)I(nameof(Role), (int)NodeType.Slave); set => Preferences.Default.Set(nameof(Role), (int)value); }
    public NodeOptions Options { get => (NodeOptions)I(nameof(Options), (int)NodeOptions.SupportsControlMessages); set => Preferences.Default.Set(nameof(Options), (int)value); }
    /// <summary>Empty = all interfaces.</summary>
    public string InterfaceAddress { get => S(nameof(InterfaceAddress), ""); set => Preferences.Default.Set(nameof(InterfaceAddress), value); }
    /// <summary>Empty = automatic.</summary>
    public string BroadcastAddress { get => S(nameof(BroadcastAddress), ""); set => Preferences.Default.Set(nameof(BroadcastAddress), value); }
    /// <summary>0 = first free from 65023.</summary>
    public int ListenerPort { get => I(nameof(ListenerPort), 0); set => Preferences.Default.Set(nameof(ListenerPort), value); }
    public bool AutoRequestMetadata { get => B(nameof(AutoRequestMetadata), true); set => Preferences.Default.Set(nameof(AutoRequestMetadata), value); }
    public bool AutoRequestMetrics { get => B(nameof(AutoRequestMetrics), true); set => Preferences.Default.Set(nameof(AutoRequestMetrics), value); }
    public bool AutoRequestWaveform { get => B(nameof(AutoRequestWaveform), true); set => Preferences.Default.Set(nameof(AutoRequestWaveform), value); }
    public bool AutoTimeSync { get => B(nameof(AutoTimeSync), true); set => Preferences.Default.Set(nameof(AutoTimeSync), value); }
    public bool AutoMasterElection { get => B(nameof(AutoMasterElection), false); set => Preferences.Default.Set(nameof(AutoMasterElection), value); }
    /// <summary>Act as a master with simulated layers (time stream, status, requests, control).</summary>
    public bool Simulate { get => B(nameof(Simulate), false); set => Preferences.Default.Set(nameof(Simulate), value); }
    public int TimeIntervalMs { get => I(nameof(TimeIntervalMs), 20); set => Preferences.Default.Set(nameof(TimeIntervalMs), value); }
    public bool LogTimePackets { get => B(nameof(LogTimePackets), false); set => Preferences.Default.Set(nameof(LogTimePackets), value); }
    public bool LogSent { get => B(nameof(LogSent), true); set => Preferences.Default.Set(nameof(LogSent), value); }

    public void EnsureNodeId()
    {
        if (!Preferences.Default.ContainsKey(nameof(NodeId))) NodeId = Random.Shared.Next(1, 65535);
    }
}
