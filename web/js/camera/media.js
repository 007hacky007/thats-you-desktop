// createMedia: the camera, effects, capture and drawing API used by the
// screens (contract 5.8). Browser pieces are created lazily, so importing
// this module in Node is safe.

import { createCamera } from './camera.js';
import { EFFECTS, createRenderer } from './effects.js';
import { encodeCanvasJpeg, drawPlaceholder } from './capture.js';
import { createDrawingSurface } from '../draw/surface.js';

export { EFFECTS };

export function createMedia({
  assets = null,
  log = null,
  document = globalThis.document,
  mediaDevices = globalThis.navigator?.mediaDevices,
  fetch: fetchFn = globalThis.fetch,
  clock = globalThis,
} = {}) {
  const camera = createCamera({ mediaDevices, document, clock, log });

  return {
    EFFECTS,
    camera,
    createRenderer(opts = {}) {
      const r = createRenderer({ document, assets, log, fetch: fetchFn, ...opts });
      r.attach(camera);
      return r;
    },
    createDrawingSurface(opts = {}) {
      return createDrawingSurface({ document, log, ...opts });
    },
    encodeJpeg(canvas) {
      return encodeCanvasJpeg(canvas);
    },
    async placeholderPortrait(name) {
      const c = document.createElement('canvas');
      c.width = 480;
      c.height = 640;
      drawPlaceholder(c.getContext('2d'), name, 480, 640);
      const { jpeg } = await encodeCanvasJpeg(c);
      return jpeg;
    },
  };
}
