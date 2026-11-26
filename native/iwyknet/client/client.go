// Package client is a Go implementation of the phone page side of the
// WebRTC Direct transport (contract 2.5). It is used by tests, the mock
// console bot and iwykbot.
package client

import (
	"bytes"
	"context"
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"net"
	"strconv"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"github.com/pion/ice/v4"
	"github.com/pion/logging"
	"github.com/pion/webrtc/v4"

	"iwyknet/server"
)

// Params are the values of a join URL fragment.
type Params struct {
	Host        string
	Port        int
	Ufrag, Pwd  string
	Token       string
	Fingerprint []byte
}

// ParseJoinURL splits a join URL (contract 2.4) into its base and params.
func ParseJoinURL(u string) (base string, p Params, err error) {
	base, frag, ok := strings.Cut(u, "#")
	if !ok {
		return "", p, errors.New("join URL has no fragment")
	}
	vals := map[string]string{}
	for _, kv := range strings.Split(frag, "&") {
		k, v, _ := strings.Cut(kv, "=")
		vals[k] = v
	}
	for _, k := range []string{"h", "p", "u", "w", "f", "t"} {
		if vals[k] == "" {
			return "", p, fmt.Errorf("join URL lacks %q", k)
		}
	}
	ip := net.ParseIP(vals["h"])
	if ip == nil || ip.To4() == nil {
		return "", p, fmt.Errorf("bad host %q", vals["h"])
	}
	port, err := strconv.Atoi(vals["p"])
	if err != nil || port < 1 || port > 65535 {
		return "", p, fmt.Errorf("bad port %q", vals["p"])
	}
	fp, err := base64.RawURLEncoding.DecodeString(vals["f"])
	if err != nil || len(fp) != 32 {
		return "", p, fmt.Errorf("bad fingerprint %q", vals["f"])
	}
	return base, Params{Host: ip.To4().String(), Port: port, Ufrag: vals["u"], Pwd: vals["w"], Token: vals["t"], Fingerprint: fp}, nil
}

// AnswerSDP synthesizes the remote answer exactly as contract 2.5.
func AnswerSDP(p Params, mid string) string {
	hexes := make([]string, len(p.Fingerprint))
	for i, b := range p.Fingerprint {
		hexes[i] = fmt.Sprintf("%02X", b)
	}
	port := strconv.Itoa(p.Port)
	lines := []string{
		"v=0",
		"o=- 0 0 IN IP4 " + p.Host,
		"s=-",
		"t=0 0",
		"a=ice-lite",
		"a=group:BUNDLE " + mid,
		"m=application " + port + " UDP/DTLS/SCTP webrtc-datachannel",
		"c=IN IP4 " + p.Host,
		"a=mid:" + mid,
		"a=ice-ufrag:" + p.Ufrag,
		"a=ice-pwd:" + p.Pwd,
		"a=fingerprint:sha-256 " + strings.Join(hexes, ":"),
		"a=setup:passive",
		"a=sctp-port:5000",
		"a=max-message-size:262144",
		"a=candidate:1 1 udp 2130706431 " + p.Host + " " + port + " typ host",
		"a=end-of-candidates",
	}
	return strings.Join(lines, "\r\n") + "\r\n"
}

// OfferMid returns the a=mid value of the application m-line of an SDP.
func OfferMid(sdp string) (string, error) {
	inApp := false
	for _, l := range strings.Split(strings.ReplaceAll(sdp, "\r\n", "\n"), "\n") {
		if strings.HasPrefix(l, "m=") {
			inApp = strings.HasPrefix(l, "m=application ")
			continue
		}
		if inApp && strings.HasPrefix(l, "a=mid:") {
			return strings.TrimPrefix(l, "a=mid:"), nil
		}
	}
	return "", errors.New("offer has no application m-line with a mid")
}

// Options tune a client.
type Options struct {
	PingInterval time.Duration // 0 = 2s, < 0 = no pings
	Timeout      time.Duration // Dial timeout when ctx has none; 0 = 17s
}

// Frame is one received frame. Text frames set Text and T (and JSON for
// msg frames); binary content sets TransferID and Data.
type Frame struct {
	Text       []byte
	T          string
	JSON       []byte // raw "json" value of msg frames
	TransferID int32
	Data       []byte // binary
}

