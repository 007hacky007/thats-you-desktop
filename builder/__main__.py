"""Builder without a window: python3 -m builder --base X --update Y [--apk Z | --no-apk]
[--host tunnel|account] --out DIR [--work DIR] [--try-untested]. With no arguments it opens the window."""
from __future__ import annotations

import argparse
import sys
from pathlib import Path


def main(argv=None) -> int:
    argv = sys.argv[1:] if argv is None else argv
    if not argv:
        from builder import gui
        return gui.main()
    if argv == ["--self-test"]:
        return self_test()
    from builder import steps
    ap = argparse.ArgumentParser(prog="builder", description=__doc__.splitlines()[0])
    ap.add_argument("--base", type=Path, required=True, help="PS4 base game: folder or .pkg")
    ap.add_argument("--update", type=Path, required=True, help="its update: folder or .pkg")
    g = ap.add_mutually_exclusive_group(required=True)
    g.add_argument("--apk", type=Path, help="the That's You! app 1.6 (recommended)")
    g.add_argument("--no-apk", action="store_true", help="build without the app (plain English-only phone page, not tested)")
    ap.add_argument("--host", choices=("tunnel", "account"), default="tunnel")
    ap.add_argument("--out", type=Path, required=True, help="where the finished game goes")
    ap.add_argument("--work", type=Path, help="work folder (default: <out>/.build)")
    ap.add_argument("--try-untested", action="store_true", help="build an untested release of the game")
    ap.add_argument("--self-test", action="store_true", help=argparse.SUPPRESS)
    a = ap.parse_args(argv)
    c = steps.Choices(base=a.base.expanduser().resolve(), update=a.update.expanduser().resolve(),
                      apk=None if a.no_apk else a.apk.expanduser().resolve(), host=a.host,
                      out=a.out.expanduser().resolve(),
                      work=(a.work or a.out / ".build").expanduser().resolve(), untested_ok=a.try_untested)
    try:
        out = steps.build(c, lambda f, m: print(f"[{f * 100:5.1f}%] {m}", flush=True))
    except (steps.BuildError, Exception) as e:  # noqa: BLE001 - the player gets one clear line
        print(f"build stopped: {e}", file=sys.stderr)
        return 1
    print(f"done: the game is in {out}")
    return 0


def self_test() -> int:
    """Checks a packaged builder: bundled files, libraries and prebuilt tools are all there."""
    import os
    from builder import steps  # noqa: F401
    from pipeline import build_web, cf_account, csc_check, game_edits, pkg, tools, unity, web_common  # noqa: F401
    import cryptography.hazmat.primitives.ciphers  # noqa: F401
    import PySide6.QtWidgets  # noqa: F401
    root = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parent.parent))
    want = [web_common.WEB_SRC / "index.html", csc_check.PORT_DIR, tools.VERSIONS, cf_account.LOCK_DIR / "package-lock.json",
            root / "native" / "iwyknet" / "unity"]
    if getattr(sys, "frozen", False):
        pre = root / "prebuilt"
        want += [pre / "native" / ("win-x64" if sys.platform == "win32" else "macos-arm64")]
        if sys.platform == "darwin":
            want.append(pre / "vgmstream-cli")
    missing = [str(p) for p in want if not Path(p).exists()]
    n = len(game_edits.all_recipes())
    print(f"self-test: {n} game edit recipes, {len(want) - len(missing)}/{len(want)} bundled paths present")
    for m in missing:
        print("missing:", m)
    return 1 if missing or n == 0 else 0


if __name__ == "__main__":
    sys.exit(main())
