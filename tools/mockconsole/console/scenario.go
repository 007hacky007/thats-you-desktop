package console

import (
	"bytes"
	"encoding/json"
	"fmt"
	"os"
	"regexp"
	"sort"
	"strings"
)

// Auto configures the built-in game emulation (contract 7.3). Pointers allow
// step-level overrides of single values.
type Auto struct {
	Join      *bool `json:"join,omitempty"`
	Resources *bool `json:"resources,omitempty"`
	SessionID *int  `json:"session_id,omitempty"`
	Jokers    *int  `json:"jokers,omitempty"`
	Refuse    *int  `json:"refuse,omitempty"`
	PendingMs *int  `json:"pending_ms,omitempty"`
}

// AutoCfg is the effective emulation configuration.
type AutoCfg struct {
	Join      bool
	Resources bool
	SessionID int
	Jokers    int
	Refuse    int
	PendingMs int
}

// DefaultAuto is the configuration without any scenario values.
func DefaultAuto() AutoCfg {
	return AutoCfg{Join: true, Resources: true, SessionID: 4242, Jokers: 2}
}

// Apply overlays the set values of a onto c.
func (c AutoCfg) Apply(a *Auto) AutoCfg {
	if a == nil {
		return c
	}
	if a.Join != nil {
		c.Join = *a.Join
	}
	if a.Resources != nil {
		c.Resources = *a.Resources
	}
	if a.SessionID != nil {
		c.SessionID = *a.SessionID
	}
	if a.Jokers != nil {
		c.Jokers = *a.Jokers
	}
	if a.Refuse != nil {
		c.Refuse = *a.Refuse
	}
	if a.PendingMs != nil {
		c.PendingMs = *a.PendingMs
	}
	return c
}

// Step is one scenario step.
type Step struct {
	ID         string           `json:"id"`
	Doc        string           `json:"doc,omitempty"`
	Player     int              `json:"player"`
	Auto       *Auto            `json:"auto,omitempty"`
	Send       []map[string]any `json:"send,omitempty"`
	Screen     string           `json:"screen,omitempty"`
	Variant    string           `json:"variant,omitempty"`
	Page       []map[string]any `json:"page,omitempty"`
	Expect     []map[string]any `json:"expect,omitempty"`
	Order      string           `json:"order,omitempty"`
	TimeoutMs  int              `json:"timeout_ms,omitempty"`
	Screenshot *bool            `json:"screenshot,omitempty"`
}

// Scenario is a scenario file (schema 1).
type Scenario struct {
	Schema      int                  `json:"schema"`
	Name        string               `json:"name"`
	Description string               `json:"description,omitempty"`
	Players     int                  `json:"players"`
	Bot         *bool                `json:"bot,omitempty"`
	Auto        *Auto                `json:"auto,omitempty"`
	Images      map[string]ImageSpec `json:"images,omitempty"`
	Steps       []Step               `json:"steps"`
	Path        string               `json:"-"`
}

// BotPlayable reports whether the Go bot can play the scenario.
func (s *Scenario) BotPlayable() bool { return s.Bot == nil || *s.Bot }

// Timeout returns the step timeout in ms (default 15000).
func (st *Step) Timeout() int {
	if st.TimeoutMs > 0 {
		return st.TimeoutMs
	}
	return 15000
}

// LoadScenario reads a scenario file; unknown keys are errors.
func LoadScenario(path string) (*Scenario, error) {
	data, err := os.ReadFile(path)
	if err != nil {
		return nil, err
	}
	s, err := ParseScenario(data)
	if err != nil {
		return nil, fmt.Errorf("%s: %w", path, err)
	}
	s.Path = path
	return s, nil
}

// ParseScenario decodes scenario JSON.
func ParseScenario(data []byte) (*Scenario, error) {
	d := json.NewDecoder(bytes.NewReader(data))
	d.UseNumber()
	d.DisallowUnknownFields()
	var s Scenario
	if err := d.Decode(&s); err != nil {
		return nil, err
	}
	if d.More() {
		return nil, fmt.Errorf("trailing data")
	}
	return &s, nil
}

