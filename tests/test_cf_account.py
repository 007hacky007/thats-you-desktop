import json
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from pipeline import cf_account


def runner(outputs: dict):
    """Fake subprocess.run: answers by the wrangler sub-command, records every argv."""
    calls = []

    def run(argv, **kw):
        calls.append((argv, kw))
        assert isinstance(argv, list), "argv lists only, never a shell string"
        for key, (rc, out) in outputs.items():
            if key in argv:
                return mock.Mock(returncode=rc, stdout=out, stderr="")
        return mock.Mock(returncode=0, stdout="", stderr="")
    run.calls = calls
    return run


W = ["/n/node", "/w/wrangler.js"]


class AccountTest(unittest.TestCase):
    def test_logged_in(self):
        self.assertTrue(cf_account.logged_in(W, run=runner({"whoami": (0, "You are logged in with an OAuth Token, associated with the email x.")})))
        self.assertFalse(cf_account.logged_in(W, run=runner({"whoami": (0, "You are not authenticated. Please run `wrangler login`.")})))
        self.assertFalse(cf_account.logged_in(W, run=runner({"whoami": (1, "error")})))

    def test_login_runs_wrangler_login(self):
        run = runner({"login": (0, "Successfully logged in.")})
        cf_account.login(W, run=run)
        self.assertEqual(run.calls[0][0], W + ["login"])
        with self.assertRaises(cf_account.AccountError):
            cf_account.login(W, run=runner({"login": (1, "denied")}))

    def test_deploy_writes_config_and_returns_address(self):
        out = ("Uploaded thats-you-1a2b3c4d (3.2 sec)\nDeployed thats-you-1a2b3c4d triggers (0.5 sec)\n"
               "  https://thats-you-1a2b3c4d.someone.workers.dev\nCurrent Version ID: x\n")
        with tempfile.TemporaryDirectory() as d:
            d = Path(d)
            (d / "web-dist").mkdir()
            run = runner({"deploy": (0, out)})
            url = cf_account.deploy(W, d / "web-dist", "thats-you-1a2b3c4d", d / "deploy", run=run)
            self.assertEqual(url, "https://thats-you-1a2b3c4d.someone.workers.dev/")
            conf = json.loads((d / "deploy" / "wrangler.json").read_text())
            self.assertEqual(conf["name"], "thats-you-1a2b3c4d")
            self.assertEqual(Path(conf["assets"]["directory"]), (d / "web-dist").resolve())
            argv = run.calls[0][0]
            self.assertEqual(argv[:3], W + ["deploy"])
            with self.assertRaises(cf_account.AccountError):
                cf_account.deploy(W, d / "web-dist", "thats-you-1a2b3c4d", d / "deploy",
                                  run=runner({"deploy": (0, "no address here")}))

    def test_worker_name(self):
        n = cf_account.worker_name(b"seed")
        self.assertRegex(n, r"^thats-you-[0-9a-f]{8}$")
        self.assertEqual(n, cf_account.worker_name(b"seed"))
        self.assertNotEqual(n, cf_account.worker_name(b"other"))

    def test_wrangler_argv_installs_once(self):
        with tempfile.TemporaryDirectory() as d:
            d = Path(d)
            node_dir = d / "tools" / "node"
            (node_dir / "bin").mkdir(parents=True)
            (node_dir / "bin" / "node").write_text("")
            (node_dir / "lib" / "node_modules" / "npm" / "bin").mkdir(parents=True)
            (node_dir / "lib" / "node_modules" / "npm" / "bin" / "npm-cli.js").write_text("")
            calls = []

            def run(argv, **kw):
                calls.append(argv)
                prefix = Path(argv[argv.index("--prefix") + 1])
                js = prefix / "node_modules" / "wrangler" / "bin" / "wrangler.js"
                js.parent.mkdir(parents=True)
                js.write_text("")
                return mock.Mock(returncode=0, stdout="", stderr="")

            argv = cf_account.ensure_wrangler(d, ensure=lambda name, w: node_dir, run=run, plat="macos-arm64")
            self.assertEqual(argv[0], str(node_dir / "bin" / "node"))
            self.assertTrue(argv[1].endswith("wrangler.js"))
            self.assertIn(f"wrangler@{cf_account.WRANGLER_VERSION}", calls[0])
            cf_account.ensure_wrangler(d, ensure=lambda name, w: node_dir, run=run, plat="macos-arm64")
            self.assertEqual(len(calls), 1)


if __name__ == "__main__":
    unittest.main()
