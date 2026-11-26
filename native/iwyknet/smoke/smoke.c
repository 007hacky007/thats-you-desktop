/*
 * smoke: minimal host of iwyknet using only include/iwyknet.h.
 * Usage: smoke [cert_path] [lan_ip]
 * Prints "OK abi=1" and exits 0 when start, endpoint, join url, qr, poll
 * and stop behave; prints "FAIL ..." and exits 1 otherwise.
 */
#include <stdio.h>
#include <string.h>
#include "iwyknet.h"

static int fail(const char *what, int32_t rc) {
    uint8_t msg[512];
    int32_t n = iwyk_last_error(msg, (int32_t)sizeof msg - 1);
    if (n < 0) n = 0;
    msg[n] = 0;
    printf("FAIL %s rc=%d %s\n", what, (int)rc, (const char *)msg);
    iwyk_stop();
    return 1;
}

int main(int argc, char **argv) {
    const char *cert = argc > 1 ? argv[1] : "iwyknet-smoke.pem";
    const char *ip = argc > 2 ? argv[2] : "127.0.0.1";
    uint8_t buf[4096];
    static uint8_t modules[IWYK_QR_MAX_MODULES];
    static uint8_t payload[IWYK_MAX_BINARY];
    iwyk_event ev;
    int32_t rc, side = 0, i;

    if (sizeof(iwyk_event) != 20) return fail("sizeof(iwyk_event)", (int32_t)sizeof(iwyk_event));
    if (iwyk_abi_version() != IWYK_ABI_VERSION) return fail("abi", iwyk_abi_version());
    rc = iwyk_version(buf, (int32_t)sizeof buf - 1);
    if (rc <= 0) return fail("version", rc);
    buf[rc] = 0;
    printf("%s\n", (const char *)buf);
    if ((rc = iwyk_poll(&ev, payload, (int32_t)sizeof payload)) != IWYK_E_NOT_STARTED) return fail("poll before start", rc);
    if ((rc = iwyk_set_log_level(IWYK_LOG_DEBUG)) != IWYK_OK) return fail("log level", rc);
    if ((rc = iwyk_start(0, ip, cert)) != IWYK_OK) return fail("start", rc);
    if ((rc = iwyk_start(0, ip, cert)) != IWYK_E_ALREADY_STARTED) return fail("second start", rc);
    rc = iwyk_endpoint(buf, (int32_t)sizeof buf - 1);
    if (rc <= 0) return fail("endpoint", rc);
    buf[rc] = 0;
    printf("endpoint %s\n", (const char *)buf);
    rc = iwyk_join_url("https://example.invalid/iwyk/", buf, (int32_t)sizeof buf - 1);
    if (rc <= 0) return fail("join_url", rc);
    buf[rc] = 0;
    if (strncmp((const char *)buf, "https://example.invalid/iwyk/#h=", 32) != 0) return fail("join_url prefix", rc);
    if ((rc = iwyk_qr((const char *)buf, modules, 10, &side)) != IWYK_E_SMALLBUF || side < 21) return fail("qr small", rc);
    if ((rc = iwyk_qr((const char *)buf, modules, IWYK_QR_MAX_MODULES, &side)) != IWYK_OK) return fail("qr", rc);
    printf("qr side %d\n", (int)side);
    for (i = 0; i < 20; i++) {
        rc = iwyk_poll(&ev, payload, (int32_t)sizeof payload);
        if (rc < 0) return fail("poll", rc);
        if (rc == 1 && ev.kind == IWYK_EV_LOG) printf("log %d: %.*s\n", (int)ev.code, (int)ev.len, (const char *)payload);
    }
    if ((rc = iwyk_send_text(1, (const uint8_t *)"x", 1)) != IWYK_E_NO_SESSION) return fail("send to no session", rc);
    if ((rc = iwyk_stop()) != IWYK_OK) return fail("stop", rc);
    if ((rc = iwyk_stop()) != IWYK_OK) return fail("second stop", rc);
    printf("OK abi=%d\n", (int)iwyk_abi_version());
    return 0;
}
