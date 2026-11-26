"""End-to-end test of the phone page: headless Chrome against the mock console over WebRTC Direct.

CLI: python3 -m pipeline.web_e2e [--scenario NAME ...] [--all] [--art] [--headful] [--chrome PATH]
                                  [--loopback] [--keep] [--out DIR]

Per run: serve the page (web/, or the build_web output with --art) on a free local port, build
tools/mockconsole into work/e2e/bin, start Chrome once (fake camera, CDP over a pipe). Per
scenario: start the mock console with the scenario file, open one browser context per scenario
player at the join URL, then for every step start it on the console, wait for the page screen,
take a screenshot, run the step's page actions and collect the console's verdict.

Report: work/reports/web-e2e.json; screenshots and logs under work/e2e/<UTC stamp>/<scenario>/.
Exit code 0 iff every step of every selected scenario passed.
"""
from __future__ import annotations

import argparse
import base64
import json
import os
import queue
import re
import shutil
import subprocess
import sys
import tempfile
import threading
import time
import urllib.error
import urllib.request
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path

from pipeline import cdp, web_common

REPO = web_common.REPO_ROOT
CONSOLE_DIR = REPO / "tools" / "mockconsole"
SCENARIO_DIR = CONSOLE_DIR / "scenarios"
MAC_CHROME = "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
CHROME_FLAGS = [
    "--headless=new", "--remote-debugging-pipe",
    "--use-fake-device-for-media-stream", "--use-fake-ui-for-media-stream",
    "--allow-loopback-in-peer-connection", "--enable-unsafe-swiftshader",
    "--autoplay-policy=no-user-gesture-required", "--no-first-run", "--no-default-browser-check",
    "--disable-background-timer-throttling", "--disable-renderer-backgrounding",
]
VIEWPORT = {"width": 390, "height": 844, "deviceScaleFactor": 3, "mobile": True}
DEFAULT_TIMEOUT_MS = 20000
CONNECT_TIMEOUT_MS = 15000
ACTIONS = {"tap", "hold", "type", "draw", "flick", "wait", "wait_screen", "reload", "navigate",
           "expect_visible", "screenshot", "set_storage"}
UID_RE = re.compile(r"\$uid:(\d+)")


class E2EError(Exception):
    pass


# ================================================================ scenarios

@dataclass
class Step:
    index: int
    id: str
    player: int = 0
    screen: str | None = None
    variant: str | None = None
    page: list = field(default_factory=list)
    timeout_ms: int = DEFAULT_TIMEOUT_MS
    screenshot: bool = True


@dataclass
class Scenario:
    name: str
    path: Path
    players: int
    steps: list


def parse_scenario(data: dict, path: Path | None = None) -> Scenario:
    where = str(path) if path else "scenario"
    if not isinstance(data, dict) or data.get("schema") != 1:
        raise E2EError(f"{where}: not a schema 1 scenario")
    name = data.get("name") or (path.stem if path else "")
    if not name:
        raise E2EError(f"{where}: no name")
    players = data.get("players", 1)
    if not isinstance(players, int) or players < 1:
        raise E2EError(f"{where}: players must be a positive integer")
    steps = []
    for i, s in enumerate(data.get("steps") or []):
        if not isinstance(s, dict):
            raise E2EError(f"{where}: step {i} is not an object")
        sid = str(s.get("id") or f"step{i}")
        player = s.get("player", 0)
        if not isinstance(player, int) or not 0 <= player < players:
            raise E2EError(f"{where}: step {sid}: player {player!r} out of range")
        actions = s.get("page") or []
        for a in actions:
            check_action(a, f"{where}: step {sid}", players)
        steps.append(Step(i, sid, player, s.get("screen"), s.get("variant"), list(actions),
                          int(s.get("timeout_ms", DEFAULT_TIMEOUT_MS)), bool(s.get("screenshot", True))))
    return Scenario(name, Path(path) if path else Path(name + ".json"), players, steps)


