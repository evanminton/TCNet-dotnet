using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace TCNet.Native;

/// <summary>
/// The C entry points declared in include/tcnet.h. Conventions:
/// strings in are UTF-8 (NULL allowed where the header says so); strings out are UTF-8, allocated here and freed with
/// tcnet_free; int results are 0 (or a length/code) on success and −1 on failure, with the reason in tcnet_last_error.
/// No exception ever crosses into C.
/// </summary>
internal static unsafe class Exports
{
    // ─────────────── memory and errors ───────────────

    [ThreadStatic] private static nint t_error;
    private static byte* s_version;
    private static readonly byte* s_empty = Alloc("");

    private static byte* Alloc(string s)
    {
        int n = Encoding.UTF8.GetByteCount(s);
        byte* p = (byte*)NativeMemory.Alloc((nuint)n + 1);
        Encoding.UTF8.GetBytes(s, new Span<byte>(p, n));
        p[n] = 0;
        return p;
    }

    private static byte* AllocOrNull(string? s) => s is null ? null : Alloc(s);

    private static string? Str(byte* p) => p == null ? null : Marshal.PtrToStringUTF8((nint)p);

    private static string Need(byte* p, string name) => Str(p) ?? throw new ArgumentNullException(name);

    private static ReadOnlySpan<byte> Bytes(byte* data, int length)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        if (data == null && length > 0) throw new ArgumentNullException(nameof(data));
        return new ReadOnlySpan<byte>(data, length);
    }

    private static void SetError(string message)
    {
        nint old = t_error;
        t_error = (nint)Alloc(message);
        if (old != 0) NativeMemory.Free((void*)old);
    }

    private static int Fail(Exception ex)
    {
        SetError(ex is AggregateException { InnerException: { } inner } ? Describe(inner) : Describe(ex));
        return -1;
    }

    private static byte* FailNull(Exception ex)
    {
        Fail(ex);
        return null;
    }

    private static string Describe(Exception ex) => $"{ex.GetType().Name}: {ex.Message}";

    /// <summary>Copies <paramref name="bytes"/> when the buffer is big enough; always returns the full length.</summary>
    private static int Copy(byte[] bytes, byte* buffer, int capacity)
    {
        if (buffer != null && capacity >= bytes.Length) bytes.CopyTo(new Span<byte>(buffer, bytes.Length));
        return bytes.Length;
    }

    private static NativeNode NodeOf(void* handle)
    {
        if (handle == null) throw new ArgumentNullException("node");
        return GCHandle.FromIntPtr((nint)handle).Target as NativeNode ?? throw new ObjectDisposedException("node");
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_free", CallConvs = [typeof(CallConvCdecl)])]
    public static void Free(void* p)
    {
        if (p != null) NativeMemory.Free(p);
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_last_error", CallConvs = [typeof(CallConvCdecl)])]
    public static byte* LastError() => t_error == 0 ? s_empty : (byte*)t_error;

    [UnmanagedCallersOnly(EntryPoint = "tcnet_version", CallConvs = [typeof(CallConvCdecl)])]
    public static byte* Version()
    {
        if (s_version == null) s_version = Alloc(Api.Version);
        return s_version;
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_sizeof_time", CallConvs = [typeof(CallConvCdecl)])]
    public static int SizeOfTime() => sizeof(TimeData);

    [UnmanagedCallersOnly(EntryPoint = "tcnet_set_json_indented", CallConvs = [typeof(CallConvCdecl)])]
    public static void SetJsonIndented(int on) => Json.Indented = on != 0;

    [UnmanagedCallersOnly(EntryPoint = "tcnet_set_strict", CallConvs = [typeof(CallConvCdecl)])]
    public static void SetStrict(int on) => TCNetParser.Strict = on != 0;

    // ─────────────── packets ───────────────

    [UnmanagedCallersOnly(EntryPoint = "tcnet_is_tcnet", CallConvs = [typeof(CallConvCdecl)])]
    public static int IsTCNet(byte* data, int length)
    {
        try { return TCNetParser.IsTCNet(Bytes(data, length)) ? 1 : 0; }
        catch (Exception ex) { return Fail(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_parse_json", CallConvs = [typeof(CallConvCdecl)])]
    public static byte* ParseJson(byte* data, int length)
    {
        try { return Alloc(Api.ParseJson(Bytes(data, length))); }
        catch (Exception ex) { return FailNull(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_parse_text", CallConvs = [typeof(CallConvCdecl)])]
    public static byte* ParseText(byte* data, int length)
    {
        try { return Alloc(Api.ParseText(Bytes(data, length))); }
        catch (Exception ex) { return FailNull(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_hexdump", CallConvs = [typeof(CallConvCdecl)])]
    public static byte* HexDump(byte* data, int length)
    {
        try { return Alloc(Wire.HexDump(Bytes(data, length))); }
        catch (Exception ex) { return FailNull(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_packet_template", CallConvs = [typeof(CallConvCdecl)])]
    public static int PacketTemplate(int messageType, int dataType, byte* buffer, int capacity)
    {
        try { return Copy(Api.Template(messageType, dataType), buffer, capacity); }
        catch (Exception ex) { return Fail(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_time_encode", CallConvs = [typeof(CallConvCdecl)])]
    public static int TimeEncode(TimeData* time, byte* buffer, int capacity)
    {
        try
        {
            if (time == null) throw new ArgumentNullException("time");
            return Copy(Api.EncodeTime(*time), buffer, capacity);
        }
        catch (Exception ex) { return Fail(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_time_decode", CallConvs = [typeof(CallConvCdecl)])]
    public static int TimeDecode(byte* data, int length, TimeData* result)
    {
        try
        {
            if (result == null) throw new ArgumentNullException("out");
            *result = Api.DecodeTime(Bytes(data, length));
            return 0;
        }
        catch (Exception ex) { return Fail(ex); }
    }

    // ─────────────── timecode ───────────────

    [UnmanagedCallersOnly(EntryPoint = "tcnet_timecode_from_ms", CallConvs = [typeof(CallConvCdecl)])]
    public static int TimecodeFromMs(uint ms, int smpteMode, byte* hhmmssff)
    {
        try
        {
            if (hhmmssff == null) throw new ArgumentNullException("out");
            var tc = Timecode.FromMilliseconds(ms, (SmpteMode)checked((byte)smpteMode));
            hhmmssff[0] = tc.Hours;
            hhmmssff[1] = tc.Minutes;
            hhmmssff[2] = tc.Seconds;
            hhmmssff[3] = tc.Frames;
            return 0;
        }
        catch (Exception ex) { return Fail(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_timecode_to_ms", CallConvs = [typeof(CallConvCdecl)])]
    public static uint TimecodeToMs(byte* hhmmssff, int smpteMode)
    {
        try
        {
            if (hhmmssff == null) throw new ArgumentNullException("timecode");
            return new Timecode(hhmmssff[0], hhmmssff[1], hhmmssff[2], hhmmssff[3]).ToMilliseconds((SmpteMode)checked((byte)smpteMode));
        }
        catch (Exception ex)
        {
            Fail(ex);
            return uint.MaxValue; // TCNET_TIMECODE_ERROR: never a valid time (timecodes stay below 24 h)
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_frame_rate", CallConvs = [typeof(CallConvCdecl)])]
    public static double FrameRate(int smpteMode) => Timecode.FrameRate((SmpteMode)(byte)smpteMode);

    // ─────────────── reference ───────────────

    [UnmanagedCallersOnly(EntryPoint = "tcnet_reference_markdown", CallConvs = [typeof(CallConvCdecl)])]
    public static byte* ReferenceMarkdown()
    {
        try { return Alloc(TCNet.Text.TCNetCatalog.ToMarkdown()); }
        catch (Exception ex) { return FailNull(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_catalog_json", CallConvs = [typeof(CallConvCdecl)])]
    public static byte* CatalogJson()
    {
        try { return Alloc(Api.CatalogJson()); }
        catch (Exception ex) { return FailNull(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_search_json", CallConvs = [typeof(CallConvCdecl)])]
    public static byte* SearchJson(byte* text)
    {
        try { return Alloc(Api.SearchJson(Need(text, "text"))); }
        catch (Exception ex) { return FailNull(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_describe", CallConvs = [typeof(CallConvCdecl)])]
    public static byte* DescribeValue(byte* optionSet, long value)
    {
        try { return Alloc(Api.Describe(Need(optionSet, "option_set"), value)); }
        catch (Exception ex) { return FailNull(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_interfaces_json", CallConvs = [typeof(CallConvCdecl)])]
    public static byte* InterfacesJson()
    {
        try { return Alloc(Api.InterfacesJson()); }
        catch (Exception ex) { return FailNull(ex); }
    }

    // ─────────────── node ───────────────

    [UnmanagedCallersOnly(EntryPoint = "tcnet_node_create", CallConvs = [typeof(CallConvCdecl)])]
    public static void* NodeCreate(byte* settingsJson)
    {
        try
        {
            var node = new NativeNode(Api.ParseSettings(Str(settingsJson)));
            return (void*)GCHandle.ToIntPtr(GCHandle.Alloc(node));
        }
        catch (Exception ex)
        {
            Fail(ex);
            return null;
        }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_node_destroy", CallConvs = [typeof(CallConvCdecl)])]
    public static void NodeDestroy(void* handle)
    {
        if (handle == null) return;
        var h = GCHandle.FromIntPtr((nint)handle);
        try
        {
            if (h.Target is NativeNode n)
            {
                n.SetCallback(0, 0, 0);
                n.Stop();
            }
        }
        catch (Exception ex) { Fail(ex); }
        finally { h.Free(); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_node_start", CallConvs = [typeof(CallConvCdecl)])]
    public static int NodeStart(void* handle)
    {
        try { NodeOf(handle).Start(); return 0; }
        catch (Exception ex) { return Fail(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_node_stop", CallConvs = [typeof(CallConvCdecl)])]
    public static int NodeStop(void* handle)
    {
        try { NodeOf(handle).Stop(); return 0; }
        catch (Exception ex) { return Fail(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_node_info_json", CallConvs = [typeof(CallConvCdecl)])]
    public static byte* NodeInfoJson(void* handle)
    {
        try { return Alloc(NodeOf(handle).InfoJson()); }
        catch (Exception ex) { return FailNull(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_node_set_type", CallConvs = [typeof(CallConvCdecl)])]
    public static int NodeSetType(void* handle, int nodeType)
    {
        try { NodeOf(handle).Node.SetNodeType((NodeType)checked((byte)nodeType)); return 0; }
        catch (Exception ex) { return Fail(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_node_set_callback", CallConvs = [typeof(CallConvCdecl)])]
    public static int NodeSetCallback(void* handle, int mask, nint callback, void* user)
    {
        try { NodeOf(handle).SetCallback(mask, callback, (nint)user); return 0; }
        catch (Exception ex) { return Fail(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_node_nodes_json", CallConvs = [typeof(CallConvCdecl)])]
    public static byte* NodeNodesJson(void* handle)
    {
        try { return Alloc(NodeOf(handle).NodesJson()); }
        catch (Exception ex) { return FailNull(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_node_find_json", CallConvs = [typeof(CallConvCdecl)])]
    public static byte* NodeFindJson(void* handle, byte* query)
    {
        try
        {
            var q = Need(query, "query");
            var json = NodeOf(handle).FindJson(q);
            if (json is null) SetError($"No node \"{q}\".");
            return AllocOrNull(json);
        }
        catch (Exception ex) { return FailNull(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_node_get_time", CallConvs = [typeof(CallConvCdecl)])]
    public static int NodeGetTime(void* handle, byte* query, TimeData* result)
    {
        try
        {
            if (result == null) throw new ArgumentNullException("out");
            var t = NodeOf(handle).LatestTime(Str(query));
            if (t is null) return 1;
            *result = TimeData.FromPacket(t);
            return 0;
        }
        catch (Exception ex) { return Fail(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_node_time_json", CallConvs = [typeof(CallConvCdecl)])]
    public static byte* NodeTimeJson(void* handle, byte* query)
    {
        try
        {
            var t = NodeOf(handle).LatestTime(Str(query));
            if (t is null)
            {
                SetError("No Time packet received yet.");
                return null;
            }
            return Alloc(Json.String(w => Json.Packet(w, t)));
        }
        catch (Exception ex) { return FailNull(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_node_publish_time", CallConvs = [typeof(CallConvCdecl)])]
    public static int NodePublishTime(void* handle, TimeData* time)
    {
        try
        {
            if (time == null) throw new ArgumentNullException("time");
            NodeOf(handle).PublishTime(*time);
            return 0;
        }
        catch (Exception ex) { return Fail(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_node_set_stream_time", CallConvs = [typeof(CallConvCdecl)])]
    public static int NodeSetStreamTime(void* handle, TimeData* time)
    {
        try
        {
            if (time == null) throw new ArgumentNullException("time");
            NodeOf(handle).SetStreamTime(*time);
            return 0;
        }
        catch (Exception ex) { return Fail(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_node_start_time_stream", CallConvs = [typeof(CallConvCdecl)])]
    public static int NodeStartTimeStream(void* handle, int intervalMs)
    {
        try { NodeOf(handle).StartStream(intervalMs); return 0; }
        catch (Exception ex) { return Fail(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_node_stop_time_stream", CallConvs = [typeof(CallConvCdecl)])]
    public static int NodeStopTimeStream(void* handle)
    {
        try { NodeOf(handle).StopStream(); return 0; }
        catch (Exception ex) { return Fail(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_node_request_json", CallConvs = [typeof(CallConvCdecl)])]
    public static byte* NodeRequestJson(void* handle, byte* query, int dataType, int layer, int timeoutMs)
    {
        try
        {
            var r = NodeOf(handle).Request(Str(query), dataType, layer, timeoutMs);
            return Alloc(Json.String(w => Json.Request(w, r)));
        }
        catch (Exception ex) { return FailNull(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_node_request_data", CallConvs = [typeof(CallConvCdecl)])]
    public static int NodeRequestData(void* handle, byte* query, int dataType, int layer, int timeoutMs, byte** data, int* length)
    {
        try
        {
            if (data == null || length == null) throw new ArgumentNullException("out");
            *data = null;
            *length = 0;
            var r = NodeOf(handle).Request(Str(query), dataType, layer, timeoutMs);
            byte[]? bytes = r.Data?.Data ?? r.Packet?.ToArray();
            if (bytes is null)
            {
                SetError(r.ToString());
                return 1;
            }
            byte* p = (byte*)NativeMemory.Alloc((nuint)Math.Max(1, bytes.Length));
            bytes.CopyTo(new Span<byte>(p, bytes.Length));
            *data = p;
            *length = bytes.Length;
            return 0;
        }
        catch (Exception ex) { return Fail(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_node_time_sync_json", CallConvs = [typeof(CallConvCdecl)])]
    public static byte* NodeTimeSyncJson(void* handle, byte* query, int rounds, int timeoutMs)
    {
        try
        {
            var r = NodeOf(handle).TimeSync(Str(query), rounds, timeoutMs);
            return Alloc(Json.String(w => Json.Sync(w, r)));
        }
        catch (Exception ex) { return FailNull(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_node_send_control", CallConvs = [typeof(CallConvCdecl)])]
    public static int NodeSendControl(void* handle, byte* query, byte* path, int timeoutMs)
    {
        try
        {
            int code = NodeOf(handle).Control(Str(query), Need(path, "path"), timeoutMs);
            if (code == -2) SetError("No answer to the control message.");
            return code;
        }
        catch (Exception ex) { return Fail(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_node_send_text", CallConvs = [typeof(CallConvCdecl)])]
    public static int NodeSendText(void* handle, byte* text, byte* query)
    {
        try { NodeOf(handle).SendText(Need(text, "text"), Str(query)); return 0; }
        catch (Exception ex) { return Fail(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_node_send_key", CallConvs = [typeof(CallConvCdecl)])]
    public static int NodeSendKey(void* handle, int code, byte* query)
    {
        try { NodeOf(handle).SendKey(code, Str(query)); return 0; }
        catch (Exception ex) { return Fail(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_node_send_raw", CallConvs = [typeof(CallConvCdecl)])]
    public static int NodeSendRaw(void* handle, byte* data, int length, byte* ip, int port)
    {
        try { NodeOf(handle).SendRaw(Bytes(data, length).ToArray(), Need(ip, "ip"), port); return 0; }
        catch (Exception ex) { return Fail(ex); }
    }

    [UnmanagedCallersOnly(EntryPoint = "tcnet_node_inject", CallConvs = [typeof(CallConvCdecl)])]
    public static int NodeInject(void* handle, byte* data, int length, byte* ip, int port)
    {
        try { NodeOf(handle).Inject(Bytes(data, length), Need(ip, "ip"), port); return 0; }
        catch (Exception ex) { return Fail(ex); }
    }
}
