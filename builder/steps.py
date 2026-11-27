"""The builder's steps, from the player's choices to a playable game in the output folder.

Each step reports progress as (fraction of the whole build, message). A step with a digest is
recorded when it finishes and skipped on the next run while its digest is unchanged, so closing
the builder (or a crash) never costs the hour-long Unity import again. Steps without a digest are
cheap or must always run.

The pipeline reads its settings from a config file the builder writes (IWYK_CONFIG) and finds the
tools bundled with the builder through IWYK_PREBUILT_DIR.
"""
from __future__ import annotations

import contextlib
import hashlib
import json
import os
import secrets
import shutil
import sys
from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable

from builder import VERSION
from builder.state import State

NEED_GB = 60
CONFIG_FILE = "builder-config.json"
LOCK_FILE = ".builder.lock"

Progress = Callable[[float, str], None]


class BuildError(Exception):
    pass


@dataclass
class Choices:
    base: Path
    update: Path
    apk: Path | None  # None: the player opted in to building without the app
    host: str  # "tunnel" or "account"
    out: Path
    work: Path
    untested_ok: bool = False  # the player accepted an untested release of the game


@dataclass
class Step:
    name: str
    weight: float
    run: Callable[["Context", Progress], None]
    digest: Callable[["Context"], str | None] = lambda ctx: None


@dataclass
class Context:
    choices: Choices
    log: Callable[[str], None] = print
    base_dir: Path | None = None
    update_dir: Path | None = None
    page_url: str | None = None
    extra: dict = field(default_factory=dict)


def write_config(c: Choices, base: Path | None = None, update: Path | None = None, page_url: str | None = None) -> Path:
    """The pipeline's config for this build (instead of the repository's config/local.json)."""
    data = {"base_dump": str(base or c.base), "patch_dump": str(update or c.update), "work_dir": str(c.work),
            "page_host": "url" if page_url else "tunnel", "phone_page_url": page_url}
    if c.apk:
        data["apk"] = str(c.apk)
    from pipeline import unity
    editor = unity.find_editor()
    if editor:
        data["unity_editor"] = str(editor)
    c.work.mkdir(parents=True, exist_ok=True)
    p = c.work / CONFIG_FILE
    p.write_text(json.dumps(data, indent=2) + "\n")
    return p


def check_disk(path: Path, need_gb: int = NEED_GB) -> None:
    p = Path(path)
    while not p.exists() and p != p.parent:
        p = p.parent
    free = shutil.disk_usage(p).free / 2**30
    if free < need_gb:
        raise BuildError(f"not enough free disk space: the build needs about {need_gb} GB, "
                         f"{free:.0f} GB are free on that drive")


def finish(build: Path, out: Path) -> Path:
    """Move the finished game (and its files) into the output folder."""
    build, out = Path(build), Path(out)
    out.mkdir(parents=True, exist_ok=True)
    for item in sorted(build.iterdir()):
        target = out / item.name
        if target.exists():
            shutil.rmtree(target) if target.is_dir() and not target.is_symlink() else target.unlink()
        shutil.move(str(item), str(target))
    build.rmdir()
    return out


