using System.Collections.ObjectModel;
using System.Windows.Input;
using TCNet.Maui.Services;
using TCNet.Networking;
using TCNet.Text;
using Cmd = TCNet.ControlCommand;

namespace TCNet.Maui.ViewModels;

public sealed record Row(string Name, string Value, string Meaning = "");

public sealed class NodesViewModel(AppState app)
{
    public AppState App { get; } = app;
    public ObservableCollection<NodeRow> Nodes => App.Nodes;
}

/// <summary>Everything known about one node and every action the protocol offers towards it.</summary>
public sealed class NodeViewModel : PollingViewModel, IQueryAttributable
{
    private readonly AppState _app;
    private RemoteNode? _node;
    private string _title = "Node", _result = "", _path = "layer/1/state=3;", _text = "Hello from TCNet", _key = "A";
    private OptionInfo? _dataType;
    private int _layer = 1;
    private bool _busy;
    private ImageSource? _artwork;

    public NodeViewModel(AppState app) : base(TimeSpan.FromSeconds(1))
    {
        _app = app;
        DataTypes = TCNetText.Options<global::TCNet.DataType>();
        _dataType = DataTypes[0];
        RequestCommand = new Command(async () => await Run(Request));
        RequestAllCommand = new Command(async () => await Run(RequestAll));
        SyncCommand = new Command(async () => await Run(Sync));
        ControlCommand = new Command(async () => await Run(Control));
        PlayCommand = new Command(async () => { ControlPath = Cmd.Join([Cmd.SetState(Layer, LayerState.Playing), Cmd.Resync(Layer)]); await Run(Control); });
        PauseCommand = new Command(async () => { ControlPath = Cmd.SetState(Layer, LayerState.Paused).ToString(); await Run(Control); });
        StopCommand = new Command(async () => { ControlPath = Cmd.SetState(Layer, LayerState.Stopped).ToString(); await Run(Control); });
        TextCommand = new Command(async () => await Run(SendText));
        KeyCommand = new Command(async () => await Run(SendKey));
    }

    public string Title { get => _title; private set => Set(ref _title, value); }
    public ObservableCollection<Row> Info { get; } = [];
    public ObservableCollection<Row> Layers { get; } = [];
    public ObservableCollection<TCNetField> Fields { get; } = [];
    public IReadOnlyList<OptionInfo> DataTypes { get; }
    public OptionInfo? DataType { get => _dataType; set => Set(ref _dataType, value); }
    public IReadOnlyList<int> LayerNumbers { get; } = [0, 1, 2, 3, 4, 5, 6, 7, 8];
    public int Layer { get => _layer; set => Set(ref _layer, value); }
    public string Result { get => _result; private set => Set(ref _result, value); }
    public string ControlPath { get => _path; set => Set(ref _path, value); }
    public string Text { get => _text; set => Set(ref _text, value); }
    public string Key { get => _key; set => Set(ref _key, value); }
    public bool Busy { get => _busy; private set => Set(ref _busy, value); }
    public ImageSource? Artwork { get => _artwork; private set => Set(ref _artwork, value); }

