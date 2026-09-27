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
/// from 0 or from 1. Memory is bounded per transfer and in total.
/// </summary>
/// <remarks>
/// <para>A transfer (per source, node, type, layer and application code) starts over, dropping the parts it holds, when a
/// packet clearly belongs to a newer transfer: a different Total Packets or Data Size, a number it already holds with
/// different content, more than <see cref="MaxPacketGap"/> since its previous packet arrived, or a header Timestamp more
/// than <see cref="SenderTimestampWindow"/> from its previous packet's (on the sender's 1 s timer). The packets of one
/// transfer are sent back to back, so reordering within a transfer still completes.</para>
/// <para>Numbering: a transfer holding part 0 completes only with 0 … total − 1 (a part numbered total is stray and
/// ignored); otherwise 1 … total completes it. The base of a sender's first completed transfer is locked for its later
/// transfers of the same type, so a stray part numbered total cannot stand in for a lost part 0.</para>
/// </remarks>
public sealed class ChunkAssembler
{
    private sealed class Transfer
    {
        public required uint Total;
        public required uint Size;
        public readonly SortedDictionary<uint, byte[]> Parts = new();
        public long Bytes;
        public DateTime Touched;
        public uint Stamp;
    }

    private readonly record struct Key(string Source, ushort Node, MessageType Type, byte Data, byte Layer, ushort App);

    private const int MaxLockedBases = 1024;

    private readonly Dictionary<Key, Transfer> _open = new();
    private readonly Dictionary<Key, uint> _base = new();
    private readonly object _gate = new();
    private long _buffered;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
    public int MaxTotalPackets { get; set; } = 4096;

    /// <summary>Largest single transfer. Artwork and beat grids are well under 1 MB; big waveforms of long tracks a few MB at most.</summary>
    public long MaxTransferBytes { get; set; } = 4 * 1024 * 1024;

    /// <summary>All open transfers together; the least recently touched are evicted to stay under it.</summary>
    public long MaxBufferedBytes { get; set; } = 32 * 1024 * 1024;

    public int MaxPendingTransfers { get; set; } = 64;

    /// <summary>A packet arriving longer than this after the transfer's previous packet starts a new transfer.</summary>
    public TimeSpan MaxPacketGap { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// A header Timestamp further than this from the transfer's previous packet's (modulo the 1 s timer) starts a new
    /// transfer. Catches transfers read back to back from a socket buffer. Zero disables.
    /// </summary>
    public TimeSpan SenderTimestampWindow { get; set; } = TimeSpan.FromMilliseconds(250);

    public int Pending { get { lock (_gate) return _open.Count; } }

    /// <summary>Payload bytes held by open transfers.</summary>
    public long BufferedBytes { get { lock (_gate) return _buffered; } }

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
        var baseKey = key with { Layer = 0 };

        lock (_gate)
        {
            Prune(now);
            if (_base.TryGetValue(baseKey, out uint locked) && number == (locked == 0 ? total : 0)) return null;

            if (!_open.TryGetValue(key, out var t) || StartsNewTransfer(t, packet.Timestamp, total, size, number, payload, now))
            {
                if (t is not null) Remove(key);
                else if (_open.Count >= MaxPendingTransfers) EvictOldest(key);
                t = new Transfer { Total = total, Size = size };
                _open[key] = t;
            }
            t.Touched = now;
            t.Stamp = packet.Timestamp;

            if (number == total && t.Parts.ContainsKey(0)) return null;
            if (!t.Parts.ContainsKey(number))
            {
                if (t.Bytes + payload.Length > MaxTransferBytes)
                {
                    Remove(key);
                    return null;
                }
                while (_buffered + payload.Length > MaxBufferedBytes && _open.Count > 1) EvictOldest(key);
                if (_buffered + payload.Length > MaxBufferedBytes)
                {
                    Remove(key);
                    return null;
                }
                if (number == 0 && t.Parts.Remove(total, out var stray))
                {
                    t.Bytes -= stray.Length;
                    _buffered -= stray.Length;
                }
                t.Parts[number] = payload;
                t.Bytes += payload.Length;
                _buffered += payload.Length;
            }

            if (t.Parts.Count < total || CompleteBase(t) is not { } first) return null;
            Remove(key);
            if (_base.Count >= MaxLockedBases) _base.Clear();
            _base[baseKey] = first;

            var parts = OrderedParts(t, first);
            var result = new byte[parts.Sum(p => p.Length)];
            int at = 0;
            foreach (var p in parts) { p.CopyTo(result, at); at += p.Length; }
            if (size > 0 && size < result.Length) result = result[..(int)size];

            return new AssembledData(packet.MessageType, dataType, layer, result, parts.Count, source, packet.NodeId, packet.NodeName, app);
        }
    }

    private bool StartsNewTransfer(Transfer t, uint stamp, uint total, uint size, uint number, byte[] payload, DateTime now)
    {
        if (t.Total != total || t.Size != size) return true;
        if (t.Parts.TryGetValue(number, out var old) && !old.AsSpan().SequenceEqual(payload)) return true;
        if (now - t.Touched > MaxPacketGap) return true;
        return SenderTimestampWindow > TimeSpan.Zero && StampDistance(t.Stamp, stamp) > SenderTimestampWindow.TotalMicroseconds;
    }

    /// <summary>Distance between two header timestamps on the sender's 0–999999 µs timer.</summary>
    private static uint StampDistance(uint a, uint b)
    {
        const uint wrap = TCNetConstants.MicrosPerSecond;
        uint d = (a % wrap + wrap - b % wrap) % wrap;
        return Math.Min(d, wrap - d);
    }

    /// <summary>First number of a complete transfer (0 when part 0 is held, else 1), or null while parts are missing.</summary>
    private static uint? CompleteBase(Transfer t)
    {
        uint first = t.Parts.ContainsKey(0) ? 0u : 1u;
        for (uint i = first; i < first + t.Total; i++)
            if (!t.Parts.ContainsKey(i)) return null;
        return first;
    }

    private static List<byte[]> OrderedParts(Transfer t, uint first)
    {
        var list = new List<byte[]>((int)t.Total);
        for (uint i = first; i < first + t.Total; i++) list.Add(t.Parts[i]);
        return list;
    }

    /// <summary>Drops open transfers and the numbering bases learned from senders.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _open.Clear();
            _base.Clear();
            _buffered = 0;
        }
    }

    private void Remove(Key key)
    {
        if (_open.Remove(key, out var t)) _buffered -= t.Bytes;
    }

    private void Prune(DateTime now)
    {
        foreach (var k in _open.Where(kv => now - kv.Value.Touched > Timeout).Select(kv => kv.Key).ToList()) Remove(k);
    }

    private void EvictOldest(Key keep)
    {
        var oldest = _open.Where(kv => kv.Key != keep).MinBy(kv => kv.Value.Touched);
        if (oldest.Value is not null) Remove(oldest.Key);
    }
}
