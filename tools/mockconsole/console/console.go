// Package console emulates the game side of the phone protocol for testing
// the phone page: join and resource handshakes, scenario steps, validation of
// everything the page sends against protocol/messages.json, and an HTTP API.
// It talks to the network only through the Transport interface.
package console

import (
	"context"
	"crypto/rand"
	"errors"
	"fmt"
	"sort"
	"strings"
	"sync"
	"time"
)

// EventKind mirrors the native event kinds (contract 3.2).
type EventKind int32

// Event kinds.
const (
	EventOpened EventKind = 1
	EventText   EventKind = 2
	EventBinary EventKind = 3
	EventClosed EventKind = 4
	EventLog    EventKind = 5
)

// Event is one transport event.
type Event struct {
	Kind       EventKind
	Session    int32
	TransferID int32
	Code       int32
	Data       []byte
}

// Transport is what the console needs from the WebRTC server (satisfied by
// an adapter over iwyknet's server.Server, and by MemTransport in tests).
type Transport interface {
	Next(ctx context.Context) (Event, error)
	SendText(session int32, text []byte) error
	SendBinary(session, transferID int32, data []byte) error
	Close(session int32) error
	JoinURL(base string) (string, error)
}

// Options configures a Console.
type Options struct {
	Transport     Transport
	Catalogue     *Catalogue
	Scenario      *Scenario // nil: no steps, emulation only
	PageURL       string
	IP            string
	UDP           int
	Logf          func(format string, a ...any)
	HeaderTimeout time.Duration // header/binary pairing window, default 2 s
}

// Live session states.
const (
	stOpen     = "open"
	stHello    = "hello"
	stPending  = "pending"
	stJoined   = "joined"
	stAssigned = "assigned"
	stRefused  = "refused"
	stReplaced = "replaced"
	stClosing  = "closing"
	stClosed   = "closed"
)

type pendingHeader struct {
	msg map[string]any
	at  time.Time
}

type session struct {
	id      int32
	addr    string
	uid     string
	player  int
	state   string
	headers map[int32]*pendingHeader
	opened  time.Time
}

func (s *session) live() bool {
	return s.state != stClosed && s.state != stRefused && s.state != stReplaced && s.state != stClosing
}

type player struct {
	index   int
	uid     string
	pid     int
	slot    int
	session int32
}

// Upload is a completed content transfer from the page.
type Upload struct {
	TransferID int32
	Header     map[string]any
	Type       string
	ImgType    int
	GUID       string
	Size       int
	Width      int
	Height     int
	Errors     []string
}

// Item is one received thing (pings are not items).
type Item struct {
	Seq        int64          `json:"seq"`
	Session    int32          `json:"session"`
	Player     int            `json:"player"`
	Kind       string         `json:"kind"` // open, text, binary, closed
	Frame      any            `json:"frame,omitempty"`
	TransferID int32          `json:"transfer_id,omitempty"`
	Len        int            `json:"len,omitempty"`
	Valid      bool           `json:"valid"`
	Errors     []string       `json:"errors,omitempty"`
	T          string         `json:"-"`
	Type       string         `json:"-"`
	Msg        map[string]any `json:"-"`
	Upload     *Upload        `json:"-"`
	At         time.Time      `json:"-"`
}

// Summary renders an item in one line.
func (it *Item) Summary() string {
	switch {
	case it.Kind == "open":
		return "session opened"
	case it.Kind == "closed":
		return "session closed"
	case it.Upload != nil:
		return fmt.Sprintf("upload %s ImgType %d %dx%d %d bytes guid %s", it.Upload.Type, it.Upload.ImgType, it.Upload.Width, it.Upload.Height, it.Upload.Size, it.Upload.GUID)
	case it.Kind == "binary":
		return fmt.Sprintf("binary %d (%d bytes)", it.TransferID, it.Len)
	case it.Type != "":
		return Short(it.Msg, 200)
	}
	return Short(it.Frame, 200)
}

// TypeLabel names an item for result lists.
func (it *Item) TypeLabel() string {
	switch {
	case it.Type != "":
		return it.Type
	case it.Upload != nil:
		return "upload"
	case it.T != "":
		return it.T
	}
	return it.Kind
}

// Received is one entry of a step result's received list.
type Received struct {
	Type    string `json:"type"`
	AtMs    int64  `json:"at_ms"`
	Summary string `json:"summary"`
	Matched bool   `json:"matched"`
	Player  int    `json:"player"`
}

// StepResult is the outcome of one step.
type StepResult struct {
	Index    int        `json:"index"`
	ID       string     `json:"id"`
	Pass     bool       `json:"pass"`
	Done     bool       `json:"done"`
	State    string     `json:"state"`
	Errors   []string   `json:"errors"`
	Received []Received `json:"received"`
}

type stepRun struct {
	index     int
	step      *Step
	exps      []*Expect
	matched   []bool
	reasons   []string
	noneStart []time.Time
	strict    bool
	errors    []string
	received  []Received
	state     string
	start     time.Time
	ready     time.Time
	// pageActions: an external driver performs the step's page actions after
	// the sends, so none items are satisfied only none_ms after pageDone
	// (violations still count from activation; see StartOptions).
	pageActions bool
	pageDone    time.Time
	deadline    time.Time
	done        chan struct{}
	finished    bool
}

// Console is the mock game.
type Console struct {
	opt      Options
	cat      *Catalogue
	sc       *Scenario
	joinURL  string
	mu       sync.Mutex
	auto     AutoCfg
	sessions map[int32]*session
	players  []*player
	byUID    map[string]*player
	nextPID  int
	tid      int32
	guids    map[string]string // image key -> GUID
	images   map[string][]byte // GUID -> JPEG (scenario images and uploads)
	captures map[string]any
	items    []*Item
	seq      int64
	catErrs  []string
	step     *stepRun
	results  map[int]*StepResult
	hooks    []func(int)
	started  time.Time
	quit     chan struct{}
	quitOnce sync.Once
}

