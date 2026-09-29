/*
 * tcnet.h: C API of TCNet-dotnet (TCNet Link Specification V3.5.1B), built with NativeAOT.
 * No .NET runtime is needed on the target machine.
 *
 *   Shared: link TCNetNative.lib (Windows import library) and ship TCNetNative.dll;
 *           or libTCNetNative.so / libTCNetNative.dylib on Linux / macOS (-lTCNetNative).
 *   Static: #define TCNET_STATIC, link TCNetNative.lib / .a plus the NativeAOT runtime libraries listed in
 *           runtime/link.rsp (Windows) or runtime/link.txt, and build with the static CRT (/MT).
 *
 * Conventions
 *   - Text is UTF-8. char* results are allocated by the library: free them with tcnet_free (never free()).
 *   - int results: >= 0 success (0, a length or a code), -1 failure. On failure tcnet_last_error() says why
 *     (per thread; valid until the next failure on that thread; never NULL).
 *   - char* results: NULL on failure, with tcnet_last_error().
 *   - JSON is compact unless tcnet_set_json_indented(1). Every value comes with its meaning ("...Text" fields);
 *     packets carry "fields": [{offset, size, name, value, meaning}] in wire order.
 *   - Node calls block until done (start, stop, requests, time sync, control). tcnet_node_stop and
 *     tcnet_node_destroy fail (see tcnet_last_error) when called from inside an event callback.
 */
#ifndef TCNET_H
#define TCNET_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#if defined(_WIN32) && !defined(TCNET_STATIC)
#  define TCNET_API __declspec(dllimport)
#else
#  define TCNET_API
#endif

#if defined(_WIN32)
#  define TCNET_CALL __cdecl
#else
#  define TCNET_CALL
#endif

/* ───────────── protocol values (see tcnet_catalog_json for every table with meanings) ───────────── */

enum tcnet_message_type {
    TCNET_MSG_OPTIN = 2, TCNET_MSG_OPTOUT = 3, TCNET_MSG_STATUS = 5, TCNET_MSG_TIME_SYNC = 10,
    TCNET_MSG_ERROR_NOTIFICATION = 13, TCNET_MSG_REQUEST = 20, TCNET_MSG_APPLICATION_DATA = 30,
    TCNET_MSG_CONTROL = 101, TCNET_MSG_TEXT_DATA = 128, TCNET_MSG_KEYBOARD_DATA = 132, TCNET_MSG_DATA = 200,
    TCNET_MSG_DATA_FILE = 204, TCNET_MSG_APPLICATION_SPECIFIC_DATA = 213, TCNET_MSG_TIME = 254
};

enum tcnet_data_type {
    TCNET_DATA_METRICS = 2, TCNET_DATA_METADATA = 4, TCNET_DATA_BEAT_GRID = 8, TCNET_DATA_CUE_DATA = 12,
    TCNET_DATA_SMALL_WAVEFORM = 16, TCNET_DATA_BIG_WAVEFORM = 32, TCNET_DATA_LOW_RES_ARTWORK = 128,
    TCNET_DATA_MIXER = 150
};

enum tcnet_node_type { TCNET_NODE_AUTO = 1, TCNET_NODE_MASTER = 2, TCNET_NODE_SLAVE = 4, TCNET_NODE_REPEATER = 8 };

enum tcnet_node_options {
    TCNET_OPT_NEED_AUTHENTICATION = 1, TCNET_OPT_SUPPORTS_CONTROL = 2,
    TCNET_OPT_SUPPORTS_APPLICATION_DATA = 4, TCNET_OPT_DO_NOT_DISTURB = 8
};

/* Layer numbers as used in requests and data packets (time/layer arrays are 0-based: index = layer - 1). */
enum tcnet_layer {
    TCNET_LAYER_NONE = 0, TCNET_LAYER_1 = 1, TCNET_LAYER_2 = 2, TCNET_LAYER_3 = 3, TCNET_LAYER_4 = 4,
    TCNET_LAYER_A = 5, TCNET_LAYER_B = 6, TCNET_LAYER_M = 7, TCNET_LAYER_C = 8
};

