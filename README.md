# TCNet-dotnet — TCNet V3.5.1B for .NET 10

A from-scratch implementation of the **TCNet Link Specification V3.5.1B (02/03/2022)** with a command-line utility and a .NET MAUI app. Every packet, field and option in the spec is modelled. Each option comes with its spec meaning, and every packet can describe itself field by field.

```
TCNet.slnx
├─ src/TCNet               class library (net10.0, AOT/trim safe)
├─ tests/TCNet.Tests       xUnit: byte offsets from the spec tables, round trips, reassembly, time sync, loopback nodes
├─ tools/TCNet.Monitor     tcnet-monitor CLI (listen, nodes, request, sync, control, text, key, master, decode, layout, options, catalog)
└─ samples/TCNet.Maui      MAUI app (Windows, Android; iOS / Mac Catalyst on macOS)
```

## Build

```powershell
dotnet build TCNet.slnx -c Debug
dotnet build TCNet.slnx -c Release
dotnet test  tests/TCNet.Tests -c Release

dotnet workload install maui                                # once, for the app
dotnet build samples/TCNet.Maui -c Release -f net10.0-windows10.0.19041.0 -p:TCNetAppTfm=net10.0-windows10.0.19041.0
dotnet run --project samples/TCNet.Maui -f net10.0-windows10.0.19041.0 -p:TCNetAppTfm=net10.0-windows10.0.19041.0

./build.ps1 -c Both -App        # Debug + Release, tests, and the app for this OS
```

The `-p:TCNetAppTfm=` property builds only that platform, so the Android workload is not needed on Windows. Windows Firewall must allow UDP 60000–60002 and 65023–65535.

## Library

| Area | Types |
|---|---|
| Packets | `OptInPacket` (2), `OptOutPacket` (3), `StatusPacket` (5), `TimeSyncPacket` (10), `ErrorNotificationPacket` (13), `RequestPacket` (20), `ApplicationDataPacket` (30 / 213), `ControlPacket` (101), `TextDataPacket` (128), `KeyboardDataPacket` (132), `MetricsDataPacket`, `MetadataPacket`, `BeatGridDataPacket`, `CueDataPacket`, `SmallWaveformPacket`, `BigWaveformPacket`, `MixerDataPacket` (200/2…150), `LowResArtworkPacket` (204/128), `TimePacket` (254), `UnknownPacket` / `UnknownDataPacket` |
| Parsing | `TCNetPacket.TryParse(bytes, out packet, out error)`. Checks "TCN". Short packets from older flames are zero-padded (`WasPadded`); `TCNetPacketParser.Strict` rejects them instead |
| Chunked data | `ChunkedDataPacket.Split(...)`, `TCNetChunkAssembler` (any packet order, 0- or 1-based numbering), `BeatGrid`, `Waveform`, `AssembledData` |
| Timing | `TCNetClock` (0–999999 µs wheel, 12 h uptime), `TimeSync.Compute/Average` (spec delay formula), `Timecode`, `TCNetPlayback` (simulated 8-layer master) |
| Network | `TCNetNode`: binds 60000/60001/60002 shared and a unicast port 65023+. Opt-IN every 1 s (broadcast + unicast to known nodes), population list with timeout, Opt-OUT on stop. Answers Time Sync, Request and Control. Also: `RequestAsync` and typed helpers, `TimeSyncAsync`, `SendControlAsync`, `SendTextAsync`, `SendKeyAsync`, `PublishTimeAsync` / `StartTimeStream`, `SendToSlavesAsync`, auto metadata/metrics requests, master election |
| Human readable | `packet.Describe()` (offset, size, name, value, meaning for every field), `packet.ToDisplayString()`, `TCNetText.Describe/DescribeFlags/Options<T>()`, `TCNetOptionCatalog.Packets/OptionSets/SpecNotes/Search/ToMarkdown()`, registered application codes, `Wire.HexDump` |

```csharp
await using var node = new TCNetNode(new() { NodeName = "MYAPP", NodeType = NodeType.Slave, AutoRequestMetadata = true });
node.NodeDiscovered += (_, e) => Console.WriteLine(e.Node);
node.PacketReceived += (_, e) => { if (e.Packet is TimePacket t) Console.WriteLine(t.Summary); };
node.Start();

var master = node.Nodes.First(n => n.NodeType == NodeType.Master);
var meta   = await node.RequestMetadataAsync(master, layer: 1);
var grid   = await node.RequestBeatGridAsync(master, layer: 1);
var ack    = await node.SendControlAsync(master, ControlCommand.SetLayerState(1, LayerState.Stopped));

foreach (var f in meta!.Describe()) Console.WriteLine(f);   // [  29+256] Track Artist = … (UTF-16)
```

## tcnet-monitor

```
tcnet-monitor listen --full                 every packet, every field, with meanings
tcnet-monitor nodes --seconds 5             population list with status, layers, time sync
tcnet-monitor request BRIDGE beatgrid 1     request any data type; waveforms drawn as text, artwork saved as JPEG
tcnet-monitor sync BRIDGE 8
tcnet-monitor control BRIDGE "layer/1/state=6;"
tcnet-monitor master                        simulate a master (time stream, status, requests, control)
tcnet-monitor decode 0100030554434E02…      decode any datagram
tcnet-monitor layout metrics | options | catalog --markdown | interfaces
```

## MAUI app

- **Live**: the 8 layers of a chosen node: name, state, time and remaining time, waveform with play head, timecode with SMPTE mode and state, beat marker, BPM, speed and pitch bend, artist and title, track ID, source, on-air fader.
- **Nodes → node detail**: every Opt-IN/Status field with its meaning and per-layer data. You can request any data type, or all of them. Other actions: time sync (4 rounds), control paths (play, stop, resync), Text Data, Keyboard Data and artwork preview.
- **Mixer**: every Mixer Data field, plus the master, crossfader and 6 channel strips.
- **Packets**: live log (sent and received) with a filter. Tap a packet for every field and a hex dump.
- **Send**: build any packet type, edit the hex, decode it and send it to a broadcast port, a node or an IP:port.
- **Reference**: every packet layout, option table, application code and spec note, with search and a copy-as-Markdown button.
- **Settings**: node name, ID, type, the 4 option flags, interface, ports and broadcast address. Also auto requests, time sync, master election and simulate-a-master.

## Spec notes honoured

- Metadata strings are UTF-8 for protocol < 3.5 and UTF-16LE for ≥ 3.5 (256-byte fields), chosen from the sender's header version.
- Time packet: LC Time is at 52 and LC Beat Marker at 95; the table's 48/94 are typos.
- Cue Data: cue 1 is printed at 47, overlapping Loop OUT (46–49). The default uses the printed offsets: an empty cue 1 is not written, so Loop OUT survives; a set cue 1 takes the shared bytes. `CueDataPacket.DefaultLayout = CueTableLayout.AfterLoop` moves the table to 50.
- Metadata (548) and Mixer (270) are sent at the stated sizes, even though their fields end one byte earlier.
- Chunk numbering is sent 0-based (it matches the beat grid OFFSET formula). Reassembly accepts either base, and bounds packets, bytes and pending transfers (`MaxTotalPackets`, `MaxTransferBytes`, `MaxPendingTransfers`).
- Uptime rolls over at 12 h. Master election follows the Opt-OUT tip: highest uptime wins, uptimes within 2 s tie and go to the lower Node ID, and a node that was elected steps back if a higher-ranked master appears.

TCNet is by Event Imagineering Group. This library is an independent implementation of the public specification.