// ErrBusy is returned when a step is started while another one runs.
var ErrBusy = errors.New("another step is running")

// New creates a console.
func New(opt Options) (*Console, error) {
	if opt.Transport == nil || opt.Catalogue == nil {
		return nil, errors.New("console: transport and catalogue are required")
	}
	if opt.HeaderTimeout == 0 {
		opt.HeaderTimeout = 2 * time.Second
	}
	if opt.Logf == nil {
		opt.Logf = func(string, ...any) {}
	}
	sc := opt.Scenario
	if sc == nil {
		sc = &Scenario{Schema: 1, Name: "", Players: 1}
	}
	c := &Console{
		opt: opt, cat: opt.Catalogue, sc: sc,
		auto:     DefaultAuto().Apply(sc.Auto),
		sessions: map[int32]*session{}, byUID: map[string]*player{},
		nextPID: 2, guids: map[string]string{}, images: map[string][]byte{},
		captures: map[string]any{}, results: map[int]*StepResult{},
		started: time.Now(), quit: make(chan struct{}),
	}
	keys := make([]string, 0, len(sc.Images))
	for k := range sc.Images {
		keys = append(keys, k)
	}
	sort.Strings(keys)
	for _, k := range keys {
		spec := sc.Images[k]
		b, err := SynthJPEG(spec)
		if err != nil {
			return nil, fmt.Errorf("image %s: %w", k, err)
		}
		g := NewGUID()
		c.guids[k] = g
		c.images[g] = b
	}
	if opt.PageURL != "" {
		u, err := opt.Transport.JoinURL(opt.PageURL)
		if err != nil {
			return nil, fmt.Errorf("join url: %w", err)
		}
		c.joinURL = u
	}
	return c, nil
}

// NewGUID returns a random version 4 UUID string.
func NewGUID() string {
	var b [16]byte
	if _, err := rand.Read(b[:]); err != nil {
		panic(err)
	}
	b[6] = b[6]&0x0f | 0x40
	b[8] = b[8]&0x3f | 0x80
	return fmt.Sprintf("%x-%x-%x-%x-%x", b[0:4], b[4:6], b[6:8], b[8:10], b[10:16])
}

// JoinURL returns the join URL (with the QR fragment).
func (c *Console) JoinURL() string { return c.joinURL }

// Scenario returns the loaded scenario (never nil).
func (c *Console) Scenario() *Scenario { return c.sc }

// Catalogue returns the catalogue.
func (c *Console) Catalogue() *Catalogue { return c.cat }

// Quit returns a channel closed by RequestQuit.
func (c *Console) Quit() <-chan struct{} { return c.quit }

// RequestQuit asks the program to exit.
func (c *Console) RequestQuit() { c.quitOnce.Do(func() { close(c.quit) }) }

// OnStepReady registers a hook called (in its own goroutine) after a step's
// sends went out and the console started waiting for its expectations.
func (c *Console) OnStepReady(fn func(index int)) {
	c.mu.Lock()
	c.hooks = append(c.hooks, fn)
	c.mu.Unlock()
}

// Run processes transport events until ctx ends or the transport fails.
func (c *Console) Run(ctx context.Context) error {
	tick := time.NewTicker(50 * time.Millisecond)
	defer tick.Stop()
	go func() {
		for {
			select {
			case <-ctx.Done():
				return
			case <-tick.C:
				c.mu.Lock()
				c.checkHeadersLocked(time.Now())
				c.mu.Unlock()
			}
		}
	}()
	for {
		ev, err := c.opt.Transport.Next(ctx)
		if err != nil {
			if ctx.Err() != nil {
				return nil
			}
			return err
		}
		c.handle(ev)
	}
}

func (c *Console) handle(ev Event) {
	c.mu.Lock()
	defer c.mu.Unlock()
	now := time.Now()
	switch ev.Kind {
	case EventLog:
		c.opt.Logf("native: %s", ev.Data)
	case EventOpened:
		c.sessions[ev.Session] = &session{id: ev.Session, addr: string(ev.Data), player: -1, state: stOpen, headers: map[int32]*pendingHeader{}, opened: now}
		c.addItemLocked(&Item{Session: ev.Session, Player: -1, Kind: "open", Frame: string(ev.Data), Valid: true, At: now})
	case EventText:
		c.handleTextLocked(ev, now)
	case EventBinary:
		c.handleBinaryLocked(ev, now)
	case EventClosed:
		s := c.sessions[ev.Session]
		p := -1
		if s != nil {
			s.state = stClosed
			p = s.player
		}
		c.addItemLocked(&Item{Session: ev.Session, Player: p, Kind: "closed", Frame: map[string]any{"code": ev.Code, "detail": string(ev.Data)}, Valid: true, At: now})
	}
}

func (c *Console) handleTextLocked(ev Event, now time.Time) {
	s := c.sessions[ev.Session]
	if s == nil {
		s = &session{id: ev.Session, player: -1, state: stOpen, headers: map[int32]*pendingHeader{}, opened: now}
		c.sessions[ev.Session] = s
	}
	it := &Item{Session: ev.Session, Player: s.player, Kind: "text", Valid: true, At: now}
	v, err := Decode(ev.Data)
	if err != nil {
		it.Frame = string(ev.Data)
		c.invalidLocked(it, "text frame is not JSON: "+err.Error())
		c.addItemLocked(it)
		return
	}
	it.Frame = v
	fr, ok := v.(map[string]any)
	if !ok {
		c.invalidLocked(it, "text frame is not an object")
		c.addItemLocked(it)
		return
	}
	t, _ := fr["t"].(string)
	it.T = t
	if len(ev.Data) > 65536 {
		c.invalidLocked(it, "text frame over 65536 bytes")
	}
	switch t {
	case "ping":
		return
	case "hello":
		uid, _ := fr["uid"].(string)
		if s.uid != "" {
			c.invalidLocked(it, "second hello on one session")
		} else if uid == "" {
			c.invalidLocked(it, "hello without uid")
		} else {
			c.helloLocked(s, uid, now)
			it.Player = s.player
		}
		c.addItemLocked(it)
	case "msg":
		if s.uid == "" {
			c.invalidLocked(it, "msg before hello")
		}
		id, _ := AsInt(fr["id"])
		if id != int64(c.cat.JSONTransferID) {
			c.invalidLocked(it, fmt.Sprintf("msg id %v, want %d", fr["id"], c.cat.JSONTransferID))
			c.addItemLocked(it)
			return
		}
		ts, errs := c.cat.Validate("p2g", fr["json"])
		it.Type = ts
		it.Msg, _ = fr["json"].(map[string]any)
		for _, e := range errs {
			c.invalidLocked(it, e)
		}
		if it.Valid {
			c.onMessageLocked(s, it, now)
		}
		c.addItemLocked(it)
	default:
		c.invalidLocked(it, fmt.Sprintf("unexpected frame t=%q from the page", t))
		c.addItemLocked(it)
	}
}

