import json
import os
import sys
import tempfile
import unittest
from pathlib import Path

from pipeline import assetripper
from pipeline.assetripper import ExportError

FAKE = [sys.executable, str(Path(__file__).parent / "fixtures" / "fake_assetripper.py")]


class AssetRipperTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        r = Path(self.tmp.name)
        self.inp, self.out, self.calls = r / "in put", r / "out", r / "calls.jsonl"
        self.inp.mkdir()
        os.environ["FAKE_AR_CALLS"] = str(self.calls)
        os.environ.pop("FAKE_AR_FAIL", None)

    def tearDown(self):
        os.environ.pop("FAKE_AR_FAIL", None)
        self.tmp.cleanup()

    def paths(self):
        return [json.loads(l)[0] for l in self.calls.read_text().splitlines()]

    def test_export_calls_reset_settings_load_then_export(self):
        assetripper.export(self.inp, self.out, {"ScriptExportMode": "Decompiled", "IgnoreStreamingAssets": True,
                                                "PublicizeAssemblies": False}, exe=FAKE)
        self.assertEqual(self.paths(), ["/Reset", "/Settings/Update", "/LoadFolder", "/Export/UnityProject"])
        lines = [json.loads(l) for l in self.calls.read_text().splitlines()]
        settings = lines[1][1]
        self.assertEqual(settings["ScriptExportMode"], ["Decompiled"])
        self.assertIn("IgnoreStreamingAssets", settings)
        self.assertNotIn("PublicizeAssemblies", settings)
        self.assertEqual(lines[2][1]["Path"], [str(self.inp)])
        self.assertTrue((self.out / "ExportedProject/Assets/a.cs").is_file())
        self.assertTrue((self.out / assetripper.MARKER).is_file())

    def test_marker_written_last(self):
        os.environ["FAKE_AR_FAIL"] = "export"
        with self.assertRaises(ExportError):
            assetripper.export(self.inp, self.out, {}, exe=FAKE)
        self.assertFalse((self.out / assetripper.MARKER).exists())

    def test_complete_export_skipped(self):
        assetripper.export(self.inp, self.out, {}, exe=FAKE)
        self.calls.unlink()
        assetripper.export(self.inp, self.out, {}, exe=FAKE)
        self.assertFalse(self.calls.exists())

    def test_changed_inputs_redo_export(self):
        assetripper.export(self.inp, self.out, {}, exe=FAKE, key="inputs-v1")
        self.calls.unlink()
        assetripper.export(self.inp, self.out, {}, exe=FAKE, key="inputs-v1")
        self.assertFalse(self.calls.exists())
        assetripper.export(self.inp, self.out, {}, exe=FAKE, key="inputs-v2")
        self.assertIn("/Export/UnityProject", self.paths())

    def test_process_stopped_on_error(self):
        os.environ["FAKE_AR_FAIL"] = "export"
        procs = []
        orig = assetripper._start
        assetripper._start = lambda *a: procs.append(orig(*a)) or procs[-1]
        try:
            with self.assertRaises(ExportError):
                assetripper.export(self.inp, self.out, {}, exe=FAKE)
        finally:
            assetripper._start = orig
        self.assertIsNotNone(procs[0].poll())


if __name__ == "__main__":
    unittest.main()
