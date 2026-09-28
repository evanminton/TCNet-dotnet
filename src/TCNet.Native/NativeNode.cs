using System.Net;
using TCNet.Networking;
using TCNet.Text;

namespace TCNet.Native;

/// <summary>Event kinds passed to the C callback (tcnet.h TCNET_EVENT_*); the mask bit of a kind is 1 &lt;&lt; kind.</summary>
internal enum EventKind
{
    PacketReceived = 1,
    PacketSent = 2,
    InvalidDatagram = 3,
    NodeDiscovered = 4,
    NodeChanged = 5,
    NodeLost = 6,
    DataAssembled = 7,
    TimeSynced = 8,
    RoleChanged = 9,
    Warning = 10,
}

/// <summary>A <see cref="TCNetNode"/> driven from C: blocking calls, JSON results and one event callback.</summary>
internal sealed unsafe class NativeNode
{
    /// <summary>Mask bit: packet events also carry every decoded field (slower; Time packets arrive every few ms).</summary>
    public const int WithFields = 1 << 16;

    private sealed record Target(nint Function, nint User, int Mask);

    private volatile Target? _target;
    private readonly object _timeGate = new();
    private TimeData _streamTime;

    public NativeNode(NodeSettings settings)
    {
        Node = new TCNetNode(settings);
        Node.PacketReceived += (_, e) => OnPacket(EventKind.PacketReceived, e);
        Node.PacketSent += (_, e) => OnPacket(EventKind.PacketSent, e);
        Node.InvalidDatagram += (_, e) => Emit(EventKind.InvalidDatagram, w =>
        {
            w.WriteString("from", e.RemoteEndPoint.ToString());
            w.WriteNumber("port", e.Port);
            w.WriteString("reason", e.Reason);
            w.WriteNumber("bytes", e.Data.Length);
            w.WriteString("hex", Wire.HexDump(e.Data, 256));
        });
        Node.NodeDiscovered += (_, e) => OnNode(EventKind.NodeDiscovered, e);
        Node.NodeChanged += (_, e) => OnNode(EventKind.NodeChanged, e);
        Node.NodeLost += (_, e) => OnNode(EventKind.NodeLost, e);
        Node.DataAssembled += (_, e) => Emit(EventKind.DataAssembled, w =>
        {
            if (e.Node is not null) w.WriteString("node", e.Node.Key);
            w.WritePropertyName("data");
            Json.Data(w, e.Data);
        });
        Node.TimeSynced += (_, e) => Emit(EventKind.TimeSynced, w =>
        {
            w.WriteString("node", e.Node.Key);
            w.WritePropertyName("sync");
            Json.Sync(w, e.Result);
        });
        Node.RoleChanged += (_, role) => Emit(EventKind.RoleChanged, w =>
        {
            w.WriteNumber("role", (byte)role);
            w.WriteString("roleText", TCNetText.Describe(role));
        });
        Node.Warning += (_, message) => Emit(EventKind.Warning, w => w.WriteString("message", message));
    }

    public TCNetNode Node { get; }

    public void SetCallback(int mask, nint function, nint user) =>
        _target = function == 0 ? null : new Target(function, user, mask);

    private void OnPacket(EventKind kind, PacketEventArgs e)
    {
        var t = _target;
        if (t is null || (t.Mask & (1 << (int)kind)) == 0) return;
        bool fields = (t.Mask & WithFields) != 0;
        Emit(kind, w =>
        {
            w.WriteString("from", e.RemoteEndPoint.ToString());
            w.WriteNumber("port", e.Port);
            w.WriteBoolean("outgoing", e.Outgoing);
            w.WriteString("time", e.Time);
            if (e.Node is not null) w.WriteString("node", e.Node.Key);
            w.WritePropertyName("packet");
            Json.Packet(w, e.Packet, fields);
        });
    }

    private void OnNode(EventKind kind, NodeEventArgs e) => Emit(kind, w =>
    {
        if (e.Reason is not null) w.WriteString("reason", e.Reason);
        w.WritePropertyName("node");
        Json.Node(w, e.Node);
    });

    /// <summary>Calls back into C with {"event": kind, …}. The JSON is only valid during the call.</summary>
    private void Emit(EventKind kind, Action<System.Text.Json.Utf8JsonWriter> body)
    {
        var t = _target;
        if (t is null || (t.Mask & (1 << (int)kind)) == 0) return;
        byte[] json = Json.Utf8(w =>
        {
            w.WriteStartObject();
            w.WriteString("event", Name(kind));
            w.WriteNumber("kind", (int)kind);
            body(w);
            w.WriteEndObject();
        });
        fixed (byte* p = json)
            ((delegate* unmanaged[Cdecl]<nint, int, byte*, void>)(void*)t.Function)(t.User, (int)kind, p);
    }

    private static string Name(EventKind k) => k switch
    {
        EventKind.PacketReceived => "packetReceived",
        EventKind.PacketSent => "packetSent",
        EventKind.InvalidDatagram => "invalidDatagram",
        EventKind.NodeDiscovered => "nodeDiscovered",
        EventKind.NodeChanged => "nodeChanged",
        EventKind.NodeLost => "nodeLost",
        EventKind.DataAssembled => "dataAssembled",
        EventKind.TimeSynced => "timeSynced",
        EventKind.RoleChanged => "roleChanged",
        _ => "warning",
    };

