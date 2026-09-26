namespace TCNet;

/// <summary>Fixed values from the TCNet Link Specification V3.5.1B.</summary>
public static class TCNetConstants
{
    // ---- Ports (spec: "NETWORK PORTS" / "NETWORK PARTICIPATION") ----

    /// <summary>Broadcast port for Opt-IN / Opt-OUT / Status and broadcast text/keyboard/app data.</summary>
    public const int BroadcastPort = 60000;

    /// <summary>Broadcast port for TCNet Time Packets (and broadcast Application Specific Data, type 30).</summary>
    public const int TimePort = 60001;

    /// <summary>Third broadcast listener port named in "Network participation – second step".</summary>
    public const int ApplicationPort = 60002;

    /// <summary>Lowest unicast listener port.</summary>
    public const int UnicastPortMin = 65023;

    /// <summary>Highest unicast listener port.</summary>
    public const int UnicastPortMax = 65535;

    /// <summary>Default unicast listener port.</summary>
    public const int DefaultUnicastPort = 65023;

    // ---- Protocol ----

    /// <summary>Protocol version implemented by this library (3.5).</summary>
    public const byte ProtocolVersionMajor = 3;

    /// <inheritdoc cref="ProtocolVersionMajor"/>
    public const byte ProtocolVersionMinor = 5;

    /// <summary>The three header bytes at offset 4: "TCN".</summary>
    public static ReadOnlySpan<byte> HeaderMagic => "TCN"u8;

    /// <summary>Management header size (bytes 0–23) shared by every packet.</summary>
    public const int ManagementHeaderSize = 24;

    /// <summary>Length of the Node Name field.</summary>
    public const int NodeNameLength = 8;

    /// <summary>Number of layers carried by Status / Time packets (1, 2, 3, 4, A, B, M, C).</summary>
    public const int LayerCount = 8;

    // ---- Packet sizes (the "Size" row of each packet table) ----

    public const int OptInSize = 68;
    public const int OptOutSize = 28;
    public const int StatusSize = 300;
    public const int TimeSyncSize = 32;
    public const int ErrorNotificationSize = 30;
    public const int RequestSize = 26;
    /// <summary>Control / Text / Keyboard / chunked data header size; payload starts at byte 42.</summary>
    public const int PayloadHeaderSize = 42;
    public const int KeyboardDataSize = 44;
    public const int MetricsDataSize = 122;
    public const int MetadataSize = 548;
    public const int SmallWaveformSize = 2442;
    public const int MixerDataSize = 270;
    public const int TimeSize = 162;

    /// <summary>Small waveform payload: 1200 bars × (level, color).</summary>
    public const int SmallWaveformDataSize = 2400;

    /// <summary>Beat grid: max data bytes per packet (spec: "maximum of 2400 bytes of Data").</summary>
    public const int BeatGridClusterSize = 2400;

    /// <summary>Big waveform / artwork: standard data cluster size.</summary>
    public const int BigWaveformClusterSize = 4800;

    /// <inheritdoc cref="BigWaveformClusterSize"/>
    public const int ArtworkClusterSize = 4800;

    /// <summary>Beat grid entry size (beat number u16, type u8, reserved u8, timestamp u32).</summary>
    public const int BeatGridEntrySize = 8;

    /// <summary>Metadata artist/title field size in bytes.</summary>
    public const int MetadataTextSize = 256;

    /// <summary>Application Specific Data "Packet Signature" constant (178260640 = 0x0AA00AA0).</summary>
    public const uint ApplicationDataSignature = 178260640;

    // ---- Timing ----

    /// <summary>Timestamps run 0–999999 µs and wrap each second.</summary>
    public const uint TimestampModulo = 1_000_000;

    /// <summary>Opt-IN uptime rolls over every 12 hours (0–43199 s).</summary>
    public const int UptimeRolloverSeconds = 43_200;

    /// <summary>Opt-IN / Status broadcast interval.</summary>
    public static readonly TimeSpan OptInInterval = TimeSpan.FromMilliseconds(1000);

    /// <summary>Max value of Metrics track length / position in ms (0x5265C00 = 24 h).</summary>
    public const uint MaxMetricsTimeMs = 0x5265C00;

    /// <summary>Max value of Time packet layer times in ms (0x55D4A80 = 25 h).</summary>
    public const uint MaxLayerTimeMs = 0x55D4A80;

    /// <summary>Speed / pitch bend value representing 100 % (spec: 32768 = 100 %).</summary>
    public const int SpeedUnity = 32768;

    /// <summary>First protocol version whose metadata strings are UTF-16 (3.5.0).</summary>
    public static readonly Version Utf16MetadataVersion = new(3, 5);
}
