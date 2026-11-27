package server_test

import (
	"bytes"
	"context"
	"crypto/rand"
	"encoding/binary"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net"
	"path/filepath"
	"strings"
	"sync"
	"testing"
	"time"

	"iwyknet/client"
	"iwyknet/server"
)

const testBase = "http://127.0.0.1:8088/"

func startServer(t *testing.T, mod func(*server.Config)) *server.Server {
	t.Helper()
	cfg := server.Config{
		UDPPort:      0,
		LANIP:        "127.0.0.1",
		CertPath:     filepath.Join(t.TempDir(), "dtls.pem"),
		PingInterval: 200 * time.Millisecond,
		LogLevel:     server.LogOff,
	}
	if mod != nil {
		mod(&cfg)
	}
	s, err := server.Start(cfg)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(s.Stop)
	return s
}

func params(t *testing.T, s *server.Server) client.Params {
	t.Helper()
	u, err := s.JoinURL(testBase)
	if err != nil {
		t.Fatal(err)
	}
	base, p, err := client.ParseJoinURL(u)
	if err != nil || base != testBase {
		t.Fatalf("parse %q: %v", base, err)
	}
	return p
}

func dial(t *testing.T, s *server.Server, o client.Options) *client.Client {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	c, err := client.Dial(ctx, params(t, s), o)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { c.Close() })
	return c
}

// events collects server events in the background.
type events struct {
	mu  sync.Mutex
	all []server.Event
	ch  chan struct{}
}

func collect(t *testing.T, s *server.Server) *events {
	e := &events{ch: make(chan struct{}, 1)}
	ctx, cancel := context.WithCancel(context.Background())
	t.Cleanup(cancel)
	go func() {
		for {
			ev, err := s.Next(ctx)
			if err != nil {
				return
			}
			if ev.Kind == server.EventLog {
				continue
			}
			e.mu.Lock()
			e.all = append(e.all, ev)
			e.mu.Unlock()
			select {
			case e.ch <- struct{}{}:
			default:
			}
		}
	}()
	return e
}

func (e *events) snapshot() []server.Event {
	e.mu.Lock()
	defer e.mu.Unlock()
	return append([]server.Event(nil), e.all...)
}

// waitFor waits until pred holds for the events of a session.
func (e *events) waitFor(t *testing.T, d time.Duration, what string, pred func([]server.Event) bool) []server.Event {
	t.Helper()
	deadline := time.After(d)
	for {
		if all := e.snapshot(); pred(all) {
			return all
		}
		select {
		case <-e.ch:
		case <-deadline:
			t.Fatalf("timeout waiting for %s; events: %s", what, describe(e.snapshot()))
			return nil
		}
	}
}

func describe(evs []server.Event) string {
	var b strings.Builder
	for _, ev := range evs {
		fmt.Fprintf(&b, "[s%d k%d c%d len%d %.40q] ", ev.Session, ev.Kind, ev.Code, len(ev.Data), ev.Data)
	}
	return b.String()
}

func ofSession(evs []server.Event, s int32) []server.Event {
	var out []server.Event
	for _, ev := range evs {
		if ev.Session == s {
			out = append(out, ev)
		}
	}
	return out
}

func hasClosed(evs []server.Event, s int32) bool {
	for _, ev := range ofSession(evs, s) {
		if ev.Kind == server.EventClosed {
			return true
		}
	}
	return false
}

func nextFrame(t *testing.T, c *client.Client, d time.Duration) (client.Frame, error) {
	ctx, cancel := context.WithTimeout(context.Background(), d)
	defer cancel()
	return c.Next(ctx)
}

func waitClosed(t *testing.T, c *client.Client, d time.Duration) {
	t.Helper()
	select {
	case <-c.Done():
	case <-time.After(d):
		t.Fatal("client was not closed")
	}
}

