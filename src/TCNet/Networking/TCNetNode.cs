using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace TCNet.Networking;

/// <summary>A received (or sent) packet with its endpoints.</summary>
public sealed class TCNetPacketEventArgs(TCNetPacket packet, IPEndPoint remote, int localPort, bool outgoing, TCNetRemoteNode? node) : EventArgs
{
    public TCNetPacket Packet { get; } = packet;
    public IPEndPoint RemoteEndPoint { get; } = remote;
    /// <summary>Local port the datagram arrived on (60000/60001/60002/listener), or the port it was sent to.</summary>
    public int LocalPort { get; } = localPort;
    public bool Outgoing { get; } = outgoing;
    public TCNetRemoteNode? Node { get; } = node;
    public DateTime Time { get; } = DateTime.Now;
}

/// <summary>A datagram that is not a valid TCNet packet.</summary>
public sealed class TCNetInvalidDatagramEventArgs(byte[] data, IPEndPoint remote, int localPort, string reason) : EventArgs
{
    public byte[] Data { get; } = data;
    public IPEndPoint RemoteEndPoint { get; } = remote;
    public int LocalPort { get; } = localPort;
    public string Reason { get; } = reason;
}

public sealed class TCNetNodeEventArgs(TCNetRemoteNode node, string? reason = null) : EventArgs
{
    public TCNetRemoteNode Node { get; } = node;
    /// <summary>For NodeLost: "Opt-OUT" or "Timeout".</summary>
    public string? Reason { get; } = reason;
}

public sealed class TCNetAssembledEventArgs(AssembledData data, TCNetRemoteNode? node) : EventArgs
{
    public AssembledData Data { get; } = data;
    public TCNetRemoteNode? Node { get; } = node;
}

public sealed class TCNetTimeSyncEventArgs(TCNetRemoteNode node, TimeSyncSample sample) : EventArgs
{
    public TCNetRemoteNode Node { get; } = node;
    public TimeSyncSample Sample { get; } = sample;
}

/// <summary>Result of a data request.</summary>
public sealed record TCNetRequestResult(
    DataType DataType,
    byte Layer,
    TCNetPacket? Packet,
    AssembledData? Data,
    ErrorNotificationPacket? Notification,
    bool TimedOut)
{
    public bool Success => !TimedOut && (Packet is not null || Data is not null);

    public override string ToString() =>
        TimedOut ? $"{DataType} L{Layer}: timed out"
        : Notification is not null ? $"{DataType} L{Layer}: {Text.TCNetText.Describe(Notification.Code)}"
        : Data is not null ? $"{DataType} L{Layer}: {Data.Data.Length} bytes in {Data.PacketCount} packets"
        : $"{DataType} L{Layer}: {Packet?.Summary}";
}

/// <summary>
/// A local TCNet node: joins the network (Opt-IN every second, broadcast + unicast), keeps the population list,
/// answers time sync, requests and control messages, and sends any packet.
/// </summary>
public sealed class TCNetNode : IAsyncDisposable, IDisposable
{
    private readonly ConcurrentDictionary<string, TCNetRemoteNode> _nodes = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<TCNetRequestResult>> _pendingRequests = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<TimeSyncSample>> _pendingSyncs = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<ErrorNotificationPacket>> _pendingControl = new();
    private readonly byte[] _sequences = new byte[256];
    private readonly List<Socket> _broadcastSockets = [];
    private readonly List<Task> _loops = [];
    private Socket? _unicast;
    private CancellationTokenSource? _cts;
    private Task? _stopTask;
    private volatile bool _electionPending, _electedMaster;
    private readonly object _lifecycleGate = new();
    private HashSet<IPAddress> _localAddresses = [];
    private IPAddress _broadcast = IPAddress.Broadcast;
    private Task? _timeStream;
    private CancellationTokenSource? _timeStreamCts;
    private readonly object _sendGate = new();

    public TCNetNode(TCNetNodeSettings? settings = null)
    {
        Settings = settings ?? new TCNetNodeSettings();
        NodeType = Settings.NodeType;
    }

    public TCNetNodeSettings Settings { get; }

    /// <summary>Current role (may change through master election or <see cref="SetNodeType"/>).</summary>
    public NodeType NodeType { get; private set; }

    public TCNetClock Clock { get; } = new();

    public TCNetChunkAssembler Assembler { get; } = new();

    public bool IsRunning => _cts is not null;

    /// <summary>The bound unicast listener port.</summary>
    public int ListenerPort { get; private set; }

    /// <summary>Broadcast ports that were bound successfully.</summary>
    public IReadOnlyList<int> BoundBroadcastPorts { get; private set; } = [];

    /// <summary>Broadcast destination in use.</summary>
    public IPAddress BroadcastAddress => _broadcast;

    /// <summary>The population list.</summary>
    public IReadOnlyList<TCNetRemoteNode> Nodes => _nodes.Values.ToList();

    private long _packetsReceived, _packetsSent;
    public long PacketsReceived => Interlocked.Read(ref _packetsReceived);
    public long PacketsSent => Interlocked.Read(ref _packetsSent);

    /// <summary>Called when a Request arrives. Return packets to send back, or null/empty for "data empty".</summary>
    public Func<RequestPacket, TCNetRemoteNode?, IReadOnlyList<TCNetPacket>?>? RequestHandler { get; set; }

    /// <summary>Called when a Control packet arrives. The returned code is sent back in a notification.</summary>
    public Func<ControlPacket, TCNetRemoteNode?, NotificationCode>? ControlHandler { get; set; }

