package main

import (
	"bytes"
	"context"
	"encoding/json"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"testing"
	"time"

	"iwyknet/server"

	"mockconsole/bot"
	"mockconsole/console"
)

func scenarioFiles(t *testing.T) []string {
	t.Helper()
	files, err := filepath.Glob("scenarios/*.json")
	if err != nil || len(files) == 0 {
		t.Fatalf("no scenario files: %v", err)
	}
	return files
}

func loadCat(t *testing.T) *console.Catalogue {
	t.Helper()
	p, err := console.FindCatalogue(".")
	if err != nil {
		t.Fatal(err)
	}
	cat, err := console.LoadCatalogue(p)
	if err != nil {
		t.Fatal(err)
	}
	return cat
}

var requiredScenarios = []string{"join", "profile", "thats-you", "would-you-rather", "blank-you", "top-drawer",
	"canvas-opinion", "mimic-this", "quickdraw", "pause-joker", "end-of-game", "rejoin-reload", "reconnect",
	"replaced", "refusals", "pending", "disconnect-reasons"}

func TestCheckAllScenarios(t *testing.T) {
	files := scenarioFiles(t)
	var out, errb bytes.Buffer
	if code := run(context.Background(), append([]string{"-check"}, files...), &out, &errb); code != 0 {
		t.Fatalf("-check exit %d\n%s", code, errb.String())
	}
	names := map[string]bool{}
	for _, f := range files {
		sc, err := console.LoadScenario(f)
		if err != nil {
			t.Fatal(err)
		}
		if sc.Name+".json" != filepath.Base(f) {
			t.Errorf("%s: name %q does not match the file name", f, sc.Name)
		}
		names[sc.Name] = true
	}
	for _, n := range requiredScenarios {
		if !names[n] {
			t.Errorf("missing scenario %s", n)
		}
	}
}

func TestCheckRejectsBadFile(t *testing.T) {
	p := filepath.Join(t.TempDir(), "bad.json")
	os.WriteFile(p, []byte(`{"schema":1,"name":"bad","players":1,"steps":[{"id":"a","player":0,"expect":[{"msg":"Nope"}]}]}`), 0o644)
	var out, errb bytes.Buffer
	if code := run(context.Background(), []string{"-check", p}, &out, &errb); code != 1 {
		t.Errorf("exit %d", code)
	}
	if !strings.Contains(errb.String(), "unknown TypeString Nope") {
		t.Errorf("stderr %s", errb.String())
	}
}

// runBotScenario plays one scenario with the Go bot over 127.0.0.1.
func runBotScenario(t *testing.T, cat *console.Catalogue, sc *console.Scenario) console.Report {
	t.Helper()
	srv, err := server.Start(server.Config{UDPPort: 0, LANIP: "127.0.0.1", CertPath: filepath.Join(t.TempDir(), "dtls.pem")})
	if err != nil {
		t.Fatalf("server: %v", err)
	}
	defer srv.Stop()
	ip, port := srv.Endpoint()
	c, err := console.New(console.Options{Transport: serverTransport{srv}, Catalogue: cat, Scenario: sc,
		PageURL: "http://127.0.0.1:8088/", IP: ip, UDP: port, Logf: t.Logf})
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 4*time.Minute)
	defer cancel()
	go c.Run(ctx)
	b, err := bot.Attach(ctx, c, c.JoinURL(), t.Logf)
	if err != nil {
		t.Fatal(err)
	}
	defer b.Close()
	return c.RunAll(ctx)
}

