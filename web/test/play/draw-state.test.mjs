import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  COLOURS, TOOLS, SIZES, initialDrawState, drawReducer, toolbarModel, applyToSurface, sizeDiameter, colourCss,
} from '../../js/ui/components/draw-state.js';

test('palette has the 14 game colours with ids 1..14', () => {
  assert.equal(COLOURS.length, 14);
  assert.deepEqual(COLOURS.map((c) => c.id), [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14]);
  assert.deepEqual(COLOURS[0], { id: 1, name: 'BLACK', rgb: [0, 0, 0] });
  assert.deepEqual(COLOURS[1].rgb, [243, 60, 43]);
  assert.deepEqual(COLOURS[13], { id: 14, name: 'TURQUOISE', rgb: [20, 218, 255] });
  assert.equal(colourCss(2), 'rgb(243, 60, 43)');
  assert.equal(colourCss(99), 'rgb(0, 0, 0)');
});

test('tools and sizes', () => {
  assert.deepEqual(TOOLS, ['brush', 'eraser', 'spray']);
  assert.deepEqual(SIZES, [1, 2, 3]);
  assert.equal(sizeDiameter('brush', 1), 0.08 * 64);
  assert.equal(sizeDiameter('eraser', 3), 0.55 * 64);
  assert.equal(sizeDiameter('spray', 2), 0.35 * 64);
  assert.equal(sizeDiameter('spray', 9), 0.35 * 64, 'bad size falls back to medium');
});

test('initial state', () => {
  assert.deepEqual(initialDrawState(), { tool: 'brush', colour: 1, size: 2, canUndo: false, isEmpty: true, busy: false });
});

test('reducer validates tool, colour and size', () => {
  let s = initialDrawState();
  s = drawReducer(s, { type: 'tool', tool: 'spray' });
  assert.equal(s.tool, 'spray');
  assert.equal(drawReducer(s, { type: 'tool', tool: 'pan' }), s);
  s = drawReducer(s, { type: 'colour', colour: 14 });
  assert.equal(s.colour, 14);
  assert.equal(drawReducer(s, { type: 'colour', colour: 0 }), s);
  assert.equal(drawReducer(s, { type: 'colour', colour: 15 }), s);
  assert.equal(drawReducer(s, { type: 'colour', colour: 2.5 }), s);
  s = drawReducer(s, { type: 'size', size: 3 });
  assert.equal(s.size, 3);
  assert.equal(drawReducer(s, { type: 'size', size: 4 }), s);
  assert.equal(drawReducer(s, { type: 'bogus' }), s);
});

test('picking a colour while erasing switches back to the brush', () => {
  let s = drawReducer(initialDrawState(), { type: 'tool', tool: 'eraser' });
  s = drawReducer(s, { type: 'colour', colour: 5 });
  assert.equal(s.tool, 'brush');
  assert.equal(s.colour, 5);
  let t = drawReducer(initialDrawState(), { type: 'tool', tool: 'spray' });
  t = drawReducer(t, { type: 'colour', colour: 5 });
  assert.equal(t.tool, 'spray', 'spray keeps its tool');
});

test('surface state and busy flag', () => {
  let s = drawReducer(initialDrawState(), { type: 'surface', canUndo: true, isEmpty: false });
  assert.equal(s.canUndo, true);
  assert.equal(s.isEmpty, false);
  s = drawReducer(s, { type: 'busy', busy: true });
  assert.equal(s.busy, true);
});

test('toolbar model marks selections, testids and undo', () => {
  let s = drawReducer(initialDrawState(), { type: 'colour', colour: 3 });
  s = drawReducer(s, { type: 'surface', canUndo: true, isEmpty: false });
  const m = toolbarModel(s);
  assert.equal(m.colours.length, 14);
  assert.equal(m.colours[2].testid, 'colour-3');
  assert.equal(m.colours[2].selected, true);
  assert.equal(m.colours[0].selected, false);
  assert.deepEqual(m.tools.map((t) => t.testid), ['tool-brush', 'tool-eraser', 'tool-spray']);
  assert.deepEqual(m.tools.map((t) => t.selected), [true, false, false]);
  assert.deepEqual(m.sizes.map((t) => t.testid), ['size-1', 'size-2', 'size-3']);
  assert.deepEqual(m.sizes.map((t) => t.selected), [false, true, false]);
  assert.equal(m.undoEnabled, true);
  assert.equal(m.confirmEnabled, true);
  assert.equal(m.coloursEnabled, true);
  const busy = toolbarModel(drawReducer(s, { type: 'busy', busy: true }));
  assert.equal(busy.undoEnabled, false);
  assert.equal(busy.confirmEnabled, false);
  const eraser = toolbarModel(drawReducer(s, { type: 'tool', tool: 'eraser' }));
  assert.equal(eraser.colours.some((c) => c.selected), false, 'no colour highlighted while erasing');
});

test('applyToSurface copies tool, colour and size', () => {
  const surface = {};
  applyToSurface({ tool: 'spray', colour: 7, size: 1 }, surface);
  assert.deepEqual(surface, { tool: 'spray', colour: 7, size: 1 });
  assert.doesNotThrow(() => applyToSurface(initialDrawState(), null));
});
