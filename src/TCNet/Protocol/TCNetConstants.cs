namespace TCNet;

/// <summary>Values fixed by the TCNet Link Specification V3.5.1B.</summary>
public static class TCNetConstants
{
    /// <summary>Broadcast port: Opt-IN, Opt-OUT, Status, broadcast Text/Keyboard and type 213 application data.</summary>
    public const int BroadcastPort = 60000;

    /// <summary>Broadcast port for Time packets and broadcast type 30 application data.</summary>
    public const int TimePort = 60001;

    /// <summary>Third broadcast listener port ("open a listener on port 60000, 60001, 60002").</summary>
    public const int ApplicationPort = 60002;

    /// <summary>Unicast listener port range 65023–65535 (default 65023).</summary>
    public const int UnicastPortMin = 65023, UnicastPortMax = 65535;

    public const byte ProtocolMajor = 3;
    public const byte ProtocolMinor = 5;

    /// <summary>"TCN" at bytes 4–6 of every packet.</summary>
    public static ReadOnlySpan<byte> Magic => "TCN"u8;

    /// <summary>Management header length (bytes 0–23).</summary>
    public const int HeaderLength = 24;

    /// <summary>Payload offset of Control, Text, Keyboard, chunked and application data packets.</summary>
    public const int PayloadOffset = 42;

    public const int NodeNameLength = 8;
    public const int LayerCount = 8;

    public const int OptInLength = 68;
    public const int OptOutLength = 28;
    public const int StatusLength = 300;
    public const int TimeSyncLength = 32;
    public const int ErrorNotificationLength = 30;
    public const int RequestLength = 26;
    public const int KeyboardLength = 44;
    public const int MetricsLength = 122;
    public const int MetadataLength = 548;
    public const int SmallWaveformLength = 2442;
    public const int MixerLength = 270;
    public const int TimeLength = 162;

    /// <summary>Small waveform data: 1200 bars × (level, colour).</summary>
    public const int SmallWaveformDataLength = 2400;

    /// <summary>Beat grid: at most 2400 data bytes per packet (300 entries of 8 bytes).</summary>
    public const int BeatGridCluster = 2400;

    /// <summary>Big waveform and artwork: standard cluster size.</summary>
    public const int FileCluster = 4800;

    public const int BeatGridEntryLength = 8;
    public const int MetadataTextLength = 256;

    /// <summary>Application data "Packet Signature": 178260640 (0x0AA00AA0).</summary>
    public const uint ApplicationSignature = 178260640;

    /// <summary>Header timestamps are microseconds 0–999999.</summary>
    public const uint MicrosPerSecond = 1_000_000;

    /// <summary>Opt-IN uptime rolls over every 12 hours.</summary>
    public const int UptimeRollover = 43_200;

    /// <summary>Speed / pitch value for 100 %.</summary>
    public const int SpeedUnity = 32768;

    public static readonly TimeSpan OptInInterval = TimeSpan.FromSeconds(1);
}
