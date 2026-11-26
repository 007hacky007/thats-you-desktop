package server

import (
	"context"
	"errors"
	"testing"
	"time"
)

func TestEventQueueCaps(t *testing.T) {
	q := newEventQueue(100, 3)
	if !q.push(Event{Kind: EventText, Data: make([]byte, 60)}, false) {
		t.Fatal("first")
	}
	if q.push(Event{Kind: EventText, Data: make([]byte, 41)}, false) {
		t.Fatal("over byte cap accepted")
	}
	if !q.push(Event{Kind: EventText, Data: make([]byte, 40)}, false) {
		t.Fatal("exact byte cap refused")
	}
	if !q.push(Event{Kind: EventOpened}, false) {
		t.Fatal("third event")
	}
	if q.push(Event{Kind: EventOpened}, false) {
		t.Fatal("over count cap accepted")
	}
	if !q.push(Event{Kind: EventClosed, Data: []byte("x")}, true) {
		t.Fatal("forced CLOSED refused")
	}
	if n, b := q.size(); n != 4 || b != 101 {
		t.Fatalf("size %d %d", n, b)
	}
	// popIf leaves a too-big event queued.
	if ev, ok, big := q.popIf(10); ok || !big || len(ev.Data) != 60 {
		t.Fatalf("popIf small: %v %v", ok, big)
	}
	if ev, ok, _ := q.popIf(60); !ok || len(ev.Data) != 60 {
		t.Fatal("popIf exact")
	}
	kinds := []EventKind{}
	for {
		ev, ok := q.pop()
		if !ok {
			break
		}
		kinds = append(kinds, ev.Kind)
	}
	if len(kinds) != 3 || kinds[2] != EventClosed {
		t.Fatalf("order %v", kinds)
	}
	if n, b := q.size(); n != 0 || b != 0 {
		t.Fatalf("not empty %d %d", n, b)
	}
}

func TestEventQueueDefaultCaps(t *testing.T) {
	q := newEventQueue(QueueMaxBytes, QueueMaxEvents)
	for i := 0; i < QueueMaxEvents; i++ {
		if !q.push(Event{Kind: EventText}, false) {
			t.Fatalf("refused at %d", i)
		}
	}
	if q.push(Event{Kind: EventText}, false) {
		t.Fatal("event 100001 accepted")
	}
	for i := 0; i < QueueMaxEvents; i++ {
		if _, ok := q.pop(); !ok {
			t.Fatalf("pop %d", i)
		}
	}
	for i := 0; i < 512; i++ {
		if !q.push(Event{Kind: EventBinary, Data: make([]byte, MaxBinary)}, false) {
			t.Fatalf("binary %d refused", i)
		}
	}
	if q.push(Event{Kind: EventBinary, Data: []byte{1}}, false) {
		t.Fatal("over 64 MiB accepted")
	}
}

func TestEventQueueWaitAndClose(t *testing.T) {
	q := newEventQueue(1000, 10)
	go func() {
		time.Sleep(20 * time.Millisecond)
		q.push(Event{Kind: EventLog, Data: []byte("hi")}, false)
	}()
	ctx, cancel := context.WithTimeout(context.Background(), 2*time.Second)
	defer cancel()
	ev, err := q.wait(ctx)
	if err != nil || string(ev.Data) != "hi" {
		t.Fatalf("wait: %v", err)
	}
	short, c2 := context.WithTimeout(context.Background(), 10*time.Millisecond)
	defer c2()
	if _, err := q.wait(short); !errors.Is(err, context.DeadlineExceeded) {
		t.Fatalf("timeout: %v", err)
	}
	q.push(Event{Kind: EventText}, false)
	q.close()
	if _, ok := q.pop(); ok {
		t.Fatal("close did not discard")
	}
	if q.push(Event{Kind: EventClosed}, true) {
		t.Fatal("push after close")
	}
	if _, err := q.wait(context.Background()); !errors.Is(err, ErrStopped) {
		t.Fatalf("wait after close: %v", err)
	}
}
