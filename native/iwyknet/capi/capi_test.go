package main

import (
	"bufio"
	"context"
	"os"
	"path/filepath"
	"reflect"
	"regexp"
	"runtime"
	"strconv"
	"strings"
	"testing"
	"time"

	"iwyknet/client"
	"iwyknet/server"
)

func headerPath() string { return filepath.Join("..", "include", "iwyknet.h") }

func TestHeaderDefinesMatchGo(t *testing.T) {
	want := map[string]int{
		"IWYK_ABI_VERSION": server.ABIVersion,
		"IWYK_OK":          server.OK, "IWYK_E_NOT_STARTED": server.ENotStarted,
		"IWYK_E_ALREADY_STARTED": server.EAlreadyStarted, "IWYK_E_ARG": server.EArg,
		"IWYK_E_NO_SESSION": server.ENoSession, "IWYK_E_SMALLBUF": server.ESmallBuf,
		"IWYK_E_TOO_BIG": server.ETooBig, "IWYK_E_BIND": server.EBind, "IWYK_E_CERT": server.ECert,
		"IWYK_E_NO_IP": server.ENoIP, "IWYK_E_FULL": server.EFull, "IWYK_E_INTERNAL": server.EInternal,
		"IWYK_EV_OPENED": int(server.EventOpened), "IWYK_EV_TEXT": int(server.EventText),
		"IWYK_EV_BINARY": int(server.EventBinary), "IWYK_EV_CLOSED": int(server.EventClosed),
		"IWYK_EV_LOG":     int(server.EventLog),
		"IWYK_CLOSE_HOST": server.CloseHost, "IWYK_CLOSE_REMOTE": server.CloseRemote,
		"IWYK_CLOSE_TRANSPORT": server.CloseTransport, "IWYK_CLOSE_PROTOCOL": server.CloseProtocol,
		"IWYK_CLOSE_STOPPED": server.CloseStopped,
		"IWYK_LOG_DEBUG":     server.LogDebug, "IWYK_LOG_INFO": server.LogInfo, "IWYK_LOG_WARN": server.LogWarn,
		"IWYK_LOG_ERROR": server.LogError, "IWYK_LOG_OFF": server.LogOff,
		"IWYK_MAX_TEXT": server.MaxText, "IWYK_MAX_BINARY": server.MaxBinary, "IWYK_CHUNK": server.Chunk,
		"IWYK_MAX_SESSIONS": server.MaxSessions, "IWYK_QR_MAX_MODULES": server.QRMaxModules,
	}
	f, err := os.Open(headerPath())
	if err != nil {
		t.Fatal(err)
	}
	defer f.Close()
	re := regexp.MustCompile(`^#define\s+(IWYK_\w+)\s+(-?\d+)`)
	got := map[string]int{}
	sc := bufio.NewScanner(f)
	for sc.Scan() {
		if m := re.FindStringSubmatch(sc.Text()); m != nil {
			v, _ := strconv.Atoi(m[2])
			got[m[1]] = v
		}
	}
	if !reflect.DeepEqual(got, want) {
		t.Fatalf("header defines differ:\n got %v\nwant %v", got, want)
	}
	if server.QRMaxModules != 177*177 {
		t.Fatal("QR max")
	}
}

func TestHeaderFunctionsAreExported(t *testing.T) {
	h, _ := os.ReadFile(headerPath())
	src, _ := os.ReadFile("exports.go")
	fre := regexp.MustCompile(`(?m)^int32_t (iwyk_\w+)\(`)
	ere := regexp.MustCompile(`(?m)^//export (iwyk_\w+)$`)
	var hf, ef []string
	for _, m := range fre.FindAllStringSubmatch(string(h), -1) {
		hf = append(hf, m[1])
	}
	for _, m := range ere.FindAllStringSubmatch(string(src), -1) {
		ef = append(ef, m[1])
	}
	if len(hf) != 14 || !reflect.DeepEqual(hf, ef) {
		t.Fatalf("header %v\nexports %v", hf, ef)
	}
	if cEventSize() != 20 {
		t.Fatalf("sizeof(iwyk_event) = %d", cEventSize())
	}
}

