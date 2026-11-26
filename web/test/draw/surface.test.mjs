import test from 'node:test';
import assert from 'node:assert/strict';
import { createDrawingSurface } from '../../js/draw/surface.js';
import { ALL_COLOURS } from '../../js/draw/tools.js';

function fakeDoc() {
  const canvases = [];
  const doc = {
    canvases,
    createElement(tag) {
      const ops = [];
      const ctx = new Proxy({ ops }, {
        get(t, k) {
          if (k in t) return t[k];
          if (k === 'createRadialGradient') return () => ({ addColorStop() {} });
          return (...a) => { ops.push([k, ...a]); };
        },
        set(t, k, v) { ops.push(['set', k, v]); t[k] = v; return true; },
      });
      const listeners = {};
      const el = {
        tag, width: 0, height: 0, style: {}, attrs: {}, ops, listeners,
        setAttribute(k, v) { this.attrs[k] = v; },
        getContext: () => ctx,
        addEventListener(ev, fn) { listeners[ev] = fn; },
        removeEventListener(ev) { delete listeners[ev]; },
        getBoundingClientRect: () => ({ left: 10, top: 20, width: 192, height: 256 }),
        setPointerCapture() {},
        fire(ev, e) { listeners[ev]?.(e); },
      };
      if (tag === 'canvas') canvases.push(el);
      return el;
    },
  };
  return doc;
}

function setup(extra = {}) {
  const document = fakeDoc();
  const encoded = [];
  const s = createDrawingSurface({
    document, raf: null,
    encode: async (canvas) => { encoded.push(canvas); return { jpeg: new Uint8Array([0xff, 0xd8]), quality: 0.75 }; },
    ...extra,
  });
  return { s, document, encoded };
}

const pd = (x, y, id = 1) => ({ pointerId: id, button: 0, clientX: x, clientY: y, preventDefault() {} });

test('element is a 2x canvas with touch-action none and the test id', () => {
  const { s } = setup();
  assert.equal(s.element.width, 768);
  assert.equal(s.element.height, 1024);
  assert.equal(s.element.style.touchAction, 'none');
  assert.equal(s.element.attrs['data-testid'], 'draw-canvas');
});

test('defaults and validated setters', () => {
  const { s } = setup();
  assert.equal(s.tool, 'brush'); assert.equal(s.colour, 1); assert.equal(s.size, 2);
  s.tool = 'spray'; s.colour = 14; s.size = 3;
  s.tool = 'pan'; s.colour = 15; s.size = 0;
  assert.deepEqual([s.tool, s.colour, s.size], ['spray', 14, 3]);
  assert.equal(s.isEmpty, true); assert.equal(s.canUndo, false);
  assert.equal(s.coloursAvailable, ALL_COLOURS);
  assert.equal(s.coloursUsed, 0);
});

test('pointer stroke maps client coordinates to output pixels', () => {
  const { s } = setup();
  let changes = 0;
  s.on('change', () => changes++);
  s.colour = 2;
  s.element.fire('pointerdown', pd(10, 20));
  s.element.fire('pointermove', pd(106, 148));
  s.element.fire('pointerup', pd(202, 276));
  assert.equal(changes, 1);
  assert.equal(s.strokeCount, 1);
  assert.equal(s.isEmpty, false);
  assert.equal(s.coloursUsed, 1 << 2);
});

test('pointer points are painted at 2x on the stroke layer', () => {
  const { s, document } = setup();
  s.element.fire('pointerdown', pd(10, 20));
  s.element.fire('pointerup', pd(202, 276));
  const ops = document.canvases[1].ops;
  assert.ok(ops.some((o) => o[0] === 'moveTo' && o[1] === 0 && o[2] === 0));
  assert.ok(ops.some((o) => o[0] === 'lineTo' && o[1] === 768 && o[2] === 1024));
});

test('second finger is ignored while drawing', () => {
  const { s } = setup();
  s.element.fire('pointerdown', pd(10, 20, 1));
  s.element.fire('pointerdown', pd(50, 50, 2));
  s.element.fire('pointermove', pd(60, 60, 2));
  s.element.fire('pointerup', pd(60, 60, 2));
  assert.equal(s.strokeCount, 0);
  s.element.fire('pointerup', pd(30, 30, 1));
  assert.equal(s.strokeCount, 1);
});

test('eraser strokes do not count as colours or content; undo replays', () => {
  const { s } = setup();
  const offs = [];
  let changes = 0;
  offs.push(s.on('change', () => changes++));
  s.addStroke({ tool: 'brush', colour: 3, size: 1, points: [[10, 10], [50, 50]] });
  s.addStroke({ tool: 'spray', colour: 9, size: 2, points: [[100, 100], [140, 100]] });
  s.addStroke({ tool: 'eraser', colour: 4, size: 3, points: [[20, 20]] });
  assert.equal(s.coloursUsed, (1 << 3) | (1 << 9));
  s.undo();
  assert.equal(s.strokeCount, 2);
  s.undo();
  assert.equal(s.coloursUsed, 1 << 3);
  s.undo();
  assert.equal(s.isEmpty, true);
  assert.equal(s.canUndo, false);
  s.undo();
  assert.equal(changes, 6);
  offs[0]();
  s.addStroke({ tool: 'brush', colour: 1, points: [[1, 1]] });
  assert.equal(changes, 6);
});

