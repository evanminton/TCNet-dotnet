using System.Net;

namespace TCNet.Tests;

public class ChunkAssemblerFixTests
{
    private static readonly IPEndPoint Src = new(IPAddress.Loopback, 65023);

    private static BigWaveformPacket Part(uint n, byte content, uint stamp = 0, uint total = 3) =>
        new() { TotalPackets = total, PacketNumber = n, TotalSize = total, Timestamp = stamp, Payload = [content] };

    [Fact]
    public void NewTransferBySenderTimestamp_DropsStaleParts()
    {
        var asm = new ChunkAssembler();
        Assert.Null(asm.Add(Part(0, 0xA0, 100_000), Src));
        Assert.Null(asm.Add(Part(1, 0xA1, 100_020), Src));
        // Transfer A lost part 2; B is read right after it, 500 ms later on the sender's clock.
        Assert.Null(asm.Add(Part(2, 0xB2, 600_000), Src));
        Assert.Null(asm.Add(Part(0, 0xB0, 600_010), Src));
        Assert.Equal(new byte[] { 0xB0, 0xB1, 0xB2 }, asm.Add(Part(1, 0xB1, 600_020), Src)!.Data);
    }

    [Fact]
    public void ArrivalGap_DropsStaleParts()
    {
        var asm = new ChunkAssembler { MaxPacketGap = TimeSpan.FromMilliseconds(30) };
        Assert.Null(asm.Add(Part(0, 0xA0), Src));
        Assert.Null(asm.Add(Part(1, 0xA1), Src));
        Thread.Sleep(100);
        Assert.Null(asm.Add(Part(2, 0xB2), Src));
        Assert.Null(asm.Add(Part(1, 0xB1), Src));
        Assert.Equal(new byte[] { 0xB0, 0xB1, 0xB2 }, asm.Add(Part(0, 0xB0), Src)!.Data);
    }

    [Fact]
    public void OutOfOrderAcrossTimerWrap_Completes()
    {
        var asm = new ChunkAssembler();
        Assert.Null(asm.Add(Part(2, 2, 5), Src));
        Assert.Null(asm.Add(Part(0, 0, 999_990), Src));
        Assert.Equal(new byte[] { 0, 1, 2 }, asm.Add(Part(1, 1, 999_995), Src)!.Data);
    }

    [Fact]
    public void ZeroBasedSender_StrayLastNumber_DoesNotReplaceLostPartZero()
    {
        var asm = new ChunkAssembler();
        Assert.Null(asm.Add(Part(0, 0), Src));
        Assert.Null(asm.Add(Part(1, 1), Src));
        Assert.NotNull(asm.Add(Part(2, 2), Src));

        // Next transfer: part 0 lost, a stray part numbered 3 (= total) arrives.
        Assert.Null(asm.Add(Part(3, 0xEE), Src));
        Assert.Null(asm.Add(Part(1, 0x11), Src));
        Assert.Null(asm.Add(Part(2, 0x22), Src));
        Assert.Equal(new byte[] { 0x00, 0x11, 0x22 }, asm.Add(Part(0, 0x00), Src)!.Data);
    }

    [Fact]
    public void HeldPartZero_StrayLastNumberIsDropped()
    {
        var asm = new ChunkAssembler();
        Assert.Null(asm.Add(Part(3, 0xEE), Src));
        Assert.Null(asm.Add(Part(0, 0), Src));
        Assert.Equal(1, asm.BufferedBytes);
        Assert.Null(asm.Add(Part(3, 0xEE), Src));
        Assert.Null(asm.Add(Part(1, 1), Src));
        Assert.Equal(new byte[] { 0, 1, 2 }, asm.Add(Part(2, 2), Src)!.Data);
        Assert.Equal(0, asm.BufferedBytes);
    }

    [Fact]
    public void Defaults_BoundMemory()
    {
        var asm = new ChunkAssembler();
        Assert.True(asm.MaxTransferBytes <= 4 * 1024 * 1024);
        Assert.True(asm.MaxBufferedBytes <= 32 * 1024 * 1024);
        Assert.True(asm.MaxBufferedBytes >= asm.MaxTransferBytes);
    }

