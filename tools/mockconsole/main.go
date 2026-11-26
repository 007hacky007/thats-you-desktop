// Command mockconsole is a stand-in for the game when testing the phone page.
// See README.md for the CLI, the HTTP API and the scenario format.
package main

import (
	"context"
	"encoding/json"
	"flag"
	"fmt"
	"io"
	"net"
	"net/http"
	"os"
	"os/signal"
	"path/filepath"
	"syscall"
	"time"

	"iwyknet/server"

	"mockconsole/bot"
	"mockconsole/console"
)

func main() {
	os.Exit(run(context.Background(), os.Args[1:], os.Stdout, os.Stderr))
}

// serverTransport adapts *server.Server to console.Transport.
type serverTransport struct{ s *server.Server }

func (t serverTransport) Next(ctx context.Context) (console.Event, error) {
	ev, err := t.s.Next(ctx)
	if err != nil {
		return console.Event{}, err
	}
	return console.Event{Kind: console.EventKind(ev.Kind), Session: ev.Session, TransferID: ev.TransferID, Code: ev.Code, Data: ev.Data}, nil
}
func (t serverTransport) SendText(s int32, b []byte) error       { return t.s.SendText(s, b) }
func (t serverTransport) SendBinary(s, id int32, b []byte) error { return t.s.SendBinary(s, id, b) }
func (t serverTransport) Close(s int32) error                    { return t.s.Close(s) }
func (t serverTransport) JoinURL(base string) (string, error)    { return t.s.JoinURL(base) }

func findCatalogue(flagValue string) (string, error) {
	if flagValue != "" {
		return flagValue, nil
	}
	cwd, _ := os.Getwd()
	exe, _ := os.Executable()
	return console.FindCatalogue(cwd, filepath.Dir(exe))
}