def check_action(a, where: str, players: int) -> None:
    if not isinstance(a, dict) or a.get("do") not in ACTIONS:
        raise E2EError(f"{where}: unknown page action {a!r}")
    do = a["do"]
    need = {"tap": ["target"], "hold": ["target"], "type": ["target", "text"], "draw": ["target", "points"],
            "flick": ["dir"], "wait": ["ms"], "wait_screen": ["screen"], "navigate": ["fragment"],
            "expect_visible": ["target"], "set_storage": ["key", "value"]}.get(do, [])
    for k in need:
        if k not in a:
            raise E2EError(f"{where}: action {do} needs {k}")
    if do == "flick" and a["dir"] not in ("up", "down", "left", "right"):
        raise E2EError(f"{where}: flick dir must be up or down")
    if do == "navigate" and a["fragment"] not in ("join", "none"):
        raise E2EError(f"{where}: navigate fragment must be join or none")
    if "player" in a and not (isinstance(a["player"], int) and 0 <= a["player"] < players):
        raise E2EError(f"{where}: action player out of range")


def load_scenario(path: Path) -> Scenario:
    try:
        data = json.loads(Path(path).read_text(encoding="utf-8"))
    except (OSError, ValueError) as e:
        raise E2EError(f"{path}: {e}") from e
    return parse_scenario(data, Path(path))


def scenario_files(names: list[str] | None, all_: bool, directory: Path = SCENARIO_DIR) -> list[Path]:
    files = sorted(directory.glob("*.json")) if directory.is_dir() else []
    if all_:
        if not files:
            raise E2EError(f"no scenarios in {directory}")
        return files
    out = []
    for n in names or []:
        p = Path(n)
        if p.suffix == ".json" and p.is_file():
            out.append(p)
            continue
        cand = directory / f"{n}.json"
        if not cand.is_file():
            known = ", ".join(f.stem for f in files) or "none"
            raise E2EError(f"unknown scenario {n} (known: {known})")
        out.append(cand)
    if not out:
        raise E2EError("choose scenarios with --scenario NAME or --all")
    return out


def resolve_uids(value, lookup):
    """Replace $uid:<player> in string values (recursively) using lookup(player) -> uid."""
    if isinstance(value, str):
        return UID_RE.sub(lambda m: lookup(int(m.group(1))), value)
    if isinstance(value, list):
        return [resolve_uids(v, lookup) for v in value]
    if isinstance(value, dict):
        return {k: resolve_uids(v, lookup) for k, v in value.items()}
    return value


# ================================================================ console

def _http_request(method: str, url: str, body: dict | None, timeout: float):
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method,
                                 headers={"Content-Type": "application/json"} if data else {})
    try:
        with urllib.request.urlopen(req, timeout=timeout) as r:
            return r.status, json.loads(r.read() or b"null")
    except urllib.error.HTTPError as e:
        raw = e.read()
        try:
            return e.code, json.loads(raw or b"null")
        except ValueError:
            return e.code, {"error": raw.decode("utf-8", "replace")}


class ConsoleAPI:
    def __init__(self, http_addr: str, request=_http_request):
        self.base = f"http://{http_addr}"
        self.request = request

    def _call(self, method, path, body=None, timeout=10.0):
        status, data = self.request(method, self.base + path, body, timeout)
        if status >= 400:
            raise E2EError(f"{method} {path}: HTTP {status} {data}")
        return data

    def start_step(self, i: int) -> None:
        self._call("POST", f"/api/steps/{i}/start", {})

    def result(self, i: int, timeout_ms: int) -> dict:
        return self._call("GET", f"/api/steps/{i}/result?timeout_ms={int(timeout_ms)}",
                          timeout=timeout_ms / 1000.0 + 15)

    def state(self) -> dict:
        return self._call("GET", "/api/state")

    def info(self) -> dict:
        return self._call("GET", "/api/info")

    def quit(self) -> None:
        try:
            self._call("POST", "/api/quit", {}, timeout=3)
        except (E2EError, OSError, ValueError):
            pass

    def uid_of(self, player: int) -> str:
        for s in self.state().get("sessions", []):
            if s.get("player") == player and s.get("uid"):
                return s["uid"]
        raise E2EError(f"no session with a uid for player {player}")


