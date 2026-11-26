package main

import (
	"bufio"
	"context"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"io"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"iwyknet/server"
)

type line map[string]any

func TestBotSession(t *testing.T) {
	s, err := server.Start(server.Config{LANIP: "127.0.0.1", CertPath: filepath.Join(t.TempDir(), "c.pem"), LogLevel: server.LogOff, PingInterval: 100 * time.Millisecond})
	if err != nil {
		t.Fatal(err)
	}
	defer s.Stop()
	join, _ := s.JoinURL("http://127.0.0.1:8088/")

	inR, inW := io.Pipe()
	outR, outW := io.Pipe()
	done := make(chan int, 1)
	go func() {
		done <- run(context.Background(), []string{"-url", join, "-uid", "bot-uid", "-ping", "100ms"}, inR, outW, io.Discard)
		outW.Close()
	}()
	lines := make(chan line, 100)
	go func() {
		sc := bufio.NewScanner(outR)
		for sc.Scan() {
			var l line
			if json.Unmarshal(sc.Bytes(), &l) != nil {
				t.Errorf("bad output line %q", sc.Text())
				continue
			}
			lines <- l
		}
		close(lines)
	}()
	next := func() line {
		select {
		case l := <-lines:
			return l
		case <-time.After(10 * time.Second):
			t.Fatal("no output line")
			return nil
		}
	}
	nextEvent := func(kind server.EventKind) server.Event {
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		for {
			ev, err := s.Next(ctx)
			if err != nil {
				t.Fatalf("waiting for %d: %v", kind, err)
			}
			if ev.Kind == kind && !(kind == server.EventText && string(ev.Data) == `{"t":"ping"}`) {
				return ev
			}
		}
	}
	if l := next(); l["ev"] != "open" {
		t.Fatalf("first line %v", l)
	}
	nextEvent(server.EventOpened)
	hello := nextEvent(server.EventText)
	if !strings.Contains(string(hello.Data), `"uid":"bot-uid"`) {
		t.Fatalf("hello %s", hello.Data)
	}
	sid := hello.Session

	s.SendText(sid, []byte(`{"t":"msg","id":-7503,"json":{"TypeString":"X<y>","N":1}}`))
	l := next()
	frame, _ := l["frame"].(map[string]any)
	if l["ev"] != "text" || frame["t"] != "msg" {
		t.Fatalf("text line %v", l)
	}
	data := []byte("content bytes")
	s.SendBinary(sid, 42, data)
	l = next()
	sum := sha256.Sum256(data)
	if l["ev"] != "binary" || l["id"] != float64(42) || l["len"] != float64(len(data)) || l["sha256"] != hex.EncodeToString(sum[:]) {
		t.Fatalf("binary line %v", l)
	}

	io.WriteString(inW, `{"cmd":"msg","json":{"TypeString":"ClientRequestPlayerIDMessage","UID":"bot-uid"}}`+"\n")
	ev := nextEvent(server.EventText)
	if string(ev.Data) != `{"t":"msg","id":-7503,"json":{"TypeString":"ClientRequestPlayerIDMessage","UID":"bot-uid"}}` {
		t.Fatalf("msg %s", ev.Data)
	}
	io.WriteString(inW, `{"cmd":"text","frame":{"t":"custom", "a": [1, 2]}}`+"\n")
	if ev := nextEvent(server.EventText); string(ev.Data) != `{"t":"custom","a":[1,2]}` {
		t.Fatalf("text %s", ev.Data)
	}
	io.WriteString(inW, `{"cmd":"binary","id":7,"hex":"00ff10"}`+"\n")
	if ev := nextEvent(server.EventBinary); ev.TransferID != 7 || hex.EncodeToString(ev.Data) != "00ff10" {
		t.Fatalf("binary hex %v", ev)
	}
	io.WriteString(inW, `{"cmd":"binary","id":8,"len":20000}`+"\n")
	if ev := nextEvent(server.EventBinary); ev.TransferID != 8 || len(ev.Data) != 20000 {
		t.Fatalf("binary len %v", ev.TransferID)
	}
	io.WriteString(inW, `{"cmd":"bogus"}`+"\n")
	if l := next(); l["ev"] != "error" {
		t.Fatalf("bogus %v", l)
	}
	io.WriteString(inW, `{"cmd":"close"}`+"\n")
	for {
		l := next()
		if l == nil || l["ev"] == "closed" {
			break
		}
	}
	select {
	case code := <-done:
		if code != 0 {
			t.Fatalf("exit %d", code)
		}
	case <-time.After(5 * time.Second):
		t.Fatal("bot did not exit")
	}
	if ev := nextEvent(server.EventClosed); ev.Code != server.CloseRemote {
		t.Fatalf("closed code %d", ev.Code)
	}
}

func TestBotBadArgs(t *testing.T) {
	if run(context.Background(), nil, strings.NewReader(""), io.Discard, io.Discard) != 2 {
		t.Fatal("missing url")
	}
	var out strings.Builder
	if run(context.Background(), []string{"-url", "https://x/#h=1"}, strings.NewReader(""), &out, io.Discard) != 1 || !strings.Contains(out.String(), `"ev":"error"`) {
		t.Fatalf("bad url: %s", out.String())
	}
}
