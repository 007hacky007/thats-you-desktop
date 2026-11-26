package console

import (
	"fmt"
	"regexp"
	"strconv"
	"strings"
)

// MatcherOps lists the matcher keys of scenario expectations (contract 7.4).
var MatcherOps = map[string]bool{"eq": true, "ne": true, "gt": true, "ge": true, "lt": true, "le": true,
	"in": true, "type": true, "nonempty": true, "regex": true, "any": true}

// AsMatcher reports whether v is a matcher object ({"op": arg} with one known op).
func AsMatcher(v any) (string, any, bool) {
	m, ok := Normalize(v).(map[string]any)
	if !ok || len(m) != 1 {
		return "", nil, false
	}
	for k, a := range m {
		if MatcherOps[k] {
			return k, a, true
		}
	}
	return "", nil, false
}

// CheckMatcher validates a matcher's argument shape; "" when fine.
func CheckMatcher(op string, arg any) string {
	switch op {
	case "gt", "ge", "lt", "le":
		if _, ok := AsFloat(Normalize(arg)); !ok {
			if s, ok := arg.(string); !ok || !IsPlaceholder(s) {
				return op + " needs a number"
			}
		}
	case "in":
		if _, ok := Normalize(arg).([]any); !ok {
			return "in needs an array"
		}
	case "type":
		switch arg {
		case "string", "int", "bool", "double", "array", "object":
		default:
			return fmt.Sprintf("unknown type %v", arg)
		}
	case "regex":
		s, ok := arg.(string)
		if !ok {
			return "regex needs a string"
		}
		if _, err := regexp.Compile(s); err != nil {
			return "bad regex: " + err.Error()
		}
	case "nonempty", "any":
		if b, ok := arg.(bool); !ok || !b {
			return op + " needs true"
		}
	}
	return ""
}

// Match compares a received value with an expectation value (literal or matcher).
// It returns "" on a match, else a reason.
func Match(want, got any) string {
	if op, arg, ok := AsMatcher(want); ok {
		if matchOp(op, arg, got) {
			return ""
		}
		return fmt.Sprintf("%s %s: got %s", op, Short(arg, 60), Short(got, 60))
	}
	if Equal(want, got) {
		return ""
	}
	return fmt.Sprintf("want %s, got %s", Short(want, 60), Short(got, 60))
}

func matchOp(op string, arg, got any) bool {
	switch op {
	case "eq", "ne", "gt", "ge", "lt", "le", "in", "nonempty":
		if op == "nonempty" && got == nil {
			return false
		}
		return applyOp(op, got, arg)
	case "any":
		return true
	case "type":
		tn := TypeName(got)
		if arg == "double" {
			return tn == "double" || tn == "int"
		}
		return tn == arg
	case "regex":
		s, ok := got.(string)
		if !ok {
			return false
		}
		re, err := regexp.Compile(arg.(string))
		return err == nil && re.MatchString(s)
	}
	return false
}

var placeholderRe = regexp.MustCompile(`^\$(guid|uid|pid|slot|cap):([A-Za-z0-9_.-]+)$|^\$tid$`)

// IsPlaceholder reports whether s is a whole-string placeholder.
func IsPlaceholder(s string) bool { return placeholderRe.MatchString(s) }

// ParsePlaceholder splits a placeholder into kind and argument.
func ParsePlaceholder(s string) (kind, arg string, ok bool) {
	m := placeholderRe.FindStringSubmatch(s)
	if m == nil {
		return "", "", false
	}
	if s == "$tid" {
		return "tid", "", true
	}
	return m[1], m[2], true
}

// PlaceholderSource resolves one placeholder.
type PlaceholderSource func(kind, arg string) (any, error)

// Substitute replaces whole-string placeholders anywhere inside v.
func Substitute(v any, src PlaceholderSource) (any, error) {
	switch t := v.(type) {
	case string:
		if kind, arg, ok := ParsePlaceholder(t); ok {
			return src(kind, arg)
		}
		return t, nil
	case map[string]any:
		out := make(map[string]any, len(t))
		for k, x := range t {
			r, err := Substitute(x, src)
			if err != nil {
				return nil, err
			}
			out[k] = r
		}
		return out, nil
	case *Obj:
		out := NewObj()
		for _, k := range t.Keys {
			r, err := Substitute(t.Vals[k], src)
			if err != nil {
				return nil, err
			}
			out.Set(k, r)
		}
		return out, nil
	case []any:
		out := make([]any, len(t))
		for i, x := range t {
			r, err := Substitute(x, src)
			if err != nil {
				return nil, err
			}
			out[i] = r
		}
		return out, nil
	}
	return v, nil
}

// PlayerArg parses the player index of $uid/$pid/$slot.
func PlayerArg(arg string) (int, error) {
	n, err := strconv.Atoi(arg)
	if err != nil || n < 0 || n > 15 {
		return 0, fmt.Errorf("bad player %q", arg)
	}
	return n, nil
}

// CheckSource is a placeholder source for static checks: ints for
// pid/slot/tid, strings for guid/uid, Skip for captures.
func CheckSource(images map[string]ImageSpec, players int) PlaceholderSource {
	return func(kind, arg string) (any, error) {
		switch kind {
		case "guid":
			if _, ok := images[arg]; !ok {
				return nil, fmt.Errorf("$guid:%s: unknown image", arg)
			}
			return "00000000-0000-4000-8000-000000000000", nil
		case "uid", "pid", "slot":
			n, err := PlayerArg(arg)
			if err != nil {
				return nil, err
			}
			if n >= players {
				return nil, fmt.Errorf("$%s:%s: only %d players", kind, arg, players)
			}
			if kind == "uid" {
				return "uid-check", nil
			}
			return Normalize(2 + n), nil
		case "tid":
			return Normalize(1000), nil
		case "cap":
			return Skip, nil
		}
		return nil, fmt.Errorf("unknown placeholder $%s", kind)
	}
}

func joinErrs(errs []string) string { return strings.Join(errs, "; ") }
