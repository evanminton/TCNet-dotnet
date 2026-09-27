namespace TCNet;

/// <summary>Header byte 7.</summary>
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

/// <summary>Header byte 17.</summary>
public enum NodeType : byte
{
    Auto = 1,
    Master = 2,
    Slave = 4,
    Repeater = 8,
}

/// <summary>Header bytes 18–19, summed flags.</summary>
[Flags]
public enum NodeOptions : ushort
{
    None = 0,
    NeedAuthentication = 1,
    SupportsControl = 2,
    SupportsApplicationData = 4,
    DoNotDisturb = 8,
}

/// <summary>Layer numbers 1–8 (1, 2, 3, 4, A, B, M, C).</summary>
public enum Layer : byte
{
    None = 0,
    L1 = 1,
    L2 = 2,
    L3 = 3,
    L4 = 4,
    A = 5,
    B = 6,
    M = 7,
    C = 8,
}

public enum LayerState : byte
{
    Idle = 0,
    Playing = 3,
    Looping = 4,
    Paused = 5,
    Stopped = 6,
    CueDown = 7,
    PlatterDown = 8,
    FastForward = 9,
    FastReverse = 10,
    Hold = 11,
}

/// <summary>Byte 24 of Request, Data (200) and Data File (204) packets.</summary>
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

public enum NotificationCode : ushort
{
    RequestUnknown = 1,
    RequestNotPossible = 13,
    RequestDataEmpty = 14,
    Ok = 255,
}

public enum Step : byte
{
    Initialize = 0,
    Response = 1,
}

public enum SmpteMode : byte
{
    /// <summary>0 in a layer field: use the general mode.</summary>
    General = 0,
    Fps24 = 24,
    Fps25 = 25,
    Fps2997 = 29,
    Fps30 = 30,
}

public enum TimecodeState : byte
{
    Stopped = 0,
    Running = 1,
    ForceResync = 2,
}

public enum AutoMasterMode : byte
{
    Disabled = 0,
    HtpMaster = 1,
    LinkMaster = 2,
}

public enum BeatType : byte
{
    None = 0,
    UpBeat = 10,
    DownBeat = 20,
}

public enum MixerType : byte
{
    Standard = 0,
    Extended = 2,
}

/// <summary>Send Return 3 source and BeatFX channel select.</summary>
public enum MixerChannelSelect : byte
{
    Ch1 = 0,
    Ch2 = 1,
    Ch3 = 2,
    Ch4 = 3,
    Ch5 = 4,
    Ch6 = 5,
    Mic = 6,
    Master = 7,
    CrossfaderA = 8,
    CrossfaderB = 9,
    None = 255,
}

public enum SendReturnType : byte
{
    UsbAux = 0,
    UsbInsert = 1,
    JackAux = 2,
    JackInsert = 3,
    None = 255,
}

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

public enum CrossfaderAssign : byte
{
    Thru = 0,
    A = 1,
    B = 2,
}

/// <summary>Cue Data table position; the spec prints cue 1 at 47, overlapping Loop OUT (46–49).</summary>
public enum CueLayout
{
    /// <summary>Printed offsets (cue 1 at 47). Empty cue 1 is not written so Loop OUT survives.</summary>
    Printed,
    /// <summary>Cue 1 at 50, directly after Loop OUT, same 22-byte stride.</summary>
    AfterLoop,
}
