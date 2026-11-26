import json
import tempfile
import unittest
from pathlib import Path

from pipeline import run_player


def render_line(scene="Home", t=5.0, reason="timer", no_fx=False, **extra):
    e = {
        "t": t, "kind": "render", "msg": "cams=3 effects=2", "reason": reason, "scene": scene,
        "noImageEffects": no_fx,
        "noImageEffectsOnly": None,
        "keepImageEffects": ["MenuRenderer", "ScreenCopier"] if no_fx else [],
        "cameras": [
            {"name": "Main Camera", "depth": 0, "clearFlags": "Skybox", "cullingMask": 1, "targetTexture": None,
             "rect": [0, 0, 1, 1], "visibleRenderers": 3, "shaders": ["Standard", "Custom/Floor"],
             "effects": [{"type": "UltimateBloom", "enabled": not no_fx, "probeDisabled": no_fx,
                          "shaders": ["Hidden/Ultimate/BloomCombine"]}]},
            {"name": "Menu Camera", "depth": 3, "clearFlags": "Depth", "cullingMask": 64, "targetTexture": None,
             "rect": [0, 0, 1, 1], "visibleRenderers": 1, "shaders": [],
             "effects": [{"type": "MenuRenderer", "enabled": True, "probeDisabled": False, "probeKept": no_fx,
                          "shaders": []}]},
            {"name": "UI Camera", "depth": 5, "clearFlags": "Depth", "cullingMask": 32, "targetTexture": None,
             "rect": [0, 0, 1, 1], "visibleRenderers": 4, "shaders": ["Unlit/Transparent Colored"], "effects": []},
        ],
        "ngui": {"type": "UIDrawCall", "drawCalls": 4,
                 "shaders": [{"name": "Unlit/Transparent Colored", "drawCalls": 4, "visible": 4}]},
        "shaders": [
            {"name": "Standard", "supported": True, "passCount": 6, "renderQueue": 2000},
            {"name": "Custom/Floor", "supported": False, "passCount": 1, "renderQueue": 2000},
            {"name": "Hidden/Ultimate/BloomCombine", "supported": True, "passCount": 1, "renderQueue": 2000},
            {"name": "Unlit/Transparent Colored", "supported": True, "passCount": 1, "renderQueue": 3000},
        ],
    }
    e.update(extra)
    return json.dumps(e)


class PlayerArgvTests(unittest.TestCase):
    def test_defaults_unchanged(self):
        argv = run_player.player_argv(Path("/a/exe"), Path("/r/x"))
        self.assertNotIn("-portSaveDir", argv)
        self.assertNotIn("-portNoImageEffects", argv)

    def test_save_dir_and_no_image_effects(self):
        argv = run_player.player_argv(Path("/a/exe"), Path("/r/x"), keys="3:cross",
                                      save_dir=Path("/r/x/save"), no_image_effects=True)
        self.assertEqual(argv, ["/a/exe", "-logFile", "/r/x/player.log", "-portObserve", "/r/x/shots",
                                "-portKeys", "3:cross", "-portSaveDir", "/r/x/save", "-portNoImageEffects",
                                "-screen-width", "1280", "-screen-height", "720", "-screen-fullscreen", "0"])

    def test_fx_only_and_keep(self):
        argv = run_player.player_argv(Path("/a/exe"), Path("/r/x"), no_image_effects=True,
                                      fx_only="UltimateBloom,MenuRenderer", fx_keep="AmplifyColorEffect")
        i = argv.index("-portNoImageEffects")
        self.assertEqual(argv[i:i + 5], ["-portNoImageEffects", "-portNoImageEffectsOnly", "UltimateBloom,MenuRenderer",
                                         "-portKeepImageEffects", "AmplifyColorEffect"])

    def test_fx_lists_need_no_image_effects(self):
        argv = run_player.player_argv(Path("/a/exe"), Path("/r/x"), fx_only="UltimateBloom", fx_keep="X")
        self.assertNotIn("-portNoImageEffectsOnly", argv)
        self.assertNotIn("-portKeepImageEffects", argv)
        self.assertEqual(run_player.main(["--fx-only", "UltimateBloom", "--app", "/nonexistent.app"]), 2)

    def test_validate_types(self):
        self.assertEqual(run_player.validate_types(" UltimateBloom, UnityStandardAssets.ImageEffects.Bloom;UltimateBloom,"),
                         "UltimateBloom,UnityStandardAssets.ImageEffects.Bloom")
        for bad in ("", " , ", "Ultimate Bloom", "-portKeys"):
            with self.assertRaises(ValueError):
                run_player.validate_types(bad)


