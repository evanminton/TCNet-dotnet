using System.Net;

namespace TCNet.Networking;

/// <summary>An entry of the population list: a remote node and the latest data received from it.</summary>
public sealed class TCNetRemoteNode
{
    internal TCNetRemoteNode(IPAddress address, ushort nodeId)
    {
        Address = address;
        NodeId = nodeId;
        FirstSeen = LastSeen = DateTime.UtcNow;
    }

    /// <summary>Population list key: "ip#nodeId".</summary>
    public string Key => MakeKey(Address, NodeId);

    internal static string MakeKey(IPAddress address, ushort nodeId) => $"{address}#{nodeId}";

    public IPAddress Address { get; }
    public ushort NodeId { get; }
    public string NodeName { get; internal set; } = "";
    public NodeType NodeType { get; internal set; }
    public NodeOptions NodeOptions { get; internal set; }
    public Version ProtocolVersion { get; internal set; } = new(0, 0);

    /// <summary>Unicast listener port (from Opt-IN / Status / Time Sync), 0 if unknown.</summary>
    public int ListenerPort { get; internal set; }

    public IPEndPoint? EndPoint => ListenerPort > 0 ? new IPEndPoint(Address, ListenerPort) : null;

    public ushort NodeCount { get; internal set; }
    public ushort Uptime { get; internal set; }
    public string VendorName { get; internal set; } = "";
    public string ApplicationName { get; internal set; } = "";
    public string ApplicationVersion { get; internal set; } = "";

    /// <summary>Last header timestamp received (sender's µs timer).</summary>
    public uint LastTimestamp { get; internal set; }

    public DateTime FirstSeen { get; }
    public DateTime LastSeen { get; internal set; }
    public DateTime? LastOptIn { get; internal set; }

    /// <summary>
    /// True once the node has been seen as Auto. A master that was Auto before took the role by election;
    /// one never seen as Auto is treated as configured as Master.
    /// </summary>
    public bool SeenAsAuto { get; internal set; }

    /// <summary>True when the node runs on this machine.</summary>
    public bool IsLocal { get; internal set; }

    public long PacketsReceived { get; internal set; }

    // ---- Latest data ----

    public OptInPacket? LastOptInPacket { get; internal set; }
    public StatusPacket? LastStatus { get; internal set; }
    public TimePacket? LastTime { get; internal set; }
    public DateTime? LastTimeReceived { get; internal set; }
    public MixerDataPacket? LastMixer { get; internal set; }

    /// <summary>Per layer (index = layer − 1): latest metrics.</summary>
    public MetricsDataPacket?[] Metrics { get; } = new MetricsDataPacket?[8];
    public MetadataPacket?[] Metadata { get; } = new MetadataPacket?[8];
    public CueDataPacket?[] Cues { get; } = new CueDataPacket?[8];
    public Waveform?[] SmallWaveforms { get; } = new Waveform?[8];
    public Waveform?[] BigWaveforms { get; } = new Waveform?[8];
    public BeatGrid?[] BeatGrids { get; } = new BeatGrid?[8];
    public byte[]?[] Artwork { get; } = new byte[]?[8];

    // ---- Time sync ----

    /// <summary>Remote timer − local timer in µs, from the last time sync.</summary>
    public int? ClockOffsetMicros { get; internal set; }

    /// <summary>One-way delay estimate in µs from the last time sync.</summary>
    public uint? DelayMicros { get; internal set; }

    public DateTime? LastTimeSync { get; internal set; }

    /// <summary>Remote node's current timer estimated from the last sync.</summary>
    public uint? EstimatedRemoteTimestamp(uint localNow) =>
        ClockOffsetMicros is { } o ? TCNetClock.Add(localNow, o) : null;

    public bool IsMasterOrRepeater => NodeType is NodeType.Master or NodeType.Repeater;

    public override string ToString() =>
        $"{NodeName}#{NodeId} {NodeType} {Address}:{ListenerPort} {VendorName} {ApplicationName} {ApplicationVersion}".TrimEnd();

    internal static int LayerIndex(byte layer) => layer is >= 1 and <= 8 ? layer - 1 : -1;
}