func (c *Console) invalidLocked(it *Item, e string) {
	it.Valid = false
	it.Errors = append(it.Errors, e)
	msg := fmt.Sprintf("session %d: %s", it.Session, e)
	c.catErrs = append(c.catErrs, msg)
	if c.step != nil && !c.step.finished {
		c.step.errors = append(c.step.errors, msg)
	}
	c.opt.Logf("invalid: %s", msg)
}

func (c *Console) helloLocked(s *session, uid string, now time.Time) {
	s.uid = uid
	s.state = stHello
	p := c.byUID[uid]
	if p == nil {
		p = &player{index: len(c.players), uid: uid, slot: -1, session: s.id}
		c.players = append(c.players, p)
		c.byUID[uid] = p
	}
	s.player = p.index
	if old := c.sessions[p.session]; old != nil && old.id != s.id && old.live() {
		c.sendFrameLocked(old.id, NewObj().Set("t", "bye").Set("reason", "replaced"))
		old.state = stReplaced
		_ = c.opt.Transport.Close(old.id)
	}
	p.session = s.id
	if !c.auto.Join {
		return
	}
	if c.auto.Refuse != 0 {
		c.refuseLocked(s, c.auto.Refuse)
		return
	}
	if p.slot < 0 {
		taken := 0
		for _, q := range c.players {
			if q != p && q.slot >= 0 {
				taken++
			}
		}
		if taken >= 6 {
			c.refuseLocked(s, 2)
			return
		}
	}
	if c.auto.PendingMs > 0 {
		s.state = stPending
		c.sendFrameLocked(s.id, NewObj().Set("t", "pending"))
		id := s.id
		time.AfterFunc(time.Duration(c.auto.PendingMs)*time.Millisecond, func() {
			c.mu.Lock()
			defer c.mu.Unlock()
			if ss := c.sessions[id]; ss != nil && ss.state == stPending {
				c.acceptLocked(ss)
			}
		})
		return
	}
	c.acceptLocked(s)
}

func (c *Console) refuseLocked(s *session, reason int) {
	c.sendFrameLocked(s.id, NewObj().Set("t", "refused").Set("reason", reason))
	s.state = stRefused
	_ = c.opt.Transport.Close(s.id)
}

func (c *Console) acceptLocked(s *session) {
	s.state = stJoined
	c.sendFrameLocked(s.id, NewObj().Set("t", "joined"))
	c.sendMsgLocked(s.id, "InterfaceVersionMessage", map[string]any{"InterfaceVersion": c.cat.InterfaceVersion})
	c.sendMsgLocked(s.id, "SessionStateMessage", map[string]any{"SessionID": c.auto.SessionID})
}

func (c *Console) onMessageLocked(s *session, it *Item, now time.Time) {
	def, _ := c.cat.Message(it.Type)
	if def != nil && def.Content {
		tid, _ := AsInt(it.Msg["TransferID"])
		if _, dup := s.headers[int32(tid)]; dup {
			c.invalidLocked(it, fmt.Sprintf("second header for transfer %d", tid))
		}
		s.headers[int32(tid)] = &pendingHeader{msg: it.Msg, at: now}
	}
	switch it.Type {
	case "ClientRequestPlayerIDMessage":
		if uid, _ := it.Msg["UID"].(string); uid != s.uid {
			c.invalidLocked(it, fmt.Sprintf("UID %q differs from the hello uid %q", uid, s.uid))
		}
		if !c.auto.Join || s.player < 0 {
			return
		}
		if s.state != stJoined && s.state != stAssigned {
			c.invalidLocked(it, "ClientRequestPlayerIDMessage before joined")
			return
		}
		p := c.players[s.player]
		if p.pid == 0 {
			p.pid = c.nextPID
			c.nextPID++
		}
		if p.slot < 0 {
			used := map[int]bool{}
			for _, q := range c.players {
				if q.slot >= 0 {
					used[q.slot] = true
				}
			}
			for i := 0; i < 6; i++ {
				if !used[i] {
					p.slot = i
					break
				}
			}
			if p.slot < 0 {
				c.sendMsgLocked(s.id, "DisconnectCommandMessage", map[string]any{"Msg": 2})
				return
			}
		}
		s.state = stAssigned
		c.sendMsgLocked(s.id, "AssignPlayerIDAndSlotMessage", map[string]any{"PlayerID": p.pid, "SlotID": p.slot, "UDPPortOffset": 0, "DisplayName": "", "PSNID": ""})
		c.sendMsgLocked(s.id, "SyncJokersQuizStateMessage", map[string]any{"NumJokers": c.auto.Jokers})
		c.sendMsgLocked(s.id, "ClientOnlineStateMessage", map[string]any{"IsOnline": false})
		c.sendMsgLocked(s.id, "ClientUGCStateMessage", map[string]any{"ugcState": 0})
		c.sendMsgLocked(s.id, "ResourceRequirementsMessage", map[string]any{"Requirements": []any{}})
	case "RequestResourceMessage":
		if !c.auto.Resources {
			return
		}
		g, _ := it.Msg["ResourceGUID"].(string)
		data, ok := c.images[g]
		if !ok {
			c.opt.Logf("session %d requested unknown resource %s", s.id, g)
			return
		}
		c.sendContentLocked(s.id, "ImageResourceContentTransferMessage", map[string]any{"ImageGUID": g, "Acknowledge": true}, data)
	}
}