def read_ready_line(stream, timeout: float, sink=None) -> dict:
    """Read lines until the {"ready": true, ...} JSON line; later lines go to sink (a callable)."""
    q: queue.Queue = queue.Queue()

    def pump():
        ready_sent = False
        while True:
            try:
                raw = stream.readline()
            except (OSError, ValueError):
                break
            if not raw:
                break
            line = raw.decode("utf-8", "replace") if isinstance(raw, bytes) else raw
            if not ready_sent:
                try:
                    obj = json.loads(line)
                except ValueError:
                    obj = None
                if isinstance(obj, dict) and obj.get("ready") is True:
                    ready_sent = True
                    q.put(obj)
                    continue
            if sink:
                sink(line)
        if not ready_sent:
            q.put(None)

    threading.Thread(target=pump, daemon=True, name="console-stdout").start()
    try:
        obj = q.get(timeout=timeout)
    except queue.Empty:
        raise E2EError(f"mock console printed no ready line within {timeout} s")
    if obj is None:
        raise E2EError("mock console exited before its ready line")
    for key in ("http", "join_url"):
        if not obj.get(key):
            raise E2EError(f"mock console ready line lacks {key}")
    return obj


class ConsoleProcess:
    def __init__(self, binary: Path, scenario: Path, page_url: str, cert: Path, out_dir: Path,
                 loopback: bool = False, ready_timeout: float = 30.0):
        args = [str(binary), "-http", "127.0.0.1:0", "-udp", "0", "-ip", "127.0.0.1" if loopback else "",
                "-page", page_url, "-cert", str(cert), "-scenario", str(Path(scenario).resolve()),
                "-report", str(out_dir / "console-report.json")]
        self.args = args
        out_dir.mkdir(parents=True, exist_ok=True)
        self._stdout_log = open(out_dir / "console-stdout.log", "w", encoding="utf-8")
        self._stderr_log = open(out_dir / "console-stderr.log", "wb")
        self.proc = subprocess.Popen(args, stdout=subprocess.PIPE, stderr=self._stderr_log, cwd=str(out_dir))
        lock = threading.Lock()

        def sink(line):
            with lock:
                if not self._stdout_log.closed:
                    self._stdout_log.write(line)
                    self._stdout_log.flush()
        try:
            self.ready = read_ready_line(self.proc.stdout, ready_timeout, sink)
        except E2EError:
            self.stop()
            raise
        self.api = ConsoleAPI(self.ready["http"])

    def stop(self) -> None:
        if getattr(self, "api", None):
            self.api.quit()
        try:
            self.proc.wait(5)
        except subprocess.TimeoutExpired:
            self.proc.kill()
            self.proc.wait(5)
        self._stderr_log.close()
        time.sleep(0.05)
        try:
            self.proc.stdout.close()
        except OSError:
            pass
        self._stdout_log.close()


