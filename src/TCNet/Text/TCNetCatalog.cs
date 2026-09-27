using System.Text;

namespace TCNet.Text;

/// <summary>Reference entry for one packet type.</summary>
public sealed record PacketInfo(string Name, MessageType MessageType, DataType? DataType, string Transport, string Port, string Size, string Behavior, string Purpose)
{
    public string Key => DataType is { } d ? $"{(byte)MessageType}/{(byte)d}" : ((byte)MessageType).ToString();

    public TCNetPacket Create() => TCNetParser.Create(MessageType, DataType ?? 0);

    public IReadOnlyList<TCNetField> Layout => Create().Describe();
}

public sealed record SpecNote(string Topic, string Note);

/// <summary>The whole spec as browsable, searchable reference data.</summary>
public static class TCNetCatalog
{
    public static IReadOnlyList<PacketInfo> Packets { get; } =
    [
        new("Opt-IN", MessageType.OptIn, null, "Broadcast + unicast", "60000 + node port", "68", "Every 1000 ms", "Present and keep a node alive in the network."),
        new("Opt-OUT", MessageType.OptOut, null, "Broadcast + unicast", "60000 + node port", "28", "Once when leaving", "Tell other nodes this node leaves."),
        new("Status", MessageType.Status, null, "Broadcast, unicast to slaves", "60000", "300", "Every 1000 ms", "Current settings: layer sources, states, track IDs, names."),
        new("Time Sync", MessageType.TimeSync, null, "Unicast", "Node port", "32", "Response required", "Measure delay and the remote node's clock."),
        new("Error / Notification", MessageType.ErrorNotification, null, "Unicast", "Node port", "30", "When a request isn't handled, or OK", "Answer to a request or control message."),
        new("Request", MessageType.Request, null, "Unicast", "Node port", "26", "To a master or repeater", "Request a data type for a layer."),
        new("Application Specific Data (30)", MessageType.ApplicationData, null, "Broadcast / unicast", "60001 or node port", "42 + data", "Per application", "Non-public data between applications."),
        new("Control", MessageType.Control, null, "Unicast", "Node port", "42 + data", "Response required", "Remote control with control paths such as layer/1/state=6;."),
        new("Text Data", MessageType.TextData, null, "Broadcast / unicast", "60000 or node port", "42 + data", "Response required", "Free text."),
        new("Keyboard Data", MessageType.KeyboardData, null, "Broadcast / unicast", "60000 or node port", "44", "Response required", "Realtime key presses."),
        new("Data · Metrics", MessageType.Data, TCNet.DataType.Metrics, "Unicast", "Node port", "122", "When the cache changes / on request", "Layer state, position, speed, BPM, beat, pitch, track ID."),
        new("Data · Metadata", MessageType.Data, TCNet.DataType.Metadata, "Unicast", "Node port", "548", "On update / on request", "Artist, title, key, track ID."),
        new("Data · Beat Grid", MessageType.Data, TCNet.DataType.BeatGrid, "Unicast", "Node port", "≤ 2442 per packet", "On request", "Beat numbers, down/up beats and times."),
        new("Data · Cue Data", MessageType.Data, TCNet.DataType.CueData, "Unicast", "Node port", "436 stated (443 by table)", "On request", "Loop in/out and 18 hot/memory cues with colours."),
        new("Data · Small Waveform", MessageType.Data, TCNet.DataType.SmallWaveform, "Unicast", "Node port", "2442", "On request", "1200-bar overview waveform."),
        new("Data · Big Waveform", MessageType.Data, TCNet.DataType.BigWaveform, "Unicast", "Node port", "Depends on track", "On request", "Detailed waveform over several packets."),
        new("Data · Mixer", MessageType.Data, TCNet.DataType.Mixer, "Unicast to all slaves", "Node port", "270", "When the cache changes", "Mixer master, isolator, filter, FX, headphones, booth, 6 channels."),
        new("Data File · Low Res Artwork", MessageType.DataFile, TCNet.DataType.LowResArtwork, "Unicast", "Node port", "Depends on file", "On request", "JPEG artwork over several packets."),
        new("Application Specific Data (213)", MessageType.ApplicationSpecificData, null, "Broadcast / unicast", "60000 or node port", "42 + data", "Per application", "Type number used in the overview list."),
        new("Time", MessageType.Time, null, "Broadcast + unicast to local nodes", "60001", "162", "Every 1–40 ms or on time-critical events", "Times, totals, beat markers, states, timecode and on-air for all 8 layers."),
    ];

    public static IReadOnlyList<OptionSet> OptionSets => TCNetText.OptionSets;

