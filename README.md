# TCNet-dotnet

A .NET 10 implementation of the **TCNet Link Specification V3.5.1B** (Event Imagineering Group / PRO DJ LINK Bridge),
with a command-line utility and a .NET MAUI app. Everything the protocol carries is exposed and printed in plain words:
every field has its offset, size, value and meaning.

| Folder | What |
|---|---|
| `src/TCNet` | The library (`TCNet` package): packets, parser, chunk reassembly, clock/timecode, the node, text and reference data |
| `src/TCNet.Native` | The same library as a native C library (NativeAOT, shared `.dll` or static `.lib`) with `include/tcnet.h` — see [NATIVE.md](src/TCNet.Native/NATIVE.md) |
| `samples/native` | `tcnet_demo.c`: C program that tests the native library |
| `tests/TCNet.Tests` | xUnit tests: every byte offset against the spec, round trips, reassembly, timing, node behaviour on loopback |
| `tools/TCNet.Utility` | `tcnet` — monitor, requester, controller, master simulator and offline spec reference |
| `app/TCNet.Maui` | TCNet Monitor — live layers, nodes, mixer, packet log, packet builder, master simulator, reference, settings |

## Build

Needs the .NET 10 SDK. The app also needs the MAUI workload (`dotnet workload install maui`).

```powershell
./build.ps1                    # Debug: library, tests, utility + run tests
./build.ps1 -c Release         # Release
./build.ps1 -c Both -App       # Debug and Release, plus the MAUI app for this OS
./build.ps1 -c Release -Pack   # NuGet packages in artifacts/
```

Or double-click `verify.cmd` (Windows): Debug + Release + tests + app, output in `build-log.txt`.

Plain `dotnet` works too:

```powershell
dotnet build src/TCNet -c Release
dotnet test tests/TCNet.Tests -c Debug
dotnet run --project tools/TCNet.Utility -- help
dotnet build app/TCNet.Maui -c Release -p:TCNetAppTfm=net10.0-windows10.0.19041.0
```

`TCNetAppTfm` builds the app for one platform so the Android workload isn't needed.

## Native library (C API)

`src/TCNet.Native` compiles everything to a native library with no .NET runtime dependency, callable from C/C++ and
anything with a C FFI. It can be shared or static:

```powershell
dotnet publish src/TCNet.Native -c Release -r win-x64 -p:NativeLib=Shared   # TCNetNative.dll + import .lib
dotnet publish src/TCNet.Native -c Release -r win-x64 -p:NativeLib=Static   # TCNetNative.lib (static)
```

The API is in `src/TCNet.Native/include/tcnet.h`, and [NATIVE.md](src/TCNet.Native/NATIVE.md) covers linking.
NativeAOT needs the MSVC build tools on Windows (clang on Linux, Xcode on macOS) and only builds for the OS it runs on.

## Library

```csharp
using TCNet;
using TCNet.Networking;

await using var node = new TCNetNode(new NodeSettings { NodeName = "MYAPP", NodeType = NodeType.Slave });
node.NodeDiscovered += (_, e) => Console.WriteLine($"found {e.Node.NodeName} ({e.Node.NodeType})");
node.PacketReceived += (_, e) => Console.WriteLine(e.Packet.ToDisplayString());   // every field, human readable
await node.StartAsync();

var master = node.Nodes.First(n => n.IsMasterOrRepeater);
var metrics = await node.RequestAsync(master, DataType.Metrics, layer: 1);
await node.SendControlAsync(master, ControlCommand.SetState(1, LayerState.Playing).ToString());
var sync = await node.TimeSyncAsync(master, rounds: 4);                            // delay, round trip, offset
```

Offline:

```csharp
var p = TCNetPacket.Parse(datagram);           // any message type, zero-pads short datagrams
foreach (var f in p.Describe()) Console.WriteLine(f);   // [  24+8] Artist = … (meaning)
byte[] bytes = new OptInPacket { NodeName = "MYAPP" }.ToArray();
Console.WriteLine(TCNetCatalog.ToMarkdown());   // the whole spec reference
```

What's in it:

- **Packets**: Opt-IN (2), Opt-OUT (3), Status (5), Time Sync (10), Error/Notification (13), Request (20), Control (101),
  Text Data (128), Keyboard Data (132), Data (200: Metrics 2, Metadata 4, Beat Grid 8, Cue 12, Small Waveform 16,
  Big Waveform 32, Mixer 150), Low Res Artwork (204/128), Application Specific Data (213/30), Time (254). Each has
  typed properties, `Describe()` (offset, size, name, value, meaning), `ToDisplayString()`, `ToArray()`.
- **Chunked data**: `ChunkAssembler` reassembles Beat Grid, waveforms and artwork (bounded memory, timeouts);
  `ChunkedPacket.Split` builds them.
