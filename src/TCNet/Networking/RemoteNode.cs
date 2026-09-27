using System.Net;

namespace TCNet.Networking;

/// <summary>An entry in the population list and everything last received from it.</summary>
public sealed class RemoteNode
{
    internal RemoteNode(IPAddress address, ushort nodeId, bool isLocal)
    {
        Address = address;
        NodeId = nodeId;
        IsLocal = isLocal;
        FirstSeen = LastSeen = DateTime.UtcNow;
    }

    internal static string KeyOf(IPAddress a, ushort id) => $"{a}#{id}";

    /// <summary>"ip#nodeId".</summary>
    public string Key => KeyOf(Address, NodeId);

    public IPAddress Address { get; }
    public ushort NodeId { get; }
    public bool IsLocal { get; }
    public string NodeName { get; internal set; } = "";
    public NodeType NodeType { get; internal set; }
    public NodeOptions NodeOptions { get; internal set; }
    public Version ProtocolVersion { get; internal set; } = new(0, 0);

    /// <summary>Unicast listener port; 0 until known.</summary>
    public int ListenerPort { get; internal set; }
    public IPEndPoint? EndPoint => ListenerPort > 0 ? new IPEndPoint(Address, ListenerPort) : null;

    public ushort NodeCount { get; internal set; }
    public ushort Uptime { get; internal set; }
    public uint LastTimestamp { get; internal set; }
    public string VendorName { get; internal set; } = "";
    public string DeviceName { get; internal set; } = "";
    public string DeviceVersion { get; internal set; } = "";

    /// <summary>True once this node was seen with node type Auto (used by master election).</summary>
    public bool EverAuto { get; internal set; }

    /// <summary>When this node was first seen as Master in its current run as Master (null when not Master).</summary>
    public DateTime? MasterSince { get; internal set; }

    public DateTime FirstSeen { get; }
    public DateTime LastSeen { get; internal set; }
    public long Packets { get; internal set; }

    public OptInPacket? OptIn { get; internal set; }
    public StatusPacket? Status { get; internal set; }
    public TimePacket? Time { get; internal set; }
    public DateTime? TimeReceived { get; internal set; }
    public MixerDataPacket? Mixer { get; internal set; }

    /// <summary>Per layer (index = layer − 1).</summary>
    public MetricsPacket?[] Metrics { get; } = new MetricsPacket?[8];
    public MetadataPacket?[] Metadata { get; } = new MetadataPacket?[8];
    public CueDataPacket?[] Cues { get; } = new CueDataPacket?[8];
    public BeatGrid?[] BeatGrids { get; } = new BeatGrid?[8];
    public Waveform?[] SmallWaveforms { get; } = new Waveform?[8];
    public Waveform?[] BigWaveforms { get; } = new Waveform?[8];
    public byte[]?[] Artwork { get; } = new byte[]?[8];

    public TimeSyncResult? Sync { get; internal set; }
    public DateTime? SyncedAt { get; internal set; }

    public bool IsMasterOrRepeater => NodeType is NodeType.Master or NodeType.Repeater;

    public override string ToString() =>
        $"{NodeName}#{NodeId} {NodeType} {Address}:{ListenerPort} {VendorName} {DeviceName} {DeviceVersion}".TrimEnd();

    internal static int Index(byte layer) => layer is >= 1 and <= 8 ? layer - 1 : -1;
}
