package server

import (
	"errors"
	"fmt"
	"io"
	"net"
	"strings"
	"sync"
	"time"

	"github.com/pion/ice/v4"
	"github.com/pion/logging"
	"github.com/pion/webrtc/v4"
)

const (
	stPending = iota // no valid hello yet
	stOpen           // authenticated session
	stClosing        // host Close in progress
	stClosed
)

type peer struct {
	s           *Server
	remoteUfrag string
	firstAddr   string
	conn        *muxConn

	pc *webrtc.PeerConnection
	dc *webrtc.DataChannel

	mu          sync.Mutex
	state       int
	session     int32
	closeReason int32
	closeDetail string
	timers      []*time.Timer
	srcIP       string
	closedCh    chan struct{} // closed when the connection is torn down

	addrList []string // guarded by s.mu

	sendMu sync.Mutex // one ordered writer per session
	reasm  Reassembler
}

func newPeer(s *Server, remote string, addr net.Addr) *peer {
	return &peer{s: s, remoteUfrag: remote, firstAddr: addr.String(), conn: newMuxConn(s.udp), closedCh: make(chan struct{})}
}

func (p *peer) isOpen() bool {
	p.mu.Lock()
	defer p.mu.Unlock()
	return p.state == stOpen
}

func (p *peer) label() string {
	p.mu.Lock()
	defer p.mu.Unlock()
	if p.session != 0 {
		return fmt.Sprintf("session %d (%s)", p.session, p.firstAddr)
	}
	return "peer " + p.firstAddr
}

func (p *peer) addTimer(d time.Duration, f func()) {
	t := time.AfterFunc(d, f)
	p.mu.Lock()
	if p.state == stClosed {
		t.Stop()
	} else {
		p.timers = append(p.timers, t)
	}
	p.mu.Unlock()
}

func (p *peer) start() {
	s := p.s
	se := webrtc.SettingEngine{}
	se.SetLite(true)
	se.SetICECredentials(s.ufrag, s.pwd)
	se.SetICEUDPMux(&peerMux{conn: p.conn, listen: &net.UDPAddr{IP: s.ip, Port: s.port}})
	se.SetNetworkTypes([]webrtc.NetworkType{webrtc.NetworkTypeUDP4})
	se.SetICEMulticastDNSMode(ice.MulticastDNSModeDisabled)
	se.SetIncludeLoopbackCandidate(true)
	se.DisableCertificateFingerprintVerification(true)
	se.SetICETimeouts(5*time.Second, 25*time.Second, 2*time.Second)
	se.SetSCTPMaxMessageSize(maxIncomingSCTP)
	se.LoggerFactory = pionLogs{s: s}
	if err := se.SetAnsweringDTLSRole(webrtc.DTLSRoleServer); err != nil {
		p.fail("dtls role", err)
		return
	}
	api := webrtc.NewAPI(webrtc.WithSettingEngine(se))
	pc, err := api.NewPeerConnection(webrtc.Configuration{Certificates: []webrtc.Certificate{s.wcert}})
	if err != nil {
		p.fail("peer connection", err)
		return
	}
	p.mu.Lock()
	p.pc = pc
	closed := p.state == stClosed
	p.mu.Unlock()
	if closed {
		_ = pc.Close()
		return
	}
	s.logf(LogDebug, "new %s", p.label())
	p.addTimer(s.cfg.StunTimeout, func() {
		if !p.authenticated() {
			s.logf(LogInfo, "%s sent no hello in time", p.label())
			p.finish(CloseTransport, "")
		}
	})

	pc.OnConnectionStateChange(func(st webrtc.PeerConnectionState) {
		s.logf(LogDebug, "%s: connection %s", p.label(), st)
		switch st {
		case webrtc.PeerConnectionStateFailed:
			p.finish(CloseTransport, "connection failed")
		case webrtc.PeerConnectionStateClosed:
			p.finish(CloseRemote, "connection closed")
		}
	})

	negotiated, id := true, uint16(0)
	dc, err := pc.CreateDataChannel("game", &webrtc.DataChannelInit{Negotiated: &negotiated, ID: &id})
	if err != nil {
		p.fail("data channel", err)
		return
	}
	p.mu.Lock()
	p.dc = dc
	p.mu.Unlock()
	dc.OnOpen(func() {
		p.addTimer(s.cfg.HelloTimeout, func() {
			if !p.authenticated() {
				s.logf(LogInfo, "%s: no hello within %v", p.label(), s.cfg.HelloTimeout)
				p.finish(CloseTransport, "")
			}
		})
	})
	dc.OnMessage(p.onMessage)
	dc.OnError(func(err error) {
		if errors.Is(err, io.ErrShortBuffer) {
			p.beginClose(CloseProtocol, fmt.Sprintf("frame over %d bytes", maxIncomingSCTP))
		}
	})
	dc.OnClose(func() { p.finish(CloseRemote, "channel closed") })

	// Synthesize the phone's offer from its ufrag. An ICE-lite answerer never
	// sends checks, so the remote password is unused; the client certificate
	// is not verified (the QR token authenticates the phone).
	dummyFP := strings.TrimSuffix(strings.Repeat("00:", 32), ":")
	offer := strings.Join([]string{
		"v=0", "o=- 0 0 IN IP4 0.0.0.0", "s=-", "t=0 0", "a=group:BUNDLE 0",
		"m=application 9 UDP/DTLS/SCTP webrtc-datachannel", "c=IN IP4 0.0.0.0",
		"a=mid:0", "a=ice-ufrag:" + p.remoteUfrag, "a=ice-pwd:unusedunusedunusedunused",
		"a=fingerprint:sha-256 " + dummyFP, "a=setup:actpass",
		"a=sctp-port:5000", "a=max-message-size:262144", "",
	}, "\r\n")
	if err := pc.SetRemoteDescription(webrtc.SessionDescription{Type: webrtc.SDPTypeOffer, SDP: offer}); err != nil {
		p.fail("remote description", err)
		return
	}
	ans, err := pc.CreateAnswer(nil)
	if err != nil {
		p.fail("answer", err)
		return
	}
	if err := pc.SetLocalDescription(ans); err != nil {
		p.fail("local description", err)
	}
}

