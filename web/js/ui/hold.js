// Press-and-hold gesture (the start button charges for about 2 s).

export function holdProgress(startT, t, ms) {
  if (!(ms > 0)) return 1;
  return Math.min(1, Math.max(0, (t - startT) / ms));
}

function defaultClock() {
  return {
    now: () => (globalThis.performance ? performance.now() : Date.now()),
    setTimeout: (fn, ms) => globalThis.setTimeout(fn, ms),
    clearTimeout: (id) => globalThis.clearTimeout(id),
  };
}

// attachHold(el, { ms = 2000, onProgress(p), onDone(), onCancel() }) -> detach
// `clock` and `raf` can be injected for tests (raf null = timer ticks).
export function attachHold(el, opts = {}) {
  const { ms = 2000, onProgress, onDone, onCancel } = opts;
  const clock = opts.clock || defaultClock();
  const raf = opts.raf === undefined ? globalThis.requestAnimationFrame?.bind(globalThis) || null : opts.raf;
  const caf = globalThis.cancelAnimationFrame?.bind(globalThis);

  let active = false;
  let done = false;
  let pointerId = null;
  let startT = 0;
  let doneTimer = null;
  let tickHandle = null;
  let tickIsRaf = false;

  const report = (p) => { if (onProgress) onProgress(p); };

  const stopTicks = () => {
    if (tickHandle !== null) {
      if (tickIsRaf && caf) caf(tickHandle); else clock.clearTimeout(tickHandle);
    }
    tickHandle = null;
  };
  const tick = () => {
    tickHandle = null;
    if (!active) return;
    report(holdProgress(startT, clock.now(), ms));
    schedule();
  };
  const schedule = () => {
    if (raf) { tickIsRaf = true; tickHandle = raf(tick); } else { tickIsRaf = false; tickHandle = clock.setTimeout(tick, 50); }
  };

  const start = () => {
    if (active) return;
    active = true;
    done = false;
    startT = clock.now();
    report(0);
    if (el.classList) el.classList.add('holding');
    schedule();
    doneTimer = clock.setTimeout(() => {
      doneTimer = null;
      if (!active) return;
      active = false;
      done = true;
      stopTicks();
      report(1);
      if (el.classList) { el.classList.remove('holding'); el.classList.add('held'); }
      if (onDone) onDone();
    }, ms);
  };
  const stop = () => {
    pointerId = null;
    if (!active) return;
    active = false;
    stopTicks();
    if (doneTimer !== null) clock.clearTimeout(doneTimer);
    doneTimer = null;
    if (el.classList) el.classList.remove('holding');
    if (!done) {
      report(0);
      if (onCancel) onCancel();
    }
  };

  const down = (ev) => {
    if (ev.button !== undefined && ev.button !== 0) return;
    if (pointerId !== null) return;
    pointerId = ev.pointerId;
    try { el.setPointerCapture(ev.pointerId); } catch { /* ignore */ }
    if (ev.preventDefault) ev.preventDefault();
    start();
  };
  const up = (ev) => { if (ev.pointerId === pointerId) stop(); };
  const keydown = (ev) => {
    if ((ev.key === ' ' || ev.key === 'Enter') && !ev.repeat) {
      if (ev.preventDefault) ev.preventDefault();
      start();
    }
  };
  const keyup = (ev) => { if (ev.key === ' ' || ev.key === 'Enter') stop(); };
  const noMenu = (ev) => { if (ev.preventDefault) ev.preventDefault(); };

  el.addEventListener('pointerdown', down);
  el.addEventListener('pointerup', up);
  el.addEventListener('pointercancel', up);
  el.addEventListener('lostpointercapture', up);
  el.addEventListener('keydown', keydown);
  el.addEventListener('keyup', keyup);
  el.addEventListener('blur', stop);
  el.addEventListener('contextmenu', noMenu);

  return () => {
    stop();
    el.removeEventListener('pointerdown', down);
    el.removeEventListener('pointerup', up);
    el.removeEventListener('pointercancel', up);
    el.removeEventListener('lostpointercapture', up);
    el.removeEventListener('keydown', keydown);
    el.removeEventListener('keyup', keyup);
    el.removeEventListener('blur', stop);
    el.removeEventListener('contextmenu', noMenu);
  };
}