func run(ctx context.Context, args []string, stdout, stderr io.Writer) int {
	fs := flag.NewFlagSet("mockconsole", flag.ContinueOnError)
	fs.SetOutput(stderr)
	httpAddr := fs.String("http", "127.0.0.1:0", "HTTP listen address (status page and API)")
	udp := fs.Int("udp", 0, "UDP port of the WebRTC server (0 = any free port)")
	ip := fs.String("ip", "", "LAN IPv4 advertised in the QR code (empty = automatic)")
	page := fs.String("page", "http://127.0.0.1:8088/", "base URL of the phone page")
	cert := fs.String("cert", "", "DTLS certificate PEM path (created when missing; default in the temp dir)")
	scenarioPath := fs.String("scenario", "", "scenario file")
	auto := fs.Bool("auto", false, "run every step in order and exit (0 = all passed)")
	useBot := fs.Bool("bot", false, "answer the expectations with in-process Go clients")
	reportPath := fs.String("report", "", "write the JSON report here on exit")
	servePage := fs.String("serve-page", "", "also serve this directory at /page/")
	cataloguePath := fs.String("catalogue", "", "path of protocol/messages.json (default: searched upwards)")
	check := fs.Bool("check", false, "validate scenario files (the remaining arguments) and exit")
	verbose := fs.Bool("v", false, "log to stderr")
	if err := fs.Parse(args); err != nil {
		return 2
	}
	logf := func(string, ...any) {}
	if *verbose {
		logf = func(f string, a ...any) { fmt.Fprintf(stderr, "mockconsole: "+f+"\n", a...) }
	}
	cp, err := findCatalogue(*cataloguePath)
	if err != nil {
		fmt.Fprintln(stderr, "mockconsole:", err)
		return 2
	}
	cat, err := console.LoadCatalogue(cp)
	if err != nil {
		fmt.Fprintln(stderr, "mockconsole:", err)
		return 2
	}
	if *check {
		return checkFiles(cat, fs.Args(), stdout, stderr)
	}
	if fs.NArg() > 0 {
		fmt.Fprintln(stderr, "mockconsole: unexpected arguments", fs.Args())
		return 2
	}

	var sc *console.Scenario
	if *scenarioPath != "" {
		sc, err = console.LoadScenario(*scenarioPath)
		if err != nil {
			fmt.Fprintln(stderr, "mockconsole:", err)
			return 2
		}
		if errs := sc.Check(cat); len(errs) > 0 {
			for _, e := range errs {
				fmt.Fprintf(stderr, "%s: %s\n", *scenarioPath, e)
			}
			return 2
		}
	}
	if *useBot && sc != nil && !sc.BotPlayable() {
		fmt.Fprintf(stderr, "mockconsole: scenario %s is marked bot false; skipped\n", sc.Name)
		if *reportPath != "" {
			writeReport(*reportPath, map[string]any{"scenario": sc.Name, "pass": true, "skipped": true, "steps": []any{}, "catalogue_errors": []any{}})
		}
		return 0
	}
	if *cert == "" {
		*cert = filepath.Join(os.TempDir(), "iwyk-mockconsole", "dtls.pem")
	}

	srv, err := server.Start(server.Config{UDPPort: *udp, LANIP: *ip, CertPath: *cert})
	if err != nil {
		fmt.Fprintln(stderr, "mockconsole: start:", err)
		return 1
	}
	defer srv.Stop()
	sip, sport := srv.Endpoint()
	c, err := console.New(console.Options{Transport: serverTransport{srv}, Catalogue: cat, Scenario: sc, PageURL: *page, IP: sip, UDP: sport, Logf: logf})
	if err != nil {
		fmt.Fprintln(stderr, "mockconsole:", err)
		return 2
	}
	ln, err := net.Listen("tcp", *httpAddr)
	if err != nil {
		fmt.Fprintln(stderr, "mockconsole: http:", err)
		return 1
	}
	hs := &http.Server{Handler: c.Handler(*servePage), ReadHeaderTimeout: 10 * time.Second}
	go hs.Serve(ln)
	defer hs.Close()

	ctx, cancel := context.WithCancel(ctx)
	defer cancel()
	go c.Run(ctx)
	if *useBot {
		b, err := bot.Attach(ctx, c, c.JoinURL(), logf)
		if err != nil {
			fmt.Fprintln(stderr, "mockconsole:", err)
			return 1
		}
		defer b.Close()
	}

	ready := console.Marshal(console.NewObj().Set("ready", true).Set("http", ln.Addr().String()).Set("udp", sport).Set("join_url", c.JoinURL()))
	fmt.Fprintln(stdout, string(ready))

	code := 0
	if *auto {
		rep := c.RunAll(ctx)
		if !rep.Pass {
			code = 1
		}
		for _, st := range rep.Steps {
			if !st.Pass {
				fmt.Fprintf(stderr, "step %d %s failed: %v\n", st.Index, st.ID, st.Errors)
			}
		}
		for _, e := range rep.CatalogueErrors {
			fmt.Fprintf(stderr, "catalogue: %s\n", e)
		}
	} else {
		sig := make(chan os.Signal, 1)
		signal.Notify(sig, os.Interrupt, syscall.SIGTERM)
		defer signal.Stop(sig)
		select {
		case <-sig:
		case <-c.Quit():
		case <-ctx.Done():
		}
	}
	if *reportPath != "" {
		if err := writeReport(*reportPath, c.Report()); err != nil {
			fmt.Fprintln(stderr, "mockconsole: report:", err)
			return 1
		}
	}
	return code
}

func writeReport(path string, v any) error {
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		return err
	}
	b, err := json.MarshalIndent(v, "", "  ")
	if err != nil {
		return err
	}
	return os.WriteFile(path, append(b, '\n'), 0o644)
}

func checkFiles(cat *console.Catalogue, files []string, stdout, stderr io.Writer) int {
	if len(files) == 0 {
		fmt.Fprintln(stderr, "mockconsole: -check needs scenario files")
		return 2
	}
	bad := 0
	for _, f := range files {
		sc, err := console.LoadScenario(f)
		if err != nil {
			fmt.Fprintln(stderr, err)
			bad++
			continue
		}
		errs := sc.Check(cat)
		for _, e := range errs {
			fmt.Fprintf(stderr, "%s: %s\n", f, e)
		}
		if len(errs) > 0 {
			bad++
		}
	}
	fmt.Fprintf(stdout, "checked %d files, %d with errors\n", len(files), bad)
	if bad > 0 {
		return 1
	}
	return 0
}