enum tcnet_layer_state {
    TCNET_STATE_IDLE = 0, TCNET_STATE_PLAYING = 3, TCNET_STATE_LOOPING = 4, TCNET_STATE_PAUSED = 5,
    TCNET_STATE_STOPPED = 6, TCNET_STATE_CUE_DOWN = 7, TCNET_STATE_PLATTER_DOWN = 8,
    TCNET_STATE_FAST_FORWARD = 9, TCNET_STATE_FAST_REVERSE = 10, TCNET_STATE_HOLD = 11
};

enum tcnet_smpte_mode {
    TCNET_SMPTE_GENERAL = 0, TCNET_SMPTE_24 = 24, TCNET_SMPTE_25 = 25, TCNET_SMPTE_2997 = 29, TCNET_SMPTE_30 = 30
};

enum tcnet_timecode_state { TCNET_TC_STOPPED = 0, TCNET_TC_RUNNING = 1, TCNET_TC_FORCE_RESYNC = 2 };

enum tcnet_notification { TCNET_NOTE_UNKNOWN = 1, TCNET_NOTE_NOT_POSSIBLE = 13, TCNET_NOTE_EMPTY = 14, TCNET_NOTE_OK = 255 };

/* ───────────── Time packet (type 254) ───────────── */

typedef struct tcnet_time_layer {
    uint32_t time_ms;        /* position */
    uint32_t total_ms;       /* track length */
    uint8_t  beat_marker;    /* 0 = unknown, 1-4 */
    uint8_t  state;          /* enum tcnet_layer_state */
    uint8_t  smpte_mode;     /* enum tcnet_smpte_mode; 0 = use tcnet_time.smpte_mode */
    uint8_t  timecode_state; /* enum tcnet_timecode_state */
    uint8_t  hours, minutes, seconds, frames;
    uint8_t  on_air;         /* fader 0-255; >= 1 = on air */
    uint8_t  reserved[3];
} tcnet_time_layer;          /* 20 bytes */

#define TCNET_TIME_AUTO_TIMECODE 1 /* tcnet_time.flags: fill hours..frames from time_ms when encoding/sending */

typedef struct tcnet_time {
    uint8_t smpte_mode;      /* general SMPTE mode (byte 105) */
    uint8_t flags;           /* TCNET_TIME_AUTO_TIMECODE */
    uint8_t reserved[2];
    tcnet_time_layer layers[8]; /* 1, 2, 3, 4, A, B, M, C */
} tcnet_time;                /* 164 bytes; check with tcnet_sizeof_time() */

typedef struct tcnet_timecode { uint8_t hours, minutes, seconds, frames; } tcnet_timecode;

/* ───────────── library ───────────── */

TCNET_API const char* TCNET_CALL tcnet_version(void);        /* "3.0.0 (TCNet Link Specification 3.5)"; don't free */
TCNET_API const char* TCNET_CALL tcnet_last_error(void);     /* don't free */
TCNET_API void        TCNET_CALL tcnet_free(void* p);        /* anything the library allocated; NULL is fine */
TCNET_API int         TCNET_CALL tcnet_sizeof_time(void);    /* == sizeof(tcnet_time) */
TCNET_API void        TCNET_CALL tcnet_set_json_indented(int on);
TCNET_API void        TCNET_CALL tcnet_set_strict(int on);   /* 1 = reject packets shorter than the spec size */

/* ───────────── packets ───────────── */

TCNET_API int   TCNET_CALL tcnet_is_tcnet(const uint8_t* data, int length);          /* 1 / 0 */
TCNET_API char* TCNET_CALL tcnet_parse_json(const uint8_t* data, int length);        /* packet with every field */
TCNET_API char* TCNET_CALL tcnet_parse_text(const uint8_t* data, int length);        /* name, size, one field per line */
TCNET_API char* TCNET_CALL tcnet_hexdump(const uint8_t* data, int length);
/* Encoders return the packet length and write it only when buffer != NULL and capacity >= length. */
TCNET_API int   TCNET_CALL tcnet_packet_template(int message_type, int data_type, uint8_t* buffer, int capacity);
TCNET_API int   TCNET_CALL tcnet_time_encode(const tcnet_time* time, uint8_t* buffer, int capacity);
TCNET_API int   TCNET_CALL tcnet_time_decode(const uint8_t* data, int length, tcnet_time* out);

