using System.Text;
using TCNet.Text;

namespace TCNet;

/// <summary>One decoded field: offset, size, name, value and (optionally) its meaning.</summary>
public readonly record struct TCNetField(int Offset, int Size, string Name, string Value, string? Meaning = null)
{
    public override string ToString() =>
        Meaning is null ? $"[{Offset,4}+{Size}] {Name} = {Value}" : $"[{Offset,4}+{Size}] {Name} = {Value}  ({Meaning})";
}

/// <summary>
/// Base of every TCNet packet. Holds the 24-byte management header: Node ID (0), protocol version (2–3), "TCN" (4),
/// message type (7), node name (8–15), SEQ (16), node type (17), node options (18) and timestamp µs (20).
/// </summary>
public abstract class TCNetPacket
{
    public ushort NodeId { get; set; }
    public byte VersionMajor { get; set; } = TCNetConstants.ProtocolMajor;
    public byte VersionMinor { get; set; } = TCNetConstants.ProtocolMinor;
    public abstract MessageType MessageType { get; }
    public string NodeName { get; set; } = "";
    public byte Sequence { get; set; }
    public NodeType NodeType { get; set; } = NodeType.Slave;
    public NodeOptions NodeOptions { get; set; }

    /// <summary>Sender's timer, 0–999999 µs.</summary>
    public uint Timestamp { get; set; }

    /// <summary>Encoded length in bytes.</summary>
    public abstract int Length { get; }

    /// <summary>Spec name, e.g. "Opt-IN" or "Data · Metrics".</summary>
    public abstract string Name { get; }

    /// <summary>Datagram length when parsed (0 when built locally).</summary>
    public int ReceivedLength { get; internal set; }

    /// <summary>The datagram was shorter than the spec size and was zero-padded for parsing.</summary>
    public bool WasPadded => ReceivedLength > 0 && ReceivedLength < Length;

    public Version ProtocolVersion => new(VersionMajor, VersionMinor);

    /// <summary>Protocol 3.5 and later carry metadata text as UTF-16.</summary>
    public bool IsUtf16Era => VersionMajor > 3 || (VersionMajor == 3 && VersionMinor >= 5);

    public byte[] ToArray()
    {
        var b = new byte[Length];
        WriteTo(b);
        return b;
    }

    public int WriteTo(Span<byte> destination)
    {
        int len = Length;
        if (destination.Length < len) throw new ArgumentException($"{Name} needs {len} bytes.", nameof(destination));
        var p = destination[..len];
        p.Clear();
        Wire.PutU16(p, 0, NodeId);
        p[2] = VersionMajor;
        p[3] = VersionMinor;
        TCNetConstants.Magic.CopyTo(p[4..]);
        p[7] = (byte)MessageType;
        Wire.PutAscii(p, 8, TCNetConstants.NodeNameLength, NodeName);
        p[16] = Sequence;
        p[17] = (byte)NodeType;
        Wire.PutU16(p, 18, (ushort)NodeOptions);
        Wire.PutU32(p, 20, Timestamp);
        Encode(p);
        return len;
    }

    /// <summary>Writes bytes 24+ into a zeroed buffer of exactly <see cref="Length"/> bytes.</summary>
    protected abstract void Encode(Span<byte> p);

    /// <summary>Reads bytes 24+. <paramref name="p"/> is at least as long as a fresh instance's Length (zero padded).</summary>
    protected internal abstract void Decode(ReadOnlySpan<byte> p, int datagramLength);

    internal void DecodeHeader(ReadOnlySpan<byte> p)
    {
        NodeId = Wire.U16(p, 0);
        VersionMajor = p[2];
        VersionMinor = p[3];
        NodeName = Wire.Ascii(p, 8, TCNetConstants.NodeNameLength);
        Sequence = p[16];
        NodeType = (NodeType)p[17];
        NodeOptions = (NodeOptions)Wire.U16(p, 18);
        Timestamp = Wire.U32(p, 20);
    }

    /// <summary>Every field in wire order with values and meanings.</summary>
    public IReadOnlyList<TCNetField> Describe()
    {
        var f = new List<TCNetField>
        {
            new(0, 2, "Node ID", NodeId.ToString()),
            new(2, 2, "Protocol Version", $"{VersionMajor}.{VersionMinor}"),
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

    protected abstract void DescribeBody(List<TCNetField> f);

    /// <summary>One-line summary of the payload.</summary>
    public virtual string Summary => "";

    /// <summary>Name, length and every field on its own line.</summary>
    public string ToDisplayString()
    {
        var sb = new StringBuilder().Append(Name).Append(" · ").Append(Length).AppendLine(" bytes");
        foreach (var field in Describe()) sb.Append("  ").AppendLine(field.ToString());
        return sb.ToString();
    }

    public override string ToString() =>
        $"{Name} from {NodeName}#{NodeId} ({NodeType})" + (Summary.Length > 0 ? ": " + Summary : "");

    public static bool TryParse(ReadOnlySpan<byte> datagram, out TCNetPacket? packet, out string? error) =>
        TCNetParser.TryParse(datagram, out packet, out error);

    public static TCNetPacket Parse(ReadOnlySpan<byte> datagram) => TCNetParser.Parse(datagram);
}
