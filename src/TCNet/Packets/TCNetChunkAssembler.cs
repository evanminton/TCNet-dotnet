using System.Net;

namespace TCNet;

/// <summary>Complete data reassembled from one or more chunked packets.</summary>
public sealed record AssembledData(
    MessageType MessageType,
    DataType DataType,
    byte Layer,
    byte[] Data,
    int PacketCount,
    EndPoint? Source,
    ushort NodeId,
    string NodeName)
{
    /// <summary>For application data: the application code; otherwise 0.</summary>
    public ushort ApplicationCode { get; init; }

    public BeatGrid AsBeatGrid() => BeatGrid.Decode(Data);
    public Waveform AsWaveform() => Waveform.Decode(Data);

    /// <summary>True when the data starts with a JPEG SOI marker.</summary>
    public bool IsJpeg => Data.Length >= 2 && Data[0] == 0xFF && Data[1] == 0xD8;
}

/// <summary>
/// Collects chunked packets (beat grid, waveforms, artwork, application data) until all
/// <c>Total Packets</c> have arrived, then concatenates them in packet-number order.
/// Works whether the sender numbers packets from 0 or from 1.
/// </summary>
public sealed class TCNetChunkAssembler
{
    private readonly record struct Key(string Source, ushort NodeId, MessageType Type, byte DataType, byte Layer);

    private sealed class Pending
    {
        public readonly SortedDictionary<uint, byte[]> Parts = new();
        public uint Total;
        public uint DataSize;
        public long Bytes;
        public DateTime Updated;
    }

    private readonly Dictionary<Key, Pending> _pending = new();
    private readonly object _gate = new();

    /// <summary>Incomplete transfers older than this are dropped.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Transfers declaring more packets than this are ignored.</summary>
    public uint MaxTotalPackets { get; set; } = 4096;

    /// <summary>Transfers declaring or accumulating more bytes than this are dropped.</summary>
    public long MaxTransferBytes { get; set; } = 16 * 1024 * 1024;

    /// <summary>Maximum number of transfers in progress; the oldest is dropped to make room.</summary>
    public int MaxPendingTransfers { get; set; } = 256;

    /// <summary>Number of transfers in progress.</summary>
    public int PendingCount { get { lock (_gate) return _pending.Count; } }

    /// <summary>Adds a packet. Returns the assembled data when this packet completes a transfer.</summary>
    public AssembledData? Add(TCNetPacket packet, EndPoint? source = null)
    {
        uint total, number, dataSize;
        byte dataType, layer;
        byte[] payload;
        ushort appCode = 0;

        switch (packet)
        {
            case ChunkedDataPacket c:
                total = c.TotalPackets; number = c.PacketNumber; dataSize = c.DataSize;
                dataType = (byte)c.DataType; layer = c.LayerId; payload = c.Payload;
                break;
            case ApplicationDataPacket a:
                total = a.TotalPackets; number = a.PacketNumber; dataSize = a.DataSize;
                dataType = a.DataIdentifier1; layer = a.DataIdentifier2; payload = a.Payload; appCode = a.ApplicationCode;
                break;
            default:
                return null;
        }

        if (total == 0) total = 1;
        // Numbering may be 0-based (0..total-1) or 1-based (1..total); anything else is bogus.
        if (total > MaxTotalPackets || number > total || dataSize > MaxTransferBytes) return null;

        var now = DateTime.UtcNow;
        var key = new Key(source?.ToString() ?? "", packet.NodeId, packet.MessageType, dataType, layer);

        lock (_gate)
        {
            Prune(now);
            bool restart = !_pending.TryGetValue(key, out var pend) || pend.Total != total || pend.DataSize != dataSize;
            // A repeated packet number with different content means a new transfer has started.
            if (!restart && pend!.Parts.TryGetValue(number, out var existing) && !existing.AsSpan().SequenceEqual(payload))
                restart = true;
            if (restart)
            {
                if (pend is null) MakeRoom();
                pend = new Pending { Total = total, DataSize = dataSize };
                _pending[key] = pend;
            }

            if (pend!.Parts.TryGetValue(number, out var old)) pend.Bytes -= old.Length;
            pend.Parts[number] = payload;
            pend.Bytes += payload.Length;
            pend.Updated = now;

            if (pend.Bytes > MaxTransferBytes)
            {
                _pending.Remove(key);
                return null;
            }

            if (pend.Parts.Count < total) return null;
            uint first = pend.Parts.Keys.First(), last = pend.Parts.Keys.Last();
            if (pend.Parts.Count > total || last - first != total - 1)
            {
                // Mixed 0- and 1-based numbering (0..total all present): keep only this packet.
                if (pend.Parts.Count > total)
                {
                    pend.Parts.Clear();
                    pend.Parts[number] = payload;
                    pend.Bytes = payload.Length;
                }
                return null;
            }
            _pending.Remove(key);

            var data = new byte[pend.Bytes];
            int offset = 0;
            foreach (var part in pend.Parts.Values)
            {
                part.CopyTo(data, offset);
                offset += part.Length;
            }
            // Trim padding beyond the declared size (e.g. a zero-filled last cluster).
            if (dataSize > 0 && dataSize < data.Length) data = data[..(int)dataSize];

            var type = packet is DataPacket dp ? dp.DataType : (DataType)0;
            return new AssembledData(packet.MessageType, type, layer, data, pend.Parts.Count, source, packet.NodeId, packet.NodeName)
            {
                ApplicationCode = appCode,
            };
        }
    }

    /// <summary>Drops all partial transfers.</summary>
    public void Clear()
    {
        lock (_gate) _pending.Clear();
    }

    private void Prune(DateTime now)
    {
        List<Key>? stale = null;
        foreach (var (k, v) in _pending)
            if (now - v.Updated > Timeout) (stale ??= []).Add(k);
        if (stale is null) return;
        foreach (var k in stale) _pending.Remove(k);
    }

    private void MakeRoom()
    {
        while (_pending.Count >= Math.Max(1, MaxPendingTransfers))
            _pending.Remove(_pending.MinBy(kv => kv.Value.Updated).Key);
    }
}
