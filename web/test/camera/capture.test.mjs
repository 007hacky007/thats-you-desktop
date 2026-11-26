import test from 'node:test';
import assert from 'node:assert/strict';
import {
  JPEG_RULE, encodeJpegRule, canvasJpegEncoder, placeholderStyle, drawPlaceholder,
} from '../../js/camera/capture.js';

function fakeEncoder(sizes) {
  const calls = [];
  const fn = async (q) => {
    calls.push(q);
    const n = sizes[q];
    if (n === undefined) throw new Error('unexpected quality ' + q);
    return new Uint8Array(n);
  };
  fn.calls = calls;
  return fn;
}

test('constants of the JPEG rule', () => {
  assert.equal(JPEG_RULE.quality, 0.75);
  assert.equal(JPEG_RULE.reencodeAt, 63489);
  assert.equal(JPEG_RULE.lowQuality, 0.2);
  assert.equal(JPEG_RULE.cap, 128000);
  assert.equal(JPEG_RULE.lastQuality, 0.1);
});

test('small first encode is kept at 0.75', async () => {
  const enc = fakeEncoder({ 0.75: 63488 });
  const r = await encodeJpegRule(enc);
  assert.equal(r.quality, 0.75);
  assert.equal(r.jpeg.length, 63488);
  assert.deepEqual(enc.calls, [0.75]);
});

test('63489 bytes or more re-encodes at 0.20', async () => {
  const enc = fakeEncoder({ 0.75: 63489, 0.2: 128000 });
  const r = await encodeJpegRule(enc);
  assert.equal(r.quality, 0.2);
  assert.equal(r.jpeg.length, 128000);
  assert.deepEqual(enc.calls, [0.75, 0.2]);
});

test('still over 128000 at 0.20 re-encodes at 0.10', async () => {
  const enc = fakeEncoder({ 0.75: 300000, 0.2: 128001, 0.1: 90000 });
  const r = await encodeJpegRule(enc);
  assert.equal(r.quality, 0.1);
  assert.deepEqual(enc.calls, [0.75, 0.2, 0.1]);
});

test('too big even at 0.10 is an error', async () => {
  const enc = fakeEncoder({ 0.75: 300000, 0.2: 200000, 0.1: 128001 });
  await assert.rejects(encodeJpegRule(enc), /too big/);
});

test('empty encode result is an error', async () => {
  await assert.rejects(encodeJpegRule(async () => new Uint8Array(0)), /empty/);
});

test('canvasJpegEncoder uses toBlob with image/jpeg and the quality', async () => {
  const seen = [];
  const canvas = {
    toBlob(cb, type, q) {
      seen.push([type, q]);
      cb(new Blob([new Uint8Array([0xff, 0xd8, 0xff, 0xd9])], { type }));
    },
  };
  const bytes = await canvasJpegEncoder(canvas)(0.2);
  assert.deepEqual(seen, [['image/jpeg', 0.2]]);
  assert.deepEqual([...bytes], [0xff, 0xd8, 0xff, 0xd9]);
});

test('canvasJpegEncoder prefers convertToBlob (OffscreenCanvas)', async () => {
  const canvas = {
    async convertToBlob(opts) {
      assert.deepEqual(opts, { type: 'image/jpeg', quality: 0.75 });
      return new Blob([new Uint8Array([1, 2])]);
    },
  };
  assert.deepEqual([...await canvasJpegEncoder(canvas)(0.75)], [1, 2]);
});

test('canvasJpegEncoder rejects a null blob', async () => {
  const canvas = { toBlob(cb) { cb(null); } };
  await assert.rejects(canvasJpegEncoder(canvas)(0.75), /encode/);
});

test('placeholderStyle: initial and a stable colour', () => {
  const a = placeholderStyle('anna');
  assert.equal(a.initial, 'A');
  assert.deepEqual(placeholderStyle('anna'), a);
  assert.match(a.background, /^rgb\(\d+, \d+, \d+\)$/);
  assert.equal(placeholderStyle('  bob').initial, 'B');
  assert.equal(placeholderStyle('').initial, '?');
  assert.equal(placeholderStyle(null).initial, '?');
  // astral code point stays whole
  assert.equal(placeholderStyle('\u{1F600}x').initial, '\u{1F600}');
  assert.equal(placeholderStyle('émile').initial, 'É');
});

test('placeholderStyle: different names spread over colours', () => {
  const set = new Set(['a', 'b', 'c', 'd', 'e', 'f', 'g', 'h'].map((n) => placeholderStyle(n).background));
  assert.ok(set.size >= 3);
});

test('drawPlaceholder paints background and initial', () => {
  const ops = [];
  const ctx = new Proxy({}, {
    get(t, k) { return k in t ? t[k] : (...a) => ops.push([k, ...a]); },
    set(t, k, v) { ops.push(['set', k, v]); t[k] = v; return true; },
  });
  drawPlaceholder(ctx, 'Zed', 480, 640);
  assert.ok(ops.some((o) => o[0] === 'fillRect' && o[3] === 480 && o[4] === 640));
  assert.ok(ops.some((o) => o[0] === 'fillText' && o[1] === 'Z'));
});