// Expect is one parsed expectation item.
type Expect struct {
	Kind        string // msg, upload, hello, closed, none
	Msg         string
	Fields      map[string]any
	Capture     map[string]string // msg: name -> field
	CaptureName string            // upload: name for the ImageGUID
	ImgType     int
	Width       int
	Height      int
	MaxBytes    int
	UID         any // hello: optional expected uid (literal or placeholder)
	NoneMs      int
	Player      *int
	Raw         map[string]any
}

// Describe renders an expectation for messages.
func (e *Expect) Describe() string {
	switch e.Kind {
	case "msg":
		return e.Msg
	case "upload":
		return fmt.Sprintf("upload ImgType %d %dx%d", e.ImgType, e.Width, e.Height)
	case "hello":
		return "hello frame"
	case "closed":
		return "closed"
	case "none":
		return fmt.Sprintf("none_ms %d", e.NoneMs)
	}
	return e.Kind
}

func intOf(v any) (int, bool) {
	i, ok := AsInt(Normalize(v))
	return int(i), ok
}

func optPlayer(m map[string]any) (*int, error) {
	v, ok := m["player"]
	if !ok {
		return nil, nil
	}
	p, ok := intOf(v)
	if !ok || p < 0 {
		return nil, fmt.Errorf("bad player %v", v)
	}
	return &p, nil
}

func allowKeys(m map[string]any, keys ...string) error {
	ok := map[string]bool{}
	for _, k := range keys {
		ok[k] = true
	}
	var bad []string
	for k := range m {
		if !ok[k] {
			bad = append(bad, k)
		}
	}
	if len(bad) > 0 {
		sort.Strings(bad)
		return fmt.Errorf("unknown keys %s", strings.Join(bad, ","))
	}
	return nil
}

// ParseExpect parses one expect item.
func ParseExpect(m map[string]any) (*Expect, error) {
	e := &Expect{Raw: m}
	p, err := optPlayer(m)
	if err != nil {
		return nil, err
	}
	e.Player = p
	switch {
	case m["msg"] != nil:
		if err := allowKeys(m, "msg", "fields", "capture", "player"); err != nil {
			return nil, err
		}
		e.Kind = "msg"
		name, ok := m["msg"].(string)
		if !ok || name == "" {
			return nil, fmt.Errorf("msg must be a TypeString")
		}
		e.Msg = name
		if f, ok := m["fields"]; ok {
			fm, ok := f.(map[string]any)
			if !ok {
				return nil, fmt.Errorf("fields must be an object")
			}
			e.Fields = fm
		}
		if c, ok := m["capture"]; ok {
			cm, ok := c.(map[string]any)
			if !ok {
				return nil, fmt.Errorf("msg capture must be an object {name: field}")
			}
			e.Capture = map[string]string{}
			for k, v := range cm {
				s, ok := v.(string)
				if !ok {
					return nil, fmt.Errorf("capture %s: field name must be a string", k)
				}
				e.Capture[k] = s
			}
		}
	case m["upload"] != nil:
		if err := allowKeys(m, "upload", "capture", "player"); err != nil {
			return nil, err
		}
		e.Kind = "upload"
		u, ok := m["upload"].(map[string]any)
		if !ok {
			return nil, fmt.Errorf("upload must be an object")
		}
		if err := allowKeys(u, "ImgType", "width", "height", "maxBytes"); err != nil {
			return nil, fmt.Errorf("upload: %v", err)
		}
		var ok1 bool
		if e.ImgType, ok1 = intOf(u["ImgType"]); !ok1 {
			return nil, fmt.Errorf("upload needs ImgType")
		}
		e.Width, _ = intOf(u["width"])
		e.Height, _ = intOf(u["height"])
		e.MaxBytes = 128000
		if v, ok := u["maxBytes"]; ok {
			if e.MaxBytes, ok = intOf(v); !ok {
				return nil, fmt.Errorf("bad maxBytes")
			}
		}
		if c, ok := m["capture"]; ok {
			s, ok := c.(string)
			if !ok || s == "" {
				return nil, fmt.Errorf("upload capture must be a name")
			}
			e.CaptureName = s
		}
	case m["frame"] != nil:
		if err := allowKeys(m, "frame", "uid", "player"); err != nil {
			return nil, err
		}
		if m["frame"] != "hello" {
			return nil, fmt.Errorf("only {\"frame\": \"hello\"} can be expected")
		}
		e.Kind = "hello"
		e.UID = m["uid"]
	case m["closed"] != nil:
		if err := allowKeys(m, "closed", "player"); err != nil {
			return nil, err
		}
		if m["closed"] != true {
			return nil, fmt.Errorf("closed must be true")
		}
		e.Kind = "closed"
	case m["none_ms"] != nil:
		if err := allowKeys(m, "none_ms", "player"); err != nil {
			return nil, err
		}
		n, ok := intOf(m["none_ms"])
		if !ok || n <= 0 {
			return nil, fmt.Errorf("none_ms must be > 0")
		}
		e.Kind = "none"
		e.NoneMs = n
	default:
		return nil, fmt.Errorf("unknown expect item %s", Short(m, 80))
	}
	return e, nil
}

