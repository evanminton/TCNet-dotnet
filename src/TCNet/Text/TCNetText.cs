namespace TCNet.Text;

/// <summary>An enum value with its spec meaning.</summary>
public sealed record OptionInfo(long Value, string Name, string Description)
{
    public override string ToString() => $"{Value} {Name} – {Description}";
}

/// <summary>A spec option table.</summary>
public sealed record OptionSet(string Name, string Where, Type EnumType, bool IsFlags, IReadOnlyList<OptionInfo> Options)
{
    public OptionInfo? Find(long value) => Options.FirstOrDefault(o => o.Value == value);
}

/// <summary>A registered application code (spec: "Registered Application Codes").</summary>
public sealed record ApplicationCode(ushort Code, string Vendor, string Url)
{
    public override string ToString() => $"{Code:X4} {Vendor} {Url}".TrimEnd();
}

/// <summary>Plain-English meaning of every option value in the spec.</summary>
public static class TCNetText
{
    private static readonly Dictionary<Type, OptionSet> ByType = new();

    public static IReadOnlyList<OptionSet> OptionSets { get; }

    static TCNetText()
    {
        OptionSets =
        [
            Set("Message Type", "header byte 7", false,
                (MessageType.OptIn, "Opt-IN – announce and keep a node alive (broadcast 60000 + unicast, every 1000 ms)"),
                (MessageType.OptOut, "Opt-OUT – node leaves the network (broadcast 60000 + unicast, once)"),
                (MessageType.Status, "Status – node settings and layer states (broadcast 60000 every 1000 ms, unicast to slaves)"),
                (MessageType.TimeSync, "Time Sync – measure delay and clock offset (unicast, response required)"),
                (MessageType.ErrorNotification, "Error / Notification – request unknown, not possible, empty, or OK (unicast)"),
                (MessageType.Request, "Request – ask a master/repeater for a data type on a layer (unicast)"),
                (MessageType.ApplicationData, "Application Specific Data (broadcast 60001 or unicast)"),
                (MessageType.Control, "Control – remote control with control paths (unicast, response required)"),
                (MessageType.TextData, "Text Data (broadcast 60000 or unicast)"),
                (MessageType.KeyboardData, "Keyboard Data – realtime key presses (broadcast 60000 or unicast)"),
                (MessageType.Data, "Data – metrics, metadata, beat grid, cues, waveforms, mixer (unicast)"),
                (MessageType.DataFile, "Data File – low-res artwork JPEG (unicast)"),
                (MessageType.ApplicationSpecificData, "Application Specific Data, overview type number (broadcast 60000 or unicast)"),
                (MessageType.Time, "Time – layer times, states and timecode (broadcast 60001 every 1–40 ms)")),
            Set("Node Type", "header byte 17", false,
                (NodeType.Auto, "Auto – candidate to become master"),
                (NodeType.Master, "Master – sends time code and data"),
                (NodeType.Slave, "Slave – receives metadata and timing, may request data"),
                (NodeType.Repeater, "Repeater – receives and sends metadata and timing")),
            Set("Node Options", "header bytes 18–19 (flags, summed)", true,
                (NodeOptions.NeedAuthentication, "NEED AUTHENTICATION – authentication needed for extended communication"),
                (NodeOptions.SupportsControl, "SUPPORTS TCNCM – listens to TCNet Control Messages"),
                (NodeOptions.SupportsApplicationData, "SUPPORTS TCNASDP – listens to Application Specific Data packets"),
                (NodeOptions.DoNotDisturb, "DND – do not disturb / sleeping; requests data itself when needed")),
            Set("Layer", "data byte 25, request layer", false,
                (Layer.None, "Not layer specific"),
                (Layer.L1, "Layer 1"), (Layer.L2, "Layer 2"), (Layer.L3, "Layer 3"), (Layer.L4, "Layer 4"),
                (Layer.A, "Layer A"), (Layer.B, "Layer B"), (Layer.M, "Layer M – master out"), (Layer.C, "Layer C (reserved in data packets)")),
            Set("Layer State", "Status 42–49, Metrics 27, Time 96–103", false,
                (LayerState.Idle, "IDLE"), (LayerState.Playing, "PLAYING"), (LayerState.Looping, "LOOPING"),
                (LayerState.Paused, "PAUSED"), (LayerState.Stopped, "STOPPED"), (LayerState.CueDown, "CUE BUTTON DOWN"),
                (LayerState.PlatterDown, "PLATTER DOWN"), (LayerState.FastForward, "FFWD"), (LayerState.FastReverse, "FFRV"),
                (LayerState.Hold, "HOLD")),
            Set("Data Type", "Request / Data byte 24", false,
                (DataType.Metrics, "Metrics – state, position, speed, BPM, beat (122 bytes)"),
                (DataType.Metadata, "Metadata – artist, title, key, track ID (548 bytes)"),
                (DataType.BeatGrid, "Beat Grid – beat numbers, types and times (chunked, 2400 bytes/packet)"),
                (DataType.CueData, "Cue Data – loop and 18 hot/memory cues"),
                (DataType.SmallWaveform, "Small Wave Form – 1200 bars (2442 bytes)"),
                (DataType.BigWaveform, "Big Wave Form – detailed bars (chunked, 4800 bytes/packet)"),
                (DataType.LowResArtwork, "Low Res Artwork – JPEG (type 204, chunked)"),
                (DataType.Mixer, "Mixer Data – master, FX, headphones, booth, 6 channels (270 bytes)")),
            Set("Notification Code", "Error / Notification bytes 26–27", false,
                (NotificationCode.RequestUnknown, "Request Unknown – an unknown request is made"),
                (NotificationCode.RequestNotPossible, "Request Not Possible/Featured – recognised but can't be handled"),
                (NotificationCode.RequestDataEmpty, "Request Data = EMPTY – nothing to send"),
                (NotificationCode.Ok, "Request Response: OK")),
            Set("Step", "Time Sync / Control / Text byte 24", false,
                (Step.Initialize, "Initialize"), (Step.Response, "Response")),
            Set("SMPTE Mode", "Status 83, Time 105 and per layer", false,
                (SmpteMode.General, "0 – use the general SMPTE mode (Time byte 105)"),
                (SmpteMode.Fps24, "24 FPS"), (SmpteMode.Fps25, "25 FPS"), (SmpteMode.Fps2997, "29.97 FPS"), (SmpteMode.Fps30, "30 FPS")),
            Set("Time Code State", "Time packet per layer", false,
                (TimecodeState.Stopped, "Stopped"), (TimecodeState.Running, "Running"), (TimecodeState.ForceResync, "Force Re-sync")),
            Set("Auto Master Mode", "Status byte 84", false,
                (AutoMasterMode.Disabled, "Disabled"), (AutoMasterMode.HtpMaster, "HTP Master"), (AutoMasterMode.LinkMaster, "Link Master")),
            Set("Beat Type", "beat grid entry byte 2", false,
                (BeatType.None, "None / empty"), (BeatType.UpBeat, "Up Beat"), (BeatType.DownBeat, "Down Beat")),
            Set("Mixer Type", "Mixer byte 26", false,
                (MixerType.Standard, "Standard (use when unsure)"), (MixerType.Extended, "Extended")),
            Set("Mixer Channel Select", "Mixer 92 (Send Return 3 source), 102 (BeatFX channel)", false,
                (MixerChannelSelect.Ch1, "CH1"), (MixerChannelSelect.Ch2, "CH2"), (MixerChannelSelect.Ch3, "CH3"),
                (MixerChannelSelect.Ch4, "CH4"), (MixerChannelSelect.Ch5, "CH5"), (MixerChannelSelect.Ch6, "CH6"),
                (MixerChannelSelect.Mic, "MIC"), (MixerChannelSelect.Master, "MASTER"), (MixerChannelSelect.CrossfaderA, "CRF A"),
                (MixerChannelSelect.CrossfaderB, "CRF B"), (MixerChannelSelect.None, "NONE")),
            Set("Send Return Type", "Mixer byte 93", false,
                (SendReturnType.UsbAux, "USB-AUX"), (SendReturnType.UsbInsert, "USB-INSERT"),
                (SendReturnType.JackAux, "1/4\" TS JACK-AUX"), (SendReturnType.JackInsert, "1/4\" TS JACK-INSERT"), (SendReturnType.None, "NONE")),
            Set("Channel Source", "Mixer channel byte +0", false,
                (ChannelSource.UsbA, "USB A"), (ChannelSource.UsbB, "USB B"), (ChannelSource.Digital, "DIGITAL"), (ChannelSource.Line, "LINE"),
                (ChannelSource.Phono, "PHONO"), (ChannelSource.Internal, "INT"), (ChannelSource.Return1, "RTN1"), (ChannelSource.Return2, "RTN2"),
                (ChannelSource.Return3, "RTN3"), (ChannelSource.ReturnAll, "RTN ALL")),
            Set("Crossfader Assign", "Mixer channel byte +13", false,
                (CrossfaderAssign.Thru, "THRU"), (CrossfaderAssign.A, "A"), (CrossfaderAssign.B, "B")),
            Set("Cue Layout", "Cue Data table position (library option)", false,
                (CueLayout.Printed, "Cue 1 at byte 47 as printed (shares bytes 47–49 with Loop OUT)"),
                (CueLayout.AfterLoop, "Cue 1 at byte 50, after Loop OUT")),
        ];
        foreach (var s in OptionSets) ByType[s.EnumType] = s;
    }

