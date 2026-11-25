import tempfile
import unittest
from pathlib import Path

from pipeline import build_all


class PrepareProjectTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        r = Path(self.tmp.name)
        self.pristine, self.project, self.pdir = r / "pristine", r / "project", r / "patches"
        (self.pristine / "Assets").mkdir(parents=True)
        (self.pristine / "Assets/A.cs").write_text("class A {}\n")
        self.pdir.mkdir()

    def tearDown(self):
        self.tmp.cleanup()

    def test_first_prepare_clones_pristine(self):
        self.assertTrue(build_all.prepare_project(self.pristine, self.project, self.pdir))
        self.assertEqual((self.project / "Assets/A.cs").read_text(), "class A {}\n")

    def test_unchanged_patches_skip(self):
        build_all.prepare_project(self.pristine, self.project, self.pdir)
        (self.project / "Assets/A.cs").write_text("edited in editor\n")
        self.assertFalse(build_all.prepare_project(self.pristine, self.project, self.pdir))
        self.assertEqual((self.project / "Assets/A.cs").read_text(), "edited in editor\n")

    def test_changed_patches_rebuild_and_keep_library(self):
        build_all.prepare_project(self.pristine, self.project, self.pdir)
        (self.project / "Library").mkdir()
        (self.project / "Library/cache").write_text("big")
        (self.pdir / "010-x.patch").write_text(
            "--- a/Assets/A.cs\n+++ b/Assets/A.cs\n@@ -1 +1 @@\n-class A {}\n+class A { int x; }\n")
        (self.pdir / "010-x.md").write_text("x")
        self.assertTrue(build_all.prepare_project(self.pristine, self.project, self.pdir))
        self.assertEqual((self.project / "Assets/A.cs").read_text(), "class A { int x; }\n")
        self.assertEqual((self.project / "Library/cache").read_text(), "big")


class UnityEditorCheckTests(unittest.TestCase):
    def test_missing_editor_names_key(self):
        from pipeline.config import ConfigError
        with self.assertRaises(ConfigError) as cm:
            build_all.check_unity_editor(None)
        self.assertEqual(cm.exception.key, "unity_editor")

    def test_app_bundle_instead_of_binary_rejected(self):
        from pipeline.config import ConfigError
        with tempfile.TemporaryDirectory() as t:
            app = Path(t) / "Unity.app"
            app.mkdir()
            with self.assertRaises(ConfigError) as cm:
                build_all.check_unity_editor(app)
            self.assertIn("Contents/MacOS/Unity", cm.exception.message)

    def test_executable_accepted(self):
        with tempfile.TemporaryDirectory() as t:
            exe = Path(t) / "Unity"
            exe.write_text("#!/bin/sh\n")
            exe.chmod(0o755)
            build_all.check_unity_editor(exe)


if __name__ == "__main__":
    unittest.main()
