using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Net;
using TCNet.Networking;
using TCNet.Text;

namespace TCNet.Maui.Services;

/// <summary>A row in the node list.</summary>
public sealed class NodeRow(RemoteNode node) : Bindable
{
    private string _title = "", _line1 = "", _line2 = "";

    public RemoteNode Node { get; } = node;
    public string Key => Node.Key;
    public string Title { get => _title; private set => Set(ref _title, value); }
    public string Line1 { get => _line1; private set => Set(ref _line1, value); }
    public string Line2 { get => _line2; private set => Set(ref _line2, value); }

    public void Refresh()
    {
        var n = Node;
        Title = $"{n.NodeName} #{n.NodeId} · {TCNetText.Describe(n.NodeType).Split(" – ")[0]}";
        Line1 = $"{n.Address}:{n.ListenerPort}{(n.IsLocal ? " (this device)" : "")} · {n.VendorName} {n.DeviceName} {n.DeviceVersion} · protocol {n.ProtocolVersion}";
        Line2 = $"options: {TCNetText.DescribeFlags(n.NodeOptions)} · up {TCNetUnits.Duration(TimeSpan.FromSeconds(n.Uptime))} · {n.Packets} packets · seen {(DateTime.UtcNow - n.LastSeen).TotalSeconds:0.0} s ago"
                + (n.Sync is { } s ? $" · offset {TCNetUnits.Micros(s.OffsetMicros)}" : "");
    }

    public override string ToString() => $"{Node.NodeName} #{Node.NodeId} ({Node.NodeType}, {Node.Address})";
}

/// <summary>A row in the packet log.</summary>
public sealed class PacketRow(PacketEventArgs e)
{
    public TCNetPacket Packet { get; } = e.Packet;
    public string Time { get; } = e.Time.ToString("HH:mm:ss.fff");
    public string Direction { get; } = e.Outgoing ? "OUT" : "IN";
    public Color DirectionColor { get; } = e.Outgoing ? Color.FromArgb("#E09F3E") : Color.FromArgb("#00A884");
    public string Title { get; } = $"{e.Packet.Name} · {e.Packet.Length} B";
    public string From { get; } = $"{e.Packet.NodeName}#{e.Packet.NodeId} {e.Packet.NodeType}";
    public string Route { get; } = e.Outgoing ? $"→ {e.RemoteEndPoint}" : $"{e.RemoteEndPoint} → :{e.Port}";
    public string Summary { get; } = e.Packet.Summary;

    public bool Matches(string filter) =>
        filter.Length == 0 || Title.Contains(filter, StringComparison.OrdinalIgnoreCase) || From.Contains(filter, StringComparison.OrdinalIgnoreCase)
        || Summary.Contains(filter, StringComparison.OrdinalIgnoreCase) || Route.Contains(filter, StringComparison.OrdinalIgnoreCase);
}

/// <summary>The app's single TCNet node, its population list and packet log, marshalled to the UI thread.</summary>
public sealed class AppState : Bindable
{
    private const int MaxRows = 1000;
    private readonly ConcurrentQueue<PacketRow> _inbox = new();
    private IDispatcherTimer? _timer;
    private string _status = "Stopped", _filter = "";
    private bool _running, _paused;

    public AppState(AppPreferences prefs) => Prefs = prefs;

    public AppPreferences Prefs { get; }
    public TCNetNode? Node { get; private set; }
    public Playback Playback { get; } = Playback.Demo();
    public ObservableCollection<NodeRow> Nodes { get; } = [];
    public ObservableCollection<PacketRow> Packets { get; } = [];
    public ObservableCollection<string> Log { get; } = [];

