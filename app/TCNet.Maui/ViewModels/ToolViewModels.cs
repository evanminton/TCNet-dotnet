using System.Collections.ObjectModel;
using System.Windows.Input;
using TCNet.Maui.Services;
using TCNet.Networking;
using TCNet.Text;

namespace TCNet.Maui.ViewModels;

public sealed record ChannelStrip(string Name, double Fader, double Level, string Info);

/// <summary>Every Mixer Data field from the first node sending one.</summary>
public sealed class MixerViewModel : PollingViewModel
{
    private readonly AppState _app;
    private MixerDataPacket? _shown;
    private string _header = "No Mixer Data yet – it is unicast to slaves when the mixer changes, or use Request.";
    private double _master, _crossfader;

    public MixerViewModel(AppState app) : base(TimeSpan.FromMilliseconds(200))
    {
        _app = app;
        RequestCommand = new Command(async () =>
        {
            if (_app.Node is not { } local) { Header = "The node is not running."; return; }
            foreach (var n in local.Nodes.Where(n => n.IsMasterOrRepeater && n.EndPoint is not null))
            {
                try
                {
                    var r = await local.RequestAsync(n, DataType.Mixer, 0);
                    _app.Note($"Mixer request to {n.NodeName}: {r}");
                }
                catch (Exception ex)
                {
                    Header = $"Mixer request to {n.NodeName} failed: {ex.Message}";
                    _app.Note(Header);
                }
            }
        });
    }

    public string Header { get => _header; private set => Set(ref _header, value); }
    public double Master { get => _master; private set => Set(ref _master, value); }
    public double Crossfader { get => _crossfader; private set => Set(ref _crossfader, value); }
    public ObservableCollection<ChannelStrip> Channels { get; } = [];
    public ObservableCollection<TCNetField> Fields { get; } = [];
    public ICommand RequestCommand { get; }

    protected override void Poll()
    {
        var node = _app.Node?.Nodes.FirstOrDefault(n => n.Mixer is not null);
        if (node?.Mixer is not { } mx || ReferenceEquals(mx, _shown)) return;
        _shown = mx;
        Header = $"{mx.MixerName} · mixer ID {mx.MixerId} · {TCNetText.Describe(mx.MixerType)} · from {node.NodeName}";
        Master = mx.MasterFaderLevel / 255.0;
        Crossfader = mx.CrossFader / 255.0;
        // Updated in place: only strips and fields whose values changed are replaced.
        Channels.Update(mx.Channels.Select(c => new ChannelStrip($"CH{c.Number}", c.FaderLevel / 255.0, c.AudioLevel / 255.0,
            $"{TCNetText.Describe(c.Source)} · trim {c.TrimLevel} · comp {c.CompLevel} · EQ {c.EqHi}/{c.EqHiMid}/{c.EqLowMid}/{c.EqLow} · filter {c.FilterColor} · send {c.Send} · crossfader {TCNetText.Describe(c.CrossfaderAssign)}"
            + (c.CueA ? " · CUE A" : "") + (c.CueB ? " · CUE B" : ""))).ToList());
        Fields.Update(mx.Describe().ToList());
    }
}

public sealed class PacketsViewModel : Bindable
{
    public PacketsViewModel(AppState app)
    {
        App = app;
        ClearCommand = new Command(App.ClearPackets);
    }

    public AppState App { get; }
    public ObservableCollection<PacketRow> Packets => App.Packets;
    public ICommand ClearCommand { get; }
    public bool Paused { get => App.Paused; set { App.Paused = value; Raise(); } }
    public string Filter { get => App.Filter; set { App.Filter = value; Raise(); } }
    public bool LogTime { get => App.Prefs.LogTime; set { App.Prefs.LogTime = value; Raise(); } }
    public bool LogSent { get => App.Prefs.LogSent; set { App.Prefs.LogSent = value; Raise(); } }
}

