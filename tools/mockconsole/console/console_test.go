package console

import (
	"context"
	"errors"
	"fmt"
	"net/http/httptest"
	"slices"
	"strings"
	"testing"
	"time"
)

type fakePage struct {
	t   *testing.T
	m   *MemTransport
	sid int32
}

func (p fakePage) open() {
	p.m.Push(Event{Kind: EventOpened, Session: p.sid, Data: []byte("198.18.0.1:5000")})
}
func (p fakePage) text(s string) { p.m.Push(Event{Kind: EventText, Session: p.sid, Data: []byte(s)}) }
func (p fakePage) hello(uid string) {
	p.open()
	p.text(`{"t":"hello","token":"tttttttttttttttt","uid":"` + uid + `"}`)
}
func (p fakePage) msg(js string) { p.text(`{"t":"msg","id":-7503,"json":` + js + `}`) }
func (p fakePage) bin(tid int32, data []byte) {
	p.m.Push(Event{Kind: EventBinary, Session: p.sid, TransferID: tid, Data: data})
}
func (p fakePage) closed()          { p.m.Push(Event{Kind: EventClosed, Session: p.sid, Code: 2}) }
func (p fakePage) labels() []string { return p.m.labelsTo(p.sid) }
func (p fakePage) waitLabel(l string) {
	p.t.Helper()
	waitFor(p.t, l, func() bool { return slices.Contains(p.labels(), l) })
}

func newConsole(t *testing.T, scenario string) (*Console, *MemTransport) {
	t.Helper()
	cat := repoCatalogue(t)
	var sc *Scenario
	if scenario != "" {
		var err error
		sc, err = ParseScenario([]byte(scenario))
		if err != nil {
			t.Fatal(err)
		}
	}
	m := NewMemTransport()
	c, err := New(Options{Transport: m, Catalogue: cat, Scenario: sc, PageURL: "http://127.0.0.1:1/", HeaderTimeout: 300 * time.Millisecond, Logf: t.Logf})
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithCancel(context.Background())
	t.Cleanup(cancel)
	go c.Run(ctx)
	return c, m
}

const reqPID = `{"TypeString":"ClientRequestPlayerIDMessage","UID":"%s"}`

func TestJoinHandshakeAndAssignment(t *testing.T) {
	c, m := newConsole(t, "")
	a := fakePage{t, m, 1}
	a.hello("uid-a")
	a.waitLabel("SessionStateMessage")
	if got := strings.Join(a.labels(), ","); got != "t:joined,InterfaceVersionMessage,SessionStateMessage" {
		t.Fatalf("handshake %s", got)
	}
	a.msg(fmt.Sprintf(reqPID, "uid-a"))
	a.waitLabel("ResourceRequirementsMessage")
	want := "t:joined,InterfaceVersionMessage,SessionStateMessage,AssignPlayerIDAndSlotMessage,SyncJokersQuizStateMessage,ClientOnlineStateMessage,ClientUGCStateMessage,ResourceRequirementsMessage"
	if got := strings.Join(a.labels(), ","); got != want {
		t.Fatalf("burst %s", got)
	}
	// the assignment text is exact
	var assign string
	for _, s := range m.SentTo(1) {
		if strings.Contains(string(s.Text), "AssignPlayerIDAndSlotMessage") {
			assign = string(s.Text)
		}
	}
	if assign != `{"t":"msg","id":-7503,"json":{"TypeString":"AssignPlayerIDAndSlotMessage","PlayerID":2,"SlotID":0,"UDPPortOffset":0,"DisplayName":"","PSNID":""}}` {
		t.Errorf("assign %s", assign)
	}
	b := fakePage{t, m, 2}
	b.hello("uid-b")
	b.waitLabel("SessionStateMessage")
	b.msg(fmt.Sprintf(reqPID, "uid-b"))
	b.waitLabel("AssignPlayerIDAndSlotMessage")
	st := c.State()
	if st.Players[1].PlayerID != 3 || st.Players[1].Slot != 1 {
		t.Errorf("second player %+v", st.Players[1])
	}
	if len(c.CatalogueErrors()) != 0 {
		t.Errorf("errors %v", c.CatalogueErrors())
	}
}

