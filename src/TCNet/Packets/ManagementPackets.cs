using TCNet.Text;

namespace TCNet;

/// <summary>Type 2 – Opt-IN (68 bytes). Broadcast to 60000 and unicast to every known node every 1000 ms.</summary>
public sealed class OptInPacket : TCNetPacket
{
    public override MessageType MessageType => MessageType.OptIn;
    public override string Name => "Opt-IN";
    public override int Length => TCNetConstants.OptInSize;

    /// <summary>Number of nodes registered by the sender.</summary>
    public ushort NodeCount { get; set; }

    /// <summary>Sender's unicast listener port (65023–65535).</summary>
    public ushort ListenerPort { get; set; } = TCNetConstants.DefaultUnicastPort;

    /// <summary>Uptime in seconds, 0–43199 (rolls over every 12 hours).</summary>
    public ushort Uptime { get; set; }

    public string VendorName { get; set; } = "";
    public string ApplicationName { get; set; } = "";
    public byte ApplicationMajorVersion { get; set; }
    public byte ApplicationMinorVersion { get; set; }
    public byte ApplicationBugVersion { get; set; }

    public string ApplicationVersion => $"{ApplicationMajorVersion}.{ApplicationMinorVersion}.{ApplicationBugVersion}";

    protected override void WriteBody(Span<byte> p)
    {
        Wire.U16(p, 24, NodeCount);
        Wire.U16(p, 26, ListenerPort);
        Wire.U16(p, 28, Uptime);
        Wire.Ascii(p, 32, 16, VendorName);
        Wire.Ascii(p, 48, 16, ApplicationName);
        p[64] = ApplicationMajorVersion;
        p[65] = ApplicationMinorVersion;
        p[66] = ApplicationBugVersion;
    }

    protected internal override void ReadBody(ReadOnlySpan<byte> p, int receivedLength)
    {
        NodeCount = Wire.U16(p, 24);
        ListenerPort = Wire.U16(p, 26);
        Uptime = Wire.U16(p, 28);
        VendorName = Wire.Ascii(p, 32, 16);
        ApplicationName = Wire.Ascii(p, 48, 16);
        ApplicationMajorVersion = p[64];
        ApplicationMinorVersion = p[65];
        ApplicationBugVersion = p[66];
    }

    protected override void DescribeBody(List<TCNetField> f)
    {
        f.Add(new(24, 2, "Node Count", NodeCount.ToString()));
        f.Add(new(26, 2, "Node Listener Port", ListenerPort.ToString()));
        f.Add(new(28, 2, "Uptime", $"{Uptime} s", TCNetUnits.FormatDuration(TimeSpan.FromSeconds(Uptime))));
        f.Add(new(32, 16, "Vendor Name", VendorName));
        f.Add(new(48, 16, "Application/Device Name", ApplicationName));
        f.Add(new(64, 3, "Application/Device Version", ApplicationVersion));
    }

    public override string Summary => $"{VendorName} {ApplicationName} {ApplicationVersion}, port {ListenerPort}, {NodeCount} nodes, up {Uptime}s";
}

/// <summary>Type 3 – Opt-OUT (28 bytes). Broadcast and unicast once when leaving the network.</summary>
public sealed class OptOutPacket : TCNetPacket
{
    public override MessageType MessageType => MessageType.OptOut;
    public override string Name => "Opt-OUT";
    public override int Length => TCNetConstants.OptOutSize;

    public ushort NodeCount { get; set; }
    public ushort ListenerPort { get; set; } = TCNetConstants.DefaultUnicastPort;

    protected override void WriteBody(Span<byte> p)
    {
        Wire.U16(p, 24, NodeCount);
        Wire.U16(p, 26, ListenerPort);
    }

    protected internal override void ReadBody(ReadOnlySpan<byte> p, int receivedLength)
    {
        NodeCount = Wire.U16(p, 24);
        ListenerPort = Wire.U16(p, 26);
    }

