namespace TCNet;

/// <summary>HH:MM:SS:FF as carried in the Time packet.</summary>
public readonly record struct Timecode(byte Hours, byte Minutes, byte Seconds, byte Frames)
{
    public override string ToString() => $"{Hours:00}:{Minutes:00}:{Seconds:00}:{Frames:00}";

    /// <summary>Frames counted per second (29.97 counts 30, non-drop).</summary>
    public static int FrameCount(SmpteMode mode) => mode switch
    {
        SmpteMode.Fps24 => 24,
        SmpteMode.Fps25 => 25,
        _ => 30,
    };

    public static double FrameRate(SmpteMode mode) => mode == SmpteMode.Fps2997 ? 30000.0 / 1001.0 : FrameCount(mode);

    public static Timecode FromMilliseconds(uint ms, SmpteMode mode)
    {
        int fps = FrameCount(mode);
        long frames = (long)Math.Floor(ms / 1000.0 * FrameRate(mode));
        long secs = frames / fps;
        return new Timecode((byte)(secs / 3600 % 24), (byte)(secs / 60 % 60), (byte)(secs % 60), (byte)(frames % fps));
    }

    public uint ToMilliseconds(SmpteMode mode)
    {
        long frames = ((Hours * 60L + Minutes) * 60 + Seconds) * FrameCount(mode) + Frames;
        return (uint)Math.Round(frames * 1000.0 / FrameRate(mode));
    }

    public static Timecode Parse(string s)
    {
        var p = s.Split(':', ';', '.');
        if (p.Length != 4) throw new FormatException("Expected HH:MM:SS:FF.");
        return new Timecode(byte.Parse(p[0]), byte.Parse(p[1]), byte.Parse(p[2]), byte.Parse(p[3]));
    }
}