def build_console(out_bin: Path, log=print) -> Path:
    if not (CONSOLE_DIR / "go.mod").is_file():
        raise E2EError(f"mock console sources not found in {CONSOLE_DIR}")
    go = shutil.which("go")
    if not go:
        raise E2EError("go not found on PATH")
    out_bin.parent.mkdir(parents=True, exist_ok=True)
    log(f"building mock console -> {out_bin}")
    proc = subprocess.run([go, "build", "-o", str(out_bin), "."], cwd=str(CONSOLE_DIR),
                          stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    if proc.returncode != 0:
        raise E2EError(f"go build of the mock console failed:\n{proc.stdout}")
    return out_bin


# ================================================================ browser and page driving

def find_chrome(explicit: str | None = None) -> str:
    if explicit:
        if not Path(explicit).exists():
            raise E2EError(f"chrome not found: {explicit}")
        return explicit
    if Path(MAC_CHROME).exists():
        return MAC_CHROME
    for name in ("google-chrome", "google-chrome-stable", "chromium", "chromium-browser", "chrome"):
        p = shutil.which(name)
        if p:
            return p
    raise E2EError("Chrome not found (pass --chrome PATH)")


def chrome_args(chrome: str, user_data: Path, headful: bool = False) -> list[str]:
    flags = [f for f in CHROME_FLAGS if not (headful and f.startswith("--headless"))]
    return [chrome] + flags + [f"--user-data-dir={user_data}", "about:blank"]


FIND_JS = """(() => {
  const el = document.querySelector('[data-testid="' + CSS.escape(%s) + '"]');
  if (!el) return null;
  el.scrollIntoView({block: 'center', inline: 'center'});
  const r = el.getBoundingClientRect();
  const cs = getComputedStyle(el);
  const visible = r.width > 0 && r.height > 0 && cs.visibility !== 'hidden' && cs.display !== 'none'
    && Number(cs.opacity) !== 0;
  return {x: r.left, y: r.top, w: r.width, h: r.height, visible};
})()"""

DATASET_JS = """(() => { const d = document.body ? document.body.dataset : {};
  return {screen: d.screen || '', variant: d.variant || '', overlay: d.overlay || '', ready: document.readyState}; })()"""


class PageDriver:
    """One phone: a page in its own browser context, driven through a flattened CDP session."""

    def __init__(self, session, *, sleep=time.sleep, clock=time.monotonic, poll_s: float = 0.1):
        self.s = session
        self.sleep = sleep
        self.clock = clock
        self.poll_s = poll_s
        self.pending_storage: dict = {}

    # ---------------------------------------------------- basics

    def setup(self) -> None:
        for m in ("Page.enable", "Runtime.enable", "Log.enable"):
            self.s.send(m)
        self.s.send("Emulation.setDeviceMetricsOverride", dict(VIEWPORT))
        self.s.send("Emulation.setFocusEmulationEnabled", {"enabled": True})

    def evaluate(self, expr: str):
        r = self.s.send("Runtime.evaluate", {"expression": expr, "returnByValue": True, "awaitPromise": True})
        if r.get("exceptionDetails"):
            raise E2EError(f"page script failed: {r['exceptionDetails'].get('text', '')}")
        return r.get("result", {}).get("value")

    def _wait_load(self, mark, timeout_s: float = 20.0) -> None:
        try:
            self.s.wait_event("Page.loadEventFired", since=mark, timeout=timeout_s)
        except cdp.CDPTimeout:
            raise E2EError("page did not finish loading")

    def navigate(self, url: str) -> None:
        self._apply_storage()
        if "#" in url:  # a fragment-only change would not reload the page
            m = self.s.mark()
            self.s.send("Page.navigate", {"url": "about:blank"})
            self._wait_load(m)
        m = self.s.mark()
        r = self.s.send("Page.navigate", {"url": url})
        if r.get("errorText"):
            raise E2EError(f"navigation failed: {r['errorText']}")
        self._wait_load(m)

    def reload(self) -> None:
        self._apply_storage()
        m = self.s.mark()
        self.s.send("Page.reload", {"ignoreCache": True})
        self._wait_load(m)

    def _apply_storage(self) -> None:
        if not self.pending_storage:
            return
        for k, v in self.pending_storage.items():
            self.evaluate(f"localStorage.setItem({json.dumps(k)}, {json.dumps(v)})")
        self.pending_storage = {}

    def dataset(self) -> dict:
        return self.evaluate(DATASET_JS) or {}

    def wait_screen(self, screen: str, variant: str | None, timeout_ms: int) -> dict:
        deadline = self.clock() + timeout_ms / 1000.0
        last = {}
        while True:
            try:
                last = self.dataset()
            except (E2EError, cdp.CDPError):
                last = {}
            if last.get("screen") == screen and (variant is None or last.get("variant") == variant):
                return last
            if self.clock() >= deadline:
                want = screen + (f":{variant}" if variant is not None else "")
                got = f"{last.get('screen', '?')}:{last.get('variant', '')}"
                overlay = f" (overlay {last['overlay']})" if last.get("overlay") else ""
                raise E2EError(f"screen {want} not reached in {timeout_ms} ms, page shows {got}{overlay}")
            self.sleep(self.poll_s)

    def find(self, testid: str, timeout_ms: int = 5000) -> dict:
        deadline = self.clock() + timeout_ms / 1000.0
        while True:
            r = self.evaluate(FIND_JS % json.dumps(testid))
            if r and r.get("visible"):
                return r
            if self.clock() >= deadline:
                raise E2EError(f"element {testid} {'not visible' if r else 'not found'} within {timeout_ms} ms")
            self.sleep(self.poll_s)

    def screenshot(self, path: Path) -> None:
        r = self.s.send("Page.captureScreenshot", {"format": "png"})
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(base64.b64decode(r["data"]))

    # ---------------------------------------------------- input

    def _mouse(self, kind: str, x: float, y: float, buttons: int) -> None:
        params = {"type": kind, "x": round(x, 2), "y": round(y, 2), "button": "left", "buttons": buttons,
                  "pointerType": "mouse"}
        if kind in ("mousePressed", "mouseReleased"):
            params["clickCount"] = 1
        if kind == "mouseMoved" and not buttons:
            params["button"] = "none"
        self.s.send("Input.dispatchMouseEvent", params)

    @staticmethod
    def _centre(r: dict) -> tuple[float, float]:
        return r["x"] + r["w"] / 2, r["y"] + r["h"] / 2

    def tap(self, testid: str) -> None:
        x, y = self._centre(self.find(testid))
        self._mouse("mouseMoved", x, y, 0)
        self._mouse("mousePressed", x, y, 1)
        self._mouse("mouseReleased", x, y, 0)

    def hold(self, testid: str, ms: int) -> None:
        x, y = self._centre(self.find(testid))
        self._mouse("mouseMoved", x, y, 0)
        self._mouse("mousePressed", x, y, 1)
        self.sleep(ms / 1000.0)
        self._mouse("mouseReleased", x, y, 0)

    def type(self, testid: str, text: str) -> None:
        self.find(testid)
        self.evaluate(f"document.querySelector('[data-testid=\"' + CSS.escape({json.dumps(testid)}) + '\"]').focus()")
        self.s.send("Input.insertText", {"text": text})

    def draw(self, testid: str, points: list, steps: int = 8) -> None:
        if not points:
            raise E2EError("draw needs at least one point")
        r = self.find(testid)
        pts = [(r["x"] + float(px) * r["w"], r["y"] + float(py) * r["h"]) for px, py in points]
        steps = max(1, int(steps))
        self._mouse("mouseMoved", *pts[0], 0)
        self._mouse("mousePressed", *pts[0], 1)
        for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
            for k in range(1, steps + 1):
                t = k / steps
                self._mouse("mouseMoved", x0 + (x1 - x0) * t, y0 + (y1 - y0) * t, 1)
                self.sleep(0.008)
        self._mouse("mouseReleased", *pts[-1], 0)

    def flick(self, direction: str, testid: str | None = None, distance: float = 200.0) -> None:
        if testid:
            x, y = self._centre(self.find(testid))
        else:
            x, y = VIEWPORT["width"] / 2, VIEWPORT["height"] / 2
        dx, dy = {"up": (0, -1), "down": (0, 1), "left": (-1, 0), "right": (1, 0)}[direction]
        self._mouse("mouseMoved", x, y, 0)
        self._mouse("mousePressed", x, y, 1)
        n = 6
        for k in range(1, n + 1):
            self._mouse("mouseMoved", x + dx * distance * k / n, y + dy * distance * k / n, 1)
            self.sleep(0.016)
        self._mouse("mouseReleased", x + dx * distance, y + dy * distance, 0)


@dataclass
class ActionContext:
    pages: list                      # PageDriver per player
    join_url: str
    page_url: str
    uid_of: object                   # callable(player) -> uid
    shot_dir: Path
    shot_prefix: str
    sleep: object = time.sleep
    shots: list = field(default_factory=list)


def run_action(action: dict, player: int, ctx: ActionContext) -> None:
    a = resolve_uids(action, ctx.uid_of) if "$uid:" in json.dumps(action) else action
    page = ctx.pages[a.get("player", player)]
    do = a["do"]
    if do == "tap":
        page.tap(a["target"])
    elif do == "hold":
        page.hold(a["target"], int(a.get("ms", 2500)))
    elif do == "type":
        page.type(a["target"], str(a["text"]))
    elif do == "draw":
        page.draw(a["target"], a["points"], int(a.get("steps", 8)))
    elif do == "flick":
        page.flick(a["dir"], a.get("target"))
    elif do == "wait":
        ctx.sleep(int(a["ms"]) / 1000.0)
    elif do == "wait_screen":
        page.wait_screen(a["screen"], a.get("variant"), int(a.get("timeout_ms", DEFAULT_TIMEOUT_MS)))
    elif do == "reload":
        page.reload()
    elif do == "navigate":
        page.navigate(ctx.join_url if a["fragment"] == "join" else ctx.page_url)
    elif do == "expect_visible":
        page.find(a["target"], int(a.get("timeout_ms", 5000)))
    elif do == "screenshot":
        name = re.sub(r"[^A-Za-z0-9_.-]", "_", str(a.get("name", "shot")))
        path = ctx.shot_dir / f"{ctx.shot_prefix}-{name}.png"
        page.screenshot(path)
        ctx.shots.append(path)
    elif do == "set_storage":
        page.pending_storage[str(a["key"])] = str(a["value"])
    else:
        raise E2EError(f"unknown action {do}")


# ================================================================ scenario runner

def _rel(p: Path) -> str:
    try:
        return Path(p).resolve().relative_to(REPO).as_posix()
    except ValueError:
        return str(p)


class ScenarioRunner:
    """Runs one scenario. console: ConsoleProcess-like (ready, api, stop); open_page(player) -> PageDriver."""

    def __init__(self, scenario: Scenario, console, open_page, out_dir: Path, page_url: str,
                 log=print, sleep=time.sleep):
        self.sc = scenario
        self.console = console
        self.open_page = open_page
        self.out_dir = out_dir
        self.page_url = page_url
        self.log = log
        self.sleep = sleep

    def run(self) -> dict:
        api = self.console.api
        join_url = self.console.ready["join_url"]
        result = {"name": self.sc.name, "pass": False, "steps": []}
        pages = []
        setup_error = None
        try:
            for p in range(self.sc.players):
                page = self.open_page(p)
                pages.append(page)
                page.navigate(join_url)
                page.wait_screen("connect", None, CONNECT_TIMEOUT_MS)
        except (E2EError, cdp.CDPError) as e:
            setup_error = f"harness: opening the pages failed: {e}"
            self.log(f"  {setup_error}")
        ctx = ActionContext(pages, join_url, self.page_url, api.uid_of, self.out_dir, "", self.sleep)
        for step in self.sc.steps:
            result["steps"].append(self.run_step(step, ctx, setup_error))
        result["pass"] = bool(result["steps"]) and all(s["pass"] for s in result["steps"])
        return result

    def run_step(self, step: Step, ctx: ActionContext, setup_error: str | None) -> dict:
        errors: list[str] = []
        prefix = f"{step.index:02d}-{re.sub(r'[^A-Za-z0-9_.-]', '_', step.id)}"
        shot_path = None
        if setup_error:
            errors.append(setup_error)
        started = False
        if not setup_error:
            try:
                self.console.api.start_step(step.index)
                started = True
            except (E2EError, OSError) as e:
                errors.append(f"harness: start failed: {e}")
        page = ctx.pages[step.player] if step.player < len(ctx.pages) else None
        if started and page is not None:
            try:
                if step.screen:
                    page.wait_screen(step.screen, step.variant, step.timeout_ms)
            except (E2EError, cdp.CDPError) as e:
                errors.append(f"harness: {e}")
            if step.screenshot:
                try:
                    shot_path = self.out_dir / f"{prefix}.png"
                    page.screenshot(shot_path)
                except (E2EError, cdp.CDPError, OSError) as e:
                    shot_path = None
                    errors.append(f"harness: screenshot failed: {e}")
            ctx.shot_prefix = prefix
            for i, action in enumerate(step.page):
                try:
                    run_action(action, step.player, ctx)
                except (E2EError, cdp.CDPError) as e:
                    errors.append(f"harness: action {i} ({action.get('do')}): {e}")
                    break
        verdict = None
        if started:
            try:
                verdict = self.console.api.result(step.index, step.timeout_ms)
            except (E2EError, OSError, ValueError) as e:
                errors.append(f"harness: result failed: {e}")
        if verdict is not None:
            errors.extend(str(e) for e in verdict.get("errors") or [])
            if not verdict.get("pass") and not verdict.get("errors"):
                errors.append("console: step failed")
        ok = verdict is not None and bool(verdict.get("pass")) and not errors
        self.log(f"  [{'PASS' if ok else 'FAIL'}] {step.index:02d} {step.id}")
        for e in errors:
            self.log(f"         {e}")
        return {"id": step.id, "pass": ok, "errors": errors,
                "screenshot": _rel(shot_path) if shot_path else None}


def write_report(path: Path, mode: str, scenarios: list[dict]) -> dict:
    report = {"mode": mode, "pass": bool(scenarios) and all(s["pass"] for s in scenarios), "scenarios": scenarios}
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return report


# ================================================================ browser session

class Browser:
    def __init__(self, chrome: str, out_dir: Path, headful: bool, keep: bool, log=print):
        self.profile = Path(tempfile.mkdtemp(prefix="iwyk-e2e-chrome-"))
        self.keep = keep
        self.log = log
        self._chrome_log = open(out_dir / "chrome.log", "wb")
        self.proc, self.cdp = cdp.launch(chrome_args(chrome, self.profile, headful),
                                         stdout=self._chrome_log, stderr=self._chrome_log)
        self.cdp.send("Browser.getVersion", timeout=30)
        self.contexts: list[str] = []
        self._console_logs: dict[str, object] = {}
        self.cdp.add_listener(self._on_event)

    def _on_event(self, msg: dict) -> None:
        f = self._console_logs.get(msg.get("sessionId"))
        if not f:
            return
        m, p = msg.get("method"), msg.get("params", {})
        stamp = datetime.now().strftime("%H:%M:%S.%f")[:-3]
        line = None
        if m == "Runtime.consoleAPICalled":
            parts = []
            for arg in p.get("args", []):
                parts.append(str(arg.get("value", arg.get("description", arg.get("type")))))
            line = f"{stamp} console.{p.get('type')}: {' '.join(parts)}"
        elif m == "Runtime.exceptionThrown":
            d = p.get("exceptionDetails", {})
            line = f"{stamp} exception: {d.get('exception', {}).get('description') or d.get('text')}"
        elif m == "Log.entryAdded":
            e = p.get("entry", {})
            line = f"{stamp} log.{e.get('level')}: {e.get('text')} {e.get('url', '')}".rstrip()
        if line:
            f.write(line + "\n")
            f.flush()

    def open_page(self, log_path: Path) -> PageDriver:
        ctx_id = self.cdp.send("Target.createBrowserContext", {"disposeOnDetach": True})["browserContextId"]
        self.contexts.append(ctx_id)
        target = self.cdp.send("Target.createTarget", {"url": "about:blank", "browserContextId": ctx_id})["targetId"]
        sid = self.cdp.send("Target.attachToTarget", {"targetId": target, "flatten": True})["sessionId"]
        log_path.parent.mkdir(parents=True, exist_ok=True)
        self._console_logs[sid] = open(log_path, "a", encoding="utf-8")
        page = PageDriver(self.cdp.session(sid))
        page.setup()
        return page

    def close_contexts(self) -> None:
        for c in self.contexts:
            try:
                self.cdp.send("Target.disposeBrowserContext", {"browserContextId": c}, timeout=10)
            except cdp.CDPError:
                pass
        self.contexts = []
        for f in self._console_logs.values():
            f.close()
        self._console_logs = {}

    def close(self) -> None:
        self.close_contexts()
        try:
            self.cdp.send("Browser.close", timeout=5)
        except cdp.CDPError:
            pass
        try:
            self.proc.wait(10)
        except subprocess.TimeoutExpired:
            self.proc.kill()
            self.proc.wait(5)
        self.cdp.close()
        self._chrome_log.close()
        if not self.keep:
            shutil.rmtree(self.profile, ignore_errors=True)


# ================================================================ main

def run(args, log=print) -> int:
    from pipeline import build_web, web_serve

    files = scenario_files(args.scenario, args.all)
    scenarios = [load_scenario(f) for f in files]
    work = web_common.work_dir()
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    out_root = Path(args.out).resolve() if args.out else work / "e2e" / stamp
    out_root.mkdir(parents=True, exist_ok=True)
    e2e_dir = work / "e2e"
    mode = "art" if args.art else "neutral"

    if args.art:
        res = build_web.build(web_common.WEB_SRC, web_common.web_dist(), web_common.app_assets(),
                              warn=lambda m: log(f"build_web warning: {m}"))
        root = web_common.web_dist()
        if res["mode"] != "art":
            log("warning: --art requested but the build is neutral (no app export)")
    else:
        root = web_common.WEB_SRC
    server, page_url = web_serve.start_background(root)
    log(f"serving the {mode} page on {page_url}")
    binary = build_console(e2e_dir / "bin" / "mockconsole", log)
    chrome = find_chrome(args.chrome)
    browser = Browser(chrome, out_root, args.headful, args.keep, log)
    results = []
    try:
        for sc in scenarios:
            log(f"scenario {sc.name}")
            sdir = out_root / sc.name
            sdir.mkdir(parents=True, exist_ok=True)
            try:
                console = ConsoleProcess(binary, sc.path, page_url, e2e_dir / "dtls.pem", sdir, args.loopback)
            except (E2EError, OSError) as e:
                log(f"  mock console failed: {e}")
                results.append({"name": sc.name, "pass": False, "steps": [
                    {"id": s.id, "pass": False, "errors": [f"harness: mock console failed: {e}"], "screenshot": None}
                    for s in sc.steps]})
                continue
            try:
                runner = ScenarioRunner(sc, console, lambda p, d=sdir: browser.open_page(d / f"page-{p}.log"),
                                        sdir, page_url, log)
                results.append(runner.run())
            finally:
                browser.close_contexts()
                console.stop()
    finally:
        browser.close()
        server.shutdown()
        server.server_close()
    report_path = work / "reports" / "web-e2e.json"
    report = write_report(report_path, mode, results)
    total = sum(len(s["steps"]) for s in results)
    passed = sum(1 for s in results for st in s["steps"] if st["pass"])
    log(f"{passed}/{total} steps passed in {len(results)} scenarios ({mode}); report {_rel(report_path)}, "
        f"screenshots {_rel(out_root)}")
    return 0 if report["pass"] else 1


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="python3 -m pipeline.web_e2e", description=__doc__.split("\n")[0])
    ap.add_argument("--scenario", action="append", metavar="NAME", help="scenario name or file (repeatable)")
    ap.add_argument("--all", action="store_true", help="every scenario in tools/mockconsole/scenarios")
    ap.add_argument("--art", action="store_true", help="test the build_web output with the app's art")
    ap.add_argument("--headful", action="store_true", help="show the browser")
    ap.add_argument("--chrome", help="Chrome or Chromium binary")
    ap.add_argument("--loopback", action="store_true", help="advertise 127.0.0.1 instead of the LAN IPv4")
    ap.add_argument("--keep", action="store_true", help="keep the temporary Chrome profile")
    ap.add_argument("--out", help="folder for screenshots and logs (default work/e2e/<UTC stamp>)")
    args = ap.parse_args(argv)
    try:
        return run(args)
    except (E2EError, cdp.CDPError, OSError) as e:
        print(f"web_e2e: {e}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
