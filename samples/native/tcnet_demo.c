/*
 * tcnet_demo.c: exercises the TCNet C API (shared or static library) and exits 0 when every check passes.
 *   tcnet_demo            offline checks only (no sockets are opened)
 *   tcnet_demo --live 10  also joins the real network for 10 s and prints the nodes it sees
 * Build (Windows, shared): cl /MD /I include tcnet_demo.c /link TCNetNative.lib
 * Build (Windows, static): cl /MT /DTCNET_STATIC /I include tcnet_demo.c /link TCNetNative.lib @runtime\link.rsp
 */
#if !defined(_WIN32) && !defined(_DEFAULT_SOURCE)
#  define _DEFAULT_SOURCE /* usleep */
#endif
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "tcnet.h"

#ifdef _WIN32
#  include <windows.h>
#  define SLEEP_MS(ms) Sleep(ms)
#else
#  include <unistd.h>
#  define SLEEP_MS(ms) usleep((ms) * 1000)
#endif

static int failures = 0;
static int events = 0;

#define CHECK(cond, what)                                                        \
    do {                                                                         \
        if (cond) printf("ok    %s\n", what);                                    \
        else { printf("FAIL  %s  (%s)\n", what, tcnet_last_error()); failures++; } \
    } while (0)

static void TCNET_CALL on_event(void* user, int kind, const char* json)
{
    (void)user;
    events++;
    printf("      event %d: %.160s%s\n", kind, json, strlen(json) > 160 ? "..." : "");
}

static void show(const char* label, char* text, size_t max)
{
    if (text) printf("      %s: %.*s%s\n", label, (int)max, text, strlen(text) > max ? "..." : "");
    tcnet_free(text);
}

