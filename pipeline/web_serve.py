"""Serve the phone page locally for development and the end-to-end harness.

CLI: python3 -m pipeline.web_serve [--art] [--port 8088] [--host 127.0.0.1]

Without --art it serves web/ (the neutral page); with --art it serves <work>/web-dist (run
pipeline.build_web first). Every response has Cache-Control: no-store. http://127.0.0.1 and
http://localhost are secure contexts, so the camera and the service worker work.
"""
from __future__ import annotations

import argparse
import functools
import sys
import threading
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

from pipeline import web_common

TYPES = {
    ".html": "text/html; charset=utf-8",
    ".js": "text/javascript; charset=utf-8",
    ".mjs": "text/javascript; charset=utf-8",
    ".css": "text/css; charset=utf-8",
    ".json": "application/json",
    ".webmanifest": "application/manifest+json",
    ".png": "image/png",
    ".jpg": "image/jpeg",
    ".jpeg": "image/jpeg",
    ".svg": "image/svg+xml",
    ".otf": "font/otf",
    ".ttf": "font/ttf",
    ".woff2": "font/woff2",
    ".ogg": "audio/ogg",
    ".txt": "text/plain; charset=utf-8",
    "": "application/octet-stream",
}


class PageHandler(SimpleHTTPRequestHandler):
    extensions_map = TYPES

    def end_headers(self):
        self.send_header("Cache-Control", "no-store")
        super().end_headers()

    def log_message(self, fmt, *args):
        if getattr(self.server, "quiet", False):
            return
        super().log_message(fmt, *args)


def make_server(root: Path, host: str = "127.0.0.1", port: int = 0, quiet: bool = False) -> ThreadingHTTPServer:
    root = Path(root)
    if not root.is_dir():
        raise FileNotFoundError(f"nothing to serve: {root}")
    handler = functools.partial(PageHandler, directory=str(root))
    server = ThreadingHTTPServer((host, port), handler)
    server.daemon_threads = True
    server.quiet = quiet
    return server


def start_background(root: Path, host: str = "127.0.0.1", port: int = 0, quiet: bool = True):
    """Start a server thread; returns (server, base_url). Stop with server.shutdown()."""
    server = make_server(root, host, port, quiet)
    t = threading.Thread(target=server.serve_forever, name="web_serve", daemon=True)
    t.start()
    h, p = server.server_address[:2]
    return server, f"http://{h}:{p}/"


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="python3 -m pipeline.web_serve", description=__doc__.split("\n")[0])
    ap.add_argument("--art", action="store_true", help="serve <work>/web-dist instead of web/")
    ap.add_argument("--port", type=int, default=8088)
    ap.add_argument("--host", default="127.0.0.1")
    args = ap.parse_args(argv)
    root = web_common.web_dist() if args.art else web_common.WEB_SRC
    try:
        server = make_server(root, args.host, args.port)
    except (FileNotFoundError, OSError) as e:
        print(f"web_serve: {e}", file=sys.stderr)
        return 1
    print(f"serving {'art build' if args.art else 'neutral page'} from {root} on http://{args.host}:{server.server_address[1]}/")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
