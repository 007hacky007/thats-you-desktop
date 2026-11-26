import io
import json
import socket
import sys
import tempfile
import unittest
from pathlib import Path

from pipeline import web_common, web_e2e
from pipeline.web_e2e import E2EError


def scenario(**over):
    data = {"schema": 1, "name": "demo", "players": 2, "steps": [
        {"id": "join", "player": 0, "screen": "joined",
         "page": [{"do": "tap", "target": "connect-play"}], "timeout_ms": 5000},
        {"id": "dup", "player": 1, "screenshot": False,
         "page": [{"do": "set_storage", "key": "iwyk.uid", "value": "$uid:0"}, {"do": "reload"}]},
    ]}
    data.update(over)
    return data


class FakeSession:
    """Records CDP commands; answers Runtime.evaluate from a scripted queue."""

    def __init__(self, evaluate=None):
        self.calls = []
        self.evaluate = evaluate or (lambda expr: None)
        self.events = 0

    def send(self, method, params=None, timeout=30.0):
        self.calls.append((method, params or {}))
        if method == "Runtime.evaluate":
            return {"result": {"value": self.evaluate(params["expression"])}}
        if method == "Page.captureScreenshot":
            return {"data": "iVBORw0K"}
        return {}

    def mark(self):
        return self.events

    def wait_event(self, method, **kw):
        return {"method": method}

    def inputs(self):
        return [(p["type"], p["x"], p["y"], p.get("buttons")) for m, p in self.calls if m == "Input.dispatchMouseEvent"]


RECT = {"x": 10, "y": 100, "w": 200, "h": 50, "visible": True}


class FakeClock:
    def __init__(self):
        self.t = 0.0
        self.slept = []

    def now(self):
        return self.t

    def sleep(self, s):
        self.slept.append(s)
        self.t += s


class ScenarioParsingTests(unittest.TestCase):
    def test_parse_defaults(self):
        sc = web_e2e.parse_scenario(scenario())
        self.assertEqual(sc.name, "demo")
        self.assertEqual(sc.players, 2)
        a, b = sc.steps
        self.assertEqual((a.index, a.id, a.player, a.screen, a.variant, a.timeout_ms, a.screenshot),
                         (0, "join", 0, "joined", None, 5000, True))
        self.assertEqual((b.index, b.player, b.timeout_ms, b.screenshot), (1, 1, 20000, False))

    def test_rejects_bad_scenarios(self):
        bad = [
            {"schema": 2, "name": "x"},
            scenario(players=0),
            scenario(steps=[{"id": "a", "player": 2}]),
            scenario(steps=[{"id": "a", "page": [{"do": "jump"}]}]),
            scenario(steps=[{"id": "a", "page": [{"do": "tap"}]}]),
            scenario(steps=[{"id": "a", "page": [{"do": "flick", "dir": "sideways"}]}]),
            scenario(steps=[{"id": "a", "page": [{"do": "navigate", "fragment": "x"}]}]),
        ]
        for data in bad:
            with self.subTest(data=data):
                with self.assertRaises(E2EError):
                    web_e2e.parse_scenario(data)

    def test_scenario_files(self):
        with tempfile.TemporaryDirectory() as d:
            d = Path(d)
            (d / "b.json").write_text("{}")
            (d / "a.json").write_text("{}")
            self.assertEqual([p.name for p in web_e2e.scenario_files(None, True, d)], ["a.json", "b.json"])
            self.assertEqual(web_e2e.scenario_files(["b"], False, d), [d / "b.json"])
            with self.assertRaisesRegex(E2EError, "known: a, b"):
                web_e2e.scenario_files(["zz"], False, d)
            with self.assertRaises(E2EError):
                web_e2e.scenario_files(None, False, d)

    def test_resolve_uids(self):
        got = web_e2e.resolve_uids({"do": "set_storage", "value": "$uid:1", "list": ["a$uid:0b"], "n": 3},
                                   lambda p: f"U{p}")
        self.assertEqual(got, {"do": "set_storage", "value": "U1", "list": ["aU0b"], "n": 3})