func TestBotPlaysEveryScenario(t *testing.T) {
	if testing.Short() {
		t.Skip("bot scenarios take a while")
	}
	cat := loadCat(t)
	for _, f := range scenarioFiles(t) {
		sc, err := console.LoadScenario(f)
		if err != nil {
			t.Fatal(err)
		}
		t.Run(sc.Name, func(t *testing.T) {
			if !sc.BotPlayable() {
				t.Skipf("%s is marked bot false (needs a real browser)", sc.Name)
			}
			t.Parallel()
			rep := runBotScenario(t, cat, sc)
			if !rep.Pass {
				for _, st := range rep.Steps {
					if !st.Pass {
						t.Errorf("step %d %s: %v", st.Index, st.ID, st.Errors)
					}
				}
				for _, e := range rep.CatalogueErrors {
					t.Errorf("catalogue: %s", e)
				}
			}
		})
	}
}

func TestCLIAutoBotReadyLineAndReport(t *testing.T) {
	dir := t.TempDir()
	rp := filepath.Join(dir, "report.json")
	var out, errb bytes.Buffer
	code := run(context.Background(), []string{"-auto", "-bot", "-ip", "127.0.0.1", "-udp", "0", "-http", "127.0.0.1:0",
		"-page", "http://127.0.0.1:8088/", "-cert", filepath.Join(dir, "dtls.pem"), "-scenario", "scenarios/join.json", "-report", rp}, &out, &errb)
	if code != 0 {
		t.Fatalf("exit %d\n%s", code, errb.String())
	}
	line, _, _ := strings.Cut(out.String(), "\n")
	var ready struct {
		Ready   bool   `json:"ready"`
		HTTP    string `json:"http"`
		UDP     int    `json:"udp"`
		JoinURL string `json:"join_url"`
	}
	if err := json.Unmarshal([]byte(line), &ready); err != nil {
		t.Fatalf("ready line %q: %v", line, err)
	}
	if !ready.Ready || !strings.HasPrefix(ready.HTTP, "127.0.0.1:") || ready.UDP <= 0 || !strings.HasPrefix(ready.JoinURL, "http://127.0.0.1:8088/#h=127.0.0.1&p=") {
		t.Errorf("ready %+v", ready)
	}
	var rep console.Report
	b, err := os.ReadFile(rp)
	if err != nil {
		t.Fatal(err)
	}
	if err := json.Unmarshal(b, &rep); err != nil {
		t.Fatal(err)
	}
	if !rep.Pass || rep.Scenario != "join" || len(rep.Steps) != 3 {
		t.Errorf("report %s", b)
	}
}

func TestCLIBotSkipsNonBotScenario(t *testing.T) {
	var out, errb bytes.Buffer
	code := run(context.Background(), []string{"-auto", "-bot", "-scenario", "scenarios/replaced.json"}, &out, &errb)
	if code != 0 || !strings.Contains(errb.String(), "bot false") {
		t.Errorf("exit %d: %s", code, errb.String())
	}
}

// sendAction returns the action of the step's first ClientQuizCommandMessage or
// ClientStartDrawingCommandMessage send, or -1.
func sendAction(st *console.Step) int {
	for _, s := range st.Send {
		m, _ := s["msg"].(map[string]any)
		switch m["TypeString"] {
		case "ClientQuizCommandMessage", "ClientStartDrawingCommandMessage":
			if n, ok := console.AsInt(m["action"]); ok {
				return int(n)
			}
		}
	}
	return -1
}

func tapsTarget(st *console.Step, target string) bool {
	for _, a := range st.Page {
		if a["do"] == "tap" && a["target"] == target {
			return true
		}
	}
	return false
}

// drawingUpload reports whether the step expects a drawing upload (an upload
// next to TrophyDrawingColouringInfoMessage, or one of ImgType 1).
func drawingUpload(t *testing.T, where string, st *console.Step) bool {
	var ups []*console.Expect
	info := false
	for _, m := range st.Expect {
		e, err := console.ParseExpect(m)
		if err != nil {
			t.Fatalf("%s: %v", where, err)
		}
		switch {
		case e.Kind == "upload":
			ups = append(ups, e)
		case e.Kind == "msg" && e.Msg == "TrophyDrawingColouringInfoMessage":
			info = true
		}
	}
	drawing := info
	for _, e := range ups {
		if e.ImgType == 1 {
			drawing = true
		}
	}
	if !drawing {
		return false
	}
	if len(ups) != 1 || !info {
		t.Errorf("%s: a drawing step expects one upload and TrophyDrawingColouringInfoMessage", where)
		return true
	}
	e := ups[0]
	if (e.ImgType != 1 && e.ImgType != 5) || e.Width != 384 || e.Height != 512 || e.MaxBytes != 128000 || e.CaptureName == "" {
		t.Errorf("%s: drawing upload must be ImgType 1 or 5, 384x512, maxBytes 128000, captured: %+v", where, e)
	}
	return true
}

