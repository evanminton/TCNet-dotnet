using System.Diagnostics;

namespace TCNet;

/// <summary>A node's timer: µs 0–999999 wrapping every second, and uptime in seconds rolling over every 12 h.</summary>
public sealed class TCNetClock
{
    private readonly long _start = Stopwatch.GetTimestamp();

    public long ElapsedMicros => (long)((Stopwatch.GetTimestamp() - _start) * (1_000_000.0 / Stopwatch.Frequency));

    /// <summary>Header timestamp, 0–999999 µs.</summary>
    public uint Timestamp => (uint)(ElapsedMicros % TCNetConstants.MicrosPerSecond);

    /// <summary>Uptime 0–43199 s.</summary>
    public ushort Uptime => (ushort)(ElapsedMicros / 1_000_000 % TCNetConstants.UptimeRollover);

    private const long M = TCNetConstants.MicrosPerSecond;

    /// <summary>Forward distance from a to b on the one-second wheel.</summary>
    public static uint Forward(uint a, uint b) => (uint)((((long)b - a) % M + M) % M);

    public static uint Add(uint ts, long micros) => (uint)(((ts + micros) % M + M) % M);

    /// <summary>Shortest signed a − b on the wheel (−500000…499999).</summary>
    public static int Difference(uint a, uint b)
    {
        long d = (((long)a - b) % M + M) % M;
        return (int)(d >= M / 2 ? d - M : d);
    }
}

/// <summary>Result of one time sync exchange.</summary>
/// <param name="DelayMicros">(now − echoed timestamp) / 2.</param>
/// <param name="RemoteNow">Remote timer when the response arrived: its timestamp + delay.</param>
/// <param name="OffsetMicros">Remote timer − local timer.</param>
public readonly record struct TimeSyncResult(uint DelayMicros, uint RemoteNow, int OffsetMicros)
{
    public uint RoundTripMicros => DelayMicros * 2;
}

/// <summary>The spec's time sync arithmetic.</summary>
public static class TimeSync
{
    /// <summary>Delay = (current timer − remote timestamp) / 2; time of remote node = timestamp + delay.</summary>
    public static TimeSyncResult Compute(uint remoteTimestamp, uint echoedLocal, uint localNow)
    {
        uint delay = TCNetClock.Forward(echoedLocal, localNow) / 2;
        uint remoteNow = TCNetClock.Add(remoteTimestamp, delay);
        return new TimeSyncResult(delay, remoteNow, TCNetClock.Difference(remoteNow, localNow));
    }

    public static TimeSyncResult Compute(TimeSyncPacket response, uint localNow) =>
        Compute(response.Timestamp, response.RemoteTimestamp, localNow);

    /// <summary>Optional refinement: average several rounds (delays averaged, offsets averaged around the first).</summary>
    public static TimeSyncResult Average(IReadOnlyList<TimeSyncResult> rounds)
    {
        if (rounds.Count == 0) throw new ArgumentException("No rounds.", nameof(rounds));
        uint delay = (uint)Math.Round(rounds.Average(r => (double)r.DelayMicros));
        int baseOffset = rounds[0].OffsetMicros;
        double rel = rounds.Average(r => (double)NormalizeOffset(r.OffsetMicros - baseOffset));
        int offset = NormalizeOffset(baseOffset + (int)Math.Round(rel));
        return new TimeSyncResult(delay, rounds[^1].RemoteNow, offset);
    }

    private static int NormalizeOffset(int o)
    {
        const int M = (int)TCNetConstants.MicrosPerSecond;
        o %= M;
        if (o >= M / 2) o -= M;
        if (o < -M / 2) o += M;
        return o;
    }
}
