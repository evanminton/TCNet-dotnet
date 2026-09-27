using System.Globalization;
using System.Net;
using System.Text;
using TCNet;
using TCNet.Networking;
using TCNet.Text;

Console.OutputEncoding = Encoding.UTF8;
try
{
    return await new Tool(args).RunAsync();
}
catch (Exception ex) when (ex is FormatException or ArgumentException or InvalidOperationException or TimeoutException or System.Net.Sockets.SocketException)
{
    Console.Error.WriteLine($"tcnet: {ex.Message}");
    return 2;
}

/// <summary>tcnet – every TCNet packet, field and option from the command line, in plain English.</summary>
internal sealed class Tool
{
    private static readonly HashSet<string> Flags = ["full", "hex", "markdown", "own", "help", "all"];
    private readonly List<string> _args = [];
    private readonly Dictionary<string, string?> _opt = new(StringComparer.OrdinalIgnoreCase);

    public Tool(string[] argv)
    {
        for (int i = 0; i < argv.Length; i++)
        {
            if (!argv[i].StartsWith("--")) { _args.Add(argv[i]); continue; }
            var name = argv[i][2..];
            string? value = null;
            if (name.Contains('=')) (name, value) = (name[..name.IndexOf('=')], name[(name.IndexOf('=') + 1)..]);
            else if (!Flags.Contains(name) && i + 1 < argv.Length && !argv[i + 1].StartsWith("--")) value = argv[++i];
            _opt[name] = value;
        }
    }

    private string? Opt(string n) => _opt.GetValueOrDefault(n);
    private bool Has(string n) => _opt.ContainsKey(n);
    private string Arg(int i, string what) => i < _args.Count ? _args[i] : throw new ArgumentException($"missing {what}");
    private string Rest(int from) => string.Join(" ", _args.Skip(from));

    public Task<int> RunAsync() => (_args.FirstOrDefault()?.ToLowerInvariant(), Has("help")) switch
    {
        (_, true) or (null, _) or ("help", _) => Task.FromResult(Help()),
        ("listen", _) => Listen(),
        ("nodes", _) => NodesCmd(),
        ("request", _) => Request(),
        ("sync", _) => Sync(),
        ("control", _) => Control(),
        ("text", _) => SendText(),
        ("key", _) => SendKey(),
        ("send", _) => Send(),
        ("master", _) => Master(),
        ("decode", _) => Task.FromResult(Decode()),
        ("build", _) => Task.FromResult(Build()),
        ("layout", _) => Task.FromResult(Layout()),
        ("options", _) => Task.FromResult(Options()),
        ("codes", _) => Task.FromResult(Codes()),
        ("notes", _) => Task.FromResult(Notes()),
        ("search", _) => Task.FromResult(Search()),
        ("reference", _) => Task.FromResult(Reference()),
        ("interfaces", _) => Task.FromResult(Interfaces()),
        (var c, _) => Task.FromResult(Unknown(c!)),
    };

    private static int Unknown(string c)
    {
        Console.Error.WriteLine($"unknown command '{c}' – try: tcnet help");
        return 1;
    }

    private static int Help()
    {
        Console.WriteLine("""
            tcnet – TCNet Link Specification V3.5.1B utility

            On the network
              listen [--full] [--hex] [--type 254,200] [--from NAME]   live log of every packet, decoded
              nodes [--seconds 5]                                      population list with every Opt-IN / Status / Time field
              request <node> <data> [layer] [--out file]               metrics | metadata | beatgrid | cues | smallwave |
                                                                       bigwave | artwork | mixer | <number>
              sync <node> [rounds]                                     time sync: delay, round trip, clock offset
              control <node> "<path>"                                  e.g. "layer/1/state=6;" (6 = stop)
              text [<node>] "<text>"                                   Text Data (broadcast without a node)
              key [<node>] <char|0xNNNN>                               Keyboard Data
              send <type> [<node>|<ip:port>|bcast:<port>]              build any packet (see 'build') and send it
              master [--seconds N] [--interval 20]                     act as a master: 8 simulated layers, Time
                                                                       stream, Status, answers requests and control

            Offline
              decode <hex>|@file                                       every field of a datagram + hex dump
              build <type>                                             a default packet of any type: fields + hex
              layout [<name|type>]                                     byte layout of one or all packets
              options [<name>]                                         every option table and value
              codes | notes | interfaces                               application codes, spec notes, network interfaces
              search <text>                                            search packets, fields, options, notes
              reference [--markdown]                                   the whole reference

            Node options: --iface <ip> --bcast <ip> --name <GW code> --id <n> --port <n>
                          --role auto|master|slave|repeater --own
            Types for 'build'/'send': a name (optin, status, time, metrics, …) or a number (254, 200/2, 204/128)
            """);
        return 0;
    }

