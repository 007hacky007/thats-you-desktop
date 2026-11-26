// Drawing tools: colours, sizes, masks and stroke helpers. Pure, Node-safe.
// Stroke points are in output pixels (384x512); the surface renders at 2x.

export const OUTPUT = Object.freeze({ width: 384, height: 512 });
export const SCALE = 2;

const COLOUR_TABLE = [
  [1, 'BLACK', 0, 0, 0],
  [2, 'RED', 243, 60, 43],
  [3, 'YELLOW', 255, 235, 4],
  [4, 'BLUE', 32, 63, 230],
  [5, 'PINK', 220, 164, 142],
  [6, 'BROWN', 114, 75, 43],
  [7, 'GREY', 143, 144, 146],
  [8, 'DARK_PINK', 183, 100, 167],
  [9, 'GREEN', 0, 164, 81],
  [10, 'PURPLE', 103, 45, 135],
  [11, 'ORANGE', 245, 146, 27],
  [12, 'WHITE', 255, 255, 255],
  [13, 'KHAKI', 162, 119, 75],
  [14, 'TURQUOISE', 20, 218, 255],
];

export const COLOURS = Object.freeze(COLOUR_TABLE.map(([id, name, r, g, b]) => Object.freeze({
  id, name, rgb: Object.freeze([r, g, b]), css: `rgb(${r}, ${g}, ${b})`,
})));

function colour(id) {
  const c = COLOURS[id - 1];
  if (!c || c.id !== id) throw new Error(`unknown colour ${id}`);
  return c;
}

export const colourCss = (id) => colour(id).css;
export const colourRgb = (id) => colour(id).rgb;
export const colourBit = (id) => 1 << id;
export const ALL_COLOURS = COLOURS.reduce((m, c) => m | colourBit(c.id), 0);

export const TOOLS = Object.freeze(['brush', 'eraser', 'spray']);
export const SIZE_FACTORS = Object.freeze({
  brush: Object.freeze([0.08, 0.25, 0.55]),
  eraser: Object.freeze([0.08, 0.25, 0.55]),
  spray: Object.freeze([0.21, 0.35, 0.8]),
});
export const SIZE_UNIT = 64;

// brush and eraser: hard core to 0.85 of the radius, soft edge to 0.9
export const BRUSH_EDGE = Object.freeze({ inner: 0.85, outer: 0.9, alpha: 1 });
// spray: radial falloff from the centre, alpha per stamp
export const SPRAY = Object.freeze({ inner: 0, outer: 1, alpha: 0.25 });

export function diameter(tool, size) {
  const f = SIZE_FACTORS[tool];
  if (!f) throw new Error(`unknown tool ${tool}`);
  const v = f[size - 1];
  if (v === undefined) throw new Error(`unknown size ${size}`);
  return v * SIZE_UNIT;
}

export const stampSpacing = (tool, size) => diameter(tool, size) / 4;

// Incremental stamp placement along a polyline: the first point gets a
// stamp, then one stamp every `spacing` of path length.
export function createStamper(spacing) {
  let last = null;
  let carry = 0; // path length since the last stamp
  return {
    add(p) {
      const out = [];
      if (!last) {
        last = p;
        out.push([p[0], p[1]]);
        return out;
      }
      const dx = p[0] - last[0];
      const dy = p[1] - last[1];
      const len = Math.hypot(dx, dy);
      if (len === 0) return out;
      let d = spacing - carry;
      while (d <= len + 1e-9) {
        const t = d / len;
        out.push([last[0] + dx * t, last[1] + dy * t]);
        d += spacing;
      }
      carry = len - (d - spacing);
      last = p;
      return out;
    },
  };
}

export function stampsAlong(points, spacing) {
  const st = createStamper(spacing);
  const out = [];
  for (const p of points) out.push(...st.add(p));
  return out;
}

export function coloursUsedMask(strokes) {
  let m = 0;
  for (const s of strokes) if (s.tool === 'brush' || s.tool === 'spray') m |= colourBit(s.colour);
  return m;
}

// Undo is "replay everything except the last stroke".
export function replay(strokes, painter) {
  painter.clear();
  for (const s of strokes) painter.paint(s);
}

export function coverFit(srcW, srcH, dstW, dstH) {
  if (!(srcW > 0 && srcH > 0)) return { dx: 0, dy: 0, dw: dstW, dh: dstH };
  const k = Math.max(dstW / srcW, dstH / srcH);
  const dw = srcW * k;
  const dh = srcH * k;
  return { dx: (dstW - dw) / 2, dy: (dstH - dh) / 2, dw, dh };
}