// joinOne dials, says hello and returns the session id.
func joinOne(t *testing.T, s *server.Server, e *events, uid string) (*client.Client, int32) {
	t.Helper()
	c := dial(t, s, client.Options{PingInterval: -1})
	if err := c.Hello(uid); err != nil {
		t.Fatal(err)
	}
	var id int32
	e.waitFor(t, 10*time.Second, "hello of "+uid, func(evs []server.Event) bool {
		for _, ev := range evs {
			if ev.Kind == server.EventText && bytes.Contains(ev.Data, []byte(`"uid":"`+uid+`"`)) {
				id = ev.Session
				return true
			}
		}
		return false
	})
	return c, id
}

func TestThreeClientsGetSessions(t *testing.T) {
	s := startServer(t, nil)
	e := collect(t, s)
	var wg sync.WaitGroup
	ids := make([]int32, 3)
	for i := range 3 {
		wg.Add(1)
		go func() {
			defer wg.Done()
			ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
			defer cancel()
			c, err := client.Dial(ctx, params(t, s), client.Options{PingInterval: -1})
			if err != nil {
				t.Error(err)
				return
			}
			t.Cleanup(func() { c.Close() })
			c.Hello(fmt.Sprintf("dev-%d", i))
		}()
	}
	wg.Wait()
	evs := e.waitFor(t, 10*time.Second, "three hellos", func(evs []server.Event) bool {
		n := 0
		for _, ev := range evs {
			if ev.Kind == server.EventText {
				n++
			}
		}
		return n == 3
	})
	seen := map[int32]bool{}
	for _, ev := range evs {
		if ev.Kind == server.EventText {
			seen[ev.Session] = true
		}
	}
	if len(seen) != 3 {
		t.Fatalf("sessions %v", seen)
	}
	i := 0
	for sid := range seen {
		ids[i] = sid
		i++
		evs := ofSession(evs, sid)
		if len(evs) < 2 || evs[0].Kind != server.EventOpened || evs[1].Kind != server.EventText {
			t.Fatalf("session %d order: %s", sid, describe(evs))
		}
		// the peer's address: loopback, or the host's LAN address when ICE picks that pair
		if host, port, err := net.SplitHostPort(string(evs[0].Data)); err != nil || net.ParseIP(host).To4() == nil || port == "" {
			t.Fatalf("opened payload %q", evs[0].Data)
		}
		var h map[string]string
		if json.Unmarshal(evs[1].Data, &h) != nil || h["t"] != "hello" || !strings.HasPrefix(h["uid"], "dev-") {
			t.Fatalf("hello payload %q", evs[1].Data)
		}
	}
}

func TestBadHellosAreSilent(t *testing.T) {
	s := startServer(t, func(c *server.Config) { c.HelloTimeout = 500 * time.Millisecond })
	e := collect(t, s)
	p := params(t, s)

	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	wrong, err := client.Dial(ctx, p, client.Options{PingInterval: -1})
	if err != nil {
		t.Fatal(err)
	}
	defer wrong.Close()
	wrong.HelloToken("WrongToken123456", "a")

	notHello, err := client.Dial(ctx, p, client.Options{PingInterval: -1})
	if err != nil {
		t.Fatal(err)
	}
	defer notHello.Close()
	notHello.SendText([]byte(`{"t":"ping"}`))

	binary1, err := client.Dial(ctx, p, client.Options{PingInterval: -1})
	if err != nil {
		t.Fatal(err)
	}
	defer binary1.Close()
	binary1.SendContent(1, []byte("x"))

	late, err := client.Dial(ctx, p, client.Options{PingInterval: -1})
	if err != nil {
		t.Fatal(err)
	}
	defer late.Close()

	for _, c := range []*client.Client{wrong, notHello, binary1, late} {
		waitClosed(t, c, 5*time.Second)
	}
	if err := late.Hello("late"); err == nil {
		// The send may race with the close; either way no event may appear.
	}
	time.Sleep(200 * time.Millisecond)
	if evs := e.snapshot(); len(evs) != 0 {
		t.Fatalf("events for unauthenticated peers: %s", describe(evs))
	}
	// A good client still works afterwards.
	_, id := joinOne(t, s, e, "good")
	if id <= 0 {
		t.Fatal("no session")
	}
}