class PageDriverTests(unittest.TestCase):
    def driver(self, evaluate):
        self.clock = FakeClock()
        self.session = FakeSession(evaluate)
        return web_e2e.PageDriver(self.session, sleep=self.clock.sleep, clock=self.clock.now)

    def test_tap_presses_element_centre(self):
        d = self.driver(lambda e: RECT)
        d.tap("connect-play")
        self.assertEqual(self.session.inputs(), [("mouseMoved", 110, 125, 0), ("mousePressed", 110, 125, 1),
                                                 ("mouseReleased", 110, 125, 0)])
        expr = self.session.calls[0][1]["expression"]
        self.assertIn('"connect-play"', expr)
        self.assertIn("scrollIntoView", expr)

    def test_find_waits_then_fails(self):
        d = self.driver(lambda e: None)
        with self.assertRaisesRegex(E2EError, "not found"):
            d.find("x", timeout_ms=500)
        self.assertGreaterEqual(len(self.clock.slept), 4)

    def test_hold(self):
        d = self.driver(lambda e: RECT)
        d.hold("start", 2500)
        self.assertIn(2.5, self.clock.slept)
        kinds = [k for k, *_ in self.session.inputs()]
        self.assertEqual(kinds, ["mouseMoved", "mousePressed", "mouseReleased"])

    def test_type_focuses_and_inserts(self):
        d = self.driver(lambda e: RECT)
        d.type("name-input", "Bob")
        methods = [m for m, _ in self.session.calls]
        self.assertEqual(methods[-1], "Input.insertText")
        self.assertEqual(self.session.calls[-1][1], {"text": "Bob"})
        self.assertIn(".focus()", self.session.calls[-2][1]["expression"])

    def test_draw_interpolates_in_target_rect(self):
        d = self.driver(lambda e: RECT)
        d.draw("draw-canvas", [[0, 0], [1, 1]], steps=4)
        ins = self.session.inputs()
        self.assertEqual(ins[1], ("mousePressed", 10, 100, 1))
        moves = [i for i in ins if i[0] == "mouseMoved" and i[3] == 1]
        self.assertEqual(len(moves), 4)
        self.assertEqual(moves[-1][1:3], (210, 150))
        self.assertEqual(ins[-1], ("mouseReleased", 210, 150, 0))

    def test_flick_up_moves_upwards(self):
        d = self.driver(lambda e: RECT)
        d.flick("up", "answer-1")
        ins = self.session.inputs()
        self.assertEqual(ins[1][0], "mousePressed")
        self.assertLess(ins[-1][2], ins[1][2] - 100)
        self.assertEqual(ins[-1][1], ins[1][1])

    def test_wait_screen_with_variant(self):
        seq = iter([{"screen": "connect"}, {"screen": "holding", "variant": "wait"},
                    {"screen": "holding", "variant": "general"}])
        d = self.driver(lambda e: next(seq))
        got = d.wait_screen("holding", "general", 5000)
        self.assertEqual(got["variant"], "general")

    def test_wait_screen_timeout_reports_actual(self):
        d = self.driver(lambda e: {"screen": "connect", "variant": "", "overlay": "popup"})
        with self.assertRaisesRegex(E2EError, "page shows connect: .*overlay popup"):
            d.wait_screen("joined", None, 300)

    def test_set_storage_applied_before_reload(self):
        d = self.driver(lambda e: None)
        d.pending_storage["iwyk.uid"] = "abc"
        d.reload()
        methods = [m for m, _ in self.session.calls]
        self.assertEqual(methods, ["Runtime.evaluate", "Page.reload"])
        self.assertIn('localStorage.setItem("iwyk.uid", "abc")', self.session.calls[0][1]["expression"])
        self.assertEqual(d.pending_storage, {})

    def test_navigate_with_fragment_goes_through_blank(self):
        d = self.driver(lambda e: None)
        d.navigate("http://127.0.0.1:1/#h=1")
        urls = [p["url"] for m, p in self.session.calls if m == "Page.navigate"]
        self.assertEqual(urls, ["about:blank", "http://127.0.0.1:1/#h=1"])

    def test_setup_emulates_phone(self):
        d = self.driver(lambda e: None)
        d.setup()
        metrics = dict(self.session.calls)["Emulation.setDeviceMetricsOverride"]
        self.assertEqual(metrics, {"width": 390, "height": 844, "deviceScaleFactor": 3, "mobile": True})

    def test_screenshot_writes_png(self):
        d = self.driver(lambda e: None)
        with tempfile.TemporaryDirectory() as t:
            p = Path(t) / "x" / "01-a.png"
            d.screenshot(p)
            self.assertEqual(p.read_bytes()[:4], b"\x89PNG")


