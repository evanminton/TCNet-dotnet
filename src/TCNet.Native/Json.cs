using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using TCNet.Networking;
using TCNet.Text;

namespace TCNet.Native;

/// <summary>
/// Everything the C API returns as JSON, written by hand with <see cref="Utf8JsonWriter"/> (no reflection, AOT safe).
/// Every value comes with its plain-English meaning next to it ("…Text" properties).
/// </summary>
internal static class Json
{
    /// <summary>Pretty-print (tcnet_set_json_indented).</summary>
    public static bool Indented { get; set; }

    /// <summary>UTF-8 JSON bytes, NUL-terminated, so they can be handed to C as they are.</summary>
    public static byte[] Utf8(Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>(1024);
        using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = Indented, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            body(w);
        var bytes = new byte[buffer.WrittenCount + 1];
        buffer.WrittenSpan.CopyTo(bytes);
        return bytes;
    }

    public static string String(Action<Utf8JsonWriter> body)
    {
        var bytes = Utf8(body);
        return Encoding.UTF8.GetString(bytes, 0, bytes.Length - 1);
    }

    // ─────────────── packets ───────────────

    public static void Field(Utf8JsonWriter w, TCNetField f)
    {
        w.WriteStartObject();
        w.WriteNumber("offset", f.Offset);
        w.WriteNumber("size", f.Size);
        w.WriteString("name", f.Name);
        w.WriteString("value", f.Value);
        if (f.Meaning is not null) w.WriteString("meaning", f.Meaning);
        w.WriteEndObject();
    }

    public static void Packet(Utf8JsonWriter w, TCNetPacket p, bool fields = true)
    {
        w.WriteStartObject();
        w.WriteString("name", p.Name);
        w.WriteNumber("messageType", (byte)p.MessageType);
        w.WriteString("messageTypeText", TCNetText.Describe(p.MessageType));
        if (p is DataPacket dp)
        {
            w.WriteNumber("dataType", (byte)dp.DataType);
            w.WriteString("dataTypeText", TCNetText.Describe(dp.DataType));
            w.WriteNumber("layer", dp.LayerId);
            w.WriteString("layerName", TCNetText.LayerName(dp.LayerId));
        }
        w.WriteNumber("length", p.Length);
        w.WriteNumber("receivedLength", p.ReceivedLength);
        w.WriteBoolean("padded", p.WasPadded);
        w.WriteNumber("nodeId", p.NodeId);
        w.WriteString("nodeName", p.NodeName);
        w.WriteNumber("nodeType", (byte)p.NodeType);
        w.WriteString("nodeTypeText", TCNetText.Describe(p.NodeType));
        w.WriteNumber("nodeOptions", (ushort)p.NodeOptions);
        w.WriteString("nodeOptionsText", TCNetText.DescribeFlags(p.NodeOptions));
        w.WriteString("protocol", $"{p.VersionMajor}.{p.VersionMinor}");
        w.WriteNumber("sequence", p.Sequence);
        w.WriteNumber("timestamp", p.Timestamp);
        w.WriteString("summary", p.Summary);
        w.WriteString("text", p.ToString());
        if (p is TimePacket t)
        {
            w.WritePropertyName("time");
            Time(w, t);
        }
        if (fields)
        {
            w.WriteStartArray("fields");
            foreach (var f in p.Describe()) Field(w, f);
            w.WriteEndArray();
        }
        w.WriteEndObject();
    }

