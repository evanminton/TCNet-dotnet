namespace TCNet.Text;

/// <summary>One enum value with its spec meaning.</summary>
public sealed record OptionInfo(long Value, string Name, string Description)
{
    public override string ToString() => $"{Value,5}  {Name,-24} {Description}";
}

/// <summary>A spec option table (enum) with every value described.</summary>
public sealed record OptionSet(string Name, string Location, Type EnumType, bool IsFlags, IReadOnlyList<OptionInfo> Options)
{
    public OptionInfo? Find(long value) => Options.FirstOrDefault(o => o.Value == value);
}

/// <summary>A registered application code (Application Specific Data identifier).</summary>
public sealed record ApplicationCodeInfo(ushort Code, string Vendor, string Url)
{
    public override string ToString() => $"{Code:X4}  {Vendor}  {Url}";
}

/// <summary>Human-readable names and descriptions for every option in the spec.</summary>
public static class TCNetText
{
    private static readonly Dictionary<Type, OptionSet> Sets = new();

    /// <summary>All option tables, in spec order.</summary>
    public static IReadOnlyList<OptionSet> OptionSets { get; }

    static TCNetText()
    {
        var list = new List<OptionSet>
        {
            Make("Message Type", "Header byte 7", false,
                (MessageType.OptIn, "Opt-IN: present and keep alive a node (broadcast 60000, every 1000 ms)"),
                (MessageType.OptOut, "Opt-OUT: node leaves the network (broadcast 60000 + unicast, once)"),
                (MessageType.Status, "Status: current settings and layer status (broadcast 60000 every 1000 ms, unicast to slaves)"),
                (MessageType.TimeSync, "Time Sync: measure delay/offset to a node (unicast, response required)"),
                (MessageType.ErrorNotification, "Error / Notification: request not handled, empty, or OK (unicast)"),
                (MessageType.Request, "Request: ask a master/repeater for data (unicast)"),
                (MessageType.ApplicationData, "Application Specific Data (broadcast 60001 or unicast)"),
                (MessageType.Control, "Control: remote control using control paths (unicast, response required)"),
                (MessageType.TextData, "Text Data (broadcast 60000 or unicast)"),
                (MessageType.KeyboardData, "Keyboard Data: realtime key presses (broadcast 60000 or unicast)"),
                (MessageType.Data, "Data: metrics, metadata, beat grid, cues, waveforms, mixer (unicast)"),
                (MessageType.DataFile, "Data File: low-res artwork JPEG (unicast)"),
                (MessageType.ApplicationSpecificData, "Application Specific Data, alternate type number (broadcast 60000 or unicast)"),
                (MessageType.Time, "Time: layer times, states and timecode (broadcast 60001 every 1–40 ms)")),
            Make("Node Type", "Header byte 17", false,
                (NodeType.Auto, "Auto (auto master candidate)"),
                (NodeType.Master, "Master: generates time packets and sends data"),
                (NodeType.Slave, "Slave: receives metadata and timing, may request data"),
                (NodeType.Repeater, "Repeater: receives and sends metadata and timing")),
            Make("Node Options", "Header bytes 18–19 (flags, summed)", true,
                (NodeOptions.NeedAuthentication, "NEED AUTHENTICATION: authentication needed for extended communication"),
                (NodeOptions.SupportsControlMessages, "SUPPORTS TCNCM: listens to TCNet Control Messages"),
                (NodeOptions.SupportsApplicationData, "SUPPORTS TCNASDP: listens to Application Specific Data packets"),
                (NodeOptions.DoNotDisturb, "DND: do not disturb / sleeping; node requests data itself")),
            Make("Layer", "Data byte 25, request layer", false,
                (TCNetLayer.None, "Not layer specific"),
                (TCNetLayer.Layer1, "Layer 1"), (TCNetLayer.Layer2, "Layer 2"), (TCNetLayer.Layer3, "Layer 3"), (TCNetLayer.Layer4, "Layer 4"),
                (TCNetLayer.LayerA, "Layer A"), (TCNetLayer.LayerB, "Layer B"),
                (TCNetLayer.LayerM, "Layer M – master out"), (TCNetLayer.LayerC, "Layer C (reserved in data packets)")),
            Make("Layer State", "Status 42–49, Metrics 27, Time 96–103", false,
                (LayerState.Idle, "IDLE"), (LayerState.Playing, "PLAYING"), (LayerState.Looping, "LOOPING"),
                (LayerState.Paused, "PAUSED"), (LayerState.Stopped, "STOPPED"), (LayerState.CueButtonDown, "CUE BUTTON DOWN"),
                (LayerState.PlatterDown, "PLATTER DOWN"), (LayerState.FastForward, "FFWD"), (LayerState.FastReverse, "FFRV"),
                (LayerState.Hold, "HOLD")),
            Make("Data Type", "Request/Data byte 24", false,
                (DataType.Metrics, "Metrics Data (122 bytes)"),
                (DataType.Metadata, "Metadata: artist, title, key, track ID (548 bytes)"),
                (DataType.BeatGrid, "Beat Grid Data (chunked, 2400 bytes per packet)"),
                (DataType.CueData, "Cue Data: loop and 18 hot/memory cues"),
                (DataType.SmallWaveform, "Small Wave Form: 1200 bars (2442 bytes)"),
                (DataType.BigWaveform, "Big Wave Form (chunked, 4800 bytes per packet)"),
                (DataType.LowResArtwork, "Low Res Artwork JPEG (type 204, chunked)"),
                (DataType.Mixer, "Mixer Data (270 bytes)")),
            Make("Notification Code", "Error/Notification bytes 26–27", false,
                (NotificationCode.RequestUnknown, "Request Unknown: an unknown request is made"),
                (NotificationCode.RequestNotPossible, "Request Not Possible/Featured: recognised but can't be handled"),
                (NotificationCode.RequestDataEmpty, "Request Data = EMPTY: nothing to send"),
                (NotificationCode.Ok, "Request Response: OK")),
            Make("Step", "Time Sync / Control / Text byte 24", false,
                (SyncStep.Initialize, "Initialize"), (SyncStep.Response, "Response")),
            Make("SMPTE Mode", "Status 83, Time 105 and per layer", false,
                (SmpteMode.UseGeneral, "0 – use the general SMPTE mode (byte 105)"),
                (SmpteMode.Fps24, "24 FPS"), (SmpteMode.Fps25, "25 FPS"), (SmpteMode.Fps29_97, "29.97 FPS"), (SmpteMode.Fps30, "30 FPS")),
            Make("Time Code State", "Time packet, per layer", false,
                (TimecodeState.Stopped, "Stopped"), (TimecodeState.Running, "Running"), (TimecodeState.ForceResync, "Force Re-sync")),
            Make("Auto Master Mode", "Status byte 84", false,
                (AutoMasterMode.Disabled, "Disabled"), (AutoMasterMode.HtpMaster, "HTP Master"), (AutoMasterMode.LinkMaster, "Link Master")),
            Make("Beat Type", "Beat grid entry byte 2", false,
                (BeatType.Unknown, "Unknown / empty"), (BeatType.UpBeat, "Up Beat"), (BeatType.DownBeat, "Down Beat")),
            Make("Mixer Type", "Mixer byte 26", false,
                (MixerType.Standard, "Standard (use if unsure)"), (MixerType.Extended, "Extended")),
            Make("Mixer Channel Select", "Mixer 92 (Send Return 3 source), 102 (BeatFX channel)", false,
                (MixerChannelSelect.Channel1, "CH1"), (MixerChannelSelect.Channel2, "CH2"), (MixerChannelSelect.Channel3, "CH3"),
                (MixerChannelSelect.Channel4, "CH4"), (MixerChannelSelect.Channel5, "CH5"), (MixerChannelSelect.Channel6, "CH6"),
                (MixerChannelSelect.Mic, "MIC"), (MixerChannelSelect.Master, "MASTER"), (MixerChannelSelect.CrossfaderA, "CRF A"),
                (MixerChannelSelect.CrossfaderB, "CRF B"), (MixerChannelSelect.None, "NONE")),
            Make("Send Return Type", "Mixer byte 93", false,
                (SendReturnType.UsbAux, "USB-AUX"), (SendReturnType.UsbInsert, "USB-INSERT"),
                (SendReturnType.JackAux, "1/4\" TS JACK-AUX"), (SendReturnType.JackInsert, "1/4\" TS JACK-INSERT"), (SendReturnType.None, "NONE")),
            Make("Channel Source", "Mixer channel +0", false,
                (ChannelSource.UsbA, "USB A"), (ChannelSource.UsbB, "USB B"), (ChannelSource.Digital, "DIGITAL"), (ChannelSource.Line, "LINE"),
                (ChannelSource.Phono, "PHONO"), (ChannelSource.Internal, "INT"), (ChannelSource.Return1, "RTN1"), (ChannelSource.Return2, "RTN2"),
                (ChannelSource.Return3, "RTN3"), (ChannelSource.ReturnAll, "RTN ALL")),
            Make("Crossfader Assign", "Mixer channel +13", false,
                (CrossfaderAssign.Thru, "THRU"), (CrossfaderAssign.A, "A"), (CrossfaderAssign.B, "B")),
            Make("Cue Table Layout", "Cue Data packet (library option)", false,
                (CueTableLayout.Specification, "Cue 1 at byte 47 as printed (overlaps Loop OUT)"),
                (CueTableLayout.AfterLoop, "Cue 1 at byte 50, after Loop OUT")),
        };
        OptionSets = list;
        foreach (var s in list) Sets[s.EnumType] = s;
    }

