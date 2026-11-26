// Command iwykbot emulates the phone page over the WebRTC Direct transport
// and speaks JSON lines (contract 3.6).
//
//	iwykbot -url '<join url>' [-uid U] [-hello=true] [-ping 2s]
//
// Stdout: {"ev":"open"}, {"ev":"text","frame":{...}},
// {"ev":"binary","id":N,"len":L,"sha256":"<hex>"}, {"ev":"closed"},
// {"ev":"error","msg":"..."}.
// Stdin: {"cmd":"hello","uid":"..."}, {"cmd":"text","frame":{...}},
// {"cmd":"msg","json":{...}}, {"cmd":"binary","id":N,"hex":"..."} or
// {"cmd":"binary","id":N,"len":L}, {"cmd":"close"}.
// Exits 0 after the connection closed, 1 when it could not connect, 2 on
// bad flags. End of stdin does not close the connection.
package main

import (
	"bufio"
	"bytes"
	"context"
	"crypto/rand"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"os"
	"os/signal"
	"sync"
	"syscall"
	"time"

	"iwyknet/client"
)

type output struct {
	mu sync.Mutex
	w  io.Writer
}

func (o *output) emit(v any) {
	var b bytes.Buffer
	enc := json.NewEncoder(&b)
	enc.SetEscapeHTML(false)
	if enc.Encode(v) != nil {
		return
	}
	o.mu.Lock()
	defer o.mu.Unlock()
	o.w.Write(b.Bytes())
}

func (o *output) errorf(format string, a ...any) {
	o.emit(map[string]any{"ev": "error", "msg": fmt.Sprintf(format, a...)})
}

type command struct {
	Cmd   string          `json:"cmd"`
	UID   string          `json:"uid"`
	Frame json.RawMessage `json:"frame"`
	JSON  json.RawMessage `json:"json"`
	ID    int32           `json:"id"`
	Hex   string          `json:"hex"`
	Len   int             `json:"len"`
}

func randomUID() string {
	b := make([]byte, 8)
	rand.Read(b)
	return "bot-" + hex.EncodeToString(b)
}

func run(ctx context.Context, args []string, stdin io.Reader, stdout, stderr io.Writer) int {
	fs := flag.NewFlagSet("iwykbot", flag.ContinueOnError)
	fs.SetOutput(stderr)
	joinURL := fs.String("url", "", "join URL (the QR payload)")
	uid := fs.String("uid", "", "device uid (default: random)")
	hello := fs.Bool("hello", true, "send hello right after connecting")
	ping := fs.Duration("ping", 2*time.Second, "ping interval (0 = none)")
	timeout := fs.Duration("timeout", 17*time.Second, "connect timeout")
	if err := fs.Parse(args); err != nil {
		return 2
	}
	out := &output{w: stdout}
	if *joinURL == "" {
		fmt.Fprintln(stderr, "iwykbot: -url is required")
		return 2
	}
	if *uid == "" {
		*uid = randomUID()
	}
	_, params, err := client.ParseJoinURL(*joinURL)
	if err != nil {
		out.errorf("bad join url: %v", err)
		return 1
	}
	interval := *ping
	if interval <= 0 {
		interval = -1
	}
	dctx, cancel := context.WithTimeout(ctx, *timeout)
	c, err := client.Dial(dctx, params, client.Options{PingInterval: interval})
	cancel()
	if err != nil {
		out.errorf("connect: %v", err)
		return 1
	}
	defer c.Close()
	out.emit(map[string]any{"ev": "open"})
	if *hello {
		if err := c.Hello(*uid); err != nil {
			out.errorf("hello: %v", err)
		}
	}

	go func() {
		sc := bufio.NewScanner(stdin)
		sc.Buffer(make([]byte, 1<<20), 1<<20)
		for sc.Scan() {
			line := sc.Bytes()
			if len(line) == 0 {
				continue
			}
			if !handle(c, out, line, *uid) {
				return
			}
		}
	}()
	go func() {
		select {
		case <-ctx.Done():
			c.Close()
		case <-c.Done():
		}
	}()

	for {
		f, err := c.Next(context.Background())
		if err != nil {
			out.emit(map[string]any{"ev": "closed"})
			return 0
		}
		if f.Text == nil {
			sum := sha256.Sum256(f.Data)
			out.emit(map[string]any{"ev": "binary", "id": f.TransferID, "len": len(f.Data), "sha256": hex.EncodeToString(sum[:])})
			continue
		}
		var frame any = json.RawMessage(f.Text)
		if !json.Valid(f.Text) {
			frame = string(f.Text)
		}
		out.emit(map[string]any{"ev": "text", "frame": frame})
	}
}

// handle runs one stdin command; false stops reading stdin.
func handle(c *client.Client, out *output, line []byte, defaultUID string) bool {
	var cmd command
	if err := json.Unmarshal(line, &cmd); err != nil {
		out.errorf("bad command: %v", err)
		return true
	}
	var err error
	switch cmd.Cmd {
	case "hello":
		u := cmd.UID
		if u == "" {
			u = defaultUID
		}
		err = c.Hello(u)
	case "text":
		if len(cmd.Frame) == 0 {
			err = fmt.Errorf("text needs a frame")
			break
		}
		var buf []byte
		buf, err = compact(cmd.Frame)
		if err == nil {
			err = c.SendText(buf)
		}
	case "msg":
		if len(cmd.JSON) == 0 {
			err = fmt.Errorf("msg needs json")
			break
		}
		err = c.SendMsg(cmd.JSON)
	case "binary":
		var data []byte
		switch {
		case cmd.Hex != "":
			data, err = hex.DecodeString(cmd.Hex)
		case cmd.Len > 0:
			data = make([]byte, cmd.Len)
			rand.Read(data)
		default:
			err = fmt.Errorf("binary needs hex or len")
		}
		if err == nil {
			err = c.SendContent(cmd.ID, data)
		}
	case "close":
		c.Close()
		return false
	default:
		err = fmt.Errorf("unknown command %q", cmd.Cmd)
	}
	if err != nil {
		out.errorf("%s: %v", cmd.Cmd, err)
	}
	return true
}

func compact(raw json.RawMessage) ([]byte, error) {
	var b bytes.Buffer
	if err := json.Compact(&b, raw); err != nil {
		return nil, err
	}
	return b.Bytes(), nil
}

func main() {
	ctx, stop := signal.NotifyContext(context.Background(), os.Interrupt, syscall.SIGTERM)
	defer stop()
	os.Exit(run(ctx, os.Args[1:], os.Stdin, os.Stdout, os.Stderr))
}
