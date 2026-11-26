package server

import (
	"context"
	"sync"
)

// eventQueue is the bounded FIFO between the network goroutines and the
// host. Data events are refused when the queue is over its caps; forced
// events (OPENED, CLOSED) are always queued so every session ends with
// exactly one CLOSED.
type eventQueue struct {
	mu        sync.Mutex
	items     []Event
	head      int
	bytes     int
	maxBytes  int
	maxEvents int
	closed    bool
	notify    chan struct{} // closed and replaced on every push
}

func newEventQueue(maxBytes, maxEvents int) *eventQueue {
	return &eventQueue{maxBytes: maxBytes, maxEvents: maxEvents, notify: make(chan struct{})}
}

func (q *eventQueue) lenLocked() int { return len(q.items) - q.head }

// push appends ev. Unless force is set it fails when the event would take
// the queue over its byte or count cap.
func (q *eventQueue) push(ev Event, force bool) bool {
	q.mu.Lock()
	defer q.mu.Unlock()
	if q.closed {
		return false
	}
	if !force && (q.lenLocked()+1 > q.maxEvents || q.bytes+len(ev.Data) > q.maxBytes) {
		return false
	}
	q.items = append(q.items, ev)
	q.bytes += len(ev.Data)
	close(q.notify)
	q.notify = make(chan struct{})
	return true
}

// peek returns the next event without consuming it.
func (q *eventQueue) peek() (Event, bool) {
	q.mu.Lock()
	defer q.mu.Unlock()
	if q.lenLocked() == 0 {
		return Event{}, false
	}
	return q.items[q.head], true
}

// popIf consumes the next event when its payload is at most max bytes
// (max < 0: no limit). tooBig reports a refused event (left queued).
func (q *eventQueue) popIf(max int) (ev Event, ok bool, tooBig bool) {
	q.mu.Lock()
	defer q.mu.Unlock()
	if q.lenLocked() == 0 {
		return Event{}, false, false
	}
	ev = q.items[q.head]
	if max >= 0 && len(ev.Data) > max {
		return ev, false, true
	}
	q.items[q.head] = Event{}
	q.head++
	q.bytes -= len(ev.Data)
	if q.head > 1024 && q.head*2 > len(q.items) {
		q.items = append([]Event(nil), q.items[q.head:]...)
		q.head = 0
	} else if q.head == len(q.items) {
		q.items = q.items[:0]
		q.head = 0
	}
	return ev, true, false
}

func (q *eventQueue) pop() (Event, bool) {
	ev, ok, _ := q.popIf(-1)
	return ev, ok
}

// wait blocks until an event is available, ctx ends or the queue is closed.
func (q *eventQueue) wait(ctx context.Context) (Event, error) {
	for {
		q.mu.Lock()
		if q.lenLocked() > 0 {
			q.mu.Unlock()
			if ev, ok := q.pop(); ok {
				return ev, nil
			}
			continue
		}
		if q.closed {
			q.mu.Unlock()
			return Event{}, ErrStopped
		}
		ch := q.notify
		q.mu.Unlock()
		select {
		case <-ch:
		case <-ctx.Done():
			return Event{}, ctx.Err()
		}
	}
}

// close discards everything and refuses later pushes.
func (q *eventQueue) close() {
	q.mu.Lock()
	defer q.mu.Unlock()
	q.items, q.head, q.bytes = nil, 0, 0
	if !q.closed {
		q.closed = true
		close(q.notify)
		q.notify = make(chan struct{})
	}
}

func (q *eventQueue) size() (events, bytes int) {
	q.mu.Lock()
	defer q.mu.Unlock()
	return q.lenLocked(), q.bytes
}