func TestReplacedSessionKeepsPlayerID(t *testing.T) {
	c, m := newConsole(t, "")
	a := fakePage{t, m, 1}
	a.hello("same")
	a.waitLabel("SessionStateMessage")
	a.msg(fmt.Sprintf(reqPID, "same"))
	a.waitLabel("AssignPlayerIDAndSlotMessage")
	b := fakePage{t, m, 2}
	b.hello("same")
	b.waitLabel("SessionStateMessage")
	a.waitLabel("close")
	labels := a.labels()
	if labels[len(labels)-2] != "t:bye" {
		t.Errorf("old session got %v", labels)
	}
	if !strings.Contains(string(m.SentTo(1)[len(labels)-2].Text), `"reason":"replaced"`) {
		t.Error("bye reason")
	}
	b.msg(fmt.Sprintf(reqPID, "same"))
	b.waitLabel("AssignPlayerIDAndSlotMessage")
	st := c.State()
	if len(st.Players) != 1 || st.Players[0].PlayerID != 2 || st.Players[0].Session != 2 {
		t.Errorf("players %+v", st.Players)
	}
}

func TestRefuseAndSlotsFull(t *testing.T) {
	_, m := newConsole(t, `{"schema":1,"name":"r","players":1,"auto":{"refuse":3},"steps":[]}`)
	a := fakePage{t, m, 1}
	a.hello("x")
	a.waitLabel("close")
	if got := strings.Join(a.labels(), ","); got != "t:refused,close" {
		t.Errorf("got %s", got)
	}
	if !strings.Contains(string(m.SentTo(1)[0].Text), `"reason":3`) {
		t.Error("reason")
	}

	_, m = newConsole(t, "")
	for i := 1; i <= 7; i++ {
		p := fakePage{t, m, int32(i)}
		p.hello(fmt.Sprintf("u%d", i))
		if i <= 6 {
			p.waitLabel("SessionStateMessage")
			p.msg(fmt.Sprintf(reqPID, fmt.Sprintf("u%d", i)))
			p.waitLabel("AssignPlayerIDAndSlotMessage")
		} else {
			p.waitLabel("close")
			if !strings.Contains(string(m.SentTo(7)[0].Text), `"reason":2`) {
				t.Errorf("seventh: %s", m.SentTo(7)[0].Text)
			}
		}
	}
}

func TestPending(t *testing.T) {
	_, m := newConsole(t, `{"schema":1,"name":"p","players":1,"auto":{"pending_ms":150},"steps":[]}`)
	a := fakePage{t, m, 1}
	start := time.Now()
	a.hello("x")
	a.waitLabel("t:pending")
	a.waitLabel("SessionStateMessage")
	if time.Since(start) < 140*time.Millisecond {
		t.Error("joined too early")
	}
	if got := strings.Join(a.labels(), ","); got != "t:pending,t:joined,InterfaceVersionMessage,SessionStateMessage" {
		t.Errorf("got %s", got)
	}
}

func TestValidationErrors(t *testing.T) {
	c, m := newConsole(t, "")
	a := fakePage{t, m, 1}
	a.hello("x")
	a.waitLabel("SessionStateMessage")
	a.msg(`{"TypeString":"AnswerIndexResponseMessage","AnswerIndex":"1"}`)
	a.msg(`{"TypeString":"Bogus"}`)
	a.text(`{"t":"msg","id":5,"json":{}}`)
	a.msg(fmt.Sprintf(reqPID, "other"))
	waitFor(t, "errors", func() bool { return len(c.CatalogueErrors()) >= 4 })
	all := strings.Join(c.CatalogueErrors(), "|")
	for _, frag := range []string{"want int", "unknown TypeString Bogus", "msg id 5", "differs from the hello uid"} {
		if !strings.Contains(all, frag) {
			t.Errorf("missing %q in %s", frag, all)
		}
	}
	_, items := c.ReceivedSince(0)
	invalid := 0
	for _, it := range items {
		if !it.Valid {
			invalid++
		}
	}
	if invalid < 4 {
		t.Errorf("invalid items %d", invalid)
	}
}

