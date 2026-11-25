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

    def test_changed_apk_unzipped_again(self):
        apk = self.root / "a.apk"
        apk.write_bytes(apk_bytes({"one": "1"}))
        extract_app.unzip_apk(apk, self.root / "apk")
        apk.write_bytes(apk_bytes({"two": "2", "pad": "x" * 100}))
        extract_app.unzip_apk(apk, self.root / "apk")
        self.assertTrue((self.root / "apk/two").is_file())
        self.assertFalse((self.root / "apk/one").exists())

    def test_il2cppdumper_invoked_with_arm64_lib_and_metadata(self):
        apk_dir, tool, out = self.root / "apk", self.root / "tool", self.root / "out"
        tool.mkdir()
        (tool / "Il2CppDumper.dll").write_text("")
        (tool / "config.json").write_text('{"RequireAnyKey": true, "DumpMethod": true}')

        def fake_run(argv, **kw):
            o = Path(argv[4])
            o.mkdir(parents=True, exist_ok=True)
            for f in ("dump.cs", "script.json", "il2cpp.h"):
                (o / f).write_text("")
            (o / "DummyDll").mkdir()
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

    def test_partial_dump_is_redone(self):
        tool, out = self.root / "tool", self.root / "out"
        tool.mkdir()
        calls = []

        def crash_after_dump_cs(argv, **kw):
            calls.append(argv)
            o = Path(argv[4])
            o.mkdir(parents=True, exist_ok=True)
            (o / "dump.cs").write_text("partial")
            if len(calls) == 1:
                return mock.Mock(returncode=134, stdout="", stderr="killed")
            for f in ("script.json", "il2cpp.h"):
                (o / f).write_text("x")
            (o / "DummyDll").mkdir(exist_ok=True)
            return mock.Mock(returncode=0, stdout="", stderr="")

        with mock.patch("shutil.which", return_value="/usr/bin/dotnet"), \
             mock.patch("subprocess.run", side_effect=crash_after_dump_cs):
            with self.assertRaises(RuntimeError):
                extract_app.run_il2cppdumper(self.root, tool, out)
            self.assertFalse((out / "dump.cs").exists())
            extract_app.run_il2cppdumper(self.root, tool, out)
        self.assertEqual(len(calls), 2)
        self.assertTrue((out / "script.json").is_file())

    def test_missing_dotnet_clear_error(self):
        with mock.patch("shutil.which", return_value=None):
            with self.assertRaises(RuntimeError) as cm:
                extract_app.run_il2cppdumper(self.root, self.root, self.root / "o")
        self.assertIn("dotnet", str(cm.exception))


if __name__ == "__main__":
    unittest.main()
