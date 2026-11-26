"""Automated playthrough: the real Mac game plus N headless-Chrome phones playing whole games.

CLI: python3 -m pipeline.playthrough [--phones 3] [--minutes 20] [--name NAME] [--keys KEYS] [--headful]

Serves the built phone page (work/web-dist) on 127.0.0.1, starts the player with IWYK_PAGE_URL
pointing at it and -portJoinUrlFile, opens one browser context per phone at the join URL (fake
camera) and lets a simple policy play: join, name, card, selfie, hold START, answer, draw, type,
take photos, continue. Every new phone screen is screenshotted. The TV is screenshotted by the
in-game observer. Report: work/runs/<name>/playthrough.json (screens and variants each phone saw,
TV scenes, stuck phones, page and player errors). Exit 0 iff no phone got stuck and nothing threw.
"""
from __future__ import annotations

import argparse
import json
import os
import random
import re
import sys
import threading
import time
from pathlib import Path

from pipeline import run_player, web_common, web_e2e, web_serve

STUCK_S = 300.0  # same phone screen with no TV progress for this long -> stuck
TAP_GAP_S = 1.5

STATE_JS = r"""(() => {
  const d = document.body ? document.body.dataset : {};
  const vis = [];
  for (const el of document.querySelectorAll('[data-testid]')) {
    const r = el.getBoundingClientRect(), cs = getComputedStyle(el);
    if (r.width > 0 && r.height > 0 && cs.visibility !== 'hidden' && cs.display !== 'none' && Number(cs.opacity) !== 0
        && !el.disabled && !el.closest('[hidden]')) vis.push(el.dataset.testid);
  }
  const inp = document.querySelector('[data-testid="name-input"],[data-testid="text-input"]');
  return {screen: d.screen || '', variant: d.variant || '', overlay: d.overlay || '', visible: vis,
          inputValue: inp ? inp.value : null};
})()"""


def decide(state: dict, phone: int, mem: dict, rng: random.Random) -> list[tuple]:
    """Next actions for one phone: ("tap", id) / ("hold", id, ms) / ("type", id, text) / ("draw", id)."""
    vis = set(state.get("visible") or [])
    screen = state.get("screen", "")
    if "popup-close" in vis:
        return [("tap", "popup-close")]
    if "leave-cancel" in vis:
        return [("tap", "leave-cancel")]
    if "effects-close" in vis:
        return [("tap", "effects-close")]
    if "connect-play" in vis:
        return [("tap", "connect-play")]
    if "name-input" in vis:
        if not state.get("inputValue"):
            return [("type", "name-input", f"Bot{phone + 1}")]
        if "name-next" in vis:
            return [("tap", "name-next")]
    cards = sorted(v for v in vis if re.fullmatch(r"card-\d+", v))
    if cards:
        pick = f"card-{phone + 1}" if f"card-{phone + 1}" in cards else cards[0]
        acts = [("tap", pick)]
        return acts + ([("tap", "card-confirm")] if "card-confirm" in vis else [])
    if "card-confirm" in vis:
        return [("tap", "card-confirm")]
    if "use" in vis:
        return [("tap", "use")]
    if "shutter" in vis:
        return [("tap", "shutter")]
    if "profile-confirm" in vis:
        return [("tap", "profile-confirm")]
    if "start" in vis:
        return [("hold", "start", 2600)] if phone == 0 else []
    if "continue" in vis:
        return [("tap", "continue")]
    answers = [v for v in vis if re.fullmatch(r"answer-\d+", v)]
    if answers:
        key = (screen, state.get("variant"), tuple(sorted(answers)))
        if mem.get("answered") != key:
            mem["answered"] = key
            return [("tap", rng.choice(answers))] + ([("tap", "answer-confirm")] if "answer-confirm" in vis else [])
    if "answer-confirm" in vis:
        return [("tap", "answer-confirm")]
    if "draw-canvas" in vis:
        key = (screen, state.get("variant"))
        if mem.get("drew") != key:
            mem["drew"] = key
            return [("draw", "draw-canvas")] + ([("tap", "draw-confirm")] if "draw-confirm" in vis else [])
        if "draw-confirm" in vis:
            return [("tap", "draw-confirm")]
    if "text-input" in vis:
        if not state.get("inputValue"):
            return [("type", "text-input", f"Answer from bot {phone + 1}")]
        if "text-confirm" in vis:
            return [("tap", "text-confirm")]
    return []


class Phone:
    def __init__(self, idx: int, page, out: Path, rng: random.Random):
        self.idx, self.page, self.out, self.rng = idx, page, out, rng
        self.mem: dict = {}
        self.seen: dict[str, int] = {}
        self.last_key = None
        self.since = time.monotonic()
        self.last_tap: dict[str, float] = {}
        self.actions = 0
        self.errors: list[str] = []

    def step(self, log) -> str:
        try:
            st = self.page.evaluate(STATE_JS) or {}
        except Exception as e:  # page reloading
            return f"eval failed: {e}"
        key = f"{st.get('screen')}:{st.get('variant')}" + (f"+{st['overlay']}" if st.get("overlay") else "")
        if key != self.last_key:
            self.last_key, self.since = key, time.monotonic()
            n = self.seen.get(key, 0)
            self.seen[key] = n + 1
            log(f"phone {self.idx + 1}: {key}")
            if n < 2:
                safe = re.sub(r"[^A-Za-z0-9_.+-]", "_", key)
                try:
                    self.page.screenshot(self.out / f"p{self.idx + 1}" / f"{len(self.seen):03d}_{safe}.png")
                except Exception:
                    pass
        for act in decide(st, self.idx, self.mem, self.rng):
            now = time.monotonic()
            if act[0] == "tap" and now - self.last_tap.get(act[1], 0) < TAP_GAP_S:
                continue
            try:
                if act[0] == "tap":
                    self.page.tap(act[1])
                    self.last_tap[act[1]] = now
                elif act[0] == "hold":
                    self.page.hold(act[1], act[2])
                elif act[0] == "type":
                    self.page.type(act[1], act[2])
                elif act[0] == "draw":
                    pts = [(self.rng.uniform(0.1, 0.9), self.rng.uniform(0.1, 0.9)) for _ in range(6)]
                    self.page.draw(act[1], pts)
                self.actions += 1
                time.sleep(0.25)
            except Exception as e:
                self.errors.append(f"{key} {act}: {e}")
        return key