class FakePage:
    def __init__(self, name, log, screens=None):
        self.name = name
        self.log = log
        self.screens = screens or {}
        self.pending_storage = {}

    def navigate(self, url):
        self.log.append((self.name, "navigate", url))

    def wait_screen(self, screen, variant, timeout_ms):
        self.log.append((self.name, "wait_screen", screen, variant))
        if self.screens.get(screen) is False:
            raise E2EError(f"screen {screen} not reached")
        return {"screen": screen}

    def screenshot(self, path):
        self.log.append((self.name, "screenshot", path.name))

    def tap(self, target):
        self.log.append((self.name, "tap", target))
        if target in self.screens.get("fail_taps", ()):
            raise E2EError(f"element {target} not found")

    def reload(self):
        self.log.append((self.name, "reload", dict(self.pending_storage)))
        self.pending_storage = {}


class FakeAPI:
    def __init__(self, log, verdicts, page_done_fails=()):
        self.log = log
        self.verdicts = verdicts
        self.page_done_fails = set(page_done_fails)

    def start_step(self, i, page_actions=False):
        self.log.append(("api", "start", i, page_actions))

    def page_done(self, i):
        self.log.append(("api", "page-done", i))
        if i in self.page_done_fails:
            raise E2EError("boom")
        return True

    def result(self, i, timeout_ms):
        self.log.append(("api", "result", i))
        return self.verdicts.get(i, {"index": i, "pass": True, "errors": []})

    def uid_of(self, player):
        return f"uid{player}"


class FakeConsole:
    def __init__(self, api):
        self.api = api
        self.ready = {"ready": True, "http": "127.0.0.1:1", "join_url": "http://127.0.0.1:9/#h=1"}