    protected override void DescribeBody(List<TCNetField> f)
    {
        f.Add(new(24, 2, "Node Count", NodeCount.ToString()));
        f.Add(new(26, 2, "Node Listener Port", ListenerPort.ToString()));
    }

    public override string Summary => $"port {ListenerPort}";
}

/// <summary>Per-layer block of a Status packet.</summary>
public sealed class StatusLayer
{
    public byte Source { get; set; }
    public LayerState State { get; set; }
    public uint TrackId { get; set; }
    public string Name { get; set; } = "";
}

/// <summary>Type 5 – Status (300 bytes). Broadcast every 1000 ms; unicast to all slaves.</summary>
public sealed class StatusPacket : TCNetPacket
{
    public StatusPacket()
    {
        for (int i = 0; i < Layers.Length; i++) Layers[i] = new StatusLayer();
    }

    public override MessageType MessageType => MessageType.Status;
    public override string Name => "Status";
    public override int Length => TCNetConstants.StatusSize;

    public ushort NodeCount { get; set; }
    public ushort ListenerPort { get; set; } = TCNetConstants.DefaultUnicastPort;

    /// <summary>Layers in wire order: 1, 2, 3, 4, A, B, M, C. Index = layer number − 1.</summary>
    public StatusLayer[] Layers { get; } = new StatusLayer[TCNetConstants.LayerCount];

    public SmpteMode SmpteMode { get; set; }
    public AutoMasterMode AutoMasterMode { get; set; }

    /// <summary>Bytes 100–171, application specific.</summary>
    public byte[] ApplicationSpecific { get; } = new byte[72];

    public StatusLayer this[TCNetLayer layer] => Layers[(int)layer - 1];

    protected override void WriteBody(Span<byte> p)
    {
        Wire.U16(p, 24, NodeCount);
        Wire.U16(p, 26, ListenerPort);
        for (int i = 0; i < TCNetConstants.LayerCount; i++)
        {
            var l = Layers[i];
            p[34 + i] = l.Source;
            p[42 + i] = (byte)l.State;
            Wire.U32(p, 50 + 4 * i, l.TrackId);
            Wire.Ascii(p, 172 + 16 * i, 16, l.Name);
        }
        p[83] = (byte)SmpteMode;
        p[84] = (byte)AutoMasterMode;
        ApplicationSpecific.CopyTo(p[100..172]);
    }

    protected internal override void ReadBody(ReadOnlySpan<byte> p, int receivedLength)
    {
        NodeCount = Wire.U16(p, 24);
        ListenerPort = Wire.U16(p, 26);
        for (int i = 0; i < TCNetConstants.LayerCount; i++)
        {
            var l = Layers[i];
            l.Source = p[34 + i];
            l.State = (LayerState)p[42 + i];
            l.TrackId = Wire.U32(p, 50 + 4 * i);
            l.Name = Wire.Ascii(p, 172 + 16 * i, 16);
        }
        SmpteMode = (SmpteMode)p[83];
        AutoMasterMode = (AutoMasterMode)p[84];
        p[100..172].CopyTo(ApplicationSpecific);
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
        f.Add(new(100, 72, "App Specific", Convert.ToHexString(ApplicationSpecific.AsSpan(0, 16)) + "…"));
        for (int i = 0; i < 8; i++) f.Add(new(172 + 16 * i, 16, $"Layer {TCNetText.LayerLabel(i)} Name", Layers[i].Name));
    }

    public override string Summary =>
        string.Join("  ", Layers.Select((l, i) => $"{TCNetText.LayerLabel(i)}:{TCNetText.Short(l.State)}"));
}

/// <summary>Type 10 – Time Sync (32 bytes). Unicast; the receiver answers step 0 with step 1.</summary>
public sealed class TimeSyncPacket : TCNetPacket
{
    public override MessageType MessageType => MessageType.TimeSync;
    public override string Name => "Time Sync";
    public override int Length => TCNetConstants.TimeSyncSize;

