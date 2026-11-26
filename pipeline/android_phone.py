"""An Android Chrome (emulator or USB device) as one playthrough phone, driven over adb + DevTools.

adb reverse makes the locally served phone page reachable on the device as 127.0.0.1 (a secure
context, so the camera works); Chrome's DevTools socket is forwarded to the Mac and spoken to
with a minimal stdlib WebSocket client bridged into the pipe based cdp.CDP. Input goes in as
touch events, like a finger.
"""
from __future__ import annotations

import base64
import json
import os
import re
import shutil
import socket
import struct
import subprocess
import threading
import time
import urllib.parse
import urllib.request
from pathlib import Path

from pipeline import cdp, web_e2e

CHROME = "com.android.chrome"
DEVTOOLS_PORT = 9333
PROMPT_WATCH_S = 180.0


# ------------------------------------------------------------------ websocket framing

def encode_frame(payload: bytes, mask: bytes | None = None, opcode: int = 1) -> bytes:
    mask = mask if mask is not None else os.urandom(4)
    n = len(payload)
    head = bytes([0x80 | opcode])
    if n < 126:
        head += bytes([0x80 | n])
    elif n < 65536:
        head += bytes([0x80 | 126]) + struct.pack(">H", n)
    else:
        head += bytes([0x80 | 127]) + struct.pack(">Q", n)
    return head + mask + bytes(b ^ mask[i % 4] for i, b in enumerate(payload))


class FrameDecoder:
    """Server frames (unmasked) -> [(opcode, message)], joining fragments."""

    def __init__(self):
        self.buf = b""
        self.parts: list[bytes] = []
        self.op = 0

    def feed(self, data: bytes) -> list[tuple[int, bytes]]:
        self.buf += data
        out = []
        while len(self.buf) >= 2:
            b0, b1 = self.buf[0], self.buf[1]
            n, pos = b1 & 0x7F, 2
            if n == 126:
                if len(self.buf) < 4:
                    break
                n, pos = struct.unpack(">H", self.buf[2:4])[0], 4
            elif n == 127:
                if len(self.buf) < 10:
                    break
                n, pos = struct.unpack(">Q", self.buf[2:10])[0], 10
            if b1 & 0x80:
                pos += 4  # servers do not mask; tolerate it anyway
            if len(self.buf) < pos + n:
                break
            payload = self.buf[pos:pos + n]
            if b1 & 0x80:
                m = self.buf[pos - 4:pos]
                payload = bytes(c ^ m[i % 4] for i, c in enumerate(payload))
            self.buf = self.buf[pos + n:]
            op = b0 & 0x0F
            if op >= 8:  # control frame
                out.append((op, payload))
                continue
            if op:
                self.op = op
            self.parts.append(payload)
            if b0 & 0x80:
                out.append((self.op, b"".join(self.parts)))
                self.parts = []
        return out


def ws_connect(url: str, timeout: float = 10.0) -> socket.socket:
    assert url.startswith("ws://")
    hostport, _, path = url[5:].partition("/")
    host, _, port = hostport.partition(":")
    s = socket.create_connection((host, int(port or 80)), timeout=timeout)
    key = base64.b64encode(os.urandom(16)).decode()
    s.sendall((f"GET /{path} HTTP/1.1\r\nHost: {hostport}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n"
               f"Sec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\n\r\n").encode())
    resp = b""
    while b"\r\n\r\n" not in resp:
        chunk = s.recv(4096)
        if not chunk:
            raise ConnectionError("websocket handshake: connection closed")
        resp += chunk
    if b" 101 " not in resp.split(b"\r\n", 1)[0]:
        raise ConnectionError(f"websocket handshake failed: {resp.split(b'\r\n', 1)[0]!r}")
    s.settimeout(None)
    return s


def ws_cdp(url: str) -> cdp.CDP:
    """A cdp.CDP talking to a DevTools WebSocket (bridged through two pipes)."""
    sock = ws_connect(url)
    to_cdp_r, to_cdp_w = os.pipe()
    from_cdp_r, from_cdp_w = os.pipe()

    def down():  # websocket -> CDP reader (NUL separated)
        dec = FrameDecoder()
        try:
            while True:
                data = sock.recv(1 << 16)
                if not data:
                    break
                for op, msg in dec.feed(data):
                    if op == 8:
                        return
                    if op == 9:
                        sock.sendall(encode_frame(msg, opcode=10))
                    elif op in (1, 2):
                        os.write(to_cdp_w, msg + b"\0")
        except OSError:
            pass
        finally:
            os.close(to_cdp_w)

    def up():  # CDP writer -> websocket
        buf = b""
        try:
            while True:
                chunk = os.read(from_cdp_r, 1 << 16)
                if not chunk:
                    break
                buf += chunk
                while b"\0" in buf:
                    msg, _, buf = buf.partition(b"\0")
                    sock.sendall(encode_frame(msg))
        except OSError:
            pass
        finally:
            sock.close()

    threading.Thread(target=down, daemon=True, name="ws-down").start()
    threading.Thread(target=up, daemon=True, name="ws-up").start()
    return cdp.CDP(to_cdp_r, from_cdp_w)