    // ───────── node ─────────

    private async Task<TCNetNode> Node(NodeType role = NodeType.Slave)
    {
        var s = new NodeSettings
        {
            NodeName = Opt("name") ?? "TCNETCLI",
            NodeType = Opt("role") is { } r ? Enum.Parse<NodeType>(r, true) : role,
            DeviceName = "tcnet utility",
            ReceiveOwnPackets = Has("own"),
        };
        if (Opt("id") is { } id) s.NodeId = ushort.Parse(id, CultureInfo.InvariantCulture);
        if (Opt("port") is { } port) s.ListenerPort = int.Parse(port, CultureInfo.InvariantCulture);
        if (Opt("iface") is { } ip) s.LocalAddress = IPAddress.Parse(ip);
        if (Opt("bcast") is { } b) s.BroadcastAddress = IPAddress.Parse(b);
        var node = new TCNetNode(s);
        node.Warning += (_, w) => Console.Error.WriteLine($"! {w}");
        await node.StartAsync();
        Console.Error.WriteLine($"# {s.NodeName}#{s.NodeId} ({node.NodeType}) on port {node.ListenerPort}; shared ports {string.Join(",", node.SharedPorts)}; broadcast {node.BroadcastAddress}");
        return node;
    }

    private static async Task<RemoteNode> Find(TCNetNode node, string query, int seconds = 4)
    {
        for (int i = 0; i < seconds * 10; i++)
        {
            if (node.FindNode(query) is { ListenerPort: > 0 } n) return n;
            await Task.Delay(100);
        }
        throw new TimeoutException($"node '{query}' not found; seen: {string.Join(", ", node.Nodes.Select(n => n.NodeName))}");
    }

    private static CancellationToken CtrlC()
    {
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        return cts.Token;
    }

    private static void Fields(TCNetPacket p, string indent = "    ")
    {
        foreach (var f in p.Describe()) Console.WriteLine(indent + f);
    }

    // ───────── commands ─────────

    private async Task<int> Listen()
    {
        var types = Opt("type")?.Split(',').Select(t => byte.Parse(t.Trim(), CultureInfo.InvariantCulture)).ToHashSet();
        var from = Opt("from");
        bool full = Has("full"), hex = Has("hex");
        await using var node = await Node();
        var gate = new object();
        void Line(string s) { lock (gate) Console.WriteLine(s); }

        node.PacketReceived += (_, e) =>
        {
            if (types is not null && !types.Contains((byte)e.Packet.MessageType)) return;
            if (from is not null && !e.Packet.NodeName.Equals(from, StringComparison.OrdinalIgnoreCase)) return;
            lock (gate)
            {
                Console.WriteLine($"{e.Time:HH:mm:ss.fff} {e.RemoteEndPoint,-21} :{e.Port,-5} {e.Packet}");
                if (full) Fields(e.Packet);
                if (hex) Console.Write(TCNet.Wire.HexDump(e.Packet.ToArray(), 512));
            }
        };
        node.InvalidDatagram += (_, e) => Line($"{DateTime.Now:HH:mm:ss.fff} {e.RemoteEndPoint,-21} :{e.Port,-5} not TCNet: {e.Reason}");
        node.NodeDiscovered += (_, e) => Line($"+ {e.Node}");
        node.NodeLost += (_, e) => Line($"- {e.Node} ({e.Reason})");
        node.DataAssembled += (_, e) => Line($"= {TCNetText.Describe(e.Data.DataType)} layer {TCNetText.LayerName(e.Data.Layer)}: {TCNetUnits.Bytes(e.Data.Data.Length)} in {e.Data.Packets} packets");
        node.TimeSynced += (_, e) => Line($"~ {e.Node.NodeName}: delay {TCNetUnits.Micros(e.Result.DelayMicros)}, offset {TCNetUnits.Micros(e.Result.OffsetMicros)}");
        node.RoleChanged += (_, r) => Line($"* role → {r}");
        try { await Task.Delay(Timeout.Infinite, CtrlC()); } catch (OperationCanceledException) { }
        return 0;
    }