func TestTextCapsAndBadChunksCloseProtocol(t *testing.T) {
	s := startServer(t, nil)
	e := collect(t, s)

	c1, id1 := joinOne(t, s, e, "big")
	ok := []byte(`{"t":"x","p":"` + strings.Repeat("a", server.MaxText-16) + `"}`)
	if len(ok) != server.MaxText {
		t.Fatalf("len %d", len(ok))
	}
	c1.SendText(ok)
	c1.SendText(append(ok, ' '))
	evs := e.waitFor(t, 10*time.Second, "protocol close", func(evs []server.Event) bool { return hasClosed(evs, id1) })
	se := ofSession(evs, id1)
	if len(se) != 4 || len(se[2].Data) != server.MaxText || se[3].Code != server.CloseProtocol {
		t.Fatalf("session events: %s", describe(se))
	}
	waitClosed(t, c1, 5*time.Second)

	bad := [][]byte{
		mkChunk(5, 10, 3, 4),                 // first offset not zero
		mkChunk(5, server.MaxBinary+1, 0, 4), // total too large
		mkChunk(5, 0, 0, 4),                  // total zero
		mkChunk(0, 10, 0, 10),                // transfer id 0
		make([]byte, 12),                     // no payload
		mkChunk(5, 40000, 0, server.Chunk+1), // payload too large
	}
	for i, b := range bad {
		c, id := joinOne(t, s, e, fmt.Sprintf("chunk-%d", i))
		c.SendBinary(b)
		evs := e.waitFor(t, 10*time.Second, "bad chunk close", func(evs []server.Event) bool { return hasClosed(evs, id) })
		se := ofSession(evs, id)
		if last := se[len(se)-1]; last.Code != server.CloseProtocol {
			t.Fatalf("chunk case %d: %s", i, describe(se))
		}
	}
	// Five partial transfers.
	c, id := joinOne(t, s, e, "partials")
	for tid := int32(1); tid <= 5; tid++ {
		c.SendBinary(mkChunk(tid, 30000, 0, 100))
	}
	evs = e.waitFor(t, 10*time.Second, "partials close", func(evs []server.Event) bool { return hasClosed(evs, id) })
	if se := ofSession(evs, id); se[len(se)-1].Code != server.CloseProtocol {
		t.Fatalf("partials: %s", describe(se))
	}
}

func mkChunk(id int32, total, off uint32, n int) []byte {
	f := make([]byte, 12+n)
	binary.LittleEndian.PutUint32(f[0:], uint32(id))
	binary.LittleEndian.PutUint32(f[4:], total)
	binary.LittleEndian.PutUint32(f[8:], off)
	return f
}