func TestUploadsPairingAndResources(t *testing.T) {
	c, m := newConsole(t, `{"schema":1,"name":"u","players":1,"images":{"q":{"w":360,"h":480,"color":"#336699","label":"Q"}},"steps":[]}`)
	a := fakePage{t, m, 1}
	a.hello("x")
	a.waitLabel("SessionStateMessage")
	jpg, _ := SynthJPEG(ImageSpec{W: 480, H: 640})
	a.msg(`{"TypeString":"ClientImageResourceContentTransferMessage","TransferID":1001,"ImageGUID":"g-1","ImgType":2}`)
	a.bin(1001, jpg)
	// wrong size for a drawing
	a.msg(`{"TypeString":"ClientImageResourceContentTransferMessage","TransferID":1002,"ImageGUID":"g-2","ImgType":1}`)
	a.bin(1002, jpg)
	// binary without header, header without binary
	a.bin(1003, jpg)
	a.msg(`{"TypeString":"ClientImageResourceContentTransferMessage","TransferID":1004,"ImageGUID":"g-4","ImgType":1}`)
	// not a JPEG
	a.msg(`{"TypeString":"ClientImageResourceContentTransferMessage","TransferID":1005,"ImageGUID":"g-5","ImgType":4}`)
	a.bin(1005, []byte("nope"))
	waitFor(t, "errors", func() bool { return len(c.CatalogueErrors()) >= 4 })
	all := strings.Join(c.CatalogueErrors(), "|")
	for _, frag := range []string{"ImgType 1 image is 480x640", "without a header", "without binary", "missing SOI"} {
		if !strings.Contains(all, frag) {
			t.Errorf("missing %q in %s", frag, all)
		}
	}
	if strings.Contains(all, "1001") {
		t.Errorf("good upload flagged: %s", all)
	}
	// the uploaded portrait and the scenario image are served on request
	a.msg(`{"TypeString":"RequestResourceMessage","ResourceGUID":"g-1"}`)
	g, _ := c.Resolve("$guid:q")
	a.msg(`{"TypeString":"RequestResourceMessage","ResourceGUID":"` + g.(string) + `"}`)
	waitFor(t, "two binaries", func() bool {
		n := 0
		for _, s := range m.SentTo(1) {
			if s.Data != nil {
				n++
			}
		}
		return n == 2
	})
	var bins [][]byte
	for _, s := range m.SentTo(1) {
		if s.Data != nil {
			bins = append(bins, s.Data)
		}
	}
	if string(bins[0]) != string(jpg) {
		t.Error("portrait not echoed")
	}
	if info, err := CheckJPEG(bins[1]); err != nil || info.Width != 360 {
		t.Errorf("scenario image %v %v", info, err)
	}
}

const stepScenario = `{"schema":1,"name":"s","players":1,
 "images":{"co":{"w":480,"h":640,"color":"#cc6633","label":"CO"}},
 "steps":[
  {"id":"join","player":0,"page":[{"do":"tap","target":"connect-play"}],
   "expect":[{"frame":"hello"},{"msg":"ClientRequestPlayerIDMessage","fields":{"UID":"$uid:0"}}]},
  {"id":"photo","player":0,
   "send":[{"msg":{"TypeString":"ClientTopDrawerTakePhotoCommandMessage","action":12,"CommandText":"x {0}","PlayerName":"Bob"}}],
   "expect":[{"msg":"TopDrawerPhotoTakenResponseMessage","fields":{"Response":11}},
             {"upload":{"ImgType":3,"width":480,"height":640},"capture":"td"},
             {"msg":"TrophyPhotoFXInfoMessage","fields":{"ImageGUID":"$cap:td","usesFX":{"type":"bool"}}},
             {"msg":"UsePicPressedResponseMessage","fields":{"Response":4}}],
   "order":"any","timeout_ms":2000},
  {"id":"vote","player":0,
   "send":[{"msg":{"TypeString":"ClientAnswerCommandMessage","action":3,"roundID":1,"questionBarText":"q","ImageGUIDs":["$cap:td","$guid:co"]}}],
   "expect":[{"msg":"AnswerIndexResponseMessage","fields":{"AnswerIndex":{"in":[1,2]}},"capture":{"pick":"AnswerIndex"}},{"none_ms":200}],
   "timeout_ms":2000},
  {"id":"never","player":0,"expect":[{"msg":"ContinuePressedResponseMessage"}],"timeout_ms":300}
 ]}`