    public ICommand RequestCommand { get; }
    public ICommand RequestAllCommand { get; }
    public ICommand SyncCommand { get; }
    public ICommand ControlCommand { get; }
    public ICommand PlayCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand TextCommand { get; }
    public ICommand KeyCommand { get; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("key", out var k) && k is string key) _node = _app.Node?.FindNode(key);
        Poll();
    }

    protected override void Poll()
    {
        if (_node is not { } n)
        {
            Title = "Node not found";
            return;
        }
        Title = $"{n.NodeName} #{n.NodeId}";
        var info = new List<Row>
        {
            new("Address", $"{n.Address}:{n.ListenerPort}", n.IsLocal ? "this device" : ""),
            new("Node ID", n.NodeId.ToString(), "unique per IP"),
            new("Node Name", n.NodeName),
            new("Node Type", ((byte)n.NodeType).ToString(), TCNetText.Describe(n.NodeType)),
            new("Node Options", ((ushort)n.NodeOptions).ToString(), TCNetText.DescribeFlags(n.NodeOptions)),
            new("Protocol Version", n.ProtocolVersion.ToString()),
            new("Vendor Name", n.VendorName),
            new("Application/Device", $"{n.DeviceName} {n.DeviceVersion}"),
            new("Uptime", $"{n.Uptime} s", TCNetUnits.Duration(TimeSpan.FromSeconds(n.Uptime))),
            new("Node Count", n.NodeCount.ToString(), "nodes it has registered"),
            new("Timestamp", $"{n.LastTimestamp} µs", "last header timestamp"),
            new("Packets", n.Packets.ToString()),
            new("First / last seen", $"{n.FirstSeen.ToLocalTime():T} / {n.LastSeen.ToLocalTime():T}"),
            new("Time sync", n.Sync is { } sy ? $"offset {TCNetUnits.Micros(sy.OffsetMicros)}" : "not yet", n.Sync is { } sy2 ? $"delay {TCNetUnits.Micros(sy2.DelayMicros)}" : ""),
        };
        if (n.Status is { } st)
        {
            info.Add(new("SMPTE Mode", ((byte)st.SmpteMode).ToString(), TCNetText.Describe(st.SmpteMode)));
            info.Add(new("Auto Master Mode", ((byte)st.AutoMasterMode).ToString(), TCNetText.Describe(st.AutoMasterMode)));
        }
        if (n.Mixer is { } mx) info.Add(new("Mixer", mx.MixerName, mx.Summary));
        Replace(Info, info);

        var layers = new List<Row>();
        for (int i = 0; i < 8; i++)
        {
            var ls = n.Status?.Layers[i];
            string value = ls is null ? "no Status yet" : $"{ls.Name} · {TCNetText.Describe(ls.State)} · source {ls.Source} · track {ls.TrackId}";
            var extra = new[]
            {
                n.Metadata[i] is { } md ? $"{md.Artist} – {md.Title}" : null,
                n.Metrics[i] is { } m ? $"{m.Bpm:0.00} BPM {TCNetUnits.Ms(m.PositionMs)}/{TCNetUnits.Ms(m.TrackLengthMs)}" : null,
                n.Cues[i] is { } c ? $"{c.Cues.Count(x => !x.IsEmpty)} cues, loop {c.LoopInMs}–{c.LoopOutMs} ms" : null,
                n.BeatGrids[i] is { } g ? $"{g.Beats.Count} beats" : null,
                n.SmallWaveforms[i] is { } w ? $"waveform {w.Bars.Count} bars" : null,
                n.Artwork[i] is { } a ? $"artwork {TCNetUnits.Bytes(a.Length)}" : null,
            };
            layers.Add(new($"Layer {TCNetText.LayerLabel(i)}", value, string.Join(" · ", extra.Where(x => x is not null))));
        }
        Replace(Layers, layers);
    }

    private static void Replace<T>(ObservableCollection<T> target, List<T> items)
    {
        if (target.SequenceEqual(items)) return;
        target.Clear();
        foreach (var x in items) target.Add(x);
    }

    private async Task Run(Func<TCNetNode, RemoteNode, Task> action)
    {
        if (_app.Node is not { } local || _node is not { } remote) { Result = "Not connected."; return; }
        if (remote.EndPoint is null) { Result = "Its listener port is not known yet (no Opt-IN)."; return; }
        Busy = true;
        try { await action(local, remote); }
        catch (Exception ex) { Result = ex.Message; }
        finally { Busy = false; Poll(); }
    }

    private async Task Request(TCNetNode local, RemoteNode remote)
    {
        var type = (global::TCNet.DataType)(DataType?.Value ?? 2);
        Show(await local.RequestAsync(remote, type, (byte)Layer, TimeSpan.FromSeconds(5)));
    }

    private async Task RequestAll(TCNetNode local, RemoteNode remote)
    {
        var lines = new List<string>();
        foreach (var o in DataTypes)
        {
            var type = (global::TCNet.DataType)o.Value;
            var r = await local.RequestAsync(remote, type, type == global::TCNet.DataType.Mixer ? (byte)0 : (byte)Layer, TimeSpan.FromSeconds(3));
            lines.Add(r.ToString());
        }
        Result = string.Join("\n", lines);
        Fields.Clear();
    }

    private void Show(RequestResult r)
    {
        Result = r.ToString();
        Fields.Clear();
        if (r.Notification is { } en) { foreach (var f in en.Describe()) Fields.Add(f); return; }
        if (r.Data is { } d)
        {
            Fields.Add(new TCNetField(42, d.Data.Length, "Reassembled data", TCNetUnits.Bytes(d.Data.Length), $"{d.Packets} packets"));
            switch (d.DataType)
            {
                case global::TCNet.DataType.BeatGrid:
                    foreach (var b in d.ToBeatGrid().Beats.Take(256))
                        Fields.Add(new TCNetField(b.Number * 8, 8, $"Beat {b.Number}", TCNetUnits.Ms(b.TimeMs), TCNetText.Describe(b.Type)));
                    break;
                case global::TCNet.DataType.SmallWaveform or global::TCNet.DataType.BigWaveform:
                    var w = d.ToWaveform();
                    Fields.Add(new TCNetField(42, 0, "Bars", w.Bars.Count.ToString(), $"peak level {w.Peak}"));
                    break;
                case global::TCNet.DataType.LowResArtwork:
                    var bytes = d.Data;
                    Artwork = ImageSource.FromStream(() => new MemoryStream(bytes));
                    Fields.Add(new TCNetField(42, bytes.Length, "JPEG", d.IsJpeg ? "valid start marker" : "no JPEG marker", "shown below"));
                    break;
            }
            return;
        }
        if (r.Packet is { } p) foreach (var f in p.Describe()) Fields.Add(f);
    }

    private async Task Sync(TCNetNode local, RemoteNode remote)
    {
        var s = await local.TimeSyncAsync(remote, 4);
        Result = $"Delay {TCNetUnits.Micros(s.DelayMicros)} · round trip {TCNetUnits.Micros(s.RoundTripMicros)} · clock offset {TCNetUnits.Micros(s.OffsetMicros)} (4 rounds)";
        Fields.Clear();
    }

    private async Task Control(TCNetNode local, RemoteNode remote)
    {
        Fields.Clear();
        foreach (var c in Cmd.Parse(ControlPath))
            Fields.Add(new TCNetField(42, c.ToString().Length, c.Path, c.Value ?? "(no value)",
                c.Leaf == "state" && byte.TryParse(c.Value, out var s) ? TCNetText.Describe((LayerState)s) : null));
        var ack = await local.SendControlAsync(remote, ControlPath);
        Result = ack is null ? "Control sent – no answer." : $"Answer: {TCNetText.Describe(ack.Code)}";
    }

    private async Task SendText(TCNetNode local, RemoteNode remote)
    {
        await local.SendTextAsync(Text, remote);
        Result = $"Text Data sent ({Text.Length} characters).";
    }

    private async Task SendKey(TCNetNode local, RemoteNode remote)
    {
        ushort code = Key.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToUInt16(Key[2..], 16) : Key.Length > 0 ? Key[0] : (ushort)0;
        await local.SendKeyAsync(code, remote);
        Result = $"Keyboard Data 0x{code:X4} sent.";
    }
}
