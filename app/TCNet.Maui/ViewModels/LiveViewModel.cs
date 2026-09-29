using System.Collections.ObjectModel;
using TCNet.Maui.Services;
using TCNet.Networking;
using TCNet.Text;

namespace TCNet.Maui.ViewModels;

/// <summary>Base for view models refreshed on a UI timer while their page is visible.</summary>
public abstract class PollingViewModel(TimeSpan interval) : Bindable
{
    private IDispatcherTimer? _timer;

    public void Start()
    {
        if (_timer is null)
        {
            _timer = Application.Current!.Dispatcher.CreateTimer();
            _timer.Interval = interval;
            _timer.Tick += (_, _) => Poll();
        }
        Poll();
        _timer.Start();
    }

    public void Stop() => _timer?.Stop();

    protected abstract void Poll();
}

/// <summary>One layer: every Time, Status, Metrics, Metadata and waveform value.</summary>
public sealed class LayerCard(int index) : Bindable
{
    private string _name = $"Layer {TCNetText.LayerLabel(index)}", _state = "—", _time = "–", _remaining = "", _total = "", _tc = "--:--:--:--",
        _tcInfo = "", _bpm = "", _beat = "○ ○ ○ ○", _title = "", _artist = "", _track = "", _detail = "", _onAirText = "";
    private Color _stateColor = Colors.Gray;
    private double _progress, _onAir;
    private bool _isOnAir;
    private IReadOnlyList<WaveformBar>? _bars;
    private uint _lastTrack;

    public int Index { get; } = index;
    public byte Layer => (byte)(Index + 1);
    public string Label => $"Layer {TCNetText.LayerLabel(Index)}";
    public string Name { get => _name; private set => Set(ref _name, value); }
    public string State { get => _state; private set => Set(ref _state, value); }
    public Color StateColor { get => _stateColor; private set => Set(ref _stateColor, value); }
    public string Time { get => _time; private set => Set(ref _time, value); }
    public string Remaining { get => _remaining; private set => Set(ref _remaining, value); }
    public string Total { get => _total; private set => Set(ref _total, value); }
    public string Timecode { get => _tc; private set => Set(ref _tc, value); }
    public string TimecodeInfo { get => _tcInfo; private set => Set(ref _tcInfo, value); }
    public string Bpm { get => _bpm; private set => Set(ref _bpm, value); }
    public string Beat { get => _beat; private set => Set(ref _beat, value); }
    public string Title { get => _title; private set => Set(ref _title, value); }
    public string Artist { get => _artist; private set => Set(ref _artist, value); }
    public string Track { get => _track; private set => Set(ref _track, value); }
    public string Detail { get => _detail; private set => Set(ref _detail, value); }
    public string OnAirText { get => _onAirText; private set => Set(ref _onAirText, value); }
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public double OnAir { get => _onAir; private set => Set(ref _onAir, value); }
    public bool IsOnAir { get => _isOnAir; private set => Set(ref _isOnAir, value); }
    public IReadOnlyList<WaveformBar>? Bars { get => _bars; private set => Set(ref _bars, value); }

