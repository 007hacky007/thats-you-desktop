package server

import (
	"net"
	"os"
	"sync"
	"time"
)

// muxConn is the per-peer view of the shared UDP socket: the read loop
// pushes the peer's packets into ch, writes go straight to the socket.
type muxConn struct {
	udp    *net.UDPConn
	ch     chan packet
	closed chan struct{}
	once   sync.Once
	mu     sync.Mutex
	dl     time.Time
	dlCh   chan struct{} // closed when the deadline changes
}

type packet struct {
	b    []byte
	addr net.Addr
}

func newMuxConn(u *net.UDPConn) *muxConn {
	return &muxConn{udp: u, ch: make(chan packet, peerPacketBacklog), closed: make(chan struct{}), dlCh: make(chan struct{})}
}

// deliver queues one packet, dropping it when the peer is not keeping up
// (SCTP retransmits).
func (c *muxConn) deliver(b []byte, addr net.Addr) {
	select {
	case <-c.closed:
	case c.ch <- packet{b, addr}:
	default:
	}
}

func (c *muxConn) ReadFrom(p []byte) (int, net.Addr, error) {
	for {
		c.mu.Lock()
		dl, dlCh := c.dl, c.dlCh
		c.mu.Unlock()
		var timer <-chan time.Time
		var t *time.Timer
		if !dl.IsZero() {
			d := time.Until(dl)
			if d <= 0 {
				return 0, nil, os.ErrDeadlineExceeded
			}
			t = time.NewTimer(d)
			timer = t.C
		}
		stop := func() {
			if t != nil {
				t.Stop()
			}
		}
		select {
		case k := <-c.ch:
			stop()
			return copy(p, k.b), k.addr, nil
		case <-c.closed:
			stop()
			return 0, nil, net.ErrClosed
		case <-timer:
			return 0, nil, os.ErrDeadlineExceeded
		case <-dlCh:
			stop()
		}
	}
}

func (c *muxConn) WriteTo(p []byte, a net.Addr) (int, error) {
	select {
	case <-c.closed:
		return 0, net.ErrClosed
	default:
	}
	return c.udp.WriteTo(p, a)
}

func (c *muxConn) Close() error {
	c.once.Do(func() { close(c.closed) })
	return nil
}

func (c *muxConn) LocalAddr() net.Addr           { return c.udp.LocalAddr() }
func (c *muxConn) SetDeadline(t time.Time) error { return c.SetReadDeadline(t) }
func (c *muxConn) SetReadDeadline(t time.Time) error {
	c.mu.Lock()
	c.dl = t
	close(c.dlCh)
	c.dlCh = make(chan struct{})
	c.mu.Unlock()
	return nil
}
func (c *muxConn) SetWriteDeadline(time.Time) error { return nil }

// peerMux satisfies ice.UDPMux for exactly one PeerConnection.
type peerMux struct {
	conn   *muxConn
	listen net.Addr
}

func (m *peerMux) Close() error                                     { return m.conn.Close() }
func (m *peerMux) GetConn(string, net.Addr) (net.PacketConn, error) { return m.conn, nil }
func (m *peerMux) RemoveConnByUfrag(string)                         {}
func (m *peerMux) GetListenAddresses() []net.Addr                   { return []net.Addr{m.listen} }