    private async Task<int> NodesCmd()
    {
        int seconds = int.Parse(Opt("seconds") ?? "5", CultureInfo.InvariantCulture);
        await using var node = await Node();
        Console.Error.WriteLine($"# listening {seconds} s …");
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        var list = node.Nodes.OrderBy(n => n.NodeName).ToList();
        if (list.Count == 0) Console.WriteLine("no nodes found");
        foreach (var n in list)
        {
            Console.WriteLine($"{n.NodeName}#{n.NodeId}  {n.Address}:{n.ListenerPort}{(n.IsLocal ? "  (this machine)" : "")}");
            Console.WriteLine($"  Node Type       {(byte)n.NodeType} – {TCNetText.Describe(n.NodeType)}");
            Console.WriteLine($"  Node Options    {(ushort)n.NodeOptions} – {TCNetText.DescribeFlags(n.NodeOptions)}");
            Console.WriteLine($"  Protocol        {n.ProtocolVersion}");
            Console.WriteLine($"  Vendor / device {n.VendorName} / {n.DeviceName} {n.DeviceVersion}");
            Console.WriteLine($"  Uptime          {TCNetUnits.Duration(TimeSpan.FromSeconds(n.Uptime))}; registers {n.NodeCount} nodes; {n.Packets} packets received");
            if (n.Sync is { } s) Console.WriteLine($"  Time sync       delay {TCNetUnits.Micros(s.DelayMicros)}, offset {TCNetUnits.Micros(s.OffsetMicros)}");
            if (n.Status is { } st)
            {
                Console.WriteLine($"  Status          SMPTE {TCNetText.Describe(st.SmpteMode)}; auto master {TCNetText.Describe(st.AutoMasterMode)}");
                for (int i = 0; i < 8; i++)
                {
                    var l = st.Layers[i];
                    Console.WriteLine($"    Layer {TCNetText.LayerLabel(i)}  {l.Name,-16} {TCNetText.Describe(l.State),-16} source {l.Source,-3} track {l.TrackId}");
                }
            }
            if (n.Time is { } t)
            {
                Console.WriteLine($"  Time            general SMPTE {TCNetText.Describe(t.SmpteMode)}");
                foreach (var l in t.Layers)
                    Console.WriteLine($"    Layer {l.Label}  {TCNetUnits.Ms(l.TimeMs),12} / {TCNetUnits.Ms(l.TotalMs),-12} {TCNetText.Short(l.State),-7} beat {l.BeatMarker}  TC {l.Timecode} {TCNetText.Describe(t.ModeOf(l))}, {TCNetText.Describe(l.TimecodeState)}  on air {l.OnAir}");
            }
            if (n.Mixer is { } mx) Console.WriteLine($"  Mixer           {mx.Summary}");
            Console.WriteLine();
        }
        return 0;
    }

    private static DataType ParseData(string s) => s.ToLowerInvariant() switch
    {
        "metrics" => DataType.Metrics,
        "metadata" or "meta" => DataType.Metadata,
        "beatgrid" or "beats" or "grid" => DataType.BeatGrid,
        "cues" or "cue" or "cuedata" => DataType.CueData,
        "smallwave" or "small" or "smallwaveform" => DataType.SmallWaveform,
        "bigwave" or "big" or "bigwaveform" => DataType.BigWaveform,
        "artwork" or "art" => DataType.LowResArtwork,
        "mixer" => DataType.Mixer,
        _ => (DataType)byte.Parse(s, CultureInfo.InvariantCulture),
    };

