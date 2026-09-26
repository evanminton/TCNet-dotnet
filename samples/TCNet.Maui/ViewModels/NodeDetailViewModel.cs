using Cmd = global::TCNet.ControlCommand;
using System.Collections.ObjectModel;
using System.Windows.Input;
using TCNet.Maui.Services;
using TCNet.Networking;
using TCNet.Text;

namespace TCNet.Maui.ViewModels;

public sealed record InfoRow(string Name, string Value, string Meaning = "");

/// <summary>Everything known about one node, plus every request/command the protocol offers.</summary>
public sealed class NodeDetailViewModel : ObservableObject, IQueryAttributable
{
    private readonly TCNetService _service;
    private TCNetRemoteNode? _node;
    private IDispatcherTimer? _timer;
    private string _title = "Node", _result = "", _controlPath = "layer/1/state=3;", _text = "Hello from TCNet Monitor", _key = "A";
    private OptionInfo? _dataType;
    private int _layer = 1;
    private bool _busy;

    public NodeDetailViewModel(TCNetService service)
    {
        _service = service;
        DataTypes = TCNetText.Options<global::TCNet.DataType>();
        _dataType = DataTypes.FirstOrDefault();
        TimeSyncCommand = new Command(async () => await Run(TimeSync));
        RequestCommand = new Command(async () => await Run(Request));
        RequestAllCommand = new Command(async () => await Run(RequestAll));
        ControlCommand = new Command(async () => await Run(Control));
        StopLayerCommand = new Command(async () => { ControlPath = Cmd.SetLayerState(Layer, LayerState.Stopped).ToString(); await Run(Control); });
        PlayLayerCommand = new Command(async () => { ControlPath = $"{Cmd.SetLayerState(Layer, LayerState.Playing)} {Cmd.Resync(Layer)}"; await Run(Control); });
        TextCommand = new Command(async () => await Run(SendText));
        KeyCommand = new Command(async () => await Run(SendKey));
    }

    public string Title { get => _title; private set => SetProperty(ref _title, value); }
    public ObservableCollection<InfoRow> Info { get; } = [];
    public ObservableCollection<InfoRow> Layers { get; } = [];
    public ObservableCollection<TCNetField> ResultFields { get; } = [];

    public IReadOnlyList<OptionInfo> DataTypes { get; }
    public OptionInfo? DataType { get => _dataType; set => SetProperty(ref _dataType, value); }
    public IReadOnlyList<int> LayerNumbers { get; } = [0, 1, 2, 3, 4, 5, 6, 7, 8];
    public int Layer { get => _layer; set => SetProperty(ref _layer, value); }

    public string Result { get => _result; private set => SetProperty(ref _result, value); }
    public string ControlPath { get => _controlPath; set => SetProperty(ref _controlPath, value); }
    public string Text { get => _text; set => SetProperty(ref _text, value); }
    public string Key { get => _key; set => SetProperty(ref _key, value); }
    public bool Busy { get => _busy; private set => SetProperty(ref _busy, value); }

    private ImageSource? _artwork;
    /// <summary>Last received low-res artwork.</summary>
    public ImageSource? Artwork { get => _artwork; private set => SetProperty(ref _artwork, value); }

