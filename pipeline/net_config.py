"""Write iwyk-net.json (the game's network config, contract 4.7) into a build output.

The file carries the phone page address, which must never land in a tracked path: writing is
refused anywhere inside the repository except under work/.

CLI: python3 -m pipeline.net_config --out DIR [--config config/local.json]
     page URL from config/local.json `phone_page_url`, optional `net_udp_port`, `net_lan_ip`.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path

from pipeline.config import DEFAULT_PATH, REPO_ROOT

FILE_NAME = "iwyk-net.json"
DEFAULT_UDP_PORT = 47000
_IPV4 = re.compile(r"^(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}$")


def normalize_url(url: str) -> str:
    """http(s) URL with a host, fragment removed; ValueError otherwise."""
    if not isinstance(url, str):
        raise ValueError("phone_page_url must be a string")
    u = url.strip().split("#", 1)[0]
    low = u.lower()
    if low.startswith("https://"):
        rest = u[8:]
    elif low.startswith("http://"):
        rest = u[7:]
    else:
        raise ValueError("phone_page_url must start with https:// or http://")
    if not rest or rest.startswith("/") or any(c <= " " or c == "\x7f" for c in u):
        raise ValueError("phone_page_url is not a valid URL")
    return u


def check_port(port) -> int:
    if isinstance(port, bool) or not isinstance(port, int) or not 0 <= port <= 65535:
        raise ValueError(f"udp_port must be an integer 0..65535, got {port!r}")
    return port


def check_lan_ip(ip) -> str:
    if not isinstance(ip, str) or (ip and not _IPV4.match(ip)):
        raise ValueError(f"lan_ip must be empty or an IPv4 address, got {ip!r}")
    return ip


def check_out_dir(out_dir: Path, repo: Path = REPO_ROOT) -> Path:
    """Resolved out_dir; ValueError when it is inside the repository but not under work/."""
    out = Path(out_dir).expanduser().resolve()
    root = Path(repo).resolve()
    if out == root or root in out.parents:
        work = root / "work"
        if not (out == work or work in out.parents):
            raise ValueError(f"refusing to write {FILE_NAME} into a tracked repository path: {out}")
    return out


def render(page_url: str, udp_port: int = DEFAULT_UDP_PORT, lan_ip: str = "") -> str:
    data = {"phone_page_url": normalize_url(page_url), "udp_port": check_port(udp_port),
            "lan_ip": check_lan_ip(lan_ip), "log_level": "info"}
    return json.dumps(data, indent=2, ensure_ascii=True) + "\n"


def write(out_dir, page_url: str, udp_port: int = DEFAULT_UDP_PORT, lan_ip: str = "", *, repo: Path = REPO_ROOT) -> Path:
    text = render(page_url, udp_port, lan_ip)
    check_out_dir(out_dir, repo)
    out = Path(out_dir).expanduser()
    out.mkdir(parents=True, exist_ok=True)
    path = out / FILE_NAME
    path.write_text(text, encoding="utf-8")
    return path


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--out", type=Path, required=True, help="build output folder (next to the game executable)")
    ap.add_argument("--config", type=Path, default=DEFAULT_PATH, help="local config (default config/local.json)")
    a = ap.parse_args(argv)
    try:
        cfg = json.loads(a.config.read_text(encoding="utf-8"))
    except (OSError, ValueError) as e:
        print(f"net_config: cannot read {a.config}: {e}", file=sys.stderr)
        return 2
    url = cfg.get("phone_page_url") or ""
    if not url:
        print(f"net_config: phone_page_url is not set in {a.config}", file=sys.stderr)
        return 2
    try:
        path = write(a.out, url, cfg.get("net_udp_port", DEFAULT_UDP_PORT), cfg.get("net_lan_ip", "") or "")
    except ValueError as e:
        print(f"net_config: {e}", file=sys.stderr)
        return 2
    print(f"wrote {path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
