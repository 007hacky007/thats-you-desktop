import test from 'node:test';
import assert from 'node:assert/strict';
import {
  COLOURS, colourCss, colourBit, ALL_COLOURS, TOOLS, SIZE_FACTORS, diameter, BRUSH_EDGE, SPRAY,
  stampSpacing, createStamper, stampsAlong, coloursUsedMask, replay, coverFit, OUTPUT, SCALE,
} from '../../js/draw/tools.js';

test('the 14 colours of eDrawingColour', () => {
  assert.equal(COLOURS.length, 14);
  assert.deepEqual(COLOURS.map((c) => c.id), [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14]);
  const byName = Object.fromEntries(COLOURS.map((c) => [c.name, c.rgb]));
  assert.deepEqual(byName.BLACK, [0, 0, 0]);
  assert.deepEqual(byName.RED, [243, 60, 43]);
  assert.deepEqual(byName.YELLOW, [255, 235, 4]);
  assert.deepEqual(byName.BLUE, [32, 63, 230]);
  assert.deepEqual(byName.PINK, [220, 164, 142]);
  assert.deepEqual(byName.BROWN, [114, 75, 43]);
  assert.deepEqual(byName.GREY, [143, 144, 146]);
  assert.deepEqual(byName.DARK_PINK, [183, 100, 167]);
  assert.deepEqual(byName.GREEN, [0, 164, 81]);
  assert.deepEqual(byName.PURPLE, [103, 45, 135]);
  assert.deepEqual(byName.ORANGE, [245, 146, 27]);
  assert.deepEqual(byName.WHITE, [255, 255, 255]);
  assert.deepEqual(byName.KHAKI, [162, 119, 75]);
  assert.deepEqual(byName.TURQUOISE, [20, 218, 255]);
  assert.equal(colourCss(2), 'rgb(243, 60, 43)');
  assert.throws(() => colourCss(0));
  assert.throws(() => colourCss(15));
});

test('colour masks: bit (1 << id)', () => {
  assert.equal(colourBit(1), 2);
  assert.equal(colourBit(14), 16384);
  assert.equal(ALL_COLOURS, 0x7ffe);
});

test('sizes: factor times 64 output pixels', () => {
  assert.deepEqual(TOOLS, ['brush', 'eraser', 'spray']);
  assert.deepEqual(SIZE_FACTORS.brush, [0.08, 0.25, 0.55]);
  assert.deepEqual(SIZE_FACTORS.eraser, [0.08, 0.25, 0.55]);
  assert.deepEqual(SIZE_FACTORS.spray, [0.21, 0.35, 0.8]);
  assert.ok(Math.abs(diameter('brush', 1) - 5.12) < 1e-9);
  assert.ok(Math.abs(diameter('eraser', 3) - 35.2) < 1e-9);
  assert.ok(Math.abs(diameter('spray', 2) - 22.4) < 1e-9);
  assert.throws(() => diameter('pan', 1));
  assert.throws(() => diameter('brush', 4));
});

test('brush edge and spray constants', () => {
  assert.deepEqual(BRUSH_EDGE, { inner: 0.85, outer: 0.9, alpha: 1 });
  assert.equal(SPRAY.alpha, 0.25);
  assert.deepEqual(OUTPUT, { width: 384, height: 512 });
  assert.equal(SCALE, 2);
});

test('stamp spacing is a quarter of the diameter', () => {
  assert.ok(Math.abs(stampSpacing('spray', 3) - 0.8 * 64 / 4) < 1e-9);
});

test('stampsAlong: first point, then every spacing along the path', () => {
  const s = stampsAlong([[0, 0], [10, 0], [10, 5]], 4);
  assert.deepEqual(s.map(([x, y]) => [Math.round(x * 1000) / 1000, Math.round(y * 1000) / 1000]),
    [[0, 0], [4, 0], [8, 0], [10, 2]]);
  assert.deepEqual(stampsAlong([[3, 3]], 4), [[3, 3]]);
  assert.deepEqual(stampsAlong([], 4), []);
});

test('createStamper gives the same stamps incrementally', () => {
  const pts = [[0, 0], [3, 0], [3, 4], [9, 4], [9, 4], [9, 12]];
  const st = createStamper(2.5);
  const inc = [];
  for (const p of pts) inc.push(...st.add(p));
  assert.deepEqual(inc, stampsAlong(pts, 2.5));
});

test('coloursUsedMask counts brush and spray strokes, not the eraser', () => {
  assert.equal(coloursUsedMask([]), 0);
  assert.equal(coloursUsedMask([
    { tool: 'brush', colour: 1 }, { tool: 'spray', colour: 14 }, { tool: 'eraser', colour: 5 },
    { tool: 'brush', colour: 1 },
  ]), (1 << 1) | (1 << 14));
});

test('replay clears and paints every stroke in order', () => {
  const calls = [];
  const painter = { clear: () => calls.push('clear'), paint: (s) => calls.push(s.id) };
  replay([{ id: 'a' }, { id: 'b' }], painter);
  assert.deepEqual(calls, ['clear', 'a', 'b']);
});

test('coverFit fills the target and centres the overflow', () => {
  assert.deepEqual(coverFit(480, 640, 384, 512), { dx: 0, dy: 0, dw: 384, dh: 512 });
  assert.deepEqual(coverFit(1000, 500, 100, 100), { dx: -50, dy: 0, dw: 200, dh: 100 });
  assert.deepEqual(coverFit(500, 1000, 100, 100), { dx: 0, dy: -50, dw: 100, dh: 200 });
  assert.deepEqual(coverFit(0, 0, 100, 100), { dx: 0, dy: 0, dw: 100, dh: 100 });
});
