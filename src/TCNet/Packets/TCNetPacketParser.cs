namespace TCNet;

/// <summary>Decodes datagrams into <see cref="TCNetPacket"/> instances.</summary>
public static class TCNetPacketParser
{
    /// <summary>
    /// When true, packets shorter than their spec size are rejected. When false (default) they are zero-padded
    /// so fields added in later flames read as 0 (<see cref="TCNetPacket.WasPadded"/> is set).
    /// </summary>
    public static bool Strict { get; set; }

    /// <summary>True if <paramref name="data"/> starts with a management header ("TCN" at offset 4).</summary>
    public static bool IsTCNet(ReadOnlySpan<byte> data) =>
        data.Length >= TCNetConstants.ManagementHeaderSize && data.Slice(4, 3).SequenceEqual(TCNetConstants.HeaderMagic);

    /// <summary>Creates an empty packet instance for a message type (and data type for 200/204).</summary>
    public static TCNetPacket Create(MessageType type, DataType dataType = 0) => type switch
    {
        MessageType.OptIn => new OptInPacket(),
        MessageType.OptOut => new OptOutPacket(),
        MessageType.Status => new StatusPacket(),
        MessageType.TimeSync => new TimeSyncPacket(),
        MessageType.ErrorNotification => new ErrorNotificationPacket(),
        MessageType.Request => new RequestPacket(),
        MessageType.Control => new ControlPacket(),
        MessageType.TextData => new TextDataPacket(),
        MessageType.KeyboardData => new KeyboardDataPacket(),
        MessageType.ApplicationData or MessageType.ApplicationSpecificData => CreateApp(type),
        MessageType.Time => new TimePacket(),
        MessageType.Data => dataType switch
        {
            DataType.Metrics => new MetricsDataPacket(),
            DataType.Metadata => new MetadataPacket(),
            DataType.BeatGrid => new BeatGridDataPacket(),
            DataType.CueData => new CueDataPacket(),
            DataType.SmallWaveform => new SmallWaveformPacket(),
            DataType.BigWaveform => new BigWaveformPacket(),
            DataType.Mixer => new MixerDataPacket(),
            _ => new UnknownDataPacket(MessageType.Data, dataType),
        },
        MessageType.DataFile => dataType == DataType.LowResArtwork
            ? new LowResArtworkPacket()
            : new UnknownDataPacket(MessageType.DataFile, dataType),
        _ => new UnknownPacket((byte)type),
    };

    private static ApplicationDataPacket CreateApp(MessageType type)
    {
        var p = new ApplicationDataPacket();
        p.SetMessageType(type);
        return p;
    }

    /// <summary>Parses a datagram. Returns false with a reason when it is not a valid TCNet packet.</summary>
    public static bool TryParse(ReadOnlySpan<byte> data, out TCNetPacket? packet, out string? error)
    {
        packet = null;
        if (data.Length < TCNetConstants.ManagementHeaderSize)
        {
            error = $"Too short: {data.Length} bytes, the management header needs 24.";
            return false;
        }
        if (!data.Slice(4, 3).SequenceEqual(TCNetConstants.HeaderMagic))
        {
            error = "Missing \"TCN\" header at bytes 4–6.";
            return false;
        }

        var type = (MessageType)data[7];
        DataType dataType = data.Length > 24 ? (DataType)data[24] : 0;
        var p = Create(type, dataType);

        int required = p.Length;
        if (data.Length < required && Strict)
        {
            error = $"{p.Name} needs {required} bytes, got {data.Length}.";
            return false;
        }

        try
        {
            if (data.Length >= required)
            {
                p.ReadHeader(data);
                p.ReadBody(data, data.Length);
            }
            else
            {
                var padded = new byte[required];
                data.CopyTo(padded);
                p.ReadHeader(padded);
                p.ReadBody(padded, data.Length);
            }
            p.ReceivedLength = data.Length;
        }
        catch (Exception ex) when (ex is ArgumentException or IndexOutOfRangeException)
        {
            error = $"{p.Name}: {ex.Message}";
            return false;
        }

        packet = p;
        error = null;
        return true;
    }

    /// <summary>Parses a datagram or throws <see cref="FormatException"/>.</summary>
    public static TCNetPacket Parse(ReadOnlySpan<byte> data) =>
        TryParse(data, out var packet, out var error) ? packet! : throw new FormatException(error);
}

/// <summary>A TCNet packet with an unknown message type; body bytes are kept raw.</summary>
public sealed class UnknownPacket : TCNetPacket
{
    private readonly byte _type;

    public UnknownPacket(byte messageType) => _type = messageType;

    public override MessageType MessageType => (MessageType)_type;
    public override string Name => $"Unknown type {_type}";
    public override int Length => TCNetConstants.ManagementHeaderSize + Body.Length;

    /// <summary>Bytes 24 to the end of the datagram.</summary>
    public byte[] Body { get; set; } = [];

    protected override void WriteBody(Span<byte> p) => Body.CopyTo(p[24..]);

    protected internal override void ReadBody(ReadOnlySpan<byte> p, int receivedLength) =>
        Body = p.Slice(24, Math.Max(0, receivedLength - 24)).ToArray();

    protected override void DescribeBody(List<TCNetField> f) =>
        f.Add(new(24, Body.Length, "Body", Convert.ToHexString(Body.AsSpan(0, Math.Min(32, Body.Length))) + (Body.Length > 32 ? "…" : "")));
}