// SendItem is one parsed send item.
type SendItem struct {
	Kind     string // msg, content, resource, manifest, frame, close, wait
	Msg      map[string]any
	Raw      bool
	Header   map[string]any
	Image    string
	Resource string
	Manifest []map[string]any
	Frame    map[string]any
	WaitMs   int
	Player   *int
}

// ParseSend parses one send item.
func ParseSend(m map[string]any) (*SendItem, error) {
	s := &SendItem{}
	p, err := optPlayer(m)
	if err != nil {
		return nil, err
	}
	s.Player = p
	switch {
	case m["msg"] != nil:
		if err := allowKeys(m, "msg", "raw", "player"); err != nil {
			return nil, err
		}
		mm, ok := m["msg"].(map[string]any)
		if !ok {
			return nil, fmt.Errorf("msg must be an object")
		}
		if _, ok := mm["TypeString"].(string); !ok {
			return nil, fmt.Errorf("msg needs TypeString")
		}
		s.Kind, s.Msg = "msg", mm
		s.Raw = m["raw"] == true
	case m["content"] != nil:
		if err := allowKeys(m, "content", "player"); err != nil {
			return nil, err
		}
		c, ok := m["content"].(map[string]any)
		if !ok {
			return nil, fmt.Errorf("content must be an object")
		}
		h, ok := c["header"].(map[string]any)
		img, ok2 := c["image"].(string)
		if !ok || !ok2 {
			return nil, fmt.Errorf("content needs header and image")
		}
		s.Kind, s.Header, s.Image = "content", h, img
	case m["resource"] != nil:
		if err := allowKeys(m, "resource", "player"); err != nil {
			return nil, err
		}
		r, ok := m["resource"].(string)
		if !ok {
			return nil, fmt.Errorf("resource must be an image key")
		}
		s.Kind, s.Resource = "resource", r
	case m["manifest"] != nil:
		if err := allowKeys(m, "manifest", "player"); err != nil {
			return nil, err
		}
		list, ok := m["manifest"].([]any)
		if !ok {
			return nil, fmt.Errorf("manifest must be an array")
		}
		for _, x := range list {
			e, ok := x.(map[string]any)
			if !ok {
				return nil, fmt.Errorf("manifest entries must be objects")
			}
			if err := allowKeys(e, "image", "guid", "r"); err != nil {
				return nil, fmt.Errorf("manifest: %v", err)
			}
			s.Manifest = append(s.Manifest, e)
		}
		s.Kind = "manifest"
	case m["frame"] != nil:
		if err := allowKeys(m, "frame", "player"); err != nil {
			return nil, err
		}
		f, ok := m["frame"].(map[string]any)
		if !ok {
			return nil, fmt.Errorf("frame must be an object")
		}
		if _, ok := f["t"].(string); !ok {
			return nil, fmt.Errorf("frame needs t")
		}
		s.Kind, s.Frame = "frame", f
	case m["close"] != nil:
		if err := allowKeys(m, "close", "player"); err != nil {
			return nil, err
		}
		if m["close"] != true {
			return nil, fmt.Errorf("close must be true")
		}
		s.Kind = "close"
	case m["wait_ms"] != nil:
		if err := allowKeys(m, "wait_ms"); err != nil {
			return nil, err
		}
		n, ok := intOf(m["wait_ms"])
		if !ok || n < 0 {
			return nil, fmt.Errorf("bad wait_ms")
		}
		s.Kind, s.WaitMs = "wait", n
	default:
		return nil, fmt.Errorf("unknown send item %s", Short(m, 80))
	}
	return s, nil
}

