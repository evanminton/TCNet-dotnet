using System.Diagnostics;
using TCNet.Text;

namespace TCNet;

/// <summary>A simulated deck.</summary>
public sealed class Deck
{
    internal Deck(int index)
    {
        Index = index;
        Name = $"Deck {TCNetText.LayerLabel(index)}";
    }

    public int Index { get; }
    public byte Number => (byte)(Index + 1);
    public string Name { get; set; }
    public LayerState State { get; set; } = LayerState.Idle;
    public double PositionMs { get; set; }
    public uint LengthMs { get; set; }
    public double Bpm { get; set; } = 128;
    /// <summary>1.0 = 100 %.</summary>
    public double Speed { get; set; } = 1.0;
    public uint TrackId { get; set; }
    public string Artist { get; set; } = "";
    public string Title { get; set; } = "";
    public ushort Key { get; set; }
    public byte Source { get; set; }
    public byte OnAir { get; set; }
    public bool SyncMaster { get; set; }
    public SmpteMode SmpteMode { get; set; } = SmpteMode.General;
    public TimecodeState TimecodeState { get; set; } = TimecodeState.Running;

    public bool Moving => State is LayerState.Playing or LayerState.Looping or LayerState.FastForward or LayerState.FastReverse;
    public long BeatIndex => Bpm <= 0 ? 0 : (long)(PositionMs / (60000.0 / Bpm));
    public byte BeatMarker => LengthMs == 0 || Bpm <= 0 ? (byte)0 : (byte)(BeatIndex % 4 + 1);

    public void Load(uint trackId, string artist, string title, uint lengthMs, double bpm)
    {
        TrackId = trackId;
        Artist = artist;
        Title = title;
        LengthMs = lengthMs;
        Bpm = bpm;
        PositionMs = 0;
        State = LayerState.Paused;
    }
}

