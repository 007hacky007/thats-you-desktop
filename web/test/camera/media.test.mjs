import test from 'node:test';
import assert from 'node:assert/strict';
import { createMedia, EFFECTS } from '../../js/camera/media.js';

function fakeDocument() {
  return {
    body: { appendChild() {} },
    createElement(tag) {
      const ops = [];
      const ctx = new Proxy({}, {
        get(t, k) {
          if (k in t) return t[k];
          if (k === 'createRadialGradient') return () => ({ addColorStop() {} });
          return (...a) => { ops.push([k, ...a]); };
        },
        set(t, k, v) { t[k] = v; return true; },
      });
      return {
        tag, ops, width: 0, height: 0, style: {},
        setAttribute() {}, addEventListener() {}, removeEventListener() {},
        getContext: (type) => (type === '2d' ? ctx : null),
        toBlob(cb, type, q) { cb(new Blob([new Uint8Array([0xff, 0xd8, Math.round(q * 100)])], { type })); },
        async play() {},
      };
    },
  };
}

test('createMedia exposes the contract surface', async () => {
  const media = createMedia({ document: fakeDocument(), mediaDevices: null, assets: null });
  assert.equal(media.EFFECTS, EFFECTS);
  assert.equal(media.EFFECTS.length, 12);
  assert.equal(typeof media.camera.start, 'function');
  assert.equal(media.camera.supported, false);
  const r = media.createRenderer();
  assert.equal(r.canvas.width, 480);
  assert.equal(r.webgl, false);
  const s = media.createDrawingSurface({ raf: null });
  assert.equal(s.element.width, 768);
  s.destroy();
  r.destroy();
});

test('placeholderPortrait returns a 480x640 JPEG', async () => {
  const doc = fakeDocument();
  let made = null;
  const orig = doc.createElement;
  doc.createElement = (tag) => { made = orig(tag); return made; };
  const media = createMedia({ document: doc, mediaDevices: null });
  const jpeg = await media.placeholderPortrait('Kim');
  assert.ok(jpeg instanceof Uint8Array);
  assert.deepEqual([...jpeg], [0xff, 0xd8, 75]);
  assert.equal(made.width, 480);
  assert.equal(made.height, 640);
  assert.ok(made.ops.some((o) => o[0] === 'fillText' && o[1] === 'K'));
});

test('encodeJpeg applies the rule to a canvas', async () => {
  const media = createMedia({ document: fakeDocument(), mediaDevices: null });
  const r = await media.encodeJpeg(fakeDocument().createElement('canvas'));
  assert.equal(r.quality, 0.75);
});