    // ─────────────── queries ───────────────

    public string InfoJson() => Json.String(w =>
    {
        w.WriteStartObject();
        w.WriteBoolean("running", Node.IsRunning);
        w.WriteNumber("nodeType", (byte)Node.NodeType);
        w.WriteString("nodeTypeText", TCNetText.Describe(Node.NodeType));
        w.WriteNumber("listenerPort", Node.ListenerPort);
        w.WriteStartArray("sharedPorts");
        foreach (int port in Node.SharedPorts) w.WriteNumberValue(port);
        w.WriteEndArray();
        w.WriteString("broadcastAddress", Node.BroadcastAddress.ToString());
        w.WriteNumber("packetsReceived", Node.PacketsReceived);
        w.WriteNumber("packetsSent", Node.PacketsSent);
        w.WriteNumber("nodes", Node.Nodes.Count);
        w.WriteBoolean("streaming", Node.IsStreaming);
        w.WriteNumber("timestamp", Node.Clock.Timestamp);
        w.WritePropertyName("settings");
        Json.Settings(w, Node.Settings);
        w.WriteEndObject();
    });

    public string NodesJson()
    {
        var nodes = Node.Nodes;
        return Json.String(w =>
        {
            w.WriteStartArray();
            foreach (var n in nodes) Json.Node(w, n);
            w.WriteEndArray();
        });
    }

    public string? FindJson(string query) => Node.FindNode(query) is { } n ? Json.String(w => Json.Node(w, n)) : null;

    /// <summary>By key ("ip#id"), name or IP; null/empty = the first master (or repeater).</summary>
    public RemoteNode Resolve(string? query)
    {
        if (string.IsNullOrEmpty(query))
            return Node.Nodes.FirstOrDefault(n => n.NodeType == NodeType.Master)
                   ?? Node.Nodes.FirstOrDefault(n => n.IsMasterOrRepeater)
                   ?? throw new InvalidOperationException("No master or repeater on the network yet.");
        return Node.FindNode(query) ?? throw new ArgumentException($"No node \"{query}\" (use its key ip#id, name or IP).");
    }

    /// <summary>Latest Time packet from a node, or (query null) from the master, else the most recent from anyone.</summary>
    public TimePacket? LatestTime(string? query)
    {
        if (!string.IsNullOrEmpty(query)) return Resolve(query).Time;
        var withTime = Node.Nodes.Where(n => n.Time is not null).ToList();
        return (withTime.FirstOrDefault(n => n.NodeType == NodeType.Master)
                ?? withTime.OrderByDescending(n => n.TimeReceived).FirstOrDefault())?.Time;
    }

    // ─────────────── actions (blocking) ───────────────

    public void Start() => Node.StartAsync().GetAwaiter().GetResult();

    public void Stop() => Node.StopAsync().GetAwaiter().GetResult();

    public void PublishTime(TimeData d) => Node.PublishTimeAsync(TimeData.ToPacket(d)).GetAwaiter().GetResult();

    public void SetStreamTime(TimeData d)
    {
        lock (_timeGate) _streamTime = d;
    }

    public void StartStream(int intervalMs) => Node.StartTimeStream(() =>
    {
        TimeData d;
        lock (_timeGate) d = _streamTime;
        return TimeData.ToPacket(d);
    }, TimeSpan.FromMilliseconds((double)intervalMs));

    public void StopStream() => Node.StopTimeStreamAsync().GetAwaiter().GetResult();

    public RequestResult Request(string? query, int dataType, int layer, int timeoutMs) =>
        Node.RequestAsync(Resolve(query), (DataType)checked((byte)dataType), checked((byte)layer), Api.Wait(timeoutMs)).GetAwaiter().GetResult();

    public TimeSyncResult TimeSync(string? query, int rounds, int timeoutMs) =>
        Node.TimeSyncAsync(Resolve(query), rounds, Api.Wait(timeoutMs)).GetAwaiter().GetResult();

    /// <summary>Notification code, or −2 without an answer.</summary>
    public int Control(string? query, string path, int timeoutMs) =>
        Node.SendControlAsync(Resolve(query), path, Api.Wait(timeoutMs)).GetAwaiter().GetResult() is { } n ? (int)n.Code : -2;

    public void SendText(string text, string? query) =>
        Node.SendTextAsync(text, string.IsNullOrEmpty(query) ? null : Resolve(query)).GetAwaiter().GetResult();

    public void SendKey(int code, string? query) =>
        Node.SendKeyAsync(checked((ushort)code), string.IsNullOrEmpty(query) ? null : Resolve(query)).GetAwaiter().GetResult();

    public void SendRaw(byte[] datagram, string ip, int port) =>
        Node.SendRawAsync(datagram, new IPEndPoint(IPAddress.Parse(ip), port)).GetAwaiter().GetResult();

    public void Inject(ReadOnlySpan<byte> datagram, string ip, int port) =>
        Node.Inject(datagram, new IPEndPoint(IPAddress.Parse(ip), port), port);
}