/// <summary>
/// An eight-deck player model for acting as a master: moves play heads in real time and builds Time, Status and every
/// requestable data type; <see cref="Answer"/> and <see cref="Apply"/> plug into a node's request and control handlers.
/// </summary>
public sealed class Playback
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly object _gate = new();
    private double _last;

    public Playback()
    {
        for (int i = 0; i < 8; i++) Decks[i] = new Deck(i);
    }

    public Deck[] Decks { get; } = new Deck[8];
    public Deck this[int layer] => Decks[layer - 1];
    public SmpteMode SmpteMode { get; set; } = SmpteMode.Fps30;

    /// <summary>Two demo tracks, deck 1 playing on air.</summary>
    public static Playback Demo()
    {
        var p = new Playback();
        p[1].Load(1001, "Demo Artist", "First Light", 245_000, 124);
        p[2].Load(1002, "Demo Artist", "Second Wind", 312_000, 126);
        p[1].State = LayerState.Playing;
        p[1].OnAir = 255;
        p[1].SyncMaster = true;
        return p;
    }

    public void Advance()
    {
        lock (_gate)
        {
            double now = _clock.Elapsed.TotalMilliseconds, dt = now - _last;
            _last = now;
            foreach (var d in Decks)
            {
                if (!d.Moving) continue;
                double dir = d.State switch { LayerState.FastForward => 4, LayerState.FastReverse => -4, _ => 1 };
                d.PositionMs = Math.Clamp(d.PositionMs + dt * d.Speed * dir, 0, d.LengthMs);
                if (d.LengthMs > 0 && d.PositionMs >= d.LengthMs)
                {
                    if (d.State == LayerState.Looping) d.PositionMs = 0;
                    else d.State = LayerState.Stopped;
                }
            }
        }
    }

    /// <summary>Applies layer/N/state=S, layer/N/source=S and layer/N/resync. OK if anything applied.</summary>
    public NotificationCode Apply(ControlPacket control)
    {
        bool any = false;
        lock (_gate)
        {
            foreach (var c in control.Commands)
            {
                if (c.LayerNumber is not (>= 1 and <= 8) || c.LayerNumber is not int n) continue;
                var d = this[n];
                switch (c.Leaf.ToLowerInvariant())
                {
                    case "state" when byte.TryParse(c.Value, out byte s): d.State = (LayerState)s; any = true; break;
                    case "source" when byte.TryParse(c.Value, out byte src): d.Source = src; any = true; break;
                    case "resync": d.TimecodeState = TimecodeState.ForceResync; any = true; break;
                }
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
            foreach (var d in Decks)
            {
                var l = p.Layers[d.Index];
                l.TimeMs = (uint)d.PositionMs;
                l.TotalMs = d.LengthMs;
                l.BeatMarker = d.BeatMarker;
                l.State = d.State;
                l.OnAir = d.OnAir;
                l.SmpteMode = d.SmpteMode;
                l.TimecodeState = d.Moving ? d.TimecodeState : TimecodeState.Stopped;
                p.ComputeTimecode(l);
                if (d.TimecodeState == TimecodeState.ForceResync) d.TimecodeState = TimecodeState.Running;
            }
        }
        return p;
    }

    public StatusPacket BuildStatus()
    {
        var p = new StatusPacket { SmpteMode = SmpteMode };
        foreach (var d in Decks)
        {
            var l = p.Layers[d.Index];
            (l.Source, l.State, l.TrackId, l.Name) = (d.Source, d.State, d.TrackId, d.Name);
        }
        return p;
    }

    public MetricsPacket BuildMetrics(byte layer)
    {
        Advance();
        var d = this[layer];
        return new MetricsPacket
        {
            LayerId = layer,
            State = d.State,
            SyncMaster = d.SyncMaster ? (byte)1 : (byte)0,
            BeatMarker = d.BeatMarker,
            TrackLengthMs = d.LengthMs,
            PositionMs = (uint)d.PositionMs,
            Speed = (uint)Math.Round(d.Speed * TCNetConstants.SpeedUnity),
            BeatNumber = (uint)d.BeatIndex,
            Bpm = d.Bpm * d.Speed,
            PitchBend = (ushort)Math.Clamp(Math.Round(d.Speed * TCNetConstants.SpeedUnity), 0, ushort.MaxValue),
            TrackId = d.TrackId,
        };
    }

    public MetadataPacket BuildMetadata(byte layer)
    {
        var d = this[layer];
        return new MetadataPacket { LayerId = layer, Artist = d.Artist, Title = d.Title, Key = d.Key, TrackId = d.TrackId };
    }

    public BeatGrid BuildBeatGrid(byte layer)
    {
        var d = this[layer];
        var beats = new List<Beat>();
        if (d.Bpm > 0)
        {
            double ms = 60000.0 / d.Bpm;
            for (int b = 0; b * ms < d.LengthMs && b <= ushort.MaxValue; b++)
                beats.Add(new Beat((ushort)b, b % 4 == 0 ? BeatType.DownBeat : BeatType.UpBeat, (uint)(b * ms)));
        }
        return new BeatGrid(beats);
    }

    public Waveform BuildWaveform(byte layer, int bars)
    {
        var d = this[layer];
        var rnd = new Random((int)d.TrackId * 31 + layer);
        var list = new WaveformBar[bars];
        for (int i = 0; i < bars; i++)
        {
            double env = 0.55 + 0.45 * Math.Sin(i / (double)bars * Math.PI);
            list[i] = new WaveformBar((byte)(env * rnd.Next(120, 255)), (byte)rnd.Next(40, 220));
        }
        return new Waveform(list);
    }

    public CueDataPacket BuildCues(byte layer)
    {
        var d = this[layer];
        var p = new CueDataPacket { LayerId = layer };
        if (d.Bpm <= 0) return p;
        double phrase = 16 * 4 * 60000.0 / d.Bpm;
        for (int i = 0; i < 8 && i * phrase < d.LengthMs; i++)
        {
            p.Cues[i].Type = 1;
            p.Cues[i].InMs = (uint)(i * phrase);
            p.Cues[i].Color = new CueColor((byte)(30 * i), (byte)(220 - 20 * i), 200);
        }
        return p;
    }

    /// <summary>Request handler: every data type for layers 1–8 (mixer is not simulated).</summary>
    public IReadOnlyList<TCNetPacket>? Answer(RequestPacket request)
    {
        byte layer = request.Layer;
        if (layer is < 1 or > 8) return null;
        return request.DataType switch
        {
            DataType.Metrics => [BuildMetrics(layer)],
            DataType.Metadata => [BuildMetadata(layer)],
            DataType.CueData => [BuildCues(layer)],
            DataType.BeatGrid => ChunkedPacket.Split(BuildBeatGrid(layer).Encode(), layer, () => new BeatGridPacket()),
            DataType.SmallWaveform => [SmallWave(layer)],
            DataType.BigWaveform => ChunkedPacket.Split(BuildWaveform(layer, (int)Math.Max(1200, this[layer].LengthMs / 20)).Encode(), layer, () => new BigWaveformPacket()),
            _ => null,
        };
    }

    private SmallWaveformPacket SmallWave(byte layer)
    {
        var p = new SmallWaveformPacket { LayerId = layer };
        p.SetBars(BuildWaveform(layer, 1200).Bars);
        return p;
    }
}
