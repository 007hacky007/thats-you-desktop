"""Check that every phone page screen fits the screen without scrolling (portrait phones).

CLI: python3 -m pipeline.web_fit [--art] [--only SCREEN] [--chrome PATH]

Opens every demo screen (the links of #demo) and every demo overlay in headless Chrome at
375x667, 430x932 and 360x800, and a landscape viewport to check the rotate hint. A screen fails
when the document scrolls, when any element can scroll its content, or when visible content is
cut off at the viewport edge. Report: work/reports/web-fit.json, screenshots in work/e2e/fit/.
Exit code 0 iff nothing failed.
"""
from __future__ import annotations

import argparse
import json
import sys
import time
from pathlib import Path

from pipeline import web_common, web_e2e, web_serve

VIEWPORTS = [(375, 667), (430, 932), (360, 800)]
LANDSCAPE = (844, 390)

METRICS_JS = r"""(() => {
  const W = innerWidth, H = innerHeight, se = document.scrollingElement;
  const out = {docScroll: [se.scrollWidth - W, se.scrollHeight - H], scrollers: [], cut: []};
  const name = (e) => (e.dataset && e.dataset.testid) || (e.id ? '#' + e.id : '') ||
    (e.tagName.toLowerCase() + (e.className && typeof e.className === 'string' ? '.' + e.className.trim().split(/\s+/).join('.') : ''));
  for (const e of document.body.querySelectorAll('*')) {
    const cs = getComputedStyle(e);
    if (cs.display === 'none' || cs.visibility === 'hidden' || e.closest('[hidden],[aria-hidden="true"]')) continue;
    const scrollY = /(auto|scroll)/.test(cs.overflowY) && e.scrollHeight - e.clientHeight > 1;
    const scrollX = /(auto|scroll)/.test(cs.overflowX) && e.scrollWidth - e.clientWidth > 1 && !e.dataset.scrollX;
    if (scrollY || scrollX) out.scrollers.push(name(e) + (scrollY ? ' y+' + (e.scrollHeight - e.clientHeight) : ' x+' + (e.scrollWidth - e.clientWidth)));
    const r = e.getBoundingClientRect();
    if (!r.width || !r.height || e.children.length) continue;
    if (cs.position === 'fixed' && cs.pointerEvents === 'none') continue;
    if (r.bottom > H + 1 || r.right > W + 1 || r.top < -1 || r.left < -1) {
      if (!e.closest('.play-fx-list,[data-scroll-x]') && !/translateY\(-1|matrix\(/.test(cs.transform)) out.cut.push(name(e) + ' ' + [r.left, r.top, r.right, r.bottom].map(Math.round).join(','));
    }
  }
  out.rotate = !!document.querySelector('[data-testid="rotate-hint"]') &&
    getComputedStyle(document.querySelector('[data-testid="rotate-hint"]')).display !== 'none';
  out.screen = document.body.dataset.screen || '';
  out.art = document.body.classList.contains('art');
  return out;
})()"""

LINKS_JS = """[...document.querySelectorAll('a[href^="#demo="]')].map(a => a.getAttribute('href'))"""


def judge(m: dict, landscape: bool = False) -> list[str]:
    """Problems found in one METRICS_JS result."""
    if landscape:
        return [] if m.get("rotate") else ["landscape shows no rotate hint"]
    probs = []
    dx, dy = m.get("docScroll", [0, 0])
    if dy > 1 or dx > 1:
        probs.append(f"page scrolls by {dx}x{dy}")
    probs += [f"scrolls: {s}" for s in m.get("scrollers", [])]
    probs += [f"cut off: {s}" for s in m.get("cut", [])[:5]]
    if m.get("rotate"):
        probs.append("rotate hint shown in portrait")
    return probs


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(prog="python3 -m pipeline.web_fit")
    ap.add_argument("--art", action="store_true", help="check the build_web output (original art)")
    ap.add_argument("--only", help="only demo links containing this text")
    ap.add_argument("--chrome")
    a = ap.parse_args(argv)
    cfg = web_common.load_local()
    root = web_common.web_dist(cfg) if a.art else web_common.WEB_SRC
    out_dir = web_common.work_dir(cfg) / "e2e" / "fit"
    out_dir.mkdir(parents=True, exist_ok=True)
    server, base = web_serve.start_background(root)
    browser = web_e2e.Browser(web_e2e.find_chrome(a.chrome), out_dir, headful=False, keep=False, log=print)
    results, failed = [], 0
    try:
        page = browser.open_page(out_dir / "console.log")
        page.navigate(base + "#demo")
        time.sleep(1.0)
        links = [h for h in page.evaluate(LINKS_JS) if not a.only or a.only in h]
        for w, h in VIEWPORTS + [LANDSCAPE]:
            land = (w, h) == LANDSCAPE
            page.s.send("Emulation.setDeviceMetricsOverride",
                        {"width": w, "height": h, "deviceScaleFactor": 2, "mobile": True,
                         "screenOrientation": {"type": "landscapePrimary" if land else "portraitPrimary",
                                               "angle": 90 if land else 0}})
            for link in (links[:1] if land else links):
                page.navigate(base + link)
                time.sleep(0.6)
                m = page.evaluate(METRICS_JS)
                probs = judge(m, land)
                tag = link[len("#demo="):].replace(":", "_").replace("&overlay=", "+")
                shot = out_dir / f"{w}x{h}" / f"{tag}.png"
                page.screenshot(shot)
                results.append({"viewport": f"{w}x{h}", "link": link, "problems": probs, "art": m.get("art"),
                                "shot": str(shot)})
                if probs:
                    failed += 1
                    print(f"FAIL {w}x{h} {link}: " + "; ".join(probs))
    finally:
        browser.close()
        server.shutdown()
    report = web_common.work_dir(cfg) / "reports" / "web-fit.json"
    report.parent.mkdir(parents=True, exist_ok=True)
    report.write_text(json.dumps(results, indent=1))
    print(f"{len(results) - failed}/{len(results)} screens fit -> {report}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
