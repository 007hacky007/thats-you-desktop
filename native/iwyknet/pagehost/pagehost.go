// Package pagehost serves the phone page from the game's computer and publishes it through a
// Cloudflare quick tunnel (cloudflared tunnel --url), so phones get an HTTPS address with a
// trusted certificate without any account or domain. Game traffic does not go through the
// tunnel: phones talk to the game directly over WebRTC on the LAN.
package pagehost

import (
	"bufio"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"mime"
	"net"
	"net/http"
	neturl "net/url"
	"os"
	"os/exec"
	"path"
	"regexp"
	"strings"
	"sync"
	"time"
)

// State of the tunnel.
type State int

const (
	Starting State = iota
	Ready
	Failed
)

// Config of a page host. Zero values give the defaults.
type Config struct {
	Dir         string        // the phone page (index.html at its root)
	Cloudflared string        // path of the cloudflared executable
	Listen      string        // local address; "" = 127.0.0.1:0
	Retry       time.Duration // wait before restarting a failed tunnel; 0 = 15s
	Env         []string      // extra environment for cloudflared (tests)
	// Verify must succeed before the address is shown; nil = VerifyPublic. Showing the address
	// earlier would let phones (and the home router's DNS cache) see "no such host" for it.
	Verify      func(url string) error
	VerifyEvery time.Duration // pause between Verify attempts; 0 = 2s
	VerifyFor   time.Duration // give up after; 0 = 90s
}

// VerifyPublic resolves the tunnel's host name over DNS-over-HTTPS (so the local resolver never
// caches a negative answer) and fetches the page through the tunnel using that address.
func VerifyPublic(pageURL string) error {
	u, err := neturl.Parse(pageURL)
	if err != nil {
		return err
	}
	host := u.Hostname()
	ip, err := dohLookup(host)
	if err != nil {
		return err
	}
	dialer := &net.Dialer{Timeout: 10 * time.Second}
	tr := &http.Transport{
		DialContext: func(ctx context.Context, network, addr string) (net.Conn, error) {
			_, port, _ := net.SplitHostPort(addr)
			return dialer.DialContext(ctx, network, net.JoinHostPort(ip, port))
		},
		TLSHandshakeTimeout: 10 * time.Second,
	}
	defer tr.CloseIdleConnections()
	c := &http.Client{Transport: tr, Timeout: 15 * time.Second}
	r, err := c.Get(pageURL)
	if err != nil {
		return err
	}
	io.Copy(io.Discard, io.LimitReader(r.Body, 1<<20))
	r.Body.Close()
	if r.StatusCode != http.StatusOK {
		return fmt.Errorf("page through the tunnel: %s", r.Status)
	}
	return nil
}

// dohLookup asks Cloudflare's DNS-over-HTTPS service (by IP, so no local DNS is involved).
func dohLookup(host string) (string, error) {
	req, _ := http.NewRequest(http.MethodGet, "https://1.1.1.1/dns-query?type=A&name="+neturl.QueryEscape(host), nil)
	req.Header.Set("Accept", "application/dns-json")
	c := &http.Client{Timeout: 10 * time.Second}
	r, err := c.Do(req)
	if err != nil {
		return "", err
	}
	defer r.Body.Close()
	var ans struct {
		Status int
		Answer []struct {
			Type int
			Data string
		}
	}
	if err := json.NewDecoder(io.LimitReader(r.Body, 1<<16)).Decode(&ans); err != nil {
		return "", err
	}
	for _, a := range ans.Answer {
		if a.Type == 1 && net.ParseIP(a.Data) != nil {
			return a.Data, nil
		}
	}
	return "", fmt.Errorf("%s is not in DNS yet", host)
}

var urlRe = regexp.MustCompile(`https://[a-z0-9]+(?:-[a-z0-9]+)+\.trycloudflare\.com\b`)

// ParseURL finds the quick tunnel address in a cloudflared log line.
func ParseURL(line string) (string, bool) {
	m := urlRe.FindString(line)
	if m == "" || strings.HasPrefix(m, "https://api.") {
		return "", false
	}
	i := strings.Index(line, m) + len(m)
	if i < len(line) && line[i] == '.' { // part of a longer host name
		return "", false
	}
	return m, true
}

// Host is a running page server with its tunnel.
type Host struct {
	cfg  Config
	ln   net.Listener
	srv  *http.Server
	stop chan struct{}
	done chan struct{}

	mu       sync.Mutex
	state    State
	url      string
	detail   string
	attempts int
	child    *exec.Cmd
	running  bool
}

// Start serves cfg.Dir and starts the tunnel in the background.
func Start(cfg Config) (*Host, error) {
	st, err := os.Stat(cfg.Dir)
	if err != nil || !st.IsDir() {
		return nil, fmt.Errorf("phone page folder not found: %s", cfg.Dir)
	}
	if cfg.Listen == "" {
		cfg.Listen = "127.0.0.1:0"
	}
	if cfg.Retry == 0 {
		cfg.Retry = 15 * time.Second
	}
	if cfg.Verify == nil {
		cfg.Verify = VerifyPublic
	}
	if cfg.VerifyEvery == 0 {
		cfg.VerifyEvery = 2 * time.Second
	}
	if cfg.VerifyFor == 0 {
		cfg.VerifyFor = 90 * time.Second
	}
	ln, err := net.Listen("tcp", cfg.Listen)
	if err != nil {
		return nil, fmt.Errorf("cannot serve the phone page: %w", err)
	}
	h := &Host{cfg: cfg, ln: ln, stop: make(chan struct{}), done: make(chan struct{})}
	h.srv = &http.Server{Handler: h.handler(), ReadHeaderTimeout: 10 * time.Second}
	go h.srv.Serve(ln)
	go h.loop()
	return h, nil
}