func TestStepRunner(t *testing.T) {
	c, m := newConsole(t, stepScenario)
	a := fakePage{t, m, 1}
	if err := c.StartStep(0); err != nil {
		t.Fatal(err)
	}
	if err := c.StartStep(1); err != ErrBusy {
		t.Errorf("second start: %v", err)
	}
	a.hello("u0")
	a.waitLabel("SessionStateMessage")
	a.msg(fmt.Sprintf(reqPID, "u0"))
	r, _ := c.Result(0, 3*time.Second)
	if !r.Pass {
		t.Fatalf("join: %+v", r)
	}

	c.StartStep(1)
	a.waitLabel("ClientTopDrawerTakePhotoCommandMessage")
	jpg, _ := SynthJPEG(ImageSpec{W: 480, H: 640})
	a.msg(`{"TypeString":"ClientImageResourceContentTransferMessage","TransferID":7,"ImageGUID":"td-guid","ImgType":3}`)
	a.bin(7, jpg)
	a.msg(`{"TypeString":"TrophyPhotoFXInfoMessage","ImageGUID":"td-guid","usesFX":true}`)
	a.msg(`{"TypeString":"ContinuePressedResponseMessage","Response":3}`) // extra, recorded
	a.msg(`{"TypeString":"UsePicPressedResponseMessage","Response":4}`)
	a.msg(`{"TypeString":"TopDrawerPhotoTakenResponseMessage","Response":11}`) // any order
	r, _ = c.Result(1, 3*time.Second)
	if !r.Pass {
		t.Fatalf("photo: %+v", r)
	}
	extras := 0
	for _, rc := range r.Received {
		if !rc.Matched {
			extras++
		}
	}
	if extras < 2 { // header msg and the continue
		t.Errorf("extras %d: %+v", extras, r.Received)
	}
	// the command was completed with defaults and in catalogue order
	var cmd string
	for _, s := range m.SentTo(1) {
		if strings.Contains(string(s.Text), "TopDrawerTake") {
			cmd = string(s.Text)
		}
	}
	if !strings.Contains(cmd, `{"TypeString":"ClientTopDrawerTakePhotoCommandMessage","action":12,"time":0.0,"CommandText":"x {0}","PlayerName":"Bob"}`) {
		t.Errorf("cmd %s", cmd)
	}

	c.StartStep(2)
	a.waitLabel("ClientAnswerCommandMessage")
	for _, s := range m.SentTo(1) {
		if strings.Contains(string(s.Text), "ClientAnswerCommandMessage") && !strings.Contains(string(s.Text), `"ImageGUIDs":["td-guid",`) {
			t.Errorf("captures not substituted: %s", s.Text)
		}
	}
	a.msg(`{"TypeString":"AnswerIndexResponseMessage","AnswerIndex":2}`)
	r, _ = c.Result(2, 3*time.Second)
	if !r.Pass {
		t.Fatalf("vote: %+v", r)
	}
	if v, _ := c.Resolve("$cap:pick"); !Equal(v, 2) {
		t.Errorf("capture %v", v)
	}

	c.StartStep(3)
	r, _ = c.Result(3, 3*time.Second)
	if r.Pass || len(r.Errors) != 1 || !strings.Contains(r.Errors[0], "timeout") {
		t.Errorf("never: %+v", r)
	}
	rep := c.Report()
	if rep.Pass || len(rep.Steps) != 4 || !rep.Steps[0].Pass || rep.Steps[3].Pass {
		t.Errorf("report %+v", rep)
	}
}

