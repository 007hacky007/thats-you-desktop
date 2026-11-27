"""Install the pinned Unity editor through Unity Hub and check that it is licensed.

Unity Hub itself is installed by the player from Unity's official installer (the builder downloads
it and checks Unity's code signature before opening it; Hub changes too often to pin a hash).
The editor then installs headless: <hub> -- --headless install --version V --changeset C.
A free Unity Personal licence comes with signing in to Unity Hub; licensed() checks it by creating
a throwaway project in batch mode, before the long build starts.
"""
from __future__ import annotations

import os
import re
import shutil
import subprocess
import sys
import tempfile
import urllib.request
from pathlib import Path

from pipeline import unity

UNITY_TEAM_ID = "9QW8UQUTAA"  # Unity Technologies SF, Apple Developer ID
UNITY_ORGS = {"Unity Technologies SF", "Unity Technologies ApS"}  # exact O= of Unity's Windows certificates
HUB_URLS = {
    "darwin": "https://public-cdn.cloud.unity3d.com/hub/prod/UnityHubSetup-arm64.dmg",
    "win32": "https://public-cdn.cloud.unity3d.com/hub/prod/UnityHubSetup-x64.exe",
}
LICENCE_ERRORS = ("No valid Unity Editor license", "license is not active", "Failed to activate",
                  "No ULF license found", "LicensingClient has failed")


class UnityInstallError(Exception):
    pass


def hub_exe(plat: str | None = None) -> Path:
    plat = plat or sys.platform
    if plat == "darwin":
        return Path("/Applications/Unity Hub.app/Contents/MacOS/Unity Hub")
    pf = os.environ.get("ProgramFiles", "C:/Program Files")
    return Path(pf) / "Unity Hub" / "Unity Hub.exe"


def install_argv(hub: Path, version: str, changeset: str) -> list[str]:
    return [str(hub), "--", "--headless", "install", "--version", version, "--changeset", changeset]


def install(version: str, changeset: str, hub: Path | None = None, run=subprocess.run, find=None, log=print) -> Path:
    """Install the editor with Unity Hub (about 5 to 10 GB download). Returns the editor binary."""
    find = find or (lambda v: unity.find_editor(v))
    hub = Path(hub or hub_exe())
    if not hub.is_file():
        raise UnityInstallError("Unity Hub is not installed")
    existing = find(version)
    if existing:
        return existing
    log(f"installing Unity {version} with Unity Hub (a large download, this takes a while)")
    r = run(install_argv(hub, version, changeset), capture_output=True, text=True)
    editor = find(version)
    if r.returncode != 0 or not editor:
        tail = ((r.stdout or "") + (r.stderr or "")).strip()[-600:]
        raise UnityInstallError(f"Unity Hub could not install Unity {version}: {tail}")
    return editor


def signed_by_unity(path: Path, plat: str | None = None, run=subprocess.run) -> bool:
    """True when path (an app bundle, DMG or EXE) carries Unity Technologies' valid code signature."""
    plat = plat or sys.platform
    try:
        if plat == "darwin":
            v = run(["codesign", "--verify", "--deep", "--strict", str(path)], capture_output=True, text=True)
            if v.returncode != 0:
                return False
            r = run(["codesign", "-dvv", str(path)], capture_output=True, text=True)
            out = (r.stdout or "") + (r.stderr or "")
            return re.search(rf"^TeamIdentifier={UNITY_TEAM_ID}$", out, re.M) is not None
        ps = ("$s = Get-AuthenticodeSignature -LiteralPath $env:IWYK_SIGNED_FILE; "
              "Write-Output ($s.Status.ToString() + '|' + $s.SignerCertificate.Subject)")
        r = run(["powershell", "-NoProfile", "-NonInteractive", "-Command", ps], capture_output=True, text=True,
                env=dict(os.environ, IWYK_SIGNED_FILE=str(path)))
        status, _, subject = (r.stdout or "").strip().partition("|")
        return status == "Valid" and organisation(subject) in UNITY_ORGS
    except OSError:
        return False


def organisation(subject: str) -> str | None:
    """The O= value of an X.500 subject such as 'CN=x, O="Unity Technologies SF", C=US' (exact)."""
    rdns, cur, quoted = [], "", False
    for ch in subject:
        if ch == '"':
            quoted = not quoted
        elif ch == "," and not quoted:
            rdns.append(cur.strip())
            cur = ""
            continue
        cur += ch
    rdns.append(cur.strip())
    orgs = [r[2:].strip().strip('"') for r in rdns if r.startswith("O=")]
    return orgs[0] if len(orgs) == 1 else None


def download_hub_installer(dest_dir: Path, plat: str | None = None, run=subprocess.run) -> Path:
    """Unity's official Hub installer, kept only when Unity's code signature checks out."""
    plat = plat or sys.platform
    url = HUB_URLS.get(plat)
    if not url:
        raise UnityInstallError("no Unity Hub installer for this system")
    dest_dir = Path(dest_dir)
    dest_dir.mkdir(parents=True, exist_ok=True)
    out = dest_dir / url.rsplit("/", 1)[1]
    with urllib.request.urlopen(url, timeout=60) as r, open(out, "wb") as f:
        shutil.copyfileobj(r, f)
    if not signed_by_unity(out, plat, run=run):
        out.unlink(missing_ok=True)
        raise UnityInstallError("the downloaded Unity Hub installer is not signed by Unity; not opening it")
    return out


def open_installer(path: Path, plat: str | None = None) -> None:
    plat = plat or sys.platform
    if plat == "darwin":
        subprocess.Popen(["open", str(path)])
    else:
        os.startfile(str(path))  # noqa: S606 - the signature was checked


def open_hub_for_sign_in(hub: Path | None = None) -> None:
    hub = Path(hub or hub_exe())
    if sys.platform == "darwin":
        subprocess.Popen(["open", "-a", str(hub.parents[2])])
    else:
        subprocess.Popen([str(hub)])


def log_says_licensed(text: str, rc: int) -> bool:
    return rc == 0 and not any(e.lower() in text.lower() for e in LICENCE_ERRORS)


def licensed(editor: Path, run=subprocess.run) -> bool:
    """Create a throwaway project in batch mode; False when Unity reports no valid licence."""
    with tempfile.TemporaryDirectory(prefix="iwyk-licence-") as d:
        log = Path(d) / "unity.log"
        argv = [str(editor), "-batchmode", "-quit", "-nographics", "-createProject", str(Path(d) / "p"),
                "-logFile", str(log)]
        try:
            r = run(argv, capture_output=True, text=True, timeout=600)
        except (OSError, subprocess.TimeoutExpired):
            return False
        text = log.read_text(errors="replace") if log.is_file() else ""
        return log_says_licensed(text, r.returncode)


def status(version: str | None = None, find=None, licensed=licensed) -> str:
    """"missing", "unlicensed" or "ready"."""
    find = find or (lambda v: unity.find_editor(v))
    editor = find(version or unity.pinned_version())
    if not editor:
        return "missing"
    return "ready" if licensed(editor) else "unlicensed"
