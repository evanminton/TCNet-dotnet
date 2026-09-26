using System.Text;

namespace TCNet.Text;

/// <summary>Reference entry for one packet type.</summary>
public sealed record PacketInfo(
    string Name,
    MessageType MessageType,
    DataType? DataType,
    string Transport,
    string Port,
    string Size,
    string Behavior,
    string Functionality)
{
    /// <summary>A fresh instance of the packet (for field layouts and the packet builder).</summary>
    public TCNetPacket CreateSample() => TCNetPacketParser.Create(MessageType, DataType ?? 0);

    /// <summary>Field layout of the packet (offset, size, name).</summary>
    public IReadOnlyList<TCNetField> Layout => CreateSample().Describe();

    public string Key => DataType is { } d ? $"{(byte)MessageType}/{(byte)d}" : ((byte)MessageType).ToString();
}

/// <summary>A known inconsistency in the spec and how this library resolves it.</summary>
public sealed record SpecNote(string Topic, string Note);

/// <summary>Browsable reference of every packet, option table, application code and spec note.</summary>
public static class TCNetOptionCatalog
{
    public static IReadOnlyList<PacketInfo> Packets { get; } =
    [
        new("Opt-IN", MessageType.OptIn, null, "Broadcast + unicast", "60000 and node port", "68", "Every 1000 ms", "Present and keep alive a node in a TCNet network."),
        new("Opt-OUT", MessageType.OptOut, null, "Broadcast + unicast", "60000 and node port", "28", "Once when leaving", "Notifies other nodes that the node leaves the network."),
        new("Status", MessageType.Status, null, "Broadcast (+ unicast to slaves)", "60000", "300", "Every 1000 ms", "Status of current settings on the node: layer sources, states, track IDs and names."),
        new("Time Sync", MessageType.TimeSync, null, "Unicast", "Target node port", "32", "Response required", "Send and receive time sync data."),
        new("Error / Notification", MessageType.ErrorNotification, null, "Unicast", "Target node port", "30", "On unhandled/empty request, or OK", "Notifies that a request is not handled."),
        new("Request", MessageType.Request, null, "Unicast", "Target node port", "26", "To master/repeater", "Request data from another node."),
        new("Application Specific Data", MessageType.ApplicationData, null, "Broadcast / unicast", "60001 or node port", "42 + data", "Application dependent", "Application specific data between applications."),
        new("Control", MessageType.Control, null, "Unicast", "Target node port", "42 + data", "Response required", "Control nodes remotely with control paths."),
        new("Text Data", MessageType.TextData, null, "Broadcast / unicast", "60000 or node port", "42 + data", "Response required", "Send and receive text data."),
        new("Keyboard Data", MessageType.KeyboardData, null, "Broadcast / unicast", "60000 or node port", "44", "Response required", "Realtime keyboard data."),
        new("Data – Metrics", MessageType.Data, TCNet.DataType.Metrics, "Unicast", "Target node port", "122", "When cache changes / on request", "Metrics of a layer: state, position, speed, BPM, beat."),
        new("Data – Metadata", MessageType.Data, TCNet.DataType.Metadata, "Unicast", "Target node port", "548", "On update / on request", "Artist, title, key and track ID of a layer."),
        new("Data – Beat Grid", MessageType.Data, TCNet.DataType.BeatGrid, "Unicast", "Target node port", "≤ 2442 per packet", "On request", "Beat grid of a layer."),
        new("Data – Cue Data", MessageType.Data, TCNet.DataType.CueData, "Unicast", "Target node port", "436 (see notes)", "On request", "Loop and hot/memory cues of a layer."),
        new("Data – Small Waveform", MessageType.Data, TCNet.DataType.SmallWaveform, "Unicast", "Target node port", "2442", "On request", "1200-bar overview waveform."),
        new("Data – Big Waveform", MessageType.Data, TCNet.DataType.BigWaveform, "Unicast", "Target node port", "Depends on track", "On request", "Detailed waveform split over packets."),
        new("Data – Mixer", MessageType.Data, TCNet.DataType.Mixer, "Unicast to all slaves", "Target node port", "270", "When cache changes", "Mixer master, FX, headphones, booth and 6 channel strips."),
        new("Data File – Low Res Artwork", MessageType.DataFile, TCNet.DataType.LowResArtwork, "Unicast", "Target node port", "Depends on file", "On request", "JPEG artwork split over packets."),
        new("Application Specific Data (213)", MessageType.ApplicationSpecificData, null, "Broadcast / unicast", "60000 or node port", "42 + data", "Application dependent", "Alternate type number listed in the overview."),
        new("Time", MessageType.Time, null, "Broadcast + unicast to local nodes", "60001", "162", "Every 1–40 ms or on time critical events", "Constant stream of timing data of all layers."),
    ];

    public static IReadOnlyList<OptionSet> OptionSets => TCNetText.OptionSets;

