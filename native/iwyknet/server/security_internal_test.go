package server

import (
	"net"
	"path/filepath"
	"testing"
	"time"

	"github.com/pion/stun/v3"
)

func startInternal(t *testing.T, mod func(*Config)) *Server {
	t.Helper()
	cfg := Config{LANIP: "127.0.0.1", CertPath: filepath.Join(t.TempDir(), "dtls.pem"), LogLevel: LogOff}
	if mod != nil {
		mod(&cfg)
	}
	s, err := Start(cfg)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(s.Stop)
	return s
}

func bindingRequest(t *testing.T, user, pwd string) []byte {
	t.Helper()
	setters := []stun.Setter{stun.TransactionID, stun.BindingRequest, stun.NewUsername(user)}
	if pwd != "" {
		setters = append(setters, stun.NewShortTermIntegrity(pwd))
	}
	setters = append(setters, stun.Fingerprint)
	m, err := stun.Build(setters...)
	if err != nil {
		t.Fatal(err)
	}
	return m.Raw
}

func udpTo(t *testing.T, s *Server) *net.UDPConn {
	t.Helper()
	c, err := net.DialUDP("udp4", nil, &net.UDPAddr{IP: net.IPv4(127, 0, 0, 1), Port: s.port})
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { c.Close() })
	return c
}

func send(t *testing.T, c *net.UDPConn, b []byte) {
	t.Helper()
	if _, err := c.Write(b); err != nil {
		t.Fatal(err)
	}
	time.Sleep(100 * time.Millisecond)
}

func (s *Server) snapshot() (ufrags int, addrs map[string]string) {
	s.mu.Lock()
	defer s.mu.Unlock()
	addrs = map[string]string{}
	for k, p := range s.byAddr {
		addrs[k] = p.remoteUfrag
	}
	return len(s.byUfrag), addrs
}

func TestBindingWithoutIntegrityCreatesNoPeer(t *testing.T) {
	s := startInternal(t, nil)
	c := udpTo(t, s)
	send(t, c, bindingRequest(t, s.ufrag+":evil1", ""))
	send(t, c, bindingRequest(t, s.ufrag+":evil2", "wrongpasswordwrongpasswd"))
	if n, a := s.snapshot(); n != 0 || len(a) != 0 {
		t.Fatalf("peers=%d addrs=%v, want none", n, a)
	}
}

func TestBindingWithIntegrityCreatesPeer(t *testing.T) {
	s := startInternal(t, nil)
	c := udpTo(t, s)
	send(t, c, bindingRequest(t, s.ufrag+":good", s.pwd))
	if n, a := s.snapshot(); n != 1 || a[c.LocalAddr().String()] != "good" {
		t.Fatalf("peers=%d addrs=%v", n, a)
	}
}

func TestAddressOwnedByAnotherPeerIsNotRemapped(t *testing.T) {
	s := startInternal(t, nil)
	a, b := udpTo(t, s), udpTo(t, s)
	send(t, a, bindingRequest(t, s.ufrag+":first", s.pwd))
	send(t, b, bindingRequest(t, s.ufrag+":second", s.pwd))
	// b now claims a's ufrag, and a claims b's: neither address may move.
	send(t, b, bindingRequest(t, s.ufrag+":first", s.pwd))
	send(t, a, bindingRequest(t, s.ufrag+":second", s.pwd))
	_, m := s.snapshot()
	if m[a.LocalAddr().String()] != "first" || m[b.LocalAddr().String()] != "second" {
		t.Fatalf("addresses remapped: %v", m)
	}
}

func TestPendingPeersLimitedPerSourceIP(t *testing.T) {
	s := startInternal(t, func(c *Config) { c.MaxPendingPerIP = 2 })
	for i, u := range []string{"p1", "p2", "p3"} {
		_ = i
		send(t, udpTo(t, s), bindingRequest(t, s.ufrag+":"+u, s.pwd))
	}
	if n, _ := s.snapshot(); n != 2 {
		t.Fatalf("pending peers from one IP = %d, want 2", n)
	}
}

func TestPeerWithoutHandshakeExpires(t *testing.T) {
	s := startInternal(t, func(c *Config) { c.StunTimeout = 300 * time.Millisecond })
	send(t, udpTo(t, s), bindingRequest(t, s.ufrag+":idle", s.pwd))
	if n, _ := s.snapshot(); n != 1 {
		t.Fatalf("peer not created")
	}
	time.Sleep(700 * time.Millisecond)
	if n, a := s.snapshot(); n != 0 || len(a) != 0 {
		t.Fatalf("idle peer kept: %d %v", n, a)
	}
}

func TestDefaultPreAuthTimeoutIsShort(t *testing.T) {
	s := startInternal(t, nil)
	if s.cfg.StunTimeout > 15*time.Second {
		t.Fatalf("pre-auth timeout %v, want <= 15s", s.cfg.StunTimeout)
	}
}
