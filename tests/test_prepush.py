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

    def push(self, *extra, env=None):
        import os
        e = dict(os.environ, **(env or {}))
        return subprocess.run(["git", "push", "-q", *extra, "origin", "master"], cwd=self.repo,
                              capture_output=True, text=True, env=e)

    def test_prepush_allows_clean_repo(self):
        r = self.push()
        self.assertEqual(r.returncode, 0, r.stderr)

    def test_prepush_blocks_with_patches(self):
        self.commit("local-patches/010-x.patch", "diff\n", force=True)
        r = self.push()
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("local-patches/", r.stderr)

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

    def test_untracked_local_patch_files_do_not_block(self):
        # the private patches stay on the owner's disk; only committing them is refused
        (self.repo / "local-patches").mkdir()
        (self.repo / "local-patches/001-x.patch").write_text("diff\n")
        r = self.push()
        self.assertEqual(r.returncode, 0, r.stderr)

    def test_unknown_remote_sha_fails_closed(self):
        # remote moved on elsewhere: rsha is not a local object; force push must still be checked
        self.assertEqual(self.push().returncode, 0)
        other = Path(self.tmp.name) / "other"
        git(Path(self.tmp.name), "clone", "-q", str(self.remote), str(other))
        for k, v in [("user.name", "t"), ("user.email", "t@example.invalid"), ("commit.gpgsign", "false")]:
            git(other, "config", k, v)
        (other / "x.txt").write_text("x\n")
        git(other, "add", "x.txt")
        git(other, "commit", "-qm", "x")
        git(other, "push", "-q", "origin", "master")
        self.commit("docs/x.md", "see https://secret-page.example.dev/\n")
        r = self.push("--force")
        self.assertNotEqual(r.returncode, 0)

    def test_blocks_url_in_binary_file(self):
        p = self.repo / "Library/blob.asset"
        p.parent.mkdir(parents=True)
        p.write_bytes(b"\x00\x01binary\x00secret-page.example.dev\x00")
        git(self.repo, "add", "Library/blob.asset")
        git(self.repo, "commit", "-qm", "bin")
        self.assertNotEqual(self.push().returncode, 0)

    def test_blocks_url_in_other_case(self):
        self.commit("docs/x.md", "SECRET-PAGE.Example.dev\n")
        self.assertNotEqual(self.push().returncode, 0)

    def test_blocks_url_added_in_merge_resolution(self):
        git(self.repo, "checkout", "-q", "-b", "side")
        self.commit("a.txt", "side\n")
        git(self.repo, "checkout", "-q", "master")
        self.commit("b.txt", "main\n")
        git(self.repo, "merge", "-q", "--no-commit", "side", check=False)
        (self.repo / "c.txt").write_text("secret-page.example.dev\n")
        git(self.repo, "add", "c.txt")
        git(self.repo, "commit", "-qm", "merge")
        self.assertNotEqual(self.push().returncode, 0)

    def test_schemeless_url_with_path_or_port_normalized(self):
        self.commit("docs/n.md", "only the bare host: secret-page.example.dev\n")
        for value in ("secret-page.example.dev/some/path", "Secret-Page.example.dev:443", "https://secret-page.example.dev/"):
            git(self.repo, "config", "iwyk.privateUrl", value)
            self.assertNotEqual(self.push().returncode, 0, value)

    def test_unparsable_private_url_refuses(self):
        git(self.repo, "config", "iwyk.privateUrl", "https://")
        r = self.push()
        self.assertNotEqual(r.returncode, 0)

    def test_unset_private_url_refuses(self):
        git(self.repo, "config", "--unset", "iwyk.privateUrl")
        r = self.push()
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("iwyk.privateUrl", r.stderr)


if __name__ == "__main__":
    unittest.main()
