using System.Text;
using TCNet.Text;

namespace TCNet;

/// <summary>
/// Control (101) and Text Data (128): STEP (24), Data Size (26–29), payload from 42.
/// The payload bytes are kept exactly as received; <see cref="Text"/> decodes them (UTF-8, falling back to Latin-1).
/// </summary>
public abstract class TextPacket : TCNetPacket
{
    private byte[] _payload = [];

    public Step Step { get; set; }

    /// <summary>Raw payload bytes.</summary>
    public byte[] Payload
    {
        get => _payload;
        set => _payload = value ?? [];
    }

    /// <summary>Payload as text. Setting encodes as UTF-8 (identical to ASCII for ASCII text).</summary>
    public string Text
    {
        get => DecodeText(_payload);
        set => _payload = Encoding.UTF8.GetBytes(value ?? "");
    }

    /// <summary>Data Size as received.</summary>
    public uint DeclaredSize { get; private set; }

    /// <summary>The datagram ended before the declared Data Size; <see cref="Payload"/> holds only what arrived.</summary>
    public bool Truncated { get; private set; }

    public override int Length => TCNetConstants.PayloadOffset + _payload.Length;

    private static string DecodeText(byte[] b)
    {
        int n = Array.IndexOf(b, (byte)0);
        var span = n >= 0 ? b.AsSpan(0, n) : b.AsSpan();
        try { return new UTF8Encoding(false, true).GetString(span); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(span); }
    }

    protected override void Encode(Span<byte> p)
    {
        p[24] = (byte)Step;
        Wire.PutU32(p, 26, (uint)_payload.Length);
        _payload.CopyTo(p[42..]);
    }

    protected internal override void Decode(ReadOnlySpan<byte> p, int datagramLength)
    {
        Step = (Step)p[24];
        DeclaredSize = Wire.U32(p, 26);
        int available = Math.Max(0, datagramLength - 42);
        int n = DeclaredSize == 0 ? available : (int)Math.Min(DeclaredSize, (uint)available);
        Truncated = DeclaredSize > (uint)available;
        _payload = p.Slice(42, n).ToArray();
    }

    protected override void DescribeBody(List<TCNetField> f)
    {
        f.Add(new(24, 1, "STEP", ((byte)Step).ToString(), TCNetText.Describe(Step)));
        f.Add(Truncated
            ? new(26, 4, "Data Size", DeclaredSize.ToString(), $"truncated: {_payload.Length} bytes received")
            : new(26, 4, "Data Size", _payload.Length.ToString()));
        f.Add(new(42, _payload.Length, this is ControlPacket ? "Control Path" : "Text Data", Text));
    }

    public override string Summary => Text.Length > 80 ? Text[..80] + "…" : Text;
}

/// <summary>One "path=value;" or "path;" element of a control path.</summary>
public readonly record struct ControlCommand(string Path, string? Value = null)
{
    public override string ToString() => Value is null ? $"{Path};" : $"{Path}={Value};";

    public static IReadOnlyList<ControlCommand> Parse(string text) =>
        text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part =>
            {
                int eq = part.IndexOf('=');
                return eq < 0 ? new ControlCommand(part) : new ControlCommand(part[..eq].Trim(), part[(eq + 1)..].Trim());
            })
            .ToList();

    public static string Join(IEnumerable<ControlCommand> commands) => string.Join(" ", commands);

    /// <summary>layer/N/state=S; (spec example: state 6 stops the layer).</summary>
    public static ControlCommand SetState(int layer, LayerState state) => new($"layer/{layer}/state", ((byte)state).ToString());

    /// <summary>layer/N/source=S; (spec example: layer/5/source=1 sets layer A to follow layer 1).</summary>
    public static ControlCommand SetSource(int layer, int source) => new($"layer/{layer}/source", source.ToString());

    /// <summary>layer/N/resync;</summary>
    public static ControlCommand Resync(int layer) => new($"layer/{layer}/resync");

    /// <summary>N for paths of the form layer/N/…</summary>
    public int? LayerNumber
    {
        get
        {
            var s = Path.Split('/');
            return s.Length >= 2 && s[0].Equals("layer", StringComparison.OrdinalIgnoreCase) && int.TryParse(s[1], out int n) ? n : null;
        }
    }

    /// <summary>Last path segment, e.g. "state".</summary>
    public string Leaf => Path.Split('/')[^1];
}

/// <summary>Type 101 · Control (42 + data size).</summary>
public sealed class ControlPacket : TextPacket
{
    public ControlPacket() { }
    public ControlPacket(params ControlCommand[] commands) => Text = ControlCommand.Join(commands);

    public override MessageType MessageType => MessageType.Control;
    public override string Name => "Control";

    public IReadOnlyList<ControlCommand> Commands => ControlCommand.Parse(Text);
}

/// <summary>Type 128 · Text Data (42 + data size).</summary>
public sealed class TextDataPacket : TextPacket
{
    public TextDataPacket() { }
    public TextDataPacket(string text) => Text = text;

    public override MessageType MessageType => MessageType.TextData;
    public override string Name => "Text Data";
}