func (c *Console) handleBinaryLocked(ev Event, now time.Time) {
	s := c.sessions[ev.Session]
	it := &Item{Session: ev.Session, Player: -1, Kind: "binary", TransferID: ev.TransferID, Len: len(ev.Data), Valid: true, At: now}
	if s == nil {
		c.invalidLocked(it, "binary on an unknown session")
		c.addItemLocked(it)
		return
	}
	it.Player = s.player
	h := s.headers[ev.TransferID]
	if h == nil {
		c.invalidLocked(it, fmt.Sprintf("binary transfer %d without a header", ev.TransferID))
		c.addItemLocked(it)
		return
	}
	delete(s.headers, ev.TransferID)
	if now.Sub(h.at) > c.opt.HeaderTimeout {
		c.invalidLocked(it, fmt.Sprintf("binary transfer %d arrived %v after its header", ev.TransferID, now.Sub(h.at).Round(time.Millisecond)))
	}
	ts, _ := h.msg["TypeString"].(string)
	up := &Upload{TransferID: ev.TransferID, Header: h.msg, Type: ts, Size: len(ev.Data)}
	if it2, ok := AsInt(h.msg["ImgType"]); ok {
		up.ImgType = int(it2)
	}
	up.GUID, _ = h.msg["ImageGUID"].(string)
	if len(ev.Data) > 131072 {
		up.Errors = append(up.Errors, fmt.Sprintf("content of %d bytes over the 131072 cap", len(ev.Data)))
	} else if len(ev.Data) > 128000 && ts == "ClientImageResourceContentTransferMessage" {
		up.Errors = append(up.Errors, fmt.Sprintf("upload of %d bytes over the page cap 128000", len(ev.Data)))
	}
	if ts == "ClientImageResourceContentTransferMessage" || ts == "ClientImageUGCContentMessage" {
		info, err := CheckJPEG(ev.Data)
		if err != nil {
			up.Errors = append(up.Errors, err.Error())
		} else {
			up.Width, up.Height = info.Width, info.Height
			want := map[int][2]int{1: {384, 512}, 2: {480, 640}, 3: {480, 640}, 4: {480, 640}}[up.ImgType]
			if want[0] != 0 && (up.Width != want[0] || up.Height != want[1]) {
				up.Errors = append(up.Errors, fmt.Sprintf("ImgType %d image is %dx%d, want %dx%d", up.ImgType, up.Width, up.Height, want[0], want[1]))
			}
			if up.ImgType == 5 && !((up.Width == 480 && up.Height == 640) || (up.Width == 384 && up.Height == 512)) {
				up.Errors = append(up.Errors, fmt.Sprintf("ImgType 5 image is %dx%d, want 480x640 or 384x512", up.Width, up.Height))
			}
			if up.GUID != "" {
				c.images[up.GUID] = append([]byte(nil), ev.Data...)
			}
		}
	}
	it.Upload = up
	for _, e := range up.Errors {
		c.invalidLocked(it, e)
	}
	c.addItemLocked(it)
}

func (c *Console) checkHeadersLocked(now time.Time) {
	for _, s := range c.sessions {
		for tid, h := range s.headers {
			if now.Sub(h.at) > c.opt.HeaderTimeout {
				delete(s.headers, tid)
				it := &Item{Session: s.id, Player: s.player, Kind: "text", Valid: true, At: now, Frame: h.msg}
				c.invalidLocked(it, fmt.Sprintf("header of transfer %d without binary within %v", tid, c.opt.HeaderTimeout))
			}
		}
	}
	if c.step != nil && !c.step.finished {
		c.evaluateLocked(nil, now)
	}
}

func (c *Console) addItemLocked(it *Item) {
	c.seq++
	it.Seq = c.seq
	c.items = append(c.items, it)
	if len(c.items) > 20000 {
		c.items = c.items[len(c.items)-10000:]
	}
	if c.step != nil && !c.step.finished && !it.At.Before(c.step.start) {
		c.evaluateLocked(it, it.At)
	}
}

// ---- sending ----

func (c *Console) nextTIDLocked() int32 {
	c.tid++
	if c.tid <= 0 {
		c.tid = 1
	}
	return c.tid
}

func (c *Console) sendFrameLocked(sid int32, frame any) error {
	return c.opt.Transport.SendText(sid, Marshal(frame))
}

func (c *Console) sendMsgObjLocked(sid int32, msg any) error {
	f := NewObj().Set("t", "msg").Set("id", c.cat.JSONTransferID).Set("json", msg)
	return c.sendFrameLocked(sid, f)
}

func (c *Console) sendMsgLocked(sid int32, name string, vals map[string]any) error {
	o, err := c.cat.Build(name, vals, true)
	if err != nil {
		return err
	}
	return c.sendMsgObjLocked(sid, o)
}

func (c *Console) sendContentLocked(sid int32, name string, vals map[string]any, data []byte) error {
	tid := c.nextTIDLocked()
	v := map[string]any{"TransferID": tid}
	for k, x := range vals {
		v[k] = x
	}
	if err := c.sendMsgLocked(sid, name, v); err != nil {
		return err
	}
	return c.opt.Transport.SendBinary(sid, tid, data)
}

