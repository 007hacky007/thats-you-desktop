package console

import (
	"bytes"
	"errors"
	"io"
	"net/http"
	"strconv"
)

// registerStepAPI adds the step control routes:
//
//	POST /api/steps/<i>/start      optional body {"page_actions": true}
//	POST /api/steps/<i>/page-done  the driver finished the step's page actions
func (c *Console) registerStepAPI(mux *http.ServeMux) {
	mux.HandleFunc("POST /api/steps/{i}/start", func(w http.ResponseWriter, r *http.Request) {
		i, err := strconv.Atoi(r.PathValue("i"))
		if err != nil {
			writeErr(w, 400, "bad step index")
			return
		}
		opt, err := parseStartBody(r)
		if err != nil {
			writeErr(w, 400, err.Error())
			return
		}
		if err := c.StartStepWith(i, opt); err != nil {
			code := 400
			if errors.Is(err, ErrBusy) {
				code = 409
			}
			writeErr(w, code, err.Error())
			return
		}
		writeJSON(w, 200, map[string]any{"ok": true})
	})
	mux.HandleFunc("POST /api/steps/{i}/page-done", func(w http.ResponseWriter, r *http.Request) {
		i, err := strconv.Atoi(r.PathValue("i"))
		if err != nil {
			writeErr(w, 400, "bad step index")
			return
		}
		running, err := c.PageDone(i)
		if err != nil {
			code := 400
			if errors.Is(err, ErrNotRunning) {
				code = 409
			}
			writeErr(w, code, err.Error())
			return
		}
		writeJSON(w, 200, map[string]any{"ok": true, "running": running})
	})
}

// parseStartBody reads the optional start body: empty, {} or {"page_actions": bool}.
func parseStartBody(r *http.Request) (StartOptions, error) {
	var opt StartOptions
	body, err := io.ReadAll(io.LimitReader(r.Body, 1<<16))
	if err != nil {
		return opt, err
	}
	if len(bytes.TrimSpace(body)) == 0 {
		return opt, nil
	}
	v, err := Decode(body)
	m, ok := v.(map[string]any)
	if err != nil || !ok {
		return opt, errors.New("body must be a JSON object")
	}
	for k, x := range m {
		switch k {
		case "page_actions":
			b, ok := x.(bool)
			if !ok {
				return opt, errors.New("page_actions must be a bool")
			}
			opt.PageActions = b
		default:
			return opt, errors.New("unknown key " + strconv.Quote(k))
		}
	}
	return opt, nil
}
