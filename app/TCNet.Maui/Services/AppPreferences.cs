namespace TCNet.Maui.Services;

/// <summary>Settings persisted with MAUI Preferences.</summary>
public sealed class AppPreferences
{
    private static IPreferences P => Microsoft.Maui.Storage.Preferences.Default;

    public AppPreferences()
    {
        if (!P.ContainsKey(nameof(NodeId))) NodeId = Random.Shared.Next(1, 65535);
    }

    public string NodeName { get => P.Get(nameof(NodeName), "TCNETAPP"); set => P.Set(nameof(NodeName), value); }
    public int NodeId { get => P.Get(nameof(NodeId), 1); set => P.Set(nameof(NodeId), value); }
    public NodeType Role { get => (NodeType)P.Get(nameof(Role), (int)NodeType.Slave); set => P.Set(nameof(Role), (int)value); }
    public NodeOptions Options { get => (NodeOptions)P.Get(nameof(Options), (int)NodeOptions.SupportsControl); set => P.Set(nameof(Options), (int)value); }
    /// <summary>Empty = all interfaces.</summary>
    public string Interface { get => P.Get(nameof(Interface), ""); set => P.Set(nameof(Interface), value); }
    /// <summary>Empty = automatic.</summary>
    public string Broadcast { get => P.Get(nameof(Broadcast), ""); set => P.Set(nameof(Broadcast), value); }
    /// <summary>0 = first free from 65023.</summary>
    public int ListenerPort { get => P.Get(nameof(ListenerPort), 0); set => P.Set(nameof(ListenerPort), value); }
    public bool AutoMetadata { get => P.Get(nameof(AutoMetadata), true); set => P.Set(nameof(AutoMetadata), value); }
    public bool AutoMetrics { get => P.Get(nameof(AutoMetrics), true); set => P.Set(nameof(AutoMetrics), value); }
    public bool AutoWaveform { get => P.Get(nameof(AutoWaveform), true); set => P.Set(nameof(AutoWaveform), value); }
    public bool AutoTimeSync { get => P.Get(nameof(AutoTimeSync), true); set => P.Set(nameof(AutoTimeSync), value); }
    public bool Election { get => P.Get(nameof(Election), false); set => P.Set(nameof(Election), value); }
    public bool Simulate { get => P.Get(nameof(Simulate), false); set => P.Set(nameof(Simulate), value); }
    public int TimeIntervalMs { get => P.Get(nameof(TimeIntervalMs), 20); set => P.Set(nameof(TimeIntervalMs), value); }
    public bool LogTime { get => P.Get(nameof(LogTime), false); set => P.Set(nameof(LogTime), value); }
    public bool LogSent { get => P.Get(nameof(LogSent), true); set => P.Set(nameof(LogSent), value); }
}