class RunnerTests(unittest.TestCase):
    def run_scenario(self, verdicts=None, screens=None, data=None, page_done_fails=()):
        log = []
        api = FakeAPI(log, verdicts or {}, page_done_fails)
        pages = {}

        def open_page(p):
            pages[p] = FakePage(f"p{p}", log, screens)
            return pages[p]
        with tempfile.TemporaryDirectory() as t:
            runner = web_e2e.ScenarioRunner(web_e2e.parse_scenario(data or scenario()), FakeConsole(api), open_page,
                                            Path(t), "http://127.0.0.1:9/", log=lambda m: None, sleep=lambda s: None)
            return runner.run(), log

    def test_flow_order(self):
        result, log = self.run_scenario()
        self.assertTrue(result["pass"])
        self.assertEqual(log, [
            ("p0", "navigate", "http://127.0.0.1:9/#h=1"), ("p0", "wait_screen", "connect", None),
            ("p1", "navigate", "http://127.0.0.1:9/#h=1"), ("p1", "wait_screen", "connect", None),
            ("api", "start", 0, True), ("p0", "wait_screen", "joined", None), ("p0", "screenshot", "00-join.png"),
            ("p0", "tap", "connect-play"), ("api", "page-done", 0), ("api", "result", 0),
            ("api", "start", 1, True), ("p1", "reload", {"iwyk.uid": "uid0"}), ("api", "page-done", 1),
            ("api", "result", 1),
        ])
        self.assertEqual([s["id"] for s in result["steps"]], ["join", "dup"])
        self.assertTrue(result["steps"][0]["screenshot"].endswith("00-join.png"))
        self.assertIsNone(result["steps"][1]["screenshot"])

    def test_console_failure_and_harness_failure_are_reported(self):
        result, _ = self.run_scenario(verdicts={1: {"pass": False, "errors": ["missing hello"]}},
                                      screens={"joined": False})
        self.assertFalse(result["pass"])
        s0, s1 = result["steps"]
        self.assertFalse(s0["pass"])
        self.assertIn("harness: screen joined not reached", s0["errors"][0])
        self.assertEqual(s1["errors"], ["missing hello"])

    def test_setup_failure_fails_every_step(self):
        result, log = self.run_scenario(screens={"connect": False})
        self.assertFalse(result["pass"])
        self.assertTrue(all("opening the pages failed" in s["errors"][0] for s in result["steps"]))
        self.assertFalse(any(e[:2] == ("api", "start") for e in log))
        self.assertFalse(any(e[:2] == ("api", "page-done") for e in log))

    def test_step_without_page_actions_sends_no_page_done(self):
        data = scenario(steps=[{"id": "look", "screen": "joined"}])
        result, log = self.run_scenario(data=data)
        self.assertTrue(result["pass"])
        self.assertIn(("api", "start", 0, False), log)
        self.assertFalse(any(e[:2] == ("api", "page-done") for e in log))

    def test_page_done_sent_even_if_an_action_fails(self):
        data = scenario(steps=[{"id": "a", "page": [{"do": "tap", "target": "gone"},
                                                    {"do": "tap", "target": "never"}]}])
        result, log = self.run_scenario(data=data, screens={"fail_taps": ["gone"]})
        self.assertEqual([e for e in log if e[0] != "p1" and e[1] in ("tap", "page-done", "result")],
                         [("p0", "tap", "gone"), ("api", "page-done", 0), ("api", "result", 0)])
        self.assertFalse(result["pass"])
        self.assertIn("action 0 (tap)", result["steps"][0]["errors"][0])

    def test_page_done_failure_is_a_harness_error(self):
        result, log = self.run_scenario(page_done_fails=[0])
        self.assertIn(("api", "result", 0), log)
        self.assertFalse(result["steps"][0]["pass"])
        self.assertIn("harness: page-done failed: boom", result["steps"][0]["errors"])


class ConsoleTests(unittest.TestCase):
    def test_read_ready_line(self):
        out = io.BytesIO(b"starting\n" + json.dumps({"ready": True, "http": "127.0.0.1:5", "udp": 6,
                                                     "join_url": "http://127.0.0.1:7/#h=1"}).encode()
                         + b"\nlater line\n")
        lines = []
        ready = web_e2e.read_ready_line(out, 5, lines.append)
        self.assertEqual(ready["http"], "127.0.0.1:5")
        import time
        for _ in range(50):
            if len(lines) == 2:
                break
            time.sleep(0.01)
        self.assertEqual(lines, ["starting\n", "later line\n"])

    def test_read_ready_line_eof(self):
        with self.assertRaisesRegex(E2EError, "exited"):
            web_e2e.read_ready_line(io.BytesIO(b"oops\n"), 5)

    def test_console_process_with_fake_binary(self):
        with tempfile.TemporaryDirectory() as t:
            t = Path(t)
            fake = t / "fakeconsole"
            fake.write_text("#!" + sys.executable + "\nimport sys, json, time\n"
                            "open('args.json', 'w').write(json.dumps(sys.argv[1:]))\n"
                            "print(json.dumps({'ready': True, 'http': '127.0.0.1:1', 'udp': 2, "
                            "'join_url': 'http://127.0.0.1:3/#h=x'}), flush=True)\n"
                            "time.sleep(0.2)\n")
            fake.chmod(0o755)
            sc = t / "s.json"
            sc.write_text("{}")
            proc = web_e2e.ConsoleProcess(fake, sc, "http://127.0.0.1:3/", t / "c.pem", t / "out", loopback=True)
            self.assertEqual(proc.ready["udp"], 2)
            proc.api.request = lambda *a: (200, {})
            proc.stop()
            args = json.loads((t / "out" / "args.json").read_text())
            self.assertEqual(args[args.index("-ip") + 1], "127.0.0.1")
            self.assertEqual(args[args.index("-scenario") + 1], str(sc.resolve()))
            self.assertEqual(args[args.index("-udp") + 1], "0")
            self.assertEqual(args[args.index("-http") + 1], "127.0.0.1:0")

    def test_api_calls(self):
        calls = []

        def request(method, url, body, timeout):
            calls.append((method, url))
            if url.endswith("/api/state"):
                return 200, {"sessions": [{"player": 0, "uid": "abc"}]}
            if "/start" in url:
                return 409, {"error": "busy"}
            return 200, {"pass": True}
        api = web_e2e.ConsoleAPI("127.0.0.1:5", request)
        self.assertEqual(api.uid_of(0), "abc")
        with self.assertRaises(E2EError):
            api.uid_of(1)
        with self.assertRaisesRegex(E2EError, "409"):
            api.start_step(2)
        self.assertEqual(api.result(3, 1500), {"pass": True})
        self.assertIn(("GET", "http://127.0.0.1:5/api/steps/3/result?timeout_ms=1500"), calls)

    def test_start_body_and_page_done(self):
        calls = []
        status = {"code": 200}

        def request(method, url, body, timeout):
            calls.append((method, url, body))
            if url.endswith("/page-done"):
                return status["code"], {"error": "x"} if status["code"] >= 400 else {"ok": True}
            return 200, {"ok": True}
        api = web_e2e.ConsoleAPI("127.0.0.1:5", request)
        api.start_step(1, page_actions=True)
        api.start_step(2)
        self.assertEqual(calls[0], ("POST", "http://127.0.0.1:5/api/steps/1/start", {"page_actions": True}))
        self.assertEqual(calls[1], ("POST", "http://127.0.0.1:5/api/steps/2/start", {}))
        self.assertTrue(api.page_done(1))
        self.assertEqual(calls[2][:2], ("POST", "http://127.0.0.1:5/api/steps/1/page-done"))
        for old in (400, 404, 405):
            status["code"] = old
            self.assertFalse(api.page_done(1), old)
        status["code"] = 500
        with self.assertRaisesRegex(E2EError, "500"):
            api.page_done(1)


