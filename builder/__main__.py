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


if __name__ == "__main__":
    sys.exit(main())