    private async Task<int> Request()
    {
        await using var node = await Node();
        var target = await Find(node, Arg(1, "node"));
        var type = ParseData(Arg(2, "data type"));
        byte layer = _args.Count > 3 ? byte.Parse(_args[3], CultureInfo.InvariantCulture) : type == DataType.Mixer ? (byte)0 : (byte)1;
        var r = await node.RequestAsync(target, type, layer, TimeSpan.FromSeconds(5));
        Console.WriteLine(r);
        if (r.Notification is { } en) Fields(en);
        else if (r.Data is { } d)
        {
            switch (d.DataType)
            {
                case DataType.BeatGrid:
                    foreach (var b in d.ToBeatGrid().Beats) Console.WriteLine($"  beat {b.Number,5}  {TCNetUnits.Ms(b.TimeMs),11}  {TCNetText.Describe(b.Type)}");
                    break;
                case DataType.SmallWaveform or DataType.BigWaveform:
                    Draw(d.ToWaveform());
                    break;
                case DataType.LowResArtwork:
                    var file = Opt("out") ?? $"artwork-{target.NodeName}-L{layer}.jpg";
                    await File.WriteAllBytesAsync(file, d.Data);
                    Console.WriteLine($"  saved {TCNetUnits.Bytes(d.Data.Length)} to {file}{(d.IsJpeg ? "" : " (no JPEG marker)")}");
                    break;
            }
            if (Opt("out") is { } o && d.DataType != DataType.LowResArtwork) await File.WriteAllBytesAsync(o, d.Data);
        }
        else if (r.Packet is { } p) Fields(p);
        return r.Success ? 0 : 1;
    }

    private static void Draw(Waveform w)
    {
        const int width = 100, rows = 8;
        if (w.Bars.Count == 0) return;
        var cols = Enumerable.Range(0, width).Select(c =>
        {
            int a = c * w.Bars.Count / width, b = Math.Max(a + 1, (c + 1) * w.Bars.Count / width);
            return w.Bars.Skip(a).Take(b - a).Max(x => x.Level);
        }).ToArray();
        for (int r = rows; r >= 1; r--)
            Console.WriteLine("  " + new string(cols.Select(v => v * rows / 255.0 >= r - 0.5 ? '█' : ' ').ToArray()));
        Console.WriteLine($"  {w.Bars.Count} bars, peak level {w.Peak}");
    }

    private async Task<int> Sync()
    {
        await using var node = await Node();
        var target = await Find(node, Arg(1, "node"));
        int rounds = _args.Count > 2 ? int.Parse(_args[2], CultureInfo.InvariantCulture) : 4;
        var s = await node.TimeSyncAsync(target, rounds);
        Console.WriteLine($"{target.NodeName}: delay {TCNetUnits.Micros(s.DelayMicros)}, round trip {TCNetUnits.Micros(s.RoundTripMicros)}, clock offset {TCNetUnits.Micros(s.OffsetMicros)} over {rounds} rounds");
        return 0;
    }

    private async Task<int> Control()
    {
        await using var node = await Node();
        var target = await Find(node, Arg(1, "node"));
        var path = Rest(2);
        foreach (var c in ControlCommand.Parse(path))
            Console.WriteLine($"  {c.Path} = {c.Value ?? "(no value)"}{(c.Leaf == "state" && byte.TryParse(c.Value, out var st) ? $"  ({TCNetText.Describe((LayerState)st)})" : "")}");
        var ack = await node.SendControlAsync(target, path);
        Console.WriteLine(ack is null ? "no answer" : $"answer: {TCNetText.Describe(ack.Code)}");
        return ack?.Code == NotificationCode.Ok ? 0 : 1;
    }

    private async Task<(TCNetNode Node, RemoteNode? Target, string Rest)> Targeted()
    {
        var node = await Node();
        if (_args.Count > 2) return (node, await Find(node, _args[1]), Rest(2));
        return (node, null, Arg(1, "value"));
    }