// TestDrawStepSemantics: the page uploads a drawing when the player confirms
// it; SendDrawing (19) / SendQuickDrawDrawing (28) only force out a drawing
// that was not confirmed (phone-app.md, DRAWING 7).
func TestDrawStepSemantics(t *testing.T) {
	forced := 0
	for _, f := range scenarioFiles(t) {
		sc, err := console.LoadScenario(f)
		if err != nil {
			t.Fatal(err)
		}
		drawing, confirmed := false, false
		for i := range sc.Steps {
			st := &sc.Steps[i]
			where := sc.Name + "/" + st.ID
			act := sendAction(st)
			if act == 7 || act == 27 {
				drawing, confirmed = true, false
			}
			up := drawingUpload(t, where, st)
			if tapsTarget(st, "draw-confirm") {
				if !drawing {
					t.Errorf("%s: draw-confirm outside a drawing", where)
				}
				if !up {
					t.Errorf("%s: draw-confirm must expect the drawing upload", where)
				}
				confirmed = true
				continue
			}
			if act == 19 || act == 28 {
				if st.Screen != "holding" || st.Variant != "look-at-tv" {
					t.Errorf("%s: send drawing must end on holding:look-at-tv", where)
				}
				if confirmed && up {
					t.Errorf("%s: drawing already confirmed, the send must not expect a second upload", where)
				}
				if confirmed {
					if len(st.Expect) != 1 || st.Expect[0]["none_ms"] == nil {
						t.Errorf("%s: after a confirmed drawing the send expects only none_ms", where)
					}
				}
				if drawing && !confirmed {
					if !up {
						t.Errorf("%s: forced send must expect the drawing upload", where)
					}
					forced++
				}
				drawing, confirmed = false, false
				continue
			}
			if up {
				t.Errorf("%s: drawing upload outside a confirm or send step", where)
			}
		}
	}
	if forced == 0 {
		t.Error("no scenario forces out an unconfirmed drawing")
	}
}

func TestProfileExercisesCameraEffects(t *testing.T) {
	sc, err := console.LoadScenario("scenarios/profile.json")
	if err != nil {
		t.Fatal(err)
	}
	var st *console.Step
	for i := range sc.Steps {
		if sc.Steps[i].ID == "selfie" {
			st = &sc.Steps[i]
		}
	}
	if st == nil {
		t.Fatal("no selfie step")
	}
	next := 0
	for k := 0; k < len(st.Page); k++ {
		a := st.Page[k]
		if a["target"] == "shutter" {
			break
		}
		if a["do"] != "tap" || a["target"] != "effects" {
			continue
		}
		if k+3 >= len(st.Page) {
			t.Fatalf("effect %d: sequence cut short", next)
		}
		pick, wait, shot := st.Page[k+1], st.Page[k+2], st.Page[k+3]
		want := "effect-" + strconv.Itoa(next)
		ms, _ := console.AsInt(wait["ms"])
		if pick["do"] != "tap" || pick["target"] != want || wait["do"] != "wait" || ms < 1000 || shot["do"] != "screenshot" || shot["name"] != want {
			t.Fatalf("effect %d: got %v %v %v", next, pick, wait, shot)
		}
		next++
		k += 3
	}
	if next != 12 {
		t.Errorf("effects exercised before the shutter: %d, want 12", next)
	}
}