// placeholder source over the live state
func (c *Console) sourceLocked() PlaceholderSource {
	return func(kind, arg string) (any, error) {
		switch kind {
		case "guid":
			g, ok := c.guids[arg]
			if !ok {
				return nil, fmt.Errorf("$guid:%s: unknown image", arg)
			}
			return g, nil
		case "uid", "pid", "slot":
			n, err := PlayerArg(arg)
			if err != nil {
				return nil, err
			}
			if n >= len(c.players) {
				return nil, fmt.Errorf("$%s:%s: player not connected yet", kind, arg)
			}
			p := c.players[n]
			switch kind {
			case "uid":
				return p.uid, nil
			case "pid":
				if p.pid == 0 {
					return nil, fmt.Errorf("$pid:%s: no PlayerID yet", arg)
				}
				return Normalize(p.pid), nil
			}
			if p.slot < 0 {
				return nil, fmt.Errorf("$slot:%s: no slot yet", arg)
			}
			return Normalize(p.slot), nil
		case "tid":
			return Normalize(int(c.nextTIDLocked())), nil
		case "cap":
			v, ok := c.captures[arg]
			if !ok {
				return nil, fmt.Errorf("$cap:%s: not captured yet", arg)
			}
			return v, nil
		}
		return nil, fmt.Errorf("unknown placeholder $%s", kind)
	}
}

// Resolve substitutes placeholders with the live values.
func (c *Console) Resolve(v any) (any, error) {
	c.mu.Lock()
	defer c.mu.Unlock()
	return Substitute(v, c.sourceLocked())
}

func (c *Console) liveSessionLocked(p int) (int32, error) {
	if p < 0 || p >= len(c.players) {
		return 0, fmt.Errorf("player %d has not connected", p)
	}
	s := c.sessions[c.players[p].session]
	if s == nil || !s.live() {
		return 0, fmt.Errorf("player %d has no live session", p)
	}
	return s.id, nil
}

// SendItem executes one send item for player p.
func (c *Console) SendItem(p int, it *SendItem) error {
	if it.Kind == "wait" {
		time.Sleep(time.Duration(it.WaitMs) * time.Millisecond)
		return nil
	}
	if it.Player != nil {
		p = *it.Player
	}
	c.mu.Lock()
	defer c.mu.Unlock()
	sid, err := c.liveSessionLocked(p)
	if err != nil {
		return err
	}
	src := c.sourceLocked()
	switch it.Kind {
	case "msg":
		v, err := Substitute(it.Msg, src)
		if err != nil {
			return err
		}
		vm := v.(map[string]any)
		if it.Raw {
			return c.sendMsgObjLocked(sid, vm)
		}
		name, _ := vm["TypeString"].(string)
		delete(vm, "TypeString")
		o, err := c.cat.Build(name, vm, true)
		if err != nil {
			return err
		}
		if _, errs := c.cat.Validate("g2p", o); len(errs) > 0 {
			c.opt.Logf("warning: sending invalid %s: %s", name, joinErrs(errs))
		}
		return c.sendMsgObjLocked(sid, o)
	case "content":
		v, err := Substitute(it.Header, src)
		if err != nil {
			return err
		}
		vm := v.(map[string]any)
		name, _ := vm["TypeString"].(string)
		delete(vm, "TypeString")
		tid64, ok := AsInt(vm["TransferID"])
		if !ok {
			tid64 = int64(c.nextTIDLocked())
			vm["TransferID"] = tid64
		}
		g, ok := c.guids[it.Image]
		if !ok {
			return fmt.Errorf("unknown image %s", it.Image)
		}
		o, err := c.cat.Build(name, vm, true)
		if err != nil {
			return err
		}
		if err := c.sendMsgObjLocked(sid, o); err != nil {
			return err
		}
		return c.opt.Transport.SendBinary(sid, int32(tid64), c.images[g])
	case "resource":
		g, ok := c.guids[it.Resource]
		if !ok {
			return fmt.Errorf("unknown image %s", it.Resource)
		}
		return c.sendContentLocked(sid, "ImageResourceContentTransferMessage", map[string]any{"ImageGUID": g, "Acknowledge": true}, c.images[g])
	case "manifest":
		reqs := []any{}
		for _, e := range it.Manifest {
			var g any
			if k, ok := e["image"].(string); ok {
				gg, ok := c.guids[k]
				if !ok {
					return fmt.Errorf("unknown image %s", k)
				}
				g = gg
			} else {
				g, err = Substitute(e["guid"], src)
				if err != nil {
					return err
				}
			}
			r := e["r"]
			if r == nil {
				r = Normalize(1)
			}
			reqs = append(reqs, NewObj().Set("GUID", g).Set("Rqrmnt", r))
		}
		return c.sendMsgLocked(sid, "ResourceRequirementsMessage", map[string]any{"Requirements": reqs})
	case "frame":
		v, err := Substitute(it.Frame, src)
		if err != nil {
			return err
		}
		return c.sendFrameLocked(sid, v)
	case "close":
		if s := c.sessions[sid]; s != nil {
			s.state = stClosing
		}
		return c.opt.Transport.Close(sid)
	}
	return fmt.Errorf("unknown send kind %s", it.Kind)
}

// SendRaw executes an ad-hoc send item (HTTP API).
func (c *Console) SendRaw(p int, m map[string]any) error {
	it, err := ParseSend(m)
	if err != nil {
		return err
	}
	return c.SendItem(p, it)
}

// ---- steps ----

// StartOptions changes how a step is run.
type StartOptions struct {
	// PageActions tells the console that a driver (the e2e harness) performs
	// the step's page actions after the sends. A none item still counts
	// violations from the moment it becomes active (as without the flag), but
	// it is satisfied only none_ms after PageDone. When PageDone never comes
	// the step fails at its timeout (a crashed driver never yields a pass).
	PageActions bool
}

// StartStep sends step i and starts waiting for its expectations.
func (c *Console) StartStep(i int) error { return c.StartStepWith(i, StartOptions{}) }

