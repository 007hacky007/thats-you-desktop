import tempfile
import unittest
from pathlib import Path

from builder import package

REPO = Path(__file__).resolve().parent.parent


class DataFilesTest(unittest.TestCase):
    def test_only_public_sources_are_bundled(self):
        files = package.data_files(REPO)
        dests = [d for _s, d in files]
        srcs = [Path(s) for s, _d in files]
        self.assertTrue(any(d.startswith("pipeline/") for d in dests))
        self.assertTrue(any(d.startswith("web/") for d in dests))
        self.assertTrue(any(d.startswith("unity/Port/") for d in dests))
        self.assertIn("pipeline/versions.json", dests)
        self.assertIn("pipeline/wrangler/package-lock.json", dests)
        self.assertIn("native/iwyknet/unity/iwyknet.dylib.meta", dests)
        for bad in ("work/", "local-patches/", "config/local.json", "web/test/", "tests/", ".git/"):
            self.assertFalse(any(d.startswith(bad) for d in dests), bad)
        self.assertFalse(any("__pycache__" in d or d.endswith(".pyc") for d in dests))
        for s in srcs:
            self.assertTrue(s.is_file(), s)

    def test_private_host_check(self):
        with tempfile.TemporaryDirectory() as d:
            a = Path(d) / "a.txt"
            a.write_text("see https://secret.example.dev/ for the page")
            b = Path(d) / "b.txt"
            b.write_text("nothing here")
            self.assertEqual(package.files_containing([(str(a), "a.txt"), (str(b), "b.txt")], "secret.example.dev"),
                             ["a.txt"])

    def test_artifact_names(self):
        self.assertEqual(package.artifact_name("macos-arm64"), "ThatsYou-Builder-macOS.dmg")
        self.assertEqual(package.artifact_name("win-x64"), "ThatsYou-Builder-Windows.exe")


if __name__ == "__main__":
    unittest.main()


class LaunchTest(unittest.TestCase):
    def test_worker_processes_of_the_packaged_app_do_not_rerun_the_builder(self):
        # the audio step uses spawn worker processes; in a frozen app they start the app itself
        src = (REPO / "builder" / "launch.py").read_text()
        self.assertIn("multiprocessing.freeze_support()", src)
        self.assertLess(src.index("freeze_support()"), src.index("main()"))
