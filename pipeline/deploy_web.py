"""Deploy the built phone page (work/web-dist) to the target configured in config/local.json.

CLI: python3 -m pipeline.deploy_web [--build] [--dry-run]

config/local.json key web_deploy, one of:
  {"target": "cloudflare", "worker_name": "my-phone-page", "compatibility_date": "2026-11-01"}
  {"target": "folder", "path": "/srv/www/phone"}
  {"target": "rsync", "dest": "user@host:/srv/www/phone/", "delete": false}

The page address is private to its owner: nothing here prints it. Tool output is written in full
to <work>/web-deploy/deploy.log only; what is echoed has URLs and workers.dev hosts redacted.
"""
from __future__ import annotations

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

from pipeline import web_common

RECORD = ".iwyk-deploy.json"
URL_RE = re.compile(r"https?://[^\s\"'<>)\]]+")
WORKERS_RE = re.compile(r"\b[A-Za-z0-9][A-Za-z0-9.-]*\.workers\.dev\b")
NAME_RE = re.compile(r"^[a-z0-9][a-z0-9-]{0,62}$")
DATE_RE = re.compile(r"^\d{4}-\d{2}-\d{2}$")


class DeployError(Exception):
    pass


def redact(text: str, extra: list[str] | None = None) -> str:
    text = URL_RE.sub("<redacted>", text)
    text = WORKERS_RE.sub("<redacted>", text)
    for s in extra or []:
        if s:
            text = text.replace(s, "<redacted>")
    return text


def _secret_parts(cfg: dict) -> list[str]:
    """Strings that must never be echoed: the configured page URL and its host."""
    url = cfg.get("phone_page_url") or ""
    parts = [url]
    m = re.match(r"^[a-z]+://([^/:#?]+)", url)
    if m:
        parts.append(m.group(1))
    return sorted((p for p in parts if p), key=len, reverse=True)


def target_spec(cfg: dict) -> dict:
    spec = cfg.get("web_deploy")
    if spec is None:
        raise DeployError("config/local.json has no web_deploy (see config/local.example.json)")
    if not isinstance(spec, dict):
        raise DeployError("web_deploy must be an object")
    target = spec.get("target")
    if target == "cloudflare":
        name = spec.get("worker_name")
        if not isinstance(name, str) or not NAME_RE.match(name):
            raise DeployError("web_deploy.worker_name must be lower-case letters, digits and hyphens")
        date = spec.get("compatibility_date")
        if not isinstance(date, str) or not DATE_RE.match(date):
            raise DeployError("web_deploy.compatibility_date must be YYYY-MM-DD")
    elif target == "folder":
        if not isinstance(spec.get("path"), str) or not spec["path"].strip():
            raise DeployError("web_deploy.path is empty: set the folder to deploy into")
    elif target == "rsync":
        if not isinstance(spec.get("dest"), str) or not spec["dest"].strip():
            raise DeployError("web_deploy.dest is empty: set the rsync destination")
        if "delete" in spec and not isinstance(spec["delete"], bool):
            raise DeployError("web_deploy.delete must be true or false")
    else:
        raise DeployError(f"web_deploy.target must be cloudflare, folder or rsync (got {target!r})")
    return spec