    public static IReadOnlyList<SpecNote> SpecNotes { get; } =
    [
        new("Protocol version", "Opt-IN/Status tables show minor 6, other tables show 1; this library sends 3.5 (the document version) and accepts any."),
        new("Metadata text", "Protocol < 3.5 uses UTF-8, ≥ 3.5 uses UTF-16LE in 256-byte fields (128 UTF-16 code units). The printed '64 characters' is left over from UTF-32. Chosen by the sender's header version."),
        new("Metadata size", "Fields end at byte 547; the stated size is 548. 548 bytes are sent."),
        new("Cue Data offsets", "Loop OUT is 46–49 but cue 1 type is printed at 47, and the stated size (436) is shorter than the table (443). Default layout = printed offsets (an empty cue 1 is not written, so Loop OUT survives); CueTableLayout.AfterLoop moves the table to 50."),
        new("Time packet", "LC Time is printed at 48 (should be 52) and LC Beat Marker at 94 (should be 95); the sequential offsets are used."),
        new("Mixer size", "Fields end at byte 269; the stated size is 270. 270 bytes are sent."),
        new("Chunk numbering", "Beat grid OFFSET = beat × 8 − packet × 2400 implies 0-based packet numbers. This library sends 0-based and reassembles either base by sorting."),
        new("Cluster size", "Details say 'standard value = 32000' but the tables give 2400 (beat grid) and 4800 (big waveform, artwork); the table values are used."),
        new("Application data port", "Overview says broadcast on 60001 for type 30 and 60000 for type 213; the library listens on 60000, 60001 and 60002."),
        new("Speed", "Metrics speed range is printed as 0–20000 but details say 32768 = 100 %; values are shown as a ratio of 32768."),
        new("Uptime", "Must roll over every 12 hours (0–43199 s)."),
        new("Master election", "When a master disconnects, the Auto node with the highest uptime (then timestamp) becomes master (see TCNetMasterElection)."),
    ];

    /// <summary>Case-insensitive search across packet names, fields and option descriptions.</summary>
    public static IEnumerable<string> Search(string text)
    {
        foreach (var p in Packets)
        {
            if (Contains(p.Name, text) || Contains(p.Functionality, text)) yield return $"Packet: {p.Name} (type {p.Key})";
            foreach (var f in p.Layout)
                if (Contains(f.Name, text)) yield return $"Field: {p.Name} [{f.Offset}+{f.Size}] {f.Name}";
        }
        foreach (var s in OptionSets)
            foreach (var o in s.Options)
                if (Contains(o.Name, text) || Contains(o.Description, text)) yield return $"{s.Name}: {o.Value} {o.Name} – {o.Description}";
        foreach (var a in TCNetText.ApplicationCodes)
            if (Contains(a.Vendor, text)) yield return $"Application code: {a}";
    }

    private static bool Contains(string s, string t) => s.Contains(t, StringComparison.OrdinalIgnoreCase);

    /// <summary>The whole reference as Markdown.</summary>
    public static string ToMarkdown()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# TCNet V3.5.1B reference").AppendLine();
        sb.AppendLine("## Packets").AppendLine();
        sb.AppendLine("| Type | Name | Transport | Port | Size | Behavior |").AppendLine("|---|---|---|---|---|---|");
        foreach (var p in Packets) sb.AppendLine($"| {p.Key} | {p.Name} | {p.Transport} | {p.Port} | {p.Size} | {p.Behavior} |");
        foreach (var p in Packets)
        {
            sb.AppendLine().AppendLine($"### {p.Name} ({p.Key})").AppendLine().AppendLine(p.Functionality).AppendLine();
            sb.AppendLine("| Byte | Size | Field |").AppendLine("|---|---|---|");
            foreach (var f in p.Layout) sb.AppendLine($"| {f.Offset} | {f.Size} | {f.Name} |");
        }
        sb.AppendLine().AppendLine("## Options");
        foreach (var s in OptionSets)
        {
            sb.AppendLine().AppendLine($"### {s.Name}").AppendLine().AppendLine($"_{s.Location}{(s.IsFlags ? ", flags" : "")}_").AppendLine();
            sb.AppendLine("| Value | Name | Meaning |").AppendLine("|---|---|---|");
            foreach (var o in s.Options) sb.AppendLine($"| {o.Value} | {o.Name} | {o.Description} |");
        }
        sb.AppendLine().AppendLine("## Registered application codes").AppendLine().AppendLine("| Code | Vendor | URL |").AppendLine("|---|---|---|");
        foreach (var a in TCNetText.ApplicationCodes) sb.AppendLine($"| {a.Code:X4} | {a.Vendor} | {a.Url} |");
        sb.AppendLine().AppendLine("## Spec notes").AppendLine();
        foreach (var n in SpecNotes) sb.AppendLine($"- **{n.Topic}**: {n.Note}");
        return sb.ToString();
    }
}
