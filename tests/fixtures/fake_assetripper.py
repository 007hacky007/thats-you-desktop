"""Minimal stand-in for AssetRipper.GUI.Free used by tests/test_assetripper.py."""
import http.server
import json
import os
import sys
import urllib.parse
from pathlib import Path

args = sys.argv[1:]
port = int(args[args.index("--port") + 1])
log = Path(args[args.index("--log-path") + 1])
calls = Path(os.environ["FAKE_AR_CALLS"])
fail = os.environ.get("FAKE_AR_FAIL", "")


def record(path, form):
    with calls.open("a") as f:
        f.write(json.dumps([path, form]) + "\n")


class H(http.server.BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def do_GET(self):
        self.send_response(200)
        self.end_headers()
        self.wfile.write(b"ok")

    def do_POST(self):
        n = int(self.headers.get("Content-Length") or 0)
        form = urllib.parse.parse_qs(self.rfile.read(n).decode(), keep_blank_values=True)
        record(self.path, form)
        if self.path == "/Export/UnityProject":
            out = Path(form["Path"][0])
            (out / "ExportedProject" / "Assets").mkdir(parents=True)
            (out / "ExportedProject" / "Assets" / "a.cs").write_text("class A {}")
            if fail == "export":
                os._exit(3)
            with log.open("a") as f:
                f.write("Export : Finished post-export\n")
        self.send_response(302)
        self.send_header("Location", "/")
        self.end_headers()


log.write_text("General : start\n")
http.server.HTTPServer(("127.0.0.1", port), H).serve_forever()