class MiscTests(unittest.TestCase):
    def test_free_port(self):
        p = web_common.free_port()
        with socket.socket() as s:
            s.bind(("127.0.0.1", p))

    def test_chrome_args_camera_file(self):
        args = web_e2e.chrome_args("/c", Path("/tmp/p"), camera_file=Path("/tmp/o/test-pattern.y4m"))
        self.assertIn("--use-file-for-fake-video-capture=/tmp/o/test-pattern.y4m", args)
        self.assertIn("--use-fake-device-for-media-stream", args)
        self.assertFalse(any(a.startswith("--use-file-for-fake") for a in web_e2e.chrome_args("/c", Path("/p"))))

    def test_main_parses_camera_option(self):
        seen = {}
        orig = web_e2e.run
        web_e2e.run = lambda args: seen.setdefault("camera", args.camera) and 0
        try:
            web_e2e.main(["--all"])
            self.assertEqual(seen.pop("camera"), "pattern")
            web_e2e.main(["--all", "--camera", "default"])
            self.assertEqual(seen.pop("camera"), "default")
        finally:
            web_e2e.run = orig

    def test_chrome_args(self):
        args = web_e2e.chrome_args("/c", Path("/tmp/p"))
        for f in ("--headless=new", "--remote-debugging-pipe", "--use-fake-device-for-media-stream",
                  "--allow-loopback-in-peer-connection", "--user-data-dir=/tmp/p"):
            self.assertIn(f, args)
        self.assertNotIn("--headless=new", web_e2e.chrome_args("/c", Path("/tmp/p"), headful=True))

    def test_write_report(self):
        with tempfile.TemporaryDirectory() as t:
            p = Path(t) / "r" / "web-e2e.json"
            sc = [{"name": "a", "pass": True, "steps": [{"id": "x", "pass": True, "errors": [], "screenshot": None}]},
                  {"name": "b", "pass": False, "steps": []}]
            rep = web_e2e.write_report(p, "neutral", sc)
            self.assertFalse(rep["pass"])
            self.assertEqual(json.loads(p.read_text()), {"mode": "neutral", "pass": False, "scenarios": sc})
            self.assertTrue(web_e2e.write_report(p, "art", sc[:1])["pass"])


