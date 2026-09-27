using System.Net;

namespace TCNet.Networking;

/// <summary>Configuration of a local <see cref="TCNetNode"/>.</summary>
public sealed class NodeSettings
{
    /// <summary>Unique per IP (several applications on one machine need different IDs). Random by default.</summary>
    public ushort NodeId { get; set; } = (ushort)Random.Shared.Next(1, ushort.MaxValue);

    /// <summary>Node name / GW code, up to 8 characters.</summary>
    public string NodeName { get; set; } = "TCNETNET";

    public NodeType NodeType { get; set; } = NodeType.Slave;
    public NodeOptions NodeOptions { get; set; } = NodeOptions.SupportsControl;

    public string VendorName { get; set; } = "TCNet.NET";
    public string DeviceName { get; set; } = "TCNet .NET";
    public byte DeviceMajor { get; set; } = 3;
    public byte DeviceMinor { get; set; }
    public byte DeviceBug { get; set; }

    public byte VersionMajor { get; set; } = TCNetConstants.ProtocolMajor;
    public byte VersionMinor { get; set; } = TCNetConstants.ProtocolMinor;

    /// <summary>Unicast listener port; 0 picks the first free port from 65023.</summary>
    public int ListenerPort { get; set; }

    /// <summary>Interface to use; Any = all.</summary>
    public IPAddress LocalAddress { get; set; } = IPAddress.Any;

    /// <summary>Broadcast destination; null = the interface's directed broadcast, or 255.255.255.255.</summary>
    public IPAddress? BroadcastAddress { get; set; }

    /// <summary>Listen on the shared broadcast ports 60000, 60001 and 60002.</summary>
    public bool ListenOnBroadcastPorts { get; set; } = true;

    public TimeSpan OptInInterval { get; set; } = TCNetConstants.OptInInterval;

    /// <summary>Nodes silent this long are removed from the population list.</summary>
    public TimeSpan NodeTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Also unicast Opt-IN to every known node (Opt-IN usage step 3).</summary>
    public bool UnicastOptIn { get; set; } = true;

    /// <summary>Broadcast Status each Opt-IN period. Null = when Master or Repeater.</summary>
    public bool? SendStatus { get; set; }

    public bool AnswerTimeSync { get; set; } = true;

    /// <summary>Time sync with every node periodically.</summary>
    public bool AutoTimeSync { get; set; } = true;
    public TimeSpan TimeSyncInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Request metadata when a node's layer track ID changes (seen in Status).</summary>
    public bool AutoRequestMetadata { get; set; }

    /// <summary>Request metrics when a node's layer track ID or state changes (seen in Status).</summary>
    public bool AutoRequestMetrics { get; set; }

    /// <summary>When the master leaves, an Auto node may become master (Opt-OUT tip).</summary>
    public bool AutoMasterElection { get; set; }

    /// <summary>Process packets this node sent (looped-back broadcasts).</summary>
    public bool ReceiveOwnPackets { get; set; }

    /// <summary>Default wait for requests, time sync and control; <see cref="Timeout.InfiniteTimeSpan"/> waits forever.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(2);

    public NodeSettings Clone() => (NodeSettings)MemberwiseClone();
}