func (p *peer) fail(what string, err error) {
	p.s.logf(LogError, "%s: %s: %v", p.label(), what, err)
	p.finish(CloseTransport, what)
}

func (p *peer) authenticated() bool {
	p.mu.Lock()
	defer p.mu.Unlock()
	return p.session != 0
}

func (p *peer) onMessage(m webrtc.DataChannelMessage) {
	s := p.s
	p.mu.Lock()
	state := p.state
	p.mu.Unlock()
	switch state {
	case stClosed:
		return
	case stPending:
		p.onHello(m)
		return
	}
	if m.IsString {
		if len(m.Data) > MaxText {
			p.protocolClose(fmt.Sprintf("text frame of %d bytes", len(m.Data)))
			return
		}
		p.emit(Event{Kind: EventText, Data: m.Data})
		return
	}
	id, data, done, err := p.reasm.Add(m.Data)
	if err != nil {
		s.logf(LogWarn, "%s: %v", p.label(), err)
		p.protocolClose(err.Error())
		return
	}
	if done {
		p.emit(Event{Kind: EventBinary, TransferID: id, Data: data})
	}
}

func (p *peer) onHello(m webrtc.DataChannelMessage) {
	s := p.s
	if !m.IsString {
		s.logf(LogInfo, "%s: binary frame before hello", p.label())
		p.finish(CloseProtocol, "")
		return
	}
	if _, err := CheckHello(m.Data, s.token); err != nil {
		s.logf(LogInfo, "%s: rejected: %v", p.label(), err)
		p.finish(CloseProtocol, "")
		return
	}
	p.mu.Lock()
	if p.state != stPending {
		p.mu.Unlock()
		return
	}
	id := s.authenticate(p)
	if id == 0 {
		p.mu.Unlock()
		s.logf(LogWarn, "peer %s refused, %d sessions open", p.firstAddr, MaxSessions)
		p.finish(CloseProtocol, "")
		return
	}
	p.state = stOpen
	p.session = id
	s.queue.push(Event{Kind: EventOpened, Session: id, Data: []byte(p.firstAddr)}, true)
	ok := s.queue.push(Event{Kind: EventText, Session: id, Data: m.Data}, false)
	p.mu.Unlock()
	s.logf(LogInfo, "session %d opened from %s", id, p.firstAddr)
	if !ok {
		p.protocolClose("event queue full")
	}
}

// emit queues a session event unless the session is already closed.
func (p *peer) emit(ev Event) {
	p.mu.Lock()
	if p.state == stClosed || p.session == 0 {
		p.mu.Unlock()
		return
	}
	ev.Session = p.session
	ok := p.s.queue.push(ev, false)
	p.mu.Unlock()
	if !ok {
		p.protocolClose("event queue full")
	}
}

func (p *peer) protocolClose(detail string) {
	if p.beginClose(CloseProtocol, detail) {
		p.finish(CloseProtocol, detail)
	}
}

