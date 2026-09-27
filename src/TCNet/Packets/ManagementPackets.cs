using TCNet.Text;

namespace TCNet;

/// <summary>Type 2 · Opt-IN (68 bytes). Broadcast to 60000 and unicast to every known node every 1000 ms.</summary>
public sealed class OptInPacket : TCNetPacket
{
    public override MessageType MessageType => MessageType.OptIn;
    public override string Name => "Opt-IN";
    public override int Length => TCNetConstants.OptInLength;

    public ushort NodeCount { get; set; }
    public ushort ListenerPort { get; set; } = TCNetConstants.UnicastPortMin;
    /// <summary>Seconds, 0–43199 (rolls over every 12 h).</summary>
    public ushort Uptime { get; set; }
    public string VendorName { get; set; } = "";
    public string DeviceName { get; set; } = "";
    public byte DeviceMajor { get; set; }
    public byte DeviceMinor { get; set; }
    public byte DeviceBug { get; set; }

    public string DeviceVersion => $"{DeviceMajor}.{DeviceMinor}.{DeviceBug}";

    protected override void Encode(Span<byte> p)
    {
        Wire.PutU16(p, 24, NodeCount);
        Wire.PutU16(p, 26, ListenerPort);
        Wire.PutU16(p, 28, Uptime);
        Wire.PutAscii(p, 32, 16, VendorName);
        Wire.PutAscii(p, 48, 16, DeviceName);
        p[64] = DeviceMajor;
        p[65] = DeviceMinor;
        p[66] = DeviceBug;
    }

    protected internal override void Decode(ReadOnlySpan<byte> p, int datagramLength)
    {
        NodeCount = Wire.U16(p, 24);
        ListenerPort = Wire.U16(p, 26);
        Uptime = Wire.U16(p, 28);
        VendorName = Wire.Ascii(p, 32, 16);
        DeviceName = Wire.Ascii(p, 48, 16);
        DeviceMajor = p[64];
        DeviceMinor = p[65];
        DeviceBug = p[66];
    }

    protected override void DescribeBody(List<TCNetField> f)
    {
        f.Add(new(24, 2, "Node Count", NodeCount.ToString(), "nodes registered by the sender"));
        f.Add(new(26, 2, "Node Listener Port", ListenerPort.ToString(), "unicast port"));
        f.Add(new(28, 2, "Uptime", $"{Uptime} s", TCNetUnits.Duration(TimeSpan.FromSeconds(Uptime))));
        f.Add(new(32, 16, "Vendor Name", VendorName));
        f.Add(new(48, 16, "Application/Device Name", DeviceName));
        f.Add(new(64, 3, "Application/Device Version", DeviceVersion, "major.minor.bug"));
    }

    public override string Summary => $"{VendorName} {DeviceName} {DeviceVersion}, port {ListenerPort}, {NodeCount} nodes, up {Uptime} s";
}

/// <summary>Type 3 · Opt-OUT (28 bytes). Broadcast and unicast once when leaving.</summary>
public sealed class OptOutPacket : TCNetPacket
{
    public override MessageType MessageType => MessageType.OptOut;
    public override string Name => "Opt-OUT";
    public override int Length => TCNetConstants.OptOutLength;

    public ushort NodeCount { get; set; }
    public ushort ListenerPort { get; set; } = TCNetConstants.UnicastPortMin;

    protected override void Encode(Span<byte> p)
    {
        Wire.PutU16(p, 24, NodeCount);
        Wire.PutU16(p, 26, ListenerPort);
    }

    protected internal override void Decode(ReadOnlySpan<byte> p, int datagramLength)
    {
        NodeCount = Wire.U16(p, 24);
        ListenerPort = Wire.U16(p, 26);
    }

    protected override void DescribeBody(List<TCNetField> f)
    {
        f.Add(new(24, 2, "Node Count", NodeCount.ToString()));
        f.Add(new(26, 2, "Node Listener Port", ListenerPort.ToString()));
    }

    public override string Summary => $"leaving, port {ListenerPort}";
}

