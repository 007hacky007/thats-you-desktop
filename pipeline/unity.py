"""Run the pinned Unity editor in batch mode."""
from __future__ import annotations

import json
import os
import subprocess
import sys
from pathlib import Path

VERSIONS = Path(__file__).with_name("versions.json")


def pinned_version() -> str:
    return json.loads(VERSIONS.read_text())["unity"]["version"]


def hub_editor_path(root: Path, version: str, plat: str | None = None) -> Path:
    """Where Unity Hub puts an editor version under an install root."""
    plat = plat or sys.platform
    if plat == "darwin":
        return Path(root) / version / "Unity.app" / "Contents" / "MacOS" / "Unity"
    if plat == "win32":
        return Path(root) / version / "Editor" / "Unity.exe"
    return Path(root) / version / "Editor" / "Unity"


def default_roots(plat: str | None = None) -> list[Path]:
    plat = plat or sys.platform
    if plat == "darwin":
        return [Path("/Applications/Unity/Hub/Editor"), Path.home() / "Applications/Unity/Hub/Editor"]
    if plat == "win32":
        pf = os.environ.get("ProgramFiles", "C:/Program Files")
        return [Path(pf) / "Unity" / "Hub" / "Editor"]
    return [Path.home() / "Unity" / "Hub" / "Editor"]


def hub_config_path(plat: str | None = None) -> Path:
    """Unity Hub's file with the custom editor install folder (a JSON string)."""
    plat = plat or sys.platform
    if plat == "darwin":
        return Path.home() / "Library/Application Support/UnityHub/secondaryInstallPath.json"
    if plat == "win32":
        return Path(os.environ.get("APPDATA", str(Path.home()))) / "UnityHub" / "secondaryInstallPath.json"
    return Path.home() / ".config/UnityHub/secondaryInstallPath.json"


def find_editor(version: str | None = None, roots: list[Path] | None = None, plat: str | None = None,
                hub_config: Path | None = None) -> Path | None:
    """The editor binary of `version` (default: the pinned one) in Unity Hub's locations, or None."""
    version = version or pinned_version()
    cands = list(default_roots(plat) if roots is None else roots)
    cfg = hub_config or hub_config_path(plat)
    try:
        extra = json.loads(Path(cfg).read_text(encoding="utf-8"))
        if isinstance(extra, str) and extra:
            cands.append(Path(extra))
    except (OSError, ValueError):
        pass
    for root in cands:
        exe = hub_editor_path(root, version, plat)
        if exe.is_file():
            return exe
    return None


def run_batch(editor: Path, project: Path, args: list[str], log: Path) -> int:
    argv = [str(editor), "-batchmode", "-nographics", "-quit", "-projectPath", str(project),
            *args, "-logFile", str(log)]
    return subprocess.run(argv).returncode