    public SyncStep Step { get; set; }
    public ushort ListenerPort { get; set; } = TCNetConstants.DefaultUnicastPort;

    /// <summary>On a step 1 response: the initiator's original timestamp echoed back.</summary>
    public uint RemoteTimestamp { get; set; }

    protected override void WriteBody(Span<byte> p)
    {
        p[24] = (byte)Step;
        Wire.U16(p, 26, ListenerPort);
        Wire.U32(p, 28, RemoteTimestamp);
    }

    protected internal override void ReadBody(ReadOnlySpan<byte> p, int receivedLength)
    {
        Step = (SyncStep)p[24];
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

/// <summary>Type 13 – Error / Notification (30 bytes). Sent back when a request is not handled, or to acknowledge.</summary>
public sealed class ErrorNotificationPacket : TCNetPacket
{
    public override MessageType MessageType => MessageType.ErrorNotification;
    public override string Name => "Error / Notification";
    public override int Length => TCNetConstants.ErrorNotificationSize;

    /// <summary>Data type of the failed request.</summary>
    public byte DataType { get; set; }

    /// <summary>Layer of the original request, 0 when not layer specific.</summary>
    public byte LayerId { get; set; }

    public NotificationCode Code { get; set; }

    /// <summary>Message type of the request this notification answers.</summary>
    public ushort RequestMessageType { get; set; }

    protected override void WriteBody(Span<byte> p)
    {
        p[24] = DataType;
        p[25] = LayerId;
        Wire.U16(p, 26, (ushort)Code);
        Wire.U16(p, 28, RequestMessageType);
    }

    protected internal override void ReadBody(ReadOnlySpan<byte> p, int receivedLength)
    {
        DataType = p[24];
        LayerId = p[25];
        Code = (NotificationCode)Wire.U16(p, 26);
        RequestMessageType = Wire.U16(p, 28);
    }

    protected override void DescribeBody(List<TCNetField> f)
    {
        f.Add(new(24, 1, "Datatype", DataType.ToString(), TCNetText.Describe((DataType)DataType)));
        f.Add(new(25, 1, "Layer ID", LayerId.ToString()));
        f.Add(new(26, 2, "Code", ((ushort)Code).ToString(), TCNetText.Describe(Code)));
        f.Add(new(28, 2, "Message Type", RequestMessageType.ToString(), RequestMessageType <= 255 ? TCNetText.Describe((MessageType)RequestMessageType) : null));
    }

    public override string Summary => $"{TCNetText.Describe(Code)} for type {RequestMessageType}/data {DataType}, layer {LayerId}";
}

/// <summary>Type 20 – Request (26 bytes). Asks a master/repeater for a data type on a layer.</summary>
public sealed class RequestPacket : TCNetPacket
{
    public override MessageType MessageType => MessageType.Request;
    public override string Name => "Request";
    public override int Length => TCNetConstants.RequestSize;

    public DataType DataType { get; set; }

    /// <summary>Layer the data is requested for (1–8).</summary>
    public byte Layer { get; set; }

    protected override void WriteBody(Span<byte> p)
    {
        p[24] = (byte)DataType;
        p[25] = Layer;
    }

    protected internal override void ReadBody(ReadOnlySpan<byte> p, int receivedLength)
    {
        DataType = (DataType)p[24];
        Layer = p[25];
    }

    protected override void DescribeBody(List<TCNetField> f)
    {
        f.Add(new(24, 1, "Data Type", ((byte)DataType).ToString(), TCNetText.Describe(DataType)));
        f.Add(new(25, 1, "Layer", Layer.ToString(), TCNetText.LayerName(Layer)));
    }

    public override string Summary => $"{TCNetText.Describe(DataType)} for layer {TCNetText.LayerName(Layer)}";
}