// Screens and variants of contract 5.6.
var Screens = map[string][]string{
	"connect": nil, "joined": nil, "name": nil, "card": nil, "selfie": nil, "confirm": nil,
	"holding":           {"general", "wait", "look-at-tv", "other-photo", "other-ugc", "rejoin", "custom"},
	"quickdraw-holding": nil, "continue": {"continue", "start"}, "joker": nil,
	"answer": {"portraits", "images", "drawings", "texts"}, "draw": {"topdrawer", "canvas", "quickdraw"},
	"text": nil, "photo": {"topdrawer", "quickdraw"}, "mimic": nil,
}

var testIDRe = regexp.MustCompile(`^(connect-play|popup|popup-reconnect|popup-close|leave|leave-confirm|leave-cancel|` +
	`name-input|name-guest|name-next|card-([1-9]|1[0-2])|card-confirm|shutter|flip|effects|effect-([0-9]|1[01])|retake|use|no-camera|` +
	`profile-confirm|profile-edit-name|profile-edit-card|profile-edit-photo|continue|start|answer-[1-9][0-9]?|answer-confirm|joker-toggle|` +
	`draw-canvas|colour-([1-9]|1[0-4])|tool-brush|tool-eraser|tool-spray|size-[1-3]|undo|draw-confirm|text-input|text-confirm)$`)

// ValidTestID reports whether id is a data-testid of contract 5.10.
func ValidTestID(id string) bool { return testIDRe.MatchString(id) }

var pageKeys = map[string][]string{
	"tap": {"target"}, "hold": {"target", "ms"}, "type": {"target", "text"}, "draw": {"target", "points", "steps"},
	"flick": {"dir", "target"}, "wait": {"ms"}, "wait_screen": {"screen", "variant", "timeout_ms"}, "reload": {},
	"navigate": {"fragment"}, "expect_visible": {"target", "timeout_ms"}, "screenshot": {"name"},
	"set_storage": {"key", "value"},
}

func checkPage(a map[string]any) error {
	do, _ := a["do"].(string)
	keys, ok := pageKeys[do]
	if !ok {
		return fmt.Errorf("unknown page action %q", do)
	}
	if err := allowKeys(a, append([]string{"do"}, keys...)...); err != nil {
		return fmt.Errorf("%s: %v", do, err)
	}
	if t, ok := a["target"]; ok {
		s, _ := t.(string)
		if !ValidTestID(s) {
			return fmt.Errorf("%s: unknown testid %v", do, t)
		}
	} else if do == "tap" || do == "hold" || do == "type" || do == "draw" || do == "expect_visible" {
		return fmt.Errorf("%s needs target", do)
	}
	switch do {
	case "type":
		if _, ok := a["text"].(string); !ok {
			return fmt.Errorf("type needs text")
		}
	case "flick":
		if a["dir"] != "up" && a["dir"] != "down" {
			return fmt.Errorf("flick dir must be up or down")
		}
	case "wait":
		if n, ok := intOf(a["ms"]); !ok || n < 0 {
			return fmt.Errorf("wait needs ms")
		}
	case "wait_screen":
		sc, _ := a["screen"].(string)
		v, _ := a["variant"].(string)
		if err := checkScreen(sc, v); err != nil {
			return err
		}
	case "navigate":
		if a["fragment"] != "join" && a["fragment"] != "none" {
			return fmt.Errorf("navigate fragment must be join or none")
		}
	case "set_storage":
		if _, ok := a["key"].(string); !ok {
			return fmt.Errorf("set_storage needs key")
		}
		if _, ok := a["value"].(string); !ok {
			return fmt.Errorf("set_storage needs a string value")
		}
	case "draw":
		pts, ok := a["points"].([]any)
		if !ok || len(pts) < 2 {
			return fmt.Errorf("draw needs at least 2 points")
		}
		for _, p := range pts {
			xy, ok := p.([]any)
			if !ok || len(xy) != 2 {
				return fmt.Errorf("draw points are [x, y]")
			}
			for _, c := range xy {
				f, ok := AsFloat(c)
				if !ok || f < 0 || f > 1 {
					return fmt.Errorf("draw coordinates are 0..1")
				}
			}
		}
	}
	return nil
}