    private static OptionSet Make<T>(string name, string location, bool flags, params (T Value, string Description)[] items)
        where T : struct, Enum =>
        new(name, location, typeof(T), flags,
            items.Select(i => new OptionInfo(Convert.ToInt64(i.Value), i.Value.ToString(), i.Description)).ToArray());

    /// <summary>The option table for an enum type.</summary>
    public static OptionSet? For<T>() where T : struct, Enum => Sets.GetValueOrDefault(typeof(T));

    /// <summary>All values of an option table.</summary>
    public static IReadOnlyList<OptionInfo> Options<T>() where T : struct, Enum => For<T>()?.Options ?? [];

    /// <summary>The spec meaning of a value, e.g. LayerState.Playing → "PLAYING". Unknown values say so.</summary>
    public static string Describe<T>(T value) where T : struct, Enum
    {
        var set = For<T>();
        long v = Convert.ToInt64(value);
        if (set is null) return value.ToString();
        if (set.IsFlags) return DescribeFlags(value);
        return set.Find(v)?.Description ?? $"Unknown value {v}";
    }

    /// <summary>Describes a flag set: "SUPPORTS TCNCM + DND", or "None".</summary>
    public static string DescribeFlags<T>(T value) where T : struct, Enum
    {
        long v = Convert.ToInt64(value);
        if (v == 0) return "None";
        var set = For<T>();
        if (set is null) return value.ToString();
        var parts = new List<string>();
        long known = 0;
        foreach (var o in set.Options)
        {
            if (o.Value != 0 && (v & o.Value) == o.Value)
            {
                parts.Add(o.Description.Split(':')[0]);
                known |= o.Value;
            }
        }
        if ((v & ~known) != 0) parts.Add($"unknown 0x{v & ~known:X}");
        return string.Join(" + ", parts);
    }

