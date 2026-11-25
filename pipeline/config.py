"""Local pipeline configuration (config/local.json, git-ignored)."""
from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_PATH = REPO_ROOT / "config" / "local.json"


class ConfigError(Exception):
    def __init__(self, key: str, message: str):
        super().__init__(f"config '{key}': {message}")
        self.key = key
        self.message = message


@dataclass(frozen=True)
class Config:
    base_dump: Path
    patch_dump: Path
    apk: Path
    work_dir: Path
    unity_editor: Path | None
    phone_page_url: str | None


def _required_dir(data: dict, key: str) -> Path:
    if not data.get(key):
        raise ConfigError(key, "missing")
    p = Path(data[key]).expanduser()
    if not p.is_dir():
        raise ConfigError(key, f"not a directory: {p}")
    return p


def load(path: Path | None = None) -> Config:
    path = Path(path) if path else DEFAULT_PATH
    if not path.is_file():
        raise ConfigError("config", f"file not found: {path} (copy config/local.example.json)")
    data = json.loads(path.read_text(encoding="utf-8"))

    base = _required_dir(data, "base_dump")
    if not (base / "eboot.bin").is_file():
        raise ConfigError("base_dump", f"no eboot.bin in {base}, not a base game dump")
    patch = _required_dir(data, "patch_dump")
    if patch.resolve() == base.resolve() or not (patch / "sce_sys" / "changeinfo").is_dir():
        raise ConfigError("patch_dump", f"{patch} is not a patch dump (no sce_sys/changeinfo)")
    if not data.get("apk"):
        raise ConfigError("apk", "missing")
    apk = Path(data["apk"]).expanduser()
    if not apk.is_file():
        raise ConfigError("apk", f"file not found: {apk}")

    work = Path(data["work_dir"]).expanduser() if data.get("work_dir") else REPO_ROOT / "work"
    unity = Path(data["unity_editor"]).expanduser() if data.get("unity_editor") else None
    return Config(base, patch, apk, work, unity, data.get("phone_page_url") or None)