// Client is one connected page.
type Client struct {
	pc     *webrtc.PeerConnection
	dc     *webrtc.DataChannel
	token  string
	pings  atomic.Int64
	sendMu sync.Mutex
	reasm  server.Reassembler

	mu      sync.Mutex
	frames  []Frame
	notify  chan struct{}
	closed  bool
	errText string
	done    chan struct{}
	once    sync.Once
}

type quietLogs struct{}

func (quietLogs) NewLogger(string) logging.LeveledLogger { return quietLogger{} }

type quietLogger struct{}

func (quietLogger) Trace(string)          {}
func (quietLogger) Tracef(string, ...any) {}
func (quietLogger) Debug(string)          {}
func (quietLogger) Debugf(string, ...any) {}
func (quietLogger) Info(string)           {}
func (quietLogger) Infof(string, ...any)  {}
func (quietLogger) Warn(string)           {}
func (quietLogger) Warnf(string, ...any)  {}
func (quietLogger) Error(string)          {}
func (quietLogger) Errorf(string, ...any) {}

// Dial connects to the game and returns when the data channel is open.
func Dial(ctx context.Context, p Params, o Options) (*Client, error) {
	if o.Timeout <= 0 {
		o.Timeout = 17 * time.Second
	}
	if _, ok := ctx.Deadline(); !ok {
		var cancel context.CancelFunc
		ctx, cancel = context.WithTimeout(ctx, o.Timeout)
		defer cancel()
	}
	se := webrtc.SettingEngine{}
	se.SetNetworkTypes([]webrtc.NetworkType{webrtc.NetworkTypeUDP4})
	se.SetICEMulticastDNSMode(ice.MulticastDNSModeDisabled)
	se.SetIncludeLoopbackCandidate(true)
	se.SetSCTPMaxMessageSize(262144)
	se.LoggerFactory = quietLogs{}
	api := webrtc.NewAPI(webrtc.WithSettingEngine(se))
	pc, err := api.NewPeerConnection(webrtc.Configuration{})
	if err != nil {
		return nil, err
	}
	c := &Client{pc: pc, token: p.Token, notify: make(chan struct{}), done: make(chan struct{})}
	negotiated, id := true, uint16(0)
	dc, err := pc.CreateDataChannel("game", &webrtc.DataChannelInit{Negotiated: &negotiated, ID: &id})
	if err != nil {
		pc.Close()
		return nil, err
	}
	c.dc = dc
	opened := make(chan struct{})
	var openOnce sync.Once
	dc.OnOpen(func() { openOnce.Do(func() { close(opened) }) })
	dc.OnMessage(c.onMessage)
	dc.OnClose(func() { c.shutdown("channel closed") })
	pc.OnConnectionStateChange(func(st webrtc.PeerConnectionState) {
		if st == webrtc.PeerConnectionStateFailed || st == webrtc.PeerConnectionStateClosed {
			c.shutdown("connection " + st.String())
		}
	})

	offer, err := pc.CreateOffer(nil)
	if err == nil {
		err = pc.SetLocalDescription(offer)
	}
	var mid string
	if err == nil {
		mid, err = OfferMid(offer.SDP)
	}
	if err == nil {
		err = pc.SetRemoteDescription(webrtc.SessionDescription{Type: webrtc.SDPTypeAnswer, SDP: AnswerSDP(p, mid)})
	}
	if err != nil {
		pc.Close()
		return nil, err
	}
	select {
	case <-opened:
	case <-c.done:
		pc.Close()
		return nil, fmt.Errorf("connection closed before the channel opened: %s", c.errText)
	case <-ctx.Done():
		pc.Close()
		return nil, fmt.Errorf("data channel did not open: %w", ctx.Err())
	}
	interval := o.PingInterval
	if interval == 0 {
		interval = 2 * time.Second
	}
	if interval > 0 {
		go c.pinger(interval)
	}
	return c, nil
}

func (c *Client) pinger(d time.Duration) {
	t := time.NewTicker(d)
	defer t.Stop()
	for {
		select {
		case <-c.done:
			return
		case <-t.C:
			_ = c.SendText([]byte(`{"t":"ping"}`))
		}
	}
}

