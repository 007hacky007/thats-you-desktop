package client

import (
	"strings"
	"testing"
)

func TestParseJoinURLAndAnswer(t *testing.T) {
	u := "https://example.invalid/iwyk/?x=1#h=192.168.1.23&p=47000&u=abcdEFGH&w=abcdefghijklmnopqrstuvwx&f=8PHy8_T19vf4-fr7_P3-_wABAgMEBQYHCAkKCwwNDg8&t=0123456789abcdef"
	base, p, err := ParseJoinURL(u)
	if err != nil {
		t.Fatal(err)
	}
	if base != "https://example.invalid/iwyk/?x=1" || p.Host != "192.168.1.23" || p.Port != 47000 ||
		p.Ufrag != "abcdEFGH" || p.Pwd != "abcdefghijklmnopqrstuvwx" || p.Token != "0123456789abcdef" || len(p.Fingerprint) != 32 {
		t.Fatalf("%q %+v", base, p)
	}
	want := "v=0\r\n" +
		"o=- 0 0 IN IP4 192.168.1.23\r\n" +
		"s=-\r\n" +
		"t=0 0\r\n" +
		"a=ice-lite\r\n" +
		"a=group:BUNDLE 0\r\n" +
		"m=application 47000 UDP/DTLS/SCTP webrtc-datachannel\r\n" +
		"c=IN IP4 192.168.1.23\r\n" +
		"a=mid:0\r\n" +
		"a=ice-ufrag:abcdEFGH\r\n" +
		"a=ice-pwd:abcdefghijklmnopqrstuvwx\r\n" +
		"a=fingerprint:sha-256 F0:F1:F2:F3:F4:F5:F6:F7:F8:F9:FA:FB:FC:FD:FE:FF:00:01:02:03:04:05:06:07:08:09:0A:0B:0C:0D:0E:0F\r\n" +
		"a=setup:passive\r\n" +
		"a=sctp-port:5000\r\n" +
		"a=max-message-size:262144\r\n" +
		"a=candidate:1 1 udp 2130706431 192.168.1.23 47000 typ host\r\n" +
		"a=end-of-candidates\r\n"
	if got := AnswerSDP(p, "0"); got != want {
		t.Fatalf("answer:\n%s\nwant:\n%s", got, want)
	}
	if !strings.Contains(AnswerSDP(p, "data"), "a=group:BUNDLE data\r\n") {
		t.Fatal("mid not used")
	}
	for _, bad := range []string{
		"https://x/",
		"https://x/#h=1.2.3.4&p=1&u=a&w=b&f=8PHy8_T19vf4-fr7_P3-_wABAgMEBQYHCAkKCwwNDg8",
		"https://x/#h=::1&p=1&u=a&w=b&f=8PHy8_T19vf4-fr7_P3-_wABAgMEBQYHCAkKCwwNDg8&t=c",
		"https://x/#h=1.2.3.4&p=0&u=a&w=b&f=8PHy8_T19vf4-fr7_P3-_wABAgMEBQYHCAkKCwwNDg8&t=c",
		"https://x/#h=1.2.3.4&p=1&u=a&w=b&f=AAAA&t=c",
	} {
		if _, _, err := ParseJoinURL(bad); err == nil {
			t.Errorf("accepted %s", bad)
		}
	}
}

func TestOfferMid(t *testing.T) {
	sdp := "v=0\r\nm=audio 9 RTP 0\r\na=mid:a\r\nm=application 9 UDP/DTLS/SCTP webrtc-datachannel\r\na=mid:7\r\n"
	if m, err := OfferMid(sdp); err != nil || m != "7" {
		t.Fatalf("%q %v", m, err)
	}
	if _, err := OfferMid("v=0\r\n"); err == nil {
		t.Fatal("no mid accepted")
	}
}
