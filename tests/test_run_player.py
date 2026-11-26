import json
import plistlib
import tempfile
import unittest
from pathlib import Path

from pipeline import run_player

PLAYER_LOG = """\
Mono path[0] = '/x/Data/Managed'
[PortObserve] scene loaded: LanguageSelect (index 0, Single) t=1.20
NullReferenceException: Object reference not set to an instance of an object
  at Menu.Update () [0x00000] in <abc>:0

NullReferenceException: Object reference not set to an instance of an object
  at Menu.Update () [0x00000] in <abc>:0

[PortObserve] scene loaded: Loading (index 1, Single) t=5.00
Shader 'Wish/Water': fallback shader 'Diffuse' not found
"""


class ParsePlayerLogTests(unittest.TestCase):
    def test_exceptions_counted_and_scenes_listed(self):
        r = run_player.parse_player_log(PLAYER_LOG)
        self.assertEqual(r["scenes"], ["LanguageSelect (index 0, Single) t=1.20", "Loading (index 1, Single) t=5.00"])
        self.assertEqual(r["problems"].most_common(1)[0],
                         ("NullReferenceException: Object reference not set to an instance of an object", 2))

    def test_crash_marker(self):
        r = run_player.parse_player_log("Obtained 12 stack frames.\nReceived signal SIGSEGV\n")
        self.assertTrue(r["crashed"])


class ParseEventsTests(unittest.TestCase):
    def test_jsonl_errors_and_exceptions_dedup(self):
        lines = [
            {"t": 1.0, "kind": "log", "type": "Exception", "msg": "NullReferenceException: boom"},
            {"t": 2.0, "kind": "log", "type": "Exception", "msg": "NullReferenceException: boom"},
            {"t": 2.5, "kind": "log", "type": "Error", "msg": "Wwise: init failed"},
            {"t": 3.0, "kind": "scene", "msg": "Loading (index 1, Single)"},
            "not json",
        ]
        text = "\n".join(l if isinstance(l, str) else json.dumps(l) for l in lines)
        r = run_player.parse_events(text)
        self.assertEqual(r["problems"]["[Exception] NullReferenceException: boom"], 2)
        self.assertEqual(r["problems"]["[Error] Wwise: init failed"], 1)
        self.assertEqual(r["scenes"], ["Loading (index 1, Single) t=3.00"])


class ArgvTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.app = Path(self.tmp.name) / "ThatsYou.app"
        (self.app / "Contents/MacOS").mkdir(parents=True)
        (self.app / "Contents/MacOS/That's You!").write_text("")
        with open(self.app / "Contents/Info.plist", "wb") as f:
            plistlib.dump({"CFBundleExecutable": "That's You!"}, f)

    def tearDown(self):
        self.tmp.cleanup()

    def test_executable_from_info_plist(self):
        self.assertEqual(run_player.find_executable(self.app), self.app / "Contents/MacOS/That's You!")

    def test_executable_missing_raises(self):
        with self.assertRaises(FileNotFoundError):
            run_player.find_executable(Path(self.tmp.name) / "Nope.app")

    def test_player_argv(self):
        argv = run_player.player_argv(Path("/a/exe"), Path("/r/x"), keys="3:cross,5:Return")
        self.assertEqual(argv, ["/a/exe", "-logFile", "/r/x/player.log", "-portObserve", "/r/x/shots",
                                "-portKeys", "3:cross,5:Return", "-screen-width", "1280",
                                "-screen-height", "720", "-screen-fullscreen", "0"])

    def test_player_argv_without_keys(self):
        argv = run_player.player_argv(Path("/a/exe"), Path("/r/x"))
        self.assertNotIn("-portKeys", argv)

    def test_validate_keys(self):
        self.assertEqual(run_player.validate_keys(" 3:cross , 5.5:Return "), "3:cross,5.5:Return")
        for bad in ("cross", "x:cross", "3:"):
            with self.assertRaises(ValueError):
                run_player.validate_keys(bad)

    def test_player_argv_shot_flags(self):
        argv = run_player.player_argv(Path("/a/exe"), Path("/r/x"), shot_interval=0.5, key_shots=True)
        i = argv.index("-portShotInterval")
        self.assertEqual(argv[i + 1], "0.5")
        self.assertIn("-portKeyShots", argv)
        default = run_player.player_argv(Path("/a/exe"), Path("/r/x"))
        self.assertNotIn("-portShotInterval", default)
        self.assertNotIn("-portKeyShots", default)

    def test_player_argv_extra_appended(self):
        extra = run_player.split_extra("-portCameraHdr -portNoLightShadows 'Spot,Point' -x \"a b\"")
        self.assertEqual(extra, ["-portCameraHdr", "-portNoLightShadows", "Spot,Point", "-x", "a b"])
        argv = run_player.player_argv(Path("/a/exe"), Path("/r/x"), extra=extra)
        self.assertEqual(argv[-5:], extra)
        self.assertEqual(argv[-7:-5], ["-screen-fullscreen", "0"])
        self.assertEqual(run_player.split_extra(None), [])
        self.assertEqual(run_player.split_extra(""), [])
        with self.assertRaises(ValueError):
            run_player.split_extra("-x 'unbalanced")

    def test_extra_option_accepts_dash_value(self):
        a = run_player.build_parser().parse_args(
            run_player.join_extra_values(["--extra", "-portCameraHdr", "--seconds", "5"]))
        self.assertEqual(a.extra, "-portCameraHdr")
        self.assertEqual(a.seconds, 5.0)
        a = run_player.build_parser().parse_args(run_player.join_extra_values(["--extra=-portNoShadows"]))
        self.assertEqual(a.extra, "-portNoShadows")

    def test_validate_shot_interval(self):
        self.assertEqual(run_player.validate_shot_interval(0.25), 0.25)
        for bad in (0.0, -1.0, 0.05, float("nan")):
            with self.assertRaises(ValueError):
                run_player.validate_shot_interval(bad)

    def test_shot_pseudo_key_accepted(self):
        self.assertEqual(run_player.validate_keys("22:shot,22.5:cross"), "22:shot,22.5:cross")

    def test_caffeinate_argv(self):
        argv = run_player.caffeinate_argv(123)
        if argv is not None:
            self.assertEqual(argv[1:], ["-d", "-i", "-u", "-w", "123"])

    def test_stall_watch(self):
        shots = Path(self.tmp.name) / "shots"
        shots.mkdir()
        w = run_player.StallWatch(shots, interval=2.0, limit=8.0, first=20.0)
        self.assertIsNone(w.check(0.0, 0.0))
        self.assertIsNone(w.check(10.0, 10.0))
        (shots / "0001_LanguageSelect_scene.png").write_text("")
        (shots / "0002_LanguageSelect_timer.png").write_text("")
        self.assertIsNone(w.check(11.0, 11.0))
        self.assertIsNone(w.check(18.0, 18.0))
        (shots / "0003_LanguageSelect_key.png").write_text("")
        msg = w.check(19.5, 19.5)
        self.assertIn("player stalled", msg)
        self.assertIsNone(w.check(25.0, 25.0))
        (shots / "0004_LanguageSelect_timer.png").write_text("")
        self.assertIn("resumed", w.check(26.0, 26.0))
        self.assertEqual(len(w.warnings), 2)

    def test_stall_watch_no_timer_shot_at_all(self):
        shots = Path(self.tmp.name) / "shots"
        shots.mkdir()
        w = run_player.StallWatch(shots, interval=2.0, limit=8.0, first=20.0)
        self.assertIsNone(w.check(19.0, 19.0))
        self.assertIn("no timer screenshot yet", w.check(20.5, 20.5))
        self.assertIsNone(w.check(30.0, 30.0))

    def test_report_lists_warnings(self):
        run_dir = Path(self.tmp.name) / "run"
        run_dir.mkdir()
        text = run_player.report(run_dir, None, warnings=["WARNING t~9s: stalled"])
        self.assertIn("  WARNING t~9s: stalled", text.splitlines()[1])

    def test_screenshots_sorted(self):
        shots = Path(self.tmp.name) / "shots"
        shots.mkdir()
        for n in ("0002_Loading_timer.png", "0001_LanguageSelect_scene.png", "log.jsonl"):
            (shots / n).write_text("")
        self.assertEqual([p.name for p in run_player.screenshots(shots)],
                         ["0001_LanguageSelect_scene.png", "0002_Loading_timer.png"])


if __name__ == "__main__":
    unittest.main()
