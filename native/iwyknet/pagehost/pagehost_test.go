package pagehost

import (
	"fmt"
	"io"
	"net/http"
	"os"
	"os/exec"
	"path/filepath"
	"strconv"
	"strings"
	"sync"
	"testing"
	"time"
)

// The test binary doubles as a fake cloudflared: with FAKE_CF set it prints like the real one
// (or fails) instead of running the tests.
func TestMain(m *testing.M) {
	switch os.Getenv("FAKE_CF") {
	case "":
		os.Exit(m.Run())
	case "ok":
		fmt.Fprintln(os.Stderr, "2026-11-27T09:00:00Z INF Requesting new quick Tunnel on trycloudflare.com...")
		fmt.Fprintln(os.Stderr, "2026-11-27T09:00:01Z INF |  https://infants-enables-lawyer-compliant.trycloudflare.com                    |")
		fmt.Fprintln(os.Stderr, "args:", strings.Join(os.Args[1:], " "))
		time.Sleep(30 * time.Second)
	case "fail":
		fmt.Fprintln(os.Stderr, "ERR failed to request quick Tunnel: no internet")
		os.Exit(1)
	}
}

func okVerify(string) error { return nil }

func site(t *testing.T) string {
	dir := t.TempDir()
	for name, body := range map[string]string{
		"index.html": "<!doctype html>hi", "sw.js": "self.x=1", "manifest.webmanifest": "{}",
		"js/main.js": "export {}",
	} {
		p := filepath.Join(dir, name)
		os.MkdirAll(filepath.Dir(p), 0o755)
		os.WriteFile(p, []byte(body), 0o644)
	}
	return dir
}

func self(t *testing.T) string {
	exe, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}
	return exe
}

func waitState(t *testing.T, h *Host, want State) (string, string) {
	deadline := time.Now().Add(10 * time.Second)
	for time.Now().Before(deadline) {
		s, url, detail := h.State()
		if s == want {
			return url, detail
		}
		time.Sleep(20 * time.Millisecond)
	}
	s, url, detail := h.State()
	t.Fatalf("state %v (%q, %q), want %v", s, url, detail, want)
	return "", ""
}

func TestParseURL(t *testing.T) {
	for line, want := range map[string]string{
		"2026-11-27T09:00:01Z INF |  https://infants-enables-lawyer-compliant.trycloudflare.com  |": "https://infants-enables-lawyer-compliant.trycloudflare.com",
		"INF Visit it at: https://a-b-c.trycloudflare.com/":                                         "https://a-b-c.trycloudflare.com",
		"INF Requesting new quick Tunnel on trycloudflare.com...":                                   "",
		"https://api.trycloudflare.com/tunnel":                                                      "",
		"https://evil.com/?x=a.trycloudflare.com":                                                   "",
	} {
		got, ok := ParseURL(line)
		if got != want || ok != (want != "") {
			t.Errorf("ParseURL(%q) = %q, %v; want %q", line, got, ok, want)
		}
	}
}

func TestServesPageWithTypes(t *testing.T) {
	h, err := Start(Config{Dir: site(t), Cloudflared: self(t), Env: []string{"FAKE_CF=ok"}, Verify: okVerify})
	if err != nil {
		t.Fatal(err)
	}
	defer h.Stop()
	for path, ctype := range map[string]string{
		"/": "text/html", "/sw.js": "text/javascript", "/manifest.webmanifest": "application/manifest+json",
		"/js/main.js": "text/javascript",
	} {
		r, err := http.Get("http://" + h.Addr() + path)
		if err != nil {
			t.Fatal(err)
		}
		io.Copy(io.Discard, r.Body)
		r.Body.Close()
		if r.StatusCode != 200 || !strings.HasPrefix(r.Header.Get("Content-Type"), ctype) {
			t.Errorf("%s: %d %q, want 200 %q", path, r.StatusCode, r.Header.Get("Content-Type"), ctype)
		}
		if (path == "/" || path == "/sw.js") && r.Header.Get("Cache-Control") != "no-cache" {
			t.Errorf("%s: Cache-Control %q", path, r.Header.Get("Cache-Control"))
		}
	}
	r, err := http.Get("http://" + h.Addr() + "/../secret")
	if err == nil {
		r.Body.Close()
		if r.StatusCode == 200 {
			t.Error("path outside the page served")
		}
	}
}

func TestTunnelReady(t *testing.T) {
	h, err := Start(Config{Dir: site(t), Cloudflared: self(t), Env: []string{"FAKE_CF=ok"}, Verify: okVerify})
	if err != nil {
		t.Fatal(err)
	}
	defer h.Stop()
	url, _ := waitState(t, h, Ready)
	if url != "https://infants-enables-lawyer-compliant.trycloudflare.com/" {
		t.Errorf("url %q", url)
	}
}

func TestTunnelFailsThenRetries(t *testing.T) {
	h, err := Start(Config{Dir: site(t), Cloudflared: self(t), Env: []string{"FAKE_CF=fail"}, Retry: 50 * time.Millisecond, Verify: okVerify})
	if err != nil {
		t.Fatal(err)
	}
	defer h.Stop()
	_, detail := waitState(t, h, Failed)
	if !strings.Contains(detail, "no internet") {
		t.Errorf("detail %q", detail)
	}
	deadline := time.Now().Add(5 * time.Second)
	for h.Attempts() < 3 && time.Now().Before(deadline) {
		time.Sleep(20 * time.Millisecond)
	}
	if h.Attempts() < 3 {
		t.Errorf("attempts %d, want retries", h.Attempts())
	}
}