// beginClose records the close reason; false when already closing/closed.
func (p *peer) beginClose(reason int32, detail string) bool {
	p.mu.Lock()
	defer p.mu.Unlock()
	if p.state == stClosing || p.state == stClosed {
		return false
	}
	if p.closeReason == 0 {
		p.closeReason, p.closeDetail = reason, detail
	}
	if p.state == stOpen {
		p.state = stClosing
	}
	return true
}

// finish closes the peer once and queues CLOSED for an authenticated session.
func (p *peer) finish(reason int32, detail string) {
	p.mu.Lock()
	if p.state == stClosed {
		p.mu.Unlock()
		return
	}
	if p.closeReason != 0 {
		reason, detail = p.closeReason, p.closeDetail
	}
	p.state = stClosed
	session := p.session
	timers := p.timers
	p.timers = nil
	pc := p.pc
	if session != 0 {
		p.s.queue.push(Event{Kind: EventClosed, Session: session, Code: reason, Data: []byte(detail)}, true)
	}
	p.mu.Unlock()
	for _, t := range timers {
		t.Stop()
	}
	p.s.removePeer(p, session)
	if session != 0 {
		p.s.logf(LogInfo, "session %d closed (reason %d)", session, reason)
	}
	// pion may call finish from its own callbacks: tear down asynchronously.
	go func() {
		if pc != nil {
			_ = pc.Close()
		}
		_ = p.conn.Close()
		close(p.closedCh)
	}()
}

func (p *peer) channel() *webrtc.DataChannel {
	p.mu.Lock()
	defer p.mu.Unlock()
	return p.dc
}

func (p *peer) buffered() int {
	if dc := p.channel(); dc != nil {
		return int(dc.BufferedAmount())
	}
	return 0
}

// send queues frames for an open session, all or nothing, contiguously.
func (p *peer) send(frames [][]byte, text bool) error {
	total := 0
	for _, f := range frames {
		total += len(f)
	}
	p.sendMu.Lock()
	defer p.sendMu.Unlock()
	p.mu.Lock()
	state, session := p.state, p.session
	p.mu.Unlock()
	if state != stOpen {
		return fmt.Errorf("%w: %d is closing or closed", ErrNoSession, session)
	}
	if p.buffered()+total > SendCap {
		return fmt.Errorf("%w: %d bytes queued", ErrFull, p.buffered())
	}
	if err := p.writeLocked(frames, text); err != nil {
		return fmt.Errorf("%w: %d: %v", ErrNoSession, session, err)
	}
	return nil
}

// sendRaw writes without state or cap checks (pings, bye).
func (p *peer) sendRaw(frames [][]byte, text bool) error {
	p.sendMu.Lock()
	defer p.sendMu.Unlock()
	return p.writeLocked(frames, text)
}

func (p *peer) writeLocked(frames [][]byte, text bool) error {
	dc := p.channel()
	if dc == nil {
		return errors.New("no data channel")
	}
	for _, f := range frames {
		var err error
		if text {
			err = dc.SendText(string(f))
		} else {
			err = dc.Send(f)
		}
		if err != nil {
			return err
		}
	}
	return nil
}

// flush waits until the data channel buffer is empty or d has passed.
func (p *peer) flush(d time.Duration) {
	deadline := time.Now().Add(d)
	for p.buffered() > 0 && time.Now().Before(deadline) {
		time.Sleep(10 * time.Millisecond)
	}
}

// pionLogs forwards pion warnings and errors as DEBUG log events.
type pionLogs struct{ s *Server }

func (f pionLogs) NewLogger(scope string) logging.LeveledLogger { return pionLogger{f.s, scope} }

type pionLogger struct {
	s     *Server
	scope string
}

func (l pionLogger) Trace(string)          {}
func (l pionLogger) Tracef(string, ...any) {}
func (l pionLogger) Debug(string)          {}
func (l pionLogger) Debugf(string, ...any) {}
func (l pionLogger) Info(string)           {}
func (l pionLogger) Infof(string, ...any)  {}
func (l pionLogger) Warn(m string)         { l.s.logf(LogDebug, "pion %s: %s", l.scope, m) }
func (l pionLogger) Warnf(f string, a ...any) {
	l.s.logf(LogDebug, "pion %s: %s", l.scope, fmt.Sprintf(f, a...))
}
func (l pionLogger) Error(m string) { l.s.logf(LogDebug, "pion %s: %s", l.scope, m) }
func (l pionLogger) Errorf(f string, a ...any) {
	l.s.logf(LogDebug, "pion %s: %s", l.scope, fmt.Sprintf(f, a...))
}