// StartStepWith is StartStep with options.
func (c *Console) StartStepWith(i int, opt StartOptions) error {
	if i < 0 || i >= len(c.sc.Steps) {
		return fmt.Errorf("no step %d", i)
	}
	st := &c.sc.Steps[i]
	run := &stepRun{index: i, step: st, strict: st.Order != "any", state: "sent", done: make(chan struct{}), pageActions: opt.PageActions}
	for _, m := range st.Expect {
		e, err := ParseExpect(m)
		if err != nil {
			return fmt.Errorf("step %s: %v", st.ID, err)
		}
		run.exps = append(run.exps, e)
	}
	run.matched = make([]bool, len(run.exps))
	run.reasons = make([]string, len(run.exps))
	run.noneStart = make([]time.Time, len(run.exps))
	c.mu.Lock()
	if c.step != nil && !c.step.finished {
		c.mu.Unlock()
		return ErrBusy
	}
	c.auto = c.auto.Apply(st.Auto)
	run.start = time.Now()
	c.step = run
	c.results[i] = &StepResult{Index: i, ID: st.ID, State: "sent"}
	c.mu.Unlock()
	go c.runStep(run)
	return nil
}

// ErrNotRunning is returned by PageDone for a step that is not the current one.
var ErrNotRunning = errors.New("step is not the current step")

// PageDone marks the end of the page actions of step i (started with
// PageActions): its none_ms success clocks start now. It returns whether the step
// was still running; calling it for a finished current step is not an error.
func (c *Console) PageDone(i int) (bool, error) {
	if i < 0 || i >= len(c.sc.Steps) {
		return false, fmt.Errorf("no step %d", i)
	}
	c.mu.Lock()
	defer c.mu.Unlock()
	run := c.step
	if run == nil || run.index != i {
		return false, ErrNotRunning
	}
	if run.finished {
		return false, nil
	}
	if run.pageActions && run.pageDone.IsZero() {
		now := time.Now()
		run.pageDone = now
		c.extendForNoneLocked(run, now)
		c.evaluateLocked(nil, now)
	}
	return !run.finished, nil
}

// noneDone reports whether a none item active since start has been quiet
// long enough at now, and when its window ended. With PageActions the window
// only counts from PageDone (violations still count from start).
func noneDone(run *stepRun, start time.Time, ms int, now time.Time) (time.Time, bool) {
	if start.IsZero() {
		return time.Time{}, false
	}
	b := start
	if run.pageActions {
		if run.pageDone.IsZero() {
			return time.Time{}, false
		}
		if run.pageDone.After(b) {
			b = run.pageDone
		}
	}
	end := b.Add(time.Duration(ms) * time.Millisecond)
	return end, !now.Before(end)
}

// extendForNoneLocked moves the deadline so that the unmatched none_ms
// windows starting at from still fit.
func (c *Console) extendForNoneLocked(run *stepRun, from time.Time) {
	if run.ready.IsZero() {
		return
	}
	var total time.Duration
	for k, e := range run.exps {
		if !run.matched[k] && e.Kind == "none" {
			total += time.Duration(e.NoneMs) * time.Millisecond
		}
	}
	if total == 0 {
		return
	}
	if d := from.Add(total + 500*time.Millisecond); d.After(run.deadline) {
		run.deadline = d
	}
}

func (c *Console) runStep(run *stepRun) {
	var sendErrs []string
	for j, m := range run.step.Send {
		it, err := ParseSend(m)
		if err == nil {
			err = c.SendItem(run.step.Player, it)
		}
		if err != nil {
			sendErrs = append(sendErrs, fmt.Sprintf("send %d: %v", j, err))
		}
	}
	c.mu.Lock()
	run.errors = append(run.errors, sendErrs...)
	run.state = "waiting"
	run.ready = time.Now()
	run.deadline = run.ready.Add(time.Duration(run.step.Timeout()) * time.Millisecond)
	if run.pageActions && !run.pageDone.IsZero() {
		c.extendForNoneLocked(run, run.ready)
	}
	c.results[run.index].State = "waiting"
	hooks := append([]func(int){}, c.hooks...)
	c.evaluateLocked(nil, run.ready)
	c.mu.Unlock()
	for _, h := range hooks {
		go h(run.index)
	}
	t := time.NewTicker(20 * time.Millisecond)
	defer t.Stop()
	for {
		select {
		case <-run.done:
			return
		case now := <-t.C:
			c.mu.Lock()
			c.evaluateLocked(nil, now)
			c.mu.Unlock()
		}
	}
}

func (c *Console) expPlayer(run *stepRun, e *Expect) int {
	if e.Player != nil {
		return *e.Player
	}
	return run.step.Player
}

// matchLocked tries an expectation against an item: ("", true) on a match.
func (c *Console) matchLocked(run *stepRun, e *Expect, it *Item) (string, bool) {
	if it.Player != c.expPlayer(run, e) {
		return "", false
	}
	switch e.Kind {
	case "hello":
		if it.T != "hello" {
			return "", false
		}
		if e.UID != nil {
			want, err := Substitute(e.UID, c.sourceLocked())
			if err != nil {
				return err.Error(), false
			}
			fr, _ := it.Frame.(map[string]any)
			if r := Match(want, fr["uid"]); r != "" {
				return "hello uid: " + r, false
			}
		}
		return "", true
	case "msg":
		if it.Type != e.Msg || !it.Valid && it.Msg == nil {
			return "", false
		}
		keys := make([]string, 0, len(e.Fields))
		for k := range e.Fields {
			keys = append(keys, k)
		}
		sort.Strings(keys)
		for _, k := range keys {
			want, err := Substitute(e.Fields[k], c.sourceLocked())
			if err != nil {
				return k + ": " + err.Error(), false
			}
			got, ok := it.Msg[k]
			if !ok {
				return k + ": missing", false
			}
			if r := Match(want, got); r != "" {
				return k + ": " + r, false
			}
		}
		return "", true
	case "upload":
		if it.Upload == nil || it.Upload.Type != "ClientImageResourceContentTransferMessage" {
			return "", false
		}
		if it.Upload.ImgType != e.ImgType {
			return fmt.Sprintf("upload ImgType %d", it.Upload.ImgType), false
		}
		return "", true
	case "closed":
		return "", it.Kind == "closed"
	}
	return "", false
}