    /// <summary>Supplies layer content for the periodic Status broadcast (header fields are filled in).</summary>
    public Func<StatusPacket>? StatusProvider { get; set; }

    // ---- Events ----

    public event EventHandler<TCNetPacketEventArgs>? PacketReceived;
    public event EventHandler<TCNetPacketEventArgs>? PacketSent;
    public event EventHandler<TCNetInvalidDatagramEventArgs>? InvalidDatagram;
    public event EventHandler<TCNetNodeEventArgs>? NodeDiscovered;
    public event EventHandler<TCNetNodeEventArgs>? NodeUpdated;
    public event EventHandler<TCNetNodeEventArgs>? NodeLost;
    public event EventHandler<TCNetAssembledEventArgs>? DataAssembled;
    public event EventHandler<TCNetTimeSyncEventArgs>? TimeSynced;
    public event EventHandler<NodeType>? RoleChanged;
    public event EventHandler<string>? Warning;

    // ================= Lifecycle =================

    /// <summary>Opens sockets and starts Opt-IN, receive and housekeeping loops.</summary>
    public void Start()
    {
        lock (_lifecycleGate) StartCore();
    }

    private void StartCore()
    {
        if (_cts is not null) return;
        _localAddresses = TCNetNetwork.LocalAddresses();
        _broadcast = ResolveBroadcast();
        _unicast = BindUnicast();
        try
        {
            OpenBroadcastSockets();
        }
        catch
        {
            // Leave the node stopped with nothing bound so Start can be retried.
            foreach (var s in _broadcastSockets) s.Dispose();
            _broadcastSockets.Clear();
            _unicast.Dispose();
            _unicast = null;
            throw;
        }

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;
        _loops.Add(Task.Run(() => ReceiveLoop(_unicast, ListenerPort, ct)));
        foreach (var s in _broadcastSockets)
        {
            int port = ((IPEndPoint)s.LocalEndPoint!).Port;
            _loops.Add(Task.Run(() => ReceiveLoop(s, port, ct)));
        }
        _loops.Add(Task.Run(() => HousekeepingLoop(ct)));
    }

    private void OpenBroadcastSockets()
    {
        ListenerPort = ((IPEndPoint)_unicast!.LocalEndPoint!).Port;

        var bound = new List<int>();
        if (Settings.ListenOnBroadcastPorts)
        {
            foreach (int port in new[] { TCNetConstants.BroadcastPort, TCNetConstants.TimePort, TCNetConstants.ApplicationPort })
            {
                try
                {
                    _broadcastSockets.Add(BindShared(port));
                    bound.Add(port);
                }
                catch (SocketException ex)
                {
                    Warn($"Could not bind UDP {port} ({ex.SocketErrorCode}); only unicast will be received on it.");
                }
            }
        }
        BoundBroadcastPorts = bound;
    }

    /// <inheritdoc cref="Start"/>
    public Task StartAsync()
    {
        Start();
        return Task.CompletedTask;
    }

    /// <summary>Sends Opt-OUT (broadcast + unicast), then closes sockets. Concurrent calls share one stop.</summary>
    public Task StopAsync()
    {
        lock (_lifecycleGate)
        {
            if (_stopTask is { IsCompleted: false } running) return running;
            if (_cts is null) return Task.CompletedTask;
            return _stopTask = StopCoreAsync(_cts);
        }
    }

    private async Task StopCoreAsync(CancellationTokenSource cts)
    {
        await StopTimeStreamAsync().ConfigureAwait(false);
        try
        {
            var optOut = new OptOutPacket { NodeCount = (ushort)_nodes.Count, ListenerPort = (ushort)ListenerPort };
            await BroadcastAsync(optOut, TCNetConstants.BroadcastPort).ConfigureAwait(false);
            foreach (var n in _nodes.Values)
                if (n.EndPoint is { } ep) await SendAsync(optOut, ep).ConfigureAwait(false);
        }
        catch (Exception ex) { Warn($"Opt-OUT: {ex.Message}"); }

        cts.Cancel();
        _unicast?.Dispose();
        foreach (var s in _broadcastSockets) s.Dispose();
        try { await Task.WhenAll(_loops).ConfigureAwait(false); } catch { /* loops end on dispose */ }
        _loops.Clear();
        _broadcastSockets.Clear();
        _unicast = null;
        cts.Dispose();
        _cts = null;
        _nodes.Clear();
        Assembler.Clear();
        foreach (var p in _pendingRequests.Values) p.TrySetCanceled();
        foreach (var p in _pendingSyncs.Values) p.TrySetCanceled();
        foreach (var p in _pendingControl.Values) p.TrySetCanceled();
        _pendingRequests.Clear();
        _pendingSyncs.Clear();
        _pendingControl.Clear();
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    public void Dispose() => StopAsync().GetAwaiter().GetResult();

    /// <summary>Changes the advertised role (e.g. switch to Master to start sending).</summary>
    public void SetNodeType(NodeType type)
    {
        if (NodeType == type) return;
        _electedMaster = false;
        NodeType = type;
        RoleChanged?.Invoke(this, type);
    }

    // ================= Sockets =================

    private Socket BindUnicast()
    {
        if (Settings.ListenerPort > 0) return BindExclusive(Settings.ListenerPort);
        SocketException? last = null;
        for (int port = TCNetConstants.UnicastPortMin; port <= TCNetConstants.UnicastPortMax; port++)
        {
            try { return BindExclusive(port); }
            catch (SocketException ex) { last = ex; }
        }
        throw last ?? new SocketException((int)SocketError.AddressAlreadyInUse);
    }

    private Socket BindExclusive(int port)
    {
        var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            s.EnableBroadcast = true;
            DisableConnReset(s);
            s.Bind(new IPEndPoint(Settings.LocalAddress, port));
            return s;
        }
        catch
        {
            s.Dispose();
            throw;
        }
    }

