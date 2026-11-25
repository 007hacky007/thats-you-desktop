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


if __name__ == "__main__":
    unittest.main()