class DummyShaderTests(unittest.TestCase):
    def test_scan_resources(self):
        with tempfile.TemporaryDirectory() as tmp:
            res = Path(tmp) / "Assets" / "Resources"
            (res / "Sub").mkdir(parents=True)
            (res / "Bloom.shader").write_text('Shader "Hidden/Ultimate/BloomCombine" {\n\t//DummyShaderTextExporter\n}\n')
            (res / "Sub" / "Real.shader").write_text('Shader "Custom/Real" {\n SubShader { Pass { } }\n}\n')
            (res / "Sub" / "Fake.shader").write_text('// header\nShader "Hidden/Fake" {\n//DummyShaderTextExporter\n}\n')
            (res / "Bloom.shader.meta").write_text("DummyShaderTextExporter")
            self.assertEqual(run_player.dummy_shaders(Path(tmp)),
                             {"Hidden/Ultimate/BloomCombine": "Bloom.shader", "Hidden/Fake": "Sub/Fake.shader"})

    def test_missing_project(self):
        self.assertEqual(run_player.dummy_shaders(Path("/nonexistent/project")), {})


class RenderSummaryTests(unittest.TestCase):
    def test_parse_keeps_render_events_only(self):
        text = "\n".join([render_line(), json.dumps({"kind": "probe"}), "{bad", "[1]"])
        self.assertEqual(len(run_player.parse_render_events(text)), 1)

    def test_summary_flags_dummies_and_unsupported(self):
        events = run_player.parse_render_events("\n".join([render_line("LanguageSelect", 1.0), render_line()]))
        lines = run_player.render_summary(events, {"Hidden/Ultimate/BloomCombine": "UltimateBloomCombine.shader"})
        text = "\n".join(lines)
        self.assertIn("render summary for Home: 1 events", lines[0])
        self.assertIn("shaders (4, 1 dummy):", text)
        bloom = next(l for l in lines if l.strip().startswith("Hidden/Ultimate/BloomCombine "))
        self.assertIn("DUMMY UltimateBloomCombine.shader", bloom)
        self.assertIn("fx:UltimateBloom", bloom)
        self.assertIn("effect UltimateBloom (on): Hidden/Ultimate/BloomCombine [DUMMY]", text)
        floor = next(l for l in lines if l.strip().startswith("Custom/Floor "))
        self.assertIn("UNSUPPORTED", floor)
        ui = next(l for l in lines if l.strip().startswith("Unlit/Transparent Colored "))
        self.assertIn("ngui", ui)
        self.assertNotIn("DUMMY", ui)
        self.assertIn("ngui: 4 draw calls, 1 shaders", text)

    def test_summary_probe_disabled(self):
        lines = run_player.render_summary(run_player.parse_render_events(render_line(no_fx=True)), {})
        self.assertIn("image effects disabled", lines[0])
        self.assertIn("kept MenuRenderer,ScreenCopier", lines[0])
        self.assertNotIn("only", lines[0])
        text = "\n".join(lines)
        self.assertIn("effect UltimateBloom (probe-disabled)", text)
        self.assertIn("effect MenuRenderer (probe-kept)", text)

    def test_summary_fx_only(self):
        line = render_line(no_fx=True, noImageEffectsOnly=["UltimateBloom"], keepImageEffects=[])
        lines = run_player.render_summary(run_player.parse_render_events(line), {})
        self.assertIn("(-portNoImageEffects, only UltimateBloom)", lines[0])

    def test_summary_effects_on(self):
        text = "\n".join(run_player.render_summary(run_player.parse_render_events(render_line()), {}))
        self.assertIn("effect MenuRenderer (on)", text)
        self.assertNotIn("image effects disabled", text)

    def test_summary_without_home(self):
        lines = run_player.render_summary(run_player.parse_render_events(render_line("Loading")), {})
        self.assertEqual(len(lines), 1)
        self.assertIn("no render event in Home", lines[0])
        self.assertIn("Loading", lines[0])

    def test_report_includes_save_and_summary(self):
        with tempfile.TemporaryDirectory() as tmp:
            run_dir = Path(tmp)
            (run_dir / "shots").mkdir()
            (run_dir / "save" / "SavedGame").mkdir(parents=True)
            (run_dir / "save" / "SavedGame" / "unitySaveData").write_bytes(b"x")
            (run_dir / "shots" / "render.jsonl").write_text(render_line() + "\n")
            out = run_player.report(run_dir, None, dummies={})
            self.assertIn("SavedGame/unitySaveData", out)
            self.assertIn("render summary for Home", out)
            self.assertNotIn("render summary", run_player.report(run_dir, None))


if __name__ == "__main__":
    unittest.main()