    [Fact]
    public void GlobalByteCap_EvictsOldestTransfers()
    {
        var asm = new ChunkAssembler { MaxBufferedBytes = 10, MaxTransferBytes = 8 };
        BigWaveformPacket Half(byte layer, uint n) => new() { LayerId = layer, TotalPackets = 2, PacketNumber = n, Payload = new byte[4] };
        Assert.Null(asm.Add(Half(1, 0)));
        Assert.Null(asm.Add(Half(2, 0)));
        Assert.Null(asm.Add(Half(3, 0)));
        Assert.Equal(2, asm.Pending);
        Assert.Equal(8, asm.BufferedBytes);
        Assert.Null(asm.Add(Half(1, 1)));
        Assert.Equal(8, asm.BufferedBytes);
        Assert.NotNull(asm.Add(Half(3, 1)));
        Assert.True(asm.BufferedBytes <= 10);
        asm.Clear();
        Assert.Equal(0, asm.BufferedBytes);
    }
}

public class CueDataFixTests
{
    private static CueDataPacket Roundtrip(CueDataPacket p) => (CueDataPacket)TCNetPacket.Parse(p.ToArray());

    [Fact]
    public void LoopOut_RoundTrips_WithEmptyCues()
    {
        var values = Enumerable.Range(0, 70_001).Select(v => (uint)v)
            .Concat(Enumerable.Range(1, 65_535).Select(t => (uint)t << 8))
            .Concat([0x00FFFFFFu, 0x01020304u, 0x7F010100u, 0xFFFFFFFFu]);
        var p = new CueDataPacket();
        p.Cues[5].Type = 2;
        p.Cues[5].InMs = 1234;
        foreach (uint loopOut in values)
        {
            foreach (uint loopIn in new[] { 0u, loopOut / 2 })
            {
                p.LoopInMs = loopIn;
                p.LoopOutMs = loopOut;
                var r = Roundtrip(p);
                if (r.LoopOutMs != loopOut || r.LoopInMs != loopIn || !r.Cues[0].IsEmpty || r.LoopOutOverlapped)
                    Assert.Fail($"loop {loopIn}–{loopOut} read as {r.LoopInMs}–{r.LoopOutMs}, cue 1 type {r.Cues[0].Type} in {r.Cues[0].InMs}");
            }
        }
    }

    [Fact]
    public void LoopOut_MultipleOf256_IsNotCue1()
    {
        var r = Roundtrip(new CueDataPacket { LoopInMs = 0, LoopOutMs = 256 });
        Assert.Equal(256u, r.LoopOutMs);
        Assert.True(r.Cues[0].IsEmpty);
    }

    [Fact]
    public void Cue1_RoundTrips_IncludingSparseNearZero()
    {
        var p = new CueDataPacket();
        foreach (byte type in new byte[] { 0, 1, 2, 3, 5, 255 })
        foreach (uint outMs in new[] { 0u, 700u })
        foreach (var color in new[] { default, new CueColor(0, 0, 1) })
        for (uint inMs = 0; inMs <= 300; inMs++)
        foreach (uint loopOut in new[] { 0u, 256u, 5001u })
        {
            var c = p.Cues[0];
            (c.Type, c.InMs, c.OutMs, c.Color) = (type, inMs, outMs, color);
            if (c.IsEmpty) continue;
            p.LoopInMs = 17;
            p.LoopOutMs = loopOut;
            var r = Roundtrip(p);
            var d = r.Cues[0];
            if (d.Type != type || d.InMs != inMs || d.OutMs != outMs || d.Color != color || r.LoopInMs != 17 || !r.LoopOutOverlapped)
                Assert.Fail($"cue 1 {type}/{inMs}/{outMs}/{color} loop out {loopOut} read as {d.Type}/{d.InMs}/{d.OutMs}/{d.Color}");
        }
    }