/// <summary>Build any packet, edit its bytes, decode and send.</summary>
public sealed class SendViewModel : Bindable
{
    private readonly AppState _app;
    private PacketInfo? _type;
    private string _hex = "", _target = "bcast:60000", _result = "";
    private string? _selectedTarget;

    public SendViewModel(AppState app)
    {
        _app = app;
        Types = TCNetCatalog.Packets;
        BuildCommand = new Command(Build);
        DecodeCommand = new Command(Decode);
        SendCommand = new Command(async () => await Send());
        OptInCommand = new Command(async () => await Quick(n => n.SendOptInAsync(), "Opt-IN"));
        StatusCommand = new Command(async () => await Quick(n => n.SendStatusAsync(), "Status"));
        TimeCommand = new Command(async () => await Quick(n => n.PublishTimeAsync(_app.Playback.BuildTime()), "Time"));
        TargetsCommand = new Command(LoadTargets);
        LoadTargets();
        _selectedTarget = _target;
        Type = Types[0];
    }

    public IReadOnlyList<PacketInfo> Types { get; }
    public ObservableCollection<string> Targets { get; } = [];
    public ObservableCollection<TCNetField> Fields { get; } = [];
    public PacketInfo? Type { get => _type; set { if (Set(ref _type, value)) Build(); } }
    public string Hex { get => _hex; set => Set(ref _hex, value); }
    /// <summary>What is sent to: typed in the entry or copied from the picker.</summary>
    public string Target { get => _target; set => Set(ref _target, value ?? ""); }
    /// <summary>The picker's selection; it goes null when the list changes, which leaves <see cref="Target"/> alone.</summary>
    public string? SelectedTarget
    {
        get => _selectedTarget;
        set
        {
            if (Set(ref _selectedTarget, value) && value is not null) Target = value;
        }
    }
    public string Result { get => _result; private set => Set(ref _result, value); }

    public ICommand BuildCommand { get; }
    public ICommand DecodeCommand { get; }
    public ICommand SendCommand { get; }
    public ICommand OptInCommand { get; }
    public ICommand StatusCommand { get; }
    public ICommand TimeCommand { get; }
    public ICommand TargetsCommand { get; }

    private void LoadTargets()
    {
        var list = new[] { TCNetConstants.BroadcastPort, TCNetConstants.TimePort, TCNetConstants.ApplicationPort }.Select(p => $"bcast:{p}")
            .Concat(_app.Nodes.Select(n => $"node:{n.Key}")).ToList();
        Targets.Update(list);
    }

    private void Build()
    {
        if (Type is null) return;
        TCNetPacket p = Type.Create() switch
        {
            TimePacket => _app.Playback.BuildTime(),
            StatusPacket => _app.Playback.BuildStatus(),
            ControlPacket c => Fill(c, "layer/1/state=3;"),
            TextDataPacket t => Fill(t, "Hello from TCNet"),
            RequestPacket r => Fill(r),
            var other => other,
        };
        _app.Node?.Stamp(p);
        Hex = Convert.ToHexString(p.ToArray());
        Decode();
    }

    private static TCNetPacket Fill(TextPacket p, string text)
    {
        p.Text = text;
        return p;
    }

    private static TCNetPacket Fill(RequestPacket r)
    {
        r.DataType = DataType.Metrics;
        r.Layer = 1;
        return r;
    }

    private void Decode()
    {
        Fields.Clear();
        try
        {
            var bytes = Wire.ParseHex(Hex);
            if (!TCNetPacket.TryParse(bytes, out var p, out var err)) { Result = $"Not a TCNet packet: {err}"; return; }
            foreach (var f in p!.Describe()) Fields.Add(f);
            Result = $"{p.Name} · {bytes.Length} bytes" + (p.WasPadded ? $" (shorter than {p.Length}, zero padded)" : "");
        }
        catch (FormatException ex) { Result = ex.Message; }
    }

