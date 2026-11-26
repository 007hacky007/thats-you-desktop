import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createCameraOwners } from '../../js/ui/components/camera-owner.js';
import { createCamera } from '../../js/camera/camera.js';

function fakeCamera() {
  const calls = [];
  return { calls, release() { calls.push('release'); } };
}

test('a view unmounted during an await releases the camera when no other view owns it', () => {
  const cam = fakeCamera();
  const owners = createCameraOwners();
  const a = owners.mount();
  a.unmount(cam);
  assert.deepEqual(cam.calls, ['release']);
  a.settled(cam);
  assert.deepEqual(cam.calls, ['release', 'release']);
});

test('a late settle does not release a camera that a newer view owns', () => {
  const cam = fakeCamera();
  const owners = createCameraOwners();
  const a = owners.mount();
  a.unmount(cam);
  const b = owners.mount();
  a.settled(cam);
  assert.deepEqual(cam.calls, ['release'], 'b keeps the camera');
  b.settled(cam);
  assert.deepEqual(cam.calls, ['release'], 'a live view never releases on settle');
  b.unmount(cam);
  b.unmount(cam);
  assert.deepEqual(cam.calls, ['release', 'release'], 'unmount counts once');
  assert.equal(owners.count, 0);
});

// end to end with U4's camera and a getUserMedia that resolves after unmount
function deferredMedia() {
  const pending = [];
  const tracks = [];
  return {
    tracks,
    pending,
    getUserMedia() {
      return new Promise((resolve) => {
        pending.push(() => {
          const t = { stopped: false, stop() { this.stopped = true; }, getSettings: () => ({ facingMode: 'user' }) };
          tracks.push(t);
          resolve({ getTracks: () => [t], getVideoTracks: () => [t] });
        });
      });
    },
    enumerateDevices: async () => [{ kind: 'videoinput' }],
  };
}

function fakeClock() {
  const timers = new Map();
  let id = 0;
  return {
    setTimeout(fn) { timers.set(++id, fn); return id; },
    clearTimeout(i) { timers.delete(i); },
    runAll() { const fns = [...timers.values()]; timers.clear(); fns.forEach((f) => f()); },
  };
}

test('camera started while the permission prompt is open stops after the view unmounts', async () => {
  const md = deferredMedia();
  const clock = fakeClock();
  const camera = createCamera({ mediaDevices: md, document: null, clock, releaseMs: 10 });
  const owners = createCameraOwners();
  const view = owners.mount();
  const started = camera.start('user').then(() => view.settled(camera), () => view.settled(camera));
  await Promise.resolve();
  view.unmount(camera);
  clock.runAll();
  md.pending.shift()();
  await started;
  clock.runAll();
  assert.equal(md.tracks.length, 1);
  assert.equal(md.tracks[0].stopped, true, 'the stream that arrived late is stopped');
  assert.equal(camera.active, false);
});