# ------------------------------------------------------------------ touch input

def touch_for(kind: str, x: float, y: float, buttons: int):
    """PageDriver mouse event -> (touch type, points), or None to drop it."""
    if kind == "mousePressed":
        return "touchStart", [{"x": x, "y": y, "id": 0}]
    if kind == "mouseMoved":
        return ("touchMove", [{"x": x, "y": y, "id": 0}]) if buttons else None
    if kind == "mouseReleased":
        return "touchEnd", []
    return None


class AndroidPage(web_e2e.PageDriver):
    """PageDriver for a real mobile Chrome tab: no viewport override, touch instead of mouse."""

    def setup(self) -> None:
        for m in ("Page.enable", "Runtime.enable", "Log.enable"):
            self.s.send(m)

    def _mouse(self, kind: str, x: float, y: float, buttons: int) -> None:
        t = touch_for(kind, round(x, 2), round(y, 2), buttons)
        if t:
            self.s.send("Input.dispatchTouchEvent", {"type": t[0], "touchPoints": t[1]})


def pages_to_close(targets: list[dict], keep: str | None = None) -> list[str]:
    return [t["id"] for t in targets if t.get("type") == "page" and t.get("id") != keep]


def pick_target(targets: list[dict], url_prefix: str) -> dict | None:
    for t in targets:
        if t.get("type") == "page" and t.get("url", "").startswith(url_prefix):
            return t
    return None


# ------------------------------------------------------------------ native permission prompts

_NODE = re.compile(r'text="([^"]*)"[^>]*?bounds="\[(\d+),(\d+)\]\[(\d+),(\d+)\]"')
_ALLOW = ("While using the app", "Only this time", "Allow")


def prompt_tap(xml: str):
    """Centre of the button that allows a native permission prompt (Chrome's site prompt
    "... wants to use your camera" or Android's runtime prompt) in a uiautomator dump, or None."""
    nodes = [(m.group(1), *map(int, m.groups()[1:])) for m in _NODE.finditer(xml)]
    site = any("wants to" in t for t, *_ in nodes)
    for label in _ALLOW:
        for t, x0, y0, x1, y1 in nodes:
            if t == label and (site or label != "Allow"):
                return (x0 + x1) // 2, (y0 + y1) // 2
    return None


# ------------------------------------------------------------------ adb

def adb_path() -> str:
    # The SDK's adb first: two adb versions on one machine restart each other's server, which
    # drops every forward/reverse (DevTools and the page port) mid-game.
    sdk = [os.environ.get("ANDROID_HOME", ""), str(Path.home() / "android-sdk"), str(Path.home() / "Library/Android/sdk")]
    for p in [str(Path(d) / "platform-tools/adb") for d in sdk if d] + [shutil.which("adb")]:
        if p and Path(p).is_file():
            return p
    raise FileNotFoundError("adb not found")