    public static void Time(Utf8JsonWriter w, TimePacket t)
    {
        w.WriteStartObject();
        w.WriteNumber("smpteMode", (byte)t.SmpteMode);
        w.WriteString("smpteModeText", TCNetText.Describe(t.SmpteMode));
        w.WriteStartArray("layers");
        foreach (var l in t.Layers)
        {
            var mode = t.ModeOf(l);
            w.WriteStartObject();
            w.WriteNumber("index", l.Index);
            w.WriteString("label", l.Label);
            w.WriteNumber("timeMs", l.TimeMs);
            w.WriteString("timeText", TCNetUnits.Ms(l.TimeMs));
            w.WriteNumber("totalMs", l.TotalMs);
            w.WriteString("totalText", TCNetUnits.Ms(l.TotalMs));
            w.WriteNumber("remainingMs", l.RemainingMs);
            w.WriteNumber("beatMarker", l.BeatMarker);
            w.WriteNumber("state", (byte)l.State);
            w.WriteString("stateText", TCNetText.Describe(l.State));
            w.WriteNumber("smpteMode", (byte)l.SmpteMode);
            w.WriteString("effectiveSmpteModeText", TCNetText.Describe(mode));
            w.WriteNumber("timecodeState", (byte)l.TimecodeState);
            w.WriteString("timecodeStateText", TCNetText.Describe(l.TimecodeState));
            w.WriteString("timecode", l.Timecode.ToString());
            w.WriteNumber("onAir", l.OnAir);
            w.WriteBoolean("isOnAir", l.IsOnAir);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    public static void Data(Utf8JsonWriter w, AssembledData d)
    {
        w.WriteStartObject();
        w.WriteNumber("messageType", (byte)d.MessageType);
        w.WriteNumber("dataType", (byte)d.DataType);
        w.WriteString("dataTypeText", TCNetText.Describe(d.DataType));
        w.WriteNumber("layer", d.Layer);
        w.WriteString("layerName", TCNetText.LayerName(d.Layer));
        w.WriteNumber("bytes", d.Data.Length);
        w.WriteString("bytesText", TCNetUnits.Bytes(d.Data.Length));
        w.WriteNumber("packets", d.Packets);
        w.WriteNumber("nodeId", d.NodeId);
        w.WriteString("nodeName", d.NodeName);
        if (d.Source is not null) w.WriteString("source", d.Source.ToString());
        w.WriteNumber("applicationCode", d.ApplicationCode);
        if (TCNetText.ApplicationVendor(d.ApplicationCode) is { } vendor) w.WriteString("applicationVendor", vendor);
        w.WriteBoolean("isJpeg", d.IsJpeg);
        switch (d.DataType)
        {
            case DataType.BeatGrid:
                w.WriteStartArray("beats");
                foreach (var b in d.ToBeatGrid().Beats)
                {
                    w.WriteStartObject();
                    w.WriteNumber("number", b.Number);
                    w.WriteNumber("type", (byte)b.Type);
                    w.WriteString("typeText", TCNetText.Describe(b.Type));
                    w.WriteNumber("timeMs", b.TimeMs);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                break;
            case DataType.SmallWaveform or DataType.BigWaveform:
                var wf = d.ToWaveform();
                w.WriteStartObject("waveform");
                w.WriteNumber("bars", wf.Bars.Count);
                w.WriteNumber("peak", wf.Peak);
                w.WriteStartArray("levels");
                foreach (var bar in wf.Bars) w.WriteNumberValue(bar.Level);
                w.WriteEndArray();
                w.WriteStartArray("colors");
                foreach (var bar in wf.Bars) w.WriteNumberValue(bar.Color);
                w.WriteEndArray();
                w.WriteEndObject();
                break;
        }
        w.WriteBase64String("base64", d.Data);
        w.WriteEndObject();
    }

    public static void Sync(Utf8JsonWriter w, TimeSyncResult s)
    {
        w.WriteStartObject();
        w.WriteNumber("delayUs", s.DelayMicros);
        w.WriteNumber("roundTripUs", s.RoundTripMicros);
        w.WriteNumber("remoteNow", s.RemoteNow);
        w.WriteNumber("offsetUs", s.OffsetMicros);
        w.WriteString("text", $"delay {TCNetUnits.Micros(s.DelayMicros)}, offset {TCNetUnits.Micros(s.OffsetMicros)}");
        w.WriteEndObject();
    }

    public static void Request(Utf8JsonWriter w, RequestResult r)
    {
        w.WriteStartObject();
        w.WriteBoolean("success", r.Success);
        w.WriteBoolean("timedOut", r.TimedOut);
        w.WriteNumber("dataType", (byte)r.DataType);
        w.WriteString("dataTypeText", TCNetText.Describe(r.DataType));
        w.WriteNumber("layer", r.Layer);
        w.WriteString("text", r.ToString());
        if (r.Packet is not null)
        {
            w.WritePropertyName("packet");
            Packet(w, r.Packet);
        }
        if (r.Data is not null)
        {
            w.WritePropertyName("data");
            Data(w, r.Data);
        }
        if (r.Notification is { } n)
        {
            w.WriteStartObject("notification");
            w.WriteNumber("code", (ushort)n.Code);
            w.WriteString("codeText", TCNetText.Describe(n.Code));
            w.WriteString("summary", n.Summary);
            w.WriteEndObject();
        }
        w.WriteEndObject();
    }

    // ─────────────── nodes ───────────────

    public static void Node(Utf8JsonWriter w, RemoteNode n)
    {
        w.WriteStartObject();
        w.WriteString("key", n.Key);
        w.WriteString("address", n.Address.ToString());
        w.WriteNumber("nodeId", n.NodeId);
        w.WriteString("nodeName", n.NodeName);
        w.WriteNumber("nodeType", (byte)n.NodeType);
        w.WriteString("nodeTypeText", TCNetText.Describe(n.NodeType));
        w.WriteNumber("nodeOptions", (ushort)n.NodeOptions);
        w.WriteString("nodeOptionsText", TCNetText.DescribeFlags(n.NodeOptions));
        w.WriteString("protocol", n.ProtocolVersion.ToString());
        w.WriteNumber("listenerPort", n.ListenerPort);
        w.WriteBoolean("isLocal", n.IsLocal);
        w.WriteBoolean("isMasterOrRepeater", n.IsMasterOrRepeater);
        w.WriteString("vendorName", n.VendorName);
        w.WriteString("deviceName", n.DeviceName);
        w.WriteString("deviceVersion", n.DeviceVersion);
        w.WriteNumber("nodeCount", n.NodeCount);
        w.WriteNumber("uptime", n.Uptime);
        w.WriteString("uptimeText", TCNetUnits.Duration(TimeSpan.FromSeconds((double)n.Uptime)));
        w.WriteNumber("packets", n.Packets);
        w.WriteString("firstSeen", n.FirstSeen);
        w.WriteString("lastSeen", n.LastSeen);
        if (n.MasterSince is { } since) w.WriteString("masterSince", since);
        if (n.Sync is { } sync)
        {
            w.WritePropertyName("sync");
            Sync(w, sync);
        }
        if (n.Status is not null) w.WriteString("status", n.Status.Summary);
        if (n.Mixer is not null) w.WriteString("mixer", n.Mixer.Summary);
        w.WriteString("text", n.ToString());

        w.WriteStartArray("layers");
        for (int i = 0; i < 8; i++)
        {
            var time = n.Time?.Layers[i];
            var metrics = n.Metrics[i];
            var meta = n.Metadata[i];
            w.WriteStartObject();
            w.WriteNumber("index", i);
            w.WriteString("label", TCNetText.LayerLabel(i));
            if (time is not null)
            {
                w.WriteNumber("timeMs", time.TimeMs);
                w.WriteString("timeText", TCNetUnits.Ms(time.TimeMs));
                w.WriteNumber("totalMs", time.TotalMs);
                w.WriteNumber("state", (byte)time.State);
                w.WriteString("stateText", TCNetText.Describe(time.State));
                w.WriteString("timecode", time.Timecode.ToString());
                w.WriteBoolean("isOnAir", time.IsOnAir);
            }
            if (metrics is not null)
            {
                w.WriteNumber("bpm", metrics.Bpm);
                w.WriteNumber("positionMs", metrics.PositionMs);
                w.WriteNumber("trackLengthMs", metrics.TrackLengthMs);
                w.WriteNumber("trackId", metrics.TrackId);
                w.WriteString("metrics", metrics.Summary);
            }
            if (meta is not null)
            {
                w.WriteString("artist", meta.Artist);
                w.WriteString("title", meta.Title);
                w.WriteNumber("key", meta.Key);
                w.WriteNumber("metadataTrackId", meta.TrackId);
            }
            w.WriteBoolean("hasCues", n.Cues[i] is not null);
            w.WriteBoolean("hasBeatGrid", n.BeatGrids[i] is not null);
            w.WriteBoolean("hasArtwork", n.Artwork[i] is not null);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    public static void Settings(Utf8JsonWriter w, NodeSettings s)
    {
        w.WriteStartObject();
        w.WriteNumber("nodeId", s.NodeId);
        w.WriteString("nodeName", s.NodeName);
        w.WriteNumber("nodeType", (byte)s.NodeType);
        w.WriteString("nodeTypeText", TCNetText.Describe(s.NodeType));
        w.WriteNumber("nodeOptions", (ushort)s.NodeOptions);
        w.WriteString("nodeOptionsText", TCNetText.DescribeFlags(s.NodeOptions));
        w.WriteString("vendorName", s.VendorName);
        w.WriteString("deviceName", s.DeviceName);
        w.WriteNumber("deviceMajor", s.DeviceMajor);
        w.WriteNumber("deviceMinor", s.DeviceMinor);
        w.WriteNumber("deviceBug", s.DeviceBug);
        w.WriteNumber("versionMajor", s.VersionMajor);
        w.WriteNumber("versionMinor", s.VersionMinor);
        w.WriteNumber("listenerPort", s.ListenerPort);
        w.WriteString("localAddress", s.LocalAddress.ToString());
        if (s.BroadcastAddress is null) w.WriteNull("broadcastAddress");
        else w.WriteString("broadcastAddress", s.BroadcastAddress.ToString());
        w.WriteBoolean("listenOnBroadcastPorts", s.ListenOnBroadcastPorts);
        w.WriteNumber("optInIntervalMs", s.OptInInterval.TotalMilliseconds);
        w.WriteNumber("nodeTimeoutMs", s.NodeTimeout.TotalMilliseconds);
        w.WriteNumber("maxNodes", s.MaxNodes);
        w.WriteBoolean("unicastOptIn", s.UnicastOptIn);
        if (s.SendStatus is { } send) w.WriteBoolean("sendStatus", send);
        else w.WriteNull("sendStatus");
        w.WriteBoolean("answerTimeSync", s.AnswerTimeSync);
        w.WriteBoolean("autoTimeSync", s.AutoTimeSync);
        w.WriteNumber("timeSyncIntervalMs", s.TimeSyncInterval.TotalMilliseconds);
        w.WriteBoolean("autoRequestMetadata", s.AutoRequestMetadata);
        w.WriteBoolean("autoRequestMetrics", s.AutoRequestMetrics);
        w.WriteBoolean("autoMasterElection", s.AutoMasterElection);
        w.WriteBoolean("receiveOwnPackets", s.ReceiveOwnPackets);
        w.WriteNumber("requestTimeoutMs", s.RequestTimeout == Timeout.InfiniteTimeSpan ? -1 : s.RequestTimeout.TotalMilliseconds);
        w.WriteEndObject();
    }

    // ─────────────── reference ───────────────

    public static void Catalog(Utf8JsonWriter w)
    {
        w.WriteStartObject();
        w.WriteStartArray("packets");
        foreach (var p in TCNetCatalog.Packets)
        {
            w.WriteStartObject();
            w.WriteString("key", p.Key);
            w.WriteString("name", p.Name);
            w.WriteNumber("messageType", (byte)p.MessageType);
            if (p.DataType is { } dt) w.WriteNumber("dataType", (byte)dt);
            w.WriteString("transport", p.Transport);
            w.WriteString("port", p.Port);
            w.WriteString("size", p.Size);
            w.WriteString("behavior", p.Behavior);
            w.WriteString("purpose", p.Purpose);
            w.WriteStartArray("layout");
            foreach (var f in p.Layout) Field(w, f);
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartArray("optionSets");
        foreach (var s in TCNetCatalog.OptionSets)
        {
            w.WriteStartObject();
            w.WriteString("name", s.Name);
            w.WriteString("where", s.Where);
            w.WriteString("enum", s.EnumType.Name);
            w.WriteBoolean("isFlags", s.IsFlags);
            w.WriteStartArray("options");
            foreach (var o in s.Options)
            {
                w.WriteStartObject();
                w.WriteNumber("value", o.Value);
                w.WriteString("name", o.Name);
                w.WriteString("description", o.Description);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartArray("applicationCodes");
        foreach (var a in TCNetText.ApplicationCodes)
        {
            w.WriteStartObject();
            w.WriteNumber("code", a.Code);
            w.WriteString("hex", a.Code.ToString("X4"));
            w.WriteString("vendor", a.Vendor);
            w.WriteString("url", a.Url);
            w.WriteEndObject();
        }
        w.WriteEndArray();

        w.WriteStartArray("notes");
        foreach (var n in TCNetCatalog.Notes)
        {
            w.WriteStartObject();
            w.WriteString("topic", n.Topic);
            w.WriteString("note", n.Note);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }

    public static void Interfaces(Utf8JsonWriter w)
    {
        w.WriteStartArray();
        foreach (var i in TCNetNetwork.Interfaces())
        {
            w.WriteStartObject();
            w.WriteString("name", i.Name);
            w.WriteString("address", i.Address.ToString());
            w.WriteString("mask", i.Mask.ToString());
            w.WriteNumber("prefixLength", i.PrefixLength);
            w.WriteString("broadcast", i.Broadcast.ToString());
            w.WriteString("text", i.ToString());
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }
}
