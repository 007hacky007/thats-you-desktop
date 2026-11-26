// Effect lookup maps. A map is an absolute lookup table: the texel at output
// position (x, y) holds the feed position to sample. Encoding (shared with the
// app's map art): X = (R + G) / 2 and Y = (B + A) / 2, normalized, Y up.
// Rows are stored top first, as in a PNG; the renderer uploads with FLIP_Y.
//
// When the art maps are missing, the procedural maps below are used. They are
// our own formulas; all of them keep the image corners in place except the
// Kaleidoscope, which folds the whole picture around the centre.

import { decodePng } from './png.js';

export const MAP_EFFECTS = Object.freeze([
  'Warp', 'Shrink', 'Stretch', 'Kaleidoscope', 'Bulge', 'Twist', 'Square', 'Grid', 'Hourglass',
]);

const TAU = Math.PI * 2;
const ASPECT = 0.75; // output width / height (3:4)
const clamp01 = (v) => (v < 0 ? 0 : v > 1 ? 1 : v);

// Window that is 1 in the middle and 0 on every edge.
const edgeWindow = (x, y) => Math.sin(Math.PI * x) * Math.sin(Math.PI * y);

// Radial helper in aspect-correct units around the centre.
function radial(x, y, fn) {
  const dx = (x - 0.5) * ASPECT;
  const dy = y - 0.5;
  const [nx, ny] = fn(dx, dy, Math.hypot(dx, dy));
  return [0.5 + nx / ASPECT, 0.5 + ny];
}

function scaleInside(radius, strength, metric) {
  // strength > 0 samples farther out (content shrinks), < 0 nearer (content grows)
  return (x, y) => radial(x, y, (dx, dy, r) => {
    const d = metric === 'box' ? Math.max(Math.abs(dx), Math.abs(dy)) : r;
    if (d >= radius) return [dx, dy];
    const t = 1 - d / radius;
    const f = 1 + strength * t * t;
    return [dx * f, dy * f];
  });
}

const RAW_FUNCTIONS = {
  // gentle double wave, fading out towards the edges
  Warp(x, y) {
    const w = edgeWindow(x, y);
    return [
      x + 0.045 * Math.sin(TAU * 2 * y + 0.7) * w,
      y + 0.04 * Math.sin(TAU * 1.5 * x + 0.3) * w,
    ];
  },
  // the middle of the picture gets smaller
  Shrink: scaleInside(0.36, 0.65, 'round'),
  // the horizontal middle band is stretched vertically
  Stretch(x, y) {
    return [x, y + 0.07 * Math.sin(TAU * y)];
  },
  // six-fold mirror around the centre of the square feed
  Kaleidoscope(x, y) {
    const dx = (x - 0.5) * ASPECT;
    const dy = y - 0.5;
    const r = Math.hypot(dx, dy) * 0.9;
    const sector = Math.PI / 3;
    let a = Math.atan2(dy, dx);
    a = ((a % sector) + sector) % sector;
    if (a > sector / 2) a = sector - a;
    let rr = r % 1;
    if (rr > 0.5) rr = 1 - rr; // reflect at the edge of the feed
    return [0.5 + rr * Math.cos(a + Math.PI / 6), 0.5 + rr * Math.sin(a + Math.PI / 6)];
  },
  // the middle of the picture swells
  Bulge: scaleInside(0.4, -0.55, 'round'),
  // swirl around the centre
  Twist(x, y) {
    return radial(x, y, (dx, dy, r) => {
      const R = 0.37;
      if (r >= R) return [dx, dy];
      const t = 1 - r / R;
      const a = 2.4 * t * t;
      const c = Math.cos(a), s = Math.sin(a);
      return [dx * c - dy * s, dx * s + dy * c];
    });
  },
  // a squared-off swelling
  Square: scaleInside(0.34, -0.5, 'box'),
  // a grid of glass blocks, 3 x 4
  Grid(x, y) {
    const a = 0.6;
    return [
      x - (a * Math.sin(TAU * 3 * x)) / (TAU * 3),
      y - (a * Math.sin(TAU * 4 * y)) / (TAU * 4),
    ];
  },
  // narrow waist, wide top and bottom
  Hourglass(x, y) {
    const k = 0.35 * Math.cos(TAU * (y - 0.5)) * Math.sqrt(Math.sin(Math.PI * y));
    return [x + k * (x - 0.5) * Math.sin(Math.PI * x), y];
  },
};

// (x, y) output position, Y up -> [sx, sy] feed position, both clamped to 0..1
export const MAP_FUNCTIONS = Object.freeze(Object.fromEntries(Object.entries(RAW_FUNCTIONS).map(
  ([name, fn]) => [name, (x, y) => { const [sx, sy] = fn(x, y); return [clamp01(sx), clamp01(sy)]; }],
)));

export function encodeCoord(v) {
  const n = Math.round(clamp01(v) * 510);
  return [Math.ceil(n / 2), Math.floor(n / 2)];
}

export function decodeTexel(t) {
  return [(t[0] + t[1]) / 510, (t[2] + t[3]) / 510];
}

export function proceduralMap(name, { width = 192, height = 256 } = {}) {
  const fn = MAP_FUNCTIONS[name];
  if (!fn) throw new Error(`unknown effect map ${name}`);
  const data = new Uint8Array(width * height * 4);
  for (let row = 0; row < height; row++) {
    const y = height > 1 ? 1 - row / (height - 1) : 0.5;
    for (let col = 0; col < width; col++) {
      const x = width > 1 ? col / (width - 1) : 0.5;
      const [sx, sy] = fn(x, y);
      const o = (row * width + col) * 4;
      const [r, g] = encodeCoord(sx);
      const [b, a] = encodeCoord(sy);
      data[o] = r; data[o + 1] = g; data[o + 2] = b; data[o + 3] = a;
    }
  }
  return { width, height, data, source: 'procedural' };
}

const procCache = new Map();
function cachedProcedural(name) {
  if (!procCache.has(name)) procCache.set(name, proceduralMap(name));
  return procCache.get(name);
}

// -> Promise<{ width, height, data, source: 'art' | 'procedural' }>
export async function loadEffectMap(name, {
  assets = null,
  fetch: fetchFn = globalThis.fetch,
  log = null,
  hasInflate = typeof DecompressionStream === 'function',
} = {}) {
  if (!MAP_EFFECTS.includes(name)) throw new Error(`unknown effect map ${name}`);
  let url = null;
  try {
    url = assets && typeof assets.effectMap === 'function' ? assets.effectMap(name) : null;
  } catch {
    url = null;
  }
  if (url && hasInflate && typeof fetchFn === 'function') {
    try {
      const res = await fetchFn(url);
      if (!res.ok) throw new Error(`http ${res.status}`);
      const img = await decodePng(new Uint8Array(await res.arrayBuffer()));
      return { ...img, source: 'art' };
    } catch (err) {
      log?.warn?.(`effect map ${name}: ${err && err.message}; using procedural map`);
    }
  }
  return cachedProcedural(name);
}
