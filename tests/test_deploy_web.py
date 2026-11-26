import json
import subprocess
import tempfile
import unittest
from pathlib import Path

from pipeline import deploy_web
from pipeline.deploy_web import Deployer, DeployError

PAGE = "https://example.invalid/iwyk/"


class FakeRun:
    def __init__(self, output="", code=0):
        self.calls = []
        self.output = output
        self.code = code

    def __call__(self, cmd, cwd=None, stdout=None, stderr=None, text=None):
        self.calls.append((list(cmd), cwd))
        return subprocess.CompletedProcess(cmd, self.code, stdout=self.output)


class DeployTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name).resolve()
        self.work = self.root / "work"
        self.dist = self.work / "web-dist"
        (self.dist / "js").mkdir(parents=True)
        (self.dist / "index.html").write_text("x")
        (self.dist / "js" / "main.js").write_text("y")
        (self.dist / "precache.json").write_text("{}")
        self.repo = self.root / "repo"
        self.repo.mkdir()
        self.home = self.root / "home"
        self.home.mkdir()
        self.lines = []

    def tearDown(self):
        self.tmp.cleanup()

    def cfg(self, spec):
        return {"work_dir": str(self.work), "phone_page_url": PAGE, "web_deploy": spec}

    def deployer(self, spec, run=None, which=lambda n: "/usr/bin/" + n):
        return Deployer(self.cfg(spec), run=run or FakeRun(), which=which, out=self.lines.append,
                        repo=self.repo, home=self.home)

    def echoed(self):
        return "\n".join(self.lines)

    # -------------------------------------------------- config errors

    def test_missing_web_deploy(self):
        with self.assertRaisesRegex(DeployError, "no web_deploy"):
            Deployer({"work_dir": str(self.work)}, out=self.lines.append).deploy()

    def test_bad_target(self):
        with self.assertRaisesRegex(DeployError, "target"):
            self.deployer({"target": "ftp"}).deploy()

    def test_empty_folder_path(self):
        with self.assertRaisesRegex(DeployError, "path is empty"):
            self.deployer({"target": "folder", "path": ""}).deploy()

    def test_bad_worker_name(self):
        with self.assertRaisesRegex(DeployError, "worker_name"):
            self.deployer({"target": "cloudflare", "worker_name": "Bad Name", "compatibility_date": "2026-11-01"}).deploy()

    def test_requires_built_page(self):
        (self.dist / "precache.json").unlink()
        with self.assertRaisesRegex(DeployError, "build_web"):
            self.deployer({"target": "folder", "path": str(self.root / "out")}).deploy()

    # -------------------------------------------------- cloudflare

    def test_cloudflare_writes_config_runs_wrangler_and_redacts(self):
        out = ("Uploaded my-phone-page\nPublished https://my-phone-page.someone.workers.dev\n"
               "also my-phone-page.someone.workers.dev and " + PAGE + "\n")
        run = FakeRun(out)
        spec = {"target": "cloudflare", "worker_name": "my-phone-page", "compatibility_date": "2026-11-01"}
        self.deployer(spec, run).deploy()
        conf = self.work / "web-deploy" / "wrangler.json"
        data = json.loads(conf.read_text())
        self.assertEqual(data, {"name": "my-phone-page", "compatibility_date": "2026-11-01",
                                "assets": {"directory": str(self.dist)}})
        cmd, cwd = run.calls[0]
        self.assertEqual(cmd, ["wrangler", "deploy", "--config", str(conf)])
        self.assertEqual(cwd, str(self.work / "web-deploy"))
        text = self.echoed()
        self.assertNotIn("workers.dev", text)
        self.assertNotIn("http", text)
        self.assertNotIn("example.invalid", text)
        self.assertIn("phone_page_url", text)
        log = (self.work / "web-deploy" / "deploy.log").read_text()
        self.assertIn("my-phone-page.someone.workers.dev", log)

    def test_cloudflare_falls_back_to_npx(self):
        run = FakeRun()
        spec = {"target": "cloudflare", "worker_name": "p", "compatibility_date": "2026-11-01"}
        self.deployer(spec, run, which=lambda n: "/x/npx" if n == "npx" else None).deploy()
        self.assertEqual(run.calls[0][0][:4], ["npx", "--yes", "wrangler", "deploy"])

    def test_cloudflare_failure(self):
        spec = {"target": "cloudflare", "worker_name": "p", "compatibility_date": "2026-11-01"}
        with self.assertRaisesRegex(DeployError, "exit code 3"):
            self.deployer(spec, FakeRun("boom https://x.example.invalid", 3)).deploy()
        self.assertNotIn("example.invalid", self.echoed())

    def test_cloudflare_dry_run(self):
        run = FakeRun()
        spec = {"target": "cloudflare", "worker_name": "p", "compatibility_date": "2026-11-01"}
        self.deployer(spec, run).deploy(dry_run=True)
        self.assertEqual(run.calls, [])
        self.assertFalse((self.work / "web-deploy" / "wrangler.json").exists())
        self.assertIn("dry run", self.echoed())

    # -------------------------------------------------- folder

    def test_folder_deploy_records_and_removes_only_recorded(self):
        dest = self.root / "srv" / "phone"
        dest.mkdir(parents=True)
        (dest / "foreign.txt").write_text("keep")
        d = self.deployer({"target": "folder", "path": str(dest)})
        d.deploy()
        self.assertEqual((dest / "js" / "main.js").read_text(), "y")
        rec = json.loads((dest / ".iwyk-deploy.json").read_text())
        self.assertEqual(rec["files"], ["index.html", "js/main.js", "precache.json"])
        (self.dist / "js" / "main.js").unlink()
        (self.dist / "new.js").write_text("n")
        d.deploy()
        self.assertFalse((dest / "js" / "main.js").exists())
        self.assertFalse((dest / "js").exists())
        self.assertTrue((dest / "new.js").is_file())
        self.assertTrue((dest / "foreign.txt").is_file())

    def test_folder_ignores_hostile_record_entries(self):
        dest = self.root / "srv"
        dest.mkdir()
        outside = self.root / "outside.txt"
        outside.write_text("keep")
        (dest / ".iwyk-deploy.json").write_text(json.dumps({"files": ["../outside.txt"]}))
        self.deployer({"target": "folder", "path": str(dest)}).deploy()
        self.assertTrue(outside.is_file())

    def test_folder_refusals(self):
        for bad in ("/", str(self.home), str(self.repo / "site"), str(self.dist / "x"), str(self.work)):
            with self.subTest(path=bad):
                with self.assertRaisesRegex(DeployError, "refusing"):
                    self.deployer({"target": "folder", "path": bad}).deploy()

    def test_folder_relative_path_rejected(self):
        with self.assertRaisesRegex(DeployError, "absolute"):
            self.deployer({"target": "folder", "path": "phone"}).deploy()

    def test_folder_dry_run(self):
        dest = self.root / "srv2"
        self.deployer({"target": "folder", "path": str(dest)}).deploy(dry_run=True)
        self.assertFalse(dest.exists())

    # -------------------------------------------------- rsync

    def test_rsync_without_delete(self):
        run = FakeRun("sent to deploy@web.example.invalid\n")
        self.deployer({"target": "rsync", "dest": "deploy@web.example.invalid:/srv/www/"}, run).deploy()
        self.assertEqual(run.calls[0][0], ["rsync", "-a", f"{self.dist}/", "deploy@web.example.invalid:/srv/www/"])
        self.assertNotIn("example.invalid", self.echoed())

    def test_rsync_delete_only_when_true(self):
        run = FakeRun()
        self.deployer({"target": "rsync", "dest": "h:/d/", "delete": True}, run).deploy()
        self.assertIn("--delete", run.calls[0][0])
        with self.assertRaisesRegex(DeployError, "delete"):
            self.deployer({"target": "rsync", "dest": "h:/d/", "delete": "yes"}).deploy()

    def test_rsync_dry_run(self):
        run = FakeRun()
        self.deployer({"target": "rsync", "dest": "h:/d/"}, run).deploy(dry_run=True)
        self.assertEqual(run.calls, [])

    # -------------------------------------------------- misc

    def test_redact(self):
        self.assertEqual(deploy_web.redact("see https://a.b/c?d and x.y.workers.dev."),
                         "see <redacted> and <redacted>.")

    def test_main_reports_error_without_address(self):
        cfg = self.root / "local.json"
        cfg.write_text(json.dumps({"work_dir": str(self.work), "phone_page_url": PAGE}))
        import io
        import contextlib
        err = io.StringIO()
        with contextlib.redirect_stderr(err):
            rc = deploy_web.main(["--config", str(cfg)])
        self.assertEqual(rc, 1)
        self.assertIn("web_deploy", err.getvalue())
        self.assertNotIn("example.invalid", err.getvalue())


if __name__ == "__main__":
    unittest.main()