    [Fact]
    public void Cue1_ZeroesByte46_AndKeepsPrintedOffsets()
    {
        var p = new CueDataPacket { LoopOutMs = 0x010203FF };
        p.Cues[0].Type = 1;
        var b = p.ToArray();
        Assert.Equal(0, b[46]);
        Assert.Equal(1, b[47]);
        Assert.Equal(443, b.Length);
    }

    [Fact]
    public void ForeignSparseCue1_NearZero_IsRead()
    {
        var b = new CueDataPacket().ToArray();
        b[47] = 3;
        b[49] = 5;
        var r = (CueDataPacket)TCNetPacket.Parse(b);
        Assert.Equal(3, r.Cues[0].Type);
        Assert.Equal(5u, r.Cues[0].InMs);
    }

    [Fact]
    public void SpecSize436_IsNotPadded_AndStrictAccepts()
    {
        var p = new CueDataPacket { LoopInMs = 1 };
        p.Cues[17].Type = 4;
        p.Cues[17].Color = new CueColor(9, 8, 7);
        var b = p.ToArray()[..436];
        var r = Assert.IsType<CueDataPacket>(TCNetPacket.Parse(b));
        Assert.False(r.WasPadded);
        Assert.Equal(new CueColor(9, 8, 7), r.Cues[17].Color);
        Assert.True(TCNetParser.TryParse(b, strict: true, out _, out var error), error);
        Assert.True(TCNetParser.TryParse(b[..435], strict: true, out _, out _));
        Assert.False(TCNetParser.TryParse(b[..434], strict: true, out _, out _));
        Assert.True(((CueDataPacket)TCNetPacket.Parse(b[..434])).WasPadded);
    }
}

public class PacketMiscFixTests
{
    [Theory]
    [InlineData("54 43 4E")]
    [InlineData("54434e")]
    [InlineData("0x54,0x43,0x4E")]
    [InlineData("0X54 0x434e")]
    [InlineData("54-43:4e")]
    [InlineData(" 54\t43\r\n4E ")]
    public void ParseHex_Accepts(string text) => Assert.Equal("TCN"u8.ToArray(), Wire.ParseHex(text));

    [Theory]
    [InlineData("5G4")]
    [InlineData("54 zz 43")]
    [InlineData("10x2")]
    [InlineData("540x43")]
    [InlineData("0x")]
    [InlineData("54434")]
    [InlineData("5 4")]
    [InlineData("54;43")]
    public void ParseHex_RejectsNonHex(string text) => Assert.Throws<FormatException>(() => Wire.ParseHex(text));

    [Fact]
    public void ParseHex_Empty() => Assert.Empty(Wire.ParseHex(" "));

    [Fact]
    public void TextPacket_DeclaredSizeBeyondDatagram_IsTruncated()
    {
        var b = new TextDataPacket("hello").ToArray();
        b[26] = 100;
        var r = Assert.IsType<TextDataPacket>(TCNetPacket.Parse(b));
        Assert.True(r.Truncated);
        Assert.Equal(100u, r.DeclaredSize);
        Assert.Equal("hello", r.Text);
        Assert.Contains(r.Describe(), f => f.Name == "Data Size" && f.Meaning!.Contains("truncated"));

        Assert.False(Assert.IsType<TextDataPacket>(TCNetPacket.Parse(new TextDataPacket("hello").ToArray())).Truncated);
    }

    [Fact]
    public void LayerIndexers_RejectNone()
    {
        var s = new StatusPacket();
        var t = new TimePacket();
        Assert.Same(s.Layers[7], s[Layer.C]);
        Assert.Same(t.Layers[0], t[Layer.L1]);
        var e1 = Assert.Throws<ArgumentOutOfRangeException>(() => s[Layer.None]);
        var e2 = Assert.Throws<ArgumentOutOfRangeException>(() => t[Layer.None]);
        Assert.Equal("layer", e1.ParamName);
        Assert.Equal("layer", e2.ParamName);
        Assert.Throws<ArgumentOutOfRangeException>(() => t[(Layer)9]);
    }
}
