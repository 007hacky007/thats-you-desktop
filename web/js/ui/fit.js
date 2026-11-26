// Fit the current screen to the viewport height without scrolling, like the
// original app's NGUI root (fixed design layout, scaled down to fit height).

export const MIN_SCALE = 0.5;

export function fitScale(available, natural) {
  if (!(available > 0) || !(natural > 0) || natural <= available + 1) return 1;
  return Math.max(MIN_SCALE, Math.round((available / natural) * 1000) / 1000);
}

export function fitStyle(scale, natural) {
  if (scale >= 1) return { transform: '', height: '', flex: '' };
  return { transform: `scale(${scale})`, height: `${Math.ceil(natural)}px`, flex: 'none' };
}

// True when a phone is held sideways. Uses the device orientation, never the viewport shape:
// an open on-screen keyboard makes the viewport wider than tall in portrait.
export function isLandscapeDevice({ orientationType, windowOrientation, screenW, screenH, coarse }) {
  if (!coarse) return false;
  if (Math.min(screenW || 0, screenH || 0) >= 600) return false; // tablet
  if (typeof orientationType === 'string' && orientationType) return orientationType.startsWith('landscape');
  if (typeof windowOrientation === 'number') return Math.abs(windowOrientation) === 90;
  return (screenW || 0) > (screenH || 0);
}

export function typing(doc) {
  const el = doc && doc.activeElement;
  return !!el && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.isContentEditable === true);
}

function readOrientation(win) {
  const scr = win.screen || {};
  return {
    orientationType: scr.orientation && scr.orientation.type,
    windowOrientation: typeof win.orientation === 'number' ? win.orientation : undefined,
    screenW: scr.width, screenH: scr.height,
    coarse: !!(win.matchMedia && win.matchMedia('(pointer: coarse)').matches),
  };
}

// Keeps host's current screen element scaled to fit. Returns { refit, stop }.
export function attachFit(host, win = globalThis) {
  let queued = false;
  const raf = win.requestAnimationFrame ? (f) => win.requestAnimationFrame(f) : (f) => setTimeout(f, 16);
  function apply() {
    queued = false;
    const doc = win.document;
    if (doc && doc.body && doc.body.classList) {
      doc.body.classList.toggle('landscape-device', isLandscapeDevice(readOrientation(win)));
    }
    if (typing(doc)) return; // the keyboard shrinks the viewport; keep the layout as it was
    const el = host.firstElementChild;
    if (!el || !el.style) return;
    Object.assign(el.style, fitStyle(1));
    const available = host.clientHeight;
    const natural = el.scrollHeight;
    const s = fitScale(available, natural);
    Object.assign(el.style, fitStyle(s, natural));
  }
  function refit() {
    if (queued) return;
    queued = true;
    raf(apply);
  }
  const offs = [];
  if (win.addEventListener) {
    win.addEventListener('resize', refit);
    win.addEventListener('orientationchange', refit);
    offs.push(() => win.removeEventListener('resize', refit));
    offs.push(() => win.removeEventListener('orientationchange', refit));
    if (win.document && win.document.addEventListener) {
      win.document.addEventListener('focusout', refit); // keyboard closed
      offs.push(() => win.document.removeEventListener('focusout', refit));
    }
  }
  if (host.addEventListener) {
    host.addEventListener('load', refit, true); // images
    offs.push(() => host.removeEventListener('load', refit, true));
  }
  if (win.MutationObserver) {
    const mo = new win.MutationObserver(refit);
    mo.observe(host, { childList: true, subtree: true, characterData: true, attributes: true, attributeFilter: ['class', 'hidden', 'src'] });
    offs.push(() => mo.disconnect());
  }
  return { refit, stop: () => offs.forEach((f) => f()) };
}
