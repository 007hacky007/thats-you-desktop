"""Drive AssetRipper.GUI.Free headless through its local web API.

API notes (AssetRipper 2.0.0, see docs/notes/assetripper.md): form-encoded
POSTs, every call answers 302 and blocks until finished. Settings can only be
changed before loading; checkboxes are true when present in the form.
Success is read from the log ("Export : Finished post-export").
"""
from __future__ import annotations

import shutil
import socket
import subprocess
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

MARKER = ".export-complete"
DONE_LINE = "Export : Finished post-export"
TIMEOUT = 6 * 3600


class ExportError(Exception):
    pass


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *a, **k):
        return None


_opener = urllib.request.build_opener(_NoRedirect)


def _free_port() -> int:
    with socket.socket() as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


def _start(exe: list[str], port: int, log: Path) -> subprocess.Popen:
    return subprocess.Popen(exe + ["--headless", "--port", str(port), "--log-path", str(log)],
                            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


def _post(port: int, path: str, form: dict[str, str]) -> None:
    data = urllib.parse.urlencode(form).encode()
    try:
        _opener.open(urllib.request.Request(f"http://127.0.0.1:{port}{path}", data=data), timeout=TIMEOUT)
    except urllib.error.HTTPError as e:
        if e.code != 302:
            raise ExportError(f"{path}: HTTP {e.code}") from e
    except (urllib.error.URLError, ConnectionError) as e:
        raise ExportError(f"{path}: {e}") from e


def _wait_ready(proc: subprocess.Popen, port: int, timeout: float = 120) -> None:
    end = time.monotonic() + timeout
    while time.monotonic() < end:
        if proc.poll() is not None:
            raise ExportError(f"AssetRipper exited with {proc.returncode} during startup")
        try:
            urllib.request.urlopen(f"http://127.0.0.1:{port}/", timeout=2)
            return
        except (urllib.error.URLError, ConnectionError):
            time.sleep(0.5)
    raise ExportError("AssetRipper did not start")


def settings_form(settings: dict) -> dict[str, str]:
    form = {}
    for k, v in settings.items():
        if v is True:
            form[k] = ""
        elif v is False or v is None:
            continue
        else:
            form[k] = str(v)
    return form


def default_exe(work_dir: Path) -> list[str]:
    from pipeline import tools

    d = tools.ensure("assetripper", work_dir)
    name = "AssetRipper.GUI.Free.exe" if (d / "AssetRipper.GUI.Free.exe").exists() else "AssetRipper.GUI.Free"
    return [str(d / name)]


def export(input_dir: Path, out_dir: Path, settings: dict, exe: list[str]) -> None:
    out_dir = Path(out_dir)
    if (out_dir / MARKER).is_file():
        return
    tmp = out_dir.with_name(out_dir.name + ".tmp")
    for d in (tmp, out_dir):
        if d.exists():
            shutil.rmtree(d)
    tmp.mkdir(parents=True)
    log = tmp / "assetripper.log"
    port = _free_port()
    proc = _start(exe, port, log)
    try:
        _wait_ready(proc, port)
        _post(port, "/Reset", {})
        _post(port, "/Settings/Update", settings_form(settings))
        _post(port, "/LoadFolder", {"Path": str(input_dir)})
        _post(port, "/Export/UnityProject", {"Path": str(tmp)})
        text = log.read_text(errors="replace") if log.exists() else ""
        if DONE_LINE not in text:
            raise ExportError(f"export did not finish, see {log}")
    finally:
        proc.terminate()
        try:
            proc.wait(10)
        except subprocess.TimeoutExpired:
            proc.kill()
            proc.wait()
    tmp.rename(out_dir)
    (out_dir / MARKER).write_text("ok")