    public static IReadOnlyList<SpecNote> Notes { get; } =
    [
        new("Protocol version", "Tables show minor 6 (Opt-IN, Status) and 1 elsewhere. This library sends 3.5 and accepts any version."),
        new("Metadata text", "Up to 3.4.9: UTF-8. From 3.5.0: UTF-16LE. Both in 256-byte fields (128 UTF-16 characters; the printed '64' is a UTF-32 leftover). Chosen from the sender's header version."),
        new("Metadata size", "Fields end at 547; the stated size is 548 and 548 is sent."),
        new("Cue Data", "Loop OUT is 46–49 but cue 1 is printed at 47. Printed layout: an empty cue 1 is not written, so Loop OUT survives. When reading, bytes 47–49 are cue 1 when cue 1 has other data, or when bytes 46 and 48 are 0 with a type at 47. CueLayout.AfterLoop puts cue 1 at 50."),
        new("Time packet", "LC Time is printed at 48 and LC Beat Marker at 94; the sequence gives 52 and 95, which are used."),
        new("Mixer size", "Fields end at 269; 270 bytes are sent."),
        new("Chunk numbering", "OFFSET = beat × 8 − packet × 2400 implies 0-based packet numbers, which are sent. Reassembly accepts 0- or 1-based numbering in any order."),
        new("Cluster size", "Details say 32000; the tables say 2400 (beat grid) and 4800 (big waveform, artwork). The table values are used."),
        new("Application data", "Type 30 on the detail page (broadcast 60001) and 213 in the overview (broadcast 60000); both are parsed. The library listens on 60000–60002."),
        new("Speed", "Metrics speed is printed as 0–20000, but the details say 32768 = 100 %. Values are shown as a ratio of 32768."),
        new("Request answers", "The library answers unknown or unhandled requests with 'Request Not Possible', and requests the handler has no data for with 'Request Data = EMPTY'."),
        new("Master election", "When the master leaves, the Auto node with the highest uptime becomes master (uptimes within 2 s tie and go to the lower Node ID; after 3 rounds the lowest Node ID wins). An elected master steps back when it meets a configured master or one with a lower Node ID."),
        new("Uptime", "Rolls over every 12 hours (0–43199 s)."),
    ];

    public static IEnumerable<string> Search(string text)
    {
        bool Has(string? s) => s is not null && s.Contains(text, StringComparison.OrdinalIgnoreCase);
        foreach (var p in Packets)
        {
            if (Has(p.Name) || Has(p.Purpose)) yield return $"Packet {p.Key}: {p.Name} – {p.Purpose}";
            foreach (var f in p.Layout)
                if (Has(f.Name)) yield return $"Field: {p.Name} byte {f.Offset} ({f.Size}) {f.Name}";
        }
        foreach (var s in OptionSets)
            foreach (var o in s.Options)
                if (Has(o.Name) || Has(o.Description)) yield return $"{s.Name}: {o.Value} = {o.Description}";
        foreach (var a in TCNetText.ApplicationCodes)
            if (Has(a.Vendor)) yield return $"Application code {a}";
        foreach (var n in Notes)
            if (Has(n.Topic) || Has(n.Note)) yield return $"Note – {n.Topic}: {n.Note}";
    }

    public static string ToMarkdown()
    {
        var sb = new StringBuilder("# TCNet V3.5.1B reference\n\n## Packets\n\n| Type | Name | Transport | Port | Size | Behavior |\n|---|---|---|---|---|---|\n");
        foreach (var p in Packets) sb.AppendLine($"| {p.Key} | {p.Name} | {p.Transport} | {p.Port} | {p.Size} | {p.Behavior} |");
        foreach (var p in Packets)
        {
            sb.AppendLine().AppendLine($"### {p.Name} ({p.Key})").AppendLine().AppendLine(p.Purpose).AppendLine();
            sb.AppendLine("| Byte | Size | Field |").AppendLine("|---|---|---|");
            foreach (var f in p.Layout) sb.AppendLine($"| {f.Offset} | {f.Size} | {f.Name} |");
        }
        sb.AppendLine().AppendLine("## Options");
        foreach (var s in OptionSets)
        {
            sb.AppendLine().AppendLine($"### {s.Name}").AppendLine().AppendLine($"_{s.Where}{(s.IsFlags ? ", flags" : "")}_").AppendLine();
            sb.AppendLine("| Value | Name | Meaning |").AppendLine("|---|---|---|");
            foreach (var o in s.Options) sb.AppendLine($"| {o.Value} | {o.Name} | {o.Description} |");
        }
        sb.AppendLine().AppendLine("## Registered application codes").AppendLine().AppendLine("| Code | Vendor | URL |").AppendLine("|---|---|---|");
        foreach (var a in TCNetText.ApplicationCodes) sb.AppendLine($"| {a.Code:X4} | {a.Vendor} | {a.Url} |");
        sb.AppendLine().AppendLine("## Spec notes").AppendLine();
        foreach (var n in Notes) sb.AppendLine($"- **{n.Topic}**: {n.Note}");
        return sb.ToString();
    }
}
