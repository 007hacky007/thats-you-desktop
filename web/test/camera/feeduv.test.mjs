import test from 'node:test';
import assert from 'node:assert/strict';
import { feedCrop, feedRect, feedPoint } from '../../js/camera/feeduv.js';

const close = (a, b, msg) => assert.ok(Math.abs(a - b) < 1e-9, `${msg}: ${a} != ${b}`);

test('640x480 landscape: central 3:4 slice like the original app', () => {
  const c = feedCrop(640, 480, { feed: 'portrait', mirror: false });
  close(c.u0, 0.21875, 'u0'); close(c.u1, 0.78125, 'u1');
  close(c.v0, 0, 'v0'); close(c.v1, 1, 'v1');
});

test('640x480 front camera is mirrored horizontally', () => {
  const c = feedCrop(640, 480, { feed: 'portrait', mirror: true });
  close(c.u0, 0.78125, 'u0'); close(c.u1, 0.21875, 'u1');
  close(c.v0, 0, 'v0'); close(c.v1, 1, 'v1');
});

test('480x640 portrait frame uses the whole frame', () => {
  const c = feedCrop(480, 640, { feed: 'portrait' });
  assert.deepEqual([c.u0, c.u1, c.v0, c.v1], [0, 1, 0, 1]);
});

test('1280x720 crops a centred 540x720 rectangle', () => {
  const c = feedCrop(1280, 720, { feed: 'portrait' });
  close(c.u0, 370 / 1280, 'u0'); close(c.u1, 910 / 1280, 'u1');
  close(c.v0, 0, 'v0'); close(c.v1, 1, 'v1');
});

test('720x1280 crops a centred 720x960 rectangle', () => {
  const c = feedCrop(720, 1280, { feed: 'portrait' });
  close(c.u0, 0, 'u0'); close(c.u1, 1, 'u1');
  close(c.v0, 0.125, 'v0'); close(c.v1, 0.875, 'v1');
});

test('square feed for 640x480 matches the original 0.125..0.875', () => {
  const c = feedCrop(640, 480, { feed: 'square' });
  close(c.u0, 0.125, 'u0'); close(c.u1, 0.875, 'u1');
  close(c.v0, 0, 'v0'); close(c.v1, 1, 'v1');
});

test('square feed for 480x640, 1280x720 and 720x1280', () => {
  let c = feedCrop(480, 640, { feed: 'square' });
  close(c.v0, 0.125, 'v0'); close(c.v1, 0.875, 'v1'); close(c.u0, 0, 'u0'); close(c.u1, 1, 'u1');
  c = feedCrop(1280, 720, { feed: 'square' });
  close(c.u0, 280 / 1280, 'u0'); close(c.u1, 1000 / 1280, 'u1');
  c = feedCrop(720, 1280, { feed: 'square', mirror: true });
  close(c.u0, 1, 'u0'); close(c.u1, 0, 'u1');
  close(c.v0, 280 / 1280, 'v0'); close(c.v1, 1000 / 1280, 'v1');
});

test('unknown or zero frame size gives the full frame', () => {
  const c = feedCrop(0, 0, { feed: 'portrait', mirror: true });
  assert.deepEqual([c.u0, c.u1, c.v0, c.v1], [1, 0, 0, 1]);
});

test('feedRect gives the pixel source rectangle for the 2D fallback', () => {
  assert.deepEqual(feedRect(640, 480, { feed: 'portrait', mirror: true }),
    { sx: 140, sy: 0, sw: 360, sh: 480, mirror: true });
  assert.deepEqual(feedRect(720, 1280, { feed: 'portrait' }),
    { sx: 0, sy: 160, sw: 720, sh: 960, mirror: false });
});

test('feedPoint maps feed space linearly into the crop', () => {
  const c = feedCrop(640, 480, { feed: 'portrait', mirror: true });
  const [u, v] = feedPoint(c, 0, 0.5);
  close(u, 0.78125, 'u'); close(v, 0.5, 'v');
  const [u2] = feedPoint(c, 1, 0);
  close(u2, 0.21875, 'u2');
});