func (c *Console) onMatchLocked(run *stepRun, e *Expect, it *Item) {
	switch e.Kind {
	case "msg":
		for name, field := range e.Capture {
			c.captures[name] = it.Msg[field]
		}
	case "upload":
		up := it.Upload
		if e.Width > 0 && (up.Width != e.Width || up.Height != e.Height) {
			run.errors = append(run.errors, fmt.Sprintf("upload ImgType %d: %dx%d, want %dx%d", up.ImgType, up.Width, up.Height, e.Width, e.Height))
		}
		if up.Size > e.MaxBytes {
			run.errors = append(run.errors, fmt.Sprintf("upload ImgType %d: %d bytes over maxBytes %d", up.ImgType, up.Size, e.MaxBytes))
		}
		if e.CaptureName != "" {
			c.captures[e.CaptureName] = up.GUID
		}
	}
}

func isActivity(it *Item) bool { return it.Kind != "open" }

// evaluateLocked feeds a new item (or nil for a clock tick) to the running step.
func (c *Console) evaluateLocked(it *Item, now time.Time) {
	run := c.step
	if run == nil || run.finished {
		return
	}
	matchedItem := false
	if it != nil {
		if run.strict {
			k := firstUnmatched(run)
			if k >= 0 {
				e := run.exps[k]
				if e.Kind == "none" {
					if isActivity(it) && it.Player == c.expPlayer(run, e) && !run.noneStart[k].IsZero() {
						run.errors = append(run.errors, fmt.Sprintf("expected nothing for %d ms, got %s", e.NoneMs, it.Summary()))
						run.matched[k] = true
						c.startNoneLocked(run, now)
					}
				} else if r, ok := c.matchLocked(run, e, it); ok {
					run.matched[k] = true
					matchedItem = true
					c.onMatchLocked(run, e, it)
					c.startNoneLocked(run, now)
				} else if r != "" {
					run.reasons[k] = r
				}
			}
		} else {
			for k, e := range run.exps {
				if run.matched[k] || e.Kind == "none" {
					continue
				}
				if r, ok := c.matchLocked(run, e, it); ok {
					run.matched[k] = true
					matchedItem = true
					c.onMatchLocked(run, e, it)
					break
				} else if r != "" {
					run.reasons[k] = r
				}
			}
			if !matchedItem && isActivity(it) {
				for k, e := range run.exps {
					if !run.matched[k] && e.Kind == "none" && it.Player == c.expPlayer(run, e) && !run.ready.IsZero() {
						run.errors = append(run.errors, fmt.Sprintf("expected nothing for %d ms, got %s", e.NoneMs, it.Summary()))
						run.matched[k] = true
					}
				}
			}
		}
		run.received = append(run.received, Received{Type: it.TypeLabel(), AtMs: it.At.Sub(run.start).Milliseconds(), Summary: it.Summary(), Matched: matchedItem, Player: it.Player})
	}
	if run.ready.IsZero() {
		return
	}
	// clock: none_ms windows
	if run.strict {
		c.startNoneLocked(run, run.ready)
		for {
			k := firstUnmatched(run)
			if k < 0 || run.exps[k].Kind != "none" {
				break
			}
			end, ok := noneDone(run, run.noneStart[k], run.exps[k].NoneMs, now)
			if !ok {
				break
			}
			run.matched[k] = true
			c.startNoneLocked(run, end)
		}
	} else {
		for k, e := range run.exps {
			if _, ok := noneDone(run, run.ready, e.NoneMs, now); !run.matched[k] && e.Kind == "none" && ok {
				run.matched[k] = true
			}
		}
	}
	if firstUnmatched(run) < 0 {
		c.finishLocked(run, nil)
		return
	}
	if now.After(run.deadline) {
		var errs []string
		if run.pageActions && run.pageDone.IsZero() {
			// a driver that never reported page-done must not yield a pass
			errs = append(errs, fmt.Sprintf("timeout after %d ms: page-done never arrived (page actions not finished)", run.step.Timeout()))
		}
		for k, e := range run.exps {
			if !run.matched[k] {
				msg := fmt.Sprintf("timeout after %d ms waiting for %s", run.step.Timeout(), e.Describe())
				if run.reasons[k] != "" {
					msg += " (last mismatch: " + run.reasons[k] + ")"
				}
				errs = append(errs, msg)
			}
		}
		c.finishLocked(run, errs)
	}
}

// startNoneLocked activates the first unmatched item if it is a none item:
// from at on, activity of its player is a violation.
func (c *Console) startNoneLocked(run *stepRun, at time.Time) {
	if run.ready.IsZero() {
		return
	}
	k := firstUnmatched(run)
	if k >= 0 && run.exps[k].Kind == "none" && run.noneStart[k].IsZero() {
		if at.Before(run.ready) {
			at = run.ready
		}
		run.noneStart[k] = at
	}
}

func firstUnmatched(run *stepRun) int {
	for k, m := range run.matched {
		if !m {
			return k
		}
	}
	return -1
}

func (c *Console) finishLocked(run *stepRun, errs []string) {
	run.errors = append(run.errors, errs...)
	run.finished = true
	pass := len(run.errors) == 0
	run.state = "failed"
	if pass {
		run.state = "passed"
	}
	r := c.results[run.index]
	r.Pass, r.Done, r.State = pass, true, run.state
	r.Errors = append([]string{}, run.errors...)
	r.Received = append([]Received{}, run.received...)
	c.opt.Logf("step %d %s: %s %s", run.index, run.step.ID, run.state, strings.Join(run.errors, "; "))
	close(run.done)
}

