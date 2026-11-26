import json
import os
import sys
import threading
import time
import unittest

from pipeline import cdp


class FakePeer:
    """Plays the browser end of the pipe: reads NUL framed commands, answers through a handler."""

    def __init__(self, handler):
        self.to_browser_r, self.to_browser_w = os.pipe()
        self.from_browser_r, self.from_browser_w = os.pipe()
        self.handler = handler
        self.received = []
        self.thread = threading.Thread(target=self.loop, daemon=True)
        self.thread.start()

    def client(self):
        return cdp.CDP(self.from_browser_r, self.to_browser_w)

    def emit(self, msg, split=False):
        data = json.dumps(msg).encode() + b"\0"
        if split:
            for i in range(0, len(data), 3):
                os.write(self.from_browser_w, data[i:i + 3])
                time.sleep(0.001)
        else:
            os.write(self.from_browser_w, data)

    def loop(self):
        buf = b""
        while True:
            try:
                chunk = os.read(self.to_browser_r, 4096)
            except OSError:
                return
            if not chunk:
                return
            buf += chunk
            while b"\0" in buf:
                raw, buf = buf.split(b"\0", 1)
                msg = json.loads(raw)
                self.received.append(msg)
                for reply in self.handler(msg) or []:
                    self.emit(reply)

    def hangup(self):
        os.close(self.from_browser_w)


class CdpTests(unittest.TestCase):
    def test_command_reply_correlation_and_session(self):
        def handler(msg):
            # answer in reverse order to prove correlation by id
            return [{"id": msg["id"], "result": {"echo": msg["method"], "session": msg.get("sessionId")}}]
        peer = FakePeer(handler)
        c = peer.client()
        self.assertEqual(c.send("Browser.getVersion"), {"echo": "Browser.getVersion", "session": None})
        s = c.session("S1")
        self.assertEqual(s.send("Page.enable")["session"], "S1")
        ids = [m["id"] for m in peer.received]
        self.assertEqual(ids, sorted(set(ids)))
        self.assertEqual(peer.received[1]["sessionId"], "S1")
        self.assertEqual(peer.received[1]["params"], {})
        c.close()

    def test_concurrent_commands(self):
        lock = threading.Lock()
        held = []

        def handler(msg):
            with lock:
                held.append(msg)
                if len(held) < 2:
                    return []
                out = [{"id": m["id"], "result": {"m": m["method"]}} for m in reversed(held)]
                held.clear()
                return out
        peer = FakePeer(handler)
        c = peer.client()
        results = {}

        def call(name):
            results[name] = c.send(name)["m"]
        ts = [threading.Thread(target=call, args=(n,)) for n in ("A.a", "B.b")]
        for t in ts:
            t.start()
        for t in ts:
            t.join(5)
        self.assertEqual(results, {"A.a": "A.a", "B.b": "B.b"})
        c.close()

    def test_error_reply(self):
        peer = FakePeer(lambda m: [{"id": m["id"], "error": {"code": -32000, "message": "nope"}}])
        c = peer.client()
        with self.assertRaises(cdp.CDPError) as cm:
            c.send("Page.navigate")
        self.assertEqual(cm.exception.code, -32000)
        self.assertIn("nope", str(cm.exception))
        c.close()

    def test_timeout(self):
        peer = FakePeer(lambda m: [])
        c = peer.client()
        with self.assertRaises(cdp.CDPTimeout):
            c.send("Slow.call", timeout=0.1)
        c.close()

    def test_events_split_frames_and_waiting(self):
        peer = FakePeer(lambda m: [])
        c = peer.client()
        seen = []
        remove = c.add_listener(seen.append)
        mark = c.mark()
        peer.emit({"method": "Page.loadEventFired", "params": {"t": 1}, "sessionId": "X"}, split=True)
        peer.emit({"method": "Page.loadEventFired", "params": {"t": 2}, "sessionId": "Y"})
        ev = c.wait_event("Page.loadEventFired", session_id="Y", since=mark, timeout=2)
        self.assertEqual(ev["params"], {"t": 2})
        ev = c.wait_event("Page.loadEventFired", since=mark, predicate=lambda p: p["t"] == 1, timeout=2)
        self.assertEqual(ev["sessionId"], "X")
        with self.assertRaises(cdp.CDPTimeout):
            c.wait_event("Page.loadEventFired", timeout=0.1)  # since defaults to now
        later = threading.Timer(0.1, lambda: peer.emit({"method": "Runtime.consoleAPICalled", "params": {}}))
        later.start()
        self.assertEqual(c.wait_event("Runtime.consoleAPICalled", timeout=2)["params"], {})
        self.assertEqual(len(seen), 3)
        remove()
        c.close()

    def test_pipe_closed_fails_pending(self):
        peer = FakePeer(lambda m: [])
        c = peer.client()
        threading.Timer(0.1, peer.hangup).start()
        with self.assertRaisesRegex(cdp.CDPError, "closed"):
            c.send("X.y", timeout=5)
        self.assertTrue(c.closed)
        with self.assertRaisesRegex(cdp.CDPError, "closed"):
            c.send("X.z")
        c.close()

    def test_launch_maps_fd_3_and_4(self):
        child = (
            "import os, json\n"
            "buf = b''\n"
            "while b'\\0' not in buf:\n"
            "    buf += os.read(3, 4096)\n"
            "msg = json.loads(buf.split(b'\\0')[0])\n"
            "os.write(4, json.dumps({'id': msg['id'], 'result': {'ok': msg['method']}}).encode() + b'\\0')\n"
        )
        proc, c = cdp.launch([sys.executable, "-c", child, "--remote-debugging-pipe"])
        try:
            self.assertEqual(c.send("Browser.getVersion", timeout=10), {"ok": "Browser.getVersion"})
        finally:
            proc.wait(10)
            c.close()


if __name__ == "__main__":
    unittest.main()