func TestMissingCloudflared(t *testing.T) {
	h, err := Start(Config{Dir: site(t), Cloudflared: filepath.Join(t.TempDir(), "nope"), Retry: time.Hour})
	if err != nil {
		t.Fatal(err)
	}
	defer h.Stop()
	_, detail := waitState(t, h, Failed)
	if detail == "" {
		t.Error("no detail")
	}
}

func TestStopKillsTunnel(t *testing.T) {
	h, err := Start(Config{Dir: site(t), Cloudflared: self(t), Env: []string{"FAKE_CF=ok"}, Verify: okVerify})
	if err != nil {
		t.Fatal(err)
	}
	waitState(t, h, Ready)
	done := make(chan struct{})
	go func() { h.Stop(); close(done) }()
	select {
	case <-done:
	case <-time.After(5 * time.Second):
		t.Fatal("Stop did not return")
	}
	if h.ChildRunning() {
		t.Error("tunnel process still running")
	}
}

func TestMissingDirRefused(t *testing.T) {
	if _, err := Start(Config{Dir: filepath.Join(t.TempDir(), "nope"), Cloudflared: "x"}); err == nil {
		t.Error("want error")
	}
}

// TestRealTunnel opens a real quick tunnel; run with IWYK_REAL_CLOUDFLARED=/path/to/cloudflared.
func TestRealTunnel(t *testing.T) {
	cf := os.Getenv("IWYK_REAL_CLOUDFLARED")
	if cf == "" {
		t.Skip("set IWYK_REAL_CLOUDFLARED to run")
	}
	h, err := Start(Config{Dir: site(t), Cloudflared: cf})
	if err != nil {
		t.Fatal(err)
	}
	defer h.Stop()
	deadline := time.Now().Add(60 * time.Second)
	var url string
	for time.Now().Before(deadline) {
		if s, u, _ := h.State(); s == Ready {
			url = u
			break
		}
		time.Sleep(200 * time.Millisecond)
	}
	if url == "" {
		_, _, d := h.State()
		t.Fatalf("tunnel not ready: %s", d)
	}
	var lastErr string
	for time.Now().Before(deadline) {
		r, err := http.Get(url + "sw.js")
		if err != nil {
			lastErr = err.Error()
		}
		if err == nil {
			lastErr = r.Status
			b, _ := io.ReadAll(r.Body)
			r.Body.Close()
			if r.StatusCode == 200 && string(b) == "self.x=1" {
				t.Logf("served through %s", url)
				return
			}
		}
		time.Sleep(time.Second)
	}
	t.Fatalf("page not reachable through the tunnel %s: %s", url, lastErr)
}

func TestReadyOnlyAfterVerify(t *testing.T) {
	calls := 0
	var mu sync.Mutex
	verify := func(url string) error {
		mu.Lock()
		defer mu.Unlock()
		calls++
		if calls < 3 {
			return fmt.Errorf("not in DNS yet")
		}
		return nil
	}
	h, err := Start(Config{Dir: site(t), Cloudflared: self(t), Env: []string{"FAKE_CF=ok"}, Verify: verify,
		VerifyEvery: 10 * time.Millisecond})
	if err != nil {
		t.Fatal(err)
	}
	defer h.Stop()
	waitState(t, h, Ready)
	mu.Lock()
	defer mu.Unlock()
	if calls != 3 {
		t.Errorf("verify called %d times before ready", calls)
	}
}

func TestStaleTunnelFromCrashIsEnded(t *testing.T) {
	pid := filepath.Join(t.TempDir(), "cloudflared.pid")
	stale := exec.Command(self(t))
	stale.Env = append(os.Environ(), "FAKE_CF=ok")
	if err := stale.Start(); err != nil {
		t.Fatal(err)
	}
	exited := make(chan struct{})
	go func() { stale.Wait(); close(exited) }()
	os.WriteFile(pid, []byte(strconv.Itoa(stale.Process.Pid)), 0o644)
	h, err := Start(Config{Dir: site(t), Cloudflared: self(t), Env: []string{"FAKE_CF=ok"}, Verify: okVerify, PidFile: pid})
	if err != nil {
		t.Fatal(err)
	}
	select {
	case <-exited:
	case <-time.After(5 * time.Second):
		stale.Process.Kill()
		t.Fatal("stale tunnel from an earlier run still running")
	}
	waitState(t, h, Ready)
	b, _ := os.ReadFile(pid)
	if strings.TrimSpace(string(b)) == strconv.Itoa(stale.Process.Pid) || len(b) == 0 {
		t.Errorf("pid file %q not updated", b)
	}
	h.Stop()
	if _, err := os.Stat(pid); !os.IsNotExist(err) {
		t.Error("pid file left after Stop")
	}
}

func TestPidFileOfOtherProgramIsNotKilled(t *testing.T) {
	pid := filepath.Join(t.TempDir(), "cloudflared.pid")
	os.WriteFile(pid, []byte(strconv.Itoa(os.Getppid())), 0o644) // the go tool, not a tunnel
	h, err := Start(Config{Dir: site(t), Cloudflared: self(t), Env: []string{"FAKE_CF=ok"}, Verify: okVerify, PidFile: pid})
	if err != nil {
		t.Fatal(err)
	}
	defer h.Stop()
	if p, err := os.FindProcess(os.Getppid()); err != nil || p == nil {
		t.Fatal("parent gone")
	}
}