    /// <summary>Returns true when a new track appeared without a waveform (the caller may request one).</summary>
    public bool Update(RemoteNode? node)
    {
        if (node is null)
        {
            State = "—";
            StateColor = Colors.Gray;
            return false;
        }
        var t = node.Time?.Layers[Index];
        var st = node.Status?.Layers[Index];
        var m = node.Metrics[Index];
        var md = node.Metadata[Index];

        Name = st is { Name.Length: > 0 } ? st.Name : Label;
        var state = t?.State ?? m?.State ?? st?.State ?? LayerState.Idle;
        State = TCNetText.Describe(state);
        StateColor = state switch
        {
            LayerState.Playing or LayerState.Looping => Color.FromArgb("#00C48C"),
            LayerState.Paused or LayerState.Hold or LayerState.CueDown => Color.FromArgb("#E09F3E"),
            LayerState.Stopped => Color.FromArgb("#E5484D"),
            LayerState.Idle => Colors.Gray,
            _ => Color.FromArgb("#5B8DEF"),
        };

        uint cur = t?.TimeMs ?? m?.PositionMs ?? 0, tot = t?.TotalMs ?? m?.TrackLengthMs ?? 0;
        Time = TCNetUnits.Ms(cur);
        Total = TCNetUnits.Ms(tot);
        Remaining = TCNetUnits.Remaining(cur, tot);
        Progress = tot > 0 ? Math.Clamp(cur / (double)tot, 0, 1) : 0;

        if (t is not null && node.Time is { } tp)
        {
            Timecode = t.Timecode.ToString();
            TimecodeInfo = $"{TCNetText.Describe(tp.ModeOf(t))} · {TCNetText.Describe(t.TimecodeState)}";
            OnAir = t.OnAir / 255.0;
            IsOnAir = t.IsOnAir;
            OnAirText = t.IsOnAir ? $"ON AIR {t.OnAir}" : "";
        }

        byte beat = t?.BeatMarker ?? m?.BeatMarker ?? 0;
        Beat = string.Join(" ", Enumerable.Range(1, 4).Select(i => i == beat ? "●" : "○"));
        Bpm = m is null ? "" : $"{m.Bpm:0.00} BPM";
        Detail = m is null ? "" : $"speed {TCNetUnits.Percent(m.SpeedRatio)} · pitch {TCNetUnits.Percent(m.PitchRatio)} · beat #{m.BeatNumber} · {(m.SyncMaster == 1 ? "sync master" : "sync slave")}";
        Title = md?.Title ?? "";
        Artist = md?.Artist ?? "";
        uint track = st?.TrackId ?? m?.TrackId ?? md?.TrackId ?? 0;
        Track = track == 0 ? "" : $"track {track}" + (st is not null ? $" · source {st.Source}" : "") + (md is { Key: > 0 } ? $" · key {md.Key}" : "");
        Bars = node.SmallWaveforms[Index]?.Bars ?? node.BigWaveforms[Index]?.Bars;

        bool fresh = track != 0 && track != _lastTrack;
        _lastTrack = track;
        return fresh && Bars is null;
    }
}

public sealed class LiveViewModel : PollingViewModel
{
    private readonly AppState _app;
    private NodeRow? _source;
    private string _header = "";

    public LiveViewModel(AppState app) : base(TimeSpan.FromMilliseconds(100))
    {
        _app = app;
        Layers = new ObservableCollection<LayerCard>(Enumerable.Range(0, 8).Select(i => new LayerCard(i)));
    }

    public ObservableCollection<NodeRow> Sources => _app.Nodes;
    public ObservableCollection<LayerCard> Layers { get; }
    public NodeRow? Source { get => _source; set => Set(ref _source, value); }
    public string Header { get => _header; private set => Set(ref _header, value); }

    protected override void Poll()
    {
        if (Source is null || !Sources.Contains(Source))
            Source = Sources.FirstOrDefault(r => r.Node.Time is not null) ?? Sources.FirstOrDefault();
        var node = Source?.Node;
        Header = node switch
        {
            null => _app.IsRunning ? "Waiting for a node…" : "Stopped – start the node in Settings",
            { Time: null } => $"{node.NodeName}: no Time packets yet",
            { Time: { } time, TimeReceived: { } at } => $"{node.NodeName} · general SMPTE {TCNetText.Describe(time.SmpteMode)} · last Time packet {(DateTime.UtcNow - at).TotalMilliseconds:0} ms ago",
            _ => $"{node.NodeName}: no Time packets yet",
        };
        foreach (var card in Layers)
        {
            if (card.Update(node) && node is not null && _app.Prefs.AutoWaveform && _app.Node is { } local && node.EndPoint is not null)
                _ = Fetch(local, node, card.Layer);
        }
    }

    private static async Task Fetch(TCNetNode local, RemoteNode node, byte layer)
    {
        try { await local.RequestSmallWaveformAsync(node, layer); }
        catch (Exception) { }
    }
}
