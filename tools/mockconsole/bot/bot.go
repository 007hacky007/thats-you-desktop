// Package bot plays the page side of scenarios with iwyknet's Go client: one
// client per scenario player that answers each step's expectations by
// synthesizing them (eq values, catalogue defaults, generated JPEGs).
package bot

import (
	"context"
	"crypto/rand"
	"encoding/json"
	"fmt"
	"math/big"
	"strings"
	"sync"
	"time"

	"iwyknet/client"

	"mockconsole/console"
)

// Bot answers the expectations of every started step.
type Bot struct {
	ctx     context.Context
	c       *console.Console
	cat     *console.Catalogue
	params  client.Params
	logf    func(string, ...any)
	mu      sync.Mutex
	players map[int]*player
	busy    sync.Mutex
}

type player struct {
	index     int
	uid       string
	mu        sync.Mutex
	cli       *client.Client
	gen       int // connection generation
	joined    bool
	refused   bool
	iv, ss    bool
	manifest  []any
	lastGUID  string
	tid       int32
	changed   chan struct{}
	closedGen int
}

// Attach parses the join URL and registers the bot on the console's step hook.
func Attach(ctx context.Context, c *console.Console, joinURL string, logf func(string, ...any)) (*Bot, error) {
	_, p, err := client.ParseJoinURL(joinURL)
	if err != nil {
		return nil, fmt.Errorf("bot: %w", err)
	}
	if logf == nil {
		logf = func(string, ...any) {}
	}
	b := &Bot{ctx: ctx, c: c, cat: c.Catalogue(), params: p, logf: logf, players: map[int]*player{}}
	c.OnStepReady(b.onStep)
	return b, nil
}

// Close closes every client.
func (b *Bot) Close() {
	b.mu.Lock()
	defer b.mu.Unlock()
	for _, p := range b.players {
		p.mu.Lock()
		if p.cli != nil {
			p.cli.Close()
		}
		p.mu.Unlock()
	}
}

func randInt(lo, hi int64) int64 {
	n, err := rand.Int(rand.Reader, big.NewInt(hi-lo))
	if err != nil {
		panic(err)
	}
	return lo + n.Int64()
}

func (b *Bot) player(i int) *player {
	b.mu.Lock()
	defer b.mu.Unlock()
	p := b.players[i]
	if p == nil {
		p = &player{index: i, uid: fmt.Sprintf("bot-%d-%s", i, console.NewGUID()[:8]), tid: int32(randInt(1000, 1000000)), changed: make(chan struct{})}
		b.players[i] = p
	}
	return p
}

func (p *player) signalLocked() {
	close(p.changed)
	p.changed = make(chan struct{})
}

// waitFor waits until cond (called with p.mu held) is true.
func (p *player) waitFor(ctx context.Context, cond func() bool) bool {
	for {
		p.mu.Lock()
		if cond() {
			p.mu.Unlock()
			return true
		}
		ch := p.changed
		p.mu.Unlock()
		select {
		case <-ch:
		case <-ctx.Done():
			return false
		}
	}
}

func (b *Bot) onStep(i int) {
	b.busy.Lock()
	defer b.busy.Unlock()
	st := b.c.Scenario().Steps[i]
	ctx, cancel := context.WithTimeout(b.ctx, time.Duration(st.Timeout())*time.Millisecond)
	defer cancel()
	for j, m := range st.Expect {
		e, err := console.ParseExpect(m)
		if err != nil {
			b.logf("bot: step %s expect %d: %v", st.ID, j, err)
			return
		}
		pi := st.Player
		if e.Player != nil {
			pi = *e.Player
		}
		if err := b.act(ctx, b.player(pi), e); err != nil {
			b.logf("bot: step %s expect %d (%s): %v", st.ID, j, e.Describe(), err)
			return
		}
	}
}

