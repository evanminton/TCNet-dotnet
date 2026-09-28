using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TCNet.Native;

/// <summary>C <c>tcnet_time_layer</c> (20 bytes): one layer of a Time packet.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TimeLayerData
{
    public uint TimeMs;
    public uint TotalMs;
    public byte BeatMarker;
    public byte State;
    public byte SmpteMode;
    public byte TimecodeState;
    public byte Hours;
    public byte Minutes;
    public byte Seconds;
    public byte Frames;
    public byte OnAir;
    public byte Reserved1;
    public byte Reserved2;
    public byte Reserved3;
}

[InlineArray(8)]
internal struct TimeLayers8
{
    private TimeLayerData _element0;
}

/// <summary>C <c>tcnet_time</c> (164 bytes): general SMPTE mode, flags and the eight layers 1–4, A, B, M, C.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TimeData
{
    /// <summary>Flag: compute each layer's HH:MM:SS:FF from its time when encoding.</summary>
    public const byte AutoTimecode = 1;

    public byte SmpteMode;
    public byte Flags;
    public byte Reserved1;
    public byte Reserved2;
    public TimeLayers8 Layers;

    public static TimePacket ToPacket(TimeData d)
    {
        var p = new TimePacket { SmpteMode = (SmpteMode)d.SmpteMode };
        for (int i = 0; i < 8; i++)
        {
            TimeLayerData s = d.Layers[i];
            var l = p.Layers[i];
            l.TimeMs = s.TimeMs;
            l.TotalMs = s.TotalMs;
            l.BeatMarker = s.BeatMarker;
            l.State = (LayerState)s.State;
            l.SmpteMode = (SmpteMode)s.SmpteMode;
            l.TimecodeState = (TimecodeState)s.TimecodeState;
            l.Timecode = new Timecode(s.Hours, s.Minutes, s.Seconds, s.Frames);
            l.OnAir = s.OnAir;
            if ((d.Flags & AutoTimecode) != 0) p.ComputeTimecode(l);
        }
        return p;
    }

    public static TimeData FromPacket(TimePacket p)
    {
        var d = new TimeData { SmpteMode = (byte)p.SmpteMode };
        for (int i = 0; i < 8; i++)
        {
            var l = p.Layers[i];
            d.Layers[i] = new TimeLayerData
            {
                TimeMs = l.TimeMs,
                TotalMs = l.TotalMs,
                BeatMarker = l.BeatMarker,
                State = (byte)l.State,
                SmpteMode = (byte)l.SmpteMode,
                TimecodeState = (byte)l.TimecodeState,
                Hours = l.Timecode.Hours,
                Minutes = l.Timecode.Minutes,
                Seconds = l.Timecode.Seconds,
                Frames = l.Timecode.Frames,
                OnAir = l.OnAir,
            };
        }
        return d;
    }
}
