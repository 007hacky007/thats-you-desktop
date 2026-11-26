package server

import (
	"errors"
	"regexp"
	"strings"
	"testing"
)

var joinRe = regexp.MustCompile(`^(https?://[^#]*)#h=(\d+\.\d+\.\d+\.\d+)&p=(\d+)&u=([A-Za-z0-9]{8})&w=([A-Za-z0-9]{24})&f=([A-Za-z0-9_-]{43})&t=([A-Za-z0-9]{16})$`)

func TestBuildJoinURL(t *testing.T) {
	fp := make([]byte, 32)
	for i := range fp {
		fp[i] = byte(0xF0 + i)
	}
	u, err := BuildJoinURL("https://example.invalid/iwyk/#old=1", "192.168.1.23", 47000,
		"abcdEFGH", "abcdefghijklmnopqrstuvwx", fp, "0123456789abcdef")
	if err != nil {
		t.Fatal(err)
	}
	want := "https://example.invalid/iwyk/#h=192.168.1.23&p=47000&u=abcdEFGH&w=abcdefghijklmnopqrstuvwx&f=8PHy8_T19vf4-fr7_P3-_wABAgMEBQYHCAkKCwwNDg8&t=0123456789abcdef"
	if u != want {
		t.Fatalf("got  %s\nwant %s", u, want)
	}
	if !joinRe.MatchString(u) {
		t.Fatalf("regexp mismatch")
	}
	u, err = BuildJoinURL("http://127.0.0.1:8088/", "127.0.0.1", 1, "abcdEFGH", "abcdefghijklmnopqrstuvwx", fp, "0123456789abcdef")
	if err != nil || !strings.HasPrefix(u, "http://127.0.0.1:8088/#h=127.0.0.1&p=1&") {
		t.Fatalf("http: %s %v", u, err)
	}
	for _, base := range []string{"", "ftp://x/", "example.invalid/", "HTTPS//x", "https://a b/", "https://x/" + strings.Repeat("a", 4000), "javascript:alert(1)"} {
		if _, err := BuildJoinURL(base, "1.2.3.4", 1, "u", "w", fp, "t"); !errors.Is(err, ErrArg) {
			t.Errorf("base %q: %v", base[:min(len(base), 30)], err)
		}
	}
	if _, err := BuildJoinURL("https://x/", "::1", 1, "u", "w", fp, "t"); !errors.Is(err, ErrArg) {
		t.Errorf("ipv6 accepted")
	}
	if _, err := BuildJoinURL("https://x/", "1.2.3.4", 0, "u", "w", fp, "t"); !errors.Is(err, ErrArg) {
		t.Errorf("port 0 accepted")
	}
	if _, err := BuildJoinURL("https://x/", "1.2.3.4", 1, "u", "w", fp[:31], "t"); !errors.Is(err, ErrArg) {
		t.Errorf("short fingerprint accepted")
	}
	// The longest allowed base still fits in 4096 bytes.
	long := "https://x/" + strings.Repeat("a", maxBaseURL-10)
	u, err = BuildJoinURL(long, "255.255.255.255", 65535, "abcdEFGH", "abcdefghijklmnopqrstuvwx", fp, "0123456789abcdef")
	if err != nil || len(u) > 4096 {
		t.Fatalf("long base: %d %v", len(u), err)
	}
}