/// <summary>Type 132 · Keyboard Data (44 bytes): two key bytes at 42.</summary>
public sealed class KeyboardDataPacket : TCNetPacket
{
    public override MessageType MessageType => MessageType.KeyboardData;
    public override string Name => "Keyboard Data";
    public override int Length => TCNetConstants.KeyboardLength;

    public byte Key1 { get; set; }
    public byte Key2 { get; set; }

    /// <summary>The two bytes as a little-endian code.</summary>
    public ushort KeyCode
    {
        get => (ushort)(Key1 | Key2 << 8);
        set { Key1 = (byte)value; Key2 = (byte)(value >> 8); }
    }

    public static KeyboardDataPacket For(char c) => new() { KeyCode = c <= 0xFF ? c : '?' };

    protected override void Encode(Span<byte> p)
    {
        Wire.PutU32(p, 26, 2);
        p[42] = Key1;
        p[43] = Key2;
    }

    protected internal override void Decode(ReadOnlySpan<byte> p, int datagramLength)
    {
        Key1 = p[42];
        Key2 = p[43];
    }

    protected override void DescribeBody(List<TCNetField> f)
    {
        f.Add(new(26, 4, "Data Size", "2"));
        f.Add(new(42, 2, "Keyboard Data", $"0x{Key1:X2} 0x{Key2:X2}", Key1 is >= 0x20 and < 0x7F ? $"'{(char)Key1}'" : null));
    }

    public override string Summary => $"key 0x{KeyCode:X4}" + (Key1 is >= 0x20 and < 0x7F ? $" '{(char)Key1}'" : "");
}

/// <summary>
/// Type 30 (and 213) · Application Specific Data: identifier (24–25), total size (26), total packets (30),
/// packet no (34), signature (38), data (42…).
/// </summary>
public sealed class ApplicationDataPacket : TCNetPacket
{
    private MessageType _type = MessageType.ApplicationData;

    public ApplicationDataPacket() { }

    public ApplicationDataPacket(MessageType type) => UseType(type);

    public override MessageType MessageType => _type;
    public override string Name => $"Application Specific Data ({(byte)_type})";

    /// <summary>30 (detail page) or 213 (overview list).</summary>
    public void UseType(MessageType type)
    {
        if (type is not (MessageType.ApplicationData or MessageType.ApplicationSpecificData))
            throw new ArgumentOutOfRangeException(nameof(type));
        _type = type;
    }

    public byte Identifier1 { get; set; }
    public byte Identifier2 { get; set; }

    /// <summary>Registered application code, identifier 1 = high byte (e.g. 0x0AAA ShowKontrol).</summary>
    public ushort ApplicationCode
    {
        get => (ushort)(Identifier1 << 8 | Identifier2);
        set { Identifier1 = (byte)(value >> 8); Identifier2 = (byte)value; }
    }

    /// <summary>Size of all packets' data together (0 = this packet's payload length).</summary>
    public uint TotalSize { get; set; }
    public uint TotalPackets { get; set; } = 1;
    public uint PacketNumber { get; set; }
    public uint Signature { get; set; } = TCNetConstants.ApplicationSignature;
    public byte[] Payload { get; set; } = [];

    public override int Length => TCNetConstants.PayloadOffset + Payload.Length;

    protected override void Encode(Span<byte> p)
    {
        p[24] = Identifier1;
        p[25] = Identifier2;
        Wire.PutU32(p, 26, TotalSize == 0 ? (uint)Payload.Length : TotalSize);
        Wire.PutU32(p, 30, TotalPackets);
        Wire.PutU32(p, 34, PacketNumber);
        Wire.PutU32(p, 38, Signature);
        Payload.CopyTo(p[42..]);
    }

    protected internal override void Decode(ReadOnlySpan<byte> p, int datagramLength)
    {
        _type = (MessageType)p[7];
        Identifier1 = p[24];
        Identifier2 = p[25];
        TotalSize = Wire.U32(p, 26);
        TotalPackets = Wire.U32(p, 30);
        PacketNumber = Wire.U32(p, 34);
        Signature = Wire.U32(p, 38);
        Payload = p.Slice(42, Math.Max(0, datagramLength - 42)).ToArray();
    }

    protected override void DescribeBody(List<TCNetField> f)
    {
        f.Add(new(24, 2, "Data Identifier 1/2", $"0x{ApplicationCode:X4}", TCNetText.ApplicationVendor(ApplicationCode) ?? "unregistered code"));
        f.Add(new(26, 4, "Data Size", TotalSize.ToString(), "size of all packets"));
        f.Add(new(30, 4, "Total Packets", TotalPackets.ToString()));
        f.Add(new(34, 4, "Packet No", PacketNumber.ToString()));
        f.Add(new(38, 4, "Packet Signature", Signature.ToString(), Signature == TCNetConstants.ApplicationSignature ? "standard" : "non-standard"));
        f.Add(new(42, Payload.Length, "Data", Wire.Hex(Payload)));
    }

    public override string Summary => $"app 0x{ApplicationCode:X4} packet {PacketNumber}/{TotalPackets}, {Payload.Length} bytes";
}
