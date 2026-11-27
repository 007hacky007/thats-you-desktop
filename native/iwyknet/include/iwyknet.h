/*
 * iwyknet - WebRTC Direct data channel server for the phone web page.
 *
 * This header is the ABI contract of iwyknet.dylib / iwyknet.dll.
 *
 * - Every function returns int32_t (>= 0 success, sometimes a length; < 0 an IWYK_E_* code).
 * - Strings in are NUL-terminated UTF-8. Outputs go into caller-owned buffers (out, cap).
 *   The library copies inputs before returning and never keeps a caller pointer.
 * - No callbacks into the host. No memory allocated by the library crosses the boundary.
 * - All functions may be called from any thread. Only iwyk_start (about 1 s) and
 *   iwyk_stop (about 0.5 s) may block.
 * - The library hosts a Go runtime: it must never be unloaded.
 */
#ifndef IWYKNET_H
#define IWYKNET_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define IWYK_ABI_VERSION 1

/* results (>= 0 success, sometimes a length) */
#define IWYK_OK                0
#define IWYK_E_NOT_STARTED    -1
#define IWYK_E_ALREADY_STARTED -2
#define IWYK_E_ARG            -3   /* bad argument (null, range, bad URL, bad IP) */
#define IWYK_E_NO_SESSION     -4   /* unknown, closing or closed session */
#define IWYK_E_SMALLBUF       -5   /* output buffer too small; nothing consumed */
#define IWYK_E_TOO_BIG        -6   /* payload over a size cap */
#define IWYK_E_BIND           -7   /* cannot bind the UDP port */
#define IWYK_E_CERT           -8   /* cannot read/create/write the certificate file */
#define IWYK_E_NO_IP          -9   /* no usable LAN IPv4; pass lan_ip */
#define IWYK_E_FULL          -10   /* per-session send queue over 8 MiB */
#define IWYK_E_INTERNAL      -11

/* event kinds */
#define IWYK_EV_OPENED 1   /* payload: phone address "ip:port" */
#define IWYK_EV_TEXT   2   /* payload: one text frame, verbatim UTF-8 */
#define IWYK_EV_BINARY 3   /* payload: one complete reassembled content transfer; transfer_id set */
#define IWYK_EV_CLOSED 4   /* code: close reason; payload: optional UTF-8 detail */
#define IWYK_EV_LOG    5   /* session 0; code: level; payload: UTF-8 message */

/* close reasons (CLOSED code) */
#define IWYK_CLOSE_HOST      1  /* host called iwyk_close */
#define IWYK_CLOSE_REMOTE    2  /* phone closed the channel or connection */
#define IWYK_CLOSE_TRANSPORT 3  /* ICE/DTLS/SCTP failure or timeout */
#define IWYK_CLOSE_PROTOCOL  4  /* frame over a cap, bad chunk, event queue overflow */
#define IWYK_CLOSE_STOPPED   5  /* never delivered: iwyk_stop discards the queue (listed for logs) */

/* log levels */
#define IWYK_LOG_DEBUG 0
#define IWYK_LOG_INFO  1
#define IWYK_LOG_WARN  2
#define IWYK_LOG_ERROR 3
#define IWYK_LOG_OFF   4

/* limits */
#define IWYK_MAX_TEXT       65536
#define IWYK_MAX_BINARY     131072
#define IWYK_CHUNK          16384
#define IWYK_MAX_SESSIONS   16
#define IWYK_QR_MAX_MODULES 31329   /* 177 * 177 */

typedef struct iwyk_event {
    int32_t kind;
    int32_t session;      /* > 0 for session events, 0 for LOG */
    int32_t transfer_id;  /* BINARY only, else 0 */
    int32_t code;         /* CLOSED: reason; LOG: level; else 0 */
    int32_t len;          /* payload bytes */
} iwyk_event;             /* 20 bytes, 5 x int32, no padding */

int32_t iwyk_abi_version(void);                                   /* returns IWYK_ABI_VERSION */
int32_t iwyk_version(uint8_t* out, int32_t cap);                  /* "iwyknet 1.0.0 abi 1 go1.27.4"; returns length */
int32_t iwyk_set_log_level(int32_t level);                        /* valid before start; default INFO */
int32_t iwyk_start(int32_t udp_port, const char* lan_ip, const char* cert_path);
int32_t iwyk_stop(void);                                          /* idempotent; OK when not started */
int32_t iwyk_endpoint(uint8_t* out, int32_t cap);                 /* "192.168.1.23:47000"; returns length */
int32_t iwyk_join_url(const char* base_url, uint8_t* out, int32_t cap); /* section 2.4; returns length */
int32_t iwyk_qr(const char* text, uint8_t* modules, int32_t cap, int32_t* side);
int32_t iwyk_poll(iwyk_event* ev, uint8_t* buf, int32_t cap);
int32_t iwyk_send_text(int32_t session, const uint8_t* utf8, int32_t len);
int32_t iwyk_send_binary(int32_t session, int32_t transfer_id, const uint8_t* data, int32_t len);
int32_t iwyk_close(int32_t session);
int32_t iwyk_buffered(int32_t session);                           /* bytes queued, not yet on the wire */
int32_t iwyk_last_error(uint8_t* out, int32_t cap);               /* message of the last failing call; returns length */

/* Phone page host: serves the page folder on 127.0.0.1 and publishes it through a Cloudflare
 * quick tunnel (cloudflared tunnel --url). Independent of iwyk_start. */
int32_t iwyk_pagehost_start(const char* page_dir, const char* cloudflared_path);
int32_t iwyk_pagehost_state(uint8_t* url, int32_t url_cap, uint8_t* detail, int32_t detail_cap);
                                                                  /* 0 starting, 1 ready (url), 2 failed (detail, retrying) */
int32_t iwyk_pagehost_stop(void);                                 /* idempotent */

#ifdef __cplusplus
}
#endif

#endif /* IWYKNET_H */