func TestStrictOrderAndNoneMs(t *testing.T) {
	sc := `{"schema":1,"name":"s","players":1,"steps":[
	 {"id":"join","player":0,"page":[{"do":"tap","target":"connect-play"}],"expect":[{"frame":"hello"},{"msg":"ClientRequestPlayerIDMessage"}]},
	 {"id":"strict","player":0,"expect":[{"msg":"ContinuePressedResponseMessage"},{"msg":"StartGameButtonPressedResponseMessage"}],"timeout_ms":400},
	 {"id":"quiet","player":0,"expect":[{"none_ms":300}],"timeout_ms":2000},
	 {"id":"closed","player":0,"send":[{"frame":{"t":"bye","reason":"closed"}},{"close":true}],"expect":[{"closed":true}],"timeout_ms":1000}
	]}`
	c, m := newConsole(t, sc)
	a := fakePage{t, m, 1}
	c.StartStep(0)
	a.hello("u0")
	a.waitLabel("SessionStateMessage")
	a.msg(fmt.Sprintf(reqPID, "u0"))
	if r, _ := c.Result(0, 3*time.Second); !r.Pass {
		t.Fatal(r)
	}
	c.StartStep(1)
	time.Sleep(30 * time.Millisecond)
	a.msg(`{"TypeString":"StartGameButtonPressedResponseMessage","Response":9}`)
	a.msg(`{"TypeString":"ContinuePressedResponseMessage","Response":3}`)
	r, _ := c.Result(1, 3*time.Second)
	if r.Pass || !strings.Contains(strings.Join(r.Errors, "|"), "StartGameButtonPressedResponseMessage") {
		t.Errorf("strict: %+v", r)
	}
	c.StartStep(2)
	time.Sleep(60 * time.Millisecond)
	a.msg(`{"TypeString":"ContinuePressedResponseMessage","Response":3}`)
	r, _ = c.Result(2, 3*time.Second)
	if r.Pass || !strings.Contains(strings.Join(r.Errors, "|"), "expected nothing") {
		t.Errorf("none_ms violation: %+v", r)
	}
	c.StartStep(2)
	r, _ = c.Result(2, 3*time.Second)
	if !r.Pass {
		t.Errorf("none_ms quiet: %+v", r)
	}
	c.StartStep(3)
	r, _ = c.Result(3, 3*time.Second)
	if !r.Pass {
		t.Errorf("closed: %+v", r)
	}
	if got := a.labels(); got[len(got)-2] != "t:bye" || got[len(got)-1] != "close" {
		t.Errorf("labels %v", got)
	}
}

func TestStepHookAndSendErrors(t *testing.T) {
	sc := `{"schema":1,"name":"s","players":1,"steps":[
	 {"id":"nosession","player":0,"send":[{"msg":{"TypeString":"ClientQuizCommandMessage","action":4}}],"expect":[],"timeout_ms":200}]}`
	c, _ := newConsole(t, sc)
	got := make(chan int, 1)
	c.OnStepReady(func(i int) { got <- i })
	c.StartStep(0)
	r, _ := c.Result(0, 2*time.Second)
	if r.Pass || !strings.Contains(strings.Join(r.Errors, "|"), "has not connected") {
		t.Errorf("result %+v", r)
	}
	select {
	case i := <-got:
		if i != 0 {
			t.Error(i)
		}
	case <-time.After(time.Second):
		t.Error("hook not called")
	}
}

const pageActionsScenario = `{"schema":1,"name":"s","players":1,"steps":[
 {"id":"join","player":0,"page":[{"do":"tap","target":"connect-play"}],"expect":[{"frame":"hello"},{"msg":"ClientRequestPlayerIDMessage"}]},
 {"id":"quiet","player":0,"expect":[{"none_ms":300}],"timeout_ms":5000},
 {"id":"msg-then-quiet","player":0,"expect":[{"msg":"ContinuePressedResponseMessage"},{"none_ms":300}],"timeout_ms":5000},
 {"id":"any","player":0,"order":"any","expect":[{"none_ms":300},{"msg":"ContinuePressedResponseMessage"}],"timeout_ms":5000},
 {"id":"short","player":0,"expect":[{"none_ms":200}],"timeout_ms":300}
]}`

const continueMsg = `{"TypeString":"ContinuePressedResponseMessage","Response":3}`

func joinedPage(t *testing.T, c *Console, m *MemTransport) fakePage {
	t.Helper()
	a := fakePage{t, m, 1}
	c.StartStep(0)
	a.hello("u0")
	a.waitLabel("SessionStateMessage")
	a.msg(fmt.Sprintf(reqPID, "u0"))
	if r, _ := c.Result(0, 3*time.Second); !r.Pass {
		t.Fatal(r)
	}
	return a
}

