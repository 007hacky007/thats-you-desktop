"""Shared helpers for the phone page tooling (build, deploy, serve, e2e).

Reads config/local.json directly and leniently: only work_dir, phone_page_url and web_deploy are
used here, the game dump paths are never required. Never print the phone_page_url value.
"""
from __future__ import annotations

import json
import socket
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_CONFIG = REPO_ROOT / "config" / "local.json"
WEB_SRC = REPO_ROOT / "web"
APP_ASSETS_REL = Path("export") / "app" / "ExportedProject" / "Assets"


class WebConfigError(Exception):
    pass


def load_local(path: Path | None = None) -> dict:
    """config/local.json as a dict; {} when the file does not exist."""
    if path is None:
        from pipeline import config
        path = config.config_path()
    path = Path(path)
    if not path.is_file():
        return {}
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as e:
        raise WebConfigError(f"cannot read {path}: {e}") from e
    if not isinstance(data, dict):
        raise WebConfigError(f"{path}: top level must be an object")
    return data


def work_dir(cfg: dict | None = None) -> Path:
    cfg = load_local() if cfg is None else cfg
    w = cfg.get("work_dir")
    return Path(w).expanduser() if w else REPO_ROOT / "work"


def web_dist(cfg: dict | None = None) -> Path:
    return work_dir(cfg) / "web-dist"


def app_assets(cfg: dict | None = None) -> Path:
    return work_dir(cfg) / APP_ASSETS_REL


def phone_page_url(cfg: dict | None = None) -> str | None:
    cfg = load_local() if cfg is None else cfg
    return cfg.get("phone_page_url") or None


def free_port(host: str = "127.0.0.1", kind: int = socket.SOCK_STREAM) -> int:
    with socket.socket(socket.AF_INET, kind) as s:
        s.bind((host, 0))
        return s.getsockname()[1]


def is_within(child: Path, parent: Path) -> bool:
    try:
        Path(child).resolve().relative_to(Path(parent).resolve())
        return True
    except ValueError:
        return False
