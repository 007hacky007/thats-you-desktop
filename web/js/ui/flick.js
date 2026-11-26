// Flick gesture (a quick swipe), used to confirm answers, cards and photos.
// classifyFlick is pure; attachFlick wires it to Pointer Events.

// samples: [{ x, y, t }] in CSS px and ms, oldest first. Only the movement of
// the last `maxMs` counts, so a slow drag followed by a quick flick works.
export function classifyFlick(samples, { minDistance = 60, maxMs = 600 } = {}) {
  if (!Array.isArray(samples) || samples.length < 2) return null;
  const last = samples[samples.length - 1];
  let first = samples[0];
  for (const s of samples) {
    if (last.t - s.t <= maxMs) { first = s; break; }
  }
  if (first === last || last.t - first.t > maxMs) return null;
  const dx = last.x - first.x;
  const dy = last.y - first.y;
  const ax = Math.abs(dx);
  const ay = Math.abs(dy);
  if (Math.max(ax, ay) < minDistance) return null;
  // the main axis must clearly dominate (about 30 degrees around it)
  if (ay >= ax) {
    if (ax > ay * 0.6) return null;
    return dy < 0 ? 'up' : 'down';
  }
  if (ay > ax * 0.6) return null;
  return dx < 0 ? 'left' : 'right';
}

const realClock = {
  now: () => (globalThis.performance ? performance.now() : Date.now()),
};

// attachFlick(el, { onFlick(dir), onMove(dx, dy), onEnd(), minDistance, maxMs }) -> detach
export function attachFlick(el, opts = {}) {
  const { onFlick, onMove, onEnd, minDistance = 60, maxMs = 600, clock = realClock } = opts;
  let pointerId = null;
  let samples = [];
  let origin = null;

  const reset = () => {
    pointerId = null;
    samples = [];
    origin = null;
  };
  const down = (ev) => {
    if (pointerId !== null || (ev.button !== undefined && ev.button !== 0)) return;
    pointerId = ev.pointerId;
    origin = { x: ev.clientX, y: ev.clientY };
    samples = [{ x: ev.clientX, y: ev.clientY, t: clock.now() }];
    try { el.setPointerCapture(ev.pointerId); } catch { /* not capturable */ }
  };
  const move = (ev) => {
    if (ev.pointerId !== pointerId) return;
    samples.push({ x: ev.clientX, y: ev.clientY, t: clock.now() });
    if (samples.length > 64) samples.splice(0, samples.length - 64);
    if (onMove) onMove(ev.clientX - origin.x, ev.clientY - origin.y);
  };
  const up = (ev) => {
    if (ev.pointerId !== pointerId) return;
    samples.push({ x: ev.clientX, y: ev.clientY, t: clock.now() });
    const dir = classifyFlick(samples, { minDistance, maxMs });
    reset();
    if (onEnd) onEnd(dir);
    if (dir && onFlick) onFlick(dir);
  };
  const cancel = (ev) => {
    if (ev.pointerId !== pointerId) return;
    reset();
    if (onEnd) onEnd(null);
  };

  el.addEventListener('pointerdown', down);
  el.addEventListener('pointermove', move);
  el.addEventListener('pointerup', up);
  el.addEventListener('pointercancel', cancel);
  return () => {
    el.removeEventListener('pointerdown', down);
    el.removeEventListener('pointermove', move);
    el.removeEventListener('pointerup', up);
    el.removeEventListener('pointercancel', cancel);
    reset();
  };
}
