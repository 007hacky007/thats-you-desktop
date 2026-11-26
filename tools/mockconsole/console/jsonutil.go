package console

import (
	"bytes"
	"encoding/json"
	"fmt"
	"math"
	"strconv"
)

// Obj is a JSON object that keeps its key order when encoded.
type Obj struct {
	Keys []string
	Vals map[string]any
}

// NewObj returns an empty ordered object.
func NewObj() *Obj { return &Obj{Vals: map[string]any{}} }

// Set adds or replaces a key, keeping the first position of an existing key.
func (o *Obj) Set(k string, v any) *Obj {
	if _, ok := o.Vals[k]; !ok {
		o.Keys = append(o.Keys, k)
	}
	o.Vals[k] = v
	return o
}

// Get returns a value.
func (o *Obj) Get(k string) (any, bool) {
	v, ok := o.Vals[k]
	return v, ok
}

// MarshalJSON writes the keys in insertion order without whitespace.
func (o *Obj) MarshalJSON() ([]byte, error) {
	var b bytes.Buffer
	b.WriteByte('{')
	for i, k := range o.Keys {
		if i > 0 {
			b.WriteByte(',')
		}
		kb, _ := json.Marshal(k)
		b.Write(kb)
		b.WriteByte(':')
		vb, err := marshal(o.Vals[k])
		if err != nil {
			return nil, err
		}
		b.Write(vb)
	}
	b.WriteByte('}')
	return b.Bytes(), nil
}

// marshal encodes without HTML escaping.
func marshal(v any) ([]byte, error) {
	var b bytes.Buffer
	enc := json.NewEncoder(&b)
	enc.SetEscapeHTML(false)
	if err := enc.Encode(v); err != nil {
		return nil, err
	}
	return bytes.TrimRight(b.Bytes(), "\n"), nil
}

// Marshal encodes a value compactly (ordered objects keep their order).
func Marshal(v any) []byte {
	b, err := marshal(v)
	if err != nil {
		panic(err)
	}
	return b
}

// Decode parses JSON with json.Number for numbers. Objects become map[string]any.
func Decode(data []byte) (any, error) {
	d := json.NewDecoder(bytes.NewReader(data))
	d.UseNumber()
	var v any
	if err := d.Decode(&v); err != nil {
		return nil, err
	}
	if d.More() {
		return nil, fmt.Errorf("trailing data after JSON value")
	}
	return v, nil
}

// Normalize converts a decoded or literal Go value to the generic JSON model
// (map[string]any, []any, string, bool, nil, json.Number).
func Normalize(v any) any {
	switch t := v.(type) {
	case *Obj:
		m := map[string]any{}
		for _, k := range t.Keys {
			m[k] = Normalize(t.Vals[k])
		}
		return m
	case map[string]any:
		m := make(map[string]any, len(t))
		for k, x := range t {
			m[k] = Normalize(x)
		}
		return m
	case []any:
		a := make([]any, len(t))
		for i, x := range t {
			a[i] = Normalize(x)
		}
		return a
	case []string:
		a := make([]any, len(t))
		for i, x := range t {
			a[i] = x
		}
		return a
	case int:
		return json.Number(strconv.Itoa(t))
	case int32:
		return json.Number(strconv.Itoa(int(t)))
	case int64:
		return json.Number(strconv.FormatInt(t, 10))
	case float64:
		return json.Number(strconv.FormatFloat(t, 'g', -1, 64))
	default:
		return v
	}
}

// AsFloat returns the numeric value of a JSON number.
func AsFloat(v any) (float64, bool) {
	switch t := v.(type) {
	case json.Number:
		f, err := t.Float64()
		return f, err == nil
	case float64:
		return t, true
	case int:
		return float64(t), true
	case int32:
		return float64(t), true
	case int64:
		return float64(t), true
	}
	return 0, false
}

// AsInt returns the integer value of a JSON number without a fractional part.
func AsInt(v any) (int64, bool) {
	if n, ok := v.(json.Number); ok {
		if i, err := strconv.ParseInt(string(n), 10, 64); err == nil {
			return i, true
		}
	}
	f, ok := AsFloat(v)
	if !ok || f != math.Trunc(f) || math.IsInf(f, 0) || f > math.MaxInt64 || f < math.MinInt64 {
		return 0, false
	}
	return int64(f), true
}

// Equal compares two JSON values; numbers compare by value.
func Equal(a, b any) bool {
	a, b = Normalize(a), Normalize(b)
	if fa, ok := AsFloat(a); ok {
		fb, ok2 := AsFloat(b)
		return ok2 && fa == fb
	}
	switch x := a.(type) {
	case map[string]any:
		y, ok := b.(map[string]any)
		if !ok || len(x) != len(y) {
			return false
		}
		for k, v := range x {
			w, ok := y[k]
			if !ok || !Equal(v, w) {
				return false
			}
		}
		return true
	case []any:
		y, ok := b.([]any)
		if !ok || len(x) != len(y) {
			return false
		}
		for i := range x {
			if !Equal(x[i], y[i]) {
				return false
			}
		}
		return true
	case string:
		y, ok := b.(string)
		return ok && x == y
	case bool:
		y, ok := b.(bool)
		return ok && x == y
	case nil:
		return b == nil
	}
	return false
}

// TypeName names the JSON type of a value: string, int, double, bool, array, object, null.
func TypeName(v any) string {
	switch t := Normalize(v).(type) {
	case string:
		return "string"
	case bool:
		return "bool"
	case nil:
		return "null"
	case []any:
		return "array"
	case map[string]any:
		return "object"
	case json.Number:
		if _, ok := AsInt(t); ok {
			return "int"
		}
		return "double"
	}
	return "unknown"
}

// Short returns a compact one-line rendering, truncated to n bytes.
func Short(v any, n int) string {
	b := Marshal(v)
	if len(b) > n {
		return string(b[:n]) + "..."
	}
	return string(b)
}