/* ───────────── timecode ───────────── */

TCNET_API int      TCNET_CALL tcnet_timecode_from_ms(uint32_t ms, int smpte_mode, tcnet_timecode* out);
/* Returns TCNET_TIMECODE_ERROR (never a valid time) on error; see tcnet_last_error. */
#define TCNET_TIMECODE_ERROR 0xFFFFFFFFu
TCNET_API uint32_t TCNET_CALL tcnet_timecode_to_ms(const tcnet_timecode* timecode, int smpte_mode);
TCNET_API double   TCNET_CALL tcnet_frame_rate(int smpte_mode);  /* 29.97 → 30000/1001 */

/* ───────────── reference (the spec, offline) ───────────── */

TCNET_API char* TCNET_CALL tcnet_reference_markdown(void);
TCNET_API char* TCNET_CALL tcnet_catalog_json(void);            /* packets + layouts, option tables, app codes, notes */
TCNET_API char* TCNET_CALL tcnet_search_json(const char* text); /* ["Packet 2: Opt-IN – ...", ...] */
/* Meaning of a value: option_set is a table name ("Node Type", "Layer State") or enum name ("NodeType"). */
TCNET_API char* TCNET_CALL tcnet_describe(const char* option_set, int64_t value);
TCNET_API char* TCNET_CALL tcnet_interfaces_json(void);         /* IPv4 interfaces with broadcast addresses */

/* ───────────── node ───────────── */

typedef struct tcnet_node tcnet_node;

/*
 * settings_json: NULL or {} for defaults, or any of
 *   nodeId, nodeName (≤ 8), nodeType ("Master"/2), nodeOptions, vendorName, deviceName, deviceMajor, deviceMinor,
 *   deviceBug, versionMajor, versionMinor, listenerPort (0 = first free from 65023), localAddress, broadcastAddress,
 *   listenOnBroadcastPorts, optInIntervalMs, nodeTimeoutMs, unicastOptIn, sendStatus (null = when master),
 *   answerTimeSync, autoTimeSync, timeSyncIntervalMs, autoRequestMetadata, autoRequestMetrics,
 *   autoMasterElection, receiveOwnPackets, requestTimeoutMs (-1 = forever).
 * tcnet_node_info_json returns them under "settings" (next to live state such as "running" and "sharedPorts");
 * that "settings" object can be passed back to tcnet_node_create.
 */
TCNET_API tcnet_node* TCNET_CALL tcnet_node_create(const char* settings_json);
TCNET_API void        TCNET_CALL tcnet_node_destroy(tcnet_node* node);  /* stops it first */
TCNET_API int         TCNET_CALL tcnet_node_start(tcnet_node* node);    /* binds UDP 60000-60002 + unicast port */
TCNET_API int         TCNET_CALL tcnet_node_stop(tcnet_node* node);     /* sends Opt-OUT */
TCNET_API char*       TCNET_CALL tcnet_node_info_json(tcnet_node* node);
TCNET_API int         TCNET_CALL tcnet_node_set_type(tcnet_node* node, int node_type);

/* Events: json is {"event": name, "kind": n, ...} and is only valid during the call. mask = OR of
 * TCNET_EVENT_MASK(kind), plus TCNET_EVENT_WITH_FIELDS to get every field of packets. callback NULL = off.
 * The callback can run on several threads at the same time (one per receive port, the housekeeping and time
 * stream threads, and any thread calling a send, request, sync, control or inject function), so make it
 * thread-safe. tcnet_node_set_callback and tcnet_node_destroy wait until calls to the previous callback have
 * returned, so its user data may be freed right after they return. Called from inside a callback,
 * tcnet_node_set_callback doesn't wait (that could deadlock), so the previous callback may still be running. */