func (c *Client) push(f Frame) {
	c.mu.Lock()
	defer c.mu.Unlock()
	if c.closed {
		return
	}
	c.frames = append(c.frames, f)
	close(c.notify)
	c.notify = make(chan struct{})
}

func (c *Client) onMessage(m webrtc.DataChannelMessage) {
	if m.IsString {
		var env struct {
			T    string          `json:"t"`
			JSON json.RawMessage `json:"json"`
		}
		_ = json.Unmarshal(m.Data, &env)
		if env.T == "ping" {
			c.pings.Add(1)
			return
		}
		f := Frame{Text: m.Data, T: env.T}
		if env.T == "msg" && len(env.JSON) > 0 {
			f.JSON = []byte(env.JSON)
		}
		c.push(f)
		return
	}
	id, data, done, err := c.reasm.Add(m.Data)
	if err != nil {
		return // the page drops a broken transfer
	}
	if done {
		c.push(Frame{TransferID: id, Data: data})
	}
}

func (c *Client) shutdown(why string) {
	c.once.Do(func() {
		c.mu.Lock()
		c.closed = true
		c.errText = why
		close(c.notify)
		c.notify = make(chan struct{})
		c.mu.Unlock()
		close(c.done)
		go c.pc.Close()
	})
}

// PingsReceived counts the ping frames filtered out by Next.
func (c *Client) PingsReceived() int64 { return c.pings.Load() }

// Next returns the next non-ping frame; io.EOF once the connection is
// closed and every frame received before has been returned.
func (c *Client) Next(ctx context.Context) (Frame, error) {
	for {
		c.mu.Lock()
		if len(c.frames) > 0 {
			f := c.frames[0]
			c.frames = c.frames[1:]
			c.mu.Unlock()
			return f, nil
		}
		if c.closed {
			c.mu.Unlock()
			return Frame{}, io.EOF
		}
		ch := c.notify
		c.mu.Unlock()
		select {
		case <-ch:
		case <-ctx.Done():
			return Frame{}, ctx.Err()
		}
	}
}

// Done is closed when the connection has closed.
func (c *Client) Done() <-chan struct{} { return c.done }

// Hello sends {"t":"hello","token":<token of the join URL>,"uid":uid}.
func (c *Client) Hello(uid string) error { return c.HelloToken(c.token, uid) }

// HelloToken sends a hello frame with an explicit token (tests).
func (c *Client) HelloToken(token, uid string) error {
	b := new(bytes.Buffer)
	enc := json.NewEncoder(b)
	enc.SetEscapeHTML(false)
	if err := enc.Encode(struct {
		T     string `json:"t"`
		Token string `json:"token"`
		UID   string `json:"uid"`
	}{"hello", token, uid}); err != nil {
		return err
	}
	return c.SendText(bytes.TrimRight(b.Bytes(), "\n"))
}

// SendText sends one raw text frame.
func (c *Client) SendText(text []byte) error {
	c.sendMu.Lock()
	defer c.sendMu.Unlock()
	return c.dc.SendText(string(text))
}

// SendBinary sends one raw binary frame (tests use it for broken chunks).
func (c *Client) SendBinary(frame []byte) error {
	c.sendMu.Lock()
	defer c.sendMu.Unlock()
	return c.dc.Send(frame)
}

// SendMsg wraps a game JSON object as {"t":"msg","id":-7503,"json":...}.
func (c *Client) SendMsg(gameJSON []byte) error {
	var buf bytes.Buffer
	if err := json.Compact(&buf, gameJSON); err != nil {
		return fmt.Errorf("game JSON: %w", err)
	}
	return c.SendText([]byte(`{"t":"msg","id":-7503,"json":` + buf.String() + `}`))
}

// SendContent sends one content transfer, chunked per 2.2.
func (c *Client) SendContent(transferID int32, data []byte) error {
	frames, err := server.EncodeChunks(transferID, data)
	if err != nil {
		return err
	}
	c.sendMu.Lock()
	defer c.sendMu.Unlock()
	for _, f := range frames {
		if err := c.dc.Send(f); err != nil {
			return err
		}
	}
	return nil
}

// Buffered returns the data channel's buffered amount.
func (c *Client) Buffered() int { return int(c.dc.BufferedAmount()) }

// Close closes the connection.
func (c *Client) Close() error {
	c.shutdown("closed by client")
	return c.pc.Close()
}
