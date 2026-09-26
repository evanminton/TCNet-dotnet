using System.Globalization;

namespace TCNet.Text;

/// <summary>Formatting helpers for the units used by TCNet fields.</summary>
public static class TCNetUnits
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Milliseconds as "m:ss.fff" (or "h:mm:ss.fff" past an hour).</summary>
    public static string FormatMs(uint ms)
    {
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1
            ? string.Create(Inv, $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}")
            : string.Create(Inv, $"{t.Minutes}:{t.Seconds:00}.{t.Milliseconds:000}");
    }

    /// <summary>Remaining time as "-m:ss.f".</summary>
    public static string FormatRemaining(uint current, uint total)
    {
        uint rem = total > current ? total - current : 0;
        var t = TimeSpan.FromMilliseconds(rem);
        return string.Create(Inv, $"-{(int)t.TotalMinutes}:{t.Seconds:00}.{t.Milliseconds / 100}");
    }

    /// <summary>A duration as "1h 02m 03s" / "2m 03s" / "3s".</summary>
    public static string FormatDuration(TimeSpan t) =>
        t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes:00}m {t.Seconds:00}s"
        : t.TotalMinutes >= 1 ? $"{t.Minutes}m {t.Seconds:00}s"
        : $"{t.Seconds}s";

    /// <summary>A ratio as "100.00 %" / "+2.50 %" style when <paramref name="signedDelta"/>.</summary>
    public static string FormatPercent(double ratio, bool signedDelta = false) =>
        signedDelta
            ? ((ratio - 1) * 100).ToString("+0.00;-0.00;0.00", Inv) + " %"
            : (ratio * 100).ToString("0.00", Inv) + " %";

    /// <summary>A byte 0–255 as a percentage of full scale.</summary>
    public static string FormatLevel(byte value) => (value / 255.0 * 100).ToString("0", Inv) + " %";

    /// <summary>A µs offset/delay as "1.234 ms".</summary>
    public static string FormatMicros(long micros) => (micros / 1000.0).ToString("0.000", Inv) + " ms";

    /// <summary>Bytes as "1.2 KB".</summary>
    public static string FormatBytes(long bytes) =>
        bytes >= 1024 * 1024 ? (bytes / 1048576.0).ToString("0.0", Inv) + " MB"
        : bytes >= 1024 ? (bytes / 1024.0).ToString("0.0", Inv) + " KB"
        : bytes + " B";
}