class AndroidPhone:
    def __init__(self, serial: str | None = None):
        self.adb = [adb_path()] + (["-s", serial] if serial else [])
        self.reversed: list[int] = []
        self.cdp: cdp.CDP | None = None
        self.log_file = None
        self.prompts = 0
        self._stop = threading.Event()

    def _watch_prompts(self) -> None:
        """Answer native permission prompts the way a player would (they cover the page)."""
        end = time.monotonic() + PROMPT_WATCH_S  # prompts come while joining; dumps are heavy
        while not self._stop.wait(2.0) and time.monotonic() < end:
            try:
                r = subprocess.run(self.adb + ["exec-out", "uiautomator", "dump", "/dev/tty"],
                                   capture_output=True, text=True, timeout=20)
                xy = prompt_tap(r.stdout)
                if xy:
                    self.sh("shell", "input", "tap", str(xy[0]), str(xy[1]))
                    self.prompts += 1
            except (subprocess.SubprocessError, RuntimeError, OSError):
                pass

    def sh(self, *args: str, check: bool = True) -> str:
        r = subprocess.run(self.adb + list(args), capture_output=True, text=True, timeout=60)
        if check and r.returncode:
            raise RuntimeError(f"adb {' '.join(args)}: {r.stderr.strip() or r.stdout.strip()}")
        return r.stdout

    def prepare(self, port: int) -> None:
        """Reverse the page port, skip Chrome's first run screens, forward DevTools."""
        self.sh("wait-for-device")
        self.sh("reverse", f"tcp:{port}", f"tcp:{port}")
        self.reversed.append(port)
        self.sh("shell", "am", "set-debug-app", "--persistent", CHROME)
        self.sh("shell", "echo 'chrome --disable-fre --no-default-browser-check --no-first-run "
                         "--disable-notifications' > /data/local/tmp/chrome-command-line")
        for perm in ("CAMERA", "POST_NOTIFICATIONS"):  # no native permission prompts over the page
            self.sh("shell", "pm", "grant", CHROME, f"android.permission.{perm}", check=False)
        self.sh("shell", "am", "force-stop", CHROME)
        self.sh("forward", f"tcp:{DEVTOOLS_PORT}", "localabstract:chrome_devtools_remote")
        threading.Thread(target=self._watch_prompts, daemon=True, name="android-prompts").start()

    def _devtools(self, path: str, method: str = "GET"):
        req = urllib.request.Request(f"http://127.0.0.1:{DEVTOOLS_PORT}{path}", method=method)
        with urllib.request.urlopen(req, timeout=5) as r:
            body = r.read()
        try:
            return json.loads(body)
        except ValueError:
            return None

    def _targets(self) -> list[dict] | None:
        try:
            return self._devtools("/json/list") or []
        except OSError:
            return None  # Chrome (its DevTools socket) not up

    def open(self, url: str, log_path: Path, timeout: float = 90.0) -> AndroidPage:
        """One Chrome tab at url (old tabs closed, so a single player per device)."""
        prefix = url.split("#", 1)[0]
        self.sh("shell", "am", "start", "-a", "android.intent.action.VIEW", "-d", f"'{url}'", "-n",
                f"{CHROME}/com.google.android.apps.chrome.Main")
        t0, target, made = time.monotonic(), None, False
        while time.monotonic() - t0 < timeout:
            targets = self._targets()
            target = pick_target(targets or [], prefix)
            if target:
                break
            if targets is not None and not made and time.monotonic() - t0 > 20:
                # Chrome is up but a cold first start swallowed the intent: open the tab directly
                self._devtools("/json/new?" + urllib.parse.quote(url, safe=":/?#&=%"), method="PUT")
                made = True
            time.sleep(1)
        if not target:
            raise RuntimeError(f"Chrome on the device never opened {prefix}")
        for tid in pages_to_close(self._targets() or [], keep=target["id"]):  # restored old tabs
            try:
                self._devtools(f"/json/close/{tid}")
            except OSError:
                pass
        self.cdp = ws_cdp(target["webSocketDebuggerUrl"])
        log_path.parent.mkdir(parents=True, exist_ok=True)
        self.log_file = open(log_path, "a", encoding="utf-8")
        self.cdp.add_listener(self._on_event)
        page = AndroidPage(cdp.Session(self.cdp, None))
        page.setup()
        return page

    def _on_event(self, msg: dict) -> None:
        m, p = msg.get("method"), msg.get("params", {})
        line = None
        if m == "Runtime.consoleAPICalled":
            line = f"console.{p.get('type')}: " + " ".join(
                str(a.get("value", a.get("description", a.get("type")))) for a in p.get("args", []))
        elif m == "Runtime.exceptionThrown":
            d = p.get("exceptionDetails", {})
            line = f"exception: {d.get('exception', {}).get('description') or d.get('text')}"
        elif m == "Log.entryAdded":
            e = p.get("entry", {})
            line = f"log.{e.get('level')}: {e.get('text')} {e.get('url', '')}".rstrip()
        if line and self.log_file:
            self.log_file.write(time.strftime("%H:%M:%S ") + line + "\n")
            self.log_file.flush()

    def screencap(self, path: Path) -> None:
        r = subprocess.run(self.adb + ["exec-out", "screencap", "-p"], capture_output=True, timeout=30)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(r.stdout)

    def close(self) -> None:
        self._stop.set()
        if self.cdp:
            self.cdp.close()
        if self.log_file:
            self.log_file.close()
        for p in self.reversed:
            self.sh("reverse", "--remove", f"tcp:{p}", check=False)
        self.sh("forward", "--remove", f"tcp:{DEVTOOLS_PORT}", check=False)
