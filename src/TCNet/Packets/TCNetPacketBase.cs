using System.Text;
using TCNet.Text;

namespace TCNet;

/// <summary>One decoded field of a packet: byte offset, size, name, formatted value and (optionally) meaning.</summary>
public readonly record struct TCNetField(int Offset, int Size, string Name, string Value, string? Meaning = null)
{
    public override string ToString() =>
        Meaning is null ? $"[{Offset,4}+{Size}] {Name} = {Value}" : $"[{Offset,4}+{Size}] {Name} = {Value} ({Meaning})";
}

/// <summary>
/// Base class for every TCNet packet. Holds the 24-byte management header that starts every packet:
/// Node ID (0–1), protocol version (2–3), "TCN" (4–6), message type (7), node name (8–15), SEQ (16),
/// node type (17), node options (18–19) and timestamp in µs (20–23).
/// </summary>
public abstract class TCNetPacket
{
    /// <summary>Unique node ID of the sender (must be unique per IP).</summary>
    public ushort NodeId { get; set; }

    public byte ProtocolMajor { get; set; } = TCNetConstants.ProtocolVersionMajor;
    public byte ProtocolMinor { get; set; } = TCNetConstants.ProtocolVersionMinor;

    /// <summary>Message type written at byte 7.</summary>
    public abstract MessageType MessageType { get; }

    /// <summary>Node name / GW code, up to 8 ASCII characters.</summary>
    public string NodeName { get; set; } = "";

    /// <summary>Sequence number (0–255).</summary>
    public byte Sequence { get; set; }

    public NodeType NodeType { get; set; } = NodeType.Slave;

    public NodeOptions NodeOptions { get; set; }

    /// <summary>Sender's timer in microseconds (0–999999).</summary>
    public uint Timestamp { get; set; }

    /// <summary>Number of bytes actually received when this packet was parsed (0 for packets built locally).</summary>
    public int ReceivedLength { get; internal set; }

    /// <summary>True when the received datagram was shorter than the spec size and was zero-padded to parse.</summary>
    public bool WasPadded => ReceivedLength > 0 && ReceivedLength < Length;

    /// <summary>Encoded size in bytes.</summary>
    public abstract int Length { get; }

    /// <summary>Spec name, e.g. "Opt-IN", "Data – Metrics".</summary>
    public abstract string Name { get; }

    public Version ProtocolVersion => new(ProtocolMajor, ProtocolMinor);

    /// <summary>Metadata strings are UTF-16 from protocol 3.5.0; UTF-8 before.</summary>
    public bool UsesUtf16Text => ProtocolMajor > 3 || (ProtocolMajor == 3 && ProtocolMinor >= 5);

    /// <summary>Encodes the packet into a new array.</summary>
    public byte[] ToArray()
    {
        var buffer = new byte[Length];
        WriteTo(buffer);
        return buffer;
    }

    /// <summary>Encodes the packet into <paramref name="destination"/> and returns the number of bytes written.</summary>
    public int WriteTo(Span<byte> destination)
    {
        int length = Length;
        if (destination.Length < length)
            throw new ArgumentException($"{Name} needs {length} bytes, destination has {destination.Length}.", nameof(destination));
        var p = destination[..length];
        p.Clear();
        Wire.U16(p, 0, NodeId);
        p[2] = ProtocolMajor;
        p[3] = ProtocolMinor;
        TCNetConstants.HeaderMagic.CopyTo(p[4..]);
        p[7] = (byte)MessageType;
        Wire.Ascii(p, 8, TCNetConstants.NodeNameLength, NodeName);
        p[16] = Sequence;
        p[17] = (byte)NodeType;
        Wire.U16(p, 18, (ushort)NodeOptions);
        Wire.U32(p, 20, Timestamp);
        WriteBody(p);
        return length;
    }

    /// <summary>Writes bytes 24 and up. <paramref name="p"/> is exactly <see cref="Length"/> bytes and already zeroed.</summary>
    protected abstract void WriteBody(Span<byte> p);

