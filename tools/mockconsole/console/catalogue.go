package console

import (
	"encoding/json"
	"fmt"
	"math"
	"os"
	"path/filepath"
	"sort"
	"strings"
)

// Field is one field of a message or nested type.
type Field struct {
	Name     string `json:"name"`
	Type     string `json:"type"`
	Enum     string `json:"enum,omitempty"`
	Default  any    `json:"default,omitempty"`
	Optional bool   `json:"optional,omitempty"`
	Doc      string `json:"doc,omitempty"`
}

// Rule is one validity rule; When makes it conditional.
type Rule struct {
	Field string `json:"field"`
	Op    string `json:"op"`
	Value any    `json:"value,omitempty"`
	When  *Rule  `json:"when,omitempty"`
}

// MessageDef describes one game message.
type MessageDef struct {
	Name         string
	Dir          string   `json:"dir"`
	Page         bool     `json:"page"`
	Unused       bool     `json:"unused"`
	Content      bool     `json:"content"`
	Base         string   `json:"base"`
	InheritValid *bool    `json:"inheritValid"`
	Doc          string   `json:"doc"`
	ReplyTo      []string `json:"replyTo"`
	OwnFields    []Field  `json:"fields"`
	OwnValid     []Rule   `json:"valid"`

	Fields   []Field // base fields first
	Valid    []Rule
	resolved bool
}

// Catalogue is protocol/messages.json loaded.
type Catalogue struct {
	Schema           int                        `json:"schema"`
	InterfaceVersion string                     `json:"interfaceVersion"`
	JSONTransferID   int                        `json:"jsonTransferId"`
	Enums            map[string]map[string]int  `json:"enums"`
	Types            map[string][]Field         `json:"types"`
	Messages         map[string]*MessageDef     `json:"messages"`
	Frames           map[string]json.RawMessage `json:"frames"`
}

var knownOps = map[string]bool{"eq": true, "ne": true, "gt": true, "ge": true, "lt": true, "le": true, "in": true, "nonempty": true}

// LoadCatalogue reads and resolves a catalogue file.
func LoadCatalogue(path string) (*Catalogue, error) {
	data, err := os.ReadFile(path)
	if err != nil {
		return nil, err
	}
	return ParseCatalogue(data)
}

// ParseCatalogue parses and resolves catalogue JSON (bases, inherited rules) and checks it.
func ParseCatalogue(data []byte) (*Catalogue, error) {
	var c Catalogue
	d := json.NewDecoder(strings.NewReader(string(data)))
	d.UseNumber()
	if err := d.Decode(&c); err != nil {
		return nil, fmt.Errorf("catalogue: %w", err)
	}
	if c.Schema != 1 {
		return nil, fmt.Errorf("catalogue: schema %d, want 1", c.Schema)
	}
	for name, m := range c.Messages {
		m.Name = name
	}
	var errs []string
	for _, name := range c.MessageNames() {
		if err := c.resolve(name, map[string]bool{}); err != nil {
			errs = append(errs, err.Error())
		}
	}
	errs = append(errs, c.lint()...)
	if len(errs) > 0 {
		return nil, fmt.Errorf("catalogue: %s", strings.Join(errs, "; "))
	}
	return &c, nil
}

func (c *Catalogue) resolve(name string, seen map[string]bool) error {
	m := c.Messages[name]
	if m.resolved {
		return nil
	}
	if seen[name] {
		return fmt.Errorf("%s: base cycle", name)
	}
	seen[name] = true
	fields := []Field{}
	valid := []Rule{}
	if m.Base != "" {
		b, ok := c.Messages[m.Base]
		if !ok {
			return fmt.Errorf("%s: unknown base %s", name, m.Base)
		}
		if err := c.resolve(m.Base, seen); err != nil {
			return err
		}
		fields = append(fields, b.Fields...)
		if m.InheritValid == nil || *m.InheritValid {
			valid = append(valid, b.Valid...)
		}
	}
	m.Fields = append(fields, m.OwnFields...)
	m.Valid = append(valid, m.OwnValid...)
	m.resolved = true
	return nil
}

