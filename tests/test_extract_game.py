import tempfile
import unittest
from pathlib import Path

from pipeline import extract_game


class ExtractGameTests(unittest.TestCase):
    def test_streaming_assets_cloned_into_project(self):
        with tempfile.TemporaryDirectory() as t:
            t = Path(t)
            sa = t / "Media/StreamingAssets/Localisation"
            sa.mkdir(parents=True)
            (sa / "en_text.loc").write_bytes(b"loc")
            proj = t / "export/ExportedProject"
            (proj / "Assets").mkdir(parents=True)
            n = extract_game.clone_streaming_assets(t / "Media", proj)
            self.assertEqual(n, 1)
            self.assertEqual((proj / "Assets/StreamingAssets/Localisation/en_text.loc").read_bytes(), b"loc")

    def test_settings_skip_streaming_assets_and_decompile(self):
        s = extract_game.GAME_SETTINGS
        self.assertTrue(s["IgnoreStreamingAssets"])
        self.assertEqual(s["ScriptExportMode"], "Decompiled")
        self.assertEqual(s["ScriptContentLevel"], "Level2")


if __name__ == "__main__":
    unittest.main()