func TestBinaryRoundTrips(t *testing.T) {
	s := startServer(t, nil)
	e := collect(t, s)
	c, id := joinOne(t, s, e, "bin")
	for i, n := range []int{1, 16384, 16385, 131072} {
		data := make([]byte, n)
		rand.Read(data)
		tid := int32(100 + i)
		// phone -> game
		if err := c.SendContent(tid, data); err != nil {
			t.Fatal(err)
		}
		e.waitFor(t, 10*time.Second, fmt.Sprintf("binary %d", n), func(evs []server.Event) bool {
			for _, ev := range ofSession(evs, id) {
				if ev.Kind == server.EventBinary && ev.TransferID == tid {
					if !bytes.Equal(ev.Data, data) {
						t.Fatalf("content mismatch %d", n)
					}
					return true
				}
			}
			return false
		})
		// game -> phone, with a text frame on each side to check ordering.
		if err := s.SendText(id, []byte(`{"t":"msg","id":-7503,"json":{"TypeString":"A"}}`)); err != nil {
			t.Fatal(err)
		}
		if err := s.SendBinary(id, tid, data); err != nil {
			t.Fatal(err)
		}
		if err := s.SendText(id, []byte(`{"t":"msg","id":-7503,"json":{"TypeString":"B"}}`)); err != nil {
			t.Fatal(err)
		}
		f1, err := nextFrame(t, c, 10*time.Second)
		if err != nil || f1.T != "msg" || string(f1.JSON) != `{"TypeString":"A"}` {
			t.Fatalf("frame A: %+v %v", f1, err)
		}
		f2, err := nextFrame(t, c, 10*time.Second)
		if err != nil || f2.TransferID != tid || !bytes.Equal(f2.Data, data) {
			t.Fatalf("binary %d back: id %d len %d %v", n, f2.TransferID, len(f2.Data), err)
		}
		f3, err := nextFrame(t, c, 10*time.Second)
		if err != nil || string(f3.JSON) != `{"TypeString":"B"}` {
			t.Fatalf("frame B: %+v %v", f3, err)
		}
	}
	if err := s.SendBinary(id, 0, []byte{1}); !errors.Is(err, server.ErrArg) {
		t.Fatalf("id 0: %v", err)
	}
	if err := s.SendBinary(id, 1, make([]byte, server.MaxBinary+1)); !errors.Is(err, server.ErrTooBig) {
		t.Fatalf("too big: %v", err)
	}
	if err := s.SendText(id, make([]byte, server.MaxText+1)); !errors.Is(err, server.ErrTooBig) {
		t.Fatalf("text too big: %v", err)
	}
	if err := s.SendText(id, nil); !errors.Is(err, server.ErrTooBig) {
		t.Fatalf("empty text: %v", err)
	}
}

func TestHostCloseAndRemoteClose(t *testing.T) {
	s := startServer(t, nil)
	e := collect(t, s)
	c1, id1 := joinOne(t, s, e, "one")
	c2, id2 := joinOne(t, s, e, "two")

	s.SendText(id1, []byte(`{"t":"bye","reason":"closed"}`))
	if err := s.Close(id1); err != nil {
		t.Fatal(err)
	}
	if err := s.Close(id1); !errors.Is(err, server.ErrNoSession) {
		t.Fatalf("second close: %v", err)
	}
	if err := s.SendText(id1, []byte("x")); !errors.Is(err, server.ErrNoSession) {
		t.Fatalf("send to closing: %v", err)
	}
	f, err := nextFrame(t, c1, 5*time.Second)
	if err != nil || f.T != "bye" {
		t.Fatalf("bye before close: %+v %v", f, err)
	}
	waitClosed(t, c1, 5*time.Second)
	if _, err := nextFrame(t, c1, time.Second); !errors.Is(err, io.EOF) {
		t.Fatalf("after close: %v", err)
	}
	e.waitFor(t, 5*time.Second, "host close", func(evs []server.Event) bool { return hasClosed(evs, id1) })

	c2.Close()
	evs := e.waitFor(t, 10*time.Second, "remote close", func(evs []server.Event) bool { return hasClosed(evs, id2) })
	time.Sleep(300 * time.Millisecond)
	evs = e.snapshot()
	for _, sid := range []int32{id1, id2} {
		n := 0
		var code int32
		se := ofSession(evs, sid)
		for _, ev := range se {
			if ev.Kind == server.EventClosed {
				n++
				code = ev.Code
			}
		}
		want := int32(server.CloseHost)
		if sid == id2 {
			want = server.CloseRemote
		}
		if n != 1 || code != want || se[len(se)-1].Kind != server.EventClosed {
			t.Fatalf("session %d: %s", sid, describe(se))
		}
	}
	for _, sid := range []int32{id1, id2} {
		if err := s.SendText(sid, []byte("x")); !errors.Is(err, server.ErrNoSession) {
			t.Fatalf("send after close: %v", err)
		}
		if _, err := s.Buffered(sid); !errors.Is(err, server.ErrNoSession) {
			t.Fatalf("buffered after close: %v", err)
		}
		if err := s.Close(sid); !errors.Is(err, server.ErrNoSession) {
			t.Fatalf("close after close: %v", err)
		}
	}
}