func (c *Catalogue) lint() []string {
	var errs []string
	checkType := func(where, t string) {
		switch {
		case t == "int", t == "long", t == "double", t == "bool", t == "string", t == "string[]":
		case strings.HasPrefix(t, "object:"), strings.HasPrefix(t, "list:"):
			name := t[strings.Index(t, ":")+1:]
			if _, ok := c.Types[name]; !ok && name != "Message" {
				errs = append(errs, fmt.Sprintf("%s: unknown type %s", where, name))
			}
		default:
			errs = append(errs, fmt.Sprintf("%s: unknown type %q", where, t))
		}
	}
	for tn, fs := range c.Types {
		for _, f := range fs {
			checkType("type "+tn+"."+f.Name, f.Type)
		}
	}
	for _, name := range c.MessageNames() {
		m := c.Messages[name]
		if m.Dir != "p2g" && m.Dir != "g2p" && m.Dir != "both" {
			errs = append(errs, fmt.Sprintf("%s: bad dir %q", name, m.Dir))
		}
		seen := map[string]bool{}
		for _, f := range m.Fields {
			if seen[f.Name] {
				errs = append(errs, fmt.Sprintf("%s: duplicate field %s", name, f.Name))
			}
			seen[f.Name] = true
			checkType(name+"."+f.Name, f.Type)
			if f.Enum != "" {
				if _, ok := c.Enums[f.Enum]; !ok {
					errs = append(errs, fmt.Sprintf("%s.%s: unknown enum %s", name, f.Name, f.Enum))
				}
			}
			if f.Default != nil {
				if e := c.checkValue(f.Type, f.Default); e != "" {
					errs = append(errs, fmt.Sprintf("%s.%s default: %s", name, f.Name, e))
				}
			}
		}
		for _, r := range m.Valid {
			for rr := &r; rr != nil; rr = rr.When {
				if !knownOps[rr.Op] {
					errs = append(errs, fmt.Sprintf("%s: unknown op %q", name, rr.Op))
				}
				root := strings.SplitN(rr.Field, ".", 2)[0]
				if !seen[root] {
					errs = append(errs, fmt.Sprintf("%s: rule on unknown field %s", name, rr.Field))
				}
			}
		}
	}
	return errs
}

// MessageNames returns every message name, sorted.
func (c *Catalogue) MessageNames() []string {
	names := make([]string, 0, len(c.Messages))
	for n := range c.Messages {
		names = append(names, n)
	}
	sort.Strings(names)
	return names
}

// Message returns a message definition.
func (c *Catalogue) Message(name string) (*MessageDef, bool) {
	m, ok := c.Messages[name]
	return m, ok
}

// FieldOf finds a field of a message by name.
func (m *MessageDef) FieldOf(name string) (Field, bool) {
	for _, f := range m.Fields {
		if f.Name == name {
			return f, true
		}
	}
	return Field{}, false
}

// checkValue checks a JSON value against a catalogue type; "" when fine.
func (c *Catalogue) checkValue(t string, v any) string {
	if v == Skip {
		return ""
	}
	v = Normalize(v)
	switch {
	case t == "int" || t == "long":
		i, ok := AsInt(v)
		if !ok {
			return fmt.Sprintf("want %s, got %s", t, TypeName(v))
		}
		if t == "int" && (i > math.MaxInt32 || i < math.MinInt32) {
			return fmt.Sprintf("int out of range: %d", i)
		}
	case t == "double":
		if _, ok := AsFloat(v); !ok {
			return fmt.Sprintf("want double, got %s", TypeName(v))
		}
	case t == "bool":
		if _, ok := v.(bool); !ok {
			return fmt.Sprintf("want bool, got %s", TypeName(v))
		}
	case t == "string":
		if _, ok := v.(string); !ok {
			return fmt.Sprintf("want string, got %s", TypeName(v))
		}
	case t == "string[]":
		a, ok := v.([]any)
		if !ok {
			return fmt.Sprintf("want string[], got %s", TypeName(v))
		}
		for i, x := range a {
			if _, ok := x.(string); !ok {
				return fmt.Sprintf("element %d: want string, got %s", i, TypeName(x))
			}
		}
	case strings.HasPrefix(t, "object:"):
		if e := c.checkObject(t[7:], v); e != "" {
			return e
		}
	case strings.HasPrefix(t, "list:"):
		a, ok := v.([]any)
		if !ok {
			return fmt.Sprintf("want array, got %s", TypeName(v))
		}
		for i, x := range a {
			if e := c.checkObject(t[5:], x); e != "" {
				return fmt.Sprintf("element %d: %s", i, e)
			}
		}
	}
	return ""
}

func (c *Catalogue) checkObject(typ string, v any) string {
	m, ok := v.(map[string]any)
	if !ok {
		return fmt.Sprintf("want object %s, got %s", typ, TypeName(v))
	}
	if typ == "Message" {
		return ""
	}
	for _, f := range c.Types[typ] {
		x, ok := m[f.Name]
		if !ok {
			if !f.Optional {
				return fmt.Sprintf("%s: missing field %s", typ, f.Name)
			}
			continue
		}
		if e := c.checkValue(f.Type, x); e != "" {
			return fmt.Sprintf("%s.%s: %s", typ, f.Name, e)
		}
	}
	return ""
}

// Skip is a marker value: fields holding it are not type or rule checked
// (used for unresolved placeholders in -check).
const Skip = "\x00skip"