enum tcnet_event_kind {
    TCNET_EVENT_PACKET_RECEIVED = 1, TCNET_EVENT_PACKET_SENT = 2, TCNET_EVENT_INVALID_DATAGRAM = 3,
    TCNET_EVENT_NODE_DISCOVERED = 4, TCNET_EVENT_NODE_CHANGED = 5, TCNET_EVENT_NODE_LOST = 6,
    TCNET_EVENT_DATA_ASSEMBLED = 7, TCNET_EVENT_TIME_SYNCED = 8, TCNET_EVENT_ROLE_CHANGED = 9, TCNET_EVENT_WARNING = 10
};
#define TCNET_EVENT_MASK(kind) (1 << (kind))
#define TCNET_EVENT_ALL 0x7FE
#define TCNET_EVENT_NODES (TCNET_EVENT_MASK(TCNET_EVENT_NODE_DISCOVERED) | TCNET_EVENT_MASK(TCNET_EVENT_NODE_CHANGED) | TCNET_EVENT_MASK(TCNET_EVENT_NODE_LOST))
#define TCNET_EVENT_WITH_FIELDS (1 << 16)
typedef void (TCNET_CALL *tcnet_event_callback)(void* user, int kind, const char* json);
TCNET_API int TCNET_CALL tcnet_node_set_callback(tcnet_node* node, int mask, tcnet_event_callback callback, void* user);

/* Population list. A node query is its key "ip#nodeId", its name, or its IP. */
TCNET_API char* TCNET_CALL tcnet_node_nodes_json(tcnet_node* node);
TCNET_API char* TCNET_CALL tcnet_node_find_json(tcnet_node* node, const char* query);

/* Latest Time packet; query NULL = the master's (else the most recent). 0 = ok, 1 = none yet, -1 = error. */
TCNET_API int   TCNET_CALL tcnet_node_get_time(tcnet_node* node, const char* query, tcnet_time* out);
TCNET_API char* TCNET_CALL tcnet_node_time_json(tcnet_node* node, const char* query);

/* Sending time (as master): once, or streamed every interval_ms (spec: 1-40 ms) from the last set_stream_time. */
TCNET_API int TCNET_CALL tcnet_node_publish_time(tcnet_node* node, const tcnet_time* time);
TCNET_API int TCNET_CALL tcnet_node_set_stream_time(tcnet_node* node, const tcnet_time* time);
TCNET_API int TCNET_CALL tcnet_node_start_time_stream(tcnet_node* node, int interval_ms);
TCNET_API int TCNET_CALL tcnet_node_stop_time_stream(tcnet_node* node);

/* Requests. query NULL = the master. layer = enum tcnet_layer (mixer id for TCNET_DATA_MIXER).
 * timeout_ms: 0 = requestTimeoutMs, < 0 = forever. */
TCNET_API char* TCNET_CALL tcnet_node_request_json(tcnet_node* node, const char* query, int data_type, int layer, int timeout_ms);
/* Raw bytes: the reassembled data (beat grid, waveform, artwork JPEG) or the answering packet.
 * 0 = ok (*data from the library: tcnet_free it), 1 = no data (timed out / notification), -1 = error. */
TCNET_API int   TCNET_CALL tcnet_node_request_data(tcnet_node* node, const char* query, int data_type, int layer, int timeout_ms,
                                                   uint8_t** data, int* length);
TCNET_API char* TCNET_CALL tcnet_node_time_sync_json(tcnet_node* node, const char* query, int rounds, int timeout_ms);

/* Control path (e.g. "layer/1/state=6;" stops layer 1): returns the notification code (255 = OK), -2 = no answer, -1 = error. */
TCNET_API int TCNET_CALL tcnet_node_send_control(tcnet_node* node, const char* query, const char* path, int timeout_ms);
TCNET_API int TCNET_CALL tcnet_node_send_text(tcnet_node* node, const char* text, const char* query); /* query NULL = broadcast */
TCNET_API int TCNET_CALL tcnet_node_send_key(tcnet_node* node, int key_code, const char* query);      /* query NULL = broadcast */
TCNET_API int TCNET_CALL tcnet_node_send_raw(tcnet_node* node, const uint8_t* data, int length, const char* ip, int port);
/* Handle a datagram as if it had arrived from ip:port (testing, replay). */
TCNET_API int TCNET_CALL tcnet_node_inject(tcnet_node* node, const uint8_t* data, int length, const char* ip, int port);

#ifdef __cplusplus
}
#endif
#endif /* TCNET_H */
