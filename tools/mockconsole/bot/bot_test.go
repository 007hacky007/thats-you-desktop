package bot

import (
	"path/filepath"
	"runtime"
	"testing"

	"mockconsole/console"
)

func cat(t *testing.T) *console.Catalogue {
	_, file, _, _ := runtime.Caller(0)
	c, err := console.LoadCatalogue(filepath.Join(filepath.Dir(file), "..", "..", "..", "protocol", "messages.json"))
	if err != nil {
		t.Fatal(err)
	}
	return c
}

func TestSynthSatisfiesMatchers(t *testing.T) {
	c := cat(t)
	f := func(typ string) console.Field { return console.Field{Name: "x", Type: typ} }
	cases := []struct {
		field console.Field
		want  string
	}{
		{f("int"), `5`}, {f("int"), `{"eq":7}`}, {f("int"), `{"ne":0}`}, {f("int"), `{"gt":3}`},
		{f("int"), `{"ge":-2}`}, {f("int"), `{"lt":-4}`}, {f("int"), `{"le":0}`}, {f("int"), `{"in":[4,5]}`},
		{f("string"), `{"nonempty":true}`}, {f("string"), `{"type":"string"}`}, {f("bool"), `{"ne":false}`},
		{f("string"), `{"ne":""}`}, {f("string[]"), `{"nonempty":true}`}, {f("double"), `{"type":"double"}`},
		{f("int"), `{"any":true}`}, {f("object:UGCInfo"), `{"type":"object"}`},
	}
	for _, tc := range cases {
		w, _ := console.Decode([]byte(tc.want))
		got := synth(c, tc.field, w)
		if r := console.Match(w, got); r != "" {
			t.Errorf("%s: synthesized %s: %s", tc.want, console.Marshal(got), r)
		}
	}
}

func TestRequiredOf(t *testing.T) {
	m, _ := console.Decode([]byte(`[{"GUID":"a","Rqrmnt":1},{"GUID":"b","Rqrmnt":2},{"GUID":"c","Rqrmnt":1}]`))
	got := string(console.Marshal(requiredOf(m.([]any))))
	if got != `[{"GUID":"a","Rqrmnt":1},{"GUID":"c","Rqrmnt":1}]` {
		t.Error(got)
	}
	if string(console.Marshal(requiredOf(nil))) != `[]` {
		t.Error("empty manifest")
	}
}