// Addr is the local address the page is served on.
func (h *Host) Addr() string { return h.ln.Addr().String() }

// State returns the tunnel state, its public URL when ready, and a detail on failure.
func (h *Host) State() (State, string, string) {
	h.mu.Lock()
	defer h.mu.Unlock()
	return h.state, h.url, h.detail
}

// Attempts counts tunnel starts so far.
func (h *Host) Attempts() int {
	h.mu.Lock()
	defer h.mu.Unlock()
	return h.attempts
}

// ChildRunning reports whether a cloudflared process is alive.
func (h *Host) ChildRunning() bool {
	h.mu.Lock()
	defer h.mu.Unlock()
	return h.running
}

// Stop ends the tunnel and the server. Safe to call more than once.
func (h *Host) Stop() {
	select {
	case <-h.stop:
	default:
		close(h.stop)
	}
	h.mu.Lock()
	if h.child != nil && h.running {
		killTree(h.child)
	}
	h.mu.Unlock()
	<-h.done
	h.srv.Close()
}

func (h *Host) handler() http.Handler {
	mime.AddExtensionType(".webmanifest", "application/manifest+json")
	mime.AddExtensionType(".js", "text/javascript; charset=utf-8")
	mime.AddExtensionType(".mjs", "text/javascript; charset=utf-8")
	files := http.FileServer(http.Dir(h.cfg.Dir))
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		if r.Method != http.MethodGet && r.Method != http.MethodHead {
			http.Error(w, "method not allowed", http.StatusMethodNotAllowed)
			return
		}
		p := path.Clean("/" + r.URL.Path)
		if p == "/" || p == "/index.html" || p == "/sw.js" {
			w.Header().Set("Cache-Control", "no-cache")
		}
		w.Header().Set("X-Content-Type-Options", "nosniff")
		files.ServeHTTP(w, r)
	})
}

func (h *Host) set(s State, url, detail string) {
	h.mu.Lock()
	h.state, h.url, h.detail = s, url, detail
	h.mu.Unlock()
}

func (h *Host) loop() {
	defer close(h.done)
	for {
		err := h.runOnce()
		select {
		case <-h.stop:
			return
		default:
		}
		h.set(Failed, "", err.Error())
		select {
		case <-h.stop:
			return
		case <-time.After(h.cfg.Retry):
		}
	}
}

// verify waits until the address works from outside, then shows it.
func (h *Host) verify(cmd *exec.Cmd, url string) {
	deadline := time.Now().Add(h.cfg.VerifyFor)
	for {
		err := h.cfg.Verify(url)
		h.mu.Lock()
		current := h.child == cmd && h.running
		h.mu.Unlock()
		if !current {
			return
		}
		if err == nil {
			h.set(Ready, url, "")
			return
		}
		if time.Now().After(deadline) {
			h.set(Starting, "", "the tunnel address does not work yet: "+err.Error())
			killTree(cmd) // restart with a new address
			return
		}
		select {
		case <-h.stop:
			return
		case <-time.After(h.cfg.VerifyEvery):
		}
	}
}

// runOnce runs cloudflared until it exits and returns why it ended.
func (h *Host) runOnce() error {
	h.mu.Lock()
	h.attempts++
	if h.state != Failed {
		h.state = Starting
	}
	h.mu.Unlock()
	cmd := exec.Command(h.cfg.Cloudflared, "tunnel", "--no-autoupdate", "--url", "http://"+h.Addr())
	cmd.Env = append(os.Environ(), h.cfg.Env...)
	hideWindow(cmd)
	pr, pw := io.Pipe()
	cmd.Stdout, cmd.Stderr = pw, pw
	if err := cmd.Start(); err != nil {
		pw.Close()
		return fmt.Errorf("cannot start cloudflared: %w", err)
	}
	h.mu.Lock()
	h.child, h.running = cmd, true
	stopped := false
	select {
	case <-h.stop:
		stopped = true
	default:
	}
	h.mu.Unlock()
	if stopped {
		killTree(cmd)
	}
	attachChild(cmd)
	var last []string
	lines := make(chan struct{})
	go func() {
		defer close(lines)
		sc := bufio.NewScanner(pr)
		sc.Buffer(make([]byte, 64*1024), 1024*1024)
		for sc.Scan() {
			line := sc.Text()
			if u, ok := ParseURL(line); ok {
				go h.verify(cmd, u+"/")
			}
			if strings.Contains(line, "ERR") || strings.Contains(line, "error") {
				last = append(last, strings.TrimSpace(line))
				if len(last) > 3 {
					last = last[1:]
				}
			}
		}
	}()
	err := cmd.Wait()
	pw.Close()
	<-lines
	h.mu.Lock()
	h.running = false
	h.mu.Unlock()
	why := "the tunnel stopped"
	if len(last) > 0 {
		why = last[len(last)-1]
	} else if err != nil {
		why = "the tunnel stopped: " + err.Error()
	}
	return errors.New(why)
}
