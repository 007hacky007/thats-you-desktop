package server

import (
	"context"
	"errors"
	"fmt"
	"net"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"github.com/pion/stun/v3"
	"github.com/pion/webrtc/v4"
)

// Config configures Start.
type Config struct {
	UDPPort      int           // 0 = any free port
	LANIP        string        // "" = auto (3.4)
	CertPath     string        // required
	PingInterval time.Duration // 0 = 2s (tests may shorten)
	HelloTimeout time.Duration // 0 = 10s
	LogLevel     int           // IWYK_LOG_* values

	// Not part of the C ABI; zero values give the contract defaults.
	StunTimeout     time.Duration // pending peer without a hello; 0 = 15s
	CloseFlush      time.Duration // graceful Close flush; 0 = 2s
	StopFlush       time.Duration // Stop flush; 0 = 500ms
	QueueMaxBytes   int           // 0 = 64 MiB
	QueueMaxEvents  int           // 0 = 100000
	MaxPendingPerIP int           // pending peers per source IP; 0 = 4
}

// sessionCounter makes session ids unique for the process lifetime.
var sessionCounter atomic.Int32

// Server is a running WebRTC Direct endpoint.
type Server struct {
	cfg      Config
	udp      *net.UDPConn
	ip       net.IP
	port     int
	ufrag    string
	pwd      string
	token    string
	cert     *Cert
	wcert    webrtc.Certificate
	queue    *eventQueue
	logLevel atomic.Int32
	done     chan struct{}

	mu       sync.Mutex
	stopped  bool
	byUfrag  map[string]*peer
	byAddr   map[string]*peer
	sessions map[int32]*peer
	pending  int
	pendIP   map[string]int // pending peers per source IP
}

// Start binds the UDP port and starts serving.
func Start(cfg Config) (*Server, error) {
	if cfg.UDPPort < 0 || cfg.UDPPort > 65535 {
		return nil, fmt.Errorf("%w: udp port %d", ErrArg, cfg.UDPPort)
	}
	if cfg.CertPath == "" {
		return nil, fmt.Errorf("%w: certificate path is required", ErrArg)
	}
	if cfg.LogLevel < LogDebug || cfg.LogLevel > LogOff {
		return nil, fmt.Errorf("%w: log level %d", ErrArg, cfg.LogLevel)
	}
	var fixedIP net.IP
	if cfg.LANIP != "" {
		fixedIP = net.ParseIP(cfg.LANIP)
		if fixedIP == nil || fixedIP.To4() == nil || strings.Contains(cfg.LANIP, ":") {
			return nil, fmt.Errorf("%w: lan_ip %q is not an IPv4 literal", ErrArg, cfg.LANIP)
		}
		fixedIP = fixedIP.To4()
	}
	def := func(d *time.Duration, v time.Duration) {
		if *d <= 0 {
			*d = v
		}
	}
	def(&cfg.PingInterval, 2*time.Second)
	def(&cfg.HelloTimeout, 10*time.Second)
	def(&cfg.StunTimeout, 15*time.Second)
	def(&cfg.CloseFlush, 2*time.Second)
	def(&cfg.StopFlush, 500*time.Millisecond)
	if cfg.QueueMaxBytes <= 0 {
		cfg.QueueMaxBytes = QueueMaxBytes
	}
	if cfg.MaxPendingPerIP <= 0 {
		cfg.MaxPendingPerIP = 4
	}
	if cfg.QueueMaxEvents <= 0 {
		cfg.QueueMaxEvents = QueueMaxEvents
	}

	s := &Server{
		cfg:      cfg,
		queue:    newEventQueue(cfg.QueueMaxBytes, cfg.QueueMaxEvents),
		done:     make(chan struct{}),
		byUfrag:  map[string]*peer{},
		byAddr:   map[string]*peer{},
		sessions: map[int32]*peer{},
		pendIP:   map[string]int{},
	}
	s.logLevel.Store(int32(cfg.LogLevel))

	cert, status, err := LoadOrCreateCert(cfg.CertPath, time.Now())
	if err != nil {
		return nil, err
	}
	s.cert = cert
	s.wcert = webrtc.CertificateFromX509(cert.Key, cert.X509)
	switch status {
	case CertRegenerated:
		s.logf(LogWarn, "DTLS certificate was unreadable or expiring and has been regenerated (phones need a new QR scan)")
	case CertCreated:
		s.logf(LogInfo, "created DTLS certificate")
	}

	ifaces := SystemInterfaces()
	if fixedIP != nil {
		s.ip = fixedIP
		if !IsLocalIP(fixedIP, ifaces) {
			s.logf(LogWarn, "lan_ip %s is not a local address", fixedIP)
		}
	} else {
		s.ip, err = SelectLANIP(RouteIP(), ifaces)
		if err != nil {
			return nil, err
		}
	}

	s.udp, err = net.ListenUDP("udp4", &net.UDPAddr{IP: net.IPv4zero, Port: cfg.UDPPort})
	if err != nil {
		return nil, fmt.Errorf("%w: udp4 port %d: %v", ErrBind, cfg.UDPPort, err)
	}
	s.port = s.udp.LocalAddr().(*net.UDPAddr).Port
	s.ufrag, s.pwd, s.token = RandomString(8), RandomString(24), RandomString(16)
	s.logf(LogInfo, "listening on udp %s:%d", s.ip, s.port)

	go s.readLoop()
	go s.keepalive()
	return s, nil
}