func TestStopSendsByeAndIdsKeepGrowing(t *testing.T) {
	s := startServer(t, nil)
	e := collect(t, s)
	c, id := joinOne(t, s, e, "stopper")
	start := time.Now()
	s.Stop()
	if d := time.Since(start); d > 1500*time.Millisecond {
		t.Fatalf("stop took %v", d)
	}
	var got []string
	for {
		f, err := nextFrame(t, c, 5*time.Second)
		if err != nil {
			break
		}
		got = append(got, string(f.Text))
	}
	if len(got) == 0 || got[len(got)-1] != `{"t":"bye","reason":"shutdown"}` {
		t.Fatalf("frames %q", got)
	}
	if _, ok := s.Poll(); ok {
		t.Fatal("event after stop")
	}
	if err := s.SendText(id, []byte("x")); err == nil {
		t.Fatal("send after stop")
	}
	s.Stop() // idempotent

	s2 := startServer(t, nil)
	e2 := collect(t, s2)
	_, id2 := joinOne(t, s2, e2, "again")
	if id2 <= id {
		t.Fatalf("session id %d not above %d", id2, id)
	}
}

func TestPingsArrive(t *testing.T) {
	s := startServer(t, func(c *server.Config) { c.PingInterval = 100 * time.Millisecond })
	e := collect(t, s)
	c := dial(t, s, client.Options{PingInterval: 50 * time.Millisecond})
	c.Hello("pinger")
	evs := e.waitFor(t, 5*time.Second, "pings from client", func(evs []server.Event) bool {
		n := 0
		for _, ev := range evs {
			if ev.Kind == server.EventText && string(ev.Data) == `{"t":"ping"}` {
				n++
			}
		}
		return n >= 5
	})
	before := c.PingsReceived()
	time.Sleep(1050 * time.Millisecond)
	if n := c.PingsReceived() - before; n < 7 || n > 12 {
		t.Fatalf("%d server pings in 1s at 100ms", n)
	}
	if hasClosed(e.snapshot(), evs[0].Session) {
		t.Fatal("session closed")
	}
}

func TestSessionLimit(t *testing.T) {
	s := startServer(t, nil)
	e := collect(t, s)
	var ids []int32
	for i := range server.MaxSessions {
		_, id := joinOne(t, s, e, fmt.Sprintf("u%d", i))
		ids = append(ids, id)
	}
	c := dial(t, s, client.Options{PingInterval: -1})
	c.Hello("seventeen")
	waitClosed(t, c, 5*time.Second)
	for _, ev := range e.snapshot() {
		if bytes.Contains(ev.Data, []byte("seventeen")) {
			t.Fatal("17th session got events")
		}
	}
	// Closing one frees a slot.
	s.Close(ids[0])
	e.waitFor(t, 5*time.Second, "close", func(evs []server.Event) bool { return hasClosed(evs, ids[0]) })
	joinOne(t, s, e, "late-comer")
}

func TestBufferedAndFull(t *testing.T) {
	s := startServer(t, nil)
	e := collect(t, s)
	_, id := joinOne(t, s, e, "full")
	if n, err := s.Buffered(id); err != nil || n < 0 {
		t.Fatalf("buffered %d %v", n, err)
	}
	data := make([]byte, server.MaxBinary)
	var full error
	for i := 0; i < 200 && full == nil; i++ {
		full = s.SendBinary(id, int32(i+1), data)
	}
	if !errors.Is(full, server.ErrFull) {
		t.Skipf("send queue drained faster than it filled: %v", full)
	}
	if n, _ := s.Buffered(id); n <= 0 {
		t.Fatalf("buffered %d after E_FULL", n)
	}
}

func TestPollLimitKeepsEvent(t *testing.T) {
	s := startServer(t, nil)
	c := dial(t, s, client.Options{PingInterval: -1})
	c.Hello("poller")
	deadline := time.Now().Add(10 * time.Second)
	for time.Now().Before(deadline) {
		ev, ok, err := s.PollLimit(4)
		if errors.Is(err, server.ErrSmallBuf) {
			if ev.Kind != server.EventOpened {
				t.Fatalf("kind %d", ev.Kind)
			}
			ev2, ok, err := s.PollLimit(1000)
			if !ok || err != nil || ev2.Kind != server.EventOpened || ev2.Session != ev.Session {
				t.Fatal("event was consumed by the failed poll")
			}
			return
		}
		if ok && ev.Kind != server.EventLog {
			t.Fatalf("unexpected %d", ev.Kind)
		}
		time.Sleep(10 * time.Millisecond)
	}
	t.Fatal("no OPENED")
}

