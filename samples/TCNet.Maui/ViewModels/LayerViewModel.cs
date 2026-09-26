using TCNet.Maui.Services;
using TCNet.Networking;
using TCNet.Text;

namespace TCNet.Maui.ViewModels;

/// <summary>One layer card on the Live page: every Time, Status, Metrics and Metadata field for the layer.</summary>
public sealed class LayerViewModel : ObservableObject
{
    public LayerViewModel(int index)
    {
        Index = index;
        Label = $"Layer {TCNetText.LayerLabel(index)}";
        _name = Label;
    }

    public int Index { get; }
    public byte LayerNumber => (byte)(Index + 1);
    public string Label { get; }

    private string _name = "", _state = "—", _time = "–:––.–––", _remaining = "", _total = "", _timecode = "--:--:--:--",
        _tcInfo = "", _bpm = "", _beat = "○ ○ ○ ○", _artist = "", _title = "", _track = "", _extra = "", _onAirText = "";
    private Color _stateColor = Colors.Gray;
    private double _progress, _onAir;
    private bool _isOnAir, _hasData;
    private global::TCNet.Waveform? _waveform;
    private uint _lastTrackId;

    public string Name { get => _name; private set => SetProperty(ref _name, value); }
    public string State { get => _state; private set => SetProperty(ref _state, value); }
    public Color StateColor { get => _stateColor; private set => SetProperty(ref _stateColor, value); }
    public string Time { get => _time; private set => SetProperty(ref _time, value); }
    public string Remaining { get => _remaining; private set => SetProperty(ref _remaining, value); }
    public string Total { get => _total; private set => SetProperty(ref _total, value); }
    public string Timecode { get => _timecode; private set => SetProperty(ref _timecode, value); }
    public string TimecodeInfo { get => _tcInfo; private set => SetProperty(ref _tcInfo, value); }
    public string Bpm { get => _bpm; private set => SetProperty(ref _bpm, value); }
    public string Beat { get => _beat; private set => SetProperty(ref _beat, value); }
    public string Artist { get => _artist; private set => SetProperty(ref _artist, value); }
    public string Title { get => _title; private set => SetProperty(ref _title, value); }
    public string Track { get => _track; private set => SetProperty(ref _track, value); }
    public string Extra { get => _extra; private set => SetProperty(ref _extra, value); }
    public string OnAirText { get => _onAirText; private set => SetProperty(ref _onAirText, value); }
    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public double OnAir { get => _onAir; private set => SetProperty(ref _onAir, value); }
    public bool IsOnAir { get => _isOnAir; private set => SetProperty(ref _isOnAir, value); }
    public bool HasData { get => _hasData; private set => SetProperty(ref _hasData, value); }
    public global::TCNet.Waveform? Waveform { get => _waveform; private set => SetProperty(ref _waveform, value); }

    /// <summary>Returns true when the loaded track changed (so the caller can request a waveform).</summary>
    public bool Update(TCNetRemoteNode? node)
    {
        if (node is null)
        {
            HasData = false;
            State = "—";
            StateColor = Colors.Gray;
            return false;
        }

        var t = node.LastTime?.Layers[Index];
        var st = node.LastStatus?.Layers[Index];
        var m = node.Metrics[Index];
        var md = node.Metadata[Index];

        HasData = t is not null || st is not null || m is not null;
        Name = st?.Name is { Length: > 0 } n ? n : Label;

        var state = t?.State ?? m?.LayerState ?? st?.State ?? LayerState.Idle;
        State = TCNetText.Describe(state);
        StateColor = state switch
        {
            LayerState.Playing or LayerState.Looping => Color.FromArgb("#00C48C"),
            LayerState.Paused or LayerState.Hold or LayerState.CueButtonDown => Color.FromArgb("#E09F3E"),
            LayerState.Stopped => Color.FromArgb("#E5484D"),
            LayerState.Idle => Colors.Gray,
            _ => Color.FromArgb("#5B8DEF"),
        };

        uint cur = t?.CurrentTimeMs ?? m?.CurrentPosition ?? 0;
        uint tot = t?.TotalTimeMs ?? m?.TrackLength ?? 0;
        Time = TCNetUnits.FormatMs(cur);
        Total = TCNetUnits.FormatMs(tot);
        Remaining = TCNetUnits.FormatRemaining(cur, tot);
        Progress = tot > 0 ? Math.Clamp(cur / (double)tot, 0, 1) : 0;

        if (t is not null && node.LastTime is { } tp)
        {
            var mode = tp.EffectiveSmpteMode(t);
            Timecode = t.Timecode.ToString();
            TimecodeInfo = $"{TCNetText.Describe(mode)} · {TCNetText.Describe(t.TimecodeState)}";
            OnAir = t.OnAir / 255.0;
            IsOnAir = t.IsOnAir;
            OnAirText = t.IsOnAir ? $"ON AIR {t.OnAir}" : "off air";
        }

        byte beat = t?.BeatMarker ?? m?.BeatMarker ?? 0;
        Beat = string.Join(" ", Enumerable.Range(1, 4).Select(i => i == beat ? "●" : "○"));

        if (m is not null)
        {
            Bpm = $"{m.BpmValue:0.00} BPM";
            Extra = $"speed {TCNetUnits.FormatPercent(m.SpeedRatio)} · bend {TCNetUnits.FormatPercent(m.PitchBendRatio)} · beat #{m.BeatNumber} · {(m.IsSyncMaster ? "SYNC MASTER" : "sync slave")}";
        }
        else Bpm = "";

        Artist = md?.TrackArtist ?? "";
        Title = md?.TrackTitle ?? "";
        uint trackId = st?.TrackId ?? m?.TrackId ?? md?.TrackId ?? 0;
        Track = trackId == 0 ? "" : $"track #{trackId}{(st is not null ? $" · source {st.Source}" : "")}{(md is { TrackKey: > 0 } ? $" · key {md.TrackKey}" : "")}";

        Waveform = node.SmallWaveforms[Index] ?? node.BigWaveforms[Index];

        bool changed = trackId != 0 && trackId != _lastTrackId;
        _lastTrackId = trackId;
        return changed || (trackId != 0 && Waveform is null && state != LayerState.Idle && Random.Shared.Next(50) == 0);
    }
}