// SetLogLevel changes the level of LOG events (IWYK_LOG_*).
func (s *Server) SetLogLevel(level int) error {
	if level < LogDebug || level > LogOff {
		return fmt.Errorf("%w: log level %d", ErrArg, level)
	}
	s.logLevel.Store(int32(level))
	return nil
}

func (s *Server) logf(level int, format string, a ...any) {
	if level >= LogOff || int32(level) < s.logLevel.Load() {
		return
	}
	s.queue.push(Event{Kind: EventLog, Code: int32(level), Data: []byte(fmt.Sprintf(format, a...))}, false)
}

// Endpoint returns the advertised address and the bound UDP port.
func (s *Server) Endpoint() (ip string, port int) { return s.ip.String(), s.port }

// Fingerprint returns the SHA-256 fingerprint of the DTLS certificate.
func (s *Server) Fingerprint() []byte { return s.cert.Fingerprint() }

// JoinURL builds the QR payload (contract 2.4) for base.
func (s *Server) JoinURL(base string) (string, error) {
	return BuildJoinURL(base, s.ip.String(), s.port, s.ufrag, s.pwd, s.cert.Fingerprint(), s.token)
}

// Poll takes the next event without blocking.
func (s *Server) Poll() (Event, bool) { return s.queue.pop() }

// PollLimit takes the next event when its payload is at most max bytes.
// When it is larger, the event stays queued and err wraps ErrSmallBuf
// (ev then carries the kind and payload, for its length).
func (s *Server) PollLimit(max int) (ev Event, ok bool, err error) {
	ev, ok, big := s.queue.popIf(max)
	if big {
		return ev, false, fmt.Errorf("%w: next event needs %d bytes", ErrSmallBuf, len(ev.Data))
	}
	return ev, ok, nil
}

// Next blocks until an event is available, ctx ends or the server stops.
func (s *Server) Next(ctx context.Context) (Event, error) { return s.queue.wait(ctx) }

func (s *Server) openPeer(session int32) (*peer, error) {
	s.mu.Lock()
	p := s.sessions[session]
	stopped := s.stopped
	s.mu.Unlock()
	if stopped {
		return nil, ErrStopped
	}
	if p == nil {
		return nil, fmt.Errorf("%w: %d", ErrNoSession, session)
	}
	return p, nil
}

// SendText sends one text frame (1..65536 bytes).
func (s *Server) SendText(session int32, text []byte) error {
	if len(text) == 0 || len(text) > MaxText {
		return fmt.Errorf("%w: text frame of %d bytes", ErrTooBig, len(text))
	}
	p, err := s.openPeer(session)
	if err != nil {
		return err
	}
	return p.send([][]byte{text}, true)
}

// SendBinary sends one content transfer (1..131072 bytes), chunked per 2.2.
func (s *Server) SendBinary(session, transferID int32, data []byte) error {
	frames, err := EncodeChunks(transferID, data)
	if err != nil {
		return err
	}
	p, err := s.openPeer(session)
	if err != nil {
		return err
	}
	return p.send(frames, false)
}

// Close closes a session gracefully: queued frames are flushed (up to 2 s),
// then a CLOSED(HOST) event follows.
func (s *Server) Close(session int32) error {
	p, err := s.openPeer(session)
	if err != nil {
		return err
	}
	if !p.beginClose(CloseHost, "closed by host") {
		return fmt.Errorf("%w: %d is closing", ErrNoSession, session)
	}
	go func() {
		p.flush(s.cfg.CloseFlush)
		p.finish(CloseHost, "")
	}()
	return nil
}

// Buffered returns the bytes queued on the session's data channel.
func (s *Server) Buffered(session int32) (int, error) {
	p, err := s.openPeer(session)
	if err != nil {
		return 0, err
	}
	return p.buffered(), nil
}

// Stop sends bye(shutdown) to every session, flushes for up to 500 ms,
// closes everything and discards the event queue. Idempotent.
func (s *Server) Stop() {
	s.mu.Lock()
	if s.stopped {
		s.mu.Unlock()
		return
	}
	s.stopped = true
	var all []*peer
	seen := map[*peer]bool{}
	for _, p := range s.byUfrag {
		if !seen[p] {
			seen[p] = true
			all = append(all, p)
		}
	}
	s.mu.Unlock()
	close(s.done)

	var open []*peer
	for _, p := range all {
		if p.isOpen() {
			open = append(open, p)
			_ = p.sendRaw([][]byte{[]byte(`{"t":"bye","reason":"shutdown"}`)}, true)
		}
	}
	deadline := time.Now().Add(s.cfg.StopFlush)
	for _, p := range open {
		p.flush(time.Until(deadline))
	}
	s.queue.close()
	for _, p := range all {
		p.finish(CloseStopped, "")
	}
	limit := time.After(300 * time.Millisecond)
wait:
	for _, p := range all {
		select {
		case <-p.closedCh:
		case <-limit:
			break wait
		}
	}
	_ = s.udp.Close()
}