/// <summary>One layer block of a Status packet.</summary>
public sealed class StatusLayer
{
    public byte Source { get; set; }
    public LayerState State { get; set; }
    public uint TrackId { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>Type 5 · Status (300 bytes). Broadcast every 1000 ms and unicast to all slaves.</summary>
public sealed class StatusPacket : TCNetPacket
{
    public StatusPacket()
    {
        for (int i = 0; i < Layers.Length; i++) Layers[i] = new StatusLayer();
    }

    public override MessageType MessageType => MessageType.Status;
    public override string Name => "Status";
    public override int Length => TCNetConstants.StatusLength;

    public ushort NodeCount { get; set; }
    public ushort ListenerPort { get; set; } = TCNetConstants.UnicastPortMin;

    /// <summary>Wire order 1, 2, 3, 4, A, B, M, C.</summary>
    public StatusLayer[] Layers { get; } = new StatusLayer[TCNetConstants.LayerCount];

    public StatusLayer this[Layer layer] => Layers[(int)layer - 1];

    public SmpteMode SmpteMode { get; set; }
    public AutoMasterMode AutoMasterMode { get; set; }

    /// <summary>Bytes 100–171 (application specific).</summary>
    public byte[] AppSpecific { get; } = new byte[72];

    protected override void Encode(Span<byte> p)
    {
        Wire.PutU16(p, 24, NodeCount);
        Wire.PutU16(p, 26, ListenerPort);
        for (int i = 0; i < 8; i++)
        {
            p[34 + i] = Layers[i].Source;
            p[42 + i] = (byte)Layers[i].State;
            Wire.PutU32(p, 50 + 4 * i, Layers[i].TrackId);
            Wire.PutAscii(p, 172 + 16 * i, 16, Layers[i].Name);
        }
        p[83] = (byte)SmpteMode;
        p[84] = (byte)AutoMasterMode;
        AppSpecific.CopyTo(p[100..]);
    }

    protected internal override void Decode(ReadOnlySpan<byte> p, int datagramLength)
    {
        NodeCount = Wire.U16(p, 24);
        ListenerPort = Wire.U16(p, 26);
        for (int i = 0; i < 8; i++)
        {
            Layers[i].Source = p[34 + i];
            Layers[i].State = (LayerState)p[42 + i];
            Layers[i].TrackId = Wire.U32(p, 50 + 4 * i);
            Layers[i].Name = Wire.Ascii(p, 172 + 16 * i, 16);
        }
        SmpteMode = (SmpteMode)p[83];
        AutoMasterMode = (AutoMasterMode)p[84];
        p.Slice(100, 72).CopyTo(AppSpecific);
    }

    protected override void DescribeBody(List<TCNetField> f)
    {
        f.Add(new(24, 2, "Node Count", NodeCount.ToString()));
        f.Add(new(26, 2, "Node Listener Port", ListenerPort.ToString()));
        for (int i = 0; i < 8; i++) f.Add(new(34 + i, 1, $"Layer {TCNetText.LayerLabel(i)} Source", Layers[i].Source.ToString()));
        for (int i = 0; i < 8; i++) f.Add(new(42 + i, 1, $"Layer {TCNetText.LayerLabel(i)} Status", ((byte)Layers[i].State).ToString(), TCNetText.Describe(Layers[i].State)));
        for (int i = 0; i < 8; i++) f.Add(new(50 + 4 * i, 4, $"Layer {TCNetText.LayerLabel(i)} Track ID", Layers[i].TrackId.ToString()));
        f.Add(new(83, 1, "SMPTE Mode", ((byte)SmpteMode).ToString(), TCNetText.Describe(SmpteMode)));
        f.Add(new(84, 1, "Auto Master Mode", ((byte)AutoMasterMode).ToString(), TCNetText.Describe(AutoMasterMode)));
        f.Add(new(100, 72, "App Specific", Wire.Hex(AppSpecific, 16)));
        for (int i = 0; i < 8; i++) f.Add(new(172 + 16 * i, 16, $"Layer {TCNetText.LayerLabel(i)} Name", Layers[i].Name));
    }

    public override string Summary => string.Join("  ", Layers.Select((l, i) => $"{TCNetText.LayerLabel(i)}:{TCNetText.Short(l.State)}"));
}

/// <summary>Type 10 · Time Sync (32 bytes). Step 0 from the initiator, step 1 back with its timestamp echoed.</summary>
public sealed class TimeSyncPacket : TCNetPacket
{
    public override MessageType MessageType => MessageType.TimeSync;
    public override string Name => "Time Sync";
    public override int Length => TCNetConstants.TimeSyncLength;

    public Step Step { get; set; }
    public ushort ListenerPort { get; set; } = TCNetConstants.UnicastPortMin;
    /// <summary>In a step 1 response: the initiator's original timestamp.</summary>
    public uint RemoteTimestamp { get; set; }

    protected override void Encode(Span<byte> p)
    {
        p[24] = (byte)Step;
        Wire.PutU16(p, 26, ListenerPort);
        Wire.PutU32(p, 28, RemoteTimestamp);
    }

    protected internal override void Decode(ReadOnlySpan<byte> p, int datagramLength)
    {
        Step = (Step)p[24];
        ListenerPort = Wire.U16(p, 26);
        RemoteTimestamp = Wire.U32(p, 28);
    }

    protected override void DescribeBody(List<TCNetField> f)
    {
        f.Add(new(24, 1, "STEP", ((byte)Step).ToString(), TCNetText.Describe(Step)));
        f.Add(new(26, 2, "Node Listener Port", ListenerPort.ToString()));
        f.Add(new(28, 4, "Remote Timestamp", $"{RemoteTimestamp} µs"));
    }

    public override string Summary => $"step {(byte)Step}, ts {Timestamp}, remote ts {RemoteTimestamp}";
}

/// <summary>Type 13 · Error / Notification (30 bytes).</summary>
public sealed class ErrorNotificationPacket : TCNetPacket
{
    public override MessageType MessageType => MessageType.ErrorNotification;
    public override string Name => "Error / Notification";
    public override int Length => TCNetConstants.ErrorNotificationLength;

    public byte DataType { get; set; }
    public byte LayerId { get; set; }
    public NotificationCode Code { get; set; }
    /// <summary>Message type of the request this answers.</summary>
    public ushort RequestType { get; set; }

    protected override void Encode(Span<byte> p)
    {
        p[24] = DataType;
        p[25] = LayerId;
        Wire.PutU16(p, 26, (ushort)Code);
        Wire.PutU16(p, 28, RequestType);
    }

    protected internal override void Decode(ReadOnlySpan<byte> p, int datagramLength)
    {
        DataType = p[24];
        LayerId = p[25];
        Code = (NotificationCode)Wire.U16(p, 26);
        RequestType = Wire.U16(p, 28);
    }

    protected override void DescribeBody(List<TCNetField> f)
    {
        f.Add(new(24, 1, "Datatype", DataType.ToString(), TCNetText.Describe((TCNet.DataType)DataType)));
        f.Add(new(25, 1, "Layer ID", LayerId.ToString(), LayerId == 0 ? "not layer specific" : $"layer {TCNetText.LayerName(LayerId)}"));
        f.Add(new(26, 2, "Code", ((ushort)Code).ToString(), TCNetText.Describe(Code)));
        f.Add(new(28, 2, "Message Type", RequestType.ToString(), RequestType <= 255 ? TCNetText.Describe((MessageType)RequestType) : null));
    }

    public override string Summary => $"{TCNetText.Describe(Code)} (request type {RequestType}, data {DataType}, layer {LayerId})";
}

/// <summary>Type 20 · Request (26 bytes).</summary>
public sealed class RequestPacket : TCNetPacket
{
    public override MessageType MessageType => MessageType.Request;
    public override string Name => "Request";
    public override int Length => TCNetConstants.RequestLength;

    public DataType DataType { get; set; }
    public byte Layer { get; set; }

    protected override void Encode(Span<byte> p)
    {
        p[24] = (byte)DataType;
        p[25] = Layer;
    }

    protected internal override void Decode(ReadOnlySpan<byte> p, int datagramLength)
    {
        DataType = (DataType)p[24];
        Layer = p[25];
    }

    protected override void DescribeBody(List<TCNetField> f)
    {
        f.Add(new(24, 1, "Data Type", ((byte)DataType).ToString(), TCNetText.Describe(DataType)));
        f.Add(new(25, 1, "Layer", Layer.ToString(), $"layer {TCNetText.LayerName(Layer)}"));
    }

    public override string Summary => $"{DataType} for layer {TCNetText.LayerName(Layer)}";
}
