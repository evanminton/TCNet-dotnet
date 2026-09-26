using System.Diagnostics;

namespace TCNet;

/// <summary>One simulated deck/layer.</summary>
public sealed class PlaybackLayer
{
    internal PlaybackLayer(int index) => Index = index;

    public int Index { get; }
    public byte LayerNumber => (byte)(Index + 1);

    public string Name { get; set; } = "";
    public LayerState State { get; set; } = LayerState.Idle;
    public double PositionMs { get; set; }
    public uint TotalMs { get; set; }
    public double Bpm { get; set; } = 128;
    /// <summary>Playback speed ratio (1.0 = 100 %).</summary>
    public double Speed { get; set; } = 1.0;
    public uint TrackId { get; set; }
    public string Artist { get; set; } = "";
    public string Title { get; set; } = "";
    public ushort Key { get; set; }
    public byte Source { get; set; }
    public byte OnAir { get; set; }
    public SmpteMode SmpteMode { get; set; } = SmpteMode.UseGeneral;
    public TimecodeState TimecodeState { get; set; } = TimecodeState.Running;
    public bool SyncMaster { get; set; }

    public bool IsMoving => State is LayerState.Playing or LayerState.Looping or LayerState.FastForward or LayerState.FastReverse;

    /// <summary>Beat index from 0, based on BPM and position.</summary>
    public long BeatIndex => Bpm <= 0 ? 0 : (long)(PositionMs / (60000.0 / Bpm));

    /// <summary>Beat in bar, 1–4.</summary>
    public byte BeatMarker => Bpm <= 0 || TotalMs == 0 ? (byte)0 : (byte)(BeatIndex % 4 + 1);

    /// <summary>Loads a track and cues it at 0.</summary>
    public void Load(uint trackId, string artist, string title, uint totalMs, double bpm)
    {
        TrackId = trackId;
        Artist = artist;
        Title = title;
        TotalMs = totalMs;
        Bpm = bpm;
        PositionMs = 0;
        State = LayerState.Paused;
    }
}

/// <summary>
/// A small eight-layer player model for acting as a TCNet master: advances play heads in real time and builds
/// Time, Status, Metrics, Metadata, Beat Grid, Cue and waveform packets, and answers requests.
/// </summary>
public sealed class TCNetPlayback
{
    private readonly Stopwatch _watch = Stopwatch.StartNew();
    private double _lastMs;
    private readonly object _gate = new();

    /// <summary>Lock to hold while changing layers from another thread than the one sending packets.</summary>
    public object SyncRoot => _gate;

    public TCNetPlayback()
    {
        for (int i = 0; i < Layers.Length; i++) Layers[i] = new PlaybackLayer(i) { Name = $"Layer {Text.TCNetText.LayerLabel(i)}" };
    }

    public PlaybackLayer[] Layers { get; } = new PlaybackLayer[8];

    public SmpteMode SmpteMode { get; set; } = SmpteMode.Fps30;

    public PlaybackLayer this[int layerNumber] => Layers[layerNumber - 1];

    /// <summary>Advances all moving layers by the wall-clock time since the last call.</summary>
    public void Advance()
    {
        lock (_gate)
        {
            double now = _watch.Elapsed.TotalMilliseconds;
            double dt = now - _lastMs;
            _lastMs = now;
            foreach (var l in Layers)
            {
                if (!l.IsMoving) continue;
                double dir = l.State == LayerState.FastReverse ? -4 : l.State == LayerState.FastForward ? 4 : 1;
                l.PositionMs = Math.Clamp(l.PositionMs + dt * l.Speed * dir, 0, l.TotalMs);
                if (l.TotalMs > 0 && l.PositionMs >= l.TotalMs && l.State == LayerState.Looping) l.PositionMs = 0;
                else if (l.TotalMs > 0 && l.PositionMs >= l.TotalMs) l.State = LayerState.Stopped;
            }
        }
    }

    /// <summary>Applies control commands like "layer/1/state=6;" or "layer/5/source=1;". Returns OK or not possible.</summary>
    public NotificationCode Apply(ControlPacket control)
    {
        lock (_gate) return ApplyCore(control);
    }

    private NotificationCode ApplyCore(ControlPacket control)
    {
        bool any = false;
        foreach (var c in control.Commands)
        {
            if (c.LayerNumber is not { } n || n < 1 || n > 8) continue;
            var l = this[n];
            var leaf = c.Path.Split('/').Last().ToLowerInvariant();
            switch (leaf)
            {
                case "state" when byte.TryParse(c.Value, out var s): l.State = (LayerState)s; any = true; break;
                case "source" when byte.TryParse(c.Value, out var src): l.Source = src; any = true; break;
                case "resync": l.TimecodeState = TimecodeState.ForceResync; any = true; break;
            }
        }
        return any ? NotificationCode.Ok : NotificationCode.RequestNotPossible;
    }

    public TimePacket BuildTime()
    {
        Advance();
        var p = new TimePacket { SmpteMode = SmpteMode };
        lock (_gate)
        {
            foreach (var l in Layers)
            {
                var t = p.Layers[l.Index];
                t.CurrentTimeMs = (uint)l.PositionMs;
                t.TotalTimeMs = l.TotalMs;
                t.BeatMarker = l.BeatMarker;
                t.State = l.State;
                t.OnAir = l.OnAir;
                t.SmpteMode = l.SmpteMode;
                t.TimecodeState = l.IsMoving ? l.TimecodeState : TimecodeState.Stopped;
                p.UpdateTimecodeFromTime(t);
                if (l.TimecodeState == TimecodeState.ForceResync) l.TimecodeState = TimecodeState.Running;
            }
        }
        return p;
    }