    public ICommand TimeSyncCommand { get; }
    public ICommand RequestCommand { get; }
    public ICommand RequestAllCommand { get; }
    public ICommand ControlCommand { get; }
    public ICommand StopLayerCommand { get; }
    public ICommand PlayLayerCommand { get; }
    public ICommand TextCommand { get; }
    public ICommand KeyCommand { get; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("key", out var k) && k is string key)
            _node = _service.Node?.FindNode(key);
        Refresh();
    }

    public void Start()
    {
        _timer ??= Application.Current!.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public void Stop() => _timer?.Stop();

    private void OnTick(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        var n = _node;
        if (n is null) { Title = "Node not found"; return; }
        Title = $"{n.NodeName} #{n.NodeId}";

        var info = new List<InfoRow>
        {
            new("Address", $"{n.Address}:{n.ListenerPort}", n.IsLocal ? "this device" : ""),
            new("Node ID", n.NodeId.ToString()),
            new("Node Name", n.NodeName),
            new("Node Type", ((byte)n.NodeType).ToString(), TCNetText.Describe(n.NodeType)),
            new("Node Options", ((ushort)n.NodeOptions).ToString(), TCNetText.DescribeFlags(n.NodeOptions)),
            new("Protocol Version", n.ProtocolVersion.ToString()),
            new("Vendor", n.VendorName),
            new("Application", $"{n.ApplicationName} {n.ApplicationVersion}"),
            new("Uptime", $"{n.Uptime} s", TCNetUnits.FormatDuration(TimeSpan.FromSeconds(n.Uptime))),
            new("Node Count", n.NodeCount.ToString(), "nodes registered by this node"),
            new("Last Timestamp", $"{n.LastTimestamp} µs"),
            new("First Seen", n.FirstSeen.ToLocalTime().ToString("T")),
            new("Last Seen", n.LastSeen.ToLocalTime().ToString("T")),
            new("Packets", n.PacketsReceived.ToString()),
            new("Clock Offset", n.ClockOffsetMicros is { } o ? TCNetUnits.FormatMicros(o) : "not synced"),
            new("Delay", n.DelayMicros is { } d ? TCNetUnits.FormatMicros(d) : "–"),
        };
        if (n.LastStatus is { } st)
        {
            info.Add(new("SMPTE Mode", ((byte)st.SmpteMode).ToString(), TCNetText.Describe(st.SmpteMode)));
            info.Add(new("Auto Master Mode", ((byte)st.AutoMasterMode).ToString(), TCNetText.Describe(st.AutoMasterMode)));
        }
        if (n.LastMixer is { } mx) info.Add(new("Mixer", mx.MixerName, TCNetText.Describe(mx.MixerType)));
        Replace(Info, info);

        var layers = new List<InfoRow>();
        for (int i = 0; i < 8; i++)
        {
            var s = n.LastStatus?.Layers[i];
            var md = n.Metadata[i];
            var m = n.Metrics[i];
            string value = s is null ? "no status" : $"{s.Name} · {TCNetText.Describe(s.State)} · source {s.Source} · track {s.TrackId}";
            string meaning = string.Join(" · ", new[]
            {
                md is null ? null : $"{md.TrackArtist} – {md.TrackTitle}",
                m is null ? null : $"{m.BpmValue:0.00} BPM, {TCNetUnits.FormatMs(m.CurrentPosition)}/{TCNetUnits.FormatMs(m.TrackLength)}",
                n.Cues[i] is { } c ? $"{c.Cues.Count(x => !x.IsEmpty)} cues" : null,
                n.BeatGrids[i] is { } g ? $"{g.Entries.Count} beats" : null,
                n.Artwork[i] is { } a ? $"artwork {TCNetUnits.FormatBytes(a.Length)}" : null,
            }.Where(x => x is not null));
            layers.Add(new($"Layer {TCNetText.LayerLabel(i)}", value, meaning));
        }
        Replace(Layers, layers);
    }

    /// <summary>
    /// Updates <paramref name="target"/> in place: rows are immutable records, so only rows whose value changed are
    /// replaced by index (one small Replace notification each) instead of clearing and rebuilding every row.
    /// </summary>
    private static void Replace(ObservableCollection<InfoRow> target, IList<InfoRow> items)
    {
        bool sameShape = target.Count == items.Count && target.Select(r => r.Name).SequenceEqual(items.Select(r => r.Name));
        if (!sameShape)
        {
            target.Clear();
            foreach (var i in items) target.Add(i);
            return;
        }
        for (int i = 0; i < items.Count; i++)
            if (target[i] != items[i]) target[i] = items[i];
    }

    private async Task Run(Func<TCNetNode, TCNetRemoteNode, Task> action)
    {
        if (_service.Node is not { } local || _node is not { } remote) { Result = "Not connected."; return; }
        if (remote.EndPoint is null) { Result = "Listener port unknown (no Opt-IN received yet)."; return; }
        Busy = true;
        try { await action(local, remote); }
        catch (Exception ex) { Result = ex.Message; }
        finally { Busy = false; Refresh(); }
    }

    private async Task TimeSync(TCNetNode local, TCNetRemoteNode remote)
    {
        var s = await local.TimeSyncAsync(remote, 4);
        Result = $"Delay {TCNetUnits.FormatMicros(s.DelayMicros)} · round trip {TCNetUnits.FormatMicros(s.RoundTripMicros)} · offset {TCNetUnits.FormatMicros(s.OffsetMicros)} (4 rounds)";
        ResultFields.Clear();
    }

    private async Task Request(TCNetNode local, TCNetRemoteNode remote)
    {
        var type = (global::TCNet.DataType)(DataType?.Value ?? (long)global::TCNet.DataType.Metrics);
        var r = await local.RequestAsync(remote, type, (byte)Layer, TimeSpan.FromSeconds(5));
        ShowResult(r);
    }

    private async Task RequestAll(TCNetNode local, TCNetRemoteNode remote)
    {
        var lines = new List<string>();
        foreach (var o in DataTypes)
        {
            var r = await local.RequestAsync(remote, (global::TCNet.DataType)o.Value, o.Value == (long)global::TCNet.DataType.Mixer ? (byte)0 : (byte)Layer, TimeSpan.FromSeconds(3));
            lines.Add(r.ToString());
        }
        Result = string.Join("\n", lines);
        ResultFields.Clear();
    }

    private void ShowResult(TCNetRequestResult r)
    {
        Result = r.ToString();
        ResultFields.Clear();
        if (r.Notification is { } en) foreach (var f in en.Describe()) ResultFields.Add(f);
        else if (r.Data is { } d)
        {
            ResultFields.Add(new TCNetField(0, d.Data.Length, "Assembled data", TCNetUnits.FormatBytes(d.Data.Length), $"{d.PacketCount} packets"));
            switch (d.DataType)
            {
                case global::TCNet.DataType.BeatGrid:
                    foreach (var e in d.AsBeatGrid().Entries.Take(200))
                        ResultFields.Add(new TCNetField(e.BeatNumber * 8, 8, $"Beat {e.BeatNumber}", TCNetUnits.FormatMs(e.TimestampMs), TCNetText.Describe(e.Type)));
                    break;
                case global::TCNet.DataType.SmallWaveform or global::TCNet.DataType.BigWaveform:
                    var w = d.AsWaveform();
                    ResultFields.Add(new TCNetField(0, 0, "Bars", w.Bars.Count.ToString(), $"peak level {w.PeakLevel}"));
                    break;
                case global::TCNet.DataType.LowResArtwork:
                    ResultFields.Add(new TCNetField(0, 0, "JPEG", d.IsJpeg ? "yes" : "no", "shown below"));
                    var bytes = d.Data;
                    Artwork = ImageSource.FromStream(() => new MemoryStream(bytes));
                    break;
            }
        }
        else if (r.Packet is { } p) foreach (var f in p.Describe()) ResultFields.Add(f);
    }

    private async Task Control(TCNetNode local, TCNetRemoteNode remote)
    {
        var ack = await local.SendControlAsync(remote, ControlPath);
        Result = ack is null ? "Control sent – no response." : $"Control response: {TCNetText.Describe(ack.Code)}";
        ResultFields.Clear();
        foreach (var c in Cmd.ParseAll(ControlPath))
            ResultFields.Add(new TCNetField(42, c.ToString().Length, c.Path, c.Value ?? "(no value)", c.LayerNumber is { } ln ? $"layer {TCNetText.LayerName((byte)ln)}" : null));
    }

    private async Task SendText(TCNetNode local, TCNetRemoteNode remote)
    {
        await local.SendTextAsync(Text, remote);
        Result = $"Text Data sent ({Text.Length} characters).";
    }

    private async Task SendKey(TCNetNode local, TCNetRemoteNode remote)
    {
        ushort code = Key.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? Convert.ToUInt16(Key[2..], 16)
            : Key.Length > 0 ? Key[0] : (ushort)0;
        await local.SendKeyAsync(code, remote);
        Result = $"Keyboard Data 0x{code:X4} sent.";
    }
}