func TestVersionAndLastError(t *testing.T) {
	buf := make([]byte, 64)
	n := apiVersion(buf)
	want := "iwyknet 1.0.0 abi 1 " + runtime.Version()
	if n != int32(len(want)) || string(buf[:n]) != want {
		t.Fatalf("version %q", buf[:max(n, 0)])
	}
	if rc := apiVersion(make([]byte, 5)); rc != server.ESmallBuf {
		t.Fatalf("small version buffer %d", rc)
	}
	if rc := apiSetLogLevel(7); rc != server.EArg {
		t.Fatalf("level 7: %d", rc)
	}
	n = apiLastError(buf)
	if n <= 0 || !strings.Contains(string(buf[:n]), "log level") {
		t.Fatalf("last error %q", buf[:max(n, 0)])
	}
	if rc := apiLastError(make([]byte, 2)); rc != server.ESmallBuf {
		t.Fatalf("small last error %d", rc)
	}
	if n2 := apiLastError(buf); n2 != n {
		t.Fatal("last error was overwritten by a small-buffer read")
	}
	if rc := apiSetLogLevel(server.LogOff); rc != server.OK {
		t.Fatal(rc)
	}
}

func TestQRSideRules(t *testing.T) {
	var side int32 = -1
	if rc := apiQR("hello", make([]byte, 10), &side); rc != server.ESmallBuf || side != 21 {
		t.Fatalf("small: rc %d side %d", rc, side)
	}
	mods := make([]byte, server.QRMaxModules)
	if rc := apiQR("hello", mods, &side); rc != server.OK || side != 21 {
		t.Fatalf("ok: rc %d side %d", rc, side)
	}
	if rc := apiQR("", mods, &side); rc != server.EArg || side != 0 {
		t.Fatalf("empty: rc %d side %d", rc, side)
	}
	if rc := apiQR(strings.Repeat("x", 5000), mods, &side); rc != server.EArg {
		t.Fatalf("too long: rc %d", rc)
	}
}

func TestNotStartedAndStop(t *testing.T) {
	apiStop()
	var ev event
	buf := make([]byte, 100)
	for name, rc := range map[string]int32{
		"poll":     apiPoll(&ev, buf),
		"endpoint": apiEndpoint(buf),
		"join":     apiJoinURL("https://example.invalid/iwyk/", buf),
		"text":     apiSendText(1, []byte("x")),
		"binary":   apiSendBinary(1, 1, []byte("x")),
		"close":    apiClose(1),
		"buffered": apiBuffered(1),
	} {
		if rc != server.ENotStarted {
			t.Errorf("%s: %d", name, rc)
		}
	}
	if apiStop() != server.OK || apiStop() != server.OK {
		t.Fatal("stop not idempotent")
	}
}