class TestPatternTests(unittest.TestCase):
    def test_frame_size(self):
        self.assertEqual(web_e2e.y4m_frame_size(640, 480), 640 * 480 * 3 // 2)
        self.assertEqual(web_e2e.y4m_frame_size(5, 3), 15 + 2 * 3 * 2)

    def test_rgb_to_ycbcr(self):
        self.assertEqual(web_e2e.rgb_to_ycbcr(255, 255, 255), (255, 128, 128))
        self.assertEqual(web_e2e.rgb_to_ycbcr(0, 0, 0), (0, 128, 128))
        y, cb, cr = web_e2e.rgb_to_ycbcr(255, 0, 0)
        self.assertEqual((y, cb, cr), (76, 85, 255))

    def test_file_layout(self):
        with tempfile.TemporaryDirectory() as t:
            p = web_e2e.write_test_pattern(Path(t) / "sub" / "pattern.y4m")
            data = p.read_bytes()
            header, rest = data.split(b"\n", 1)
            self.assertEqual(header, b"YUV4MPEG2 W640 H480 F15:1 Ip A1:1 C420jpeg")
            fs = 6 + web_e2e.y4m_frame_size(640, 480)
            self.assertEqual(len(rest), 30 * fs)
            for i in range(30):
                self.assertEqual(rest[i * fs:i * fs + 6], b"FRAME\n")
            y = rest[6:6 + 640 * 480]
            # first bar (white) on the left, last bar (black) on the right, away from the lines
            self.assertEqual(y[100 * 640 + 20], 255)
            self.assertEqual(y[100 * 640 + 620], 0)
            # grid line at x = 40, diagonal through the centre
            self.assertEqual(y[100 * 640 + 40], 128)
            self.assertEqual(y[239 * 640 + 319], 255)
            # frames differ (moving marker), so the stream is visibly live
            self.assertNotEqual(rest[6:fs], rest[fs + 6:2 * fs])

    def test_small_custom_size(self):
        with tempfile.TemporaryDirectory() as t:
            p = web_e2e.write_test_pattern(Path(t) / "p.y4m", width=64, height=48, fps=10, frames=3)
            header, rest = p.read_bytes().split(b"\n", 1)
            self.assertEqual(header, b"YUV4MPEG2 W64 H48 F10:1 Ip A1:1 C420jpeg")
            self.assertEqual(len(rest), 3 * (6 + web_e2e.y4m_frame_size(64, 48)))


class PageErrorTests(unittest.TestCase):
    LOG = [
        "09:00:00.001 console.log: boot\n",
        "09:00:00.002 log.error: Failed to load resource: the server responded with a status of 404 (Not Found) "
        "http://127.0.0.1:1/css/art/ui/TableTop2_Albedo.png\n",
        "09:00:00.003 log.warning: something\n",
        "09:00:00.004 exception: TypeError: x is undefined\n",
        "09:00:00.005 console.error: not collected\n",
        "garbage line exception: no stamp prefix\n",
    ]

    def test_from_lines(self):
        got = web_e2e.page_errors_from_lines(self.LOG, "page-0")
        self.assertEqual(got, [
            "page-0: log.error: Failed to load resource: the server responded with a status of 404 (Not Found) "
            "http://127.0.0.1:1/css/art/ui/TableTop2_Albedo.png",
            "page-0: exception: TypeError: x is undefined",
        ])

    def test_collect_from_directory(self):
        with tempfile.TemporaryDirectory() as t:
            t = Path(t)
            (t / "page-1.log").write_text("09:00:00.000 exception: B\n")
            (t / "page-0.log").write_text("".join(self.LOG))
            (t / "console-stdout.log").write_text("09:00:00.000 exception: not a page\n")
            got = web_e2e.collect_page_errors(t)
            self.assertEqual(len(got), 3)
            self.assertTrue(got[0].startswith("page-0: log.error"))
            self.assertEqual(got[-1], "page-1: exception: B")
            self.assertEqual(web_e2e.collect_page_errors(t / "missing"), [])


if __name__ == "__main__":
    unittest.main()