func (s *Server) keepalive() {
	t := time.NewTicker(s.cfg.PingInterval)
	defer t.Stop()
	ping := [][]byte{[]byte(`{"t":"ping"}`)}
	for {
		select {
		case <-s.done:
			return
		case <-t.C:
		}
		s.mu.Lock()
		list := make([]*peer, 0, len(s.sessions))
		for _, p := range s.sessions {
			list = append(list, p)
		}
		s.mu.Unlock()
		for _, p := range list {
			if p.isOpen() {
				_ = p.sendRaw(ping, true)
			}
		}
	}
}

func (s *Server) readLoop() {
	buf := make([]byte, 65536)
	for {
		n, addr, err := s.udp.ReadFrom(buf)
		if err != nil {
			if errors.Is(err, net.ErrClosed) {
				return
			}
			select {
			case <-s.done:
				return
			default:
			}
			// Windows reports ICMP port unreachable as a read error (WSAECONNRESET).
			continue
		}
		b := append([]byte(nil), buf[:n]...)
		key := addr.String()
		var target *peer
		if remote, ok := s.stunRemoteUfrag(b); ok {
			target = s.peerForUfrag(remote, addr)
		} else {
			s.mu.Lock()
			target = s.byAddr[key]
			s.mu.Unlock()
		}
		if target != nil {
			target.conn.deliver(b, addr)
		}
	}
}

// stunRemoteUfrag returns the remote ufrag of a STUN binding request whose
// USERNAME is "<our ufrag>:<remote ufrag>".
func (s *Server) stunRemoteUfrag(b []byte) (string, bool) {
	if !stun.IsMessage(b) {
		return "", false
	}
	m := &stun.Message{Raw: b}
	if m.Decode() != nil || m.Type != stun.BindingRequest {
		return "", false
	}
	var u stun.Username
	if u.GetFrom(m) != nil {
		return "", false
	}
	local, remote, ok := strings.Cut(u.String(), ":")
	if !ok || local != s.ufrag || remote == "" || len(remote) > 256 {
		return "", false
	}
	// Only a phone that knows the ICE password from the QR code may create a
	// peer or move an address (checked before any state changes).
	if stun.NewShortTermIntegrity(s.pwd).Check(m) != nil {
		return "", false
	}
	return remote, true
}

// peerForUfrag maps addr to the peer of remote ufrag, creating a pending
// peer when there is room.
func (s *Server) peerForUfrag(remote string, addr net.Addr) *peer {
	key := addr.String()
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.stopped {
		return nil
	}
	old := s.byAddr[key]
	if p := s.byUfrag[remote]; p != nil {
		if old == nil {
			s.byAddr[key] = p
			p.addrList = append(p.addrList, key)
		} else if old != p {
			return nil // never take an address away from another peer
		}
		return p
	}
	if old != nil {
		return nil
	}
	ip := hostOf(addr)
	if s.pending >= MaxPending || s.pendIP[ip] >= s.cfg.MaxPendingPerIP {
		return nil
	}
	p := newPeer(s, remote, addr)
	p.srcIP = ip
	s.pending++
	s.pendIP[ip]++
	s.byUfrag[remote] = p
	s.byAddr[key] = p
	p.addrList = append(p.addrList, key)
	go p.start()
	return p
}

// authenticate turns a pending peer into a session (caller holds p.mu).
// It returns 0 when the session limit is reached.
func (s *Server) authenticate(p *peer) int32 {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.stopped || s.byUfrag[p.remoteUfrag] != p || len(s.sessions) >= MaxSessions {
		return 0
	}
	id := sessionCounter.Add(1)
	s.sessions[id] = p
	s.unpend(p)
	return id
}

func (s *Server) removePeer(p *peer, session int32) {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.byUfrag[p.remoteUfrag] == p {
		delete(s.byUfrag, p.remoteUfrag)
		if session == 0 {
			s.unpend(p)
		}
	}
	for _, k := range p.addrList {
		if s.byAddr[k] == p {
			delete(s.byAddr, k)
		}
	}
	if session != 0 && s.sessions[session] == p {
		delete(s.sessions, session)
	}
}

// unpend drops p from the pending counters (caller holds s.mu).
func (s *Server) unpend(p *peer) {
	s.pending--
	if s.pendIP[p.srcIP]--; s.pendIP[p.srcIP] <= 0 {
		delete(s.pendIP, p.srcIP)
	}
}

func hostOf(a net.Addr) string {
	if u, ok := a.(*net.UDPAddr); ok {
		return u.IP.String()
	}
	h, _, _ := net.SplitHostPort(a.String())
	return h
}