func TestStartErrors(t *testing.T) {
	dir := t.TempDir()
	cert := filepath.Join(dir, "c.pem")
	cases := []server.Config{
		{UDPPort: -1, LANIP: "127.0.0.1", CertPath: cert},
		{UDPPort: 70000, LANIP: "127.0.0.1", CertPath: cert},
		{LANIP: "127.0.0.1"},
		{LANIP: "::1", CertPath: cert},
		{LANIP: "nope", CertPath: cert},
		{LANIP: "127.0.0.1", CertPath: cert, LogLevel: 9},
	}
	for i, c := range cases {
		if _, err := server.Start(c); !errors.Is(err, server.ErrArg) {
			t.Errorf("case %d: %v", i, err)
		}
	}
	s := startServer(t, nil)
	_, port := s.Endpoint()
	if _, err := server.Start(server.Config{UDPPort: port, LANIP: "127.0.0.1", CertPath: cert}); !errors.Is(err, server.ErrBind) {
		t.Errorf("bind: %v", err)
	}
	if _, err := server.Start(server.Config{LANIP: "127.0.0.1", CertPath: dir}); !errors.Is(err, server.ErrCert) {
		t.Errorf("cert: %v", err)
	}
}

func TestEndpointJoinURLAndFingerprint(t *testing.T) {
	path := filepath.Join(t.TempDir(), "keep.pem")
	s1 := startServer(t, func(c *server.Config) { c.CertPath = path })
	u1, _ := s1.JoinURL("https://example.invalid/iwyk/#x")
	ip, port := s1.Endpoint()
	if ip != "127.0.0.1" || port <= 0 {
		t.Fatalf("endpoint %s %d", ip, port)
	}
	fp1 := s1.Fingerprint()
	s1.Stop()
	s2 := startServer(t, func(c *server.Config) { c.CertPath = path })
	u2, _ := s2.JoinURL("https://example.invalid/iwyk/")
	if !bytes.Equal(fp1, s2.Fingerprint()) || len(fp1) != 32 {
		t.Fatal("fingerprint changed across restarts")
	}
	_, p1, _ := client.ParseJoinURL(u1)
	_, p2, _ := client.ParseJoinURL(u2)
	if p1.Ufrag == p2.Ufrag || p1.Pwd == p2.Pwd || p1.Token == p2.Token {
		t.Fatal("credentials reused across starts")
	}
	if !strings.HasPrefix(u1, "https://example.invalid/iwyk/#h=127.0.0.1&p=") {
		t.Fatalf("url %s", u1)
	}
	if _, err := s2.JoinURL("ftp://x/"); !errors.Is(err, server.ErrArg) {
		t.Fatalf("ftp: %v", err)
	}
}

func TestQueueOverflowClosesSession(t *testing.T) {
	s := startServer(t, func(c *server.Config) { c.QueueMaxEvents = 4 })
	c := dial(t, s, client.Options{PingInterval: -1})
	c.Hello("flood")
	for i := range 5 {
		c.SendText([]byte(fmt.Sprintf(`{"t":"n","i":%d}`, i)))
	}
	waitClosed(t, c, 10*time.Second)
	var kinds []server.EventKind
	var last server.Event
	for {
		ev, ok := s.Poll()
		if !ok {
			break
		}
		kinds = append(kinds, ev.Kind)
		last = ev
	}
	if len(kinds) != 5 || kinds[0] != server.EventOpened || last.Kind != server.EventClosed || last.Code != server.CloseProtocol {
		t.Fatalf("events %v last %+v", kinds, last)
	}
}