int main(int argc, char** argv)
{
    int live = argc > 1 && strcmp(argv[1], "--live") == 0;
    int live_seconds = live && argc > 2 ? atoi(argv[2]) : 10;
    uint8_t buf[4096];
    int n;

    printf("TCNet native library %s\n", tcnet_version());
    CHECK(tcnet_sizeof_time() == (int)sizeof(tcnet_time), "tcnet_time layout matches the library (164 bytes)");

    /* packets */
    n = tcnet_packet_template(TCNET_MSG_OPTIN, 0, NULL, 0);
    CHECK(n > 24 && n <= (int)sizeof buf, "Opt-IN size query");
    CHECK(tcnet_packet_template(TCNET_MSG_OPTIN, 0, buf, sizeof buf) == n, "Opt-IN template");
    CHECK(tcnet_is_tcnet(buf, n) == 1, "recognised as TCNet");
    {
        char* json = tcnet_parse_json(buf, n);
        CHECK(json != NULL && strstr(json, "\"name\":\"Opt-IN\"") && strstr(json, "\"fields\":["), "Opt-IN to JSON with fields");
        show("json", json, 240);
    }
    CHECK(tcnet_parse_json((const uint8_t*)"not tcnet", 9) == NULL && strlen(tcnet_last_error()) > 0, "bad datagram gives NULL + error");
    printf("      error: %s\n", tcnet_last_error());

    /* Time */
    {
        tcnet_time t, back;
        memset(&t, 0, sizeof t);
        t.smpte_mode = TCNET_SMPTE_30;
        t.flags = TCNET_TIME_AUTO_TIMECODE;
        t.layers[0].time_ms = 61000;
        t.layers[0].total_ms = 300000;
        t.layers[0].state = TCNET_STATE_PLAYING;
        t.layers[0].timecode_state = TCNET_TC_RUNNING;
        t.layers[0].on_air = 255;
        n = tcnet_time_encode(&t, buf, sizeof buf);
        CHECK(n == 162, "encode Time (162 bytes)");
        memset(&back, 0, sizeof back);
        CHECK(tcnet_time_decode(buf, n, &back) == 0, "decode Time");
        CHECK(back.layers[0].time_ms == 61000 && back.layers[0].minutes == 1 && back.layers[0].seconds == 1 && back.layers[0].frames == 0,
              "layer 1 at 61000 ms = 00:01:01:00");
        show("text", tcnet_parse_text(buf, n), 300);
    }

    /* timecode */
    {
        tcnet_timecode tc;
        CHECK(tcnet_timecode_from_ms(3723040, TCNET_SMPTE_25, &tc) == 0 && tc.hours == 1 && tc.minutes == 2 && tc.seconds == 3 && tc.frames == 1,
              "3723040 ms @25 = 01:02:03:01");
        CHECK(tcnet_timecode_to_ms(&tc, TCNET_SMPTE_25) == 3723040, "and back to ms");
        CHECK(tcnet_frame_rate(TCNET_SMPTE_2997) > 29.97 && tcnet_frame_rate(TCNET_SMPTE_2997) < 29.98, "29.97 frame rate");
    }

    /* reference */
    {
        char* d = tcnet_describe("Node Type", TCNET_NODE_MASTER);
        CHECK(d != NULL && strstr(d, "Master") != NULL, "describe Node Type 2");
        show("meaning", d, 120);
        d = tcnet_describe("NodeOptions", TCNET_OPT_SUPPORTS_CONTROL | TCNET_OPT_DO_NOT_DISTURB);
        CHECK(d != NULL && strstr(d, " + ") != NULL, "describe flags");
        show("meaning", d, 120);
        d = tcnet_search_json("artwork");
        CHECK(d != NULL && d[0] == '[', "search the spec");
        show("search", d, 200);
        d = tcnet_catalog_json();
        CHECK(d != NULL && strstr(d, "\"optionSets\"") != NULL, "catalog JSON");
        tcnet_free(d);
        d = tcnet_reference_markdown();
        CHECK(d != NULL && strncmp(d, "# TCNet", 7) == 0, "reference markdown");
        tcnet_free(d);
        show("interfaces", tcnet_interfaces_json(), 200);
    }

    /* node, offline: datagrams are injected, no socket is opened */
    {
        tcnet_node* node = tcnet_node_create("{\"nodeName\":\"CDEMO\",\"nodeType\":\"Slave\",\"requestTimeoutMs\":1000}");
        tcnet_time t;
        char* info;
        CHECK(node != NULL, "create node");
        CHECK(tcnet_node_create("{\"noSuchSetting\":1}") == NULL, "unknown setting rejected");
        printf("      error: %s\n", tcnet_last_error());
        if (node) {
            info = tcnet_node_info_json(node);
            CHECK(info != NULL && strstr(info, "\"nodeName\":\"CDEMO\"") != NULL, "node info JSON");
            show("info", info, 200);
            CHECK(tcnet_node_set_callback(node, TCNET_EVENT_NODES | TCNET_EVENT_MASK(TCNET_EVENT_PACKET_RECEIVED), on_event, NULL) == 0, "set callback");

            n = tcnet_packet_template(TCNET_MSG_OPTIN, 0, buf, sizeof buf);
            CHECK(tcnet_node_inject(node, buf, n, "10.0.0.5", 60000) == 0, "inject Opt-IN from 10.0.0.5");
            CHECK(events >= 1, "callback fired");
            memset(&t, 0, sizeof t);
            t.smpte_mode = TCNET_SMPTE_25;
            t.flags = TCNET_TIME_AUTO_TIMECODE;
            t.layers[1].time_ms = 1500;
            t.layers[1].state = TCNET_STATE_PLAYING;
            n = tcnet_time_encode(&t, buf, sizeof buf);
            CHECK(tcnet_node_inject(node, buf, n, "10.0.0.5", 60001) == 0, "inject Time");
            memset(&t, 0, sizeof t);
            CHECK(tcnet_node_get_time(node, NULL, &t) == 0 && t.layers[1].time_ms == 1500 && t.layers[1].seconds == 1 && t.layers[1].frames == 12,
                  "latest time: layer 2 at 00:00:01:12");
            show("nodes", tcnet_node_nodes_json(node), 300);
            tcnet_node_set_callback(node, 0, NULL, NULL);
            tcnet_node_destroy(node);
        }
    }

    /* node, live */
    if (live) {
        tcnet_node* node = tcnet_node_create("{\"nodeName\":\"CDEMO\"}");
        CHECK(node != NULL && tcnet_node_start(node) == 0, "start node on the network");
        if (node) {
            tcnet_node_set_callback(node, TCNET_EVENT_NODES | TCNET_EVENT_MASK(TCNET_EVENT_WARNING), on_event, NULL);
            printf("      listening for %d s...\n", live_seconds);
            SLEEP_MS(live_seconds * 1000);
            show("nodes", tcnet_node_nodes_json(node), 2000);
            show("time", tcnet_node_time_json(node, NULL), 400);
            CHECK(tcnet_node_stop(node) == 0, "stop node (Opt-OUT)");
            tcnet_node_set_callback(node, 0, NULL, NULL);
            tcnet_node_destroy(node);
        }
    }

    printf(failures ? "\n%d check(s) FAILED\n" : "\nALL CHECKS PASSED\n", failures);
    return failures ? 1 : 0;
}
