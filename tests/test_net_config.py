import io
import json
import tempfile
import unittest
from contextlib import redirect_stderr, redirect_stdout
from pathlib import Path

from pipeline import net_config

URL = "https://example.invalid/iwyk/"


class WriteTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        self.repo = self.root / "repo"
        (self.repo / "work").mkdir(parents=True)
        (self.repo / "web").mkdir()

    def tearDown(self):
        self.tmp.cleanup()

    def test_writes_the_config_file(self):
        out = self.root / "build"
        p = net_config.write(out, URL + "#h=1", 47001, "192.168.1.5", repo=self.repo)
        self.assertEqual(p, out / "iwyk-net.json")
        text = p.read_text(encoding="utf-8")
        self.assertEqual(json.loads(text), {"phone_page_url": URL, "udp_port": 47001, "lan_ip": "192.168.1.5",
                                            "log_level": "info"})
        self.assertEqual(list(json.loads(text)), ["phone_page_url", "udp_port", "lan_ip", "log_level"])
        self.assertTrue(text.endswith("}\n"))
        self.assertTrue(text.isascii())

    def test_defaults(self):
        p = net_config.write(self.root / "b", "http://127.0.0.1:8080/", repo=self.repo)
        d = json.loads(p.read_text())
        self.assertEqual((d["udp_port"], d["lan_ip"]), (47000, ""))

    def test_validation(self):
        for bad in ("", "ftp://example.invalid/", "example.invalid", "https://", "https://a b/"):
            with self.assertRaises(ValueError, msg=bad):
                net_config.write(self.root / "b", bad, repo=self.repo)
        for port in (-1, 65536, "47000", True):
            with self.assertRaises(ValueError, msg=port):
                net_config.write(self.root / "b", URL, port, repo=self.repo)
        for ip in ("1.2.3", "1.2.3.256", "::1", "01.2.3.4"):
            with self.assertRaises(ValueError, msg=ip):
                net_config.write(self.root / "b", URL, 0, ip, repo=self.repo)
        self.assertFalse((self.root / "b").exists())

    def test_refuses_tracked_repo_paths(self):
        for d in (self.repo, self.repo / "web", self.repo / "web" / "dist", self.repo / "work" / ".." / "web"):
            with self.assertRaises(ValueError, msg=str(d)):
                net_config.write(d, URL, repo=self.repo)
        self.assertFalse((self.repo / "iwyk-net.json").exists())
        with self.assertRaises(ValueError):
            net_config.write(net_config.REPO_ROOT / "unity", URL)  # the real repository by default
        p = net_config.write(self.repo / "work" / "builds" / "mac", URL, repo=self.repo)
        self.assertTrue(p.is_file())

    def test_refuses_a_symlink_into_the_repo(self):
        link = self.root / "link"
        link.symlink_to(self.repo / "web")
        with self.assertRaises(ValueError):
            net_config.write(link, URL, repo=self.repo)


class CliTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)

    def tearDown(self):
        self.tmp.cleanup()

    def run_cli(self, cfg: dict, *extra) -> tuple[int, str, str]:
        path = self.root / "local.json"
        path.write_text(json.dumps(cfg))
        out, err = io.StringIO(), io.StringIO()
        with redirect_stdout(out), redirect_stderr(err):
            rc = net_config.main(["--config", str(path), "--out", str(self.root / "out"), *extra])
        return rc, out.getvalue(), err.getvalue()

    def test_reads_local_json(self):
        rc, out, _ = self.run_cli({"phone_page_url": URL, "net_udp_port": 0, "net_lan_ip": "10.0.0.7", "apk": "x"})
        self.assertEqual(rc, 0)
        d = json.loads((self.root / "out" / "iwyk-net.json").read_text())
        self.assertEqual(d, {"phone_page_url": URL, "udp_port": 0, "lan_ip": "10.0.0.7", "log_level": "info"})
        self.assertNotIn("example.invalid", out, "the page URL is never printed")

    def test_missing_url_fails(self):
        rc, _, err = self.run_cli({"phone_page_url": ""})
        self.assertEqual(rc, 2)
        self.assertIn("phone_page_url", err)
        self.assertFalse((self.root / "out").exists())

    def test_invalid_values_fail(self):
        rc, _, err = self.run_cli({"phone_page_url": URL, "net_udp_port": 99999})
        self.assertEqual(rc, 2)
        self.assertIn("udp_port", err)

    def test_missing_config_fails(self):
        out, err = io.StringIO(), io.StringIO()
        with redirect_stdout(out), redirect_stderr(err):
            rc = net_config.main(["--config", str(self.root / "nope.json"), "--out", str(self.root / "o")])
        self.assertEqual(rc, 2)


if __name__ == "__main__":
    unittest.main()
