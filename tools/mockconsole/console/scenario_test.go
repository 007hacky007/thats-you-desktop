package console

import (
	"os"
	"strings"
	"testing"
)

func writeFile(p, s string) error { return os.WriteFile(p, []byte(s), 0o644) }

func TestCheckValidScenario(t *testing.T) {
	cat := repoCatalogue(t)
	sc, err := ParseScenario([]byte(stepScenario))
	if err != nil {
		t.Fatal(err)
	}
	if errs := sc.Check(cat); len(errs) > 0 {
		t.Errorf("errors: %v", errs)
	}
}

func TestCheckFindsErrors(t *testing.T) {
	cat := repoCatalogue(t)
	bad := `{"schema":1,"name":"b","players":1,
	 "images":{"x":{"w":0,"h":10,"color":"blue"}},
	 "steps":[
	  {"id":"a","player":0,"page":[{"do":"tap","target":"nope"},{"do":"fly"}],"expect":[{"msg":"ClientRequestPlayerIDMessage"}],"screen":"zzz"},
	  {"id":"a","player":2,"order":"random",
	   "send":[{"msg":{"TypeString":"ClientHoldingScreenCommandMessage","action":5,"HoldingScreenText":"x","HoldingScreenType":3}},
	           {"msg":{"TypeString":"Nope"}},
	           {"msg":{"TypeString":"ClientQuizCommandMessage","action":4,"bogus":1}},
	           {"resource":"missing"},
	           {"content":{"header":{"TypeString":"ClientQuizCommandMessage","action":4},"image":"x"}},
	           {"manifest":[{"r":5}]},
	           {"msg":{"TypeString":"SessionStateMessage","SessionID":"$pid:4"}}],
	   "expect":[{"msg":"AssignPlayerIDAndSlotMessage"},
	             {"msg":"AnswerIndexResponseMessage","fields":{"AnswerIndex":"one","Nope":1}},
	             {"msg":"AnswerIndexResponseMessage","fields":{"AnswerIndex":{"type":"float"}},"capture":{"c":"Missing"}},
	             {"upload":{"ImgType":9}},
	             {"frame":"joined"},
	             {"weird":1}],
	   "variant":"x"}
	 ]}`
	sc, err := ParseScenario([]byte(bad))
	if err != nil {
		t.Fatal(err)
	}
	all := strings.Join(sc.Check(cat), "\n")
	for _, frag := range []string{
		"bad size", "#rrggbb", "unknown testid", "unknown page action", "unknown screen",
		"duplicate id", "player 2 out of range", `order "random"`, "OtherPlayerIndex",
		"unknown TypeString Nope", "has no field bogus", "unknown image missing", "is not a content header",
		"manifest entry needs image or guid", "manifest r must be 0..2", "only 1 players",
		"is a game -> phone message", "want int", "has no field Nope", "unknown type float",
		"capture c", "ImgType must be 0..5", "only {\"frame\": \"hello\"}", "unknown expect item",
		"variant without screen", "must tap connect-play",
	} {
		if !strings.Contains(all, frag) {
			t.Errorf("missing %q", frag)
		}
	}
}

func TestParseScenarioUnknownKeys(t *testing.T) {
	if _, err := ParseScenario([]byte(`{"schema":1,"name":"x","players":1,"steps":[],"extra":1}`)); err == nil {
		t.Error("unknown top-level key accepted")
	}
	if _, err := ParseScenario([]byte(`{"schema":1,"name":"x","players":1,"steps":[{"id":"a","player":0,"sned":[]}]}`)); err == nil {
		t.Error("unknown step key accepted")
	}
}

func TestValidTestIDs(t *testing.T) {
	for _, id := range []string{"connect-play", "card-12", "effect-11", "colour-14", "size-3", "answer-6", "popup-reconnect", "start", "draw-canvas"} {
		if !ValidTestID(id) {
			t.Errorf("%s rejected", id)
		}
	}
	for _, id := range []string{"card-13", "effect-12", "colour-0", "size-4", "answer-0", "connect"} {
		if ValidTestID(id) {
			t.Errorf("%s accepted", id)
		}
	}
}