class Deployer:
    def __init__(self, cfg: dict, *, run=subprocess.run, which=shutil.which, out=print,
                 repo: Path = web_common.REPO_ROOT, home: Path | None = None):
        self.cfg = cfg
        self.work = web_common.work_dir(cfg)
        self.dist = self.work / "web-dist"
        self.deploy_dir = self.work / "web-deploy"
        self.log_path = self.deploy_dir / "deploy.log"
        self.run = run
        self.which = which
        self.out = out
        self.repo = Path(repo)
        self.home = Path(home) if home else Path.home()
        self.secrets = _secret_parts(cfg)

    def say(self, msg: str) -> None:
        self.out(redact(msg, self.secrets))

    def _log(self, text: str) -> None:
        self.deploy_dir.mkdir(parents=True, exist_ok=True)
        with open(self.log_path, "a", encoding="utf-8") as f:
            f.write(text)

    def _run_logged(self, cmd: list[str], cwd: Path | None, extra_secrets: list[str]) -> None:
        self._log(f"$ {' '.join(cmd)}\n")
        try:
            proc = self.run(cmd, cwd=str(cwd) if cwd else None, stdout=subprocess.PIPE,
                            stderr=subprocess.STDOUT, text=True)
        except FileNotFoundError as e:
            raise DeployError(f"cannot run {cmd[0]}: {e}") from e
        output = proc.stdout or ""
        self._log(output + f"\n[exit {proc.returncode}]\n")
        shown = redact(output, self.secrets + extra_secrets).rstrip()
        if shown:
            self.out(shown)
        if proc.returncode != 0:
            raise DeployError(f"{cmd[0]} failed with exit code {proc.returncode} (full log: {self.log_path})")

    def deploy(self, dry_run: bool = False) -> None:
        spec = target_spec(self.cfg)
        if not (self.dist / "precache.json").is_file():
            raise DeployError(f"no built page in {self.dist} (run python3 -m pipeline.build_web or pass --build)")
        getattr(self, "_" + spec["target"])(spec, dry_run)

    # ------------------------------------------------------------ targets

    def _cloudflare(self, spec: dict, dry_run: bool) -> None:
        conf = self.deploy_dir / "wrangler.json"
        data = {"name": spec["worker_name"], "compatibility_date": spec["compatibility_date"],
                "assets": {"directory": str(self.dist.resolve())}}
        if self.which("wrangler"):
            cmd = ["wrangler", "deploy", "--config", str(conf)]
        elif self.which("npx"):
            cmd = ["npx", "--yes", "wrangler", "deploy", "--config", str(conf)]
        else:
            raise DeployError("wrangler not found (install it, or install Node.js for npx)")
        self.say(f"plan: write {conf}")
        self.say(f"plan: run {' '.join(cmd[:-2])} in {self.deploy_dir}")
        if dry_run:
            self.say("dry run: nothing deployed")
            return
        self.deploy_dir.mkdir(parents=True, exist_ok=True)
        conf.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
        self._run_logged(cmd, self.deploy_dir, [spec["worker_name"]])
        self.say("deployed. Set phone_page_url in config/local.json to the page address if it changed "
                 "(see the full log under work/web-deploy).")

    def _check_folder(self, dest: Path) -> None:
        resolved = dest.resolve()
        if resolved == Path(resolved.anchor):
            raise DeployError("refusing to deploy into a filesystem root")
        if resolved == self.home.resolve():
            raise DeployError("refusing to deploy into the home folder")
        if web_common.is_within(resolved, self.repo):
            raise DeployError("refusing to deploy into the repository")
        if web_common.is_within(resolved, self.dist) or web_common.is_within(self.dist, resolved):
            raise DeployError("refusing to deploy into or above the build output")

    def _folder(self, spec: dict, dry_run: bool) -> None:
        dest = Path(os.path.expanduser(spec["path"]))
        if not dest.is_absolute():
            raise DeployError("web_deploy.path must be an absolute path")
        self._check_folder(dest)
        record_path = dest / RECORD
        previous: list[str] = []
        if record_path.is_file():
            try:
                previous = list(json.loads(record_path.read_text(encoding="utf-8")).get("files", []))
            except (ValueError, AttributeError):
                previous = []
        files = sorted(p.relative_to(self.dist).as_posix() for p in self.dist.rglob("*") if p.is_file())
        stale = sorted(set(previous) - set(files))
        self.say(f"plan: copy {len(files)} files to {dest}, remove {len(stale)} files of the previous deploy")
        if dry_run:
            self.say("dry run: nothing deployed")
            return
        for rel in files:
            target = dest / rel
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(self.dist / rel, target)
        for rel in stale:
            p = dest / rel
            if ".." in Path(rel).parts or Path(rel).is_absolute() or not web_common.is_within(p, dest):
                continue
            if p.is_file() or p.is_symlink():
                p.unlink()
                parent = p.parent
                while parent != dest and web_common.is_within(parent, dest) and not any(parent.iterdir()):
                    parent.rmdir()
                    parent = parent.parent
        record_path.write_text(json.dumps({"files": files}, indent=1) + "\n", encoding="utf-8")
        self._log(f"folder deploy: {len(files)} files to {dest}, removed {len(stale)}\n")
        self.say("deployed.")

    def _rsync(self, spec: dict, dry_run: bool) -> None:
        dest = spec["dest"]
        cmd = ["rsync", "-a"] + (["--delete"] if spec.get("delete") is True else []) + [f"{self.dist}/", dest]
        host = dest.split(":", 1)[0] if ":" in dest else ""
        secrets = [dest] + ([host, host.split("@")[-1]] if host else [])
        self.say(f"plan: rsync -a{' --delete' if spec.get('delete') is True else ''} {self.dist}/ <dest from config>")
        if dry_run:
            self.say("dry run: nothing deployed")
            return
        self._run_logged(cmd, None, secrets)
        self.say("deployed.")


def main(argv=None, **deployer_kw) -> int:
    ap = argparse.ArgumentParser(prog="python3 -m pipeline.deploy_web", description=__doc__.split("\n")[0])
    ap.add_argument("--build", action="store_true", help="run pipeline.build_web first")
    ap.add_argument("--dry-run", action="store_true", help="print the plan only")
    ap.add_argument("--config", type=Path, help=argparse.SUPPRESS)
    args = ap.parse_args(argv)
    try:
        cfg = web_common.load_local(args.config)
        if args.build:
            from pipeline import build_web
            if build_web.main(["--config", str(args.config)] if args.config else []) != 0:
                return 1
        Deployer(cfg, **deployer_kw).deploy(dry_run=args.dry_run)
    except (DeployError, web_common.WebConfigError, OSError) as e:
        print(f"deploy_web: {redact(str(e), _secret_parts(locals().get('cfg') or {}))}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
