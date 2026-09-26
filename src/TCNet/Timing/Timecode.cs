namespace TCNet;

/// <summary>SMPTE timecode as carried per layer in the Time packet.</summary>
public readonly record struct Timecode(byte Hours, byte Minutes, byte Seconds, byte Frames)
{
    public override string ToString() => $"{Hours:00}:{Minutes:00}:{Seconds:00}:{Frames:00}";

    /// <summary>Nominal frames per second for a mode (29.97 counts 30 frames).</summary>
    public static int FramesPerSecond(SmpteMode mode) => mode switch
    {
        SmpteMode.Fps24 => 24,
        SmpteMode.Fps25 => 25,
        SmpteMode.Fps29_97 => 30,
        SmpteMode.Fps30 => 30,
        _ => 30,
    };

    /// <summary>Exact frame rate (29.97 → 30000/1001).</summary>
    public static double FrameRate(SmpteMode mode) => mode == SmpteMode.Fps29_97 ? 30000.0 / 1001.0 : FramesPerSecond(mode);

    /// <summary>Converts a millisecond position to timecode (hours wrap at 24; 29.97 uses non-drop counting).</summary>
    public static Timecode FromMilliseconds(uint ms, SmpteMode mode)
    {
        int fps = FramesPerSecond(mode);
        var (num, den) = Rate(mode);
        long totalFrames = ms * num / (1000L * den);
        long frames = totalFrames % fps;
        long totalSeconds = totalFrames / fps;
        return new Timecode(
            (byte)(totalSeconds / 3600 % 24),
            (byte)(totalSeconds / 60 % 60),
            (byte)(totalSeconds % 60),
            (byte)frames);
    }

    /// <summary>
    /// Converts timecode back to milliseconds: the first whole millisecond inside the frame, so
    /// <see cref="FromMilliseconds"/> returns the same timecode.
    /// </summary>
    public uint ToMilliseconds(SmpteMode mode)
    {
        int fps = FramesPerSecond(mode);
        var (num, den) = Rate(mode);
        long totalFrames = ((Hours * 60L + Minutes) * 60 + Seconds) * fps + Frames;
        return (uint)((totalFrames * 1000L * den + num - 1) / num);
    }

    /// <summary>Exact frame rate as a fraction (integer maths avoids off-by-one frames).</summary>
    private static (long Num, long Den) Rate(SmpteMode mode) =>
        mode == SmpteMode.Fps29_97 ? (30000, 1001) : (FramesPerSecond(mode), 1);

    /// <summary>Parses "HH:MM:SS:FF" (':' ';' or '.' separators).</summary>
    public static Timecode Parse(string s)
    {
        var parts = s.Split(':', ';', '.');
        if (parts.Length != 4) throw new FormatException("Expected HH:MM:SS:FF.");
        return new Timecode(byte.Parse(parts[0]), byte.Parse(parts[1]), byte.Parse(parts[2]), byte.Parse(parts[3]));
    }
}
