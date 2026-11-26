package console

import (
	"context"
	"errors"
	"fmt"
	"sync"
	"testing"
	"time"
)

// Sent is one thing the console sent through MemTransport.
type Sent struct {
	Session    int32
	Text       []byte
	TransferID int32
	Data       []byte
	Close      bool
}

// MemTransport is an in-memory Transport for tests.
type MemTransport struct {
	events chan Event
	mu     sync.Mutex
	sent   []Sent
	closed map[int32]bool
	notify chan struct{}
}

func NewMemTransport() *MemTransport {
	return &MemTransport{events: make(chan Event, 1000), closed: map[int32]bool{}, notify: make(chan struct{}, 1)}
}

func (m *MemTransport) Push(ev Event) { m.events <- ev }

func (m *MemTransport) Next(ctx context.Context) (Event, error) {
	select {
	case ev := <-m.events:
		return ev, nil
	case <-ctx.Done():
		return Event{}, ctx.Err()
	}
}

func (m *MemTransport) record(s Sent) error {
	m.mu.Lock()
	defer m.mu.Unlock()
	if m.closed[s.Session] && !s.Close {
		return errors.New("no session")
	}
	m.sent = append(m.sent, s)
	select {
	case m.notify <- struct{}{}:
	default:
	}
	return nil
}

func (m *MemTransport) SendText(s int32, t []byte) error {
	return m.record(Sent{Session: s, Text: append([]byte(nil), t...)})
}

func (m *MemTransport) SendBinary(s, tid int32, d []byte) error {
	return m.record(Sent{Session: s, TransferID: tid, Data: d})
}

func (m *MemTransport) Close(s int32) error {
	m.mu.Lock()
	already := m.closed[s]
	m.closed[s] = true
	m.mu.Unlock()
	if already {
		return errors.New("no session")
	}
	m.record(Sent{Session: s, Close: true})
	m.events <- Event{Kind: EventClosed, Session: s, Code: 1}
	return nil
}

func (m *MemTransport) JoinURL(base string) (string, error) {
	return base + "#h=127.0.0.1&p=1&u=uuuuuuuu&w=pppppppppppppppppppppppp&f=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&t=tttttttttttttttt", nil
}

// SentTo returns what was sent to session s.
func (m *MemTransport) SentTo(s int32) []Sent {
	m.mu.Lock()
	defer m.mu.Unlock()
	var out []Sent
	for _, x := range m.sent {
		if x.Session == s {
			out = append(out, x)
		}
	}
	return out
}

// textsTo returns decoded text frames sent to s; msg frames are reduced to
// their TypeString, others to "t:<t>".
func (m *MemTransport) labelsTo(s int32) []string {
	var out []string
	for _, x := range m.SentTo(s) {
		switch {
		case x.Close:
			out = append(out, "close")
		case x.Text != nil:
			v, _ := Decode(x.Text)
			f := v.(map[string]any)
			if f["t"] == "msg" {
				out = append(out, f["json"].(map[string]any)["TypeString"].(string))
			} else {
				out = append(out, "t:"+f["t"].(string))
			}
		default:
			out = append(out, fmt.Sprintf("bin:%d", x.TransferID))
		}
	}
	return out
}

// waitFor polls until pred holds.
func waitFor(t *testing.T, what string, pred func() bool) {
	t.Helper()
	deadline := time.Now().Add(3 * time.Second)
	for time.Now().Before(deadline) {
		if pred() {
			return
		}
		time.Sleep(5 * time.Millisecond)
	}
	t.Fatalf("timeout waiting for %s", what)
}
