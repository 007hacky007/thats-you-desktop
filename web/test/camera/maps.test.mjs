import test from 'node:test';
import assert from 'node:assert/strict';
import {
  MAP_EFFECTS, MAP_FUNCTIONS, encodeCoord, decodeTexel, proceduralMap, loadEffectMap,
} from '../../js/camera/maps.js';
import { makePng, pixels } from './png-fixture.mjs';

test('nine map effects', () => {
  assert.deepEqual(MAP_EFFECTS,
    ['Warp', 'Shrink', 'Stretch', 'Kaleidoscope', 'Bulge', 'Twist', 'Square', 'Grid', 'Hourglass']);
  for (const n of MAP_EFFECTS) assert.equal(typeof MAP_FUNCTIONS[n], 'function');
});

test('coordinate pair encoding round trips within 1/510', () => {
  for (let i = 0; i <= 1000; i++) {
    const v = i / 1000;
    const [hi, lo] = encodeCoord(v);
    assert.ok(hi >= 0 && hi <= 255 && lo >= 0 && lo <= 255);
    assert.ok(Math.abs(hi - lo) <= 1);
    assert.ok(Math.abs((hi + lo) / 510 - v) <= 1 / 1020 + 1e-12);
  }
  assert.deepEqual(encodeCoord(0), [0, 0]);
  assert.deepEqual(encodeCoord(1), [255, 255]);
  assert.deepEqual(encodeCoord(-3), [0, 0]);
  assert.deepEqual(encodeCoord(7), [255, 255]);
});

test('decodeTexel: X = (R + G) / 2, Y = (B + A) / 2', () => {
  assert.deepEqual(decodeTexel([255, 255, 0, 0]), [1, 0]);
  assert.deepEqual(decodeTexel([132, 129, 0, 255]), [261 / 510, 0.5]);
});

for (const name of MAP_EFFECTS) {
  test(`procedural ${name}: size, range, determinism`, () => {
    const a = proceduralMap(name, { width: 48, height: 64 });
    assert.equal(a.width, 48); assert.equal(a.height, 64);
    assert.equal(a.data.length, 48 * 64 * 4);
    assert.equal(a.source, 'procedural');
    const b = proceduralMap(name, { width: 48, height: 64 });
    assert.deepEqual(a.data, b.data);
    // not the identity somewhere in the middle
    let maxDev = 0;
    for (let row = 0; row < 64; row++) {
      for (let col = 0; col < 48; col++) {
        const o = (row * 48 + col) * 4;
        const [x, y] = decodeTexel(a.data.subarray(o, o + 4));
        assert.ok(x >= 0 && x <= 1 && y >= 0 && y <= 1);
        maxDev = Math.max(maxDev, Math.abs(x - col / 47), Math.abs(y - (1 - row / 63)));
      }
    }
    assert.ok(maxDev > 0.03, `${name} looks like identity (${maxDev})`);
  });
  test(`function ${name} stays in range on a dense grid`, () => {
    for (let i = 0; i <= 60; i++) {
      for (let j = 0; j <= 80; j++) {
        const [x, y] = MAP_FUNCTIONS[name](i / 60, j / 80);
        assert.ok(Number.isFinite(x) && Number.isFinite(y));
        assert.ok(x >= 0 && x <= 1 && y >= 0 && y <= 1, `${name} ${i},${j} -> ${x},${y}`);
      }
    }
  });
}

test('identity at the corners (rows top first, Y up) except Kaleidoscope', () => {
  for (const name of MAP_EFFECTS) {
    if (name === 'Kaleidoscope') continue;
    const m = proceduralMap(name, { width: 30, height: 40 });
    const at = (col, row) => decodeTexel(m.data.subarray((row * 30 + col) * 4, (row * 30 + col) * 4 + 4));
    const near = (got, want) => Math.abs(got[0] - want[0]) < 2 / 510 && Math.abs(got[1] - want[1]) < 2 / 510;
    assert.ok(near(at(0, 0), [0, 1]), `${name} top-left ${at(0, 0)}`);
    assert.ok(near(at(29, 0), [1, 1]), `${name} top-right`);
    assert.ok(near(at(0, 39), [0, 0]), `${name} bottom-left`);
    assert.ok(near(at(29, 39), [1, 0]), `${name} bottom-right`);
  }
});

test('Kaleidoscope is symmetric about both centre lines', () => {
  const f = MAP_FUNCTIONS.Kaleidoscope;
  for (const [x, y] of [[0.1, 0.2], [0.3, 0.9], [0.45, 0.6], [0.05, 0.05]]) {
    const a = f(x, y), b = f(1 - x, y), c = f(x, 1 - y);
    assert.ok(Math.abs(a[0] - b[0]) < 1e-9 && Math.abs(a[1] - b[1]) < 1e-9);
    assert.ok(Math.abs(a[0] - c[0]) < 1e-9 && Math.abs(a[1] - c[1]) < 1e-9);
  }
});

test('loadEffectMap decodes the art map when available', async () => {
  const px = pixels(4, 3, 4, 5);
  const png = makePng({ w: 4, h: 3, colorType: 6, px, filterFor: () => 4 });
  const urls = [];
  const map = await loadEffectMap('Warp', {
    assets: { effectMap: (n) => (n === 'Warp' ? 'art/fx/Warp.png' : null) },
    fetch: async (url) => { urls.push(url); return new Response(png); },
  });
  assert.deepEqual(urls, ['art/fx/Warp.png']);
  assert.equal(map.source, 'art');
  assert.equal(map.width, 4); assert.equal(map.height, 3);
  assert.deepEqual([...map.data], [...px]);
});

test('loadEffectMap falls back to procedural without art, on fetch errors and bad PNGs', async () => {
  const warnings = [];
  const log = { warn: (...a) => warnings.push(a.join(' ')), info() {}, debug() {}, error() {} };
  let m = await loadEffectMap('Bulge', { assets: { effectMap: () => null }, fetch: async () => { throw new Error('x'); } });
  assert.equal(m.source, 'procedural');
  m = await loadEffectMap('Bulge', { assets: null });
  assert.equal(m.source, 'procedural');
  m = await loadEffectMap('Bulge', {
    assets: { effectMap: () => 'art/fx/Bulge.png' }, log,
    fetch: async () => new Response('nope', { status: 404 }),
  });
  assert.equal(m.source, 'procedural');
  m = await loadEffectMap('Bulge', {
    assets: { effectMap: () => 'art/fx/Bulge.png' }, log,
    fetch: async () => new Response(new Uint8Array([1, 2, 3])),
  });
  assert.equal(m.source, 'procedural');
  assert.equal(warnings.length, 2);
});

test('loadEffectMap uses procedural maps when DecompressionStream is missing', async () => {
  let fetched = false;
  const m = await loadEffectMap('Twist', {
    assets: { effectMap: () => 'art/fx/Twist.png' },
    fetch: async () => { fetched = true; return new Response(new Uint8Array(0)); },
    hasInflate: false,
  });
  assert.equal(m.source, 'procedural');
  assert.equal(fetched, false);
});

test('loadEffectMap rejects unknown names', async () => {
  await assert.rejects(loadEffectMap('Nope', {}), /unknown effect map/);
});
