import test from 'node:test';
import assert from 'node:assert/strict';
import { EFFECTS, effectById, createRenderer } from '../../js/camera/effects.js';
import { FRAGMENTS, VERTEX, programKey, v2mirrorX } from '../../js/camera/shaders.js';
import { MAP_EFFECTS } from '../../js/camera/maps.js';

test('12 effects with ids, keys, kinds and parameters', () => {
  assert.deepEqual(EFFECTS.map((e) => e.name), [
    'None', 'Mirror', 'Warp', 'Shrink', 'Stretch', 'Kaleidoscope', 'Bulge', 'Twist', 'V2Mirror', 'Square', 'Grid', 'Hourglass',
  ]);
  EFFECTS.forEach((e, i) => {
    assert.equal(e.id, i);
    assert.equal(e.key, `CameraEffectName_${e.name}`);
    assert.ok(['none', 'map', 'mirror', 'v2mirror'].includes(e.kind));
    assert.ok(['portrait', 'square'].includes(e.feed));
  });
  const p = (n) => EFFECTS.find((e) => e.name === n);
  assert.deepEqual([p('Mirror').kind, p('Mirror').smoothing], ['mirror', 0.015]);
  assert.deepEqual([p('Warp').smoothing, p('Warp').taps], [0.1, 2]);
  assert.deepEqual([p('Shrink').smoothing, p('Shrink').taps], [0.15, 4]);
  assert.deepEqual([p('Stretch').smoothing, p('Stretch').taps], [0.15, 4]);
  assert.deepEqual([p('Kaleidoscope').smoothing, p('Kaleidoscope').taps, p('Kaleidoscope').feed], [0.2, 4, 'square']);
  assert.deepEqual([p('Bulge').smoothing, p('Bulge').taps], [0.1, 2]);
  assert.deepEqual([p('Twist').smoothing, p('Twist').taps], [0.15, 4]);
  assert.deepEqual([p('Square').smoothing, p('Square').taps], [0.1, 2]);
  assert.deepEqual([p('Grid').smoothing, p('Grid').taps], [0.15, 4]);
  assert.deepEqual([p('Hourglass').smoothing, p('Hourglass').taps], [0.15, 4]);
  assert.deepEqual([...p('V2Mirror').lines], [0.25, 0.75]);
  assert.deepEqual(EFFECTS.filter((e) => e.kind === 'map').map((e) => e.map).sort(), [...MAP_EFFECTS].sort());
  assert.equal(effectById(5).name, 'Kaleidoscope');
  assert.equal(effectById(12), null);
  assert.ok(Object.isFrozen(EFFECTS) && Object.isFrozen(EFFECTS[0]));
});

test('program per effect kind', () => {
  assert.equal(programKey(EFFECTS[0]), 'none');
  assert.equal(programKey(EFFECTS[1]), 'mirror');
  assert.equal(programKey(EFFECTS[2]), 'map2');
  assert.equal(programKey(EFFECTS[3]), 'map4');
  assert.equal(programKey(EFFECTS[8]), 'v2mirror');
  for (const e of EFFECTS) assert.ok(FRAGMENTS[programKey(e)]);
});

test('shader sources: GLSL ES 1.0, uniforms present, ASCII only', () => {
  assert.match(VERTEX, /attribute vec2 a_pos/);
  for (const [k, src] of Object.entries(FRAGMENTS)) {
    assert.match(src, /precision (highp|mediump) float/, k);
    assert.match(src, /uniform vec4 u_crop/, k);
    assert.match(src, /gl_FragColor/, k);
    assert.ok(/^[\x09\x0a\x20-\x7e]*$/.test(src), `${k} has non-ASCII`);
  }
  assert.match(FRAGMENTS.map2, /u_smooth/);
  assert.equal((FRAGMENTS.map2.match(/lookup\(v_uv [+-]/g) || []).length, 2);
  assert.equal((FRAGMENTS.map4.match(/lookup\(v_uv [+-]/g) || []).length, 4);
  assert.match(FRAGMENTS.mirror, /u_band/);
  assert.match(FRAGMENTS.v2mirror, /u_lines/);
});

test('V2Mirror fold keeps the centre band and reflects it outwards', () => {
  const f = (x) => v2mirrorX(x, 0.25, 0.75);
  for (const x of [0.25, 0.4, 0.5, 0.75]) assert.ok(Math.abs(f(x) - x) < 1e-12);
  assert.ok(Math.abs(f(0) - 0.5) < 1e-12);
  assert.ok(Math.abs(f(0.1) - 0.4) < 1e-12);
  assert.ok(Math.abs(f(1) - 0.5) < 1e-12);
  assert.ok(Math.abs(f(0.9) - 0.6) < 1e-12);
});

// --- 2D fallback with a fake canvas ---------------------------------------
function fakeCanvasDoc() {
  const ops = [];
  const ctx = new Proxy({}, {
    get(t, k) { return k in t ? t[k] : (...a) => { ops.push([k, ...a]); }; },
    set(t, k, v) { t[k] = v; return true; },
  });
  const document = {
    createElement() {
      return {
        width: 0, height: 0,
        getContext(type) { return type === '2d' ? ctx : null; },
        toBlob(cb, type, q) { cb(new Blob([new Uint8Array(q === 0.75 ? 1000 : 10)])); },
        addEventListener() {},
      };
    },
  };
  return { document, ops };
}

test('2D fallback: no WebGL, only None, mirrored drawImage for the front camera', async () => {
  const { document, ops } = fakeCanvasDoc();
  const r = createRenderer({ document, webgl: true, raf: null });
  assert.equal(r.webgl, false);
  assert.equal(r.canvas.width, 480);
  assert.equal(r.canvas.height, 640);
  assert.deepEqual(r.effects.map((e) => e.id), [0]);
  assert.equal(r.setEffect(3), 0);
  assert.equal(r.effect, 0);

  await assert.rejects(r.capture(), /no camera frame/);
  const video = { readyState: 4, videoWidth: 640, videoHeight: 480 };
  r.attach({ active: true, facing: 'user', video });
  const shot = await r.capture();
  assert.equal(shot.usesFX, false);
  assert.equal(shot.quality, 0.75);
  assert.equal(shot.jpeg.length, 1000);
  assert.ok(ops.some((o) => o[0] === 'scale' && o[1] === -1));
  const di = ops.find((o) => o[0] === 'drawImage');
  assert.deepEqual(di.slice(2), [140, 0, 360, 480, 0, 0, 480, 640]);
  r.destroy();
});

test('rear camera is not mirrored', () => {
  const { document, ops } = fakeCanvasDoc();
  const r = createRenderer({ document, raf: null });
  r.attach({ active: true, facing: 'environment', video: { readyState: 4, videoWidth: 720, videoHeight: 1280 } });
  assert.equal(r.render(), true);
  assert.ok(!ops.some((o) => o[0] === 'scale'));
  assert.deepEqual(ops.find((o) => o[0] === 'drawImage').slice(2), [0, 160, 720, 960, 0, 0, 480, 640]);
});