def _alive(pid: int) -> bool:
    if pid <= 0:
        return False
    if sys.platform == "win32":
        import ctypes
        h = ctypes.windll.kernel32.OpenProcess(0x1000, False, pid)  # PROCESS_QUERY_LIMITED_INFORMATION
        if not h:
            return False
        code = ctypes.c_ulong()
        ctypes.windll.kernel32.GetExitCodeProcess(h, ctypes.byref(code))
        ctypes.windll.kernel32.CloseHandle(h)
        return code.value == 259  # STILL_ACTIVE
    try:
        os.kill(pid, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    return True


@contextlib.contextmanager
def work_lock(work: Path):
    """Only one builder at a time per work folder (two would delete each other's files)."""
    work = Path(work)
    work.mkdir(parents=True, exist_ok=True)
    lock = work / LOCK_FILE
    for _ in range(2):
        try:
            fd = os.open(lock, os.O_CREAT | os.O_EXCL | os.O_WRONLY)
            break
        except FileExistsError:
            try:
                pid = int(lock.read_text().strip() or 0)
            except (OSError, ValueError):
                pid = 0
            if _alive(pid):
                raise BuildError("another builder is already building in this folder; "
                                 "wait for it or close it first") from None
            lock.unlink(missing_ok=True)
    else:
        raise BuildError(f"cannot lock {work}")
    try:
        os.write(fd, str(os.getpid()).encode())
        os.close(fd)
        yield
    finally:
        lock.unlink(missing_ok=True)


def _digest(*parts) -> str:
    return hashlib.sha256(("\0".join(str(p) for p in parts) + "\0" + VERSION).encode()).hexdigest()[:20]


def _file_key(p: Path | None) -> str:
    if not p:
        return "-"
    st = Path(p).stat()
    return f"{p}:{st.st_size}:{st.st_mtime_ns}"


def _inputs_key(ctx: Context) -> str:
    c = ctx.choices
    return _digest(_file_key(c.base), _file_key(c.update), _file_key(c.apk))


# ---------------------------------------------------------------- steps


def step_inputs(ctx: Context, progress: Progress) -> None:
    from pipeline import inputs
    c = ctx.choices
    check_disk(c.work)
    dirs = {}
    for role, path in (("base", c.base), ("update", c.update)):
        info = inputs.check_game(path, role)
        if not info.supported and not c.untested_ok:
            raise BuildError(f"{Path(path).name} is an untested release of That's You! "
                             f"({info.content_id} {info.version}); tick \"try anyway\" to build it")
        if info.kind == "pkg":
            progress(0.1 if role == "base" else 0.6, f"unpacking the {role} package")
            dirs[role] = inputs.unpack_pkg(path, c.work / "input" / role, c.work / "input" / "scratch", log=ctx.log)
        else:
            dirs[role] = Path(path)
    if c.apk:
        progress(0.9, "checking the app")
        inputs.check_apk(c.apk)
    ctx.base_dir, ctx.update_dir = dirs["base"], dirs["update"]
    write_config(c, ctx.base_dir, ctx.update_dir, ctx.page_url)


def step_unity(ctx: Context, progress: Progress) -> None:
    from pipeline import unity, unity_install
    if not unity.find_editor():
        if not unity_install.hub_exe().is_file():
            raise BuildError("Unity Hub is not installed: install it, sign in, then start the build again")
        versions = json.loads(unity.VERSIONS.read_text())["unity"]
        progress(0.05, f"installing Unity {versions['version']} (large download)")
        unity_install.install(versions["version"], versions["changeset"], log=ctx.log)
    progress(0.8, "checking the Unity licence")
    if unity_install.status() != "ready":
        raise BuildError("Unity is installed but not licensed: open Unity Hub, sign in with a free Unity "
                         "account, then start the build again")
    write_config(ctx.choices, ctx.base_dir, ctx.update_dir, ctx.page_url)


def step_merge(ctx: Context, progress: Progress) -> None:
    from pipeline import merge
    merge.merge(ctx.base_dir, ctx.update_dir, ctx.choices.work / "merged")


def step_extract_game(ctx: Context, progress: Progress) -> None:
    from pipeline import extract_game
    extract_game.main()


def step_extract_app(ctx: Context, progress: Progress) -> None:
    from pipeline import extract_app
    extract_app.main()


def step_project(ctx: Context, progress: Progress) -> None:
    from pipeline import build_all, compile_report, config
    c = config.load()
    w = c.work_dir
    rc = build_all.build_project(c, w / "export" / "game" / "ExportedProject", w / "project", log=lambda *a: ctx.log(" ".join(map(str, a))))
    report = compile_report.parse(w / "reports" / "unity-import.log", exit_code=rc)
    compile_report.write(report, w / "reports")
    if report.failed:
        raise BuildError(f"Unity could not import the game (exit {rc}); see {w / 'reports' / 'compile.md'}")


def step_phone_page(ctx: Context, progress: Progress) -> None:
    from pipeline import build_web
    if build_web.main([]) != 0:
        raise BuildError("building the phone page failed")


def step_hosting(ctx: Context, progress: Progress) -> None:
    c = ctx.choices
    if c.host != "account":
        ctx.page_url = None
        write_config(c, ctx.base_dir, ctx.update_dir, None)
        return
    from pipeline import cf_account
    wr = cf_account.ensure_wrangler(c.work)
    if not cf_account.logged_in(wr):
        progress(0.3, "sign in to Cloudflare in your browser")
        cf_account.login(wr)
    name_file = c.work / "web-deploy-account" / "worker-name"
    name = name_file.read_text().strip() if name_file.is_file() else cf_account.worker_name(secrets.token_bytes(16))
    name_file.parent.mkdir(parents=True, exist_ok=True)
    name_file.write_text(name)
    progress(0.6, "putting the phone page on your Cloudflare account")
    ctx.page_url = cf_account.deploy(wr, c.work / "web-dist", name, c.work / "web-deploy-account")
    write_config(c, ctx.base_dir, ctx.update_dir, ctx.page_url)


def step_player(ctx: Context, progress: Progress) -> None:
    from pipeline import build_player
    if build_player.main([]) != 0:
        raise BuildError("building the game failed; see the details")


def step_finish(ctx: Context, progress: Progress) -> None:
    from pipeline import build_player
    target = build_player.host_target()
    build = ctx.choices.work / "builds" / target
    if not build.is_dir():
        raise BuildError(f"no finished game in {build}")
    ctx.extra["output"] = finish(build, ctx.choices.out)


def plan(c: Choices) -> list[Step]:
    """All steps in order; weights are rough shares of the total time."""
    inputs_key = lambda ctx: None  # noqa: E731 - always runs (cheap once packages are unpacked)

    def after(name):
        return lambda ctx: _digest(name, _inputs_key(ctx), ctx.choices.apk is None)

    return [
        Step("inputs", 0.04, step_inputs, inputs_key),
        Step("unity", 0.02, step_unity),
        Step("merge", 0.03, step_merge, after("merge")),
        Step("extract-game", 0.20, step_extract_game, after("extract-game")),
        Step("extract-app", 0.03, step_extract_app, after("extract-app")),
        Step("project", 0.55, step_project, after("project")),
        Step("phone-page", 0.02, step_phone_page),
        Step("hosting", 0.02, step_hosting),
        Step("player", 0.08, step_player),
        Step("finish", 0.01, step_finish),
    ]


def run_all(step_list: list[Step], ctx: Context | None, st: State, progress: Progress) -> None:
    total = sum(s.weight for s in step_list) or 1.0
    done = 0.0
    for s in step_list:
        start = done / total
        share = s.weight / total
        digest = s.digest(ctx)
        if st.done(s.name, digest):
            done += s.weight
            progress(done / total, f"{s.name}: already done")
            continue
        last = [start]

        def sub(frac: float, msg: str, start=start, share=share, last=last) -> None:
            value = max(last[0], start + share * max(0.0, min(1.0, frac)))
            last[0] = value
            progress(value, msg)

        sub(0.0, f"{s.name}: starting")
        s.run(ctx, sub)
        st.mark(s.name, digest)
        done += s.weight
        progress(done / total, f"{s.name}: done")
    progress(1.0, "finished")


def build(c: Choices, progress: Progress, log: Callable[[str], None] = print) -> Path:
    """Run every step (resuming finished ones). Returns the output folder."""
    if getattr(sys, "frozen", False):
        os.environ.setdefault("IWYK_PREBUILT_DIR", str(Path(sys._MEIPASS) / "prebuilt"))  # noqa: SLF001
    os.environ["IWYK_CONFIG"] = str(c.work / CONFIG_FILE)  # every pipeline step reads this build's config
    ctx = Context(c, log=log)
    with work_lock(c.work):
        run_all(plan(c), ctx, State(c.work), progress)
    return ctx.extra["output"]