    private async Task Send()
    {
        try
        {
            if (_app.Node is not { } node) { Result = "The node is not running."; return; }
            if (_app.Resolve(Target) is not { } ep) { Result = $"Unknown target '{Target}'."; return; }
            var bytes = Wire.ParseHex(Hex);
            await node.SendRawAsync(bytes, ep);
            Result = $"Sent {bytes.Length} bytes to {ep}.";
        }
        catch (Exception ex) { Result = ex.Message; }
    }

    private async Task Quick(Func<TCNetNode, Task> send, string what)
    {
        if (_app.Node is not { } node) { Result = "The node is not running."; return; }
        try { await send(node); Result = $"{what} sent."; }
        catch (Exception ex) { Result = ex.Message; }
    }
}

/// <summary>One simulated deck on the Master page, with its own buttons.</summary>
public sealed class DeckRow : Bindable
{
    private string _line = "";

    // Deck setters and Load lock inside the library (the node streams Time from another thread); the row only uses those
    // and reads state from the TimePacket snapshot in Refresh.
    public DeckRow(Deck deck)
    {
        Deck = deck;
        PlayCommand = new Command(() => Deck.State = LayerState.Playing);
        PauseCommand = new Command(() => Deck.State = LayerState.Paused);
        StopCommand = new Command(() => { Deck.State = LayerState.Stopped; Deck.PositionMs = 0; });
        LoadCommand = new Command(() =>
        {
            uint id = (uint)Random.Shared.Next(2000, 9999);
            Deck.Load(id, "Demo Artist", $"Track {id}", (uint)Random.Shared.Next(150_000, 420_000), Random.Shared.Next(110, 140));
        });
        AirCommand = new Command(() => Deck.OnAir = Deck.OnAir == 0 ? (byte)255 : (byte)0);
    }

    public Deck Deck { get; }
    public string Name => $"Layer {TCNetText.LayerLabel(Deck.Index)}";
    public string Line { get => _line; private set => Set(ref _line, value); }
    public ICommand PlayCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand LoadCommand { get; }
    public ICommand AirCommand { get; }

    public void Refresh(TimePacket t)
    {
        var l = t.Layers[Deck.Index];
        Line = Deck.TrackId == 0
            ? "empty"
            : $"{TCNetText.Describe(l.State)} · {TCNetUnits.Ms(l.TimeMs)} / {TCNetUnits.Ms(l.TotalMs)} · TC {l.Timecode} · {Deck.Bpm:0.0} BPM · beat {l.BeatMarker} · on air {l.OnAir} · {Deck.Artist} – {Deck.Title}";
    }
}

/// <summary>Controls the simulated master (Settings → Simulate).</summary>
public sealed class MasterViewModel : PollingViewModel
{
    private readonly AppState _app;
    private string _header = "";

    public MasterViewModel(AppState app) : base(TimeSpan.FromMilliseconds(200))
    {
        _app = app;
        Decks = new ObservableCollection<DeckRow>(app.Playback.Decks.Select(d => new DeckRow(d)));
    }

    public ObservableCollection<DeckRow> Decks { get; }
    public string Header { get => _header; private set => Set(ref _header, value); }

    protected override void Poll()
    {
        Header = _app.Prefs.Simulate
            ? _app.Node?.IsStreaming == true ? $"Simulating a master: Time every {_app.Prefs.TimeIntervalMs} ms, Status every second, answering requests and control." : "Simulation is on but the node is not running."
            : "Simulation is off – turn on 'Simulate a master' in Settings. The decks below still show what would be sent.";
        var t = _app.Playback.BuildTime();
        foreach (var d in Decks) d.Refresh(t);
    }
}

public sealed record RefRow(string Value, string Title, string Detail);

public sealed class RefGroup(string name, IEnumerable<RefRow> rows) : ObservableCollection<RefRow>(rows)
{
    public string Name { get; } = name;
}

