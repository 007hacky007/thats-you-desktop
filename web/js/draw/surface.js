// Drawing surface: 384x512 output, drawn at 2x internally. Strokes live on
// their own layer above the background, so the eraser (destination-out on
// the stroke layer) reveals the background again. Undo replays the stroke
// list without the last stroke. Nothing touches browser globals at import.

import {
  OUTPUT, SCALE, TOOLS, COLOURS, ALL_COLOURS, BRUSH_EDGE, SPRAY,
  colourCss, colourRgb, diameter, stampSpacing, stampsAlong, coloursUsedMask, replay, coverFit,
} from './tools.js';
import { encodeCanvasJpeg } from '../camera/capture.js';

const W = OUTPUT.width * SCALE;
const H = OUTPUT.height * SCALE;

function makeCanvas(document, w, h) {
  const c = document.createElement('canvas');
  c.width = w;
  c.height = h;
  return c;
}

async function defaultLoadImage(url, document) {
  const img = document.createElement('img');
  img.decoding = 'async';
  img.src = url;
  if (typeof img.decode === 'function') {
    await img.decode();
  } else {
    await new Promise((resolve, reject) => {
      img.onload = resolve;
      img.onerror = () => reject(new Error('image load failed'));
    });
  }
  return img;
}

export function createDrawingSurface({
  document = globalThis.document,
  log = null,
  raf = globalThis.requestAnimationFrame?.bind(globalThis),
  loadImage = (url) => defaultLoadImage(url, document),
  encode = encodeCanvasJpeg,
} = {}) {
  const element = makeCanvas(document, W, H);
  element.setAttribute?.('data-testid', 'draw-canvas');
  if (element.style) {
    element.style.touchAction = 'none';
    element.style.userSelect = 'none';
  }
  const view = element.getContext('2d');
  const committed = makeCanvas(document, W, H); // finished strokes
  const committedCtx = committed.getContext('2d');
  const work = makeCanvas(document, W, H); // finished strokes + the live one
  const workCtx = work.getContext('2d');

  let tool = 'brush';
  let colour = 1;
  let size = 2;
  let background = null; // image source
  let bgToken = 0;
  const strokes = [];
  let current = null;
  let pointerId = null;
  let frameQueued = false;
  let destroyed = false;
  const listeners = new Set();
  const sprayCache = new Map();

  function emit() {
    for (const fn of [...listeners]) {
      try { fn(); } catch (err) { log?.error?.(`draw change listener: ${err && err.message}`); }
    }
  }

  function sprayStamp(colourId, d) {
    const key = `${colourId}|${d}`;
    let s = sprayCache.get(key);
    if (s) return s;
    const px = Math.max(2, Math.ceil(d));
    s = makeCanvas(document, px, px);
    const ctx = s.getContext('2d');
    const [r, g, b] = colourRgb(colourId);
    const grad = ctx.createRadialGradient(px / 2, px / 2, 0, px / 2, px / 2, px / 2);
    grad.addColorStop(SPRAY.inner, `rgba(${r}, ${g}, ${b}, ${SPRAY.alpha})`);
    grad.addColorStop(SPRAY.outer, `rgba(${r}, ${g}, ${b}, 0)`);
    ctx.fillStyle = grad;
    ctx.fillRect(0, 0, px, px);
    sprayCache.set(key, s);
    return s;
  }

  function paintLine(ctx, pts, width) {
    ctx.lineWidth = width;
    if (pts.length === 1) {
      ctx.beginPath();
      ctx.arc(pts[0][0] * SCALE, pts[0][1] * SCALE, width / 2, 0, Math.PI * 2);
      ctx.fill();
      return;
    }
    ctx.beginPath();
    ctx.moveTo(pts[0][0] * SCALE, pts[0][1] * SCALE);
    for (let i = 1; i < pts.length; i++) ctx.lineTo(pts[i][0] * SCALE, pts[i][1] * SCALE);
    ctx.stroke();
  }

  function paintStroke(ctx, s) {
    if (!s.points.length) return;
    const d = diameter(s.tool, s.size) * SCALE;
    ctx.save();
    if (s.tool === 'spray') {
      ctx.globalCompositeOperation = 'source-over';
      const stamp = sprayStamp(s.colour, d);
      const half = stamp.width / 2;
      for (const [x, y] of stampsAlong(s.points, stampSpacing('spray', s.size))) {
        ctx.drawImage(stamp, x * SCALE - half, y * SCALE - half);
      }
    } else {
      ctx.globalCompositeOperation = s.tool === 'eraser' ? 'destination-out' : 'source-over';
      const css = s.tool === 'eraser' ? '#000' : colourCss(s.colour);
      ctx.strokeStyle = css;
      ctx.fillStyle = css;
      ctx.lineCap = 'round';
      ctx.lineJoin = 'round';
      // soft rim between 0.85 and 0.9 of the radius, then the hard core
      ctx.globalAlpha = 0.5 * BRUSH_EDGE.alpha;
      paintLine(ctx, s.points, d * BRUSH_EDGE.outer);
      ctx.globalAlpha = BRUSH_EDGE.alpha;
      paintLine(ctx, s.points, d * BRUSH_EDGE.inner);
    }
    ctx.restore();
  }

  function drawBackground(ctx, w, h) {
    ctx.fillStyle = '#ffffff';
    ctx.fillRect(0, 0, w, h);
    if (!background) return;
    const bw = background.naturalWidth || background.videoWidth || background.width;
    const bh = background.naturalHeight || background.videoHeight || background.height;
    const f = coverFit(bw, bh, w, h);
    ctx.drawImage(background, f.dx, f.dy, f.dw, f.dh);
  }

  function composite() {
    if (destroyed) return;
    let layer = committed;
    if (current) {
      workCtx.clearRect(0, 0, W, H);
      workCtx.drawImage(committed, 0, 0);
      paintStroke(workCtx, current);
      layer = work;
    }
    drawBackground(view, W, H);
    view.drawImage(layer, 0, 0);
  }

  function schedule() {
    if (frameQueued || destroyed) return;
    if (!raf) { composite(); return; }
    frameQueued = true;
    raf(() => { frameQueued = false; composite(); });
  }

  function rebuild() {
    replay(strokes, {
      clear: () => committedCtx.clearRect(0, 0, W, H),
      paint: (s) => paintStroke(committedCtx, s),
    });
  }

  function toPoint(e) {
    const r = element.getBoundingClientRect();
    if (!r.width || !r.height) return null;
    return [
      ((e.clientX - r.left) / r.width) * OUTPUT.width,
      ((e.clientY - r.top) / r.height) * OUTPUT.height,
    ];
  }

  function addPoint(p) {
    if (!p || !current) return;
    const last = current.points[current.points.length - 1];
    if (last && last[0] === p[0] && last[1] === p[1]) return;
    current.points.push(p);
  }

  function finish() {
    if (!current) return;
    const s = current;
    current = null;
    pointerId = null;
    if (!s.points.length) return;
    strokes.push(s);
    paintStroke(committedCtx, s);
    composite();
    emit();
  }

  const handlers = {
    pointerdown(e) {
      if (pointerId !== null || (e.button !== undefined && e.button > 0)) return;
      e.preventDefault?.();
      pointerId = e.pointerId;
      try { element.setPointerCapture?.(e.pointerId); } catch { /* not capturable */ }
      current = { tool, colour, size, points: [] };
      addPoint(toPoint(e));
      schedule();
    },
    pointermove(e) {
      if (e.pointerId !== pointerId || !current) return;
      e.preventDefault?.();
      const evs = typeof e.getCoalescedEvents === 'function' ? e.getCoalescedEvents() : [];
      for (const ce of evs.length ? evs : [e]) addPoint(toPoint(ce));
      schedule();
    },
    pointerup(e) {
      if (e.pointerId !== pointerId) return;
      addPoint(toPoint(e));
      finish();
    },
    pointercancel(e) {
      if (e.pointerId !== pointerId) return;
      finish();
    },
  };
  for (const [ev, fn] of Object.entries(handlers)) element.addEventListener(ev, fn);

  composite();

  return {
    element,
    get tool() { return tool; },
    set tool(v) { if (TOOLS.includes(v)) tool = v; },
    get colour() { return colour; },
    set colour(v) { if (COLOURS.some((c) => c.id === v)) colour = v; },
    get size() { return size; },
    set size(v) { if (v === 1 || v === 2 || v === 3) size = v; },
    get canUndo() { return strokes.length > 0; },
    get isEmpty() { return !strokes.some((s) => s.tool !== 'eraser'); },
    get coloursAvailable() { return ALL_COLOURS; },
    get coloursUsed() { return coloursUsedMask(strokes); },
    get strokeCount() { return strokes.length; },

    on(event, fn) {
      if (event !== 'change' || typeof fn !== 'function') return () => {};
      listeners.add(fn);
      return () => listeners.delete(fn);
    },

    // resolves true when the image is shown, false when it could not load
    async setBackground(url) {
      const token = ++bgToken;
      if (!url) {
        background = null;
        composite();
        return true;
      }
      try {
        const img = await loadImage(url);
        if (token !== bgToken || destroyed) return false;
        background = img;
        composite();
        return true;
      } catch (err) {
        if (token === bgToken) {
          background = null;
          composite();
        }
        log?.warn?.(`drawing background: ${err && err.message}`);
        return false;
      }
    },

    undo() {
      if (current) finish();
      if (!strokes.length) return;
      strokes.pop();
      rebuild();
      composite();
      emit();
    },

    // test and demo hook: add a whole stroke as if drawn
    addStroke(s) {
      const stroke = {
        tool: TOOLS.includes(s.tool) ? s.tool : tool,
        colour: s.colour ?? colour,
        size: s.size ?? size,
        points: (s.points || []).map((p) => [p[0], p[1]]),
      };
      if (!stroke.points.length) return;
      strokes.push(stroke);
      paintStroke(committedCtx, stroke);
      composite();
      emit();
    },

    async export() {
      if (current) finish();
      const out = makeCanvas(document, OUTPUT.width, OUTPUT.height);
      const ctx = out.getContext('2d');
      ctx.imageSmoothingEnabled = true;
      if ('imageSmoothingQuality' in ctx) ctx.imageSmoothingQuality = 'high';
      drawBackground(ctx, OUTPUT.width, OUTPUT.height);
      ctx.drawImage(committed, 0, 0, OUTPUT.width, OUTPUT.height);
      const { jpeg, quality } = await encode(out);
      return { jpeg, quality, coloursAvailable: ALL_COLOURS, coloursUsed: coloursUsedMask(strokes) };
    },

    destroy() {
      destroyed = true;
      for (const [ev, fn] of Object.entries(handlers)) element.removeEventListener(ev, fn);
      listeners.clear();
      sprayCache.clear();
      committed.width = committed.height = 0;
      work.width = work.height = 0;
    },
  };
}
