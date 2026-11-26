import tempfile
import unittest
import urllib.request
from pathlib import Path

from pipeline import web_serve


class WebServeTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        root = Path(self.tmp.name)
        files = {"index.html": "<!doctype html>", "js/main.js": "export {};", "manifest.webmanifest": "{}",
                 "art/manifest.json": "{}", "a.png": "p", "f.otf": "o", "f.ttf": "t", "s.ogg": "g",
                 "icons/icon.svg": "<svg/>", "css/base.css": "body{}"}
        for rel, text in files.items():
            (root / rel).parent.mkdir(parents=True, exist_ok=True)
            (root / rel).write_text(text)
        self.server, self.base = web_serve.start_background(root)

    def tearDown(self):
        self.server.shutdown()
        self.server.server_close()
        self.tmp.cleanup()

    def get(self, path):
        with urllib.request.urlopen(self.base + path, timeout=5) as r:
            return r.headers, r.read()

    def test_types_and_no_store(self):
        expected = {"js/main.js": "text/javascript", "manifest.webmanifest": "application/manifest+json",
                    "art/manifest.json": "application/json", "a.png": "image/png", "f.otf": "font/otf",
                    "f.ttf": "font/ttf", "s.ogg": "audio/ogg", "icons/icon.svg": "image/svg+xml",
                    "index.html": "text/html", "css/base.css": "text/css"}
        for path, ctype in expected.items():
            with self.subTest(path=path):
                headers, _ = self.get(path)
                self.assertEqual(headers["Content-Type"].split(";")[0], ctype)
                self.assertEqual(headers["Cache-Control"], "no-store")

    def test_root_serves_index(self):
        headers, body = self.get("")
        self.assertEqual(body, b"<!doctype html>")
        self.assertEqual(headers["Cache-Control"], "no-store")

    def test_missing_root(self):
        with self.assertRaises(FileNotFoundError):
            web_serve.make_server(Path(self.tmp.name) / "nope")


if __name__ == "__main__":
    unittest.main()
