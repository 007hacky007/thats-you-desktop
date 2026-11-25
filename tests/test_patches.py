import subprocess
import tempfile
import unittest
from pathlib import Path

from pipeline import patches
from pipeline.patches import PatchError


class PatchTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        r = Path(self.tmp.name)
        self.pristine, self.project, self.pdir = r / "game-pristine", r / "game", r / "local patches"
        for d in (self.pristine, self.project):
            (d / "Assets/Scripts").mkdir(parents=True)
            (d / "Assets/Scripts/A.cs").write_text("class A {\n  int x = 1;\n}\n")
            (d / "Assets/Scripts/B.cs").write_text("class B {}\n")
        self.pdir.mkdir()

    def tearDown(self):
        self.tmp.cleanup()

    def edit_and_make(self, name, old, new, md=True):
        f = self.project / "Assets/Scripts/A.cs"
        f.write_text(f.read_text().replace(old, new))
        p = patches.make(self.project, name, pristine=self.pristine, patch_dir=self.pdir)
        if not md:
            p.with_suffix(".md").unlink()
        # reset project to pristine for the apply test
        f.write_text((self.pristine / "Assets/Scripts/A.cs").read_text())
        return p

    def test_make_writes_patch_and_md_template(self):
        p = self.edit_and_make("010-x", "x = 1", "x = 2")
        self.assertIn("+  int x = 2;", p.read_text())
        self.assertIn("a/Assets/Scripts/A.cs", p.read_text())
        self.assertTrue(p.with_suffix(".md").is_file())

    def test_apply_in_order(self):
        a_pr, a_pj = self.pristine / "Assets/Scripts/A.cs", self.project / "Assets/Scripts/A.cs"
        v = lambda n: f"class A {{\n  int x = {n};\n}}\n"
        a_pj.write_text(v(2))
        patches.make(self.project, "010-first", pristine=self.pristine, patch_dir=self.pdir)
        a_pr.write_text(v(2))
        a_pj.write_text(v(3))
        patches.make(self.project, "020-second", pristine=self.pristine, patch_dir=self.pdir)
        a_pr.write_text(v(1))
        a_pj.write_text(v(1))
        applied = patches.apply_all(self.project, self.pdir)
        self.assertEqual(applied, ["010-first", "020-second"])
        self.assertEqual(a_pj.read_text(), v(3))

    def test_added_and_deleted_files(self):
        (self.project / "Assets/Scripts/New.cs").write_text("class N {}\n")
        (self.project / "Assets/Scripts/B.cs").unlink()
        patches.make(self.project, "030-add-del", pristine=self.pristine, patch_dir=self.pdir)
        (self.project / "Assets/Scripts/New.cs").unlink()
        (self.project / "Assets/Scripts/B.cs").write_text("class B {}\n")
        patches.apply_all(self.project, self.pdir)
        self.assertTrue((self.project / "Assets/Scripts/New.cs").is_file())
        self.assertFalse((self.project / "Assets/Scripts/B.cs").exists())

    def test_failed_patch_raises_with_name(self):
        self.edit_and_make("010-x", "x = 1", "x = 2")
        (self.project / "Assets/Scripts/A.cs").write_text("totally different\n")
        with self.assertRaises(PatchError) as cm:
            patches.apply_all(self.project, self.pdir)
        self.assertEqual(cm.exception.name, "010-x")

    def test_patch_without_md_rejected(self):
        self.edit_and_make("010-x", "x = 1", "x = 2", md=False)
        with self.assertRaises(PatchError) as cm:
            patches.apply_all(self.project, self.pdir)
        self.assertIn(".md", str(cm.exception))

    def test_no_changes_makes_nothing(self):
        with self.assertRaises(PatchError):
            patches.make(self.project, "040-empty", pristine=self.pristine, patch_dir=self.pdir)


if __name__ == "__main__":
    unittest.main()
