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
        p = patches.make(self.project, name, ["Assets/Scripts/A.cs"], pristine=self.pristine, patch_dir=self.pdir)
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
        patches.make(self.project, "010-first", ["Assets/Scripts/A.cs"], pristine=self.pristine, patch_dir=self.pdir)
        a_pr.write_text(v(2))
        a_pj.write_text(v(3))
        patches.make(self.project, "020-second", ["Assets/Scripts/A.cs"], pristine=self.pristine, patch_dir=self.pdir)
        a_pr.write_text(v(1))
        a_pj.write_text(v(1))
        applied = patches.apply_all(self.project, self.pdir)
        self.assertEqual(applied, ["010-first", "020-second"])
        self.assertEqual(a_pj.read_text(), v(3))

    def test_added_and_deleted_files(self):
        (self.project / "Assets/Scripts/New.cs").write_text("class N {}\n")
        (self.project / "Assets/Scripts/B.cs").unlink()
        patches.make(self.project, "030-add-del", ["Assets/Scripts/New.cs", "Assets/Scripts/B.cs"], pristine=self.pristine, patch_dir=self.pdir)
        (self.project / "Assets/Scripts/New.cs").unlink()
        (self.project / "Assets/Scripts/B.cs").write_text("class B {}\n")
        patches.apply_all(self.project, self.pdir)
        self.assertTrue((self.project / "Assets/Scripts/New.cs").is_file())
        self.assertFalse((self.project / "Assets/Scripts/B.cs").exists())

    def test_apply_inside_an_enclosing_git_repo(self):
        # work/project lives inside the main repo; git apply must not resolve paths against it
        root = Path(self.tmp.name) / "outer"
        root.mkdir()
        subprocess.run(["git", "init", "-q", str(root)], check=True)
        inner_pr, inner_pj = root / "work/pristine", root / "work/project"
        for d in (inner_pr, inner_pj):
            (d / "Assets").mkdir(parents=True)
            (d / "Assets/A.cs").write_text("one\n")
        (inner_pj / "Assets/A.cs").write_text("two\n")
        patches.make(inner_pj, "010-x", ["Assets/A.cs"], pristine=inner_pr, patch_dir=self.pdir)
        (inner_pj / "Assets/A.cs").write_text("one\n")
        patches.apply_all(inner_pj, self.pdir)
        self.assertEqual((inner_pj / "Assets/A.cs").read_text(), "two\n")

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

    def test_only_named_paths_captured(self):
        # Unity rewrites many files on import; only the files we name belong in the patch
        (self.project / "Assets/Scripts/B.cs").write_text("class B { /* touched by Unity */ }\n")
        (self.project / "Assets/Scripts/A.cs").write_text("class A {\n  int x = 9;\n}\n")
        p = patches.make(self.project, "050-named", ["Assets/Scripts/A.cs"], pristine=self.pristine, patch_dir=self.pdir)
        text = p.read_text()
        self.assertIn("Assets/Scripts/A.cs", text)
        self.assertNotIn("B.cs", text)

    def test_directory_path_captures_files_below(self):
        (self.project / "Assets/Scripts/A.cs").unlink()
        (self.project / "Assets/Scripts/B.cs").unlink()
        p = patches.make(self.project, "060-dir", ["Assets/Scripts"], pristine=self.pristine, patch_dir=self.pdir)
        self.assertEqual(p.read_text().count("deleted file mode"), 2)

    def test_relative_paths_give_clean_headers(self):
        import os
        (self.project / "Assets/Scripts/B.cs").unlink()
        cwd = os.getcwd()
        os.chdir(self.project.parent)
        try:
            p = patches.make(Path("game"), "070-rel", ["Assets/Scripts/B.cs"], pristine=Path("game-pristine"),
                             patch_dir=self.pdir)
        finally:
            os.chdir(cwd)
        self.assertIn("diff --git a/Assets/Scripts/B.cs b/Assets/Scripts/B.cs", p.read_text())
        self.assertNotIn("game-pristine", p.read_text())

    def test_no_changes_makes_nothing(self):
        with self.assertRaises(PatchError):
            patches.make(self.project, "040-empty", ["Assets/Scripts/A.cs"], pristine=self.pristine, patch_dir=self.pdir)


if __name__ == "__main__":
    unittest.main()
