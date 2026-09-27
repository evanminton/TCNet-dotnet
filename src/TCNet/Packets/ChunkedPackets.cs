using TCNet.Text;

namespace TCNet;

/// <summary>
/// Data split over packets: Data Size (26, total of all packets), Total Packets (30), Packet No (34),
/// Data Cluster Size (38, reserved in small waveform) and data from 42.
/// </summary>
public abstract class ChunkedPacket : DataPacket
{
    public uint TotalSize { get; set; }
    public uint TotalPackets { get; set; } = 1;
    public uint PacketNumber { get; set; }
    public uint ClusterSize { get; set; }
    public byte[] Payload { get; set; } = [];

    /// <summary>Standard bytes per packet for this data type.</summary>
    public abstract int StandardCluster { get; }

    protected virtual bool HasClusterField => true;

    public override int Length => TCNetConstants.PayloadOffset + Payload.Length;

    protected override void EncodeData(Span<byte> p)
    {
        Wire.PutU32(p, 26, TotalSize == 0 ? (uint)Payload.Length : TotalSize);
        Wire.PutU32(p, 30, TotalPackets);
        Wire.PutU32(p, 34, PacketNumber);
        if (HasClusterField) Wire.PutU32(p, 38, ClusterSize == 0 ? (uint)StandardCluster : ClusterSize);
        Payload.AsSpan(0, Math.Min(Payload.Length, p.Length - 42)).CopyTo(p[42..]);
    }

    protected override void DecodeData(ReadOnlySpan<byte> p, int datagramLength)
    {
        TotalSize = Wire.U32(p, 26);
        TotalPackets = Wire.U32(p, 30);
        PacketNumber = Wire.U32(p, 34);
        ClusterSize = HasClusterField ? Wire.U32(p, 38) : 0;
        Payload = p.Slice(42, Math.Max(0, datagramLength - 42)).ToArray();
    }

    protected override void DescribeData(List<TCNetField> f)
    {
        f.Add(new(26, 4, "Data Size", TotalSize.ToString(), "all packets together"));
        f.Add(new(30, 4, "Total Packets", TotalPackets.ToString()));
        f.Add(new(34, 4, "Packet No", PacketNumber.ToString()));
        f.Add(new(38, 4, HasClusterField ? "Data Cluster Size" : "RESERVED", ClusterSize.ToString()));
        DescribePayload(f);
    }

    protected virtual void DescribePayload(List<TCNetField> f) =>
        f.Add(new(42, Payload.Length, "Data", TCNetUnits.Bytes(Payload.Length), Wire.Hex(Payload, 16)));

    public override string Summary => $"L{TCNetText.LayerName(LayerId)} packet {PacketNumber} of {TotalPackets}, {Payload.Length}/{TotalSize} bytes";

    /// <summary>Splits data into packets of at most <paramref name="cluster"/> bytes, numbered from 0.</summary>
    public static List<T> Split<T>(ReadOnlySpan<byte> data, byte layer, Func<T> create, int cluster = 0) where T : ChunkedPacket
    {
        var first = create();
        if (cluster <= 0) cluster = first.StandardCluster;
        int total = Math.Max(1, (data.Length + cluster - 1) / cluster);
        var list = new List<T>(total);
        for (int i = 0; i < total; i++)
        {
            var pkt = i == 0 ? first : create();
            int start = i * cluster;
            pkt.LayerId = layer;
            pkt.TotalSize = (uint)data.Length;
            pkt.TotalPackets = (uint)total;
            pkt.PacketNumber = (uint)i;
            pkt.ClusterSize = (uint)cluster;
            pkt.Payload = data.Slice(start, Math.Min(cluster, data.Length - start)).ToArray();
            list.Add(pkt);
        }
        return list;
    }
}

/// <summary>Data type 8 · Beat Grid. 8-byte entries at OFFSET = beat × 8 − packet × 2400.</summary>
public sealed class BeatGridPacket : ChunkedPacket
{
    public override DataType DataType => DataType.BeatGrid;
    public override string Name => "Data · Beat Grid";
    public override int StandardCluster => TCNetConstants.BeatGridCluster;

    public IReadOnlyList<Beat> Beats => BeatGrid.Decode(Payload).Beats;

    protected override void DescribePayload(List<TCNetField> f)
    {
        base.DescribePayload(f);
        var beats = Beats;
        for (int i = 0; i < Math.Min(8, beats.Count); i++)
        {
            var b = beats[i];
            f.Add(new(42 + i * 8, 8, $"Beat {b.Number}", $"{b.TimeMs} ms", $"{TCNetText.Describe(b.Type)}, {TCNetUnits.Ms(b.TimeMs)}"));
        }
        if (beats.Count > 8) f.Add(new(42 + 64, 0, "…", $"{beats.Count - 8} more beats"));
    }
}

/// <summary>Data type 16 · Small Wave Form (2442 bytes): 1200 × (level, colour).</summary>
public sealed class SmallWaveformPacket : ChunkedPacket
{
    public SmallWaveformPacket()
    {
        Payload = new byte[TCNetConstants.SmallWaveformDataLength];
        TotalSize = TCNetConstants.SmallWaveformDataLength;
    }