// Result waits up to timeout for step i and returns its result.
func (c *Console) Result(i int, timeout time.Duration) (StepResult, error) {
	c.mu.Lock()
	r, ok := c.results[i]
	var done chan struct{}
	if c.step != nil && c.step.index == i {
		done = c.step.done
	}
	c.mu.Unlock()
	if !ok {
		if i < 0 || i >= len(c.sc.Steps) {
			return StepResult{}, fmt.Errorf("no step %d", i)
		}
		return StepResult{Index: i, ID: c.sc.Steps[i].ID, State: "idle", Errors: []string{}, Received: []Received{}}, nil
	}
	if done != nil {
		select {
		case <-done:
		case <-time.After(timeout):
		}
	}
	c.mu.Lock()
	defer c.mu.Unlock()
	out := *r
	if !out.Done && c.step != nil && c.step.index == i {
		out.Errors = append([]string{}, c.step.errors...)
		out.Received = append([]Received{}, c.step.received...)
	}
	if out.Errors == nil {
		out.Errors = []string{}
	}
	if out.Received == nil {
		out.Received = []Received{}
	}
	return out, nil
}

// RunStep starts step i and waits for its result.
func (c *Console) RunStep(i int) (StepResult, error) {
	if err := c.StartStep(i); err != nil {
		return StepResult{}, err
	}
	st := c.sc.Steps[i]
	return c.Result(i, time.Duration(st.Timeout())*time.Millisecond+time.Duration(sendTime(&st))+5*time.Second)
}

func sendTime(st *Step) time.Duration {
	var d time.Duration
	for _, m := range st.Send {
		if n, ok := intOf(m["wait_ms"]); ok {
			d += time.Duration(n) * time.Millisecond
		}
	}
	return d
}

// Report is the -report output.
type Report struct {
	Scenario        string       `json:"scenario"`
	Pass            bool         `json:"pass"`
	Steps           []StepResult `json:"steps"`
	CatalogueErrors []string     `json:"catalogue_errors"`
}

// RunAll runs every step in order (the -auto mode).
func (c *Console) RunAll(ctx context.Context) Report {
	for i := range c.sc.Steps {
		if ctx.Err() != nil {
			break
		}
		if _, err := c.RunStep(i); err != nil {
			c.mu.Lock()
			c.results[i] = &StepResult{Index: i, ID: c.sc.Steps[i].ID, Done: true, State: "failed", Errors: []string{err.Error()}}
			c.mu.Unlock()
		}
	}
	return c.Report()
}

// Report summarises all steps run so far.
func (c *Console) Report() Report {
	c.mu.Lock()
	defer c.mu.Unlock()
	rep := Report{Scenario: c.sc.Name, Pass: true, Steps: []StepResult{}, CatalogueErrors: append([]string{}, c.catErrs...)}
	for i, st := range c.sc.Steps {
		r, ok := c.results[i]
		if !ok {
			rep.Pass = false
			rep.Steps = append(rep.Steps, StepResult{Index: i, ID: st.ID, State: "idle", Errors: []string{"not run"}, Received: []Received{}})
			continue
		}
		out := *r
		if out.Errors == nil {
			out.Errors = []string{}
		}
		if out.Received == nil {
			out.Received = []Received{}
		}
		if !out.Pass {
			rep.Pass = false
		}
		rep.Steps = append(rep.Steps, out)
	}
	if len(rep.CatalogueErrors) > 0 {
		rep.Pass = false
	}
	return rep
}

// SessionView is one entry of /api/state.
type SessionView struct {
	Session  int32  `json:"session"`
	UID      string `json:"uid"`
	Player   int    `json:"player"`
	State    string `json:"state"`
	PlayerID int    `json:"player_id"`
	Slot     int    `json:"slot"`
	Addr     string `json:"addr"`
}

// StepView is the step part of /api/state.
type StepView struct {
	Index int    `json:"index"`
	ID    string `json:"id"`
	State string `json:"state"`
}

// StateView is /api/state.
type StateView struct {
	Sessions []SessionView  `json:"sessions"`
	Step     StepView       `json:"step"`
	Players  []SessionView  `json:"players"`
	Captures map[string]any `json:"captures"`
}

// State returns the live state.
func (c *Console) State() StateView {
	c.mu.Lock()
	defer c.mu.Unlock()
	v := StateView{Sessions: []SessionView{}, Players: []SessionView{}, Step: StepView{Index: -1, State: "idle"}, Captures: map[string]any{}}
	ids := make([]int, 0, len(c.sessions))
	for id := range c.sessions {
		ids = append(ids, int(id))
	}
	sort.Ints(ids)
	for _, id := range ids {
		s := c.sessions[int32(id)]
		sv := SessionView{Session: s.id, UID: s.uid, Player: s.player, State: s.state, PlayerID: -1, Slot: -1, Addr: s.addr}
		if s.player >= 0 {
			p := c.players[s.player]
			if p.pid > 0 {
				sv.PlayerID = p.pid
			}
			sv.Slot = p.slot
		}
		v.Sessions = append(v.Sessions, sv)
	}
	for _, p := range c.players {
		pv := SessionView{Session: p.session, UID: p.uid, Player: p.index, PlayerID: -1, Slot: p.slot}
		if p.pid > 0 {
			pv.PlayerID = p.pid
		}
		if s := c.sessions[p.session]; s != nil {
			pv.State = s.state
		}
		v.Players = append(v.Players, pv)
	}
	if c.step != nil {
		v.Step = StepView{Index: c.step.index, ID: c.step.step.ID, State: c.step.state}
	}
	for k, x := range c.captures {
		v.Captures[k] = x
	}
	return v
}

// PlayerUID returns the uid of player p ("" when not connected).
func (c *Console) PlayerUID(p int) string {
	c.mu.Lock()
	defer c.mu.Unlock()
	if p < 0 || p >= len(c.players) {
		return ""
	}
	return c.players[p].uid
}

// ReceivedSince returns items with seq > since.
func (c *Console) ReceivedSince(since int64) (int64, []*Item) {
	c.mu.Lock()
	defer c.mu.Unlock()
	out := []*Item{}
	for _, it := range c.items {
		if it.Seq > since {
			cp := *it
			out = append(out, &cp)
		}
	}
	return c.seq, out
}

// CatalogueErrors returns every validation error seen so far.
func (c *Console) CatalogueErrors() []string {
	c.mu.Lock()
	defer c.mu.Unlock()
	return append([]string{}, c.catErrs...)
}
