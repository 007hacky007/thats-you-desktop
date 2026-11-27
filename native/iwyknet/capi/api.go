// Command capi is built with -buildmode=c-shared into iwyknet.dylib /
// iwyknet.dll. The C ABI is include/iwyknet.h; exports.go holds the thin
// cgo wrappers, this file the logic on plain Go slices so it is testable.
package main

import (
	"errors"
	"fmt"
	"runtime"
	"strconv"
	"sync"

	"iwyknet/pagehost"
	"iwyknet/server"
)

// event mirrors iwyk_event.
type event struct {
	Kind, Session, TransferID, Code, Len int32
}

var (
	gmu    sync.Mutex
	gsrv   *server.Server
	glevel = server.LogInfo

	errMu   sync.Mutex
	lastErr string
)

func fail(code int32, err error) int32 {
	errMu.Lock()
	lastErr = err.Error()
	errMu.Unlock()
	return code
}

func failErr(err error) int32 { return fail(server.Code(err), err) }

func errArg(format string, a ...any) int32 {
	return fail(server.EArg, fmt.Errorf(format, a...))
}

// writeOut copies s into out (NUL-terminated when there is room) and
// returns its length, or E_SMALLBUF.
func writeOut(s string, out []byte) int32 {
	if len(s) > len(out) {
		return fail(server.ESmallBuf, fmt.Errorf("need %d bytes, have %d", len(s), len(out)))
	}
	copy(out, s)
	if len(s) < len(out) {
		out[len(s)] = 0
	}
	return int32(len(s))
}

func versionString() string {
	return "iwyknet " + server.Version + " abi " + strconv.Itoa(server.ABIVersion) + " " + runtime.Version()
}

func apiVersion(out []byte) int32 { return writeOut(versionString(), out) }

func apiSetLogLevel(level int32) int32 {
	if level < server.LogDebug || level > server.LogOff {
		return errArg("log level %d", level)
	}
	gmu.Lock()
	defer gmu.Unlock()
	glevel = int(level)
	if gsrv != nil {
		_ = gsrv.SetLogLevel(int(level))
	}
	return server.OK
}

func apiStart(port int32, lanIP, certPath string) int32 {
	gmu.Lock()
	defer gmu.Unlock()
	if gsrv != nil {
		return fail(server.EAlreadyStarted, errors.New("already started; call iwyk_stop first"))
	}
	if port < 0 || port > 65535 {
		return errArg("udp port %d", port)
	}
	if certPath == "" {
		return errArg("cert_path is required")
	}
	s, err := server.Start(server.Config{UDPPort: int(port), LANIP: lanIP, CertPath: certPath, LogLevel: glevel})
	if err != nil {
		return failErr(err)
	}
	gsrv = s
	return server.OK
}

func apiStop() int32 {
	gmu.Lock()
	defer gmu.Unlock()
	if gsrv != nil {
		gsrv.Stop()
		gsrv = nil
	}
	return server.OK
}

func started() (*server.Server, int32) {
	if gsrv == nil {
		return nil, fail(server.ENotStarted, errors.New("not started"))
	}
	return gsrv, server.OK
}

func apiEndpoint(out []byte) int32 {
	gmu.Lock()
	defer gmu.Unlock()
	s, rc := started()
	if s == nil {
		return rc
	}
	ip, port := s.Endpoint()
	return writeOut(ip+":"+strconv.Itoa(port), out)
}

func apiJoinURL(base string, out []byte) int32 {
	gmu.Lock()
	defer gmu.Unlock()
	s, rc := started()
	if s == nil {
		return rc
	}
	u, err := s.JoinURL(base)
	if err != nil {
		return failErr(err)
	}
	return writeOut(u, out)
}

func apiQR(text string, modules []byte, side *int32) int32 {
	n, m, err := server.QR(text)
	*side = int32(n)
	if err != nil {
		return failErr(err)
	}
	if len(modules) < len(m) {
		return fail(server.ESmallBuf, fmt.Errorf("QR needs %d bytes, have %d", len(m), len(modules)))
	}
	copy(modules, m)
	return server.OK
}

func apiPoll(ev *event, buf []byte) int32 {
	gmu.Lock()
	defer gmu.Unlock()
	s, rc := started()
	if s == nil {
		return rc
	}
	e, ok, err := s.PollLimit(len(buf))
	if err != nil {
		*ev = event{Kind: int32(e.Kind), Session: e.Session, TransferID: e.TransferID, Code: e.Code, Len: int32(len(e.Data))}
		return failErr(err)
	}
	if !ok {
		return 0
	}
	copy(buf, e.Data)
	*ev = event{Kind: int32(e.Kind), Session: e.Session, TransferID: e.TransferID, Code: e.Code, Len: int32(len(e.Data))}
	return 1
}

func apiSendText(session int32, data []byte) int32 {
	gmu.Lock()
	defer gmu.Unlock()
	s, rc := started()
	if s == nil {
		return rc
	}
	if err := s.SendText(session, data); err != nil {
		return failErr(err)
	}
	return server.OK
}

func apiSendBinary(session, transferID int32, data []byte) int32 {
	gmu.Lock()
	defer gmu.Unlock()
	s, rc := started()
	if s == nil {
		return rc
	}
	if err := s.SendBinary(session, transferID, data); err != nil {
		return failErr(err)
	}
	return server.OK
}

func apiClose(session int32) int32 {
	gmu.Lock()
	defer gmu.Unlock()
	s, rc := started()
	if s == nil {
		return rc
	}
	if err := s.Close(session); err != nil {
		return failErr(err)
	}
	return server.OK
}

func apiBuffered(session int32) int32 {
	gmu.Lock()
	defer gmu.Unlock()
	s, rc := started()
	if s == nil {
		return rc
	}
	n, err := s.Buffered(session)
	if err != nil {
		return failErr(err)
	}
	return int32(min(n, 1<<31-1))
}

func apiLastError(out []byte) int32 {
	errMu.Lock()
	msg := lastErr
	errMu.Unlock()
	if len(msg) > len(out) {
		// Do not overwrite the stored message with this failure.
		return server.ESmallBuf
	}
	return writeOut(msg, out)
}

var (
	pmu   sync.Mutex
	phost *pagehost.Host
)

// apiPageHostStart serves the phone page in dir and opens a quick tunnel with cloudflared.
func apiPageHostStart(dir, cloudflared string) int32 {
	pmu.Lock()
	defer pmu.Unlock()
	if phost != nil {
		return fail(server.EAlreadyStarted, fmt.Errorf("page host already started"))
	}
	if dir == "" || cloudflared == "" {
		return errArg("page folder and cloudflared path are required")
	}
	h, err := pagehost.Start(pagehost.Config{Dir: dir, Cloudflared: cloudflared})
	if err != nil {
		return fail(server.EArg, err)
	}
	phost = h
	return 0
}

// apiPageHostState: 0 starting, 1 ready (url filled), 2 failed (detail filled).
func apiPageHostState(url, detail []byte) int32 {
	pmu.Lock()
	h := phost
	pmu.Unlock()
	if h == nil {
		return fail(server.ENotStarted, fmt.Errorf("page host not started"))
	}
	st, u, d := h.State()
	if rc := writeOut(u, url); rc < 0 {
		return rc
	}
	if len(d) > len(detail) {
		d = d[:len(detail)]
	}
	if rc := writeOut(d, detail); rc < 0 {
		return rc
	}
	return int32(st)
}

func apiPageHostStop() int32 {
	pmu.Lock()
	h := phost
	phost = nil
	pmu.Unlock()
	if h != nil {
		h.Stop()
	}
	return 0
}

func main() {}
