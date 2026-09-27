using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using TCNet.Text;

namespace TCNet.Networking;

public sealed class PacketEventArgs(TCNetPacket packet, IPEndPoint remote, int port, bool outgoing, RemoteNode? node) : EventArgs
{
    public TCNetPacket Packet { get; } = packet;
    /// <summary>Sender (incoming) or destination (outgoing).</summary>
    public IPEndPoint RemoteEndPoint { get; } = remote;
    /// <summary>Local port it arrived on, or the destination port.</summary>
    public int Port { get; } = port;
    public bool Outgoing { get; } = outgoing;
    public RemoteNode? Node { get; } = node;
    public DateTime Time { get; } = DateTime.Now;
}

public sealed class InvalidDatagramEventArgs(byte[] data, IPEndPoint remote, int port, string reason) : EventArgs
{
    public byte[] Data { get; } = data;
    public IPEndPoint RemoteEndPoint { get; } = remote;
    public int Port { get; } = port;
    public string Reason { get; } = reason;
}

public sealed class NodeEventArgs(RemoteNode node, string? reason = null) : EventArgs
{
    public RemoteNode Node { get; } = node;
    /// <summary>For NodeLost: "Opt-OUT" or "timeout".</summary>
    public string? Reason { get; } = reason;
}

public sealed class DataEventArgs(AssembledData data, RemoteNode? node) : EventArgs
{
    public AssembledData Data { get; } = data;
    public RemoteNode? Node { get; } = node;
}

public sealed class SyncEventArgs(RemoteNode node, TimeSyncResult result) : EventArgs
{
    public RemoteNode Node { get; } = node;
    public TimeSyncResult Result { get; } = result;
}

/// <summary>Outcome of a Request.</summary>
public sealed record RequestResult(DataType DataType, byte Layer, TCNetPacket? Packet, AssembledData? Data, ErrorNotificationPacket? Notification, bool TimedOut)
{
    public bool Success => !TimedOut && (Packet is not null || Data is not null);

    public override string ToString() =>
        TimedOut ? $"{TCNetText.Describe(DataType)} L{TCNetText.LayerName(Layer)}: no answer"
        : Notification is { } n ? $"{TCNetText.Describe(DataType)} L{TCNetText.LayerName(Layer)}: {TCNetText.Describe(n.Code)}"
        : Data is { } d ? $"{TCNetText.Describe(DataType)} L{TCNetText.LayerName(Layer)}: {TCNetUnits.Bytes(d.Data.Length)} in {d.Packets} packets"
        : $"{TCNetText.Describe(DataType)} L{TCNetText.LayerName(Layer)}: {Packet?.Summary}";
}

/// <summary>
/// A local TCNet node. Joins the network (Opt-IN every second, broadcast and unicast), keeps the population list,
/// answers Time Sync, Request and Control, reassembles chunked data, and can request, control, stream Time packets
/// and take part in master election.
/// </summary>
public sealed class TCNetNode : IAsyncDisposable
{
    private readonly Dictionary<string, RemoteNode> _nodes = new();
    private readonly object _nodesGate = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<RequestResult>> _requests = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<TimeSyncResult>> _syncs = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ErrorNotificationPacket>> _controls = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _controlQueues = new();
    private readonly SemaphoreSlim _life = new(1, 1);
    private readonly byte[] _seq = new byte[256];
    private readonly List<Socket> _shared = [];
    private readonly List<Task> _loops = [];
    private Socket? _socket;
    private CancellationTokenSource? _cts;
    private HashSet<IPAddress> _local = [];
    private CancellationTokenSource? _streamCts;
    private Task? _stream;
    private int _electionRounds = -1;
    private bool _elected;
    private long _received, _sent;

    public TCNetNode(NodeSettings? settings = null)
    {
        Settings = settings ?? new NodeSettings();
        NodeType = Settings.NodeType;
    }

    public NodeSettings Settings { get; }
    public TCNetClock Clock { get; } = new();
    public ChunkAssembler Assembler { get; } = new();

    /// <summary>Current role (changes with <see cref="SetNodeType"/> or master election).</summary>
    public NodeType NodeType { get; private set; }

    public bool IsRunning => _cts is not null;
    public int ListenerPort { get; private set; }
    public IReadOnlyList<int> SharedPorts { get; private set; } = [];
    public IPAddress BroadcastAddress { get; private set; } = IPAddress.Broadcast;
    public long PacketsReceived => Interlocked.Read(ref _received);
    public long PacketsSent => Interlocked.Read(ref _sent);

    /// <summary>Snapshot of the population list.</summary>
    public IReadOnlyList<RemoteNode> Nodes
    {
        get { lock (_nodesGate) return _nodes.Values.ToList(); }
    }

