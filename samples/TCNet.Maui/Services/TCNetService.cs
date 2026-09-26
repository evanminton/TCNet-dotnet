using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Net;
using TCNet.Networking;
using TCNet.Text;

namespace TCNet.Maui.Services;

/// <summary>A row in the node list.</summary>
public sealed class NodeItem : ObservableObject
{
    public NodeItem(TCNetRemoteNode node) { Node = node; Refresh(); }

    public TCNetRemoteNode Node { get; }
    public string Key => Node.Key;

    private string _title = "", _subtitle = "", _detail = "";
    public string Title { get => _title; private set => SetProperty(ref _title, value); }
    public string Subtitle { get => _subtitle; private set => SetProperty(ref _subtitle, value); }
    public string Detail { get => _detail; private set => SetProperty(ref _detail, value); }

    public override string ToString() => $"{Node.NodeName} #{Node.NodeId} ({Node.NodeType}, {Node.Address})";

    public void Refresh()
    {
        var n = Node;
        Title = $"{n.NodeName} #{n.NodeId}";
        Subtitle = $"{n.NodeType} · {n.Address}:{n.ListenerPort}{(n.IsLocal ? " · this device" : "")} · {n.VendorName} {n.ApplicationName} {n.ApplicationVersion}".Trim();
        var seen = (DateTime.UtcNow - n.LastSeen).TotalSeconds;
        var sync = n.ClockOffsetMicros is { } o ? $" · offset {TCNetUnits.FormatMicros(o)}" : "";
        Detail = $"protocol {n.ProtocolVersion} · up {TCNetUnits.FormatDuration(TimeSpan.FromSeconds(n.Uptime))} · {n.PacketsReceived} packets · seen {seen:0.0}s ago{sync}";
    }
}

/// <summary>A row in the packet log.</summary>
public sealed class PacketItem
{
    public PacketItem(TCNetPacketEventArgs e)
    {
        Packet = e.Packet;
        Time = e.Time.ToString("HH:mm:ss.fff");
        Direction = e.Outgoing ? "OUT" : "IN";
        EndPoint = e.Outgoing ? $"→ {e.RemoteEndPoint}" : $"{e.RemoteEndPoint} → :{e.LocalPort}";
        Title = $"{e.Packet.Name}  ({e.Packet.Length} B)";
        From = $"{e.Packet.NodeName}#{e.Packet.NodeId} {e.Packet.NodeType}";
        Summary = e.Packet.Summary;
    }

    public TCNetPacket Packet { get; }
    public string Time { get; }
    public string Direction { get; }
    public string EndPoint { get; }
    public string Title { get; }
    public string From { get; }
    public string Summary { get; }
    public Color DirectionColor => Direction == "IN" ? Color.FromArgb("#00A884") : Color.FromArgb("#E09F3E");
}

/// <summary>Owns the TCNet node for the app and marshals its state to the UI.</summary>
public sealed class TCNetService : ObservableObject
{
    private const int MaxPackets = 1000;
    private readonly ConcurrentQueue<PacketItem> _incoming = new();
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private IDispatcherTimer? _timer;
    private string _status = "Stopped";
    private bool _isRunning;
    private bool _pausePackets;
    private string _packetFilter = "";
    // Cached copies of the logging preferences: OnPacket runs on the receive thread for every packet.
    private bool _logTimePackets, _logSent;

    public TCNetService(AppSettings settings)
    {
        Settings = settings;
        Settings.EnsureNodeId();
        ReloadLogSettings();
        var pb = Playback;
        pb[1].Load(1001, "Demo Artist", "Demo Track One", 245_000, 124);
        pb[2].Load(1002, "Demo Artist", "Demo Track Two", 312_000, 126);
        pb[1].State = LayerState.Playing;
        pb[1].OnAir = 255;
        pb[1].SyncMaster = true;
    }

    public AppSettings Settings { get; }
    public TCNetNode? Node { get; private set; }
    public TCNetPlayback Playback { get; } = new();

    public ObservableCollection<NodeItem> Nodes { get; } = [];
    public ObservableCollection<PacketItem> Packets { get; } = [];