    private static Socket BindShared(int port)
    {
        var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        try
        {
            if (OperatingSystem.IsWindows()) s.ExclusiveAddressUse = false;
            s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            s.EnableBroadcast = true;
            DisableConnReset(s);
            // Broadcasts are only delivered to sockets bound to the wildcard address on most platforms.
            s.Bind(new IPEndPoint(IPAddress.Any, port));
            return s;
        }
        catch
        {
            s.Dispose();
            throw;
        }
    }

    private static void DisableConnReset(Socket s)
    {
        if (!OperatingSystem.IsWindows()) return;
        const int SIO_UDP_CONNRESET = -1744830452;
        try { s.IOControl(SIO_UDP_CONNRESET, [0, 0, 0, 0], null); } catch (SocketException) { }
    }

    private IPAddress ResolveBroadcast()
    {
        if (Settings.BroadcastAddress is { } b) return b;
        if (!Settings.LocalAddress.Equals(IPAddress.Any))
        {
            var iface = TCNetNetwork.GetInterfaces().FirstOrDefault(i => i.Address.Equals(Settings.LocalAddress));
            if (iface is not null) return iface.Broadcast;
        }
        return IPAddress.Broadcast;
    }

    private async Task ReceiveLoop(Socket socket, int localPort, CancellationToken ct)
    {
        var buffer = new byte[65536];
        EndPoint any = new IPEndPoint(IPAddress.Any, 0);
        while (!ct.IsCancellationRequested)
        {
            SocketReceiveFromResult r;
            try
            {
                r = await socket.ReceiveFromAsync(buffer, SocketFlags.None, any, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch (ObjectDisposedException) { break; }
            catch (SocketException ex) when (ex.SocketErrorCode is SocketError.ConnectionReset or SocketError.MessageSize) { continue; }
            catch (SocketException ex)
            {
                if (ct.IsCancellationRequested) break;
                Warn($"Receive on {localPort}: {ex.SocketErrorCode}");
                await Task.Delay(200, CancellationToken.None).ConfigureAwait(false);
                continue;
            }

            try
            {
                HandleDatagram(buffer.AsSpan(0, r.ReceivedBytes), (IPEndPoint)r.RemoteEndPoint, localPort);
            }
            catch (Exception ex)
            {
                Warn($"Handling datagram from {r.RemoteEndPoint}: {ex.Message}");
            }
        }
    }

    // ================= Receive =================

    /// <summary>Processes a datagram as if it had been received (useful for replaying captures and tests).</summary>
    public void InjectDatagram(ReadOnlySpan<byte> data, IPEndPoint remote, int localPort = 0) => HandleDatagram(data, remote, localPort);

    private void HandleDatagram(ReadOnlySpan<byte> data, IPEndPoint remote, int localPort)
    {
        if (!TCNetPacketParser.TryParse(data, out var packet, out var error))
        {
            InvalidDatagram?.Invoke(this, new TCNetInvalidDatagramEventArgs(data.ToArray(), remote, localPort, error ?? "invalid"));
            return;
        }

        bool own = packet!.NodeId == Settings.NodeId && packet.NodeName == TruncatedName && _localAddresses.Contains(remote.Address);
        if (own && !Settings.ReceiveOwnPackets) return;

        Interlocked.Increment(ref _packetsReceived);
        TCNetRemoteNode? node = own ? null : Track(packet, remote);
        PacketReceived?.Invoke(this, new TCNetPacketEventArgs(packet, remote, localPort, false, node));
        if (own) return;

        switch (packet)
        {
            case TimeSyncPacket ts: OnTimeSync(ts, remote, node!); break;
            case RequestPacket rq: OnRequest(rq, remote, node!); break;
            case ControlPacket cp: OnControl(cp, remote, node!); break;
            case ErrorNotificationPacket en: OnNotification(en, node!); break;
            case StatusPacket st: OnStatus(st, node!); break;
            case TimePacket tp:
                node!.LastTime = tp;
                node.LastTimeReceived = DateTime.UtcNow;
                break;
            case DataPacket dp: OnData(dp, remote, node!); break;
            case ApplicationDataPacket ap:
                if (Assembler.Add(ap, remote) is { } appData) DataAssembled?.Invoke(this, new TCNetAssembledEventArgs(appData, node));
                break;
        }
    }

    private string TruncatedName => Settings.NodeName.Length > 8 ? Settings.NodeName[..8] : Settings.NodeName;

    private TCNetRemoteNode? Track(TCNetPacket packet, IPEndPoint remote)
    {
        string key = TCNetRemoteNode.MakeKey(remote.Address, packet.NodeId);

        if (packet is OptOutPacket)
        {
            if (_nodes.TryRemove(key, out var gone))
            {
                NodeLost?.Invoke(this, new TCNetNodeEventArgs(gone, "Opt-OUT"));
                if (gone.NodeType == NodeType.Master) EvaluateElection();
            }
            return gone;
        }

        // Receive loops run concurrently: only the thread whose instance was stored reports discovery.
        bool isNew = false;
        if (!_nodes.TryGetValue(key, out var node))
        {
            var created = new TCNetRemoteNode(remote.Address, packet.NodeId)
            {
                IsLocal = _localAddresses.Contains(remote.Address),
                ListenerPort = remote.Port is >= TCNetConstants.UnicastPortMin and <= TCNetConstants.UnicastPortMax ? remote.Port : 0,
            };
            node = _nodes.GetOrAdd(key, created);
            isNew = ReferenceEquals(node, created);
        }

        bool significant;
        lock (node)
        {
            var previousType = node.NodeType;
            node.NodeName = packet.NodeName;
            node.NodeType = packet.NodeType;
            node.NodeOptions = packet.NodeOptions;
            node.ProtocolVersion = packet.ProtocolVersion;
            node.LastTimestamp = packet.Timestamp;
            node.LastSeen = DateTime.UtcNow;
            node.PacketsReceived++;

            significant = isNew || previousType != node.NodeType;
            switch (packet)
            {
                case OptInPacket oi:
                    if (oi.ListenerPort != 0) node.ListenerPort = oi.ListenerPort;
                    node.NodeCount = oi.NodeCount;
                    node.Uptime = oi.Uptime;
                    node.VendorName = oi.VendorName;
                    node.ApplicationName = oi.ApplicationName;
                    node.ApplicationVersion = oi.ApplicationVersion;
                    node.LastOptIn = DateTime.UtcNow;
                    node.LastOptInPacket = oi;
                    significant = true;
                    break;
                case StatusPacket st:
                    if (st.ListenerPort != 0) node.ListenerPort = st.ListenerPort;
                    node.NodeCount = st.NodeCount;
                    break;
                case TimeSyncPacket ts when ts.ListenerPort != 0:
                    node.ListenerPort = ts.ListenerPort;
                    break;
            }
        }

        if (isNew) NodeDiscovered?.Invoke(this, new TCNetNodeEventArgs(node));
        else if (significant) NodeUpdated?.Invoke(this, new TCNetNodeEventArgs(node));
        return node;
    }

    private void OnStatus(StatusPacket st, TCNetRemoteNode node)
    {
        StatusPacket? previous;
        lock (node)
        {
            previous = node.LastStatus;
            node.LastStatus = st;
        }
        NodeUpdated?.Invoke(this, new TCNetNodeEventArgs(node));
        if (!Settings.AutoRequestMetadata && !Settings.AutoRequestMetrics) return;

        for (int i = 0; i < TCNetConstants.LayerCount; i++)
        {
            var now = st.Layers[i];
            var before = previous?.Layers[i];
            bool trackChanged = before is null || before.TrackId != now.TrackId;
            bool stateChanged = before is null || before.State != now.State;
            if (now.TrackId == 0 && now.State == LayerState.Idle) continue;
            byte layer = (byte)(i + 1);
            if (Settings.AutoRequestMetadata && trackChanged) _ = SafeRequest(node, DataType.Metadata, layer);
            if (Settings.AutoRequestMetrics && (trackChanged || stateChanged)) _ = SafeRequest(node, DataType.Metrics, layer);
        }
    }

    private async Task SafeRequest(TCNetRemoteNode node, DataType type, byte layer)
    {
        try { await RequestAsync(node, type, layer).ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Warn($"Auto request {type} layer {layer} from {node}: {ex.Message}"); }
    }

    private void OnData(DataPacket dp, IPEndPoint remote, TCNetRemoteNode node)
    {
        int li = TCNetRemoteNode.LayerIndex(dp.LayerId);
        switch (dp)
        {
            case MetricsDataPacket m when li >= 0: node.Metrics[li] = m; break;
            case MetadataPacket md when li >= 0: node.Metadata[li] = md; break;
            case CueDataPacket c when li >= 0: node.Cues[li] = c; break;
            case SmallWaveformPacket sw when li >= 0 && sw.TotalPackets <= 1: node.SmallWaveforms[li] = sw.Waveform; break;
            case MixerDataPacket mx: node.LastMixer = mx; break;
        }

        if (dp is ChunkedDataPacket chunk)
        {
            var assembled = Assembler.Add(chunk, remote);
            if (assembled is null) return;
            if (li >= 0)
            {
                switch (assembled.DataType)
                {
                    case DataType.BeatGrid: node.BeatGrids[li] = assembled.AsBeatGrid(); break;
                    case DataType.SmallWaveform: node.SmallWaveforms[li] = assembled.AsWaveform(); break;
                    case DataType.BigWaveform: node.BigWaveforms[li] = assembled.AsWaveform(); break;
                    case DataType.LowResArtwork: node.Artwork[li] = assembled.Data; break;
                }
            }
            DataAssembled?.Invoke(this, new TCNetAssembledEventArgs(assembled, node));
            Complete(node, assembled.DataType, assembled.Layer, new TCNetRequestResult(assembled.DataType, assembled.Layer, dp, assembled, null, false));
        }
        else
        {
            Complete(node, dp.DataType, dp.LayerId, new TCNetRequestResult(dp.DataType, dp.LayerId, dp, null, null, false));
        }
    }

    private void OnNotification(ErrorNotificationPacket en, TCNetRemoteNode node)
    {
        if (en.RequestMessageType == (ushort)MessageType.Control || en.RequestMessageType == (ushort)MessageType.TextData)
        {
            if (_pendingControl.TryRemove(node.Key, out var ctl)) ctl.TrySetResult(en);
            return;
        }
        Complete(node, (DataType)en.DataType, en.LayerId, new TCNetRequestResult((DataType)en.DataType, en.LayerId, null, null, en, false));
    }

    private void Complete(TCNetRemoteNode node, DataType type, byte layer, TCNetRequestResult result)
    {
        if (_pendingRequests.TryRemove(RequestKey(node, type, layer), out var tcs)) tcs.TrySetResult(result);
    }

    private static string RequestKey(TCNetRemoteNode node, DataType type, byte layer) => $"{node.Key}/{(byte)type}/{layer}";

    private void OnTimeSync(TimeSyncPacket ts, IPEndPoint remote, TCNetRemoteNode node)
    {
        if (ts.Step == SyncStep.Initialize)
        {
            if (!Settings.AnswerTimeSync) return;
            var reply = new TimeSyncPacket { Step = SyncStep.Response, ListenerPort = (ushort)ListenerPort, RemoteTimestamp = ts.Timestamp };
            _ = SendSafe(reply, ReplyEndPoint(node, remote));
        }
        else
        {
            var sample = TimeSync.Compute(ts, Clock.Timestamp);
            node.ClockOffsetMicros = sample.OffsetMicros;
            node.DelayMicros = sample.DelayMicros;
            node.LastTimeSync = DateTime.UtcNow;
            TimeSynced?.Invoke(this, new TCNetTimeSyncEventArgs(node, sample));
            if (_pendingSyncs.TryRemove(node.Key, out var tcs)) tcs.TrySetResult(sample);
        }
    }

    private void OnRequest(RequestPacket rq, IPEndPoint remote, TCNetRemoteNode node)
    {
        var target = ReplyEndPoint(node, remote);
        IReadOnlyList<TCNetPacket>? reply = null;
        NotificationCode code = NotificationCode.RequestNotPossible;
        try
        {
            if (RequestHandler is not null)
            {
                reply = RequestHandler(rq, node);
                code = NotificationCode.RequestDataEmpty;
            }
        }
        catch (Exception ex)
        {
            Warn($"RequestHandler failed: {ex.Message}");
            code = NotificationCode.RequestNotPossible;
            reply = null;
        }

        if (reply is { Count: > 0 })
        {
            _ = SendManySafe(reply, target);
            return;
        }
        var err = new ErrorNotificationPacket
        {
            DataType = (byte)rq.DataType,
            LayerId = rq.Layer,
            Code = code,
            RequestMessageType = (ushort)MessageType.Request,
        };
        _ = SendSafe(err, target);
    }

    private void OnControl(ControlPacket cp, IPEndPoint remote, TCNetRemoteNode node)
    {
        if (cp.Step != SyncStep.Initialize) return;
        NotificationCode code = NotificationCode.RequestNotPossible;
        try
        {
            if (ControlHandler is not null) code = ControlHandler(cp, node);
        }
        catch (Exception ex)
        {
            Warn($"ControlHandler failed: {ex.Message}");
        }
        var err = new ErrorNotificationPacket { Code = code, RequestMessageType = (ushort)MessageType.Control };
        _ = SendSafe(err, ReplyEndPoint(node, remote));
    }

    private static IPEndPoint ReplyEndPoint(TCNetRemoteNode node, IPEndPoint remote) =>
        node.ListenerPort > 0 ? new IPEndPoint(remote.Address, node.ListenerPort) : remote;

    // ================= Housekeeping =================

    private async Task HousekeepingLoop(CancellationToken ct)
    {
        var lastSync = new Dictionary<string, DateTime>();
        using var timer = new PeriodicTimer(Settings.OptInInterval);
        do
        {
            try
            {
                await SendOptInAsync().ConfigureAwait(false);
                if (Settings.SendStatus ?? NodeType is NodeType.Master or NodeType.Repeater)
                    await SendStatusAsync().ConfigureAwait(false);
                PruneNodes();
                if (Settings.AutoTimeSync)
                {
                    var now = DateTime.UtcNow;
                    foreach (var k in lastSync.Keys.Where(k => !_nodes.ContainsKey(k)).ToList()) lastSync.Remove(k);
                    foreach (var n in _nodes.Values)
                    {
                        if (n.EndPoint is null) continue;
                        if (lastSync.TryGetValue(n.Key, out var t) && now - t < Settings.TimeSyncInterval) continue;
                        lastSync[n.Key] = now;
                        _ = SyncSafe(n);
                    }
                }
            }
            catch (Exception ex)
            {
                // Keep the node alive: a failing StatusProvider or event handler must not stop Opt-IN.
                if (ct.IsCancellationRequested) break;
                Warn($"Housekeeping: {ex.Message}");
            }
        }
        while (await WaitTick(timer, ct).ConfigureAwait(false));
    }

    private static async Task<bool> WaitTick(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { return false; }
    }

    private async Task SyncSafe(TCNetRemoteNode n)
    {
        try { await TimeSyncAsync(n).ConfigureAwait(false); }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException) { }
        catch (Exception ex) { Warn($"Time sync with {n}: {ex.Message}"); }
    }

    private void PruneNodes()
    {
        var cutoff = DateTime.UtcNow - Settings.NodeTimeout;
        bool masterLost = false;
        foreach (var (key, node) in _nodes)
        {
            if (node.LastSeen >= cutoff) continue;
            if (_nodes.TryRemove(key, out var gone))
            {
                masterLost |= gone.NodeType == NodeType.Master;
                NodeLost?.Invoke(this, new TCNetNodeEventArgs(gone, "Timeout"));
            }
        }
        if (masterLost) EvaluateElection();
        // Retry an election that could not decide yet, and resolve two elected masters.
        else if (_electionPending && !_nodes.Values.Any(n => n.NodeType == NodeType.Master)) EvaluateElection();
        else if (_electedMaster) EvaluateElection();
        if (_nodes.Values.Any(n => n.NodeType == NodeType.Master)) _electionPending = false;
    }

    private void EvaluateElection()
    {
        if (!Settings.AutoMasterElection) return;
        if (NodeType == NodeType.Auto)
        {
            _electionPending = true;
            if (TCNetMasterElection.ShouldPromote(Clock.Uptime, Settings.NodeId, TruncatedName, _nodes.Values))
            {
                _electionPending = false;
                SetNodeType(NodeType.Master);
                _electedMaster = true;
            }
        }
        else if (NodeType == NodeType.Master && _electedMaster)
        {
            // Two nodes promoted at once: the lower-ranked one returns to Auto.
            if (_nodes.Values.Any(n => TCNetMasterElection.ShouldDemote(Clock.Uptime, Settings.NodeId, TruncatedName, n)))
                SetNodeType(NodeType.Auto);
        }
    }

    // ================= Send =================

    /// <summary>Builds this node's Opt-IN packet.</summary>
    public OptInPacket CreateOptIn() => new()
    {
        NodeCount = (ushort)_nodes.Count,
        ListenerPort = (ushort)ListenerPort,
        Uptime = Clock.Uptime,
        VendorName = Settings.VendorName,
        ApplicationName = Settings.ApplicationName,
        ApplicationMajorVersion = Settings.ApplicationMajorVersion,
        ApplicationMinorVersion = Settings.ApplicationMinorVersion,
        ApplicationBugVersion = Settings.ApplicationBugVersion,
    };

    /// <summary>Broadcasts Opt-IN to 60000 and unicasts it to every known node.</summary>
    public async Task SendOptInAsync()
    {
        var optIn = CreateOptIn();
        await BroadcastAsync(optIn, TCNetConstants.BroadcastPort).ConfigureAwait(false);
        if (!Settings.UnicastOptInToKnownNodes) return;
        foreach (var n in _nodes.Values)
            if (n.EndPoint is { } ep) await SendAsync(optIn, ep).ConfigureAwait(false);
    }

    /// <summary>Broadcasts a Status packet (from <see cref="StatusProvider"/>) and unicasts it to slaves.</summary>
    public async Task SendStatusAsync()
    {
        var status = StatusProvider?.Invoke() ?? new StatusPacket();
        status.NodeCount = (ushort)_nodes.Count;
        status.ListenerPort = (ushort)ListenerPort;
        await BroadcastAsync(status, TCNetConstants.BroadcastPort).ConfigureAwait(false);
        await SendToSlavesAsync(status).ConfigureAwait(false);
    }

    /// <summary>Fills the management header (ID, name, type, options, version, SEQ per message type, timestamp).</summary>
    public void StampHeader(TCNetPacket packet)
    {
        packet.NodeId = Settings.NodeId;
        packet.NodeName = TruncatedName;
        packet.NodeType = NodeType;
        packet.NodeOptions = Settings.NodeOptions;
        packet.ProtocolMajor = Settings.ProtocolMajor;
        packet.ProtocolMinor = Settings.ProtocolMinor;
        lock (_sendGate) packet.Sequence = _sequences[(byte)packet.MessageType]++;
        packet.Timestamp = Clock.Timestamp;
    }

    /// <summary>Sends a packet to an endpoint. The header is stamped unless <paramref name="stampHeader"/> is false.</summary>
    public async Task SendAsync(TCNetPacket packet, IPEndPoint target, bool stampHeader = true, CancellationToken ct = default)
    {
        var socket = _unicast ?? throw new InvalidOperationException("Node is not started.");
        if (stampHeader) StampHeader(packet);
        var bytes = packet.ToArray();
        await socket.SendToAsync(bytes, SocketFlags.None, target, ct).ConfigureAwait(false);
        Interlocked.Increment(ref _packetsSent);
        PacketSent?.Invoke(this, new TCNetPacketEventArgs(packet, target, target.Port, true, null));
    }

    /// <summary>Sends raw bytes (e.g. a hand-edited packet) to an endpoint.</summary>
    public async Task SendRawAsync(byte[] datagram, IPEndPoint target, CancellationToken ct = default)
    {
        var socket = _unicast ?? throw new InvalidOperationException("Node is not started.");
        await socket.SendToAsync(datagram, SocketFlags.None, target, ct).ConfigureAwait(false);
        Interlocked.Increment(ref _packetsSent);
    }

    /// <summary>Sends to a node's listener port.</summary>
    public Task SendAsync(TCNetPacket packet, TCNetRemoteNode node, CancellationToken ct = default) =>
        SendAsync(packet, node.EndPoint ?? throw new InvalidOperationException($"Listener port of {node} is unknown."), true, ct);

    /// <summary>Broadcasts to <paramref name="port"/> on the configured broadcast address.</summary>
    public Task BroadcastAsync(TCNetPacket packet, int port, CancellationToken ct = default) =>
        SendAsync(packet, new IPEndPoint(_broadcast, port), true, ct);

    /// <summary>Unicasts to every known node.</summary>
    public async Task SendToAllAsync(TCNetPacket packet, CancellationToken ct = default)
    {
        foreach (var n in _nodes.Values)
            if (n.EndPoint is { } ep) await SendAsync(packet, ep, true, ct).ConfigureAwait(false);
    }

    /// <summary>Unicasts to every known Slave (and Auto) node.</summary>
    public async Task SendToSlavesAsync(TCNetPacket packet, CancellationToken ct = default)
    {
        foreach (var n in _nodes.Values)
            if (n.NodeType is NodeType.Slave or NodeType.Auto && n.EndPoint is { } ep)
                await SendAsync(packet, ep, true, ct).ConfigureAwait(false);
    }

    /// <summary>Time packet usage: broadcast to 60001 and unicast to every local node's port.</summary>
    public async Task PublishTimeAsync(TimePacket packet, CancellationToken ct = default)
    {
        await BroadcastAsync(packet, TCNetConstants.TimePort, ct).ConfigureAwait(false);
        foreach (var n in _nodes.Values)
            if (n.IsLocal && n.EndPoint is { } ep) await SendAsync(packet, ep, true, ct).ConfigureAwait(false);
    }

    /// <summary>Streams time packets at <paramref name="interval"/> (1–40 ms per spec) until stopped.</summary>
    public void StartTimeStream(Func<TimePacket> factory, TimeSpan interval)
    {
        _ = StopTimeStreamAsync();
        var cts = new CancellationTokenSource();
        _timeStreamCts = cts;
        _timeStream = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(interval);
            while (await WaitTick(timer, cts.Token).ConfigureAwait(false))
            {
                try { await PublishTimeAsync(factory(), cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                catch (Exception ex) when (ex is SocketException or InvalidOperationException) { Warn($"Time stream: {ex.Message}"); }
            }
        });
    }

    public bool IsTimeStreaming => _timeStream is { IsCompleted: false };

    public async Task StopTimeStreamAsync()
    {
        var cts = _timeStreamCts;
        var task = _timeStream;
        _timeStreamCts = null;
        _timeStream = null;
        if (cts is null) return;
        cts.Cancel();
        if (task is not null) { try { await task.ConfigureAwait(false); } catch { } }
        cts.Dispose();
    }

    /// <summary>Sends a list of packets (e.g. a chunked transfer) in order.</summary>
    public async Task SendManyAsync(IEnumerable<TCNetPacket> packets, IPEndPoint target, CancellationToken ct = default)
    {
        foreach (var p in packets) await SendAsync(p, target, true, ct).ConfigureAwait(false);
    }

    private async Task SendSafe(TCNetPacket packet, IPEndPoint target)
    {
        try { await SendAsync(packet, target).ConfigureAwait(false); }
        catch (Exception ex) { Warn($"Send {packet.Name} to {target}: {ex.Message}"); }
    }

    private async Task SendManySafe(IEnumerable<TCNetPacket> packets, IPEndPoint target)
    {
        try { await SendManyAsync(packets, target).ConfigureAwait(false); }
        catch (Exception ex) { Warn($"Send to {target}: {ex.Message}"); }
    }

    // ================= Round trips =================

    /// <summary>Runs the time sync routine <paramref name="rounds"/> times and returns the averaged result.</summary>
    public async Task<TimeSyncSample> TimeSyncAsync(TCNetRemoteNode node, int rounds = 1, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        var samples = new List<TimeSyncSample>();
        for (int i = 0; i < Math.Max(1, rounds); i++)
        {
            samples.Add(await RoundTripAsync(_pendingSyncs, node.Key, joinExisting: true,
                () => SendAsync(new TimeSyncPacket { Step = SyncStep.Initialize, ListenerPort = (ushort)ListenerPort }, node, ct),
                timeout ?? Settings.RequestTimeout, ct).ConfigureAwait(false));
        }
        var result = samples.Count == 1 ? samples[0] : TimeSync.Average(samples);
        node.ClockOffsetMicros = result.OffsetMicros;
        node.DelayMicros = result.DelayMicros;
        return result;
    }

    /// <summary>
    /// Sends a Request and waits for the data (single packet, or all chunks reassembled) or an Error/Notification.
    /// </summary>
    public async Task<TCNetRequestResult> RequestAsync(TCNetRemoteNode node, DataType type, byte layer, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        try
        {
            return await RoundTripAsync(_pendingRequests, RequestKey(node, type, layer), joinExisting: true,
                () => SendAsync(new RequestPacket { DataType = type, Layer = layer }, node, ct),
                timeout ?? Settings.RequestTimeout, ct).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return new TCNetRequestResult(type, layer, null, null, null, true);
        }
    }

    public async Task<MetricsDataPacket?> RequestMetricsAsync(TCNetRemoteNode node, byte layer, CancellationToken ct = default) =>
        (await RequestAsync(node, DataType.Metrics, layer, null, ct).ConfigureAwait(false)).Packet as MetricsDataPacket;

    public async Task<MetadataPacket?> RequestMetadataAsync(TCNetRemoteNode node, byte layer, CancellationToken ct = default) =>
        (await RequestAsync(node, DataType.Metadata, layer, null, ct).ConfigureAwait(false)).Packet as MetadataPacket;

    public async Task<CueDataPacket?> RequestCuesAsync(TCNetRemoteNode node, byte layer, CancellationToken ct = default) =>
        (await RequestAsync(node, DataType.CueData, layer, null, ct).ConfigureAwait(false)).Packet as CueDataPacket;

    public async Task<BeatGrid?> RequestBeatGridAsync(TCNetRemoteNode node, byte layer, CancellationToken ct = default) =>
        (await RequestAsync(node, DataType.BeatGrid, layer, null, ct).ConfigureAwait(false)).Data?.AsBeatGrid();

    public async Task<Waveform?> RequestSmallWaveformAsync(TCNetRemoteNode node, byte layer, CancellationToken ct = default) =>
        (await RequestAsync(node, DataType.SmallWaveform, layer, null, ct).ConfigureAwait(false)).Data?.AsWaveform();

    public async Task<Waveform?> RequestBigWaveformAsync(TCNetRemoteNode node, byte layer, CancellationToken ct = default) =>
        (await RequestAsync(node, DataType.BigWaveform, layer, TimeSpan.FromSeconds(5), ct).ConfigureAwait(false)).Data?.AsWaveform();

    public async Task<byte[]?> RequestArtworkAsync(TCNetRemoteNode node, byte layer, CancellationToken ct = default) =>
        (await RequestAsync(node, DataType.LowResArtwork, layer, TimeSpan.FromSeconds(5), ct).ConfigureAwait(false)).Data?.Data;

    public async Task<MixerDataPacket?> RequestMixerAsync(TCNetRemoteNode node, byte mixerId = 0, CancellationToken ct = default) =>
        (await RequestAsync(node, DataType.Mixer, mixerId, null, ct).ConfigureAwait(false)).Packet as MixerDataPacket;

    /// <summary>Sends a Control packet and waits for the notification response (null on timeout).</summary>
    public async Task<ErrorNotificationPacket?> SendControlAsync(TCNetRemoteNode node, string controlPath, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        try
        {
            return await RoundTripAsync(_pendingControl, node.Key, joinExisting: false,
                () => SendAsync(new ControlPacket { Text = controlPath }, node, ct),
                timeout ?? Settings.RequestTimeout, ct).ConfigureAwait(false);
        }
        catch (TimeoutException) { return null; }
    }

    /// <inheritdoc cref="SendControlAsync(TCNetRemoteNode, string, TimeSpan?, CancellationToken)"/>
    public Task<ErrorNotificationPacket?> SendControlAsync(TCNetRemoteNode node, params ControlCommand[] commands) =>
        SendControlAsync(node, ControlCommand.Join(commands));

    /// <summary>Sends Text Data to a node, or broadcasts it to 60000 when <paramref name="node"/> is null.</summary>
    public Task SendTextAsync(string text, TCNetRemoteNode? node = null, CancellationToken ct = default) =>
        node is null ? BroadcastAsync(new TextDataPacket(text), TCNetConstants.BroadcastPort, ct) : SendAsync(new TextDataPacket(text), node, ct);

    /// <summary>Sends Keyboard Data to a node, or broadcasts it to 60000 when <paramref name="node"/> is null.</summary>
    public Task SendKeyAsync(ushort keyCode, TCNetRemoteNode? node = null, CancellationToken ct = default)
    {
        var p = new KeyboardDataPacket { KeyCode = keyCode };
        return node is null ? BroadcastAsync(p, TCNetConstants.BroadcastPort, ct) : SendAsync(p, node, ct);
    }

    /// <summary>
    /// Registers a waiter for <paramref name="key"/>, sends, and waits for the response. When a round trip for the
    /// same key is already in flight, <paramref name="joinExisting"/> shares its response (same request); otherwise
    /// the call waits for it to finish first (e.g. control paths, whose responses cannot be told apart).
    /// The waiter is only ever removed by the call that registered it.
    /// </summary>
    private static async Task<T> RoundTripAsync<T>(ConcurrentDictionary<string, TaskCompletionSource<T>> pending, string key, bool joinExisting,
        Func<Task> send, TimeSpan timeout, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        while (!pending.TryAdd(key, tcs))
        {
            if (!pending.TryGetValue(key, out var existing)) continue;
            if (joinExisting) return await WithTimeout(existing.Task, timeout, ct, static () => { }).ConfigureAwait(false);
            try { await existing.Task.WaitAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
            catch (Exception ex) when (ex is not OperationCanceledException) { }
        }

        try
        {
            await send().ConfigureAwait(false);
            return await WithTimeout(tcs.Task, timeout, ct, static () => { }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Release anyone who joined this round trip.
            if (ex is OperationCanceledException) tcs.TrySetCanceled();
            else if (tcs.TrySetException(ex)) _ = tcs.Task.Exception;
            throw;
        }
        finally
        {
            pending.TryRemove(new KeyValuePair<string, TaskCompletionSource<T>>(key, tcs));
        }
    }

    private static async Task<T> WithTimeout<T>(Task<T> task, TimeSpan timeout, CancellationToken ct, Action onTimeout)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var delay = Task.Delay(timeout, cts.Token);
        var done = await Task.WhenAny(task, delay).ConfigureAwait(false);
        if (done == task)
        {
            cts.Cancel();
            return await task.ConfigureAwait(false);
        }
        onTimeout();
        ct.ThrowIfCancellationRequested();
        throw new TimeoutException();
    }

    private void Warn(string message)
    {
        try { Warning?.Invoke(this, message); }
        catch { /* a failing Warning handler must not take down the loop reporting it */ }
    }

    /// <summary>Finds a node by key, name or address.</summary>
    public TCNetRemoteNode? FindNode(string keyOrNameOrAddress) =>
        _nodes.TryGetValue(keyOrNameOrAddress, out var n) ? n
        : _nodes.Values.FirstOrDefault(x => x.NodeName.Equals(keyOrNameOrAddress, StringComparison.OrdinalIgnoreCase)
                                            || x.Address.ToString() == keyOrNameOrAddress);
}
