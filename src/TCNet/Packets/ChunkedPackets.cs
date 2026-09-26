using TCNet.Text;

namespace TCNet;

/// <summary>
/// Data packets whose content may span several datagrams: Data Size (26–29, total of all packets),
/// Total Packets (30–33), Packet No (34–37), Data Cluster Size (38–41) and data from byte 42.
/// </summary>
public abstract class ChunkedDataPacket : DataPacket
{
    /// <summary>Total data size across all packets.</summary>
    public uint DataSize { get; set; }

    /// <summary>Number of packets used for the data.</summary>
    public uint TotalPackets { get; set; } = 1;

    /// <summary>This packet's number (this library sends 0-based; reassembly accepts either base).</summary>
    public uint PacketNumber { get; set; }

    /// <summary>Data cluster size (bytes per packet). Reserved/zero in small waveform packets.</summary>
    public uint ClusterSize { get; set; }

    /// <summary>This packet's slice of data (bytes 42…).</summary>
    public byte[] Payload { get; set; } = [];

    /// <summary>Whether bytes 38–41 carry the cluster size (false for small waveform, where they are reserved).</summary>
    protected virtual bool HasClusterSize => true;

    /// <summary>Standard cluster size used by <see cref="Split{T}"/>.</summary>
    public abstract int StandardClusterSize { get; }

    public override int Length => TCNetConstants.PayloadHeaderSize + Payload.Length;

    protected override void WriteData(Span<byte> p)
    {
        Wire.U32(p, 26, DataSize == 0 ? (uint)Payload.Length : DataSize);
        Wire.U32(p, 30, TotalPackets);
        Wire.U32(p, 34, PacketNumber);
        if (HasClusterSize) Wire.U32(p, 38, ClusterSize == 0 ? (uint)StandardClusterSize : ClusterSize);
        Payload.CopyTo(p[42..]);
    }

    protected override void ReadData(ReadOnlySpan<byte> p, int receivedLength)
    {
        DataSize = Wire.U32(p, 26);
        TotalPackets = Wire.U32(p, 30);
        PacketNumber = Wire.U32(p, 34);
        ClusterSize = HasClusterSize ? Wire.U32(p, 38) : 0;
        Payload = p.Slice(42, Math.Max(0, receivedLength - 42)).ToArray();
    }

    protected override void DescribeData(List<TCNetField> f)
    {
        f.Add(new(26, 4, "Data Size", DataSize.ToString()));
        f.Add(new(30, 4, "Total Packets", TotalPackets.ToString()));
        f.Add(new(34, 4, "Packet No", PacketNumber.ToString()));
        f.Add(new(38, 4, HasClusterSize ? "Data Cluster Size" : "RESERVED", ClusterSize.ToString()));
        DescribePayload(f);
    }

    protected virtual void DescribePayload(List<TCNetField> f) =>
        f.Add(new(42, Payload.Length, "Data", $"{Payload.Length} bytes", Convert.ToHexString(Payload.AsSpan(0, Math.Min(16, Payload.Length)))));

    public override string Summary => $"L{TCNetText.LayerName(LayerId)} packet {PacketNumber}/{TotalPackets}, {Payload.Length} of {DataSize} bytes";

    /// <summary>Splits <paramref name="data"/> into packets of at most <paramref name="clusterSize"/> bytes (0-based numbering).</summary>
    public static List<T> Split<T>(byte[] data, byte layer, Func<T> create, int clusterSize = 0) where T : ChunkedDataPacket
    {
        var list = new List<T>();
        var probe = create();
        if (clusterSize <= 0) clusterSize = probe.StandardClusterSize;
        uint total = (uint)Math.Max(1, (data.Length + clusterSize - 1) / clusterSize);
        for (uint i = 0; i < total; i++)
        {
            var pkt = i == 0 ? probe : create();
            int start = (int)i * clusterSize;
            int len = Math.Min(clusterSize, data.Length - start);
            pkt.LayerId = layer;
            pkt.DataSize = (uint)data.Length;
            pkt.TotalPackets = total;
            pkt.PacketNumber = i;
            pkt.ClusterSize = (uint)clusterSize;
            pkt.Payload = len > 0 ? data.AsSpan(start, len).ToArray() : [];
            list.Add(pkt);
        }
        return list;
    }
}

