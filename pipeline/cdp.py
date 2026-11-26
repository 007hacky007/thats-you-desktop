"""Minimal Chrome DevTools Protocol client over --remote-debugging-pipe (stdlib only).

Chrome reads commands from fd 3 and writes replies and events to fd 4; every message is one JSON
object followed by a NUL byte. Sessions are flattened (Target.attachToTarget flatten=true): a
command or event for a page carries a top-level "sessionId".
"""
from __future__ import annotations

import fcntl
import json
import os
import subprocess
import threading
import time


class CDPError(Exception):
    def __init__(self, message: str, code: int | None = None, method: str | None = None):
        super().__init__(f"{method}: {message}" if method else message)
        self.code = code
        self.method = method


class CDPTimeout(CDPError):
    pass


class CDP:
    MAX_EVENTS = 20000

    def __init__(self, read_fd: int, write_fd: int):
        self._rfd = read_fd
        self._wfd = write_fd
        self._lock = threading.Condition()
        self._write_lock = threading.Lock()
        self._next_id = 1
        self._pending: dict[int, dict | None] = {}
        self._events: list[dict] = []
        self._first_seq = 0
        self._listeners: list = []
        self.closed = False
        self._reader = threading.Thread(target=self._read_loop, name="cdp-reader", daemon=True)
        self._reader.start()

    # ------------------------------------------------------------ transport

    def _read_loop(self) -> None:
        buf = b""
        try:
            while True:
                try:
                    chunk = os.read(self._rfd, 1 << 16)
                except OSError:
                    break
                if not chunk:
                    break
                buf += chunk
                while True:
                    i = buf.find(b"\0")
                    if i < 0:
                        break
                    raw, buf = buf[:i], buf[i + 1:]
                    if raw:
                        try:
                            msg = json.loads(raw.decode("utf-8"))
                        except ValueError:
                            continue
                        self._dispatch(msg)
        finally:
            with self._lock:
                self.closed = True
                self._lock.notify_all()

    def _dispatch(self, msg: dict) -> None:
        if "id" in msg:
            with self._lock:
                if msg["id"] in self._pending:
                    self._pending[msg["id"]] = msg
                    self._lock.notify_all()
            return
        with self._lock:
            self._events.append(msg)
            if len(self._events) > self.MAX_EVENTS:
                drop = len(self._events) - self.MAX_EVENTS
                del self._events[:drop]
                self._first_seq += drop
            self._lock.notify_all()
            listeners = list(self._listeners)
        for fn in listeners:
            try:
                fn(msg)
            except Exception:  # a listener must never kill the reader
                pass

    def _write(self, data: bytes) -> None:
        with self._write_lock:
            view = memoryview(data)
            while view:
                n = os.write(self._wfd, view)
                view = view[n:]

    # ------------------------------------------------------------ API

    def send(self, method: str, params: dict | None = None, session_id: str | None = None,
             timeout: float = 30.0) -> dict:
        with self._lock:
            if self.closed:
                raise CDPError("pipe closed", method=method)
            cid = self._next_id
            self._next_id += 1
            self._pending[cid] = None
        msg = {"id": cid, "method": method, "params": params or {}}
        if session_id:
            msg["sessionId"] = session_id
        try:
            self._write(json.dumps(msg).encode("utf-8") + b"\0")
        except OSError as e:
            with self._lock:
                self._pending.pop(cid, None)
            raise CDPError(f"write failed: {e}", method=method) from e
        deadline = time.monotonic() + timeout
        with self._lock:
            while self._pending.get(cid) is None:
                if self.closed:
                    self._pending.pop(cid, None)
                    raise CDPError("pipe closed", method=method)
                left = deadline - time.monotonic()
                if left <= 0:
                    self._pending.pop(cid, None)
                    raise CDPTimeout(f"no reply within {timeout} s", method=method)
                self._lock.wait(left)
            reply = self._pending.pop(cid)
        if "error" in reply:
            err = reply["error"] or {}
            raise CDPError(err.get("message", "error"), err.get("code"), method)
        return reply.get("result", {})

    def mark(self) -> int:
        """Sequence number of the next event; pass it as `since` to wait for later events only."""
        with self._lock:
            return self._first_seq + len(self._events)

    def wait_event(self, method: str, *, session_id: str | None = None, predicate=None,
                   since: int | None = None, timeout: float = 10.0) -> dict:
        """First event `method` (optionally of one session, matching predicate(params)) at or after `since`."""
        pos = self.mark() if since is None else since
        deadline = time.monotonic() + timeout
        with self._lock:
            while True:
                start = max(pos - self._first_seq, 0)
                for i in range(start, len(self._events)):
                    ev = self._events[i]
                    if ev.get("method") != method:
                        continue
                    if session_id is not None and ev.get("sessionId") != session_id:
                        continue
                    if predicate is not None and not predicate(ev.get("params", {})):
                        continue
                    return ev
                pos = self._first_seq + len(self._events)
                if self.closed:
                    raise CDPError(f"pipe closed while waiting for {method}")
                left = deadline - time.monotonic()
                if left <= 0:
                    raise CDPTimeout(f"no {method} event within {timeout} s")
                self._lock.wait(left)

    def add_listener(self, fn):
        with self._lock:
            self._listeners.append(fn)

        def remove():
            with self._lock:
                if fn in self._listeners:
                    self._listeners.remove(fn)
        return remove

    def session(self, session_id: str) -> "Session":
        return Session(self, session_id)

    def close(self) -> None:
        for fd in (self._wfd, self._rfd):
            try:
                os.close(fd)
            except OSError:
                pass
        self._reader.join(timeout=2)


class Session:
    def __init__(self, cdp: CDP, session_id: str):
        self.cdp = cdp
        self.id = session_id

    def send(self, method: str, params: dict | None = None, timeout: float = 30.0) -> dict:
        return self.cdp.send(method, params, self.id, timeout)

    def mark(self) -> int:
        return self.cdp.mark()

    def wait_event(self, method: str, **kw) -> dict:
        return self.cdp.wait_event(method, session_id=self.id, **kw)


def _high_dup(fd: int) -> int:
    new = fcntl.fcntl(fd, fcntl.F_DUPFD_CLOEXEC, 10)
    os.close(fd)
    return new


def launch(argv: list[str], *, stdout=None, stderr=None, env=None) -> tuple[subprocess.Popen, CDP]:
    """Start a browser with --remote-debugging-pipe (must be in argv); returns (process, client)."""
    to_child_r, to_child_w = (_high_dup(fd) for fd in os.pipe())
    from_child_r, from_child_w = (_high_dup(fd) for fd in os.pipe())

    # Our own descriptors are close-on-exec (PEP 446 and F_DUPFD_CLOEXEC), so close_fds=False only
    # lets fds 3 and 4, made by dup2 in the child, reach the browser.
    def child_fds():
        os.dup2(to_child_r, 3)
        os.dup2(from_child_w, 4)

    try:
        proc = subprocess.Popen(argv, stdin=subprocess.DEVNULL, stdout=stdout, stderr=stderr, env=env,
                                preexec_fn=child_fds, close_fds=False)
    except BaseException:
        for fd in (to_child_r, to_child_w, from_child_r, from_child_w):
            os.close(fd)
        raise
    os.close(to_child_r)
    os.close(from_child_w)
    return proc, CDP(from_child_r, to_child_w)
