// Throwaway spike: "WebRTC Direct" style server. A phone page hosted on HTTPS
// connects straight to this program over the LAN using only the data encoded
// in a QR code (ip, port, ICE ufrag/pwd, DTLS fingerprint, token). No
// signaling server. Received photos are saved to ./photos and shown on a
// local status page.
package main

import (
	"crypto/ecdsa"
	"crypto/elliptic"
	"crypto/rand"
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"flag"
	"fmt"
	"log"
	"net"
	"net/http"
	"net/url"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"sync"
	"time"

	"github.com/pion/ice/v4"
	"github.com/pion/stun/v3"
	"github.com/pion/webrtc/v4"
	qrcode "github.com/skip2/go-qrcode"
)

var (
	flagPage = flag.String("page", "http://localhost:8080/phone/", "URL of the hosted phone page")
	flagPort = flag.Int("port", 47000, "UDP port for WebRTC")
	flagHTTP = flag.String("http", "127.0.0.1:8080", "status page listen address")
	flagIP   = flag.String("ip", "", "LAN IP to advertise (auto if empty)")
)

// ---------- per-client UDP demux ----------

type muxConn struct {
	udp    *net.UDPConn
	ch     chan pkt
	closed chan struct{}
	once   sync.Once
	mu     sync.Mutex
	dl     time.Time
}

type pkt struct {
	b    []byte
	addr net.Addr
}

func newMuxConn(u *net.UDPConn) *muxConn {
	return &muxConn{udp: u, ch: make(chan pkt, 256), closed: make(chan struct{})}
}

func (c *muxConn) ReadFrom(p []byte) (int, net.Addr, error) {
	c.mu.Lock()
	dl := c.dl
	c.mu.Unlock()
	var timer <-chan time.Time
	if !dl.IsZero() {
		t := time.NewTimer(time.Until(dl))
		defer t.Stop()
		timer = t.C
	}
	select {
	case k := <-c.ch:
		return copy(p, k.b), k.addr, nil
	case <-c.closed:
		return 0, nil, net.ErrClosed
	case <-timer:
		return 0, nil, os.ErrDeadlineExceeded
	}
}
func (c *muxConn) WriteTo(p []byte, a net.Addr) (int, error) { return c.udp.WriteTo(p, a) }
func (c *muxConn) Close() error                             { c.once.Do(func() { close(c.closed) }); return nil }
func (c *muxConn) LocalAddr() net.Addr                      { return c.udp.LocalAddr() }
func (c *muxConn) SetDeadline(t time.Time) error            { return c.SetReadDeadline(t) }
func (c *muxConn) SetReadDeadline(t time.Time) error {
	c.mu.Lock()
	c.dl = t
	c.mu.Unlock()
	return nil
}
func (c *muxConn) SetWriteDeadline(time.Time) error { return nil }

// clientMux satisfies ice.UDPMux for exactly one PeerConnection.
type clientMux struct {
	conn   *muxConn
	listen net.Addr
}

func (m *clientMux) Close() error                                        { return m.conn.Close() }
func (m *clientMux) GetConn(string, net.Addr) (net.PacketConn, error)    { return m.conn, nil }
func (m *clientMux) RemoveConnByUfrag(string)                            {}
func (m *clientMux) GetListenAddresses() []net.Addr                      { return []net.Addr{m.listen} }

type server struct {
	udp       *net.UDPConn
	lanIP     net.IP
	ufrag     string
	pwd       string
	token     string
	cert      webrtc.Certificate
	fpHex     string // sha-256 fingerprint, colon separated
	mu        sync.Mutex
	byRemote  map[string]*muxConn // remote ufrag -> conn
	byAddr    map[string]*muxConn
	events    []string
}

func (s *server) logf(f string, a ...any) {
	msg := time.Now().Format("15:04:05 ") + fmt.Sprintf(f, a...)
	log.Print(msg)
	s.mu.Lock()
	s.events = append(s.events, msg)
	if len(s.events) > 50 {
		s.events = s.events[len(s.events)-50:]
	}
	s.mu.Unlock()
}