    /// <summary>Wire-order layer labels: 1, 2, 3, 4, A, B, M, C.</summary>
    public static readonly string[] LayerLabels = ["1", "2", "3", "4", "A", "B", "M", "C"];

    /// <summary>Label for a 0-based wire index.</summary>
    public static string LayerLabel(int index) => index is >= 0 and < 8 ? LayerLabels[index] : index.ToString();

    /// <summary>Label for a 1-based layer number (0 → "–").</summary>
    public static string LayerName(byte layer) => layer switch
    {
        0 => "–",
        >= 1 and <= 8 => LayerLabels[layer - 1],
        _ => layer.ToString(),
    };

    /// <summary>Short state label for compact displays.</summary>
    public static string Short(LayerState s) => s switch
    {
        LayerState.Idle => "IDLE",
        LayerState.Playing => "PLAY",
        LayerState.Looping => "LOOP",
        LayerState.Paused => "PAUSE",
        LayerState.Stopped => "STOP",
        LayerState.CueButtonDown => "CUE",
        LayerState.PlatterDown => "PLATTER",
        LayerState.FastForward => "FFWD",
        LayerState.FastReverse => "FFRV",
        LayerState.Hold => "HOLD",
        _ => ((byte)s).ToString(),
    };

    /// <summary>Registered application codes (spec page 36).</summary>
    public static IReadOnlyList<ApplicationCodeInfo> ApplicationCodes { get; } =
    [
        new(0x0000, "Reserved for Public", ""),
        new(0x0AA0, "Pioneer DJ", "http://www.pioneerdj.com"),
        new(0x0AAA, "TC Supply / ShowKontrol", "http://www.showkontrol.com"),
        new(0x0AAB, "TC Supply Pyrotechnic Systems", "http://www.tc-supply.com"),
        new(0x0AAC, "TC Supply Ride Control Systems", "http://www.tc-supply.com"),
        new(0x0AB0, "Avolites Lighting", "http://www.avolites.com"),
        new(0x0AB1, "MA Lighting", "http://www.malighting.com"),
        new(0x0AB3, "Chamsys Lighting", "http://www.chamsys.co.uk"),
        new(0x0AB4, "Obsidian Control", "http://www.obsidiancontrol.com"),
        new(0x0ABA, "Arkaos Software", "http://www.arkaos.net"),
        new(0x0ABB, "BLCKBOOK / Time Code Sync", "http://www.timecodesync.com"),
        new(0x0ABC, "Resolume Software", "http://www.resolume.com"),
        new(0x0ABD, "Green Hippo", "http://www.green-hippo.com"),
        new(0x0ABE, "RD/ShowCockpit", "http://www.showcockpit.com"),
        new(0x0ABF, "Disguise", "http://disguise.one"),
        new(0x0ACA, "OrangePI", "http://orangepi.dmx.org"),
        new(0x0ACB, "RedPill VR", "http://www.redpillvr.com"),
        new(0xFFFF, "Reserved for Public", ""),
    ];

    /// <summary>Vendor for an application code, or null.</summary>
    public static string? ApplicationVendor(ushort code) => ApplicationCodes.FirstOrDefault(a => a.Code == code)?.Vendor;
}