func checkScreen(screen, variant string) error {
	vars, ok := Screens[screen]
	if !ok {
		return fmt.Errorf("unknown screen %q", screen)
	}
	if variant == "" {
		return nil
	}
	for _, v := range vars {
		if v == variant {
			return nil
		}
	}
	return fmt.Errorf("screen %s has no variant %q", screen, variant)
}

// Check validates a scenario against the schema and the catalogue.
func (s *Scenario) Check(cat *Catalogue) []string {
	var errs []string
	add := func(where, f string, a ...any) {
		errs = append(errs, where+": "+fmt.Sprintf(f, a...))
	}
	if s.Schema != 1 {
		add("scenario", "schema %d, want 1", s.Schema)
	}
	if s.Name == "" {
		add("scenario", "missing name")
	}
	if s.Players < 1 || s.Players > 6 {
		add("scenario", "players %d, want 1..6", s.Players)
	}
	if len(s.Steps) == 0 {
		add("scenario", "no steps")
	}
	for k, im := range s.Images {
		if im.W <= 0 || im.H <= 0 || im.W > 4096 || im.H > 4096 {
			add("image "+k, "bad size %dx%d", im.W, im.H)
		}
		if im.Color != "" {
			if _, err := ParseColor(im.Color); err != nil {
				add("image "+k, "%v", err)
			}
		}
	}
	src := CheckSource(s.Images, s.Players)
	ids := map[string]bool{}
	firstSeen := map[int]bool{}
	for i := range s.Steps {
		st := &s.Steps[i]
		where := fmt.Sprintf("step %d (%s)", i, st.ID)
		if st.ID == "" {
			add(where, "missing id")
		} else if ids[st.ID] {
			add(where, "duplicate id")
		}
		ids[st.ID] = true
		if st.Player < 0 || st.Player >= s.Players {
			add(where, "player %d out of range", st.Player)
		}
		if st.Order != "" && st.Order != "strict" && st.Order != "any" {
			add(where, "order %q", st.Order)
		}
		if st.TimeoutMs < 0 {
			add(where, "negative timeout")
		}
		if st.Screen != "" {
			if err := checkScreen(st.Screen, st.Variant); err != nil {
				add(where, "%v", err)
			}
		} else if st.Variant != "" {
			add(where, "variant without screen")
		}
		if st.Auto != nil && st.Auto.Refuse != nil && (*st.Auto.Refuse < 0 || *st.Auto.Refuse > 255) {
			add(where, "bad refuse")
		}
		for j, a := range st.Page {
			if err := checkPage(a); err != nil {
				add(fmt.Sprintf("%s page %d", where, j), "%v", err)
			}
		}
		for j, m := range st.Send {
			w := fmt.Sprintf("%s send %d", where, j)
			it, err := ParseSend(m)
			if err != nil {
				add(w, "%v", err)
				continue
			}
			if it.Player != nil && *it.Player >= s.Players {
				add(w, "player out of range")
			}
			for _, e := range s.checkSend(cat, it, src) {
				add(w, "%s", e)
			}
		}
		hasHello, tapsPlay := false, false
		for j, m := range st.Expect {
			w := fmt.Sprintf("%s expect %d", where, j)
			e, err := ParseExpect(m)
			if err != nil {
				add(w, "%v", err)
				continue
			}
			if e.Player != nil && *e.Player >= s.Players {
				add(w, "player out of range")
			}
			if e.Kind == "hello" {
				hasHello = true
				if e.UID != nil {
					if _, err := Substitute(e.UID, src); err != nil {
						add(w, "%v", err)
					}
				}
			}
			for _, x := range checkExpect(cat, e, src) {
				add(w, "%s", x)
			}
		}
		for _, a := range st.Page {
			if a["do"] == "tap" && a["target"] == "connect-play" {
				tapsPlay = true
			}
		}
		if !firstSeen[st.Player] {
			firstSeen[st.Player] = true
			if !tapsPlay || !hasHello {
				add(where, "the first step of player %d must tap connect-play and expect the hello frame", st.Player)
			}
		}
	}
	return errs
}