func (s *server) readLoop() {
	buf := make([]byte, 65536)
	for {
		n, addr, err := s.udp.ReadFrom(buf)
		if err != nil {
			log.Fatal(err)
		}
		b := append([]byte(nil), buf[:n]...)
		key := addr.String()
		s.mu.Lock()
		c := s.byAddr[key]
		s.mu.Unlock()
		if stun.IsMessage(b) {
			m := &stun.Message{Raw: b}
			if m.Decode() == nil && m.Type == stun.BindingRequest {
				var u stun.Username
				if u.GetFrom(m) == nil {
					parts := strings.SplitN(u.String(), ":", 2)
					if len(parts) == 2 && parts[0] == s.ufrag {
						remote := parts[1]
						s.mu.Lock()
						rc := s.byRemote[remote]
						if rc == nil {
							rc = newMuxConn(s.udp)
							s.byRemote[remote] = rc
							s.mu.Unlock()
							go s.newPeer(remote, rc, addr)
							s.mu.Lock()
						}
						s.byAddr[key] = rc
						s.mu.Unlock()
						c = rc
					}
				}
			}
		}
		if c == nil {
			continue
		}
		select {
		case c.ch <- pkt{b, addr}:
		default:
		}
	}
}

func (s *server) newPeer(remoteUfrag string, conn *muxConn, addr net.Addr) {
	s.logf("new client %s (ufrag %s...)", addr, remoteUfrag[:min(6, len(remoteUfrag))])
	se := webrtc.SettingEngine{}
	se.SetLite(true)
	se.SetICECredentials(s.ufrag, s.pwd)
	se.SetICEUDPMux(&clientMux{conn: conn, listen: &net.UDPAddr{IP: s.lanIP, Port: *flagPort}})
	se.SetNetworkTypes([]webrtc.NetworkType{webrtc.NetworkTypeUDP4})
	se.SetICEMulticastDNSMode(ice.MulticastDNSModeDisabled)
	se.DisableCertificateFingerprintVerification(true)
	if err := se.SetAnsweringDTLSRole(webrtc.DTLSRoleServer); err != nil {
		s.logf("dtls role: %v", err)
	}
	api := webrtc.NewAPI(webrtc.WithSettingEngine(se))
	pc, err := api.NewPeerConnection(webrtc.Configuration{Certificates: []webrtc.Certificate{s.cert}})
	if err != nil {
		s.logf("pc: %v", err)
		return
	}
	cleanup := func() {
		s.mu.Lock()
		delete(s.byRemote, remoteUfrag)
		for k, v := range s.byAddr {
			if v == conn {
				delete(s.byAddr, k)
			}
		}
		s.mu.Unlock()
		conn.Close()
	}
	pc.OnConnectionStateChange(func(st webrtc.PeerConnectionState) {
		s.logf("client %s: %s", addr, st)
		if st == webrtc.PeerConnectionStateFailed || st == webrtc.PeerConnectionStateClosed || st == webrtc.PeerConnectionStateDisconnected {
			pc.Close()
			cleanup()
		}
	})
	negotiated, id := true, uint16(0)
	dc, err := pc.CreateDataChannel("game", &webrtc.DataChannelInit{Negotiated: &negotiated, ID: &id})
	if err != nil {
		s.logf("dc: %v", err)
		return
	}
	s.handleChannel(dc, addr)

	// Synthesize the phone's offer: we only know its ufrag (from STUN). An ICE-lite
	// answerer never sends checks, so the remote password is never used; DTLS
	// fingerprint verification of the client is disabled (auth is the QR token).
	dummyFP := strings.TrimSuffix(strings.Repeat("00:", 32), ":")
	offer := strings.Join([]string{
		"v=0", "o=- 0 0 IN IP4 0.0.0.0", "s=-", "t=0 0", "a=group:BUNDLE 0",
		"m=application 9 UDP/DTLS/SCTP webrtc-datachannel", "c=IN IP4 0.0.0.0",
		"a=mid:0", "a=ice-ufrag:" + remoteUfrag, "a=ice-pwd:unusedunusedunusedunused",
		"a=fingerprint:sha-256 " + dummyFP, "a=setup:actpass",
		"a=sctp-port:5000", "a=max-message-size:262144", "",
	}, "\r\n")
	if err := pc.SetRemoteDescription(webrtc.SessionDescription{Type: webrtc.SDPTypeOffer, SDP: offer}); err != nil {
		s.logf("setRemote: %v", err)
		return
	}
	ans, err := pc.CreateAnswer(nil)
	if err != nil {
		s.logf("answer: %v", err)
		return
	}
	if err := pc.SetLocalDescription(ans); err != nil {
		s.logf("setLocal: %v", err)
	}
}