func TestPageActionsStrictNoneWaitsForPageDone(t *testing.T) {
	c, m := newConsole(t, pageActionsScenario)
	joinedPage(t, c, m)
	if err := c.StartStepWith(1, StartOptions{PageActions: true}); err != nil {
		t.Fatal(err)
	}
	time.Sleep(500 * time.Millisecond) // longer than none_ms: quiet, but no page-done yet
	if r, _ := c.Result(1, 0); r.Done {
		t.Fatalf("finished before page-done: %+v", r)
	}
	t0 := time.Now()
	if running, err := c.PageDone(1); err != nil || !running {
		t.Fatalf("page-done %v %v", running, err)
	}
	r, _ := c.Result(1, 3*time.Second)
	if !r.Pass {
		t.Fatalf("quiet after page-done: %+v", r)
	}
	if d := time.Since(t0); d < 280*time.Millisecond {
		t.Errorf("none window ended %v after page-done", d)
	}
	if running, err := c.PageDone(1); err != nil || running {
		t.Errorf("page-done after finish: %v %v", running, err)
	}
	if _, err := c.PageDone(2); !errors.Is(err, ErrNotRunning) {
		t.Errorf("page-done of another step: %v", err)
	}
	if _, err := c.PageDone(99); err == nil {
		t.Error("page-done of a missing step")
	}
}

// A message the page sends during its own page actions (before page-done)
// is still a violation of an active none item.
func TestPageActionsViolationBeforePageDone(t *testing.T) {
	c, m := newConsole(t, pageActionsScenario)
	a := joinedPage(t, c, m)
	for _, step := range []int{1, 3} { // strict none, any-order none
		if err := c.StartStepWith(step, StartOptions{PageActions: true}); err != nil {
			t.Fatal(err)
		}
		time.Sleep(50 * time.Millisecond)
		if step == 3 {
			a.msg(continueMsg) // the expected message
		}
		a.msg(`{"TypeString":"StartGameButtonPressedResponseMessage","Response":9}`)
		time.Sleep(50 * time.Millisecond)
		c.PageDone(step)
		r, _ := c.Result(step, 3*time.Second)
		if r.Pass || !strings.Contains(strings.Join(r.Errors, "|"), "expected nothing") {
			t.Errorf("step %d: message before page-done not a violation: %+v", step, r)
		}
	}
	// activity after page-done is a violation too
	c.StartStepWith(1, StartOptions{PageActions: true})
	time.Sleep(50 * time.Millisecond)
	c.PageDone(1)
	time.Sleep(50 * time.Millisecond)
	a.msg(continueMsg)
	r, _ := c.Result(1, 3*time.Second)
	if r.Pass || !strings.Contains(strings.Join(r.Errors, "|"), "expected nothing") {
		t.Errorf("violation after page-done: %+v", r)
	}
}

func TestPageActionsMsgThenNone(t *testing.T) {
	c, m := newConsole(t, pageActionsScenario)
	a := joinedPage(t, c, m)
	c.StartStepWith(2, StartOptions{PageActions: true})
	time.Sleep(50 * time.Millisecond)
	a.msg(continueMsg)
	time.Sleep(500 * time.Millisecond)
	if r, _ := c.Result(2, 0); r.Done {
		t.Fatalf("finished before page-done: %+v", r)
	}
	c.PageDone(2)
	if r, _ := c.Result(2, 3*time.Second); !r.Pass {
		t.Fatalf("%+v", r)
	}
	// a second message after the expected one, still during page actions
	c.StartStepWith(2, StartOptions{PageActions: true})
	time.Sleep(50 * time.Millisecond)
	a.msg(continueMsg)
	a.msg(continueMsg)
	time.Sleep(50 * time.Millisecond)
	c.PageDone(2)
	if r, _ := c.Result(2, 3*time.Second); r.Pass {
		t.Fatalf("duplicate during page actions passed: %+v", r)
	}
}