/// <summary>The spec reference: packet layouts, options, application codes and notes, searchable.</summary>
public sealed class ReferenceViewModel : Bindable
{
    private readonly List<RefGroup> _all = [];
    private string _search = "", _copyResult = "";

    public ReferenceViewModel()
    {
        foreach (var p in TCNetCatalog.Packets)
        {
            var rows = new List<RefRow> { new($"type {p.Key}", p.Purpose, $"{p.Transport} · port {p.Port} · {p.Size} bytes · {p.Behavior}") };
            rows.AddRange(p.Layout.Select(f => new RefRow($"{f.Offset}", f.Name, $"{f.Size} byte{(f.Size == 1 ? "" : "s")}")));
            _all.Add(new RefGroup($"Packet · {p.Name}", rows));
        }
        foreach (var s in TCNetText.OptionSets)
            _all.Add(new RefGroup($"Option · {s.Name} ({s.Where}{(s.IsFlags ? ", flags" : "")})", s.Options.Select(o => new RefRow(o.Value.ToString(), o.Name, o.Description))));
        _all.Add(new RefGroup("Registered application codes", TCNetText.ApplicationCodes.Select(a => new RefRow(a.Code.ToString("X4"), a.Vendor, a.Url))));
        _all.Add(new RefGroup("Spec notes", TCNetCatalog.Notes.Select(n => new RefRow("", n.Topic, n.Note))));
        Groups = new ObservableCollection<RefGroup>(_all);
        CopyCommand = new Command(async () =>
        {
            try
            {
                await Clipboard.Default.SetTextAsync(TCNetCatalog.ToMarkdown());
                CopyResult = "Copied.";
            }
            catch (Exception ex) { CopyResult = $"Could not copy: {ex.Message}"; }
        });
    }

    public ObservableCollection<RefGroup> Groups { get; }
    public ICommand CopyCommand { get; }
    public string CopyResult { get => _copyResult; private set => Set(ref _copyResult, value); }

    public string Search
    {
        get => _search;
        set
        {
            if (!Set(ref _search, value ?? "")) return;
            Groups.Clear();
            foreach (var g in _all)
            {
                if (_search.Length == 0 || g.Name.Contains(_search, StringComparison.OrdinalIgnoreCase)) { Groups.Add(g); continue; }
                var hits = g.Where(r => r.Title.Contains(_search, StringComparison.OrdinalIgnoreCase)
                                        || r.Detail.Contains(_search, StringComparison.OrdinalIgnoreCase)
                                        || r.Value.Contains(_search, StringComparison.OrdinalIgnoreCase)).ToList();
                if (hits.Count > 0) Groups.Add(new RefGroup(g.Name, hits));
            }
        }
    }
}

public sealed record InterfaceChoice(string Label, string Address)
{
    public override string ToString() => Label;
}

/// <summary>Node configuration: every header field and option plus app behaviour.</summary>
public sealed class SettingsViewModel : Bindable
{
    private readonly AppState _app;
    private string _name, _id, _port, _bcast, _interval;
    private OptionInfo? _role;
    private InterfaceChoice? _iface;
    private bool _auth, _tcncm, _tcnasdp, _dnd, _meta, _metrics, _wave, _sync, _election, _simulate;