    public bool IsRunning { get => _isRunning; private set => SetProperty(ref _isRunning, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public bool PausePackets { get => _pausePackets; set => SetProperty(ref _pausePackets, value); }

    /// <summary>Case-insensitive filter on packet name / node name / summary.</summary>
    public string PacketFilter { get => _packetFilter; set => SetProperty(ref _packetFilter, value ?? ""); }

    /// <summary>Log Time packets (persisted in <see cref="AppSettings.LogTimePackets"/>).</summary>
    public bool LogTimePackets
    {
        get => _logTimePackets;
        set { Settings.LogTimePackets = value; SetProperty(ref _logTimePackets, value); }
    }

    /// <summary>Log packets sent by this node (persisted in <see cref="AppSettings.LogSent"/>).</summary>
    public bool LogSent
    {
        get => _logSent;
        set { Settings.LogSent = value; SetProperty(ref _logSent, value); }
    }

    public ObservableCollection<string> Warnings { get; } = [];

    /// <summary>Raised on the UI thread when data from a node arrived (reassembled chunks, request results).</summary>
    public event EventHandler<TCNetAssembledEventArgs>? DataAssembled;

    public event EventHandler? Restarted;

    /// <summary>Shows an error from a UI action in <see cref="Status"/>.</summary>
    public void ReportError(string action, Exception ex) => Status = $"{action} failed: {ex.Message}";

    public async Task StartAsync()
    {
        await _lifecycle.WaitAsync();
        try { StartCore(); }
        finally { _lifecycle.Release(); }
    }

    public async Task StopAsync()
    {
        await _lifecycle.WaitAsync();
        try { await StopCoreAsync(); }
        finally { _lifecycle.Release(); }
    }

    public async Task RestartAsync()
    {
        await _lifecycle.WaitAsync();
        try
        {
            await StopCoreAsync();
            StartCore();
        }
        finally { _lifecycle.Release(); }
    }

    private void ReloadLogSettings()
    {
        SetProperty(ref _logTimePackets, Settings.LogTimePackets, nameof(LogTimePackets));
        SetProperty(ref _logSent, Settings.LogSent, nameof(LogSent));
    }

    private void StartCore()
    {
        if (Node is not null) return;
        ReloadLogSettings();
        var s = Settings;
        var ns = new TCNetNodeSettings
        {
            NodeId = (ushort)s.NodeId,
            NodeName = s.NodeName,
            NodeType = s.Role,
            NodeOptions = s.Options,
            ApplicationName = "TCNet Monitor",
            VendorName = "TCNet.NET",
            ListenerPort = s.ListenerPort,
            LocalAddress = IPAddress.TryParse(s.InterfaceAddress, out var ip) ? ip : IPAddress.Any,
            BroadcastAddress = IPAddress.TryParse(s.BroadcastAddress, out var b) ? b : null,
            AutoRequestMetadata = s.AutoRequestMetadata,
            AutoRequestMetrics = s.AutoRequestMetrics,
            AutoTimeSync = s.AutoTimeSync,
            AutoMasterElection = s.AutoMasterElection,
            SendStatus = s.Simulate ? true : null,
        };
        var node = new TCNetNode(ns);
        node.PacketReceived += OnPacket;
        node.PacketSent += OnPacket;
        node.NodeDiscovered += (_, e) => MainThread.BeginInvokeOnMainThread(() => AddNode(e.Node));
        node.NodeLost += (_, e) => MainThread.BeginInvokeOnMainThread(() => RemoveNode(e.Node));
        node.DataAssembled += (_, e) => MainThread.BeginInvokeOnMainThread(() => DataAssembled?.Invoke(this, e));
        node.Warning += (_, w) => MainThread.BeginInvokeOnMainThread(() =>
        {
            Warnings.Insert(0, $"{DateTime.Now:HH:mm:ss} {w}");
            while (Warnings.Count > 50) Warnings.RemoveAt(Warnings.Count - 1);
        });
        node.RoleChanged += (_, r) => MainThread.BeginInvokeOnMainThread(() => Status = $"Role changed to {r}");

        if (s.Simulate)
        {
            node.RequestHandler = (rq, _) => Playback.HandleRequest(rq);
            node.ControlHandler = (cp, _) => Playback.Apply(cp);
            node.StatusProvider = Playback.BuildStatus;
        }

        try
        {
            MulticastLockHolder.Acquire();
            node.Start();
        }
        catch (Exception ex)
        {
            MulticastLockHolder.Release();
            Status = $"Start failed: {ex.Message}";
            return;
        }

        if (s.Simulate) node.StartTimeStream(Playback.BuildTime, TimeSpan.FromMilliseconds(Math.Clamp(s.TimeIntervalMs, 1, 40)));

        Node = node;
        IsRunning = true;
        Status = $"{ns.NodeName}#{ns.NodeId} {node.NodeType} · listener {node.ListenerPort} · broadcast ports {string.Join(",", node.BoundBroadcastPorts)} · → {node.BroadcastAddress}";
        OnPropertyChanged(nameof(Node));

        _timer ??= CreateTimer();
        _timer.Start();
        Restarted?.Invoke(this, EventArgs.Empty);
    }

    private async Task StopCoreAsync()
    {
        _timer?.Stop();
        var node = Node;
        Node = null;
        IsRunning = false;
        if (node is not null) await node.StopAsync();
        Nodes.Clear();
        Status = "Stopped";
        OnPropertyChanged(nameof(Node));
        MulticastLockHolder.Release();
    }

    public void ClearPackets()
    {
        while (_incoming.TryDequeue(out _)) { }
        Packets.Clear();
    }

    private IDispatcherTimer CreateTimer()
    {
        var t = Application.Current!.Dispatcher.CreateTimer();
        t.Interval = TimeSpan.FromMilliseconds(250);
        t.Tick += (_, _) => Tick();
        return t;
    }

    private void Tick()
    {
        // Flush packet log.
        int n = 0;
        while (n < 200 && _incoming.TryDequeue(out var item))
        {
            n++;
            if (PausePackets || !Matches(item)) continue;
            Packets.Insert(0, item);
        }
        while (Packets.Count > MaxPackets) Packets.RemoveAt(Packets.Count - 1);

        // Sync node rows.
        if (Node is { } node)
        {
            foreach (var r in node.Nodes)
                if (Nodes.All(x => x.Key != r.Key)) AddNode(r);
            foreach (var item in Nodes) item.Refresh();
        }
    }

    private bool Matches(PacketItem item)
    {
        var f = PacketFilter;
        if (string.IsNullOrWhiteSpace(f)) return true;
        return item.Title.Contains(f, StringComparison.OrdinalIgnoreCase)
               || item.From.Contains(f, StringComparison.OrdinalIgnoreCase)
               || item.Summary.Contains(f, StringComparison.OrdinalIgnoreCase)
               || item.EndPoint.Contains(f, StringComparison.OrdinalIgnoreCase);
    }

    private void OnPacket(object? sender, TCNetPacketEventArgs e)
    {
        if (e.Packet is TimePacket && !_logTimePackets) return;
        if (e.Outgoing && !_logSent) return;
        _incoming.Enqueue(new PacketItem(e));
        while (_incoming.Count > MaxPackets * 2) _incoming.TryDequeue(out _);
    }

    private void AddNode(TCNetRemoteNode node)
    {
        if (Nodes.Any(n => n.Key == node.Key)) return;
        Nodes.Add(new NodeItem(node));
    }

    private void RemoveNode(TCNetRemoteNode node)
    {
        var item = Nodes.FirstOrDefault(n => n.Key == node.Key);
        if (item is not null) Nodes.Remove(item);
    }

    /// <summary>Resolves "broadcast:60000", "node:KEY" or "ip:port" targets for the Send page.</summary>
    public IPEndPoint? ResolveTarget(string? target)
    {
        var node = Node;
        if (node is null || string.IsNullOrWhiteSpace(target)) return null;
        target = target.Trim();
        if (target.StartsWith("broadcast:", StringComparison.Ordinal) && int.TryParse(target["broadcast:".Length..], out int port))
            return new IPEndPoint(node.BroadcastAddress, port);
        if (target.StartsWith("node:", StringComparison.Ordinal))
            return node.FindNode(target["node:".Length..])?.EndPoint;
        return IPEndPoint.TryParse(target, out var ep) ? ep : null;
    }
}
