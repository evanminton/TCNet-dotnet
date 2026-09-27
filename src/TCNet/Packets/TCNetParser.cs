namespace TCNet;

/// <summary>Turns datagrams into packets.</summary>
public static class TCNetParser
{
    /// <summary>
    /// False (default): packets shorter than the spec size (older flames) are zero-padded and parsed; true: rejected.
    /// </summary>
    public static bool Strict { get; set; }

    public static bool IsTCNet(ReadOnlySpan<byte> d) =>
        d.Length >= TCNetConstants.HeaderLength && d.Slice(4, 3).SequenceEqual(TCNetConstants.Magic);

    /// <summary>A fresh packet for a message type (and data type for 200/204).</summary>
    public static TCNetPacket Create(MessageType type, DataType data = 0) => type switch
    {
        MessageType.OptIn => new OptInPacket(),
        MessageType.OptOut => new OptOutPacket(),
        MessageType.Status => new StatusPacket(),
        MessageType.TimeSync => new TimeSyncPacket(),
        MessageType.ErrorNotification => new ErrorNotificationPacket(),
        MessageType.Request => new RequestPacket(),
        MessageType.ApplicationData or MessageType.ApplicationSpecificData => new ApplicationDataPacket(type),
        MessageType.Control => new ControlPacket(),
        MessageType.TextData => new TextDataPacket(),
        MessageType.KeyboardData => new KeyboardDataPacket(),
        MessageType.Time => new TimePacket(),
        MessageType.Data => data switch
        {
            DataType.Metrics => new MetricsPacket(),
            DataType.Metadata => new MetadataPacket(),
            DataType.BeatGrid => new BeatGridPacket(),
            DataType.CueData => new CueDataPacket(),
            DataType.SmallWaveform => new SmallWaveformPacket(),
            DataType.BigWaveform => new BigWaveformPacket(),
            DataType.Mixer => new MixerDataPacket(),
            _ => new UnknownDataPacket(MessageType.Data, data),
        },
        MessageType.DataFile => data == DataType.LowResArtwork ? new ArtworkPacket() : new UnknownDataPacket(MessageType.DataFile, data),
        _ => new UnknownPacket((byte)type),
    };

    public static bool TryParse(ReadOnlySpan<byte> d, out TCNetPacket? packet, out string? error)
    {
        packet = null;
        if (d.Length < TCNetConstants.HeaderLength)
        {
            error = $"Too short: {d.Length} bytes; the header alone is 24.";
            return false;
        }
        if (!d.Slice(4, 3).SequenceEqual(TCNetConstants.Magic))
        {
            error = "No \"TCN\" at bytes 4–6.";
            return false;
        }

        var type = (MessageType)d[7];
        var p = Create(type, d.Length > 24 ? (DataType)d[24] : 0);
        int need = p.Length;
        if (d.Length < need && Strict)
        {
            error = $"{p.Name} needs {need} bytes, got {d.Length}.";
            return false;
        }

        try
        {
            if (d.Length >= need)
            {
                p.DecodeHeader(d);
                p.Decode(d, d.Length);
            }
            else
            {
                var padded = new byte[need];
                d.CopyTo(padded);
                p.DecodeHeader(padded);
                p.Decode(padded, d.Length);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException)
        {
            error = $"{p.Name}: {ex.Message}";
            return false;
        }

        p.ReceivedLength = d.Length;
        packet = p;
        error = null;
        return true;
    }

    public static TCNetPacket Parse(ReadOnlySpan<byte> d) =>
        TryParse(d, out var p, out var e) ? p! : throw new FormatException(e);
}

/// <summary>A packet with an unknown message type; bytes 24+ kept raw.</summary>
public sealed class UnknownPacket(byte type) : TCNetPacket
{
    public override MessageType MessageType => (MessageType)type;
    public override string Name => $"Unknown type {type}";
    public override int Length => TCNetConstants.HeaderLength + Body.Length;
    public byte[] Body { get; set; } = [];

    protected override void Encode(Span<byte> p) => Body.CopyTo(p[24..]);

    protected internal override void Decode(ReadOnlySpan<byte> p, int datagramLength) =>
        Body = p.Slice(24, Math.Max(0, datagramLength - 24)).ToArray();

    protected override void DescribeBody(List<TCNetField> f) => f.Add(new(24, Body.Length, "Body", Wire.Hex(Body)));
}