    public StatusPacket BuildStatus()
    {
        var p = new StatusPacket { SmpteMode = SmpteMode };
        lock (_gate)
        {
            foreach (var l in Layers)
            {
                var s = p.Layers[l.Index];
                s.Source = l.Source;
                s.State = l.State;
                s.TrackId = l.TrackId;
                s.Name = l.Name;
            }
        }
        return p;
    }

    public MetricsDataPacket BuildMetrics(byte layer)
    {
        lock (_gate) return BuildMetricsCore(layer);
    }

    private MetricsDataPacket BuildMetricsCore(byte layer)
    {
        Advance();
        var l = this[layer];
        return new MetricsDataPacket
        {
            LayerId = layer,
            LayerState = l.State,
            SyncMaster = l.SyncMaster ? (byte)1 : (byte)0,
            BeatMarker = l.BeatMarker,
            TrackLength = l.TotalMs,
            CurrentPosition = (uint)l.PositionMs,
            Speed = (uint)Math.Round(l.Speed * TCNetConstants.SpeedUnity),
            BeatNumber = (uint)l.BeatIndex,
            BpmValue = l.Bpm * l.Speed,
            PitchBend = (ushort)Math.Clamp(Math.Round(l.Speed * TCNetConstants.SpeedUnity), 0, ushort.MaxValue),
            TrackId = l.TrackId,
        };
    }

    public MetadataPacket BuildMetadata(byte layer)
    {
        var l = this[layer];
        lock (_gate) return new MetadataPacket { LayerId = layer, TrackArtist = l.Artist, TrackTitle = l.Title, TrackKey = l.Key, TrackId = l.TrackId };
    }

    /// <summary>Beat grid computed from BPM (downbeat every 4 beats).</summary>
    public BeatGrid BuildBeatGrid(byte layer)
    {
        var l = this[layer];
        var list = new List<BeatGridEntry>();
        if (l.Bpm > 0 && l.TotalMs > 0)
        {
            double beatMs = 60000.0 / l.Bpm;
            for (int b = 0; b * beatMs < l.TotalMs && b <= ushort.MaxValue; b++)
                list.Add(new BeatGridEntry((ushort)b, b % 4 == 0 ? BeatType.DownBeat : BeatType.UpBeat, (uint)(b * beatMs)));
        }
        return new BeatGrid(list);
    }

    /// <summary>A synthetic waveform with <paramref name="bars"/> bars (deterministic per track ID).</summary>
    public Waveform BuildWaveform(byte layer, int bars)
    {
        var l = this[layer];
        var rnd = new Random((int)l.TrackId + layer);
        var list = new WaveformBar[bars];
        for (int i = 0; i < bars; i++)
        {
            double env = 0.55 + 0.45 * Math.Sin(i / (double)bars * Math.PI);
            list[i] = new WaveformBar((byte)(env * (120 + rnd.Next(0, 135))), (byte)rnd.Next(40, 220));
        }
        return new Waveform(list);
    }

    /// <summary>Request handler for <see cref="Networking.TCNetNode.RequestHandler"/>.</summary>
    public IReadOnlyList<TCNetPacket>? HandleRequest(RequestPacket request)
    {
        // Called on receive threads while the owner advances and edits layers.
        lock (_gate) return HandleRequestCore(request);
    }

    private IReadOnlyList<TCNetPacket>? HandleRequestCore(RequestPacket request)
    {
        byte layer = request.Layer;
        if (request.DataType != DataType.Mixer && (layer < 1 || layer > 8)) return null;
        switch (request.DataType)
        {
            case DataType.Metrics: return [BuildMetrics(layer)];
            case DataType.Metadata: return [BuildMetadata(layer)];
            case DataType.BeatGrid:
                return ChunkedDataPacket.Split(BuildBeatGrid(layer).Encode(), layer, () => new BeatGridDataPacket());
            case DataType.SmallWaveform:
            {
                var p = new SmallWaveformPacket { LayerId = layer };
                p.SetBars(BuildWaveform(layer, 1200).Bars);
                return [p];
            }
            case DataType.BigWaveform:
            {
                int bars = (int)Math.Max(1200, this[layer].TotalMs / 20);
                return ChunkedDataPacket.Split(BuildWaveform(layer, bars).Encode(), layer, () => new BigWaveformPacket());
            }
            case DataType.CueData:
            {
                var l = this[layer];
                var p = new CueDataPacket { LayerId = layer, LoopIn = 0, LoopOut = 0 };
                if (l.Bpm > 0)
                {
                    double bar = 4 * 60000.0 / l.Bpm;
                    for (int i = 0; i < 8 && (i * 16 * bar) < l.TotalMs; i++)
                    {
                        p.Cues[i].Type = 1;
                        p.Cues[i].InTime = (uint)(i * 16 * bar);
                        p.Cues[i].Color = new CueColor((byte)(40 * i), (byte)(255 - 30 * i), 180);
                    }
                }
                return [p];
            }
            default: return null;
        }
    }
}