    public override DataType DataType => DataType.SmallWaveform;
    public override string Name => "Data · Small Waveform";
    public override int StandardCluster => TCNetConstants.SmallWaveformDataLength;
    public override int Length => TCNetConstants.SmallWaveformLength;
    protected override bool HasClusterField => false;

    public Waveform Waveform => Waveform.Decode(Payload);

    public void SetBars(IReadOnlyList<WaveformBar> bars)
    {
        var data = new byte[TCNetConstants.SmallWaveformDataLength];
        for (int i = 0; i < Math.Min(1200, bars.Count); i++)
        {
            data[2 * i] = bars[i].Level;
            data[2 * i + 1] = bars[i].Color;
        }
        Payload = data;
        TotalSize = (uint)data.Length;
    }

    protected override void DecodeData(ReadOnlySpan<byte> p, int datagramLength)
    {
        base.DecodeData(p, Math.Max(datagramLength, TCNetConstants.SmallWaveformLength));
        if (Payload.Length > TCNetConstants.SmallWaveformDataLength) Payload = Payload[..TCNetConstants.SmallWaveformDataLength];
    }

    protected override void DescribePayload(List<TCNetField> f)
    {
        var w = Waveform;
        f.Add(new(42, 2400, "Waveform Data", $"{w.Bars.Count} bars", $"level (1st byte) / colour (2nd byte), peak {w.Peak}"));
    }
}

/// <summary>Data type 32 · Big Wave Form (4800 bytes per packet).</summary>
public sealed class BigWaveformPacket : ChunkedPacket
{
    public override DataType DataType => DataType.BigWaveform;
    public override string Name => "Data · Big Waveform";
    public override int StandardCluster => TCNetConstants.FileCluster;
}

/// <summary>Type 204 · Data File, data type 128 · Low Res Artwork (JPEG, 4800 bytes per packet).</summary>
public sealed class ArtworkPacket : ChunkedPacket
{
    public override MessageType MessageType => MessageType.DataFile;
    public override DataType DataType => DataType.LowResArtwork;
    public override string Name => "Data File · Low Res Artwork";
    public override int StandardCluster => TCNetConstants.FileCluster;

    protected override void DescribePayload(List<TCNetField> f) =>
        f.Add(new(42, Payload.Length, "File Data", TCNetUnits.Bytes(Payload.Length),
            Payload.Length >= 2 && Payload[0] == 0xFF && Payload[1] == 0xD8 ? "JPEG start (FF D8)" : null));
}

public readonly record struct Beat(ushort Number, BeatType Type, uint TimeMs);

/// <summary>Decoded beat grid.</summary>
public sealed class BeatGrid(IReadOnlyList<Beat> beats)
{
    public IReadOnlyList<Beat> Beats { get; } = beats;

    public static BeatGrid Decode(ReadOnlySpan<byte> data)
    {
        var list = new List<Beat>(data.Length / 8);
        for (int o = 0; o + 8 <= data.Length; o += 8)
        {
            var b = new Beat(Wire.U16(data, o), (BeatType)data[o + 2], Wire.U32(data, o + 4));
            if (b.Number == 0 && b.TimeMs == 0 && b.Type == BeatType.None) continue;
            list.Add(b);
        }
        return new BeatGrid(list);
    }

    /// <summary>Entries placed at beat number × 8.</summary>
    public byte[] Encode()
    {
        int max = Beats.Count == 0 ? -1 : Beats.Max(b => b.Number);
        var data = new byte[(max + 1) * 8];
        foreach (var b in Beats)
        {
            int o = b.Number * 8;
            Wire.PutU16(data, o, b.Number);
            data[o + 2] = (byte)b.Type;
            Wire.PutU32(data, o + 4, b.TimeMs);
        }
        return data;
    }

    /// <summary>Last beat at or before a position.</summary>
    public Beat? At(uint ms)
    {
        Beat? hit = null;
        foreach (var b in Beats)
        {
            if (b.TimeMs > ms) break;
            hit = b;
        }
        return hit;
    }
}

/// <summary>A waveform bar: level (first byte of each pair) and colour intensity (second).</summary>
public readonly record struct WaveformBar(byte Level, byte Color)
{
    /// <summary>Spec's blue look: R = G = colour, B = 255.</summary>
    public (byte R, byte G, byte B) Blue => (Color, Color, 255);

    /// <summary>Spec's green look: R = colour, G = 255, B = colour.</summary>
    public (byte R, byte G, byte B) Green => (Color, 255, Color);
}

public sealed class Waveform(IReadOnlyList<WaveformBar> bars)
{
    public IReadOnlyList<WaveformBar> Bars { get; } = bars;
    public byte Peak => Bars.Count == 0 ? (byte)0 : Bars.Max(b => b.Level);

    public static Waveform Decode(ReadOnlySpan<byte> data)
    {
        var bars = new WaveformBar[data.Length / 2];
        for (int i = 0; i < bars.Length; i++) bars[i] = new WaveformBar(data[2 * i], data[2 * i + 1]);
        return new Waveform(bars);
    }

    public byte[] Encode()
    {
        var d = new byte[Bars.Count * 2];
        for (int i = 0; i < Bars.Count; i++) { d[2 * i] = Bars[i].Level; d[2 * i + 1] = Bars[i].Color; }
        return d;
    }
}
