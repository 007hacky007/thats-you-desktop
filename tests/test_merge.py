import hashlib
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from pipeline import merge


def tree_hash(root: Path) -> str:
    h = hashlib.sha256()
    for p in sorted(root.rglob("*")):
        if p.is_file():
            h.update(str(p.relative_to(root)).encode())
            h.update(p.read_bytes())
    return h.hexdigest()


class MergeTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        r = Path(self.tmp.name)
        self.base, self.patch, self.out = r / "base dir", r / "patch (1)", r / "out put"
        for d, files in {
            self.base: {"Media/a.dll": b"base-a", "Media/only_base": b"b", "eboot.bin": b"elf"},
            self.patch: {"Media/a.dll": b"patch-a", "Media/level14": b"new", "sce_discmap_patch.plt": b"p"},
        }.items():
            for rel, data in files.items():
                f = d / rel
                f.parent.mkdir(parents=True, exist_ok=True)
                f.write_bytes(data)

    def tearDown(self):
        self.tmp.cleanup()

    def test_patch_file_wins(self):
        merge.merge(self.base, self.patch, self.out)
        self.assertEqual((self.out / "Media/a.dll").read_bytes(), b"patch-a")

    def test_base_only_file_kept(self):
        merge.merge(self.base, self.patch, self.out)
        self.assertEqual((self.out / "Media/only_base").read_bytes(), b"b")
        self.assertEqual((self.out / "eboot.bin").read_bytes(), b"elf")

    def test_patch_only_file_added(self):
        r = merge.merge(self.base, self.patch, self.out)
        self.assertEqual((self.out / "Media/level14").read_bytes(), b"new")
        self.assertTrue((self.out / "sce_discmap_patch.plt").is_file())
        self.assertEqual(r.files, 5)
        self.assertEqual(r.from_patch, 3)

    def test_rerun_is_noop(self):
        merge.merge(self.base, self.patch, self.out)
        with mock.patch.object(merge, "_place_file") as place:
            merge.merge(self.base, self.patch, self.out)
            place.assert_not_called()

    def test_incomplete_output_redone(self):
        merge.merge(self.base, self.patch, self.out)
        (self.out / merge.MANIFEST).unlink()
        (self.out / "Media/a.dll").write_bytes(b"corrupt")
        merge.merge(self.base, self.patch, self.out)
        self.assertEqual((self.out / "Media/a.dll").read_bytes(), b"patch-a")
        self.assertTrue((self.out / merge.MANIFEST).is_file())

    def test_changed_input_redone(self):
        merge.merge(self.base, self.patch, self.out)
        (self.patch / "Media/level14").write_bytes(b"newer!")
        merge.merge(self.base, self.patch, self.out)
        self.assertEqual((self.out / "Media/level14").read_bytes(), b"newer!")

    def test_input_untouched(self):
        before = tree_hash(self.base), tree_hash(self.patch)
        merge.merge(self.base, self.patch, self.out)
        (self.out / "Media/a.dll").write_bytes(b"edit output")  # must not leak into inputs
        self.assertEqual(before, (tree_hash(self.base), tree_hash(self.patch)))

    def test_no_shell_used(self):
        with mock.patch("subprocess.run", wraps=merge.subprocess.run) as run:
            merge.merge(self.base, self.patch, self.out)
        for call in run.call_args_list:
            self.assertIsInstance(call.args[0], list)
            self.assertFalse(call.kwargs.get("shell", False))

    @unittest.skipUnless(merge.sys.platform == "darwin", "APFS clone only on macOS")
    def test_uses_system_cp_for_clone(self):
        # GNU cp earlier on PATH has no clone flag; must call macOS /bin/cp
        with mock.patch("subprocess.run", wraps=merge.subprocess.run) as run:
            r = merge.merge(self.base, self.patch, self.out)
        self.assertTrue(all(c.args[0][0] == "/bin/cp" for c in run.call_args_list))
        self.assertTrue(r.cloned)


if __name__ == "__main__":
    unittest.main()
