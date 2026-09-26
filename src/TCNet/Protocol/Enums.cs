namespace TCNet;

/// <summary>Byte 7 of the management header.</summary>
public enum MessageType : byte
{
    OptIn = 2,
    OptOut = 3,
    Status = 5,
    TimeSync = 10,
    ErrorNotification = 13,
    Request = 20,
    ApplicationData = 30,
    Control = 101,
    TextData = 128,
    KeyboardData = 132,
    Data = 200,
    DataFile = 204,
    ApplicationSpecificData = 213,
    Time = 254,
}

/// <summary>Node role (byte 17). The spec lists these as the values 1, 2, 4, 8.</summary>
public enum NodeType : byte
{
    Auto = 1,
    Master = 2,
    Slave = 4,
    Repeater = 8,
}

/// <summary>Node Options flags (bytes 18–19). Flags are summed.</summary>
[Flags]
public enum NodeOptions : ushort
{
    None = 0,
    NeedAuthentication = 1,
    SupportsControlMessages = 2,
    SupportsApplicationData = 4,
    DoNotDisturb = 8,
}

/// <summary>Layer numbers used by data packets and requests (1-based).</summary>
public enum TCNetLayer : byte
{
    None = 0,
    Layer1 = 1,
    Layer2 = 2,
    Layer3 = 3,
    Layer4 = 4,
    LayerA = 5,
    LayerB = 6,
    /// <summary>Master out (M).</summary>
    LayerM = 7,
    /// <summary>Layer C (listed as RESERVED in data packet tables).</summary>
    LayerC = 8,
}

/// <summary>Play head status of a layer.</summary>
public enum LayerState : byte
{
    Idle = 0,
    Playing = 3,
    Looping = 4,
    Paused = 5,
    Stopped = 6,
    CueButtonDown = 7,
    PlatterDown = 8,
    FastForward = 9,
    FastReverse = 10,
    Hold = 11,
}

/// <summary>Data Type byte (24) of Request, Data (200) and Data File (204) packets.</summary>
public enum DataType : byte
{
    Metrics = 2,
    Metadata = 4,
    BeatGrid = 8,
    CueData = 12,
    SmallWaveform = 16,
    BigWaveform = 32,
    LowResArtwork = 128,
    Mixer = 150,
}

/// <summary>Error / Notification code (bytes 26–27).</summary>
public enum NotificationCode : ushort
{
    RequestUnknown = 1,
    RequestNotPossible = 13,
    RequestDataEmpty = 14,
    Ok = 255,
}

/// <summary>Step byte of Time Sync / Control / Text packets.</summary>
public enum SyncStep : byte
{
    Initialize = 0,
    Response = 1,
}

/// <summary>SMPTE frame rate as carried in Status and Time packets.</summary>
public enum SmpteMode : byte
{
    /// <summary>Per-layer value 0: use the general SMPTE mode (byte 105).</summary>
    UseGeneral = 0,
    Fps24 = 24,
    Fps25 = 25,
    /// <summary>29.97 fps.</summary>
    Fps29_97 = 29,
    Fps30 = 30,
}

/// <summary>Time Code State per layer in the Time packet.</summary>
public enum TimecodeState : byte
{
    Stopped = 0,
    Running = 1,
    ForceResync = 2,
}

/// <summary>Auto Master Mode (Status byte 84).</summary>
public enum AutoMasterMode : byte
{
    Disabled = 0,
    HtpMaster = 1,
    LinkMaster = 2,
}

/// <summary>Beat grid entry type.</summary>
public enum BeatType : byte
{
    Unknown = 0,
    UpBeat = 10,
    DownBeat = 20,
}

/// <summary>Mixer type (Mixer data byte 26).</summary>
public enum MixerType : byte
{
    Standard = 0,
    Extended = 2,
}

/// <summary>Send Return 3 source / BeatFX channel select.</summary>
public enum MixerChannelSelect : byte
{
    Channel1 = 0,
    Channel2 = 1,
    Channel3 = 2,
    Channel4 = 3,
    Channel5 = 4,
    Channel6 = 5,
    Mic = 6,
    Master = 7,
    CrossfaderA = 8,
    CrossfaderB = 9,
    None = 255,
}

/// <summary>Send Return 3 type.</summary>
public enum SendReturnType : byte
{
    UsbAux = 0,
    UsbInsert = 1,
    JackAux = 2,
    JackInsert = 3,
    None = 255,
}

/// <summary>Mixer channel input source.</summary>
public enum ChannelSource : byte
{
    UsbA = 0,
    UsbB = 1,
    Digital = 2,
    Line = 3,
    Phono = 4,
    Internal = 5,
    Return1 = 6,
    Return2 = 7,
    Return3 = 8,
    ReturnAll = 9,
}

/// <summary>Mixer channel crossfader assignment.</summary>
public enum CrossfaderAssign : byte
{
    Thru = 0,
    A = 1,
    B = 2,
}

/// <summary>Where the cue table starts inside a Cue Data packet (see README "spec notes").</summary>
public enum CueTableLayout
{
    /// <summary>Offsets exactly as printed: cue 1 type at byte 47 (overlaps Loop OUT bytes 47–49).</summary>
    Specification,
    /// <summary>Cue 1 type at byte 50, immediately after Loop OUT; same 22-byte stride.</summary>
    AfterLoop,
}
