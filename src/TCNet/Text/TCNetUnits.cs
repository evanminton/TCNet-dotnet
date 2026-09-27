using System.Globalization;

namespace TCNet.Text;

/// <summary>Formatting for the units used in TCNet fields.</summary>
public static class TCNetUnits
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>ms as "m:ss.fff" or "h:mm:ss.fff".</summary>
    public static string Ms(uint ms)
    {
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1
            ? string.Create(Inv, $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}")
            : string.Create(Inv, $"{t.Minutes}:{t.Seconds:00}.{t.Milliseconds:000}");
    }

    /// <summary>Remaining time "-m:ss.f".</summary>
    public static string Remaining(uint current, uint total)
    {
        var t = TimeSpan.FromMilliseconds(total > current ? total - current : 0);
        return string.Create(Inv, $"-{(int)t.TotalMinutes}:{t.Seconds:00}.{t.Milliseconds / 100}");
    }

    public static string Duration(TimeSpan t) =>
        t.TotalHours >= 1 ? string.Create(Inv, $"{(int)t.TotalHours}h {t.Minutes:00}m {t.Seconds:00}s")
        : t.TotalMinutes >= 1 ? string.Create(Inv, $"{t.Minutes}m {t.Seconds:00}s")
        : string.Create(Inv, $"{t.Seconds}s");

    /// <summary>Ratio as "100.00 %".</summary>
    public static string Percent(double ratio) => (ratio * 100).ToString("0.00", Inv) + " %";

    /// <summary>µs as "1.234 ms".</summary>
    public static string Micros(long us) => (us / 1000.0).ToString("0.000", Inv) + " ms";

    public static string Bytes(long b) =>
        b >= 1 << 20 ? (b / 1048576.0).ToString("0.0", Inv) + " MB"
        : b >= 1 << 10 ? (b / 1024.0).ToString("0.0", Inv) + " KB"
        : b.ToString(Inv) + " B";

    /// <summary>0–255 as a percentage of full scale.</summary>
    public static string Level(byte v) => (v / 255.0 * 100).ToString("0", Inv) + " %";
}
