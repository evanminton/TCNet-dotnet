using System.Net;

namespace TCNet.Networking;

/// <summary>Configuration of a local <see cref="TCNetNode"/>.</summary>
public sealed class TCNetNodeSettings
{
    /// <summary>Node ID; must be unique among nodes on the same IP. Random by default.</summary>
    public ushort NodeId { get; set; } = (ushort)Random.Shared.Next(1, ushort.MaxValue);

    /// <summary>Node name / GW code (max 8 ASCII characters).</summary>
    public string NodeName { get; set; } = "DOTNET";

    /// <summary>Initial role. With <see cref="AutoMasterElection"/> an Auto node may promote itself to Master.</summary>
    public NodeType NodeType { get; set; } = NodeType.Slave;

    public NodeOptions NodeOptions { get; set; } = NodeOptions.SupportsControlMessages;

    public string VendorName { get; set; } = "TCNet.NET";
    public string ApplicationName { get; set; } = "TCNet .NET";
    public byte ApplicationMajorVersion { get; set; } = 2;
    public byte ApplicationMinorVersion { get; set; }
    public byte ApplicationBugVersion { get; set; }

    public byte ProtocolMajor { get; set; } = TCNetConstants.ProtocolVersionMajor;
    public byte ProtocolMinor { get; set; } = TCNetConstants.ProtocolVersionMinor;

    /// <summary>Unicast listener port. 0 = first free port from 65023 to 65535.</summary>
    public int ListenerPort { get; set; }

    /// <summary>Local interface address. <see cref="IPAddress.Any"/> = all interfaces.</summary>
    public IPAddress LocalAddress { get; set; } = IPAddress.Any;

    /// <summary>Broadcast destination. Null = directed broadcast of <see cref="LocalAddress"/>'s subnet, or 255.255.255.255.</summary>
    public IPAddress? BroadcastAddress { get; set; }

    /// <summary>Listen on 60000/60001/60002 (shared with other TCNet software on the same machine).</summary>
    public bool ListenOnBroadcastPorts { get; set; } = true;

    /// <summary>Opt-IN period (spec: 1000 ms).</summary>
    public TimeSpan OptInInterval { get; set; } = TCNetConstants.OptInInterval;

    /// <summary>Nodes not heard from for this long are removed from the population list.</summary>
    public TimeSpan NodeTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Unicast the Opt-IN to every discovered node too (spec Opt-IN usage step 3).</summary>
    public bool UnicastOptInToKnownNodes { get; set; } = true;

    /// <summary>Broadcast a Status packet every Opt-IN period. Null = only when Master or Repeater.</summary>
    public bool? SendStatus { get; set; }

    /// <summary>Answer Time Sync step 0 with step 1.</summary>
    public bool AnswerTimeSync { get; set; } = true;

    /// <summary>Periodically time-sync with every node.</summary>
    public bool AutoTimeSync { get; set; } = true;

    public TimeSpan TimeSyncInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Request metadata automatically when a node's layer track ID changes.</summary>
    public bool AutoRequestMetadata { get; set; }

    /// <summary>Request metrics automatically when a node's layer track ID or state changes.</summary>
    public bool AutoRequestMetrics { get; set; }

    /// <summary>When a Master leaves, an Auto node with the highest uptime becomes Master.</summary>
    public bool AutoMasterElection { get; set; }

    /// <summary>Also process packets this node sent itself (looped back by broadcast).</summary>
    public bool ReceiveOwnPackets { get; set; }

    /// <summary>Default timeout of requests, time sync and control round trips.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(2);

    public TCNetNodeSettings Clone() => (TCNetNodeSettings)MemberwiseClone();
}