func (b *Bot) act(ctx context.Context, p *player, e *console.Expect) error {
	switch e.Kind {
	case "hello":
		uid := p.uid
		if e.UID != nil {
			v, err := b.resolveWait(ctx, e.UID)
			if err != nil {
				return err
			}
			if op, arg, ok := console.AsMatcher(v); ok && op == "eq" {
				v = arg
			}
			if s, ok := v.(string); ok {
				uid = s
			}
		}
		return b.connect(ctx, p, uid)
	case "closed":
		p.mu.Lock()
		gen := p.gen
		cli := p.cli
		p.mu.Unlock()
		wctx, cancel := context.WithTimeout(ctx, 1500*time.Millisecond)
		defer cancel()
		if !p.waitFor(wctx, func() bool { return p.closedGen >= gen }) && cli != nil {
			cli.Close()
		}
		return nil
	case "none":
		return nil
	case "upload":
		return b.upload(ctx, p, e)
	case "msg":
		return b.message(ctx, p, e)
	}
	return fmt.Errorf("unknown expectation %s", e.Kind)
}

func (b *Bot) connect(ctx context.Context, p *player, uid string) error {
	p.mu.Lock()
	old := p.cli
	p.mu.Unlock()
	if old != nil {
		old.Close() // a page reload
	}
	cli, err := client.Dial(ctx, b.params, client.Options{})
	if err != nil {
		return err
	}
	p.mu.Lock()
	p.gen++
	gen := p.gen
	p.cli = cli
	p.uid = uid
	p.joined, p.refused, p.iv, p.ss = false, false, false, false
	p.signalLocked()
	p.mu.Unlock()
	go b.read(p, cli, gen)
	return cli.Hello(uid)
}

func (b *Bot) read(p *player, cli *client.Client, gen int) {
	for {
		f, err := cli.Next(b.ctx)
		if err != nil {
			p.mu.Lock()
			if p.closedGen < gen {
				p.closedGen = gen
			}
			if p.gen == gen {
				p.cli = nil
			}
			p.signalLocked()
			p.mu.Unlock()
			return
		}
		p.mu.Lock()
		if p.gen != gen {
			p.mu.Unlock()
			continue
		}
		switch f.T {
		case "joined":
			p.joined = true
		case "refused":
			p.refused = true
		case "msg":
			var m map[string]any
			d := json.NewDecoder(strings.NewReader(string(f.JSON)))
			d.UseNumber()
			if d.Decode(&m) == nil {
				switch m["TypeString"] {
				case "InterfaceVersionMessage":
					p.iv = true
				case "SessionStateMessage":
					p.ss = true
				case "ResourceRequirementsMessage":
					p.manifest, _ = m["Requirements"].([]any)
				}
			}
		}
		p.signalLocked()
		p.mu.Unlock()
	}
}

// resolveWait substitutes placeholders, retrying while a capture is not there yet.
func (b *Bot) resolveWait(ctx context.Context, v any) (any, error) {
	for {
		r, err := b.c.Resolve(v)
		if err == nil {
			return r, nil
		}
		if !strings.Contains(err.Error(), " yet") {
			return nil, err
		}
		select {
		case <-ctx.Done():
			return nil, err
		case <-time.After(20 * time.Millisecond):
		}
	}
}

func (b *Bot) liveClient(p *player) (*client.Client, error) {
	p.mu.Lock()
	defer p.mu.Unlock()
	if p.cli == nil {
		return nil, fmt.Errorf("player %d is not connected", p.index)
	}
	return p.cli, nil
}

func (b *Bot) upload(ctx context.Context, p *player, e *console.Expect) error {
	cli, err := b.liveClient(p)
	if err != nil {
		return err
	}
	w, h := e.Width, e.Height
	if w == 0 || h == 0 {
		w, h = 480, 640
		if e.ImgType == 1 {
			w, h = 384, 512
		}
	}
	jpg, err := console.SynthJPEG(console.ImageSpec{W: w, H: h, Color: "#3366cc", Label: fmt.Sprintf("BOT %d", e.ImgType)})
	if err != nil {
		return err
	}
	p.mu.Lock()
	p.tid++
	tid := p.tid
	guid := console.NewGUID()
	p.lastGUID = guid
	p.mu.Unlock()
	hdr, err := b.cat.Build("ClientImageResourceContentTransferMessage", map[string]any{"TransferID": tid, "ImageGUID": guid, "ImgType": e.ImgType}, true)
	if err != nil {
		return err
	}
	if err := cli.SendMsg(console.Marshal(hdr)); err != nil {
		return err
	}
	return cli.SendContent(tid, jpg)
}