    public bool IsRunning { get => _running; private set => Set(ref _running, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool Paused { get => _paused; set => Set(ref _paused, value); }
    public string Filter { get => _filter; set => Set(ref _filter, value ?? ""); }

    /// <summary>Raised on the UI thread when chunked data completes.</summary>
    public event EventHandler<DataEventArgs>? DataArrived;

    public async Task StartAsync()
    {
        if (Node is not null) return;
        var p = Prefs;
        var settings = new NodeSettings
        {
            NodeId = (ushort)Math.Clamp(p.NodeId, 1, 65535),
            NodeName = p.NodeName,
            NodeType = p.Role,
            NodeOptions = p.Options,
            DeviceName = "TCNet app",
            ListenerPort = p.ListenerPort,
            LocalAddress = IPAddress.TryParse(p.Interface, out var ip) ? ip : IPAddress.Any,
            BroadcastAddress = IPAddress.TryParse(p.Broadcast, out var bc) ? bc : null,
            AutoRequestMetadata = p.AutoMetadata,
            AutoRequestMetrics = p.AutoMetrics,
            AutoTimeSync = p.AutoTimeSync,
            AutoMasterElection = p.Election,
            SendStatus = p.Simulate ? true : null,
        };
        var node = new TCNetNode(settings);
        node.PacketReceived += OnPacket;
        node.PacketSent += OnPacket;
        node.NodeDiscovered += (_, e) => Ui(() => AddRow(e.Node));
        node.NodeLost += (_, e) => Ui(() => RemoveRow(e.Node, e.Reason));
        node.DataAssembled += (_, e) => Ui(() => DataArrived?.Invoke(this, e));
        node.Warning += (_, w) => Ui(() => Note(w));
        node.RoleChanged += (_, r) => Ui(() => { Note($"Role is now {r}"); Status = Describe(node); });
        if (p.Simulate)
        {
            node.RequestHandler = (rq, _) => Playback.Answer(rq);
            node.ControlHandler = (cp, _) => Playback.Apply(cp);
            node.StatusProvider = Playback.BuildStatus;
        }

        MulticastLock.Acquire();
        try
        {
            await node.StartAsync();
        }
        catch (Exception ex)
        {
            Status = $"Could not start: {ex.Message}";
            Note(Status);
            MulticastLock.Release();
            return;
        }
        if (p.Simulate) node.StartTimeStream(Playback.BuildTime, TimeSpan.FromMilliseconds(Math.Clamp(p.TimeIntervalMs, 1, 40)));

        Node = node;
        IsRunning = true;
        Status = Describe(node);
        Raise(nameof(Node));
        _timer ??= NewTimer();
        _timer.Start();
    }

    private static string Describe(TCNetNode n) =>
        $"{n.Settings.NodeName}#{n.Settings.NodeId} · {n.NodeType} · listener {n.ListenerPort} · shared ports {string.Join(", ", n.SharedPorts)} · broadcast {n.BroadcastAddress}"
        + (n.IsStreaming ? " · streaming Time" : "");

    public async Task StopAsync()
    {
        _timer?.Stop();
        var node = Node;
        Node = null;
        IsRunning = false;
        if (node is not null)
        {
            try { await node.StopAsync(); }
            catch (Exception ex) { Note($"Stop: {ex.Message}"); }
        }
        Nodes.Clear();
        Status = "Stopped";
        Raise(nameof(Node));
        MulticastLock.Release();
    }

    public async Task RestartAsync()
    {
        await StopAsync();
        await StartAsync();
    }

    public void ClearPackets()
    {
        while (_inbox.TryDequeue(out _)) { }
        Packets.Clear();
    }

    public void Note(string text)
    {
        Log.Insert(0, $"{DateTime.Now:HH:mm:ss}  {text}");
        while (Log.Count > 100) Log.RemoveAt(Log.Count - 1);
    }

    private static void Ui(Action a) => MainThread.BeginInvokeOnMainThread(a);

    private IDispatcherTimer NewTimer()
    {
        var t = Application.Current!.Dispatcher.CreateTimer();
        t.Interval = TimeSpan.FromMilliseconds(250);
        t.Tick += (_, _) => Flush();
        return t;
    }

    private void Flush()
    {
        for (int i = 0; i < 300 && _inbox.TryDequeue(out var row); i++)
            if (!Paused && row.Matches(Filter)) Packets.Insert(0, row);
        while (Packets.Count > MaxRows) Packets.RemoveAt(Packets.Count - 1);

        if (Node is { } node)
            foreach (var n in node.Nodes)
                if (Nodes.All(r => r.Key != n.Key)) AddRow(n);
        foreach (var r in Nodes) r.Refresh();
    }

    private void OnPacket(object? sender, PacketEventArgs e)
    {
        if (e.Packet is TimePacket && !Prefs.LogTime) return;
        if (e.Outgoing && !Prefs.LogSent) return;
        _inbox.Enqueue(new PacketRow(e));
        while (_inbox.Count > MaxRows * 2) _inbox.TryDequeue(out _);
    }

    private void AddRow(RemoteNode n)
    {
        if (Nodes.Any(r => r.Key == n.Key)) return;
        var row = new NodeRow(n);
        row.Refresh();
        Nodes.Add(row);
    }

    private void RemoveRow(RemoteNode n, string? why)
    {
        var row = Nodes.FirstOrDefault(r => r.Key == n.Key);
        if (row is not null) Nodes.Remove(row);
        Note($"{n.NodeName} left ({why})");
    }

    /// <summary>"bcast:60000", "node:KEY" or "ip:port".</summary>
    public IPEndPoint? Resolve(string target)
    {
        if (Node is not { } node) return null;
        if (target.StartsWith("bcast:", StringComparison.OrdinalIgnoreCase) && int.TryParse(target[6..], out int port))
            return new IPEndPoint(node.BroadcastAddress, port);
        if (target.StartsWith("node:", StringComparison.OrdinalIgnoreCase)) return node.FindNode(target[5..])?.EndPoint;
        return IPEndPoint.TryParse(target, out var ep) ? ep : null;
    }
}