    private static OptionSet Set<T>(string name, string where, bool flags, params (T Value, string Text)[] items) where T : struct, Enum =>
        new(name, where, typeof(T), flags, items.Select(i => new OptionInfo(Convert.ToInt64(i.Value), i.Value.ToString(), i.Text)).ToArray());

    public static OptionSet? For<T>() where T : struct, Enum => ByType.GetValueOrDefault(typeof(T));

    public static IReadOnlyList<OptionInfo> Options<T>() where T : struct, Enum => For<T>()?.Options ?? [];

    /// <summary>Spec meaning of a value; unknown values say so.</summary>
    public static string Describe<T>(T value) where T : struct, Enum
    {
        var set = For<T>();
        if (set is null) return value.ToString();
        if (set.IsFlags) return DescribeFlags(value);
        long v = Convert.ToInt64(value);
        return set.Find(v)?.Description ?? $"Unknown ({v})";
    }

    /// <summary>"SUPPORTS TCNCM + DND", "None", or unknown bits in hex.</summary>
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
            if (o.Value == 0 || (v & o.Value) != o.Value) continue;
            parts.Add(o.Description.Split(" – ")[0]);
            known |= o.Value;
        }
        if ((v & ~known) != 0) parts.Add($"unknown 0x{v & ~known:X}");
        return string.Join(" + ", parts);
    }

    /// <summary>Layer labels in wire order.</summary>
    public static IReadOnlyList<string> LayerLabels { get; } = ["1", "2", "3", "4", "A", "B", "M", "C"];

    /// <summary>Label for a 0-based wire index.</summary>
    public static string LayerLabel(int index) => index is >= 0 and < 8 ? LayerLabels[index] : index.ToString();

    /// <summary>Label for a 1-based layer number ("–" for 0).</summary>
    public static string LayerName(byte layer) => layer == 0 ? "–" : LayerLabel(layer - 1);

    public static string Short(LayerState s) => s switch
    {
        LayerState.Idle => "IDLE",
        LayerState.Playing => "PLAY",
        LayerState.Looping => "LOOP",
        LayerState.Paused => "PAUSE",
        LayerState.Stopped => "STOP",
        LayerState.CueDown => "CUE",
        LayerState.PlatterDown => "PLATTER",
        LayerState.FastForward => "FFWD",
        LayerState.FastReverse => "FFRV",
        LayerState.Hold => "HOLD",
        _ => ((byte)s).ToString(),
    };

    public static IReadOnlyList<ApplicationCode> ApplicationCodes { get; } =
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

    public static string? ApplicationVendor(ushort code) => ApplicationCodes.FirstOrDefault(a => a.Code == code)?.Vendor;
}
