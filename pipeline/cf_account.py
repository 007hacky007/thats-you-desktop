"""Put the phone page on the player's own Cloudflare account (the builder's account option).

Everything runs from pinned, portable tools in <work>/tools: Node.js and wrangler, Cloudflare's
command line tool. The player only signs in (wrangler login opens the browser); the page goes
to a Worker named thats-you-<8 hex> on their free workers.dev subdomain, and its address is
written into the game's iwyk-net.json. Nothing here prints the address except to the caller.

CLI: python3 -m pipeline.cf_account [--deploy]   (signs in when needed, prints the address)
"""
from __future__ import annotations

import argparse
import hashlib
import json
import re
import secrets
import subprocess
import sys
from pathlib import Path

from pipeline import tools

WRANGLER_VERSION = "4.168.0"
COMPATIBILITY_DATE = "2026-11-01"
ADDRESS_RE = re.compile(r"https://[a-z0-9-]+\.[a-z0-9-]+\.workers\.dev")


class AccountError(Exception):
    pass


def _node_root(dest: Path) -> Path:
    """The Node.js folder inside the extracted archive (it has one top-level folder)."""
    for cand in [dest, *sorted(p for p in dest.iterdir() if p.is_dir())]:
        if (cand / "bin" / "node").is_file() or (cand / "node.exe").is_file():
            return cand
    raise AccountError(f"Node.js not found in {dest}")


def ensure_wrangler(work_dir: Path, ensure=tools.ensure, run=subprocess.run, plat: str | None = None) -> list[str]:
    """argv prefix that runs the pinned wrangler with the pinned portable Node.js."""
    plat = plat or tools.current_platform()
    root = _node_root(Path(ensure("node", work_dir)))
    win = plat.startswith("win")
    node = root / ("node.exe" if win else "bin/node")
    npm = root / ("node_modules/npm/bin/npm-cli.js" if win else "lib/node_modules/npm/bin/npm-cli.js")
    prefix = Path(work_dir) / "tools" / f"wrangler-{WRANGLER_VERSION}"
    js = prefix / "node_modules" / "wrangler" / "bin" / "wrangler.js"
    if not js.is_file():
        prefix.mkdir(parents=True, exist_ok=True)
        r = run([str(node), str(npm), "install", "--no-audit", "--no-fund", "--prefix", str(prefix),
                 f"wrangler@{WRANGLER_VERSION}"], capture_output=True, text=True)
        if r.returncode != 0 or not js.is_file():
            raise AccountError(f"installing wrangler failed: {(r.stderr or r.stdout or '')[-800:]}")
    return [str(node), str(js)]


def _wr(argv: list[str], args: list[str], run, **kw):
    try:
        return run(argv + args, capture_output=True, text=True, **kw)
    except OSError as e:
        raise AccountError(f"cannot run wrangler: {e}") from None


def logged_in(argv: list[str], run=subprocess.run) -> bool:
    r = _wr(argv, ["whoami"], run)
    out = (r.stdout or "") + (r.stderr or "")
    return r.returncode == 0 and "not authenticated" not in out.lower() and "logged in" in out.lower()


def login(argv: list[str], run=subprocess.run) -> None:
    """Opens Cloudflare's sign-in page in the browser and waits for the player."""
    r = _wr(argv, ["login"], run)
    if r.returncode != 0:
        raise AccountError("Cloudflare sign-in did not finish: " + ((r.stderr or r.stdout or "").strip()[-400:]))


def worker_name(seed: bytes) -> str:
    return "thats-you-" + hashlib.sha256(seed).hexdigest()[:8]


def deploy(argv: list[str], web_dist: Path, name: str, deploy_dir: Path, run=subprocess.run) -> str:
    """Upload web_dist as the Worker `name`; returns its https address (with a trailing slash)."""
    if not re.fullmatch(r"[a-z0-9][a-z0-9-]{0,62}", name):
        raise AccountError(f"bad worker name {name!r}")
    deploy_dir = Path(deploy_dir)
    deploy_dir.mkdir(parents=True, exist_ok=True)
    conf = deploy_dir / "wrangler.json"
    conf.write_text(json.dumps({"name": name, "compatibility_date": COMPATIBILITY_DATE,
                                "assets": {"directory": str(Path(web_dist).resolve())}}, indent=2) + "\n")
    r = _wr(argv, ["deploy", "--config", str(conf)], run, cwd=str(deploy_dir))
    out = (r.stdout or "") + (r.stderr or "")
    (deploy_dir / "deploy.log").write_text(out)
    if r.returncode != 0:
        raise AccountError(f"upload to Cloudflare failed (log: {deploy_dir / 'deploy.log'})")
    m = ADDRESS_RE.search(out)
    if not m or not m.group(0).startswith(f"https://{name}."):
        raise AccountError(f"upload finished but no workers.dev address was reported (log: {deploy_dir / 'deploy.log'})")
    return m.group(0) + "/"


def main(argv=None) -> int:
    from pipeline import config, web_common

    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--deploy", action="store_true", help="upload work/web-dist and print its address")
    a = ap.parse_args(argv)
    c = config.load()
    wr = ensure_wrangler(c.work_dir)
    if not logged_in(wr):
        print("opening the Cloudflare sign-in page in your browser...")
        login(wr)
    print("signed in to Cloudflare")
    if a.deploy:
        name_file = c.work_dir / "web-deploy-account" / "worker-name"
        name = name_file.read_text().strip() if name_file.is_file() else worker_name(secrets.token_bytes(16))
        name_file.parent.mkdir(parents=True, exist_ok=True)
        name_file.write_text(name)
        print(deploy(wr, web_common.web_dist({"work_dir": str(c.work_dir)}), name, c.work_dir / "web-deploy-account"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
