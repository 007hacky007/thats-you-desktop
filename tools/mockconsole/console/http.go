package console

import (
	"html/template"
	"io"
	"net/http"
	"strconv"
	"strings"
	"time"

	qrcode "github.com/skip2/go-qrcode"
)

// Info is /api/info.
type Info struct {
	JoinURL  string   `json:"join_url"`
	IP       string   `json:"ip"`
	UDP      int      `json:"udp"`
	Scenario *string  `json:"scenario"`
	Players  int      `json:"players"`
	Steps    []string `json:"steps"`
}

// Info returns /api/info.
func (c *Console) Info() Info {
	in := Info{JoinURL: c.joinURL, IP: c.opt.IP, UDP: c.opt.UDP, Players: c.sc.Players, Steps: []string{}}
	if c.sc.Name != "" {
		n := c.sc.Name
		in.Scenario = &n
	}
	for _, st := range c.sc.Steps {
		in.Steps = append(in.Steps, st.ID)
	}
	return in
}

func writeJSON(w http.ResponseWriter, code int, v any) {
	w.Header().Set("Content-Type", "application/json")
	w.Header().Set("Cache-Control", "no-store")
	w.WriteHeader(code)
	w.Write(Marshal(v))
	w.Write([]byte("\n"))
}

func writeErr(w http.ResponseWriter, code int, msg string) {
	writeJSON(w, code, map[string]any{"ok": false, "error": msg})
}

// Handler serves the status page and the JSON API (contract 7.5).
// servePage, when not empty, is served at /page/.
func (c *Console) Handler(servePage string) http.Handler {
	mux := http.NewServeMux()
	mux.HandleFunc("GET /{$}", c.handleStatus)
	mux.HandleFunc("GET /qr.png", func(w http.ResponseWriter, r *http.Request) {
		if c.joinURL == "" {
			http.Error(w, "no join url", http.StatusNotFound)
			return
		}
		png, err := qrcode.Encode(c.joinURL, qrcode.Medium, 512)
		if err != nil {
			http.Error(w, err.Error(), http.StatusInternalServerError)
			return
		}
		w.Header().Set("Content-Type", "image/png")
		w.Header().Set("Cache-Control", "no-store")
		w.Write(png)
	})
	mux.HandleFunc("GET /api/info", func(w http.ResponseWriter, r *http.Request) { writeJSON(w, 200, c.Info()) })
	mux.HandleFunc("GET /api/state", func(w http.ResponseWriter, r *http.Request) { writeJSON(w, 200, c.State()) })
	c.registerStepAPI(mux)
	mux.HandleFunc("GET /api/steps/{i}/result", func(w http.ResponseWriter, r *http.Request) {
		i, err := strconv.Atoi(r.PathValue("i"))
		if err != nil {
			writeErr(w, 400, "bad step index")
			return
		}
		ms := 0
		if s := r.URL.Query().Get("timeout_ms"); s != "" {
			if ms, err = strconv.Atoi(s); err != nil || ms < 0 {
				writeErr(w, 400, "bad timeout_ms")
				return
			}
		}
		res, err := c.Result(i, time.Duration(ms)*time.Millisecond)
		if err != nil {
			writeErr(w, 404, err.Error())
			return
		}
		writeJSON(w, 200, res)
	})
	mux.HandleFunc("GET /api/received", func(w http.ResponseWriter, r *http.Request) {
		var since int64
		if s := r.URL.Query().Get("since"); s != "" {
			v, err := strconv.ParseInt(s, 10, 64)
			if err != nil {
				writeErr(w, 400, "bad since")
				return
			}
			since = v
		}
		next, items := c.ReceivedSince(since)
		writeJSON(w, 200, map[string]any{"next": next, "items": items})
	})
	mux.HandleFunc("POST /api/send", func(w http.ResponseWriter, r *http.Request) {
		body, err := io.ReadAll(io.LimitReader(r.Body, 1<<20))
		if err != nil {
			writeErr(w, 400, err.Error())
			return
		}
		v, err := Decode(body)
		m, ok := v.(map[string]any)
		if err != nil || !ok {
			writeErr(w, 400, "body must be a JSON object")
			return
		}
		p := 0
		if pv, ok := m["player"]; ok {
			if p, ok = intOf(pv); !ok {
				writeErr(w, 400, "bad player")
				return
			}
			delete(m, "player")
		}
		if err := c.SendRaw(p, m); err != nil {
			writeErr(w, 400, err.Error())
			return
		}
		writeJSON(w, 200, map[string]any{"ok": true})
	})
	mux.HandleFunc("POST /api/quit", func(w http.ResponseWriter, r *http.Request) {
		writeJSON(w, 200, map[string]any{"ok": true})
		c.RequestQuit()
	})
	if servePage != "" {
		fs := http.StripPrefix("/page/", http.FileServer(http.Dir(servePage)))
		mux.Handle("GET /page/", http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			w.Header().Set("Cache-Control", "no-store")
			if strings.HasSuffix(r.URL.Path, ".js") || strings.HasSuffix(r.URL.Path, ".mjs") {
				w.Header().Set("Content-Type", "text/javascript")
			}
			fs.ServeHTTP(w, r)
		}))
	}
	return mux
}