/// <summary>Data type 8 – Beat Grid Data. 8-byte entries; max 2400 data bytes per packet.</summary>
public sealed class BeatGridDataPacket : ChunkedDataPacket
{
    public override DataType DataType => DataType.BeatGrid;
    public override string Name => "Data – Beat Grid";
    public override int StandardClusterSize => TCNetConstants.BeatGridClusterSize;

    /// <summary>Entries contained in this packet alone.</summary>
    public IReadOnlyList<BeatGridEntry> Entries => BeatGrid.Decode(Payload).Entries;

    protected override void DescribePayload(List<TCNetField> f)
    {
        base.DescribePayload(f);
        var entries = Entries;
        foreach (var e in entries.Take(8))
            f.Add(new(-1, 8, $"Beat {e.BeatNumber}", $"{e.TimestampMs} ms", TCNetText.Describe(e.Type)));
        if (entries.Count > 8) f.Add(new(-1, 0, "…", $"{entries.Count - 8} more beats"));
    }
}

/// <summary>Data type 16 – Small Wave Form (2442 bytes): 1200 bars of (level, colour).</summary>
public sealed class SmallWaveformPacket : ChunkedDataPacket
{
    public SmallWaveformPacket()
    {
        Payload = new byte[TCNetConstants.SmallWaveformDataSize];
        DataSize = TCNetConstants.SmallWaveformDataSize;
    }

    public override DataType DataType => DataType.SmallWaveform;
    public override string Name => "Data – Small Waveform";
    public override int StandardClusterSize => TCNetConstants.SmallWaveformDataSize;
    protected override bool HasClusterSize => false;
    public override int Length => TCNetConstants.SmallWaveformSize;

    public Waveform Waveform => Waveform.Decode(Payload);

    protected override void WriteData(Span<byte> p)
    {
        if (Payload.Length != TCNetConstants.SmallWaveformDataSize)
        {
            var fixedSize = new byte[TCNetConstants.SmallWaveformDataSize];
            Payload.AsSpan(0, Math.Min(Payload.Length, fixedSize.Length)).CopyTo(fixedSize);
            Payload = fixedSize;
        }
        base.WriteData(p);
    }

    protected override void ReadData(ReadOnlySpan<byte> p, int receivedLength)
    {
        base.ReadData(p, Math.Max(receivedLength, TCNetConstants.SmallWaveformSize));
        if (Payload.Length > TCNetConstants.SmallWaveformDataSize) Payload = Payload[..TCNetConstants.SmallWaveformDataSize];
    }

    /// <summary>Sets the 1200 bars (shorter input is zero padded).</summary>
    public void SetBars(IReadOnlyList<WaveformBar> bars)
    {
        var data = new byte[TCNetConstants.SmallWaveformDataSize];
        for (int i = 0; i < Math.Min(bars.Count, 1200); i++)
        {
            data[2 * i] = bars[i].Level;
            data[2 * i + 1] = bars[i].Color;
        }
        Payload = data;
        DataSize = (uint)data.Length;
    }

    protected override void DescribePayload(List<TCNetField> f)
    {
        var w = Waveform;
        f.Add(new(42, 2400, "Waveform Data", $"{w.Bars.Count} bars", $"peak level {w.PeakLevel}"));
    }
}

/// <summary>Data type 32 – Big Wave Form. Level/colour pairs spread over packets of 4800 bytes.</summary>
public sealed class BigWaveformPacket : ChunkedDataPacket
{
    public override DataType DataType => DataType.BigWaveform;
    public override string Name => "Data – Big Waveform";
    public override int StandardClusterSize => TCNetConstants.BigWaveformClusterSize;
}

/// <summary>Type 204 – Data File, data type 128 – Low Res Artwork (JPEG bytes over packets of 4800 bytes).</summary>
public sealed class LowResArtworkPacket : ChunkedDataPacket
{
    public override MessageType MessageType => MessageType.DataFile;
    public override DataType DataType => DataType.LowResArtwork;
    public override string Name => "Data File – Low Res Artwork";
    public override int StandardClusterSize => TCNetConstants.ArtworkClusterSize;

