using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using TCNet.Native;
using TCNet.Networking;
using TCNet.Text;

namespace TCNet.Tests;

/// <summary>The managed side of the C API (src/TCNet.Native); the C side is exercised by samples/native/tcnet_demo.c.</summary>
public class NativeApiTests
{
    [Fact]
    public void TimeStruct_MatchesTheHeader()
    {
        Assert.Equal(20, Unsafe.SizeOf<TimeLayerData>());
        Assert.Equal(164, Unsafe.SizeOf<TimeData>());
    }

    [Fact]
    public void ParseJson_HasEveryField()
    {
        var packet = new OptInPacket { NodeId = 7, NodeName = "CHRONOS" };
        using var doc = JsonDocument.Parse(Api.ParseJson(packet.ToArray()));
        var root = doc.RootElement;
        Assert.Equal("Opt-IN", root.GetProperty("name").GetString());
        Assert.Equal((int)MessageType.OptIn, root.GetProperty("messageType").GetInt32());
        Assert.Equal("CHRONOS", root.GetProperty("nodeName").GetString());
        Assert.Equal(packet.Describe().Count, root.GetProperty("fields").GetArrayLength());
    }

    [Fact]
    public void Parse_Garbage_Throws() => Assert.Throws<FormatException>(() => Api.ParseJson("nope"u8));

    [Fact]
    public void Time_EncodesAndDecodes_WithAutoTimecode()
    {
        var d = new TimeData { SmpteMode = (byte)SmpteMode.Fps30, Flags = TimeData.AutoTimecode };
        d.Layers[0] = new TimeLayerData { TimeMs = 61000, TotalMs = 300000, State = (byte)LayerState.Playing, OnAir = 255 };
        var bytes = Api.EncodeTime(d);
        Assert.Equal(TCNetConstants.TimeLength, bytes.Length);

        var back = Api.DecodeTime(bytes);
        var l = back.Layers[0];
        Assert.Equal(61000u, l.TimeMs);
        Assert.Equal((byte)LayerState.Playing, l.State);
        Assert.Equal((0, 1, 1, 0), ((int)l.Hours, (int)l.Minutes, (int)l.Seconds, (int)l.Frames));
        Assert.Equal((byte)SmpteMode.Fps30, back.SmpteMode);
    }

    [Fact]
    public void DecodeTime_RejectsOtherPackets() =>
        Assert.Throws<FormatException>(() => Api.DecodeTime(new OptInPacket().ToArray()));

    [Fact]
    public void Settings_ParseEveryKind()
    {
        var s = Api.ParseSettings("""
            {"nodeName":"CHRONOS","NodeType":"Master","nodeOptions":6,"listenerPort":65100,"localAddress":"127.0.0.1",
             "broadcastAddress":null,"sendStatus":true,"requestTimeoutMs":-1,"optInIntervalMs":500}
            """);
        Assert.Equal("CHRONOS", s.NodeName);
        Assert.Equal(NodeType.Master, s.NodeType);
        Assert.Equal(NodeOptions.SupportsControl | NodeOptions.SupportsApplicationData, s.NodeOptions);
        Assert.Equal(65100, s.ListenerPort);
        Assert.Equal(IPAddress.Loopback, s.LocalAddress);
        Assert.Null(s.BroadcastAddress);
        Assert.True(s.SendStatus);
        Assert.Equal(Timeout.InfiniteTimeSpan, s.RequestTimeout);
        Assert.Equal(TimeSpan.FromMilliseconds(500), s.OptInInterval);
    }

    [Fact]
    public void Settings_RoundTripThroughInfoJson()
    {
        var s = Api.ParseSettings("""{"nodeName":"LOOP","nodeType":2,"autoMasterElection":true}""");
        var again = Api.ParseSettings(Api.SettingsJson(s));
        Assert.Equal(s.NodeName, again.NodeName);
        Assert.Equal(s.NodeType, again.NodeType);
        Assert.Equal(s.NodeId, again.NodeId);
        Assert.True(again.AutoMasterElection);
        Assert.Equal(s.RequestTimeout, again.RequestTimeout);
    }

    [Fact]
    public void Settings_UnknownName_Throws() => Assert.Throws<ArgumentException>(() => Api.ParseSettings("""{"nope":1}"""));

    [Fact]
    public void Describe_ByTableOrEnumName()
    {
        Assert.Equal(TCNetText.Describe(NodeType.Master), Api.Describe("Node Type", (long)NodeType.Master));
        Assert.Equal(TCNetText.Describe(LayerState.Playing), Api.Describe("LayerState", (long)LayerState.Playing));
        Assert.Equal(TCNetText.DescribeFlags(NodeOptions.SupportsControl | NodeOptions.DoNotDisturb),
            Api.Describe("NodeOptions", (long)(NodeOptions.SupportsControl | NodeOptions.DoNotDisturb)));
        Assert.Throws<ArgumentException>(() => Api.Describe("No Such Table", 1));
    }

    [Fact]
    public void Catalog_And_Search_AreJson()
    {
        using var catalog = JsonDocument.Parse(Api.CatalogJson());
        Assert.Equal(TCNetCatalog.Packets.Count, catalog.RootElement.GetProperty("packets").GetArrayLength());
        Assert.Equal(TCNetCatalog.OptionSets.Count, catalog.RootElement.GetProperty("optionSets").GetArrayLength());
        using var hits = JsonDocument.Parse(Api.SearchJson("artwork"));
        Assert.True(hits.RootElement.GetArrayLength() > 0);
    }

    [Fact]
    public void Node_InjectedPackets_ShowUpInJson()
    {
        var n = new NativeNode(new NodeSettings { NodeName = "TEST" });
        var from = "10.0.0.5";
        n.Inject(new OptInPacket { NodeId = 3, NodeName = "CDJ" }.ToArray(), from, 60000);
        var time = new TimePacket { NodeId = 3, NodeName = "CDJ", SmpteMode = SmpteMode.Fps25 };
        time.Layers[1].TimeMs = 1500;
        time.ComputeTimecode(time.Layers[1]);
        n.Inject(time.ToArray(), from, 60001);

        using var nodes = JsonDocument.Parse(n.NodesJson());
        var node = Assert.Single(nodes.RootElement.EnumerateArray());
        Assert.Equal("10.0.0.5#3", node.GetProperty("key").GetString());
        Assert.Equal("00:00:01:12", node.GetProperty("layers")[1].GetProperty("timecode").GetString());

        var latest = n.LatestTime(null);
        Assert.NotNull(latest);
        Assert.Equal(1500u, TimeData.FromPacket(latest!).Layers[1].TimeMs);
        Assert.NotNull(n.FindJson("CDJ"));
        using var info = JsonDocument.Parse(n.InfoJson());
        Assert.False(info.RootElement.GetProperty("running").GetBoolean());
    }

    [Fact]
    public void Wait_MapsZeroAndNegative()
    {
        Assert.Null(Api.Wait(0));
        Assert.Equal(Timeout.InfiniteTimeSpan, Api.Wait(-1));
        Assert.Equal(TimeSpan.FromMilliseconds(250), Api.Wait(250));
    }
}