var statusTmpl = template.Must(template.New("status").Parse(`<!doctype html>
<html><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1">
<title>Mock console</title>
<style>
body { font: 15px/1.4 system-ui, sans-serif; margin: 16px; background: #f4f4f4; color: #111; }
.qr { background: #fff; padding: 12px; display: inline-block; }
.qr img { width: 280px; height: 280px; image-rendering: pixelated; }
code { word-break: break-all; }
table { border-collapse: collapse; margin: 8px 0; }
td, th { border: 1px solid #ccc; padding: 3px 8px; text-align: left; vertical-align: top; }
.passed { color: #060; } .failed { color: #a00; } .waiting, .sent { color: #a60; }
pre { white-space: pre-wrap; background: #fff; padding: 8px; max-height: 300px; overflow: auto; }
</style></head><body>
<h1>Mock console{{if .Scenario}}: {{.Scenario}}{{end}}</h1>
<div class="qr"><img src="qr.png" alt="join QR code"></div>
<p>Join URL: <code>{{.JoinURL}}</code><br>UDP {{.IP}}:{{.UDP}}</p>
<h2>Sessions</h2>
<table id="sessions"><thead><tr><th>session</th><th>player</th><th>uid</th><th>state</th><th>PlayerID</th><th>slot</th></tr></thead><tbody></tbody></table>
<h2>Steps</h2>
<table id="steps"><thead><tr><th>#</th><th>id</th><th>player</th><th>screen</th><th>doc</th><th></th><th>result</th></tr></thead><tbody>
{{range .Steps}}<tr data-index="{{.Index}}"><td>{{.Index}}</td><td>{{.ID}}</td><td>{{.Player}}</td><td>{{.Screen}}</td><td>{{.Doc}}</td>
<td><button type="button" data-run="{{.Index}}">Run</button></td><td class="res"></td></tr>
{{end}}</tbody></table>
<h2>Last result</h2><pre id="result"></pre>
<script>
function cell(tr, text, cls) { var td = document.createElement('td'); td.textContent = String(text); if (cls) td.className = cls; tr.appendChild(td); }
function refresh() {
  fetch('api/state').then(function (r) { return r.json(); }).then(function (st) {
    var tb = document.querySelector('#sessions tbody');
    while (tb.firstChild) tb.removeChild(tb.firstChild);
    st.sessions.forEach(function (s) {
      var tr = document.createElement('tr');
      [s.session, s.player, s.uid, s.state, s.player_id, s.slot].forEach(function (v) { cell(tr, v); });
      tb.appendChild(tr);
    });
  }).catch(function () {});
}
function showResult(i) {
  fetch('api/steps/' + i + '/result?timeout_ms=60000').then(function (r) { return r.json(); }).then(function (res) {
    var td = document.querySelector('tr[data-index="' + i + '"] .res');
    td.textContent = res.state + (res.errors.length ? ' (' + res.errors.length + ' errors)' : '');
    td.className = 'res ' + res.state;
    document.getElementById('result').textContent = JSON.stringify(res, null, 2);
  });
}
document.querySelectorAll('button[data-run]').forEach(function (b) {
  b.addEventListener('click', function () {
    var i = b.getAttribute('data-run');
    fetch('api/steps/' + i + '/start', { method: 'POST' }).then(function (r) { return r.json(); }).then(function (res) {
      if (!res.ok) { document.getElementById('result').textContent = res.error; return; }
      var td = document.querySelector('tr[data-index="' + i + '"] .res');
      td.textContent = 'running'; td.className = 'res waiting';
      showResult(i);
    });
  });
});
refresh(); setInterval(refresh, 1000);
</script>
</body></html>
`))

type statusStep struct {
	Index           int
	ID, Doc, Screen string
	Player          int
}

func (c *Console) handleStatus(w http.ResponseWriter, r *http.Request) {
	data := struct {
		Scenario, JoinURL, IP string
		UDP                   int
		Steps                 []statusStep
	}{Scenario: c.sc.Name, JoinURL: c.joinURL, IP: c.opt.IP, UDP: c.opt.UDP}
	for i, st := range c.sc.Steps {
		sc := st.Screen
		if st.Variant != "" {
			sc += ":" + st.Variant
		}
		data.Steps = append(data.Steps, statusStep{Index: i, ID: st.ID, Doc: st.Doc, Screen: sc, Player: st.Player})
	}
	w.Header().Set("Content-Type", "text/html; charset=utf-8")
	w.Header().Set("Cache-Control", "no-store")
	statusTmpl.Execute(w, data)
}