// Validate checks a decoded game message. dir is "p2g" or "g2p" (the direction
// it travelled). It returns the TypeString (may be "") and a list of errors.
func (c *Catalogue) Validate(dir string, msg any) (string, []string) {
	obj, ok := Normalize(msg).(map[string]any)
	if !ok {
		return "", []string{"message is not a JSON object"}
	}
	ts, _ := obj["TypeString"].(string)
	if ts == "" {
		return "", []string{"missing TypeString"}
	}
	def, ok := c.Messages[ts]
	if !ok {
		return ts, []string{"unknown TypeString " + ts}
	}
	var errs []string
	if def.Dir != "both" && def.Dir != dir {
		errs = append(errs, fmt.Sprintf("%s travels %s, not %s", ts, def.Dir, dir))
	}
	if def.Unused {
		errs = append(errs, ts+" is not used by the port")
	}
	for _, f := range def.Fields {
		v, ok := obj[f.Name]
		if !ok {
			if !f.Optional {
				errs = append(errs, fmt.Sprintf("%s: missing field %s", ts, f.Name))
			}
			continue
		}
		if v == Skip {
			continue
		}
		if e := c.checkValue(f.Type, v); e != "" {
			errs = append(errs, fmt.Sprintf("%s.%s: %s", ts, f.Name, e))
		}
	}
	if len(errs) > 0 {
		return ts, errs
	}
	for _, r := range def.Valid {
		if r.When != nil {
			ok, skip := evalRule(obj, *r.When)
			if skip || !ok {
				continue
			}
		}
		ok, skip := evalRule(obj, r)
		if !skip && !ok {
			errs = append(errs, fmt.Sprintf("%s: rule failed: %s %s %s (got %s)", ts, r.Field, r.Op, Short(r.Value, 60), Short(lookupPath(obj, r.Field), 60)))
		}
	}
	return ts, errs
}

func lookupPath(obj map[string]any, path string) any {
	var cur any = obj
	for _, p := range strings.Split(path, ".") {
		m, ok := cur.(map[string]any)
		if !ok {
			return nil
		}
		cur = m[p]
	}
	return cur
}

// evalRule returns (passes, skipped).
func evalRule(obj map[string]any, r Rule) (bool, bool) {
	v := lookupPath(obj, r.Field)
	if v == Skip {
		return true, true
	}
	return applyOp(r.Op, v, r.Value), false
}

func applyOp(op string, v, want any) bool {
	switch op {
	case "eq":
		return Equal(v, want)
	case "ne":
		return !Equal(v, want)
	case "gt", "ge", "lt", "le":
		a, ok1 := AsFloat(Normalize(v))
		b, ok2 := AsFloat(Normalize(want))
		if !ok1 || !ok2 {
			return false
		}
		switch op {
		case "gt":
			return a > b
		case "ge":
			return a >= b
		case "lt":
			return a < b
		}
		return a <= b
	case "in":
		list, ok := Normalize(want).([]any)
		if !ok {
			return false
		}
		for _, x := range list {
			if Equal(v, x) {
				return true
			}
		}
		return false
	case "nonempty":
		switch t := Normalize(v).(type) {
		case string:
			return t != ""
		case []any:
			return len(t) > 0
		case map[string]any:
			return len(t) > 0
		case nil:
			return false
		}
		return true
	}
	return false
}

// DefaultValue returns the synthesized default of a field.
func (c *Catalogue) DefaultValue(f Field) any {
	if f.Default != nil {
		return Normalize(f.Default)
	}
	return c.zero(f.Type)
}

func (c *Catalogue) zero(t string) any {
	switch {
	case t == "int" || t == "long":
		return json.Number("0")
	case t == "double":
		return json.Number("0.0")
	case t == "bool":
		return false
	case t == "string":
		return ""
	case t == "string[]" || strings.HasPrefix(t, "list:"):
		return []any{}
	case strings.HasPrefix(t, "object:"):
		o := NewObj()
		for _, f := range c.Types[t[7:]] {
			o.Set(f.Name, c.DefaultValue(f))
		}
		return o
	}
	return nil
}

// Build creates an ordered message: TypeString first, then the catalogue
// fields in order taking values from vals, else (when fill) the defaults.
// Keys of vals unknown to the catalogue are appended at the end.
func (c *Catalogue) Build(name string, vals map[string]any, fill bool) (*Obj, error) {
	def, ok := c.Messages[name]
	if !ok {
		return nil, fmt.Errorf("unknown message %s", name)
	}
	o := NewObj().Set("TypeString", name)
	used := map[string]bool{"TypeString": true}
	for _, f := range def.Fields {
		if v, ok := vals[f.Name]; ok {
			o.Set(f.Name, v)
		} else if fill && !f.Optional {
			o.Set(f.Name, c.DefaultValue(f))
		}
		used[f.Name] = true
	}
	var extra []string
	for k := range vals {
		if !used[k] {
			extra = append(extra, k)
		}
	}
	sort.Strings(extra)
	for _, k := range extra {
		o.Set(k, vals[k])
	}
	return o, nil
}

// FindCatalogue looks for protocol/messages.json upwards from each start directory.
func FindCatalogue(starts ...string) (string, error) {
	for _, s := range starts {
		if s == "" {
			continue
		}
		dir, err := filepath.Abs(s)
		if err != nil {
			continue
		}
		for {
			p := filepath.Join(dir, "protocol", "messages.json")
			if _, err := os.Stat(p); err == nil {
				return p, nil
			}
			parent := filepath.Dir(dir)
			if parent == dir {
				break
			}
			dir = parent
		}
	}
	return "", fmt.Errorf("protocol/messages.json not found (use -catalogue)")
}