    protected override void DescribePayload(List<TCNetField> f)
    {
        bool jpegStart = Payload.Length >= 2 && Payload[0] == 0xFF && Payload[1] == 0xD8;
        f.Add(new(42, Payload.Length, "File Data", $"{Payload.Length} bytes", jpegStart ? "JPEG start (FF D8)" : null));
    }
}

/// <summary>One beat grid entry: beat number, type (20 downbeat / 10 upbeat) and timestamp in ms.</summary>
public readonly record struct BeatGridEntry(ushort BeatNumber, BeatType Type, uint TimestampMs);

/// <summary>A decoded beat grid.</summary>
public sealed class BeatGrid
{
    public BeatGrid(IReadOnlyList<BeatGridEntry> entries) => Entries = entries;

    public IReadOnlyList<BeatGridEntry> Entries { get; }

    /// <summary>Decodes 8-byte entries, skipping all-zero slots.</summary>
    public static BeatGrid Decode(ReadOnlySpan<byte> data)
    {
        var list = new List<BeatGridEntry>(data.Length / 8);
        for (int o = 0; o + 8 <= data.Length; o += 8)
        {
            var e = new BeatGridEntry(Wire.U16(data, o), (BeatType)data[o + 2], Wire.U32(data, o + 4));
            if (e.BeatNumber == 0 && e.TimestampMs == 0 && e.Type == BeatType.Unknown) continue;
            list.Add(e);
        }
        return new BeatGrid(list);
    }

    /// <summary>Encodes entries at index = beat number × 8 (matching the spec's OFFSET formula).</summary>
    public byte[] Encode()
    {
        int max = Entries.Count == 0 ? 0 : Entries.Max(e => e.BeatNumber);
        var data = new byte[(max + 1) * 8];
        foreach (var e in Entries)
        {
            int o = e.BeatNumber * 8;
            Wire.U16(data, o, e.BeatNumber);
            data[o + 2] = (byte)e.Type;
            Wire.U32(data, o + 4, e.TimestampMs);
        }
        return data;
    }

    /// <summary>The last beat at or before <paramref name="positionMs"/>.</summary>
    public BeatGridEntry? BeatAt(uint positionMs)
    {
        BeatGridEntry? found = null;
        foreach (var e in Entries)
        {
            if (e.TimestampMs > positionMs) break;
            found = e;
        }
        return found;
    }
}

/// <summary>One waveform bar: level (odd, i.e. first byte of each pair) and colour intensity (second byte).</summary>
public readonly record struct WaveformBar(byte Level, byte Color)
{
    /// <summary>Spec suggestion for a blue (Pioneer-like) look: R = G = colour, B = 255.</summary>
    public (byte R, byte G, byte B) BlueRgb => (Color, Color, 255);

    /// <summary>Spec suggestion for a green look: R = colour, G = 255, B = colour.</summary>
    public (byte R, byte G, byte B) GreenRgb => (Color, 255, Color);
}

/// <summary>A decoded small or big waveform.</summary>
public sealed class Waveform
{
    public Waveform(IReadOnlyList<WaveformBar> bars) => Bars = bars;

    public IReadOnlyList<WaveformBar> Bars { get; }

    public byte PeakLevel => Bars.Count == 0 ? (byte)0 : Bars.Max(b => b.Level);

    public static Waveform Decode(ReadOnlySpan<byte> data)
    {
        var bars = new WaveformBar[data.Length / 2];
        for (int i = 0; i < bars.Length; i++) bars[i] = new WaveformBar(data[2 * i], data[2 * i + 1]);
        return new Waveform(bars);
    }

    public byte[] Encode()
    {
        var data = new byte[Bars.Count * 2];
        for (int i = 0; i < Bars.Count; i++) { data[2 * i] = Bars[i].Level; data[2 * i + 1] = Bars[i].Color; }
        return data;
    }
}
