import io
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest import mock

from pipeline import extract_app


def apk_bytes(entries: dict) -> bytes:
    b = io.BytesIO()
    with zipfile.ZipFile(b, "w") as z:
        for k, v in entries.items():
            z.writestr(k, v)
    return b.getvalue()


class ExtractAppTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)

    def tearDown(self):
        self.tmp.cleanup()

    def test_unzip_paths_safe(self):
        apk = self.root / "a (1).apk"
        apk.write_bytes(apk_bytes({"../evil": "x", "ok": "y"}))
        with self.assertRaises(ValueError):
            extract_app.unzip_apk(apk, self.root / "apk")
        self.assertFalse((self.root / "evil").exists())

    def test_unzip_extracts(self):
        apk = self.root / "a.apk"
        apk.write_bytes(apk_bytes({"lib/arm64-v8a/libil2cpp.so": "so"}))
        extract_app.unzip_apk(apk, self.root / "apk")
        self.assertTrue((self.root / "apk/lib/arm64-v8a/libil2cpp.so").is_file())

    def test_il2cppdumper_invoked_with_arm64_lib_and_metadata(self):
        apk_dir, tool, out = self.root / "apk", self.root / "tool", self.root / "out"
        tool.mkdir()
        (tool / "Il2CppDumper.dll").write_text("")
        (tool / "config.json").write_text('{"RequireAnyKey": true, "DumpMethod": true}')

        def fake_run(argv, **kw):
            (out / "dump.cs").write_text("")
            return mock.Mock(returncode=0, stdout="", stderr="")

        with mock.patch("shutil.which", return_value="/usr/bin/dotnet"), \
             mock.patch("subprocess.run", side_effect=fake_run) as run:
            extract_app.run_il2cppdumper(apk_dir, tool, out)
        import json
        cfg = json.loads((tool / "config.json").read_text())
        self.assertFalse(cfg["RequireAnyKey"])
        self.assertTrue(cfg["DumpMethod"])
        argv = run.call_args.args[0]
        self.assertEqual(argv[:2], ["/usr/bin/dotnet", str(tool / "Il2CppDumper.dll")])
        self.assertEqual(argv[2], str(apk_dir / "lib/arm64-v8a/libil2cpp.so"))
        self.assertEqual(argv[3], str(apk_dir / "assets/bin/Data/Managed/Metadata/global-metadata.dat"))
        self.assertEqual(run.call_args.kwargs["env"]["DOTNET_ROLL_FORWARD"], "Major")

    def test_missing_dotnet_clear_error(self):
        with mock.patch("shutil.which", return_value=None):
            with self.assertRaises(RuntimeError) as cm:
                extract_app.run_il2cppdumper(self.root, self.root, self.root / "o")
        self.assertIn("dotnet", str(cm.exception))


if __name__ == "__main__":
    unittest.main()