func (s *Scenario) checkSend(cat *Catalogue, it *SendItem, src PlaceholderSource) []string {
	var errs []string
	checkMsg := func(m map[string]any, raw bool) {
		v, err := Substitute(m, src)
		if err != nil {
			errs = append(errs, err.Error())
			return
		}
		vm := v.(map[string]any)
		name, _ := vm["TypeString"].(string)
		def, ok := cat.Message(name)
		if !ok {
			errs = append(errs, "unknown TypeString "+name)
			return
		}
		if raw {
			return
		}
		for k := range vm {
			if _, ok := def.FieldOf(k); !ok && k != "TypeString" {
				errs = append(errs, fmt.Sprintf("%s has no field %s", name, k))
			}
		}
		o, _ := cat.Build(name, vm, true)
		_, es := cat.Validate("g2p", o)
		errs = append(errs, es...)
	}
	switch it.Kind {
	case "msg":
		checkMsg(it.Msg, it.Raw)
	case "content":
		checkMsg(it.Header, false)
		name, _ := it.Header["TypeString"].(string)
		if def, ok := cat.Message(name); ok && !def.Content {
			errs = append(errs, name+" is not a content header")
		}
		if _, ok := s.Images[it.Image]; !ok {
			errs = append(errs, "unknown image "+it.Image)
		}
	case "resource":
		if _, ok := s.Images[it.Resource]; !ok {
			errs = append(errs, "unknown image "+it.Resource)
		}
	case "manifest":
		for _, e := range it.Manifest {
			if k, ok := e["image"].(string); ok {
				if _, ok := s.Images[k]; !ok {
					errs = append(errs, "manifest: unknown image "+k)
				}
			} else if g, ok := e["guid"].(string); ok {
				if _, err := Substitute(g, src); err != nil {
					errs = append(errs, err.Error())
				}
			} else {
				errs = append(errs, "manifest entry needs image or guid")
			}
			if r, ok := intOf(e["r"]); !ok || r < 0 || r > 2 {
				errs = append(errs, "manifest r must be 0..2")
			}
		}
	}
	return errs
}

func checkExpect(cat *Catalogue, e *Expect, src PlaceholderSource) []string {
	var errs []string
	switch e.Kind {
	case "msg":
		def, ok := cat.Message(e.Msg)
		if !ok {
			return []string{"unknown TypeString " + e.Msg}
		}
		if def.Dir == "g2p" {
			errs = append(errs, e.Msg+" is a game -> phone message")
		}
		for name, want := range e.Fields {
			f, ok := def.FieldOf(name)
			if !ok {
				errs = append(errs, fmt.Sprintf("%s has no field %s", e.Msg, name))
				continue
			}
			if op, arg, ok := AsMatcher(want); ok {
				if x := CheckMatcher(op, arg); x != "" {
					errs = append(errs, name+": "+x)
				}
				if op == "eq" || op == "ne" {
					want = arg
				} else {
					if _, err := Substitute(arg, src); err != nil {
						errs = append(errs, err.Error())
					}
					continue
				}
			}
			v, err := Substitute(want, src)
			if err != nil {
				errs = append(errs, err.Error())
				continue
			}
			if x := cat.checkValue(f.Type, v); x != "" {
				errs = append(errs, fmt.Sprintf("%s.%s: %s", e.Msg, name, x))
			}
		}
		for cname, field := range e.Capture {
			if _, ok := def.FieldOf(field); !ok {
				errs = append(errs, fmt.Sprintf("capture %s: %s has no field %s", cname, e.Msg, field))
			}
		}
	case "upload":
		if e.ImgType < 0 || e.ImgType > 5 {
			errs = append(errs, "ImgType must be 0..5")
		}
		if e.Width < 0 || e.Height < 0 {
			errs = append(errs, "negative size")
		}
		if e.MaxBytes <= 0 || e.MaxBytes > 131072 {
			errs = append(errs, "maxBytes must be 1..131072")
		}
	}
	return errs
}
