// Package server implements the game side of the WebRTC Direct transport:
// one UDP port, ICE-lite with fixed per-start credentials, a persistent DTLS
// certificate and one negotiated data channel (id 0) per phone.
package server

import "errors"

// Values mirrored from include/iwyknet.h (a test keeps them in sync).
const (
	ABIVersion = 1

	OK                = 0
	ENotStarted       = -1
	EAlreadyStarted   = -2
	EArg              = -3
	ENoSession        = -4
	ESmallBuf         = -5
	ETooBig           = -6
	EBind             = -7
	ECert             = -8
	ENoIP             = -9
	EFull             = -10
	EInternal         = -11
	CloseHost         = 1
	CloseRemote       = 2
	CloseTransport    = 3
	CloseProtocol     = 4
	CloseStopped      = 5
	LogDebug          = 0
	LogInfo           = 1
	LogWarn           = 2
	LogError          = 3
	LogOff            = 4
	MaxText           = 65536
	MaxBinary         = 131072
	Chunk             = 16384
	MaxSessions       = 16
	QRMaxModules      = 31329
	ChunkHeaderLen    = 12
	MaxPending        = 8
	MaxHello          = 4096
	MaxUID            = 128
	MaxPartial        = 4
	SendCap           = 8 << 20
	QueueMaxBytes     = 64 << 20
	QueueMaxEvents    = 100000
	Version           = "1.0.0"
	maxIncomingSCTP   = 262144
	maxBaseURL        = 3800
	peerPacketBacklog = 256
)

// EventKind is the kind of an Event (IWYK_EV_*).
type EventKind int32

// Event kinds.
const (
	EventOpened EventKind = 1
	EventText   EventKind = 2
	EventBinary EventKind = 3
	EventClosed EventKind = 4
	EventLog    EventKind = 5
)

// Event is one item of the server's event queue.
type Event struct {
	Kind       EventKind
	Session    int32
	TransferID int32  // EventBinary
	Code       int32  // EventClosed: Close* reason; EventLog: level
	Data       []byte // payload, owned by the receiver
}

// Errors returned by the Go API. Errors from Start wrap one of the first four.
var (
	ErrBind       = errors.New("cannot bind the UDP port")
	ErrCert       = errors.New("certificate error")
	ErrNoIP       = errors.New("no usable LAN IPv4")
	ErrArg        = errors.New("bad argument")
	ErrNoSession  = errors.New("no such session")
	ErrTooBig     = errors.New("payload over a size cap")
	ErrFull       = errors.New("session send queue full")
	ErrStopped    = errors.New("server stopped")
	ErrInternal   = errors.New("internal error")
	ErrProtocol   = errors.New("protocol error")
	ErrSmallBuf   = errors.New("buffer too small")
	ErrNotStarted = errors.New("not started")
)

// Code maps an error of this package to its IWYK_E_* value (0 for nil).
func Code(err error) int32 {
	switch {
	case err == nil:
		return OK
	case errors.Is(err, ErrNotStarted), errors.Is(err, ErrStopped):
		return ENotStarted
	case errors.Is(err, ErrArg):
		return EArg
	case errors.Is(err, ErrNoSession):
		return ENoSession
	case errors.Is(err, ErrSmallBuf):
		return ESmallBuf
	case errors.Is(err, ErrTooBig):
		return ETooBig
	case errors.Is(err, ErrBind):
		return EBind
	case errors.Is(err, ErrCert):
		return ECert
	case errors.Is(err, ErrNoIP):
		return ENoIP
	case errors.Is(err, ErrFull):
		return EFull
	}
	return EInternal
}
