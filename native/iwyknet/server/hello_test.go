package server

import (
	"regexp"
	"strings"
	"testing"
)

func TestCheckHello(t *testing.T) {
	const tok = "AbCdEfGh12345678"
	good := []struct{ frame, uid string }{
		{`{"t":"hello","token":"AbCdEfGh12345678","uid":"dev-1"}`, "dev-1"},
		{`{"uid":"x","extra":[1,2],"token":"AbCdEfGh12345678","t":"hello"}`, "x"},
		{`{"t":"hello","token":"AbCdEfGh12345678","uid":"` + strings.Repeat("u", 128) + `"}`, strings.Repeat("u", 128)},
		{`{"t":"hello","token":"AbCdEfGh12345678","uid":"caf\u00e9"}`, "caf\u00e9"},
	}
	for _, g := range good {
		uid, err := CheckHello([]byte(g.frame), tok)
		if err != nil || uid != g.uid {
			t.Errorf("%s: uid %q err %v", g.frame, uid, err)
		}
	}
	bad := []string{
		``,
		`[]`,
		`"hello"`,
		`not json`,
		`{"t":"ping"}`,
		`{"t":"msg","token":"AbCdEfGh12345678","uid":"a"}`,
		`{"t":"hello","token":"AbCdEfGh12345679","uid":"a"}`,
		`{"t":"hello","token":"AbCdEfGh1234567","uid":"a"}`,
		`{"t":"hello","token":"","uid":"a"}`,
		`{"t":"hello","uid":"a"}`,
		`{"t":"hello","token":"AbCdEfGh12345678"}`,
		`{"t":"hello","token":"AbCdEfGh12345678","uid":""}`,
		`{"t":"hello","token":"AbCdEfGh12345678","uid":5}`,
		`{"t":"hello","token":"AbCdEfGh12345678","uid":"` + strings.Repeat("u", 129) + `"}`,
		`{"t":"hello","token":"AbCdEfGh12345678","uid":"a\nb"}`,
		`{"t":"hello","token":"AbCdEfGh12345678","uid":"a\u0000"}`,
		`{"t":"hello","token":"AbCdEfGh12345678","uid":"a\u009f"}`,
		`{"t":"hello","token":"AbCdEfGh12345678","uid":"a","pad":"` + strings.Repeat("p", 4096) + `"}`,
		"{\"t\":\"hello\",\"token\":\"AbCdEfGh12345678\",\"uid\":\"a\xff\"}",
	}
	for _, b := range bad {
		if _, err := CheckHello([]byte(b), tok); err == nil {
			t.Errorf("accepted %q", b)
		}
	}
	// Exactly 4096 bytes is allowed.
	head := `{"t":"hello","token":"AbCdEfGh12345678","uid":"a","pad":"`
	f := head + strings.Repeat("p", MaxHello-len(head)-2) + `"}`
	if len(f) != MaxHello {
		t.Fatalf("len %d", len(f))
	}
	if _, err := CheckHello([]byte(f), tok); err != nil {
		t.Errorf("4096-byte hello: %v", err)
	}
}

func TestRandomString(t *testing.T) {
	re := regexp.MustCompile(`^[A-Za-z0-9]+$`)
	seen := map[string]bool{}
	for _, n := range []int{8, 16, 24, 100} {
		s := RandomString(n)
		if len(s) != n || !re.MatchString(s) {
			t.Fatalf("bad %q", s)
		}
		if seen[s] {
			t.Fatalf("repeat")
		}
		seen[s] = true
	}
}
