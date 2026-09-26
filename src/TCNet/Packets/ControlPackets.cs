using System.Text;
using TCNet.Text;

namespace TCNet;

/// <summary>
/// Common layout of Control (101) and Text Data (128): STEP (24), DATA SIZE (26–29), text from byte 42.
/// </summary>
public abstract class TextPayloadPacket : TCNetPacket
{
    private string _text = "";
    private byte[] _bytes = [];

    public SyncStep Step { get; set; }

    /// <summary>
    /// The text payload (ASCII per spec; encoded as UTF-8, which is identical for ASCII). Received bytes that
    /// are not valid UTF-8 are shown as Latin-1; the raw bytes are kept and sent back unchanged.
    /// </summary>
    public string Text
    {
        get => _text;
        set { _text = value ?? ""; _bytes = Encoding.UTF8.GetBytes(_text); }
    }

    /// <summary>Data Size field as received (normally equal to the payload length).</summary>
    public uint DeclaredDataSize { get; private set; }

    public override int Length => TCNetConstants.PayloadHeaderSize + _bytes.Length;

    protected override void WriteBody(Span<byte> p)
    {
        p[24] = (byte)Step;
        Wire.U32(p, 26, (uint)_bytes.Length);
        _bytes.CopyTo(p[42..]);
    }

    protected internal override void ReadBody(ReadOnlySpan<byte> p, int receivedLength)
    {
        Step = (SyncStep)p[24];
        DeclaredDataSize = Wire.U32(p, 26);
        int available = Math.Max(0, receivedLength - 42);
        int n = DeclaredDataSize == 0 ? available : (int)Math.Min(DeclaredDataSize, (uint)available);
        var raw = p.Slice(42, n);
        var payload = raw;
        int nul = payload.IndexOf((byte)0);
        if (nul >= 0) payload = payload[..nul];
        _text = System.Text.Unicode.Utf8.IsValid(payload) ? Encoding.UTF8.GetString(payload) : Encoding.Latin1.GetString(payload);
        _bytes = raw.ToArray();
    }

    protected override void DescribeBody(List<TCNetField> f)
    {
        f.Add(new(24, 1, "STEP", ((byte)Step).ToString(), TCNetText.Describe(Step)));
        f.Add(new(26, 4, "Data Size", (ReceivedLength > 0 ? DeclaredDataSize : (uint)_bytes.Length).ToString()));
        f.Add(new(42, _bytes.Length, this is ControlPacket ? "Control Path" : "Text Data", Text));
    }

    public override string Summary => Text.Length > 80 ? Text[..80] + "…" : Text;
}

/// <summary>A single "path=value;" (or "path;") element of a control path string.</summary>
public readonly record struct ControlCommand(string Path, string? Value = null)
{
    public override string ToString() => Value is null ? $"{Path};" : $"{Path}={Value};";

    /// <summary>Splits "layer/1/state=6; layer/2/resync;" into commands.</summary>
    public static IReadOnlyList<ControlCommand> ParseAll(string controlPath)
    {
        var list = new List<ControlCommand>();
        foreach (var part in controlPath.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = part.IndexOf('=');
            list.Add(eq < 0 ? new ControlCommand(part) : new ControlCommand(part[..eq].Trim(), part[(eq + 1)..].Trim()));
        }
        return list;
    }

    /// <summary>Joins commands with a space: "layer/2/state=3; layer/2/resync;".</summary>
    public static string Join(IEnumerable<ControlCommand> commands) => string.Join(" ", commands.Select(c => c.ToString()));

    /// <summary>"layer/N/state=S;" – e.g. state 6 stops the layer.</summary>
    public static ControlCommand SetLayerState(int layer, LayerState state) => new($"layer/{layer}/state", ((byte)state).ToString());

    /// <summary>"layer/N/source=S;" – e.g. layer/5/source=1 sets layer A to follow layer 1.</summary>
    public static ControlCommand SetLayerSource(int layer, int sourceLayer) => new($"layer/{layer}/source", sourceLayer.ToString());

    /// <summary>"layer/N/resync;".</summary>
    public static ControlCommand Resync(int layer) => new($"layer/{layer}/resync");

    /// <summary>If the path is "layer/N/…", returns N.</summary>
    public int? LayerNumber
    {
        get
        {
            var parts = Path.Split('/');
            return parts.Length >= 2 && parts[0].Equals("layer", StringComparison.OrdinalIgnoreCase) && int.TryParse(parts[1], out int n) ? n : null;
        }
    }
}

/// <summary>Type 101 – Control (42 + data size). Remote-control a node with control path strings.</summary>
public sealed class ControlPacket : TextPayloadPacket
{
    public ControlPacket() { }

    public ControlPacket(params ControlCommand[] commands) => Text = ControlCommand.Join(commands);

    public override MessageType MessageType => MessageType.Control;
    public override string Name => "Control";

    /// <summary>The control path parsed into commands.</summary>
    public IReadOnlyList<ControlCommand> Commands => ControlCommand.ParseAll(Text);
}

/// <summary>Type 128 – Text Data (42 + data size).</summary>
public sealed class TextDataPacket : TextPayloadPacket
{
    public TextDataPacket() { }
    public TextDataPacket(string text) => Text = text;

    public override MessageType MessageType => MessageType.TextData;
    public override string Name => "Text Data";
}

