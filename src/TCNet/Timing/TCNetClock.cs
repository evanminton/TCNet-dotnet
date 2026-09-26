using System.Diagnostics;

namespace TCNet;

/// <summary>
/// The node's internal timer: microseconds 0–999999 that wrap every second (spec "Network participation – first step"),
/// plus uptime in seconds that rolls over every 12 hours.
/// </summary>
public sealed class TCNetClock
{
    private readonly long _start = Stopwatch.GetTimestamp();

    /// <summary>A shared process-wide clock.</summary>
    public static TCNetClock Shared { get; } = new();

    /// <summary>Microseconds since the clock started.</summary>
    public long ElapsedMicroseconds => (long)((Stopwatch.GetTimestamp() - _start) * (1_000_000.0 / Stopwatch.Frequency));

    /// <summary>Current timestamp, 0–999999 µs.</summary>
    public uint Timestamp => (uint)(ElapsedMicroseconds % TCNetConstants.TimestampModulo);

    /// <summary>Uptime in seconds, 0–43199.</summary>
    public ushort Uptime => (ushort)(ElapsedMicroseconds / 1_000_000 % TCNetConstants.UptimeRolloverSeconds);

    /// <summary>Forward distance from <paramref name="from"/> to <paramref name="to"/> on the 1 s wheel (0–999999).</summary>
    public static uint Forward(uint from, uint to) =>
        (uint)(((long)to - from) % TCNetConstants.TimestampModulo + TCNetConstants.TimestampModulo) % TCNetConstants.TimestampModulo;

    /// <summary>Adds a signed number of µs on the 1 s wheel.</summary>
    public static uint Add(uint timestamp, long micros) =>
        (uint)(((timestamp + micros) % TCNetConstants.TimestampModulo + TCNetConstants.TimestampModulo) % TCNetConstants.TimestampModulo);

    /// <summary>Signed shortest difference <c>a − b</c> on the wheel, in −500000…499999 µs.</summary>
    public static int Difference(uint a, uint b)
    {
        long d = ((long)a - b) % TCNetConstants.TimestampModulo;
        if (d >= TCNetConstants.TimestampModulo / 2) d -= TCNetConstants.TimestampModulo;
        if (d < -(long)(TCNetConstants.TimestampModulo / 2)) d += TCNetConstants.TimestampModulo;
        return (int)d;
    }
}

/// <summary>One completed time sync exchange with a remote node.</summary>
/// <param name="DelayMicros">One-way delay estimate: (now − echoed timestamp) / 2.</param>
/// <param name="RemoteTimeAtReceive">Remote node's timer when the response arrived: remote timestamp + delay.</param>
/// <param name="OffsetMicros">Remote timer minus local timer, −500000…499999 µs.</param>
public readonly record struct TimeSyncSample(uint DelayMicros, uint RemoteTimeAtReceive, int OffsetMicros)
{
    public uint RoundTripMicros => DelayMicros * 2;
}

/// <summary>Time sync arithmetic from the "TCNet Time Sync Packet – Usage" section.</summary>
public static class TimeSync
{
    /// <summary>
    /// Step 3: Delay = (current timer − remote timestamp) / 2; time of remote node = timestamp + delay.
    /// </summary>
    /// <param name="response">The step 1 response (its Timestamp is the remote timer; RemoteTimestamp echoes ours).</param>
    /// <param name="localNow">Local timer when the response arrived.</param>
    public static TimeSyncSample Compute(TimeSyncPacket response, uint localNow) =>
        Compute(response.Timestamp, response.RemoteTimestamp, localNow);

    /// <inheritdoc cref="Compute(TimeSyncPacket, uint)"/>
    public static TimeSyncSample Compute(uint remoteTimestamp, uint echoedLocalTimestamp, uint localNow)
    {
        uint delay = TCNetClock.Forward(echoedLocalTimestamp, localNow) / 2;
        uint remoteNow = TCNetClock.Add(remoteTimestamp, delay);
        return new TimeSyncSample(delay, remoteNow, TCNetClock.Difference(remoteNow, localNow));
    }

    /// <summary>Optional refinement: time of remote node = timestamp + ((delay1 + delay2) / 2).</summary>
    public static TimeSyncSample Average(IReadOnlyList<TimeSyncSample> samples)
    {
        if (samples.Count == 0) throw new ArgumentException("No samples.", nameof(samples));
        uint delay = (uint)samples.Average(s => (double)s.DelayMicros);
        // Average offsets on the wheel relative to the first sample to avoid wrap artefacts.
        int first = samples[0].OffsetMicros;
        double avgRel = samples.Average(s => (double)TCNetClock.Difference((uint)(s.OffsetMicros + 1_000_000), (uint)(first + 1_000_000)));
        int offset = TCNetClock.Difference(TCNetClock.Add((uint)(first + 1_000_000), (long)avgRel), 0);
        return new TimeSyncSample(delay, samples[^1].RemoteTimeAtReceive, offset);
    }
}