    /// <summary>Reads bytes 24 and up. <paramref name="p"/> is at least <see cref="Length"/> of a fresh instance (zero padded).</summary>
    /// <param name="p">Packet bytes.</param>
    /// <param name="receivedLength">Real datagram length (variable-length packets use it to size their payload).</param>
    protected internal abstract void ReadBody(ReadOnlySpan<byte> p, int receivedLength);

    internal void ReadHeader(ReadOnlySpan<byte> p)
    {
        NodeId = Wire.U16(p, 0);
        ProtocolMajor = p[2];
        ProtocolMinor = p[3];
        NodeName = Wire.Ascii(p, 8, TCNetConstants.NodeNameLength);
        Sequence = p[16];
        NodeType = (NodeType)p[17];
        NodeOptions = (NodeOptions)Wire.U16(p, 18);
        Timestamp = Wire.U32(p, 20);
    }

    /// <summary>Copies every management header field except the message type.</summary>
    public void CopyHeaderFrom(TCNetPacket other)
    {
        NodeId = other.NodeId;
        ProtocolMajor = other.ProtocolMajor;
        ProtocolMinor = other.ProtocolMinor;
        NodeName = other.NodeName;
        Sequence = other.Sequence;
        NodeType = other.NodeType;
        NodeOptions = other.NodeOptions;
        Timestamp = other.Timestamp;
    }

    /// <summary>Every field in wire order, with formatted values and option meanings.</summary>
    public IReadOnlyList<TCNetField> Describe()
    {
        var f = new List<TCNetField>
        {
            new(0, 2, "Node ID", NodeId.ToString()),
            new(2, 2, "Protocol Version", $"{ProtocolMajor}.{ProtocolMinor}"),
            new(4, 3, "Header", "TCN"),
            new(7, 1, "Message Type", ((byte)MessageType).ToString(), TCNetText.Describe(MessageType)),
            new(8, 8, "Node Name", NodeName),
            new(16, 1, "SEQ", Sequence.ToString()),
            new(17, 1, "Node Type", ((byte)NodeType).ToString(), TCNetText.Describe(NodeType)),
            new(18, 2, "Node Options", ((ushort)NodeOptions).ToString(), TCNetText.DescribeFlags(NodeOptions)),
            new(20, 4, "Timestamp", $"{Timestamp} µs"),
        };
        DescribeBody(f);
        return f;
    }

    /// <summary>Appends fields for bytes 24 and up.</summary>
    protected abstract void DescribeBody(List<TCNetField> f);

    /// <summary>A one-line summary of the packet's payload.</summary>
    public virtual string Summary => "";

    /// <summary>Multi-line field listing.</summary>
    public string ToDisplayString()
    {
        var sb = new StringBuilder();
        sb.Append(Name).Append(" (").Append(Length).AppendLine(" bytes)");
        foreach (var field in Describe()) sb.Append("  ").AppendLine(field.ToString());
        return sb.ToString();
    }

    public override string ToString()
    {
        var s = $"{Name} from {NodeName}#{NodeId} ({NodeType})";
        var summary = Summary;
        return string.IsNullOrEmpty(summary) ? s : $"{s}: {summary}";
    }

    // ---- Parsing entry points (forward to TCNetPacketParser) ----

    /// <inheritdoc cref="TCNetPacketParser.TryParse(ReadOnlySpan{byte}, out TCNetPacket?, out string?)"/>
    public static bool TryParse(ReadOnlySpan<byte> data, out TCNetPacket? packet, out string? error) =>
        TCNetPacketParser.TryParse(data, out packet, out error);

    /// <inheritdoc cref="TCNetPacketParser.Parse(ReadOnlySpan{byte})"/>
    public static TCNetPacket Parse(ReadOnlySpan<byte> data) => TCNetPacketParser.Parse(data);
}