func TestStartedLifecycle(t *testing.T) {
	apiSetLogLevel(server.LogOff)
	cert := filepath.Join(t.TempDir(), "c.pem")
	if rc := apiStart(70000, "127.0.0.1", cert); rc != server.EArg {
		t.Fatalf("port: %d", rc)
	}
	if rc := apiStart(0, "::1", cert); rc != server.EArg {
		t.Fatalf("ipv6: %d", rc)
	}
	if rc := apiStart(0, "127.0.0.1", ""); rc != server.EArg {
		t.Fatalf("no cert: %d", rc)
	}
	if rc := apiStart(0, "127.0.0.1", cert); rc != server.OK {
		t.Fatalf("start: %d", rc)
	}
	defer apiStop()
	if rc := apiStart(0, "127.0.0.1", cert); rc != server.EAlreadyStarted {
		t.Fatalf("second start: %d", rc)
	}
	buf := make([]byte, 4096)
	n := apiEndpoint(buf)
	if n <= 0 || !strings.HasPrefix(string(buf[:n]), "127.0.0.1:") {
		t.Fatalf("endpoint %q", buf[:max(n, 0)])
	}
	if rc := apiEndpoint(make([]byte, 3)); rc != server.ESmallBuf {
		t.Fatalf("small endpoint %d", rc)
	}
	if rc := apiJoinURL("ftp://x/", buf); rc != server.EArg {
		t.Fatalf("ftp %d", rc)
	}
	if rc := apiJoinURL("https://example.invalid/iwyk/", make([]byte, 20)); rc != server.ESmallBuf {
		t.Fatalf("small join %d", rc)
	}
	n = apiJoinURL("https://example.invalid/iwyk/#drop", buf)
	if n <= 0 {
		t.Fatalf("join %d", n)
	}
	join := string(buf[:n])
	if !regexp.MustCompile(`^https://example\.invalid/iwyk/#h=127\.0\.0\.1&p=\d+&u=[A-Za-z0-9]{8}&w=[A-Za-z0-9]{24}&f=[A-Za-z0-9_-]{43}&t=[A-Za-z0-9]{16}$`).MatchString(join) {
		t.Fatalf("join url %s", join)
	}
	var ev event
	if rc := apiPoll(&ev, buf); rc != 0 {
		t.Fatalf("empty poll %d", rc)
	}

	_, p, err := client.ParseJoinURL(join)
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	c, err := client.Dial(ctx, p, client.Options{PingInterval: -1})
	if err != nil {
		t.Fatal(err)
	}
	defer c.Close()
	c.Hello("capi-dev")

	// SMALLBUF leaves the OPENED event queued.
	small := make([]byte, 2)
	deadline := time.Now().Add(10 * time.Second)
	var rc int32
	for time.Now().Before(deadline) {
		ev = event{}
		rc = apiPoll(&ev, small)
		if rc != 0 {
			break
		}
		time.Sleep(10 * time.Millisecond)
	}
	if rc != server.ESmallBuf || ev.Kind != int32(server.EventOpened) || ev.Len <= 2 {
		t.Fatalf("small poll rc %d ev %+v", rc, ev)
	}
	ev = event{}
	if rc := apiPoll(&ev, buf); rc != 1 || ev.Kind != int32(server.EventOpened) || ev.Session <= 0 {
		t.Fatalf("poll opened rc %d %+v", rc, ev)
	}
	session := ev.Session
	for time.Now().Before(deadline) {
		if rc = apiPoll(&ev, buf); rc != 0 {
			break
		}
		time.Sleep(10 * time.Millisecond)
	}
	if rc != 1 || ev.Kind != int32(server.EventText) || !strings.Contains(string(buf[:ev.Len]), `"uid":"capi-dev"`) {
		t.Fatalf("hello rc %d %+v", rc, ev)
	}

	if rc := apiSendText(session, nil); rc != server.ETooBig {
		t.Fatalf("empty text %d", rc)
	}
	if rc := apiSendBinary(session, 0, []byte{1}); rc != server.EArg {
		t.Fatalf("tid 0 %d", rc)
	}
	if rc := apiSendText(session+1000, []byte("x")); rc != server.ENoSession {
		t.Fatalf("unknown session %d", rc)
	}
	if rc := apiSendText(session, []byte(`{"t":"joined"}`)); rc != server.OK {
		t.Fatalf("send %d", rc)
	}
	if rc := apiSendBinary(session, 5, make([]byte, 20000)); rc != server.OK {
		t.Fatalf("binary %d", rc)
	}
	if rc := apiBuffered(session); rc < 0 {
		t.Fatalf("buffered %d", rc)
	}
	f, err := c.Next(ctx)
	if err != nil || f.T != "joined" {
		t.Fatalf("joined %+v %v", f, err)
	}
	f, err = c.Next(ctx)
	if err != nil || f.TransferID != 5 || len(f.Data) != 20000 {
		t.Fatalf("binary back %d %v", len(f.Data), err)
	}
	if rc := apiClose(session); rc != server.OK {
		t.Fatalf("close %d", rc)
	}
	if rc := apiClose(session); rc != server.ENoSession {
		t.Fatalf("close twice %d", rc)
	}
	for time.Now().Before(deadline) {
		if rc = apiPoll(&ev, buf); rc == 1 && ev.Kind == int32(server.EventClosed) {
			break
		}
		time.Sleep(10 * time.Millisecond)
	}
	if ev.Kind != int32(server.EventClosed) || ev.Code != server.CloseHost || ev.Session != session {
		t.Fatalf("closed %+v", ev)
	}
	if apiStop() != server.OK {
		t.Fatal("stop")
	}
	if rc := apiPoll(&ev, buf); rc != server.ENotStarted {
		t.Fatalf("poll after stop %d", rc)
	}
	// Restart on a fresh port works and keeps the certificate.
	if rc := apiStart(0, "127.0.0.1", cert); rc != server.OK {
		t.Fatalf("restart %d", rc)
	}
}
