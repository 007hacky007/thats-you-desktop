import test from 'node:test';
import assert from 'node:assert/strict';
import zlib from 'node:zlib';
import { decodePng, inflateZlib } from '../../js/camera/png.js';
import { crc32, chunk, makePng, pixels } from './png-fixture.mjs';


for (const f of [0, 1, 2, 3, 4]) {
  test(`RGBA with filter ${f}`, async () => {
    const w = 7, h = 5, px = pixels(w, h, 4, f + 1);
    const img = await decodePng(makePng({ w, h, colorType: 6, px, filterFor: () => f }));
    assert.equal(img.width, w); assert.equal(img.height, h);
    assert.deepEqual([...img.data], [...px]);
  });
  test(`RGB with filter ${f}`, async () => {
    const w = 6, h = 4, px = pixels(w, h, 3, f + 11);
    const img = await decodePng(makePng({ w, h, colorType: 2, px, filterFor: () => f }));
    const want = [];
    for (let i = 0; i < w * h; i++) want.push(px[i * 3], px[i * 3 + 1], px[i * 3 + 2], 255);
    assert.deepEqual([...img.data], want);
  });
}

test('mixed filters per row, split IDAT and ancillary chunks', async () => {
  const w = 9, h = 10, px = pixels(w, h, 4, 99);
  const png = makePng({
    w, h, colorType: 6, px, filterFor: (y) => y % 5, idatSplit: 4,
    extra: [chunk('gAMA', Buffer.from([0, 0, 0xb1, 0x8f])), chunk('sRGB', Buffer.from([0]))],
  });
  const img = await decodePng(png);
  assert.deepEqual([...img.data], [...px]);
});

test('alpha survives unpremultiplied (alpha 0 keeps colour)', async () => {
  const px = new Uint8Array([200, 100, 50, 0, 10, 20, 30, 1]);
  const img = await decodePng(makePng({ w: 2, h: 1, colorType: 6, px }));
  assert.deepEqual([...img.data], [...px]);
});

test('grayscale and grayscale+alpha expand to RGBA', async () => {
  let img = await decodePng(makePng({ w: 2, h: 1, colorType: 0, px: new Uint8Array([5, 250]), filterFor: () => 1 }));
  assert.deepEqual([...img.data], [5, 5, 5, 255, 250, 250, 250, 255]);
  img = await decodePng(makePng({ w: 1, h: 2, colorType: 4, px: new Uint8Array([9, 8, 7, 6]), filterFor: () => 4 }));
  assert.deepEqual([...img.data], [9, 9, 9, 8, 7, 7, 7, 6]);
});

test('rejects non-PNG, interlaced, 16-bit and palette images', async () => {
  await assert.rejects(decodePng(new Uint8Array([1, 2, 3])), /not a png/);
  const base = makePng({ w: 1, h: 1, colorType: 6, px: new Uint8Array(4) });
  const variant = (off, val) => {
    const b = Buffer.from(base);
    b[off] = val;
    // fix the IHDR crc
    b.writeUInt32BE(crc32(b.subarray(12, 29)), 29);
    return new Uint8Array(b);
  };
  await assert.rejects(decodePng(variant(28, 1)), /interlaced/);
  await assert.rejects(decodePng(variant(24, 16)), /bit depth/);
  await assert.rejects(decodePng(variant(25, 3)), /colour type/);
});

test('rejects truncated data and bad filter bytes', async () => {
  const png = makePng({ w: 4, h: 4, colorType: 6, px: pixels(4, 4, 4) });
  await assert.rejects(decodePng(png.subarray(0, 40)));
  // build a stream whose filter byte is 9
  const ihdr = Buffer.alloc(13);
  ihdr.writeUInt32BE(1, 0); ihdr.writeUInt32BE(1, 4); ihdr[8] = 8; ihdr[9] = 6;
  const bad = Buffer.concat([
    Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), chunk('IHDR', ihdr),
    chunk('IDAT', zlib.deflateSync(Buffer.from([9, 1, 2, 3, 4]))), chunk('IEND', Buffer.alloc(0)),
  ]);
  await assert.rejects(decodePng(new Uint8Array(bad)), /filter/);
});

test('custom inflate can be injected', async () => {
  const w = 2, h = 2, px = pixels(w, h, 4, 3);
  let used = false;
  const img = await decodePng(makePng({ w, h, colorType: 6, px }), {
    inflate: async (z) => { used = true; return new Uint8Array(zlib.inflateSync(z)); },
  });
  assert.ok(used);
  assert.deepEqual([...img.data], [...px]);
});

test('inflateZlib uses DecompressionStream deflate', async () => {
  const data = Buffer.from('hello hello hello hello');
  const out = await inflateZlib(new Uint8Array(zlib.deflateSync(data)));
  assert.equal(Buffer.from(out).toString(), 'hello hello hello hello');
});