    private async Task<int> SendText()
    {
        var (node, target, text) = await Targeted();
        await using var _ = node;
        await node.SendTextAsync(text, target);
        Console.WriteLine($"sent Text Data ({text.Length} characters) to {target?.ToString() ?? "broadcast 60000"}");
        return 0;
    }

    private async Task<int> SendKey()
    {
        var (node, target, key) = await Targeted();
        await using var _ = node;
        ushort code = key.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? ushort.Parse(key[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture) : key[0];
        await node.SendKeyAsync(code, target);
        Console.WriteLine($"sent Keyboard Data 0x{code:X4} to {target?.ToString() ?? "broadcast 60000"}");
        return 0;
    }

    private static PacketInfo FindType(string q)
    {
        var alias = q.ToLowerInvariant().Replace("-", "").Replace(" ", "") switch
        {
            "optin" => "2", "optout" => "3", "status" => "5", "timesync" or "sync" => "10", "error" or "notification" => "13",
            "request" => "20", "appdata" or "application" => "30", "control" => "101", "text" => "128", "keyboard" or "key" => "132",
            "metrics" => "200/2", "metadata" => "200/4", "beatgrid" => "200/8", "cues" or "cuedata" => "200/12",
            "smallwave" or "smallwaveform" => "200/16", "bigwave" or "bigwaveform" => "200/32", "mixer" => "200/150",
            "artwork" => "204/128", "appdata213" => "213", "time" => "254",
            var other => other,
        };
        return TCNetCatalog.Packets.FirstOrDefault(p => p.Key == alias)
               ?? TCNetCatalog.Packets.FirstOrDefault(p => p.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
               ?? throw new ArgumentException($"unknown packet type '{q}'");
    }

    private static TCNetPacket Sample(PacketInfo info)
    {
        var p = info.Create();
        switch (p)
        {
            case TimePacket: return Playback.Demo().BuildTime();
            case StatusPacket: return Playback.Demo().BuildStatus();
            case ControlPacket c: c.Text = "layer/1/state=3;"; break;
            case TextDataPacket t: t.Text = "Hello from tcnet"; break;
            case RequestPacket r: r.DataType = DataType.Metrics; r.Layer = 1; break;
        }
        return p;
    }

    private int Build()
    {
        var p = Sample(FindType(Rest(1)));
        Console.WriteLine(p.ToDisplayString());
        Console.Write(TCNet.Wire.HexDump(p.ToArray(), 1024));
        Console.WriteLine(Convert.ToHexString(p.ToArray()));
        return 0;
    }

    private async Task<int> Send()
    {
        var info = FindType(Arg(1, "packet type"));
        await using var node = await Node();
        var p = Sample(info);
        string to = _args.Count > 2 ? _args[2] : "bcast:" + (p is TimePacket ? TCNetConstants.TimePort : TCNetConstants.BroadcastPort);
        IPEndPoint ep = to.StartsWith("bcast:", StringComparison.OrdinalIgnoreCase)
            ? new IPEndPoint(node.BroadcastAddress, int.Parse(to[6..], CultureInfo.InvariantCulture))
            : IPEndPoint.TryParse(to, out var direct) ? direct : (await Find(node, to)).EndPoint!;
        await node.SendAsync(p, ep);
        Console.WriteLine($"sent {p.Name} ({p.Length} bytes) to {ep}");
        Fields(p);
        return 0;
    }

    private async Task<int> Master()
    {
        int seconds = int.Parse(Opt("seconds") ?? "0", CultureInfo.InvariantCulture);
        int interval = Math.Clamp(int.Parse(Opt("interval") ?? "20", CultureInfo.InvariantCulture), 1, 40);
        var pb = Playback.Demo();
        await using var node = await Node(NodeType.Master);
        node.RequestHandler = (rq, from) =>
        {
            Console.WriteLine($"< request {TCNetText.Describe(rq.DataType)} layer {TCNetText.LayerName(rq.Layer)} from {from.NodeName}");
            return pb.Answer(rq);
        };
        node.ControlHandler = (cp, from) =>
        {
            var code = pb.Apply(cp);
            Console.WriteLine($"< control \"{cp.Text}\" from {from.NodeName}: {TCNetText.Describe(code)}");
            return code;
        };
        node.StatusProvider = pb.BuildStatus;
        node.StartTimeStream(pb.BuildTime, TimeSpan.FromMilliseconds(interval));
        Console.Error.WriteLine($"# master: Time every {interval} ms, Status and Opt-IN every second. Ctrl+C stops.");
        var ct = CtrlC();
        var end = seconds > 0 ? DateTime.UtcNow.AddSeconds(seconds) : DateTime.MaxValue;
        try
        {
            while (DateTime.UtcNow < end)
            {
                await Task.Delay(1000, ct);
                var t = pb.BuildTime();
                Console.WriteLine(string.Join("   ", t.Layers.Take(4).Select(l => $"{l.Label} {TCNetText.Short(l.State)} {l.Timecode}")) + $"   nodes {node.Nodes.Count}");
            }
        }
        catch (OperationCanceledException) { }
        return 0;
    }

    private int Decode()
    {
        var src = Rest(1);
        var data = src.StartsWith('@') ? File.ReadAllBytes(src[1..]) : TCNet.Wire.ParseHex(src);
        if (!TCNetPacket.TryParse(data, out var p, out var err))
        {
            Console.WriteLine($"not a TCNet packet: {err}");
            Console.Write(TCNet.Wire.HexDump(data));
            return 1;
        }
        Console.WriteLine(p!.ToDisplayString());
        if (p.WasPadded) Console.WriteLine($"  note: {data.Length} bytes received; zero-padded to {p.Length} (older flame?)");
        Console.Write(TCNet.Wire.HexDump(data, 1024));
        return 0;
    }

    private int Layout()
    {
        var q = Rest(1);
        foreach (var p in TCNetCatalog.Packets)
        {
            if (q.Length > 0 && p.Key != q && !p.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
            Console.WriteLine($"{p.Name}  (type {p.Key}; {p.Transport}; port {p.Port}; size {p.Size}; {p.Behavior})");
            Console.WriteLine($"  {p.Purpose}");
            foreach (var f in p.Layout) Console.WriteLine($"  {f.Offset,5} {f.Size,4}  {f.Name}");
            Console.WriteLine();
        }
        return 0;
    }

    private int Options()
    {
        var q = Rest(1);
        foreach (var s in TCNetText.OptionSets)
        {
            if (q.Length > 0 && !s.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) continue;
            Console.WriteLine($"{s.Name}  ({s.Where}{(s.IsFlags ? "; flags, summed" : "")})");
            foreach (var o in s.Options) Console.WriteLine($"  {o.Value,5}  {o.Name,-22} {o.Description}");
            Console.WriteLine();
        }
        return 0;
    }

    private static int Codes()
    {
        foreach (var a in TCNetText.ApplicationCodes) Console.WriteLine($"  {a.Code:X4}  {a.Vendor,-32} {a.Url}");
        return 0;
    }

    private static int Notes()
    {
        foreach (var n in TCNetCatalog.Notes) Console.WriteLine($"• {n.Topic}: {n.Note}");
        return 0;
    }

    private int Search()
    {
        int hits = 0;
        foreach (var h in TCNetCatalog.Search(Rest(1))) { Console.WriteLine(h); hits++; }
        if (hits == 0) Console.WriteLine("no matches");
        return 0;
    }

    private int Reference()
    {
        if (Has("markdown")) { Console.WriteLine(TCNetCatalog.ToMarkdown()); return 0; }
        _args.RemoveRange(1, _args.Count - 1);
        Layout();
        Options();
        Console.WriteLine("Registered application codes");
        Codes();
        Console.WriteLine();
        Console.WriteLine("Spec notes");
        return Notes();
    }

    private static int Interfaces()
    {
        foreach (var i in TCNetNetwork.Interfaces()) Console.WriteLine(i);
        return 0;
    }
}