    /// <summary>Answers a Request with packets; null or empty = "data empty". Not set = "not possible".</summary>
    public Func<RequestPacket, RemoteNode, IReadOnlyList<TCNetPacket>?>? RequestHandler { get; set; }

    /// <summary>Handles a Control packet; the code is sent back in an Error/Notification.</summary>
    public Func<ControlPacket, RemoteNode, NotificationCode>? ControlHandler { get; set; }

    /// <summary>Layer content for the periodic Status packet.</summary>
    public Func<StatusPacket>? StatusProvider { get; set; }

    public event EventHandler<PacketEventArgs>? PacketReceived;
    public event EventHandler<PacketEventArgs>? PacketSent;
    public event EventHandler<InvalidDatagramEventArgs>? InvalidDatagram;
    public event EventHandler<NodeEventArgs>? NodeDiscovered;
    public event EventHandler<NodeEventArgs>? NodeChanged;
    public event EventHandler<NodeEventArgs>? NodeLost;
    public event EventHandler<DataEventArgs>? DataAssembled;
    public event EventHandler<SyncEventArgs>? TimeSynced;
    public event EventHandler<NodeType>? RoleChanged;
    public event EventHandler<string>? Warning;

    // ─────────────── lifecycle ───────────────

    public async Task StartAsync()
    {
        await _life.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_cts is not null) return;
            _local = TCNetNetwork.LocalAddresses();
            BroadcastAddress = ResolveBroadcast();
            _socket = BindListener();
            ListenerPort = ((IPEndPoint)_socket.LocalEndPoint!).Port;

