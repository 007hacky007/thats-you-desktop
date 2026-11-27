import hashlib
import http.server
import io
import tarfile
import tempfile
import threading
import unittest
import zipfile
from pathlib import Path

from pipeline import tools
from pipeline.tools import ChecksumError


def make_zip() -> bytes:
    b = io.BytesIO()
    with zipfile.ZipFile(b, "w") as z:
        z.writestr("Tool/run", "#!/bin/sh\necho hi\n")
    return b.getvalue()


def make_tarxz() -> bytes:
    b = io.BytesIO()
    data = b"#!/bin/sh\necho hi\n"
    with tarfile.open(fileobj=b, mode="w:xz") as t:
        info = tarfile.TarInfo("Tool/run")
        info.size = len(data)
        info.mode = 0o755
        t.addfile(info, io.BytesIO(data))
    return b.getvalue()


class ToolsTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        root = Path(self.tmp.name)
        self.serve = root / "serve"
        self.serve.mkdir()
        self.work = root / "work"
        (self.serve / "tool.zip").write_bytes(make_zip())
        (self.serve / "tool.tar.xz").write_bytes(make_tarxz())
        test = self

        class Handler(http.server.SimpleHTTPRequestHandler):
            def __init__(self, *a, **k):
                super().__init__(*a, directory=str(test.serve), **k)

            def do_GET(self):
                test.requests += 1
                super().do_GET()

            def log_message(self, *a):
                pass

        self.requests = 0
        self.httpd = http.server.ThreadingHTTPServer(("127.0.0.1", 0), Handler)
        threading.Thread(target=self.httpd.serve_forever, daemon=True).start()
        self.base = f"http://127.0.0.1:{self.httpd.server_address[1]}"

    def tearDown(self):
        self.httpd.shutdown()
        self.httpd.server_close()
        self.tmp.cleanup()

    def spec(self, name: str, sha: str | None = None) -> dict:
        data = (self.serve / name).read_bytes()
        return {"version": "1", "url": f"{self.base}/{name}", "sha256": sha or hashlib.sha256(data).hexdigest()}

    def test_downloads_and_extracts_zip(self):
        d = tools.ensure("t", self.work, spec=self.spec("tool.zip"))
        self.assertEqual((d / "Tool/run").read_text(), "#!/bin/sh\necho hi\n")

    def test_downloads_and_extracts_tarxz(self):
        d = tools.ensure("t", self.work, spec=self.spec("tool.tar.xz"))
        self.assertTrue((d / "Tool/run").is_file())

    def test_plain_executable_is_kept_as_is(self):
        (self.serve / "tool.exe").write_bytes(b"MZ fake")
        d = tools.ensure("t", self.work, spec=self.spec("tool.exe"))
        self.assertEqual((d / "tool.exe").read_bytes(), b"MZ fake")

    def test_checksum_mismatch_raises_and_deletes(self):
        with self.assertRaises(ChecksumError):
            tools.ensure("t", self.work, spec=self.spec("tool.zip", sha="0" * 64))
        self.assertEqual([p for p in (self.work / "tools").rglob("*") if p.is_file()], [])

    def test_cached_tool_not_redownloaded(self):
        s = self.spec("tool.zip")
        tools.ensure("t", self.work, spec=s)
        n = self.requests
        tools.ensure("t", self.work, spec=s)
        self.assertEqual(self.requests, n)

    def test_unsafe_archive_path_rejected(self):
        b = io.BytesIO()
        with zipfile.ZipFile(b, "w") as z:
            z.writestr("../evil", "x")
        (self.serve / "evil.zip").write_bytes(b.getvalue())
        with self.assertRaises(ValueError):
            tools.ensure("t", self.work, spec=self.spec("evil.zip"))
        self.assertFalse((self.work / "evil").exists())

    def test_platform_spec_from_versions(self):
        entry = {"version": "2", "url_macos-arm64": "u1", "sha256_macos-arm64": "s1", "url_win-x64": "u2", "sha256_win-x64": "s2"}
        self.assertEqual(tools.platform_spec(entry, "win-x64"), {"version": "2", "url": "u2", "sha256": "s2"})


if __name__ == "__main__":
    unittest.main()
