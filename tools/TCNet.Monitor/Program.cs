using System.Globalization;
using System.Net;
using System.Text;
using TCNet;
using TCNet.Networking;
using TCNet.Text;

// tcnet-monitor – command line utility exposing every TCNet packet, field and option in readable form.

Console.OutputEncoding = Encoding.UTF8;
var cli = new Cli(args);
try
{
    return await cli.RunAsync();
}
catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException or TimeoutException)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 2;
}

internal sealed class Cli
{
    private readonly List<string> _positional = [];
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);

    public Cli(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.StartsWith("--"))
            {
                var name = a[2..];
                string? value = null;
                int eq = name.IndexOf('=');
                if (eq >= 0) { value = name[(eq + 1)..]; name = name[..eq]; }
                else if (i + 1 < args.Length && !args[i + 1].StartsWith("--") && !IsFlag(name)) value = args[++i];
                _options[name] = value;
            }
            else _positional.Add(a);
        }
    }

    private static bool IsFlag(string name) => name is "raw" or "full" or "hex" or "markdown" or "help" or "json" or "own" or "quiet";

    private string? Opt(string name) => _options.TryGetValue(name, out var v) ? v : null;
    private bool Has(string name) => _options.ContainsKey(name);
    private string Arg(int i, string what) => i < _positional.Count ? _positional[i] : throw new ArgumentException($"missing {what}");

    public async Task<int> RunAsync()
    {
        string command = _positional.Count > 0 ? _positional[0].ToLowerInvariant() : "help";
        if (Has("help")) command = "help";
        switch (command)
        {
            case "listen": return await ListenAsync();
            case "nodes": return await NodesAsync();
            case "request": return await RequestAsync();
            case "sync": return await SyncAsync();
            case "control": return await ControlAsync();
            case "text": return await TextAsync();
            case "key": return await KeyAsync();
            case "master": return await MasterAsync();
            case "decode": return Decode();
            case "layout": return Layout();
            case "catalog": return Catalog();
            case "options": return Options();
            case "interfaces": return Interfaces();
            default: Help(); return command == "help" ? 0 : 1;
        }
    }

    private static void Help()
    {
        Console.WriteLine("""
            tcnet-monitor – TCNet V3.5.1B utility

            Network commands
              listen [--full] [--hex] [--type 254,200] [--node NAME]   Live packet log (all ports)
              nodes [--seconds 5]                                      Discover and list every node with full details
              request <node> <datatype> <layer> [--out file]           Request data (metrics, metadata, beatgrid, cues,
                                                                       smallwave, bigwave, artwork, mixer, or a number)
              sync <node> [rounds]                                     Time sync: delay and clock offset
              control <node> "<path>"                                  Control path, e.g. "layer/1/state=6;"
              text [<node>] "<text>"                                   Text Data (broadcast when no node)
              key [<node>] <char|0xNNNN>                               Keyboard Data
              master [--seconds 0] [--interval 20]                     Act as a master: simulated layers, time stream,
                                                                       status, answers requests and control

            Offline commands
              decode <hex | @file>                                     Decode a datagram: every field + hex dump
              layout [<name or type>]                                  Field layout of one or all packets
              options [<name>]                                         Option tables with every value
              catalog [--markdown] [<search>]                          Full reference / search
              interfaces                                               IPv4 interfaces and broadcast addresses

            Node options (network commands)
              --iface <ip>  --bcast <ip>  --name <GW code>  --id <node id>  --port <listener port>
              --role auto|master|slave|repeater  --own (show own packets)
            """);
    }

    // ---------------- Node setup ----------------

    private TCNetNode CreateNode(NodeType defaultRole = NodeType.Slave)
    {
        var s = new TCNetNodeSettings
        {
            NodeName = Opt("name") ?? "TCNETMON",
            NodeType = Opt("role") is { } r ? Enum.Parse<NodeType>(r, true) : defaultRole,
            ApplicationName = "tcnet-monitor",
            ReceiveOwnPackets = Has("own"),
        };
        if (Opt("id") is { } id) s.NodeId = ushort.Parse(id);
        if (Opt("port") is { } port) s.ListenerPort = int.Parse(port);
        if (Opt("iface") is { } ip) s.LocalAddress = IPAddress.Parse(ip);
        if (Opt("bcast") is { } b) s.BroadcastAddress = IPAddress.Parse(b);
        var node = new TCNetNode(s);
        node.Warning += (_, w) => Console.Error.WriteLine($"! {w}");
        node.Start();
        Console.Error.WriteLine($"# node {s.NodeName}#{s.NodeId} ({node.NodeType}) listening on {node.ListenerPort}, broadcast ports [{string.Join(",", node.BoundBroadcastPorts)}], sending to {node.BroadcastAddress}");
        return node;
    }

    private static async Task<TCNetRemoteNode> FindNodeAsync(TCNetNode node, string query, int seconds = 4)
    {
        for (int i = 0; i < seconds * 10; i++)
        {
            if (node.FindNode(query) is { } n && n.ListenerPort > 0) return n;
            await Task.Delay(100);
        }
        throw new TimeoutException($"node '{query}' not found (known: {string.Join(", ", node.Nodes.Select(n => n.NodeName))})");
    }

    private static CancellationToken CtrlC()
    {
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        return cts.Token;
    }

    // ---------------- Commands ----------------

    private async Task<int> ListenAsync()
    {
        var types = Opt("type")?.Split(',').Select(t => byte.Parse(t.Trim())).ToHashSet();
        var nodeFilter = Opt("node");
        bool full = Has("full"), hex = Has("hex");
        await using var node = CreateNode();
        var ct = CtrlC();
        var gate = new object();

        node.PacketReceived += (_, e) =>
        {
            if (types is not null && !types.Contains((byte)e.Packet.MessageType)) return;
            if (nodeFilter is not null && !e.Packet.NodeName.Equals(nodeFilter, StringComparison.OrdinalIgnoreCase)) return;
            lock (gate)
            {
                Console.WriteLine($"{e.Time:HH:mm:ss.fff} {e.RemoteEndPoint,-21} →{e.LocalPort,-5} {e.Packet}");
                if (full) foreach (var f in e.Packet.Describe()) Console.WriteLine($"      {f}");
                if (hex) Console.WriteLine(Wire.HexDump(e.Packet.ToArray(), 256));
            }
        };
        node.InvalidDatagram += (_, e) => { lock (gate) Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {e.RemoteEndPoint,-21} →{e.LocalPort,-5} invalid: {e.Reason} ({e.Data.Length} bytes)"); };
        node.NodeDiscovered += (_, e) => { lock (gate) Console.WriteLine($"+ node {e.Node}"); };
        node.NodeLost += (_, e) => { lock (gate) Console.WriteLine($"- node {e.Node} ({e.Reason})"); };
        node.DataAssembled += (_, e) => { lock (gate) Console.WriteLine($"= assembled {TCNetText.Describe(e.Data.DataType)} layer {TCNetText.LayerName(e.Data.Layer)}: {TCNetUnits.FormatBytes(e.Data.Data.Length)} in {e.Data.PacketCount} packets"); };
        node.TimeSynced += (_, e) => { lock (gate) Console.WriteLine($"~ sync {e.Node.NodeName}: delay {TCNetUnits.FormatMicros(e.Sample.DelayMicros)}, offset {TCNetUnits.FormatMicros(e.Sample.OffsetMicros)}"); };

        try { await Task.Delay(Timeout.Infinite, ct); } catch (OperationCanceledException) { }
        return 0;
    }

    private async Task<int> NodesAsync()
    {
        int seconds = int.Parse(Opt("seconds") ?? "5");
        await using var node = CreateNode();
        Console.Error.WriteLine($"# listening {seconds}s…");
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        foreach (var n in node.Nodes.OrderBy(n => n.NodeName))
        {
            Console.WriteLine($"{n.NodeName}#{n.NodeId}  {n.Address}:{n.ListenerPort}{(n.IsLocal ? "  (this machine)" : "")}");
            Console.WriteLine($"  Type          {n.NodeType} – {TCNetText.Describe(n.NodeType)}");
            Console.WriteLine($"  Options       {TCNetText.DescribeFlags(n.NodeOptions)}");
            Console.WriteLine($"  Protocol      {n.ProtocolVersion}");
            Console.WriteLine($"  Vendor/App    {n.VendorName} / {n.ApplicationName} {n.ApplicationVersion}");
            Console.WriteLine($"  Uptime        {TCNetUnits.FormatDuration(TimeSpan.FromSeconds(n.Uptime))}, nodes seen {n.NodeCount}, packets {n.PacketsReceived}");
            if (n.ClockOffsetMicros is { } off) Console.WriteLine($"  Time sync     offset {TCNetUnits.FormatMicros(off)}, delay {TCNetUnits.FormatMicros(n.DelayMicros ?? 0)}");
            if (n.LastStatus is { } st)
            {
                Console.WriteLine($"  Status        SMPTE {TCNetText.Describe(st.SmpteMode)}, auto master {TCNetText.Describe(st.AutoMasterMode)}");
                for (int i = 0; i < 8; i++)
                {
                    var l = st.Layers[i];
                    Console.WriteLine($"    Layer {TCNetText.LayerLabel(i)}  {l.Name,-16} {TCNetText.Describe(l.State),-15} source {l.Source,-3} track {l.TrackId}");
                }
            }
            if (n.LastTime is { } tp)
            {
                Console.WriteLine($"  Time          general SMPTE {TCNetText.Describe(tp.SmpteMode)}");
                foreach (var l in tp.Layers)
                    Console.WriteLine($"    Layer {TCNetText.LayerLabel(l.Index)}  {TCNetUnits.FormatMs(l.CurrentTimeMs),12} / {TCNetUnits.FormatMs(l.TotalTimeMs),-12} {TCNetText.Short(l.State),-6} beat {l.BeatMarker} TC {l.Timecode} ({TCNetText.Describe(l.TimecodeState)}) on-air {l.OnAir}");
            }
            Console.WriteLine();
        }
        if (node.Nodes.Count == 0) Console.WriteLine("no nodes found");
        return 0;
    }

    private static DataType ParseDataType(string s) => s.ToLowerInvariant() switch
    {
        "metrics" => DataType.Metrics,
        "metadata" or "meta" => DataType.Metadata,
        "beatgrid" or "beats" => DataType.BeatGrid,
        "cues" or "cue" => DataType.CueData,
        "smallwave" or "small" => DataType.SmallWaveform,
        "bigwave" or "big" => DataType.BigWaveform,
        "artwork" or "art" => DataType.LowResArtwork,
        "mixer" => DataType.Mixer,
        _ => (DataType)byte.Parse(s),
    };

    private async Task<int> RequestAsync()
    {
        string who = Arg(1, "node");
        var type = ParseDataType(Arg(2, "data type"));
        byte layer = byte.Parse(_positional.Count > 3 ? _positional[3] : "1");
        await using var node = CreateNode();
        var target = await FindNodeAsync(node, who);
        var result = await node.RequestAsync(target, type, layer, TimeSpan.FromSeconds(5));
        Console.WriteLine(result);
        if (result.Notification is { } en) Console.WriteLine(en.ToDisplayString());
        if (result.Data is { } d)
        {
            switch (d.DataType)
            {
                case DataType.BeatGrid:
                    foreach (var e in d.AsBeatGrid().Entries) Console.WriteLine($"  beat {e.BeatNumber,5}  {TCNetUnits.FormatMs(e.TimestampMs),12}  {TCNetText.Describe(e.Type)}");
                    break;
                case DataType.SmallWaveform or DataType.BigWaveform:
                    PrintWaveform(d.AsWaveform());
                    break;
                case DataType.LowResArtwork:
                    var file = Opt("out") ?? $"artwork-layer{layer}.jpg";
                    await File.WriteAllBytesAsync(file, d.Data);
                    Console.WriteLine($"  saved {TCNetUnits.FormatBytes(d.Data.Length)} to {file}{(d.IsJpeg ? "" : " (no JPEG marker)")}");
                    break;
            }
        }
        else if (result.Packet is { } p) Console.WriteLine(p.ToDisplayString());
        if (Opt("out") is { } o && result.Data is { DataType: not DataType.LowResArtwork } raw) await File.WriteAllBytesAsync(o, raw.Data);
        return result.Success ? 0 : 1;
    }

    private static void PrintWaveform(Waveform w)
    {
        const int width = 100;
        const int height = 8;
        if (w.Bars.Count == 0) return;
        var cols = new int[width];
        for (int c = 0; c < width; c++)
        {
            int from = c * w.Bars.Count / width, to = Math.Max(from + 1, (c + 1) * w.Bars.Count / width);
            cols[c] = w.Bars.Skip(from).Take(to - from).Max(b => b.Level);
        }
        for (int row = height; row >= 1; row--)
            Console.WriteLine("  " + new string(cols.Select(v => v * height / 255.0 >= row - 0.5 ? '█' : ' ').ToArray()));
        Console.WriteLine($"  {w.Bars.Count} bars, peak {w.PeakLevel}");
    }

    private async Task<int> SyncAsync()
    {
        string who = Arg(1, "node");
        int rounds = _positional.Count > 2 ? int.Parse(_positional[2]) : 4;
        await using var node = CreateNode();
        var target = await FindNodeAsync(node, who);
        var s = await node.TimeSyncAsync(target, rounds);
        Console.WriteLine($"{target.NodeName}: delay {TCNetUnits.FormatMicros(s.DelayMicros)}, round trip {TCNetUnits.FormatMicros(s.RoundTripMicros)}, clock offset {TCNetUnits.FormatMicros(s.OffsetMicros)} ({rounds} rounds)");
        return 0;
    }

    private async Task<int> ControlAsync()
    {
        string who = Arg(1, "node");
        string path = Arg(2, "control path");
        await using var node = CreateNode();
        var target = await FindNodeAsync(node, who);
        foreach (var c in ControlCommand.ParseAll(path)) Console.WriteLine($"  {c.Path} = {c.Value ?? "(no value)"}");
        var ack = await node.SendControlAsync(target, path);
        Console.WriteLine(ack is null ? "no response" : $"response: {TCNetText.Describe(ack.Code)}");
        return ack?.Code == NotificationCode.Ok ? 0 : 1;
    }

    private async Task<int> TextAsync()
    {
        await using var node = CreateNode();
        TCNetRemoteNode? target = null;
        string text;
        if (_positional.Count > 2) { target = await FindNodeAsync(node, _positional[1]); text = _positional[2]; }
        else text = Arg(1, "text");
        await node.SendTextAsync(text, target);
        Console.WriteLine($"sent Text Data ({text.Length} chars) to {(target?.ToString() ?? "broadcast 60000")}");
        return 0;
    }

    private async Task<int> KeyAsync()
    {
        await using var node = CreateNode();
        TCNetRemoteNode? target = null;
        string key;
        if (_positional.Count > 2) { target = await FindNodeAsync(node, _positional[1]); key = _positional[2]; }
        else key = Arg(1, "key");
        ushort code = key.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? ushort.Parse(key[2..], NumberStyles.HexNumber) : key[0];
        await node.SendKeyAsync(code, target);
        Console.WriteLine($"sent Keyboard Data 0x{code:X4} to {(target?.ToString() ?? "broadcast 60000")}");
        return 0;
    }

    private async Task<int> MasterAsync()
    {
        int seconds = int.Parse(Opt("seconds") ?? "0");
        int interval = int.Parse(Opt("interval") ?? "20");
        var pb = new TCNetPlayback();
        pb[1].Load(1001, "Demo Artist", "Demo Track One", 245_000, 124);
        pb[2].Load(1002, "Demo Artist", "Demo Track Two", 312_000, 126);
        pb[1].State = LayerState.Playing;
        pb[1].OnAir = 255;
        pb[1].SyncMaster = true;

        await using var node = CreateNode(NodeType.Master);
        node.RequestHandler = (rq, from) =>
        {
            Console.WriteLine($"< request {TCNetText.Describe(rq.DataType)} layer {TCNetText.LayerName(rq.Layer)} from {from?.NodeName}");
            return pb.HandleRequest(rq);
        };
        node.ControlHandler = (cp, from) =>
        {
            var code = pb.Apply(cp);
            Console.WriteLine($"< control \"{cp.Text}\" from {from?.NodeName}: {TCNetText.Describe(code)}");
            return code;
        };
        node.StatusProvider = pb.BuildStatus;
        node.StartTimeStream(pb.BuildTime, TimeSpan.FromMilliseconds(Math.Clamp(interval, 1, 40)));
        Console.Error.WriteLine($"# master running: time every {interval} ms, status every 1 s. Ctrl+C to stop.");

        var ct = CtrlC();
        try
        {
            var until = seconds > 0 ? DateTime.UtcNow.AddSeconds(seconds) : DateTime.MaxValue;
            while (DateTime.UtcNow < until)
            {
                await Task.Delay(1000, ct);
                var t = pb.BuildTime();
                Console.WriteLine(string.Join("  ", t.Layers.Take(4).Select(l => $"{TCNetText.LayerLabel(l.Index)} {TCNetText.Short(l.State)} {l.Timecode}")) + $"   nodes {node.Nodes.Count}");
            }
        }
        catch (OperationCanceledException) { }
        return 0;
    }

    private int Decode()
    {
        var input = Arg(1, "hex or @file");
        byte[] data = input.StartsWith('@') ? File.ReadAllBytes(input[1..]) : Wire.ParseHex(string.Join("", _positional.Skip(1)));
        if (!TCNetPacket.TryParse(data, out var p, out var err))
        {
            Console.WriteLine($"not a TCNet packet: {err}");
            Console.WriteLine(Wire.HexDump(data));
            return 1;
        }
        Console.WriteLine(p!.ToDisplayString());
        if (p.WasPadded) Console.WriteLine($"  note: {data.Length} bytes received, zero-padded to {p.Length}");
        Console.WriteLine(Wire.HexDump(data, 512));
        return 0;
    }

    private int Layout()
    {
        var q = _positional.Count > 1 ? string.Join(" ", _positional.Skip(1)) : null;
        foreach (var p in TCNetOptionCatalog.Packets)
        {
            if (q is not null && !p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) && p.Key != q) continue;
            Console.WriteLine($"{p.Name}  (type {p.Key}, {p.Transport}, port {p.Port}, size {p.Size}, {p.Behavior})");
            Console.WriteLine($"  {p.Functionality}");
            foreach (var f in p.Layout) Console.WriteLine($"  {f.Offset,5} {f.Size,4}  {f.Name}");
            Console.WriteLine();
        }
        return 0;
    }

    private int Options()
    {
        var q = _positional.Count > 1 ? string.Join(" ", _positional.Skip(1)) : null;
        foreach (var s in TCNetText.OptionSets)
        {
            if (q is not null && !s.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
            Console.WriteLine($"{s.Name}  ({s.Location}{(s.IsFlags ? ", flags" : "")})");
            foreach (var o in s.Options) Console.WriteLine($"  {o}");
            Console.WriteLine();
        }
        if (q is null)
        {
            Console.WriteLine("Registered application codes");
            foreach (var a in TCNetText.ApplicationCodes) Console.WriteLine($"  {a}");
        }
        return 0;
    }

    private int Catalog()
    {
        if (Has("markdown")) { Console.WriteLine(TCNetOptionCatalog.ToMarkdown()); return 0; }
        if (_positional.Count > 1)
        {
            foreach (var hit in TCNetOptionCatalog.Search(string.Join(" ", _positional.Skip(1)))) Console.WriteLine(hit);
            return 0;
        }
        Layout();
        Options();
        Console.WriteLine("Spec notes");
        foreach (var n in TCNetOptionCatalog.SpecNotes) Console.WriteLine($"  {n.Topic}: {n.Note}");
        return 0;
    }

    private static int Interfaces()
    {
        foreach (var i in TCNetNetwork.GetInterfaces()) Console.WriteLine(i);
        return 0;
    }
}