            var ports = new List<int>();
            if (Settings.ListenOnBroadcastPorts)
            {
                foreach (int port in new[] { TCNetConstants.BroadcastPort, TCNetConstants.TimePort, TCNetConstants.ApplicationPort })
                {
                    try { _shared.Add(BindShared(port)); ports.Add(port); }
                    catch (SocketException ex) { Warn($"UDP {port} unavailable ({ex.SocketErrorCode}); unicast still works."); }
                }
            }
            SharedPorts = ports;

            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            var main = _socket;
            _loops.Add(Task.Run(() => ReceiveLoop(main, ListenerPort, ct)));
            foreach (var s in _shared)
            {
                int port = ((IPEndPoint)s.LocalEndPoint!).Port;
                _loops.Add(Task.Run(() => ReceiveLoop(s, port, ct)));
            }
            _loops.Add(Task.Run(() => Housekeeping(ct)));
        }
        finally { _life.Release(); }
    }

    /// <summary>Sends Opt-OUT (broadcast and unicast) and closes. Safe to call more than once or concurrently.</summary>
    public async Task StopAsync()
    {
        await _life.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_cts is null) return;
            await StopTimeStreamAsync().ConfigureAwait(false);
            try
            {
                var bye = new OptOutPacket { NodeCount = (ushort)Nodes.Count, ListenerPort = (ushort)ListenerPort };
                await BroadcastAsync(bye, TCNetConstants.BroadcastPort).ConfigureAwait(false);
                foreach (var n in Nodes)
                    if (n.EndPoint is { } ep) await SendAsync(bye, ep).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException) { }

            _cts.Cancel();
            _socket?.Dispose();
            foreach (var s in _shared) s.Dispose();
            try { await Task.WhenAll(_loops).ConfigureAwait(false); } catch { }
            _loops.Clear();
            _shared.Clear();
            _socket = null;
            _cts.Dispose();
            _cts = null;
            lock (_nodesGate) _nodes.Clear();
            Assembler.Clear();
            foreach (var t in _requests.Values) t.TrySetCanceled();
            foreach (var t in _syncs.Values) t.TrySetCanceled();
            foreach (var t in _controls.Values) t.TrySetCanceled();
            _requests.Clear();
            _syncs.Clear();
            _controls.Clear();
            _electionRounds = -1;
            _elected = false;
        }
        finally { _life.Release(); }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    /// <summary>Changes the advertised role; clears any election in progress.</summary>
    public void SetNodeType(NodeType type)
    {
        _electionRounds = -1;
        _elected = false;
        ChangeRole(type);
    }

    private void ChangeRole(NodeType type)
    {
        if (NodeType == type) return;
        NodeType = type;
        RoleChanged?.Invoke(this, type);
    }

    // ─────────────── sockets ───────────────

    private Socket BindListener()
    {
        if (Settings.ListenerPort > 0) return Bind(Settings.ListenerPort, Settings.LocalAddress, shared: false);
        SocketException? last = null;
        for (int port = TCNetConstants.UnicastPortMin; port <= TCNetConstants.UnicastPortMax; port++)
        {
            try { return Bind(port, Settings.LocalAddress, shared: false); }
            catch (SocketException ex) { last = ex; }
        }
        throw last ?? new SocketException((int)SocketError.AddressAlreadyInUse);
    }

    private static Socket BindShared(int port) => Bind(port, IPAddress.Any, shared: true);

    private static Socket Bind(int port, IPAddress address, bool shared)
    {
        var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            if (shared)
            {
                if (OperatingSystem.IsWindows()) s.ExclusiveAddressUse = false;
                s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            }
            s.EnableBroadcast = true;
            if (OperatingSystem.IsWindows())
            {
                const int SioUdpConnReset = -1744830452;
                try { s.IOControl(SioUdpConnReset, [0, 0, 0, 0], null); } catch (SocketException) { }
            }
            s.Bind(new IPEndPoint(address, port));
            return s;
        }
        catch
        {
            s.Dispose();
            throw;
        }
    }

    private IPAddress ResolveBroadcast()
    {
        if (Settings.BroadcastAddress is { } b) return b;
        if (!Settings.LocalAddress.Equals(IPAddress.Any) &&
            TCNetNetwork.Interfaces().FirstOrDefault(i => i.Address.Equals(Settings.LocalAddress)) is { } iface)
            return iface.Broadcast;
        return IPAddress.Broadcast;
    }

    private async Task ReceiveLoop(Socket socket, int port, CancellationToken ct)
    {
        var buffer = new byte[65536];
        EndPoint any = new IPEndPoint(IPAddress.Any, 0);
        while (!ct.IsCancellationRequested)
        {
            SocketReceiveFromResult r;
            try { r = await socket.ReceiveFromAsync(buffer, SocketFlags.None, any, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionReset or SocketError.MessageSize) { continue; }
            catch (SocketException ex)
            {
                if (ct.IsCancellationRequested) break;
                Warn($"Receive on {port}: {ex.SocketErrorCode}");
                try { await Task.Delay(200, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
                continue;
            }
            try { Handle(buffer.AsSpan(0, r.ReceivedBytes), (IPEndPoint)r.RemoteEndPoint, port); }
            catch (Exception ex) { Warn($"Packet from {r.RemoteEndPoint}: {ex.Message}"); }
        }
    }

    // ─────────────── receive ───────────────

    /// <summary>Processes a datagram as if received (captures, tests).</summary>
    public void Inject(ReadOnlySpan<byte> datagram, IPEndPoint from, int port = 0) => Handle(datagram, from, port);

    private string OwnName => Settings.NodeName.Length > 8 ? Settings.NodeName[..8] : Settings.NodeName;

    private void Handle(ReadOnlySpan<byte> data, IPEndPoint from, int port)
    {
        if (!TCNetParser.TryParse(data, out var packet, out var error))
        {
            InvalidDatagram?.Invoke(this, new InvalidDatagramEventArgs(data.ToArray(), from, port, error!));
            return;
        }

        bool own = packet!.NodeId == Settings.NodeId && packet.NodeName == OwnName && _local.Contains(from.Address);
        if (own && !Settings.ReceiveOwnPackets) return;
        Interlocked.Increment(ref _received);

        var node = own ? null : Track(packet, from);
        PacketReceived?.Invoke(this, new PacketEventArgs(packet, from, port, false, node));
        if (node is null) return;

        switch (packet)
        {
            case TimeSyncPacket ts: OnTimeSync(ts, from, node); break;
            case RequestPacket rq: OnRequest(rq, from, node); break;
            case ControlPacket cp: OnControl(cp, from, node); break;
            case ErrorNotificationPacket en: OnNotification(en, node); break;
            case StatusPacket st: OnStatus(st, node); break;
            case TimePacket tp:
                node.Time = tp;
                node.TimeReceived = DateTime.UtcNow;
                break;
            case DataPacket dp: OnData(dp, from, node); break;
            case ApplicationDataPacket ap:
                if (Assembler.Add(ap, from) is { } app) DataAssembled?.Invoke(this, new DataEventArgs(app, node));
                break;
        }
    }

    private RemoteNode? Track(TCNetPacket p, IPEndPoint from)
    {
        string key = RemoteNode.KeyOf(from.Address, p.NodeId);
        RemoteNode? node;
        bool created = false;

        lock (_nodesGate)
        {
            if (p is OptOutPacket)
            {
                if (!_nodes.Remove(key, out node)) return null;
            }
            else if (!_nodes.TryGetValue(key, out node))
            {
                node = new RemoteNode(from.Address, p.NodeId, _local.Contains(from.Address));
                if (from.Port is >= TCNetConstants.UnicastPortMin and <= TCNetConstants.UnicastPortMax) node.ListenerPort = from.Port;
                _nodes[key] = node;
                created = true;
            }
        }

        if (p is OptOutPacket)
        {
            NodeLost?.Invoke(this, new NodeEventArgs(node, "Opt-OUT"));
            if (node.NodeType == NodeType.Master) BeginElection();
            return null;
        }

        var before = node.NodeType;
        node.NodeName = p.NodeName;
        node.NodeType = p.NodeType;
        node.EverAuto |= p.NodeType == NodeType.Auto;
        node.NodeOptions = p.NodeOptions;
        node.ProtocolVersion = p.ProtocolVersion;
        node.LastTimestamp = p.Timestamp;
        node.LastSeen = DateTime.UtcNow;
        node.Packets++;

        bool changed = before != node.NodeType;
        switch (p)
        {
            case OptInPacket oi:
                if (oi.ListenerPort != 0) node.ListenerPort = oi.ListenerPort;
                node.NodeCount = oi.NodeCount;
                node.Uptime = oi.Uptime;
                node.VendorName = oi.VendorName;
                node.DeviceName = oi.DeviceName;
                node.DeviceVersion = oi.DeviceVersion;
                node.OptIn = oi;
                changed = true;
                break;
            case StatusPacket st:
                if (st.ListenerPort != 0) node.ListenerPort = st.ListenerPort;
                node.NodeCount = st.NodeCount;
                break;
            case TimeSyncPacket ts when ts.ListenerPort != 0:
                node.ListenerPort = ts.ListenerPort;
                break;
        }

        if (created) NodeDiscovered?.Invoke(this, new NodeEventArgs(node));
        else if (changed) NodeChanged?.Invoke(this, new NodeEventArgs(node));

        if (node.NodeType == NodeType.Master && NodeType == NodeType.Master && _elected &&
            (!node.EverAuto || node.NodeId < Settings.NodeId))
        {
            _elected = false;
            ChangeRole(NodeType.Auto);
        }
        return node;
    }

    private void OnStatus(StatusPacket st, RemoteNode node)
    {
        var old = node.Status;
        node.Status = st;
        NodeChanged?.Invoke(this, new NodeEventArgs(node));
        if (!Settings.AutoRequestMetadata && !Settings.AutoRequestMetrics) return;
        for (int i = 0; i < 8; i++)
        {
            var now = st.Layers[i];
            var was = old?.Layers[i];
            if (now.TrackId == 0 && now.State == LayerState.Idle) continue;
            bool track = was is null || was.TrackId != now.TrackId;
            bool state = was is null || was.State != now.State;
            byte layer = (byte)(i + 1);
            if (Settings.AutoRequestMetadata && track) _ = Quietly(RequestAsync(node, DataType.Metadata, layer));
            if (Settings.AutoRequestMetrics && (track || state)) _ = Quietly(RequestAsync(node, DataType.Metrics, layer));
        }
    }

    private void OnData(DataPacket dp, IPEndPoint from, RemoteNode node)
    {
        int i = RemoteNode.Index(dp.LayerId);
        switch (dp)
        {
            case MetricsPacket m when i >= 0: node.Metrics[i] = m; break;
            case MetadataPacket md when i >= 0: node.Metadata[i] = md; break;
            case CueDataPacket c when i >= 0: node.Cues[i] = c; break;
            case MixerDataPacket mx: node.Mixer = mx; break;
        }

        if (dp is ChunkedPacket chunk)
        {
            if (Assembler.Add(chunk, from) is not { } data) return;
            if (i >= 0)
            {
                switch (data.DataType)
                {
                    case DataType.BeatGrid: node.BeatGrids[i] = data.ToBeatGrid(); break;
                    case DataType.SmallWaveform: node.SmallWaveforms[i] = data.ToWaveform(); break;
                    case DataType.BigWaveform: node.BigWaveforms[i] = data.ToWaveform(); break;
                    case DataType.LowResArtwork: node.Artwork[i] = data.Data; break;
                }
            }
            DataAssembled?.Invoke(this, new DataEventArgs(data, node));
            Complete(node, dp.DataType, dp.LayerId, new RequestResult(dp.DataType, dp.LayerId, dp, data, null, false));
        }
        else Complete(node, dp.DataType, dp.LayerId, new RequestResult(dp.DataType, dp.LayerId, dp, null, null, false));
    }

    private void OnNotification(ErrorNotificationPacket en, RemoteNode node)
    {
        if (en.RequestType is (ushort)MessageType.Control or (ushort)MessageType.TextData)
        {
            if (_controls.TryRemove(node.Key, out var c)) c.TrySetResult(en);
            return;
        }
        var type = (DataType)en.DataType;
        Complete(node, type, en.LayerId, new RequestResult(type, en.LayerId, null, null, en, false));
    }

    private static string RequestKey(RemoteNode n, DataType t, byte layer) => $"{n.Key}/{(byte)t}/{layer}";

    private void Complete(RemoteNode n, DataType t, byte layer, RequestResult r)
    {
        if (_requests.TryRemove(RequestKey(n, t, layer), out var tcs)) tcs.TrySetResult(r);
    }

    private void OnTimeSync(TimeSyncPacket ts, IPEndPoint from, RemoteNode node)
    {
        if (ts.Step == Step.Initialize)
        {
            if (Settings.AnswerTimeSync)
                _ = Quietly(SendAsync(new TimeSyncPacket { Step = Step.Response, ListenerPort = (ushort)ListenerPort, RemoteTimestamp = ts.Timestamp }, ReplyTo(node, from)));
            return;
        }
        var result = TimeSync.Compute(ts, Clock.Timestamp);
        node.Sync = result;
        node.SyncedAt = DateTime.UtcNow;
        TimeSynced?.Invoke(this, new SyncEventArgs(node, result));
        if (_syncs.TryRemove(node.Key, out var tcs)) tcs.TrySetResult(result);
    }

    private void OnRequest(RequestPacket rq, IPEndPoint from, RemoteNode node)
    {
        var to = ReplyTo(node, from);
        var code = NotificationCode.RequestNotPossible;
        if (RequestHandler is { } handler)
        {
            try
            {
                var reply = handler(rq, node);
                if (reply is { Count: > 0 })
                {
                    _ = Quietly(SendManyAsync(reply, to));
                    return;
                }
                code = NotificationCode.RequestDataEmpty;
            }
            catch (Exception ex) { Warn($"RequestHandler: {ex.Message}"); }
        }
        _ = Quietly(SendAsync(new ErrorNotificationPacket
        {
            DataType = (byte)rq.DataType,
            LayerId = rq.Layer,
            Code = code,
            RequestType = (ushort)MessageType.Request,
        }, to));
    }

    private void OnControl(ControlPacket cp, IPEndPoint from, RemoteNode node)
    {
        if (cp.Step != Step.Initialize) return;
        var code = NotificationCode.RequestNotPossible;
        try { if (ControlHandler is { } h) code = h(cp, node); }
        catch (Exception ex) { Warn($"ControlHandler: {ex.Message}"); }
        _ = Quietly(SendAsync(new ErrorNotificationPacket { Code = code, RequestType = (ushort)MessageType.Control }, ReplyTo(node, from)));
    }

    private static IPEndPoint ReplyTo(RemoteNode n, IPEndPoint from) => n.ListenerPort > 0 ? new IPEndPoint(from.Address, n.ListenerPort) : from;

    // ─────────────── housekeeping ───────────────

    private async Task Housekeeping(CancellationToken ct)
    {
        var lastSync = new Dictionary<string, DateTime>();
        using var timer = new PeriodicTimer(Settings.OptInInterval);
        do
        {
            try { await SendOptInAsync().ConfigureAwait(false); }
            catch (Exception ex) when (ex is SocketException or ObjectDisposedException or InvalidOperationException) { if (ct.IsCancellationRequested) break; Warn($"Opt-IN: {ex.Message}"); }

            if (Settings.SendStatus ?? NodeType is NodeType.Master or NodeType.Repeater)
            {
                try { await SendStatusAsync().ConfigureAwait(false); }
                catch (Exception ex) { if (ct.IsCancellationRequested) break; Warn($"Status: {ex.Message}"); }
            }

            Prune();
            ElectionTick();

            if (Settings.AutoTimeSync)
            {
                var now = DateTime.UtcNow;
                foreach (var n in Nodes)
                {
                    if (n.EndPoint is null || (lastSync.TryGetValue(n.Key, out var t) && now - t < Settings.TimeSyncInterval)) continue;
                    lastSync[n.Key] = now;
                    _ = Quietly(TimeSyncAsync(n));
                }
            }
        }
        while (await Tick(timer, ct).ConfigureAwait(false));
    }

    private static async Task<bool> Tick(PeriodicTimer t, CancellationToken ct)
    {
        try { return await t.WaitForNextTickAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return false; }
    }

    private void Prune()
    {
        var cutoff = DateTime.UtcNow - Settings.NodeTimeout;
        List<RemoteNode> gone;
        lock (_nodesGate)
        {
            gone = _nodes.Values.Where(n => n.LastSeen < cutoff).ToList();
            foreach (var n in gone) _nodes.Remove(n.Key);
        }
        foreach (var n in gone) NodeLost?.Invoke(this, new NodeEventArgs(n, "timeout"));
        if (gone.Any(n => n.NodeType == NodeType.Master)) BeginElection();
    }

    private void BeginElection()
    {
        if (Settings.AutoMasterElection && NodeType == NodeType.Auto && _electionRounds < 0) _electionRounds = 0;
        ElectionTick();
    }

    private void ElectionTick()
    {
        if (_electionRounds < 0) return;
        var nodes = Nodes;
        if (!Settings.AutoMasterElection || NodeType != NodeType.Auto || nodes.Any(n => n.NodeType == NodeType.Master))
        {
            _electionRounds = -1;
            return;
        }
        _electionRounds++;
        var self = new MasterElection.Candidate(Settings.NodeId, Clock.Uptime, Clock.Timestamp);
        var others = nodes.Where(n => n.NodeType == NodeType.Auto).Select(MasterElection.Of).ToList();
        bool win = _electionRounds > 3
            ? others.All(o => Settings.NodeId < o.NodeId)
            : others.All(o => MasterElection.Beats(self, o));
        if (!win) return;
        _electionRounds = -1;
        _elected = true;
        ChangeRole(NodeType.Master);
    }

    // ─────────────── send ───────────────

    public OptInPacket CreateOptIn() => new()
    {
        NodeCount = (ushort)Nodes.Count,
        ListenerPort = (ushort)ListenerPort,
        Uptime = Clock.Uptime,
        VendorName = Settings.VendorName,
        DeviceName = Settings.DeviceName,
        DeviceMajor = Settings.DeviceMajor,
        DeviceMinor = Settings.DeviceMinor,
        DeviceBug = Settings.DeviceBug,
    };

    /// <summary>Broadcasts Opt-IN to 60000 and unicasts it to every known node.</summary>
    public async Task SendOptInAsync()
    {
        var p = CreateOptIn();
        await BroadcastAsync(p, TCNetConstants.BroadcastPort).ConfigureAwait(false);
        if (!Settings.UnicastOptIn) return;
        foreach (var n in Nodes)
            if (n.EndPoint is { } ep) await SendAsync(p, ep).ConfigureAwait(false);
    }

    /// <summary>Broadcasts Status (content from <see cref="StatusProvider"/>) and unicasts it to slaves.</summary>
    public async Task SendStatusAsync()
    {
        StatusPacket st;
        try { st = StatusProvider?.Invoke() ?? new StatusPacket(); }
        catch (Exception ex)
        {
            Warn($"StatusProvider: {ex.Message}");
            st = new StatusPacket();
        }
        st.NodeCount = (ushort)Nodes.Count;
        st.ListenerPort = (ushort)ListenerPort;
        await BroadcastAsync(st, TCNetConstants.BroadcastPort).ConfigureAwait(false);
        await SendToSlavesAsync(st).ConfigureAwait(false);
    }

    /// <summary>Fills the header: ID, name, role, options, version, SEQ (per message type) and timestamp.</summary>
    public void Stamp(TCNetPacket p)
    {
        p.NodeId = Settings.NodeId;
        p.NodeName = OwnName;
        p.NodeType = NodeType;
        p.NodeOptions = Settings.NodeOptions;
        p.VersionMajor = Settings.VersionMajor;
        p.VersionMinor = Settings.VersionMinor;
        lock (_seq) p.Sequence = _seq[(byte)p.MessageType]++;
        p.Timestamp = Clock.Timestamp;
    }

    public async Task SendAsync(TCNetPacket packet, IPEndPoint to, bool stamp = true, CancellationToken ct = default)
    {
        var s = _socket ?? throw new InvalidOperationException("The node is not running.");
        if (stamp) Stamp(packet);
        await s.SendToAsync(packet.ToArray(), SocketFlags.None, to, ct).ConfigureAwait(false);
        Interlocked.Increment(ref _sent);
        PacketSent?.Invoke(this, new PacketEventArgs(packet, to, to.Port, true, null));
    }

    public Task SendAsync(TCNetPacket packet, RemoteNode node, CancellationToken ct = default) =>
        SendAsync(packet, node.EndPoint ?? throw new InvalidOperationException($"{node.NodeName}'s listener port is unknown."), true, ct);

    /// <summary>Sends bytes as they are (hand-built or edited packets).</summary>
    public async Task SendRawAsync(byte[] datagram, IPEndPoint to, CancellationToken ct = default)
    {
        var s = _socket ?? throw new InvalidOperationException("The node is not running.");
        await s.SendToAsync(datagram, SocketFlags.None, to, ct).ConfigureAwait(false);
        Interlocked.Increment(ref _sent);
        if (TCNetParser.TryParse(datagram, out var p, out _))
            PacketSent?.Invoke(this, new PacketEventArgs(p!, to, to.Port, true, null));
    }

    public Task BroadcastAsync(TCNetPacket packet, int port, CancellationToken ct = default) =>
        SendAsync(packet, new IPEndPoint(BroadcastAddress, port), true, ct);

    public async Task SendManyAsync(IEnumerable<TCNetPacket> packets, IPEndPoint to, CancellationToken ct = default)
    {
        foreach (var p in packets) await SendAsync(p, to, true, ct).ConfigureAwait(false);
    }

    public async Task SendToAllAsync(TCNetPacket packet, CancellationToken ct = default)
    {
        foreach (var n in Nodes)
            if (n.EndPoint is { } ep) await SendAsync(packet, ep, true, ct).ConfigureAwait(false);
    }

    /// <summary>Unicast to every Slave and Auto node ("unicast to all slaves").</summary>
    public async Task SendToSlavesAsync(TCNetPacket packet, CancellationToken ct = default)
    {
        foreach (var n in Nodes)
            if (n.NodeType is NodeType.Slave or NodeType.Auto && n.EndPoint is { } ep) await SendAsync(packet, ep, true, ct).ConfigureAwait(false);
    }

    /// <summary>Time packet usage: broadcast to 60001, then unicast to each node on this machine.</summary>
    public async Task PublishTimeAsync(TimePacket packet, CancellationToken ct = default)
    {
        await BroadcastAsync(packet, TCNetConstants.TimePort, ct).ConfigureAwait(false);
        foreach (var n in Nodes)
            if (n.IsLocal && n.EndPoint is { } ep) await SendAsync(packet, ep, true, ct).ConfigureAwait(false);
    }

    public bool IsStreaming => _stream is { IsCompleted: false };

    /// <summary>Publishes a Time packet every <paramref name="interval"/> (spec: 1–40 ms).</summary>
    public void StartTimeStream(Func<TimePacket> source, TimeSpan interval)
    {
        StopTimeStreamAsync().GetAwaiter().GetResult();
        var cts = new CancellationTokenSource();
        _streamCts = cts;
        _stream = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(interval);
            while (await Tick(timer, cts.Token).ConfigureAwait(false))
            {
                try { await PublishTimeAsync(source(), cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) when (ex is SocketException or InvalidOperationException or ObjectDisposedException) { Warn($"Time stream: {ex.Message}"); }
            }
        });
    }

    public async Task StopTimeStreamAsync()
    {
        var cts = Interlocked.Exchange(ref _streamCts, null);
        var task = Interlocked.Exchange(ref _stream, null);
        if (cts is null) return;
        cts.Cancel();
        if (task is not null) { try { await task.ConfigureAwait(false); } catch { } }
        cts.Dispose();
    }

    // ─────────────── round trips ───────────────

    private static TimeSpan Clamp(TimeSpan t) =>
        t == Timeout.InfiniteTimeSpan || t.TotalMilliseconds >= int.MaxValue - 1 ? Timeout.InfiniteTimeSpan : t < TimeSpan.Zero ? TimeSpan.Zero : t;

    /// <summary>Starts (or joins) a pending round trip. The request is sent only by the caller that created it.</summary>
    private async Task<(bool Ok, T Value)> RoundTrip<T>(ConcurrentDictionary<string, TaskCompletionSource<T>> map, string key,
        Func<Task> send, TimeSpan? timeout, CancellationToken ct)
    {
        bool mine = false;
        var tcs = map.GetOrAdd(key, _ =>
        {
            mine = true;
            return new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        });
        if (mine)
        {
            try { await send().ConfigureAwait(false); }
            catch
            {
                map.TryRemove(new KeyValuePair<string, TaskCompletionSource<T>>(key, tcs));
                throw;
            }
        }
        try
        {
            return (true, await tcs.Task.WaitAsync(Clamp(timeout ?? Settings.RequestTimeout), ct).ConfigureAwait(false));
        }
        catch (TimeoutException)
        {
            map.TryRemove(new KeyValuePair<string, TaskCompletionSource<T>>(key, tcs));
            return (false, default!);
        }
    }

    /// <summary>Sends a Request and waits for the data (all chunks) or an Error/Notification. Concurrent identical requests share one.</summary>
    public async Task<RequestResult> RequestAsync(RemoteNode node, DataType type, byte layer, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var (ok, result) = await RoundTrip(_requests, RequestKey(node, type, layer),
            () => SendAsync(new RequestPacket { DataType = type, Layer = layer }, node, ct), timeout, ct).ConfigureAwait(false);
        return ok ? result : new RequestResult(type, layer, null, null, null, true);
    }

    public async Task<MetricsPacket?> RequestMetricsAsync(RemoteNode n, byte layer, CancellationToken ct = default) =>
        (await RequestAsync(n, DataType.Metrics, layer, null, ct).ConfigureAwait(false)).Packet as MetricsPacket;

    public async Task<MetadataPacket?> RequestMetadataAsync(RemoteNode n, byte layer, CancellationToken ct = default) =>
        (await RequestAsync(n, DataType.Metadata, layer, null, ct).ConfigureAwait(false)).Packet as MetadataPacket;

    public async Task<CueDataPacket?> RequestCuesAsync(RemoteNode n, byte layer, CancellationToken ct = default) =>
        (await RequestAsync(n, DataType.CueData, layer, null, ct).ConfigureAwait(false)).Packet as CueDataPacket;

    public async Task<BeatGrid?> RequestBeatGridAsync(RemoteNode n, byte layer, CancellationToken ct = default) =>
        (await RequestAsync(n, DataType.BeatGrid, layer, null, ct).ConfigureAwait(false)).Data?.ToBeatGrid();

    public async Task<Waveform?> RequestSmallWaveformAsync(RemoteNode n, byte layer, CancellationToken ct = default) =>
        (await RequestAsync(n, DataType.SmallWaveform, layer, null, ct).ConfigureAwait(false)).Data?.ToWaveform();

    public async Task<Waveform?> RequestBigWaveformAsync(RemoteNode n, byte layer, CancellationToken ct = default) =>
        (await RequestAsync(n, DataType.BigWaveform, layer, TimeSpan.FromSeconds(5), ct).ConfigureAwait(false)).Data?.ToWaveform();

    public async Task<byte[]?> RequestArtworkAsync(RemoteNode n, byte layer, CancellationToken ct = default) =>
        (await RequestAsync(n, DataType.LowResArtwork, layer, TimeSpan.FromSeconds(5), ct).ConfigureAwait(false)).Data?.Data;

    public async Task<MixerDataPacket?> RequestMixerAsync(RemoteNode n, byte mixerId = 0, CancellationToken ct = default) =>
        (await RequestAsync(n, DataType.Mixer, mixerId, null, ct).ConfigureAwait(false)).Packet as MixerDataPacket;

    /// <summary>Runs the time sync routine; several rounds are averaged. Throws <see cref="TimeoutException"/> without an answer.</summary>
    public async Task<TimeSyncResult> TimeSyncAsync(RemoteNode node, int rounds = 1, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var results = new List<TimeSyncResult>();
        for (int i = 0; i < Math.Max(1, rounds); i++)
        {
            var (ok, r) = await RoundTrip(_syncs, node.Key,
                () => SendAsync(new TimeSyncPacket { Step = Step.Initialize, ListenerPort = (ushort)ListenerPort }, node, ct), timeout, ct).ConfigureAwait(false);
            if (!ok) throw new TimeoutException($"No time sync answer from {node.NodeName}.");
            results.Add(r);
        }
        var result = results.Count == 1 ? results[0] : TimeSync.Average(results);
        node.Sync = result;
        node.SyncedAt = DateTime.UtcNow;
        return result;
    }

    /// <summary>
    /// Sends a control path and waits for the notification (null = no answer). Controls to one node are queued; the
    /// timeout starts when this control is actually sent.
    /// </summary>
    public async Task<ErrorNotificationPacket?> SendControlAsync(RemoteNode node, string controlPath, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var queue = _controlQueues.GetOrAdd(node.Key, _ => new SemaphoreSlim(1, 1));
        await queue.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var (ok, r) = await RoundTrip(_controls, node.Key,
                () => SendAsync(new ControlPacket { Text = controlPath }, node, ct), timeout, ct).ConfigureAwait(false);
            return ok ? r : null;
        }
        finally { queue.Release(); }
    }

    public Task<ErrorNotificationPacket?> SendControlAsync(RemoteNode node, params ControlCommand[] commands) =>
        SendControlAsync(node, ControlCommand.Join(commands));

    /// <summary>Text Data to a node, or broadcast to 60000 when <paramref name="node"/> is null.</summary>
    public Task SendTextAsync(string text, RemoteNode? node = null, CancellationToken ct = default) =>
        node is null ? BroadcastAsync(new TextDataPacket(text), TCNetConstants.BroadcastPort, ct) : SendAsync(new TextDataPacket(text), node, ct);

    /// <summary>Keyboard Data to a node, or broadcast to 60000.</summary>
    public Task SendKeyAsync(ushort code, RemoteNode? node = null, CancellationToken ct = default)
    {
        var p = new KeyboardDataPacket { KeyCode = code };
        return node is null ? BroadcastAsync(p, TCNetConstants.BroadcastPort, ct) : SendAsync(p, node, ct);
    }

    /// <summary>By key ("ip#id"), node name or IP address.</summary>
    public RemoteNode? FindNode(string query)
    {
        lock (_nodesGate)
        {
            if (_nodes.TryGetValue(query, out var n)) return n;
            return _nodes.Values.FirstOrDefault(x => x.NodeName.Equals(query, StringComparison.OrdinalIgnoreCase))
                   ?? _nodes.Values.FirstOrDefault(x => x.Address.ToString() == query);
        }
    }

    private async Task Quietly(Task t)
    {
        try { await t.ConfigureAwait(false); }
        catch (Exception ex) when (ex is SocketException or InvalidOperationException or ObjectDisposedException or TimeoutException or OperationCanceledException)
        {
            if (ex is SocketException) Warn(ex.Message);
        }
    }

    private void Warn(string message) => Warning?.Invoke(this, message);
}