    public SettingsViewModel(AppState app)
    {
        _app = app;
        var p = app.Prefs;
        Roles = TCNetText.Options<NodeType>();
        _name = p.NodeName;
        _id = p.NodeId.ToString();
        _port = p.ListenerPort == 0 ? "" : p.ListenerPort.ToString();
        _bcast = p.Broadcast;
        _interval = p.TimeIntervalMs.ToString();
        _role = Roles.FirstOrDefault(r => r.Value == (long)p.Role);
        _auth = p.Options.HasFlag(NodeOptions.NeedAuthentication);
        _tcncm = p.Options.HasFlag(NodeOptions.SupportsControl);
        _tcnasdp = p.Options.HasFlag(NodeOptions.SupportsApplicationData);
        _dnd = p.Options.HasFlag(NodeOptions.DoNotDisturb);
        (_meta, _metrics, _wave, _sync, _election, _simulate) = (p.AutoMetadata, p.AutoMetrics, p.AutoWaveform, p.AutoTimeSync, p.Election, p.Simulate);
        Interfaces.Add(new InterfaceChoice("All interfaces (0.0.0.0)", ""));
        foreach (var i in TCNetNetwork.Interfaces()) Interfaces.Add(new InterfaceChoice(i.ToString(), i.Address.ToString()));
        _iface = Interfaces.FirstOrDefault(i => i.Address == p.Interface) ?? Interfaces[0];
        ApplyCommand = new Command(async () => await Apply());
        StartCommand = new Command(async () => await _app.StartAsync());
        StopCommand = new Command(async () => await _app.StopAsync());
    }

    public AppState App => _app;
    public IReadOnlyList<OptionInfo> Roles { get; }
    public ObservableCollection<InterfaceChoice> Interfaces { get; } = [];
    public string NodeName { get => _name; set => Set(ref _name, value); }
    public string NodeId { get => _id; set => Set(ref _id, value); }
    public OptionInfo? Role { get => _role; set => Set(ref _role, value); }
    public InterfaceChoice? Interface { get => _iface; set => Set(ref _iface, value); }
    public string ListenerPort { get => _port; set => Set(ref _port, value); }
    public string Broadcast { get => _bcast; set => Set(ref _bcast, value); }
    public string TimeInterval { get => _interval; set => Set(ref _interval, value); }
    public bool OptAuth { get => _auth; set => Set(ref _auth, value); }
    public bool OptTcncm { get => _tcncm; set => Set(ref _tcncm, value); }
    public bool OptTcnasdp { get => _tcnasdp; set => Set(ref _tcnasdp, value); }
    public bool OptDnd { get => _dnd; set => Set(ref _dnd, value); }
    public bool AutoMetadata { get => _meta; set => Set(ref _meta, value); }
    public bool AutoMetrics { get => _metrics; set => Set(ref _metrics, value); }
    public bool AutoWaveform { get => _wave; set => Set(ref _wave, value); }
    public bool AutoTimeSync { get => _sync; set => Set(ref _sync, value); }
    public bool Election { get => _election; set => Set(ref _election, value); }
    public bool Simulate { get => _simulate; set => Set(ref _simulate, value); }
    public ICommand ApplyCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }

    private async Task Apply()
    {
        var p = _app.Prefs;
        var name = (NodeName ?? "").Trim();
        p.NodeName = name.Length == 0 ? "TCNETAPP" : name[..Math.Min(8, name.Length)];
        NodeName = p.NodeName;
        if (int.TryParse(NodeId, out int id) && id is >= 1 and <= 65535) p.NodeId = id;
        p.Role = Role is null ? NodeType.Slave : (NodeType)Role.Value;
        p.Options = (OptAuth ? NodeOptions.NeedAuthentication : 0) | (OptTcncm ? NodeOptions.SupportsControl : 0)
                    | (OptTcnasdp ? NodeOptions.SupportsApplicationData : 0) | (OptDnd ? NodeOptions.DoNotDisturb : 0);
        p.Interface = Interface?.Address ?? "";
        p.ListenerPort = int.TryParse(ListenerPort, out int port) && port is >= TCNetConstants.UnicastPortMin and <= TCNetConstants.UnicastPortMax ? port : 0;
        p.Broadcast = (Broadcast ?? "").Trim();
        p.TimeIntervalMs = int.TryParse(TimeInterval, out int ms) ? Math.Clamp(ms, 1, 40) : 20;
        (p.AutoMetadata, p.AutoMetrics, p.AutoWaveform, p.AutoTimeSync, p.Election, p.Simulate) = (AutoMetadata, AutoMetrics, AutoWaveform, AutoTimeSync, Election, Simulate);
        await _app.RestartAsync();
    }
}