- **Node** (`TCNetNode`): shared broadcast ports 60000/60001/60002, a unicast listener in 65023–65535, Opt-IN every
  second, Status, node registry with timeouts, requests with joined duplicates, per-node control queue, time sync,
  auto master election, Time streaming, request/control handlers for acting as a master.
- **Timing**: `TCNetClock` (µs timestamp), `Timecode` for all SMPTE modes, `Playback` — eight simulated decks that
  produce Time, Status and every Data type.
- **Text**: `TCNetText` (every option table, flags, application codes), `TCNetUnits` (ms, durations, percentages),
  `TCNetCatalog` (every packet layout, spec notes, search, Markdown).

## Utility

```
tcnet listen [--full] [--hex] [--type 254,200] [--from NAME]   live log of every packet, decoded
tcnet nodes [--seconds 5]                                      every node with every Opt-IN / Status / Time field
tcnet request <node> <data> [layer] [--out file]               metrics | metadata | beatgrid | cues | smallwave |
                                                               bigwave | artwork | mixer | <number>
tcnet sync <node> [rounds]                                     delay, round trip, clock offset
tcnet control <node> "<path>"                                  e.g. "layer/1/state=6;"
tcnet text [<node>] "<text>"   ·   tcnet key [<node>] <char|0xNNNN>
tcnet send <type> [<node>|<ip:port>|bcast:<port>]              build any packet and send it
tcnet master [--seconds N] [--interval 20]                     simulate a master with eight layers
tcnet decode <hex>|@file   ·   tcnet build <type>   ·   tcnet layout [<type>]
tcnet options [<name>]   ·   tcnet codes | notes | interfaces   ·   tcnet search <text>   ·   tcnet reference [--markdown]
```

Node options: `--iface <ip> --bcast <ip> --name <name> --id <n> --port <n> --role auto|master|slave|repeater --own`.
Install as a global tool with `./build.ps1 -c Release -Pack` then `dotnet tool install -g TCNet.Utility --add-source artifacts`.

## App

TCNet Monitor (Windows, Android, iOS, Mac Catalyst):

- **Live**: eight layer cards — state, time/remaining/total, timecode and SMPTE mode, BPM, beat marker, on-air,
  artist/title, speed/pitch, waveform with play head.
- **Nodes**: every node; tap for every header/Opt-IN/Status field, per-layer data, requests of any data type,
  time sync, control (play/pause/stop or any path), text and key messages, artwork.
- **Mixer**: master, crossfader and per-channel strips, plus every Mixer Data field.
- **Packets**: live log with filter/pause; tap a packet for all fields and a hex dump (copyable).
- **Send**: build any packet type, edit its hex, decode it, send to a broadcast port, node or address.
- **Master**: the simulated master's eight decks (load, play, pause, stop, on air).
- **Reference**: packets, byte layouts, option tables, application codes and spec notes, searchable, copy as Markdown.
- **Settings**: node name/ID/type/options, interface, ports, auto requests, election, simulation, log.

## Notes on the spec

Where the PDF contradicts itself the library follows what devices send and documents it (`tcnet notes`, Reference page):

- Metadata text is UTF-8 below protocol 3.5 and UTF-16LE from 3.5.
- Time packet: LC Time is read at byte 52 and the LC beat marker at 95.
- Cue Data prints cue 1 at byte 47, overlapping Loop OUT. An empty cue 1 is not written; a set cue 1 zeroes byte 46 and,
  if it would leave bytes 50–68 zero, marks byte 68 (unused). On read, bytes 47–49 are cue 1 when bytes 50–68 are set
  (or they can only be a sparse cue: a Loop OUT of 4.7 h or more), else Loop OUT; every cue 1 and Loop OUT below 2^24 ms
  that the library writes round-trips. `CueLayout.AfterLoop` places cue 1 at byte 50 instead.
- Cue Data is stated as 436 bytes but the table ends at 443; 443 is sent and 435+ bytes are accepted unpadded.
- Chunked data is numbered from 0; reassembly accepts 0- or 1-based numbering, locked per sender after its first
  transfer. A transfer's parts are dropped when a packet belongs to a newer transfer (new size or count, changed part,
  a gap of over 1 s, or a sender Timestamp over 250 ms away).
- Master election: longest uptime wins; within 2 s it is a tie and the lower Node ID, then IP address, wins; after
  three rounds the lowest ID (then address) wins. An elected master steps back for a configured master (never seen as
  Auto, or switched to Master well after the election); of two elected masters, the lower ID (then address) stays.

## License

MIT — see `LICENSE`. The TCNet Link Specification is © Event Imagineering Group; this project is not affiliated with it.
