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

    def test_screenshots_sorted(self):
        shots = Path(self.tmp.name) / "shots"
        shots.mkdir()
        for n in ("0002_Loading_timer.png", "0001_LanguageSelect_scene.png", "log.jsonl"):
            (shots / n).write_text("")
        self.assertEqual([p.name for p in run_player.screenshots(shots)],
                         ["0001_LanguageSelect_scene.png", "0002_Loading_timer.png"])


if __name__ == "__main__":
    unittest.main()