func (s *server) handleChannel(dc *webrtc.DataChannel, addr net.Addr) {
	authed := false
	var photo []byte
	var expect int
	dc.OnOpen(func() { s.logf("data channel open from %s", addr) })
	dc.OnMessage(func(m webrtc.DataChannelMessage) {
		if m.IsString {
			var msg map[string]any
			if json.Unmarshal(m.Data, &msg) != nil {
				return
			}
			switch msg["type"] {
			case "hello":
				authed = msg["token"] == s.token
				s.logf("hello from %s ua=%v authed=%v", addr, msg["ua"], authed)
				if !authed {
					dc.Close()
					return
				}
				dc.SendText(`{"type":"welcome"}`)
			case "photo":
				if !authed {
					return
				}
				expect = int(msg["size"].(float64))
				photo = make([]byte, 0, expect)
			case "ping":
				dc.SendText(string(m.Data))
			}
			return
		}
		if !authed || expect == 0 {
			return
		}
		photo = append(photo, m.Data...)
		if len(photo) >= expect {
			os.MkdirAll("photos", 0o755)
			name := filepath.Join("photos", time.Now().Format("150405.000")+".jpg")
			os.WriteFile(name, photo, 0o644)
			s.logf("photo %d bytes saved to %s", len(photo), name)
			dc.SendText(`{"type":"photoOk"}`)
			expect = 0
		}
	})
}

func (s *server) qrURL() string {
	fp, _ := hex.DecodeString(strings.ReplaceAll(s.fpHex, ":", ""))
	v := url.Values{}
	v.Set("h", s.lanIP.String())
	v.Set("p", fmt.Sprint(*flagPort))
	v.Set("u", s.ufrag)
	v.Set("w", s.pwd)
	v.Set("f", base64.RawURLEncoding.EncodeToString(fp))
	v.Set("t", s.token)
	return *flagPage + "#" + v.Encode()
}

func randStr(n int) string {
	const a = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"
	b := make([]byte, n)
	rand.Read(b)
	for i := range b {
		b[i] = a[int(b[i])%len(a)]
	}
	return string(b)
}

func lanIP() net.IP {
	c, err := net.Dial("udp4", "192.0.2.1:9")
	if err != nil {
		log.Fatal("cannot determine LAN IP, pass -ip")
	}
	defer c.Close()
	return c.LocalAddr().(*net.UDPAddr).IP
}

func main() {
	flag.Parse()
	s := &server{byRemote: map[string]*muxConn{}, byAddr: map[string]*muxConn{}}
	if *flagIP != "" {
		s.lanIP = net.ParseIP(*flagIP)
	} else {
		s.lanIP = lanIP()
	}
	s.ufrag, s.pwd, s.token = randStr(8), randStr(24), randStr(12)
	key, _ := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
	cert, err := webrtc.GenerateCertificate(key)
	if err != nil {
		log.Fatal(err)
	}
	s.cert = *cert
	fps, _ := cert.GetFingerprints()
	for _, f := range fps {
		if f.Algorithm == "sha-256" {
			s.fpHex = f.Value
		}
	}
	s.udp, err = net.ListenUDP("udp4", &net.UDPAddr{Port: *flagPort})
	if err != nil {
		log.Fatal(err)
	}
	go s.readLoop()

	http.HandleFunc("/qr.png", func(w http.ResponseWriter, r *http.Request) {
		png, _ := qrcode.Encode(s.qrURL(), qrcode.Medium, 512)
		w.Header().Set("Content-Type", "image/png")
		w.Write(png)
	})
	http.HandleFunc("/photos/", func(w http.ResponseWriter, r *http.Request) {
		http.ServeFile(w, r, filepath.Join("photos", filepath.Base(r.URL.Path)))
	})
	http.Handle("/phone/", http.StripPrefix("/phone/", http.FileServer(http.Dir("phone"))))
	http.HandleFunc("/", func(w http.ResponseWriter, r *http.Request) {
		if r.URL.Path != "/" {
			http.NotFound(w, r)
			return
		}
		files, _ := filepath.Glob("photos/*.jpg")
		sort.Sort(sort.Reverse(sort.StringSlice(files)))
		s.mu.Lock()
		ev := append([]string(nil), s.events...)
		s.mu.Unlock()
		fmt.Fprintf(w, `<!doctype html><meta charset=utf-8><meta http-equiv=refresh content=2>
<title>spike</title><body style="font-family:sans-serif;background:#111;color:#eee">
<div style="display:flex;gap:24px"><div><img src="/qr.png" width=360><p style="max-width:360px;word-break:break-all;font-size:11px">%s</p></div>
<pre style="font-size:12px">%s</pre></div><div>`, s.qrURL(), strings.Join(ev, "\n"))
		for _, f := range files {
			fmt.Fprintf(w, `<img src="/photos/%s" height=220 style="margin:4px">`, filepath.Base(f))
		}
		fmt.Fprint(w, `</div>`)
	})
	log.Printf("status page: http://%s/   phone page: %s   udp %s:%d", *flagHTTP, *flagPage, s.lanIP, *flagPort)
	log.Fatal(http.ListenAndServe(*flagHTTP, nil))
}
