package console

import (
	"bytes"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"
)

func getJSON(t *testing.T, url string, v any) int {
	t.Helper()
	resp, err := http.Get(url)
	if err != nil {
		t.Fatal(err)
	}
	defer resp.Body.Close()
	if err := json.NewDecoder(resp.Body).Decode(v); err != nil {
		t.Fatalf("%s: %v", url, err)
	}
	return resp.StatusCode
}

func postJSON(t *testing.T, url, body string, v any) int {
	t.Helper()
	resp, err := http.Post(url, "application/json", strings.NewReader(body))
	if err != nil {
		t.Fatal(err)
	}
	defer resp.Body.Close()
	if v != nil {
		json.NewDecoder(resp.Body).Decode(v)
	}
	return resp.StatusCode
}

func TestHTTPAPI(t *testing.T) {
	c, m := newConsole(t, stepScenario)
	dir := t.TempDir()
	srv := httptest.NewServer(c.Handler(dir))
	defer srv.Close()

	var info Info
	getJSON(t, srv.URL+"/api/info", &info)
	if info.Scenario == nil || *info.Scenario != "s" || len(info.Steps) != 4 || info.Players != 1 || !strings.HasPrefix(info.JoinURL, "http://127.0.0.1:1/#h=") {
		t.Errorf("info %+v", info)
	}
	var ok map[string]any
	if code := postJSON(t, srv.URL+"/api/steps/0/start", "", &ok); code != 200 || ok["ok"] != true {
		t.Fatalf("start %d %v", code, ok)
	}
	if code := postJSON(t, srv.URL+"/api/steps/1/start", "", &ok); code != 409 {
		t.Errorf("busy start %d", code)
	}
	if code := postJSON(t, srv.URL+"/api/steps/99/start", "", &ok); code != 400 {
		t.Errorf("bad index %d", code)
	}
	a := fakePage{t, m, 1}
	a.hello("u0")
	a.waitLabel("SessionStateMessage")
	var st StateView
	getJSON(t, srv.URL+"/api/state", &st)
	if len(st.Sessions) != 1 || st.Sessions[0].UID != "u0" || st.Sessions[0].Player != 0 || st.Step.Index != 0 || st.Step.State != "waiting" {
		t.Errorf("state %+v", st)
	}
	a.msg(fmt.Sprintf(reqPID, "u0"))
	var res StepResult
	getJSON(t, srv.URL+"/api/steps/0/result?timeout_ms=3000", &res)
	if !res.Pass || res.ID != "join" || len(res.Received) < 2 {
		t.Errorf("result %+v", res)
	}
	getJSON(t, srv.URL+"/api/state", &st)
	if st.Step.State != "passed" || st.Sessions[0].PlayerID != 2 || st.Sessions[0].Slot != 0 {
		t.Errorf("state after %+v", st)
	}

	var rec struct {
		Next  int64            `json:"next"`
		Items []map[string]any `json:"items"`
	}
	getJSON(t, srv.URL+"/api/received?since=0", &rec)
	if rec.Next < 3 || len(rec.Items) < 3 || rec.Items[0]["kind"] != "open" || rec.Items[1]["kind"] != "text" {
		t.Errorf("received %+v", rec)
	}
	getJSON(t, srv.URL+fmt.Sprintf("/api/received?since=%d", rec.Next), &rec)
	if len(rec.Items) != 0 {
		t.Errorf("since next: %v", rec.Items)
	}

	if code := postJSON(t, srv.URL+"/api/send", `{"player":0,"msg":{"TypeString":"ClientQuizCommandMessage","action":29}}`, &ok); code != 200 {
		t.Errorf("send %d %v", code, ok)
	}
	a.waitLabel("ClientQuizCommandMessage")
	if code := postJSON(t, srv.URL+"/api/send", `{"player":0,"frame":{"t":"bye","reason":"shutdown"}}`, &ok); code != 200 {
		t.Errorf("send frame %d", code)
	}
	a.waitLabel("t:bye")
	if code := postJSON(t, srv.URL+"/api/send", `{"player":3,"msg":{"TypeString":"ClientQuizCommandMessage","action":29}}`, &ok); code != 400 {
		t.Errorf("send to missing player %d", code)
	}

	resp, err := http.Get(srv.URL + "/qr.png")
	if err != nil {
		t.Fatal(err)
	}
	body, _ := io.ReadAll(resp.Body)
	resp.Body.Close()
	if resp.Header.Get("Content-Type") != "image/png" || !bytes.HasPrefix(body, []byte("\x89PNG")) {
		t.Errorf("qr %s", resp.Header.Get("Content-Type"))
	}
	resp, _ = http.Get(srv.URL + "/")
	body, _ = io.ReadAll(resp.Body)
	resp.Body.Close()
	for _, frag := range []string{"qr.png", "data-run=\"3\"", "never", "#h=127.0.0.1"} {
		if !strings.Contains(string(body), frag) {
			t.Errorf("status page lacks %q", frag)
		}
	}

	if code := postJSON(t, srv.URL+"/api/quit", "", &ok); code != 200 {
		t.Error("quit")
	}
	select {
	case <-c.Quit():
	case <-time.After(time.Second):
		t.Error("quit not signalled")
	}
}

func TestServePage(t *testing.T) {
	c, _ := newConsole(t, "")
	dir := t.TempDir()
	if err := writeFile(dir+"/index.html", "<p>hi</p>"); err != nil {
		t.Fatal(err)
	}
	writeFile(dir+"/a.js", "export {}")
	srv := httptest.NewServer(c.Handler(dir))
	defer srv.Close()
	resp, _ := http.Get(srv.URL + "/page/index.html")
	b, _ := io.ReadAll(resp.Body)
	resp.Body.Close()
	if string(b) != "<p>hi</p>" {
		t.Errorf("page %q", b)
	}
	resp, _ = http.Get(srv.URL + "/page/a.js")
	resp.Body.Close()
	if resp.Header.Get("Content-Type") != "text/javascript" {
		t.Errorf("js type %s", resp.Header.Get("Content-Type"))
	}
}
