package main

/*
#include <stdint.h>
typedef struct iwyk_event {
    int32_t kind;
    int32_t session;
    int32_t transfer_id;
    int32_t code;
    int32_t len;
} iwyk_event;
*/
import "C"

import (
	"unsafe"

	"iwyknet/server"
)

// cEventSize is sizeof(iwyk_event) as cgo sees it (tests check 20).
func cEventSize() uintptr { return unsafe.Sizeof(C.iwyk_event{}) }

// outBuf turns (ptr, cap) into a slice; ok is false for a bad pair.
func outBuf(p *C.uint8_t, n C.int32_t) ([]byte, bool) {
	if n < 0 || (p == nil && n > 0) {
		return nil, false
	}
	if n == 0 {
		return []byte{}, true
	}
	return unsafe.Slice((*byte)(unsafe.Pointer(p)), int(n)), true
}

// inBuf copies (ptr, len) so no caller pointer is kept.
func inBuf(p *C.uint8_t, n C.int32_t) ([]byte, bool) {
	if n < 0 || (p == nil && n > 0) {
		return nil, false
	}
	if n == 0 {
		return nil, true
	}
	return C.GoBytes(unsafe.Pointer(p), C.int(n)), true
}

func goStr(s *C.char) string {
	if s == nil {
		return ""
	}
	return C.GoString(s)
}

//export iwyk_abi_version
func iwyk_abi_version() C.int32_t { return server.ABIVersion }

//export iwyk_version
func iwyk_version(out *C.uint8_t, n C.int32_t) C.int32_t {
	b, ok := outBuf(out, n)
	if !ok {
		return C.int32_t(errArg("bad output buffer"))
	}
	return C.int32_t(apiVersion(b))
}

//export iwyk_set_log_level
func iwyk_set_log_level(level C.int32_t) C.int32_t {
	return C.int32_t(apiSetLogLevel(int32(level)))
}

//export iwyk_start
func iwyk_start(port C.int32_t, lanIP *C.char, certPath *C.char) C.int32_t {
	if certPath == nil {
		return C.int32_t(errArg("cert_path is NULL"))
	}
	return C.int32_t(apiStart(int32(port), goStr(lanIP), goStr(certPath)))
}

//export iwyk_stop
func iwyk_stop() C.int32_t { return C.int32_t(apiStop()) }

//export iwyk_endpoint
func iwyk_endpoint(out *C.uint8_t, n C.int32_t) C.int32_t {
	b, ok := outBuf(out, n)
	if !ok {
		return C.int32_t(errArg("bad output buffer"))
	}
	return C.int32_t(apiEndpoint(b))
}

//export iwyk_join_url
func iwyk_join_url(base *C.char, out *C.uint8_t, n C.int32_t) C.int32_t {
	b, ok := outBuf(out, n)
	if !ok || base == nil {
		return C.int32_t(errArg("bad argument"))
	}
	return C.int32_t(apiJoinURL(goStr(base), b))
}

//export iwyk_qr
func iwyk_qr(text *C.char, modules *C.uint8_t, n C.int32_t, side *C.int32_t) C.int32_t {
	if side == nil {
		return C.int32_t(errArg("side is NULL"))
	}
	*side = 0
	b, ok := outBuf(modules, n)
	if !ok || text == nil {
		return C.int32_t(errArg("bad argument"))
	}
	var s int32
	rc := apiQR(goStr(text), b, &s)
	*side = C.int32_t(s)
	return C.int32_t(rc)
}

//export iwyk_poll
func iwyk_poll(ev *C.iwyk_event, buf *C.uint8_t, n C.int32_t) C.int32_t {
	b, ok := outBuf(buf, n)
	if !ok || ev == nil {
		return C.int32_t(errArg("bad argument"))
	}
	var e event
	rc := apiPoll(&e, b)
	if rc == 1 || rc == server.ESmallBuf {
		ev.kind = C.int32_t(e.Kind)
		ev.session = C.int32_t(e.Session)
		ev.transfer_id = C.int32_t(e.TransferID)
		ev.code = C.int32_t(e.Code)
		ev.len = C.int32_t(e.Len)
	}
	return C.int32_t(rc)
}

//export iwyk_send_text
func iwyk_send_text(session C.int32_t, data *C.uint8_t, n C.int32_t) C.int32_t {
	if n > server.MaxText {
		return C.int32_t(fail(server.ETooBig, server.ErrTooBig))
	}
	b, ok := inBuf(data, n)
	if !ok {
		return C.int32_t(errArg("bad buffer"))
	}
	return C.int32_t(apiSendText(int32(session), b))
}

//export iwyk_send_binary
func iwyk_send_binary(session C.int32_t, transferID C.int32_t, data *C.uint8_t, n C.int32_t) C.int32_t {
	if n > server.MaxBinary {
		return C.int32_t(fail(server.ETooBig, server.ErrTooBig))
	}
	b, ok := inBuf(data, n)
	if !ok {
		return C.int32_t(errArg("bad buffer"))
	}
	return C.int32_t(apiSendBinary(int32(session), int32(transferID), b))
}

//export iwyk_close
func iwyk_close(session C.int32_t) C.int32_t { return C.int32_t(apiClose(int32(session))) }

//export iwyk_buffered
func iwyk_buffered(session C.int32_t) C.int32_t { return C.int32_t(apiBuffered(int32(session))) }

//export iwyk_last_error
func iwyk_last_error(out *C.uint8_t, n C.int32_t) C.int32_t {
	b, ok := outBuf(out, n)
	if !ok {
		return server.EArg
	}
	return C.int32_t(apiLastError(b))
}

//export iwyk_pagehost_start
func iwyk_pagehost_start(dir *C.char, cloudflared *C.char) C.int32_t {
	return C.int32_t(apiPageHostStart(goStr(dir), goStr(cloudflared)))
}

//export iwyk_pagehost_state
func iwyk_pagehost_state(url *C.uint8_t, urlLen C.int32_t, detail *C.uint8_t, detailLen C.int32_t) C.int32_t {
	u, ok1 := outBuf(url, urlLen)
	d, ok2 := outBuf(detail, detailLen)
	if !ok1 || !ok2 {
		return C.int32_t(errArg("bad output buffer"))
	}
	return C.int32_t(apiPageHostState(u, d))
}

//export iwyk_pagehost_stop
func iwyk_pagehost_stop() C.int32_t { return C.int32_t(apiPageHostStop()) }