func TestPageActionsAnyOrder(t *testing.T) {
	c, m := newConsole(t, pageActionsScenario)
	a := joinedPage(t, c, m)
	c.StartStepWith(3, StartOptions{PageActions: true})
	time.Sleep(50 * time.Millisecond)
	a.msg(continueMsg)
	time.Sleep(400 * time.Millisecond)
	if r, _ := c.Result(3, 0); r.Done {
		t.Fatalf("finished before page-done: %+v", r)
	}
	c.PageDone(3)
	if r, _ := c.Result(3, 3*time.Second); !r.Pass {
		t.Fatalf("%+v", r)
	}
	// without the flag the window starts at once and the step ends on its own
	t0 := time.Now()
	c.StartStep(3)
	time.Sleep(50 * time.Millisecond)
	a.msg(continueMsg)
	if r, _ := c.Result(3, 3*time.Second); !r.Pass {
		t.Fatalf("unflagged run: %+v", r)
	}
	if d := time.Since(t0); d > 1500*time.Millisecond {
		t.Errorf("unflagged run took %v", d)
	}
}

// Without page-done the step fails at its timeout, even when it was quiet.
func TestPageActionsNoPageDoneFails(t *testing.T) {
	c, m := newConsole(t, pageActionsScenario)
	joinedPage(t, c, m)
	t0 := time.Now()
	c.StartStepWith(4, StartOptions{PageActions: true})
	r, _ := c.Result(4, 3*time.Second)
	if r.Pass || !strings.Contains(strings.Join(r.Errors, "|"), "page-done never arrived") {
		t.Fatalf("%+v", r)
	}
	if d := time.Since(t0); d < 280*time.Millisecond {
		t.Errorf("finished after %v, want the 300 ms timeout", d)
	}
	// unflagged, the same quiet step passes
	c.StartStep(4)
	if r, _ := c.Result(4, 3*time.Second); !r.Pass {
		t.Fatalf("unflagged: %+v", r)
	}
}

func TestHTTPPageActions(t *testing.T) {
	c, m := newConsole(t, pageActionsScenario)
	joinedPage(t, c, m)
	srv := httptest.NewServer(c.Handler(t.TempDir()))
	defer srv.Close()
	var out map[string]any
	if code := postJSON(t, srv.URL+"/api/steps/1/start", `{"page_actions":"yes"}`, &out); code != 400 {
		t.Errorf("bad flag %d", code)
	}
	if code := postJSON(t, srv.URL+"/api/steps/1/start", `{"other":true}`, &out); code != 400 {
		t.Errorf("unknown key %d", code)
	}
	if code := postJSON(t, srv.URL+"/api/steps/1/start", `[1]`, &out); code != 400 {
		t.Errorf("not an object %d", code)
	}
	if code := postJSON(t, srv.URL+"/api/steps/1/start", `{"page_actions":true}`, &out); code != 200 {
		t.Fatalf("start %d %v", code, out)
	}
	time.Sleep(400 * time.Millisecond)
	var res StepResult
	getJSON(t, srv.URL+"/api/steps/1/result", &res)
	if res.Done {
		t.Fatalf("finished before page-done: %+v", res)
	}
	if code := postJSON(t, srv.URL+"/api/steps/2/page-done", ``, &out); code != 409 {
		t.Errorf("page-done other step %d", code)
	}
	if code := postJSON(t, srv.URL+"/api/steps/99/page-done", ``, &out); code != 400 {
		t.Errorf("page-done missing step %d", code)
	}
	out = nil
	if code := postJSON(t, srv.URL+"/api/steps/1/page-done", `{}`, &out); code != 200 || out["ok"] != true || out["running"] != true {
		t.Fatalf("page-done %d %v", code, out)
	}
	getJSON(t, srv.URL+"/api/steps/1/result?timeout_ms=3000", &res)
	if !res.Pass {
		t.Fatalf("%+v", res)
	}
	out = nil
	if code := postJSON(t, srv.URL+"/api/steps/1/page-done", ``, &out); code != 200 || out["running"] != false {
		t.Errorf("late page-done %d %v", code, out)
	}
	// {} and an empty body keep the old behaviour
	if code := postJSON(t, srv.URL+"/api/steps/1/start", `{}`, &out); code != 200 {
		t.Fatalf("start {} %d", code)
	}
	getJSON(t, srv.URL+"/api/steps/1/result?timeout_ms=3000", &res)
	if !res.Pass {
		t.Fatalf("unflagged quiet: %+v", res)
	}
}
