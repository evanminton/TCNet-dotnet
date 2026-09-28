using System.Net;
using System.Reflection;
using System.Text.Json;
using TCNet.Networking;
using TCNet.Text;

namespace TCNet.Native;

/// <summary>
/// The managed side of the C API: plain .NET types in and out, so it can be unit tested.
/// <see cref="Exports"/> only converts pointers and catches exceptions.
/// </summary>
internal static class Api
{
    public static string Version { get; } =
        (typeof(TCNetPacket).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
         ?? typeof(TCNetPacket).Assembly.GetName().Version?.ToString() ?? "0")
        + $" (TCNet Link Specification {TCNetConstants.ProtocolMajor}.{TCNetConstants.ProtocolMinor})";

    // ─────────────── packets ───────────────

    public static TCNetPacket Parse(ReadOnlySpan<byte> datagram) =>
        TCNetParser.TryParse(datagram, out var packet, out var error) && packet is not null
            ? packet
            : throw new FormatException(error ?? "Not a TCNet packet.");

    public static string ParseJson(ReadOnlySpan<byte> datagram)
    {
        var p = Parse(datagram);
        return Json.String(w => Json.Packet(w, p));
    }

    public static string ParseText(ReadOnlySpan<byte> datagram) => Parse(datagram).ToDisplayString();

    public static byte[] Template(int messageType, int dataType) =>
        TCNetParser.Create((MessageType)checked((byte)messageType), (DataType)checked((byte)dataType)).ToArray();

    public static byte[] EncodeTime(TimeData d) => TimeData.ToPacket(d).ToArray();

    public static TimeData DecodeTime(ReadOnlySpan<byte> datagram) =>
        Parse(datagram) is TimePacket t ? TimeData.FromPacket(t) : throw new FormatException("Not a Time packet (type 254).");

    // ─────────────── reference ───────────────

    public static string CatalogJson() => Json.String(Json.Catalog);

    public static string SearchJson(string text)
    {
        var hits = TCNetCatalog.Search(text).ToList();
        return Json.String(w =>
        {
            w.WriteStartArray();
            foreach (var h in hits) w.WriteStringValue(h);
            w.WriteEndArray();
        });
    }

    public static string InterfacesJson() => Json.String(Json.Interfaces);

    /// <summary>
    /// Meaning of a value in an option table, found by table name ("Node Type") or enum name ("NodeType").
    /// Flag tables give "A + B"; unknown values say so.
    /// </summary>
    public static string Describe(string optionSet, long value)
    {
        var set = TCNetCatalog.OptionSets.FirstOrDefault(s =>
                      s.Name.Equals(optionSet, StringComparison.OrdinalIgnoreCase) ||
                      s.EnumType.Name.Equals(optionSet, StringComparison.OrdinalIgnoreCase))
                  ?? throw new ArgumentException($"No option table \"{optionSet}\". Tables: {string.Join(", ", TCNetCatalog.OptionSets.Select(s => s.Name))}.");
        if (!set.IsFlags) return set.Find(value)?.Description ?? $"Unknown ({value})";
        if (value == 0) return "None";
        var parts = new List<string>();
        long known = 0;
        foreach (var o in set.Options)
        {
            if (o.Value == 0 || (value & o.Value) != o.Value) continue;
            parts.Add(o.Description.Split(" – ")[0]);
            known |= o.Value;
        }
        if ((value & ~known) != 0) parts.Add($"unknown 0x{value & ~known:X}");
        return string.Join(" + ", parts);
    }

    // ─────────────── settings ───────────────

    /// <summary>
    /// NodeSettings from JSON; every property is optional and names are case-insensitive. Times are in ms
    /// (requestTimeoutMs −1 = wait forever). Enums take a number or a name ("Master").
    /// </summary>
    public static NodeSettings ParseSettings(string? json)
    {
        var s = new NodeSettings();
        if (string.IsNullOrWhiteSpace(json)) return s;
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new FormatException("Settings must be a JSON object.");
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            var v = p.Value;
            switch (p.Name.ToLowerInvariant())
            {
                case "nodeid": s.NodeId = v.GetUInt16(); break;
                case "nodename": s.NodeName = v.GetString() ?? ""; break;
                case "nodetype": s.NodeType = v.ValueKind == JsonValueKind.Number ? (NodeType)v.GetByte() : Enum.Parse<NodeType>(v.GetString()!, true); break;
                case "nodeoptions": s.NodeOptions = v.ValueKind == JsonValueKind.Number ? (NodeOptions)v.GetUInt16() : Enum.Parse<NodeOptions>(v.GetString()!, true); break;
                case "vendorname": s.VendorName = v.GetString() ?? ""; break;
                case "devicename": s.DeviceName = v.GetString() ?? ""; break;
                case "devicemajor": s.DeviceMajor = v.GetByte(); break;
                case "deviceminor": s.DeviceMinor = v.GetByte(); break;
                case "devicebug": s.DeviceBug = v.GetByte(); break;
                case "versionmajor": s.VersionMajor = v.GetByte(); break;
                case "versionminor": s.VersionMinor = v.GetByte(); break;
                case "listenerport": s.ListenerPort = v.GetInt32(); break;
                case "localaddress": s.LocalAddress = v.ValueKind == JsonValueKind.Null ? IPAddress.Any : IPAddress.Parse(v.GetString()!); break;
                case "broadcastaddress": s.BroadcastAddress = v.ValueKind == JsonValueKind.Null ? null : IPAddress.Parse(v.GetString()!); break;
                case "listenonbroadcastports": s.ListenOnBroadcastPorts = v.GetBoolean(); break;
                case "optinintervalms": s.OptInInterval = Ms(v); break;
                case "nodetimeoutms": s.NodeTimeout = Ms(v); break;
                case "unicastoptin": s.UnicastOptIn = v.GetBoolean(); break;
                case "sendstatus": s.SendStatus = v.ValueKind == JsonValueKind.Null ? null : v.GetBoolean(); break;
                case "answertimesync": s.AnswerTimeSync = v.GetBoolean(); break;
                case "autotimesync": s.AutoTimeSync = v.GetBoolean(); break;
                case "timesyncintervalms": s.TimeSyncInterval = Ms(v); break;
                case "autorequestmetadata": s.AutoRequestMetadata = v.GetBoolean(); break;
                case "autorequestmetrics": s.AutoRequestMetrics = v.GetBoolean(); break;
                case "automasterelection": s.AutoMasterElection = v.GetBoolean(); break;
                case "receiveownpackets": s.ReceiveOwnPackets = v.GetBoolean(); break;
                case "requesttimeoutms": s.RequestTimeout = v.GetDouble() < 0 ? Timeout.InfiniteTimeSpan : Ms(v); break;
                // Read-only descriptions written by tcnet_node_info_json; accepted so that JSON can be fed back.
                case "nodetypetext" or "nodeoptionstext": break;
                default: throw new ArgumentException($"Unknown setting \"{p.Name}\".");
            }
        }
        return s;
    }

    private static TimeSpan Ms(JsonElement v) => TimeSpan.FromMilliseconds(v.GetDouble());

    public static string SettingsJson(NodeSettings s) => Json.String(w => Json.Settings(w, s));

    /// <summary>0 = the node's default, &lt; 0 = forever.</summary>
    public static TimeSpan? Wait(int ms) =>
        ms == 0 ? null : ms < 0 ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds((double)ms);
}
