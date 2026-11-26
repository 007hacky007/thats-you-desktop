package console

import (
	"errors"
	"testing"
)

func TestMatch(t *testing.T) {
	cases := []struct {
		want string
		got  string
		ok   bool
	}{
		{`5`, `5`, true}, {`5`, `5.0`, true}, {`5`, `6`, false}, {`"a"`, `"a"`, true},
		{`{"eq":4}`, `4`, true}, {`{"eq":4}`, `"4"`, false}, {`{"ne":4}`, `5`, true},
		{`{"gt":0}`, `1`, true}, {`{"gt":0}`, `0`, false}, {`{"ge":-2}`, `-2`, true},
		{`{"lt":3}`, `2`, true}, {`{"le":3}`, `4`, false}, {`{"in":[1,2]}`, `2`, true},
		{`{"in":[1,2]}`, `3`, false}, {`{"type":"string"}`, `"x"`, true}, {`{"type":"int"}`, `1.5`, false},
		{`{"type":"double"}`, `1`, true}, {`{"type":"array"}`, `[]`, true}, {`{"type":"object"}`, `{}`, true},
		{`{"type":"bool"}`, `false`, true}, {`{"nonempty":true}`, `""`, false}, {`{"nonempty":true}`, `"x"`, true},
		{`{"regex":"^[0-9a-f-]{36}$"}`, `"0b5c3c5e-9d3c-4c1c-8a8a-0123456789ab"`, true},
		{`{"regex":"^x"}`, `5`, false}, {`{"any":true}`, `null`, true},
		{`{"a":1,"b":2}`, `{"b":2,"a":1}`, true}, {`[1,"x"]`, `[1,"x"]`, true}, {`[1]`, `[1,2]`, false},
	}
	for _, c := range cases {
		w, _ := Decode([]byte(c.want))
		g, _ := Decode([]byte(c.got))
		if r := Match(w, g); (r == "") != c.ok {
			t.Errorf("Match(%s, %s) = %q", c.want, c.got, r)
		}
	}
}

func TestCheckMatcher(t *testing.T) {
	bad := []string{`{"gt":"x"}`, `{"in":3}`, `{"type":"float"}`, `{"regex":"("}`, `{"any":false}`}
	for _, b := range bad {
		v, _ := Decode([]byte(b))
		op, arg, ok := AsMatcher(v)
		if !ok || CheckMatcher(op, arg) == "" {
			t.Errorf("%s accepted", b)
		}
	}
	v, _ := Decode([]byte(`{"gt":"$pid:0"}`))
	op, arg, _ := AsMatcher(v)
	if e := CheckMatcher(op, arg); e != "" {
		t.Error(e)
	}
}

func TestSubstitute(t *testing.T) {
	v, _ := Decode([]byte(`{"a":"$guid:co1","b":["$uid:0","$pid:1","$tid"],"c":"$slot:0x","d":"keep $uid:0","e":{"f":"$cap:x"}}`))
	r, err := Substitute(v, func(kind, arg string) (any, error) {
		return kind + "/" + arg, nil
	})
	if err != nil {
		t.Fatal(err)
	}
	want, _ := Decode([]byte(`{"a":"guid/co1","b":["uid/0","pid/1","tid/"],"c":"slot/0x","d":"keep $uid:0","e":{"f":"cap/x"}}`))
	if !Equal(r, want) {
		t.Errorf("got %s", Marshal(r))
	}
	_, err = Substitute(v, func(kind, arg string) (any, error) { return nil, errors.New("nope") })
	if err == nil {
		t.Error("error not propagated")
	}
	if IsPlaceholder("$foo:1") || !IsPlaceholder("$tid") || IsPlaceholder("$tid2") {
		t.Error("IsPlaceholder")
	}
}