test('eraser uses destination-out on the stroke layer', () => {
  const { s, document } = setup();
  s.addStroke({ tool: 'eraser', size: 2, points: [[10, 10], [20, 20]] });
  const committed = document.canvases[1];
  const sets = committed.ops.filter((o) => o[0] === 'set' && o[1] === 'globalCompositeOperation').map((o) => o[2]);
  assert.ok(sets.includes('destination-out'));
  // the visible canvas never uses destination-out (background stays)
  const viewSets = s.element.ops.filter((o) => o[0] === 'set' && o[1] === 'globalCompositeOperation').map((o) => o[2]);
  assert.ok(!viewSets.includes('destination-out'));
});

test('brush paints a soft rim then the core with round caps at 2x', () => {
  const { s, document } = setup();
  s.addStroke({ tool: 'brush', colour: 2, size: 3, points: [[0, 0], [384, 512]] });
  const ops = document.canvases[1].ops;
  const widths = ops.filter((o) => o[0] === 'set' && o[1] === 'lineWidth').map((o) => o[2]);
  const d = 0.55 * 64 * 2;
  assert.deepEqual(widths.map((w) => Math.round(w * 1000) / 1000), [d * 0.9, d * 0.85].map((w) => Math.round(w * 1000) / 1000));
  assert.ok(ops.some((o) => o[0] === 'set' && o[1] === 'lineCap' && o[2] === 'round'));
  assert.ok(ops.some((o) => o[0] === 'lineTo' && o[1] === 768 && o[2] === 1024));
});

test('spray stamps every quarter diameter', () => {
  const { s, document } = setup();
  s.addStroke({ tool: 'spray', colour: 5, size: 1, points: [[0, 100], [0 + 0.21 * 64, 100]] });
  const draws = document.canvases[1].ops.filter((o) => o[0] === 'drawImage');
  assert.equal(draws.length, 5);
});

test('export composites background and strokes into 384x512', async () => {
  const { s, document, encoded } = setup();
  s.addStroke({ tool: 'brush', colour: 12, points: [[5, 5], [6, 6]] });
  const r = await s.export();
  assert.equal(encoded.length, 1);
  assert.equal(encoded[0].width, 384);
  assert.equal(encoded[0].height, 512);
  assert.deepEqual([...r.jpeg], [0xff, 0xd8]);
  assert.equal(r.coloursAvailable, ALL_COLOURS);
  assert.equal(r.coloursUsed, 1 << 12);
  const ops = encoded[0].ops;
  assert.ok(ops.some((o) => o[0] === 'fillRect' && o[3] === 384 && o[4] === 512));
  assert.ok(ops.some((o) => o[0] === 'drawImage' && o[1] === document.canvases[1] && o[4] === 384 && o[5] === 512));
});

test('background is cover-fitted; failures fall back to white', async () => {
  const img = { naturalWidth: 480, naturalHeight: 640 };
  const { s } = setup({
    loadImage: async (url) => { if (url === 'bad') throw new Error('nope'); return img; },
  });
  assert.equal(await s.setBackground('ok'), true);
  assert.ok(s.element.ops.some((o) => o[0] === 'drawImage' && o[1] === img && o[4] === 768 && o[5] === 1024));
  const n = s.element.ops.length;
  assert.equal(await s.setBackground('bad'), false);
  assert.ok(!s.element.ops.slice(n).some((o) => o[0] === 'drawImage' && o[1] === img));
  assert.equal(await s.setBackground(null), true);
});

test('a slower earlier background does not override a later one', async () => {
  let release;
  const slow = new Promise((r) => { release = r; });
  const a = { naturalWidth: 1, naturalHeight: 1, name: 'a' };
  const b = { naturalWidth: 1, naturalHeight: 1, name: 'b' };
  const { s } = setup({ loadImage: (url) => (url === 'a' ? slow.then(() => a) : Promise.resolve(b)) });
  const pa = s.setBackground('a');
  assert.equal(await s.setBackground('b'), true);
  release();
  assert.equal(await pa, false);
  const last = s.element.ops.filter((o) => o[0] === 'drawImage' && (o[1] === a || o[1] === b)).pop();
  assert.equal(last[1], b);
});

test('destroy removes pointer listeners', () => {
  const { s } = setup();
  s.destroy();
  assert.deepEqual(Object.keys(s.element.listeners), []);
});