/// <summary>Type 132 – Keyboard Data (44 bytes). Two bytes of key data at 42.</summary>
public sealed class KeyboardDataPacket : TCNetPacket
{
    public override MessageType MessageType => MessageType.KeyboardData;
    public override string Name => "Keyboard Data";
    public override int Length => TCNetConstants.KeyboardDataSize;

    /// <summary>The two key bytes at offset 42–43 ("HEX ASCII Code").</summary>
    public byte[] KeyData { get; } = new byte[2];

    /// <summary>Key data as a little-endian 16-bit code.</summary>
    public ushort KeyCode
    {
        get => (ushort)(KeyData[0] | KeyData[1] << 8);
        set { KeyData[0] = (byte)value; KeyData[1] = (byte)(value >> 8); }
    }

    /// <summary>Sets an ASCII key (first byte = code, second = 0).</summary>
    public static KeyboardDataPacket ForKey(char key) => new() { KeyCode = key <= 0xFF ? key : '?' };

    protected override void WriteBody(Span<byte> p)
    {
        Wire.U32(p, 26, 2);
        p[42] = KeyData[0];
        p[43] = KeyData[1];
    }

    protected internal override void ReadBody(ReadOnlySpan<byte> p, int receivedLength)
    {
        KeyData[0] = p[42];
        KeyData[1] = p[43];
    }

    protected override void DescribeBody(List<TCNetField> f)
    {
        f.Add(new(26, 4, "Data Size", "2"));
        f.Add(new(42, 2, "Keyboard Data", $"0x{KeyData[0]:X2} 0x{KeyData[1]:X2}", KeyData[0] is >= 0x20 and < 0x7F ? $"'{(char)KeyData[0]}'" : null));
    }

    public override string Summary => $"key 0x{KeyCode:X4}";
}

/// <summary>
/// Type 30 (and 213) – Application Specific Data. Non-public data between applications, identified by a
/// two-byte application code (see <see cref="TCNetText.ApplicationCodes"/>). May be split over several packets.
/// </summary>
public sealed class ApplicationDataPacket : TCNetPacket
{
    private MessageType _type = MessageType.ApplicationData;

    public override MessageType MessageType => _type;

    /// <summary>Sets the message type: <see cref="MessageType.ApplicationData"/> (30) or <see cref="MessageType.ApplicationSpecificData"/> (213).</summary>
    public void SetMessageType(MessageType type)
    {
        if (type is not (MessageType.ApplicationData or MessageType.ApplicationSpecificData))
            throw new ArgumentOutOfRangeException(nameof(type));
        _type = type;
    }

    public override string Name => "Application Specific Data";

    public byte DataIdentifier1 { get; set; }
    public byte DataIdentifier2 { get; set; }

    /// <summary>Application code, e.g. 0x0AAA for ShowKontrol (identifier 1 = high byte).</summary>
    public ushort ApplicationCode
    {
        get => (ushort)(DataIdentifier1 << 8 | DataIdentifier2);
        set { DataIdentifier1 = (byte)(value >> 8); DataIdentifier2 = (byte)value; }
    }

    /// <summary>Data size of all packets combined.</summary>
    public uint DataSize { get; set; }
    public uint TotalPackets { get; set; } = 1;
    public uint PacketNumber { get; set; }
    public uint Signature { get; set; } = TCNetConstants.ApplicationDataSignature;

    /// <summary>This packet's data (bytes 42…).</summary>
    public byte[] Payload { get; set; } = [];

    public override int Length => TCNetConstants.PayloadHeaderSize + Payload.Length;

    protected override void WriteBody(Span<byte> p)
    {
        p[24] = DataIdentifier1;
        p[25] = DataIdentifier2;
        Wire.U32(p, 26, DataSize == 0 ? (uint)Payload.Length : DataSize);
        Wire.U32(p, 30, TotalPackets);
        Wire.U32(p, 34, PacketNumber);
        Wire.U32(p, 38, Signature);
        Payload.CopyTo(p[42..]);
    }

    protected internal override void ReadBody(ReadOnlySpan<byte> p, int receivedLength)
    {
        _type = (MessageType)p[7];
        DataIdentifier1 = p[24];
        DataIdentifier2 = p[25];
        DataSize = Wire.U32(p, 26);
        TotalPackets = Wire.U32(p, 30);
        PacketNumber = Wire.U32(p, 34);
        Signature = Wire.U32(p, 38);
        Payload = p.Slice(42, Math.Max(0, receivedLength - 42)).ToArray();
    }

    protected override void DescribeBody(List<TCNetField> f)
    {
        f.Add(new(24, 2, "Data Identifier", $"0x{ApplicationCode:X4}", TCNetText.ApplicationVendor(ApplicationCode)));
        f.Add(new(26, 4, "Data Size", DataSize.ToString()));
        f.Add(new(30, 4, "Total Packets", TotalPackets.ToString()));
        f.Add(new(34, 4, "Packet No", PacketNumber.ToString()));
        f.Add(new(38, 4, "Packet Signature", Signature.ToString(), Signature == TCNetConstants.ApplicationDataSignature ? "standard" : "non-standard"));
        f.Add(new(42, Payload.Length, "Data", Convert.ToHexString(Payload.AsSpan(0, Math.Min(32, Payload.Length))) + (Payload.Length > 32 ? "…" : "")));
    }

    public override string Summary => $"app 0x{ApplicationCode:X4}, packet {PacketNumber + 1}/{TotalPackets}, {Payload.Length} bytes";
}
