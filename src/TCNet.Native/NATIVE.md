# TCNet native library (C API)

TCNet-dotnet compiled ahead of time with NativeAOT. You can call it from C, C++, Rust, Python (ctypes), Delphi,
Unity plugins and so on. The target machine needs no .NET runtime.

| Folder | What |
|---|---|
| `include/tcnet.h` | The whole API, documented inline |
| `Release/shared`, `Debug/shared` | `TCNetNative.dll` + `TCNetNative.lib` (import library) + `TCNetNative.pdb` |
| `Release/static`, `Debug/static` | `TCNetNative.lib` (static) + `runtime/` (NativeAOT runtime libraries and `link.rsp`) |
| `demo` | `tcnet_demo.c`; `shared/tcnet_demo.exe` (with the DLL) and `tcnet_demo_static.exe` (no DLL) |

Run `demo\tcnet_demo_static.exe` for the offline self-test. Add `--live 10` to also join the network for 10 seconds
and list the nodes it finds. Windows may ask to allow UDP 60000–60002 through the firewall.

## Linking

**Shared (DLL):**

```bat
cl /MD /I include app.c /link Release\shared\TCNetNative.lib
rem ship TCNetNative.dll next to app.exe
```

**Static (one exe, no DLL).** Define `TCNET_STATIC`, use the static CRT (`/MT`) and link the runtime libraries too:

```bat
cl /MT /DTCNET_STATIC /I include app.c /link Release\static\TCNetNative.lib /LIBPATH:Release\static\runtime @Release\static\runtime\link.rsp
```

In CMake or Visual Studio, add `TCNetNative.lib`, every entry of `runtime\link.rsp`, and `runtime` as a library
directory. Only one copy of the NativeAOT runtime can be linked into a process at a time. Two NativeAOT static
libraries can't share one exe, but two DLLs can share one process.

## Using it

```c
#include "tcnet.h"

static void TCNET_CALL on_event(void* user, int kind, const char* json) { puts(json); }

int main(void) {
    tcnet_node* node = tcnet_node_create("{\"nodeName\":\"CHRONOS\"}");
    tcnet_node_set_callback(node, TCNET_EVENT_NODES, on_event, NULL);
    if (tcnet_node_start(node) != 0) { puts(tcnet_last_error()); return 1; }

    tcnet_time t;
    if (tcnet_node_get_time(node, NULL, &t) == 0)          /* the master's latest Time packet */
        printf("L1 %02d:%02d:%02d:%02d\n", t.layers[0].hours, t.layers[0].minutes, t.layers[0].seconds, t.layers[0].frames);

    char* meta = tcnet_node_request_json(node, NULL, TCNET_DATA_METADATA, TCNET_LAYER_1, 0);
    if (meta) { puts(meta); tcnet_free(meta); }            /* artist, title, … with every field described */

    tcnet_node_destroy(node);                              /* sends Opt-OUT */
    return 0;
}
```

The API has four parts:

- **Packets:** parse any datagram to JSON or text (`tcnet_parse_json` gives every field with its offset, size,
  value and meaning), build templates, and encode or decode Time packets through the `tcnet_time` struct.
- **Node:** join the network, see the population list, read time, request metrics, metadata, cues, beat grid,
  waveforms, artwork and mixer data, time sync, control, text and keys, and send or stream time as master.
- **Events:** one callback, filtered by a mask, with JSON for every packet, node, data and warning event.
- **Reference:** the whole spec offline. `tcnet_catalog_json`, `tcnet_search_json`, `tcnet_describe` and
  `tcnet_reference_markdown`.

Strings the library returns are freed with `tcnet_free`. Calls return −1 or NULL on failure, and
`tcnet_last_error()` says why.

## Building it

Windows: `_tcnet-installer\build-native.cmd [win-x64|win-arm64]`, or directly:

```bat
dotnet publish src\TCNet.Native -c Release -r win-x64 -p:NativeLib=Shared -o out\shared
dotnet publish src\TCNet.Native -c Release -r win-x64 -p:NativeLib=Static -o out\static
```

NativeAOT only compiles for the OS it runs on. Build `linux-x64`/`linux-arm64` on Linux (with clang) and
`osx-arm64`/`osx-x64` on macOS (with Xcode). That gives `TCNetNative.so` / `.a` and `TCNetNative.dylib` / `.a`
from the same source and header.
