using System.Net;

namespace TCNet;

/// <summary>Data put back together from one or more chunked packets.</summary>
public sealed record AssembledData(
    MessageType MessageType,
    DataType DataType,
    byte Layer,
    byte[] Data,
    int Packets,
    EndPoint? Source,
    ushort NodeId,
    string NodeName,
    ushort ApplicationCode = 0)
{
    public BeatGrid ToBeatGrid() => BeatGrid.Decode(Data);
    public Waveform ToWaveform() => Waveform.Decode(Data);
    public bool IsJpeg => Data.Length >= 2 && Data[0] == 0xFF && Data[1] == 0xD8;
}

/// <summary>
/// Reassembles beat grid, waveform, artwork and application data. Packets may arrive in any order and be numbered
/// from 0 or from 1; a transfer completes when every number of one of those ranges has arrived. Memory is bounded.
/// </summary>
public sealed class ChunkAssembler
{
    private sealed class Transfer
    {
        public required uint Total;
        public required uint Size;
        public readonly SortedDictionary<uint, byte[]> Parts = new();
        public long Bytes;
        public DateTime Touched;
    }

    private readonly record struct Key(string Source, ushort Node, MessageType Type, byte Data, byte Layer, ushort App);

    private readonly Dictionary<Key, Transfer> _open = new();
    private readonly object _gate = new();

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
    public int MaxTotalPackets { get; set; } = 4096;
    public long MaxTransferBytes { get; set; } = 16 * 1024 * 1024;
    public int MaxPendingTransfers { get; set; } = 64;

    public int Pending { get { lock (_gate) return _open.Count; } }

    /// <summary>Adds a chunk; returns the data when this chunk completes a transfer.</summary>
    public AssembledData? Add(TCNetPacket packet, EndPoint? source = null)
    {
        uint total, number, size;
        byte data, layer;
        byte[] payload;
        ushort app = 0;
        DataType dataType = 0;
        switch (packet)
        {
            case ChunkedPacket c:
                (total, number, size, data, layer, payload, dataType) = (c.TotalPackets, c.PacketNumber, c.TotalSize, (byte)c.DataType, c.LayerId, c.Payload, c.DataType);
                break;
            case ApplicationDataPacket a:
                (total, number, size, data, layer, payload, app) = (a.TotalPackets, a.PacketNumber, a.TotalSize, a.Identifier1, a.Identifier2, a.Payload, a.ApplicationCode);
                break;
            default:
                return null;
        }

        if (total == 0) total = 1;
        if (total > MaxTotalPackets || number > total || payload.Length > MaxTransferBytes) return null;

        var now = DateTime.UtcNow;
        var key = new Key(source?.ToString() ?? "", packet.NodeId, packet.MessageType, data, layer, app);

        lock (_gate)
        {
            Prune(now);
            if (!_open.TryGetValue(key, out var t) || t.Total != total || t.Size != size ||
                (t.Parts.TryGetValue(number, out var old) && !old.AsSpan().SequenceEqual(payload)))
            {
                if (!_open.ContainsKey(key) && _open.Count >= MaxPendingTransfers) EvictOldest();
                t = new Transfer { Total = total, Size = size };
                _open[key] = t;
            }

            if (!t.Parts.ContainsKey(number))
            {
                t.Bytes += payload.Length;
                if (t.Bytes > MaxTransferBytes)
                {
                    _open.Remove(key);
                    return null;
                }
                t.Parts[number] = payload;
            }
            t.Touched = now;

            if (t.Parts.Count < total || !IsComplete(t)) return null;
            _open.Remove(key);

            var parts = OrderedParts(t);
            var result = new byte[parts.Sum(p => p.Length)];
            int at = 0;
            foreach (var p in parts) { p.CopyTo(result, at); at += p.Length; }
            if (size > 0 && size < result.Length) result = result[..(int)size];

            return new AssembledData(packet.MessageType, dataType, layer, result, parts.Count, source, packet.NodeId, packet.NodeName, app);
        }
    }

    private static bool IsComplete(Transfer t) => Covers(t, 0) || Covers(t, 1);

    private static bool Covers(Transfer t, uint first)
    {
        for (uint i = first; i < first + t.Total; i++)
            if (!t.Parts.ContainsKey(i)) return false;
        return true;
    }

    private static List<byte[]> OrderedParts(Transfer t)
    {
        uint first = Covers(t, 0) ? 0u : 1u;
        var list = new List<byte[]>((int)t.Total);
        for (uint i = first; i < first + t.Total; i++) list.Add(t.Parts[i]);
        return list;
    }

    public void Clear()
    {
        lock (_gate) _open.Clear();
    }

    private void Prune(DateTime now)
    {
        foreach (var k in _open.Where(kv => now - kv.Value.Touched > Timeout).Select(kv => kv.Key).ToList()) _open.Remove(k);
    }

    private void EvictOldest()
    {
        var oldest = _open.MinBy(kv => kv.Value.Touched);
        _open.Remove(oldest.Key);
    }
}