def tv_scenes(player_log: Path) -> list[str]:
    out = []
    if player_log.is_file():
        for m in re.finditer(r"\[PortObserve\] scene loaded: (\S+)", player_log.read_text(errors="replace")):
            out.append(m.group(1))
    return out


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="python3 -m pipeline.playthrough")
    ap.add_argument("--phones", type=int, default=3)
    ap.add_argument("--minutes", type=float, default=20.0)
    ap.add_argument("--name", default=time.strftime("play-%Y%m%d-%H%M%S"))
    ap.add_argument("--keys", default="8:Return,20:Return,30:Return",
                    help="TV key script for the language select and splash (run_player --keys format)")
    ap.add_argument("--headful", action="store_true")
    ap.add_argument("--seed", type=int, default=1)
    a = ap.parse_args(argv)

    from pipeline import config
    c = config.load()
    cfg = web_common.load_local()
    run_dir = (c.work_dir / "runs" / a.name).resolve()
    if run_dir.exists() and any(run_dir.iterdir()):
        print(f"run dir not empty: {run_dir}", file=sys.stderr)
        return 2
    run_dir.mkdir(parents=True, exist_ok=True)
    server, base = web_serve.start_background(web_common.web_dist(cfg))
    join_file = run_dir / "join-url.txt"
    os.environ["IWYK_PAGE_URL"] = base  # the player inherits it; local test only
    exe = run_player.find_executable(c.work_dir / "builds" / "mac" / "ThatsYou.app")
    seconds = a.minutes * 60 + 60
    result: dict = {}

    def player():
        result["rc"] = run_player.run(exe, run_dir, seconds, a.keys, shot_interval=15.0,
                                      extra=["-portJoinUrlFile", str(join_file)], audio={"mute": True})

    t = threading.Thread(target=player, daemon=True)
    t.start()
    timeline: list[str] = []
    t0 = time.monotonic()

    def log(msg: str):
        line = f"{time.monotonic() - t0:7.1f} {msg}"
        timeline.append(line)
        print(line, flush=True)

    deadline = time.monotonic() + 120
    while not join_file.is_file() and time.monotonic() < deadline and t.is_alive():
        time.sleep(1)
    if not join_file.is_file():
        print("game never published a join URL", file=sys.stderr)
        server.shutdown()
        return 1
    url = join_file.read_text().strip()
    log("join URL ready")
    cam = run_dir / "camera.y4m"
    web_e2e.write_test_pattern(cam)
    browser = web_e2e.Browser(web_e2e.find_chrome(None), run_dir, headful=a.headful, keep=False, camera_file=cam)
    rng = random.Random(a.seed)
    phones = []
    stuck: list[str] = []
    try:
        for i in range(a.phones):
            page = browser.open_page(run_dir / f"page-phone{i + 1}.log")
            page.navigate(url)
            phones.append(Phone(i, page, run_dir / "phones", rng))
            time.sleep(1.0)
        end = time.monotonic() + a.minutes * 60
        tv_progress = time.monotonic()
        last_tv = None
        while time.monotonic() < end and t.is_alive():
            for p in phones:
                p.step(log)
            scenes = tv_scenes(run_dir / "player.log")
            shots = len(list((run_dir / "shots").glob("*.png")))
            if (len(scenes), shots) != last_tv:
                last_tv, tv_progress = (len(scenes), shots), time.monotonic()
            for p in phones:
                if time.monotonic() - p.since > STUCK_S and p.last_key not in stuck:
                    stuck.append(f"phone {p.idx + 1} on {p.last_key}")
                    log(f"STUCK phone {p.idx + 1} on {p.last_key}")
                    p.since = time.monotonic()
            time.sleep(0.5)
    finally:
        browser.close()
        server.shutdown()
    t.join(seconds + 30)
    page_errors = web_e2e.collect_page_errors(run_dir)
    player_log = (run_dir / "player.log").read_text(errors="replace") if (run_dir / "player.log").is_file() else ""
    exceptions = sorted(set(re.findall(r"^(\w*Exception[^\n]*)", player_log, re.M)))[:40]
    report = {
        "phones": [{"phone": p.idx + 1, "screens": p.seen, "actions": p.actions, "errors": p.errors[:20]}
                   for p in phones],
        "tv_scenes": tv_scenes(run_dir / "player.log"),
        "stuck": stuck, "page_errors": page_errors[:40], "player_exceptions": exceptions,
        "timeline": timeline,
    }
    (run_dir / "playthrough.json").write_text(json.dumps(report, indent=1))
    print(f"screens per phone: {[len(p.seen) for p in phones]}; stuck: {len(stuck)}; "
          f"page errors: {len(page_errors)}; player exceptions: {len(exceptions)}")
    print(f"report: {run_dir / 'playthrough.json'}")
    return 0 if not stuck and not exceptions and not page_errors else 1


if __name__ == "__main__":
    sys.exit(main())