func (b *Bot) message(ctx context.Context, p *player, e *console.Expect) error {
	def, ok := b.cat.Message(e.Msg)
	if !ok {
		return fmt.Errorf("unknown message %s", e.Msg)
	}
	if e.Msg == "ClientRequestPlayerIDMessage" {
		if !p.waitFor(ctx, func() bool { return p.iv && p.ss }) {
			return fmt.Errorf("no InterfaceVersion and SessionState")
		}
	}
	cli, err := b.liveClient(p)
	if err != nil {
		return err
	}
	vals := map[string]any{}
	for name, want := range e.Fields {
		f, ok := def.FieldOf(name)
		if !ok {
			continue
		}
		r, err := b.resolveWait(ctx, want)
		if err != nil {
			return err
		}
		vals[name] = synth(b.cat, f, r)
	}
	p.mu.Lock()
	for _, f := range def.Fields {
		if _, set := vals[f.Name]; set {
			continue
		}
		switch {
		case f.Name == "UID" || f.Name == "DeviceUID":
			vals[f.Name] = p.uid
		case f.Name == "ImageGUID" && p.lastGUID != "":
			vals[f.Name] = p.lastGUID
		case e.Msg == "AllResourcesReceivedMessage" && f.Name == "Requirements":
			vals[f.Name] = requiredOf(p.manifest)
		}
	}
	p.mu.Unlock()
	o, err := b.cat.Build(e.Msg, vals, true)
	if err != nil {
		return err
	}
	return cli.SendMsg(console.Marshal(o))
}

func requiredOf(manifest []any) []any {
	out := []any{}
	for _, x := range manifest {
		m, ok := x.(map[string]any)
		if !ok {
			continue
		}
		if r, _ := console.AsInt(m["Rqrmnt"]); r == 1 {
			out = append(out, console.NewObj().Set("GUID", m["GUID"]).Set("Rqrmnt", 1))
		}
	}
	return out
}

// synth turns an expectation value (literal or matcher) into a value that satisfies it.
func synth(cat *console.Catalogue, f console.Field, want any) any {
	op, arg, ok := console.AsMatcher(want)
	if !ok {
		return want
	}
	dflt := cat.DefaultValue(f)
	switch op {
	case "eq":
		return arg
	case "ne":
		if !console.Equal(dflt, arg) {
			return dflt
		}
		switch t := console.Normalize(arg).(type) {
		case bool:
			return !t
		case string:
			return t + "x"
		}
		if n, ok := console.AsFloat(console.Normalize(arg)); ok {
			return console.Normalize(int(n) + 1)
		}
		return dflt
	case "gt", "ge", "lt", "le":
		n, _ := console.AsFloat(console.Normalize(arg))
		if console.Match(want, dflt) == "" {
			return dflt
		}
		v := int(n)
		switch op {
		case "gt":
			v++
		case "lt":
			v--
		}
		return console.Normalize(v)
	case "in":
		if l, ok := console.Normalize(arg).([]any); ok && len(l) > 0 {
			return l[0]
		}
	case "nonempty":
		if console.Match(want, dflt) == "" {
			return dflt
		}
		if strings.HasSuffix(f.Type, "[]") || strings.HasPrefix(f.Type, "list:") {
			return []any{"x"}
		}
		return "x"
	case "type":
		if console.Match(want, dflt) == "" {
			return dflt
		}
		switch arg {
		case "string":
			return "x"
		case "int", "double":
			return console.Normalize(0)
		case "bool":
			return false
		case "array":
			return []any{}
		case "object":
			return map[string]any{}
		}
	}
	return dflt
}
