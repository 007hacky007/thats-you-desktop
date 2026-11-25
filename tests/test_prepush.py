import subprocess
import tempfile
import unittest
from pathlib import Path

HOOKS = Path(__file__).resolve().parent.parent / "hooks"


def git(cwd, *args, check=True):
    return subprocess.run(["git", *args], cwd=cwd, capture_output=True, text=True, check=check)


class PrePushTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        r = Path(self.tmp.name)
        self.remote, self.repo = r / "remote.git", r / "repo"
        git(r, "init", "-q", "--bare", str(self.remote))
        git(r, "init", "-q", "-b", "master", str(self.repo))
        for k, v in [("user.name", "t"), ("user.email", "t@example.invalid"), ("commit.gpgsign", "false"),
                     ("core.hooksPath", str(HOOKS)), ("iwyk.privateUrl", "secret-page.example.dev")]:
            git(self.repo, "config", k, v)
        git(self.repo, "remote", "add", "origin", str(self.remote))
        self.commit("README.md", "hello\n")

    def tearDown(self):
        self.tmp.cleanup()

    def commit(self, rel, text, force=False):
        p = self.repo / rel
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(text)
        git(self.repo, "add", *(["-f"] if force else []), rel)
        git(self.repo, "commit", "-qm", f"add {rel}")

    def push(self):
        return git(self.repo, "push", "-q", "origin", "master", check=False)

    def test_prepush_allows_clean_repo(self):
        r = self.push()
        self.assertEqual(r.returncode, 0, r.stderr)

    def test_prepush_blocks_with_patches(self):
        self.commit("local-patches/010-x.patch", "diff\n", force=True)
        r = self.push()
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("Plan 7", r.stderr)

    def test_prepush_blocks_patch_in_history(self):
        self.commit("local-patches/010-x.patch", "diff\n", force=True)
        git(self.repo, "rm", "-q", "local-patches/010-x.patch")
        git(self.repo, "commit", "-qm", "remove")
        self.assertNotEqual(self.push().returncode, 0)

    def test_prepush_blocks_private_url(self):
        self.commit("docs/x.md", "see https://secret-page.example.dev/\n")
        r = self.push()
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("privateUrl", r.stderr)


if __name__ == "__main__":
    unittest.main()
