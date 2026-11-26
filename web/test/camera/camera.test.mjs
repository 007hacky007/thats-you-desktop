import test from 'node:test';
import assert from 'node:assert/strict';
import { createCamera, cameraConstraints, RELEASE_MS } from '../../js/camera/camera.js';

function fakeClock() {
  let now = 0, seq = 0;
  const timers = new Map();
  return {
    setTimeout(fn, ms) { const id = ++seq; timers.set(id, { at: now + ms, fn }); return id; },
    clearTimeout(id) { timers.delete(id); },
    advance(ms) {
      now += ms;
      for (const [id, t] of [...timers]) if (t.at <= now) { timers.delete(id); t.fn(); }
    },
    pending: () => timers.size,
  };
}

function fakeTrack(facing) {
  const listeners = {};
  return {
    kind: 'video', stopped: false, readyState: 'live',
    stop() { this.stopped = true; this.readyState = 'ended'; },
    getSettings: () => (facing ? { facingMode: facing } : {}),
    addEventListener(ev, fn) { listeners[ev] = fn; },
    fire(ev) { listeners[ev]?.(); },
  };
}

function fakeDevices({ inputs = 1, fail = {} } = {}) {
  const calls = [];
  const tracks = [];
  return {
    calls, tracks,
    async getUserMedia(c) {
      calls.push(c);
      const key = calls.length;
      if (fail[key]) { const e = new Error(fail[key]); e.name = fail[key]; throw e; }
      const facing = c.video && c.video.facingMode ? c.video.facingMode.ideal : undefined;
      const t = fakeTrack(facing);
      tracks.push(t);
      return { getTracks: () => [t], getVideoTracks: () => [t] };
    },
    async enumerateDevices() {
      const out = [{ kind: 'audioinput' }];
      for (let i = 0; i < inputs; i++) out.push({ kind: 'videoinput' });
      return out;
    },
  };
}

function fakeDocument() {
  const appended = [];
  return {
    appended,
    body: { appendChild(el) { appended.push(el); } },
    createElement(tag) {
      const attrs = {};
      return {
        tag, attrs, style: {}, srcObject: null, played: 0,
        setAttribute(k, v) { attrs[k] = v; },
        async play() { this.played++; },
      };
    },
  };
}

function setup(opts = {}) {
  const clock = fakeClock();
  const devices = fakeDevices(opts);
  const document = fakeDocument();
  const cam = createCamera({ mediaDevices: devices, document, clock });
  return { clock, devices, document, cam };
}

test('constraints use ideal values per facing', () => {
  assert.deepEqual(cameraConstraints('user'), {
    video: { facingMode: { ideal: 'user' }, width: { ideal: 640 }, height: { ideal: 480 }, frameRate: { ideal: 15 } },
    audio: false,
  });
  assert.equal(cameraConstraints('environment').video.frameRate.ideal, 30);
  assert.equal(RELEASE_MS, 10000);
});

test('video element is hidden, inline and muted', () => {
  const { cam, document } = setup();
  const v = cam.video;
  assert.equal(v.tag, 'video');
  assert.equal(v.muted, true);
  assert.equal(v.playsInline, true);
  assert.equal(v.attrs.playsinline, '');
  assert.equal(v.attrs.muted, '');
  assert.equal(v.attrs['aria-hidden'], 'true');
  assert.equal(v.style.opacity, '0');
  assert.deepEqual(document.appended, [v]);
  assert.equal(cam.video, v);
});

test('start opens the camera, plays the video and finds flip support', async () => {
  const { cam, devices } = setup({ inputs: 2 });
  assert.equal(cam.active, false);
  await cam.start('user');
  assert.deepEqual(devices.calls, [cameraConstraints('user')]);
  assert.equal(cam.active, true);
  assert.equal(cam.facing, 'user');
  assert.equal(cam.canFlip, true);
  assert.ok(cam.video.srcObject);
  assert.equal(cam.video.played, 1);
});

test('one video input means no flip', async () => {
  const { cam } = setup({ inputs: 1 });
  await cam.start('environment');
  assert.equal(cam.canFlip, false);
  assert.equal(cam.facing, 'environment');
});

test('starting the same facing again reuses the stream', async () => {
  const { cam, devices } = setup();
  await cam.start('user');
  await cam.start('user');
  assert.equal(devices.calls.length, 1);
});

test('flip switches facing and stops the old tracks', async () => {
  const { cam, devices } = setup({ inputs: 2 });
  await cam.start('user');
  await cam.flip();
  assert.equal(cam.facing, 'environment');
  assert.equal(devices.calls[1].video.facingMode.ideal, 'environment');
  assert.equal(devices.tracks[0].stopped, true);
  assert.equal(devices.tracks[1].stopped, false);
  await cam.flip();
  assert.equal(cam.facing, 'user');
});

test('reported facing from the track wins over the request', async () => {
  const { cam, devices } = setup();
  devices.getUserMedia = async (c) => {
    const t = fakeTrack('environment');
    return { getTracks: () => [t], getVideoTracks: () => [t] };
  };
  await cam.start('user');
  assert.equal(cam.facing, 'environment');
});

test('OverconstrainedError falls back to simpler constraints', async () => {
  const { cam, devices } = setup({ fail: { 1: 'OverconstrainedError', 2: 'OverconstrainedError' } });
  await cam.start('environment');
  assert.equal(devices.calls.length, 3);
  assert.deepEqual(devices.calls[1], { video: { facingMode: { ideal: 'environment' } }, audio: false });
  assert.deepEqual(devices.calls[2], { video: true, audio: false });
  assert.equal(cam.active, true);
});

test('permission errors reject and leave the camera inactive', async () => {
  const { cam, devices } = setup({ fail: { 1: 'NotAllowedError' } });
  await assert.rejects(cam.start('user'), (e) => e.name === 'NotAllowedError');
  assert.equal(devices.calls.length, 1);
  assert.equal(cam.active, false);
});

test('release stops the camera 10 s later', async () => {
  const { cam, clock, devices } = setup();
  await cam.start('user');
  cam.release();
  clock.advance(9999);
  assert.equal(cam.active, true);
  clock.advance(1);
  assert.equal(cam.active, false);
  assert.equal(devices.tracks[0].stopped, true);
  assert.equal(cam.video.srcObject, null);
});

test('start during the release delay cancels it', async () => {
  const { cam, clock, devices } = setup();
  await cam.start('user');
  cam.release();
  clock.advance(5000);
  await cam.start('user');
  clock.advance(20000);
  assert.equal(cam.active, true);
  assert.equal(devices.calls.length, 1);
  assert.equal(clock.pending(), 0);
});

test('stopNow stops at once', async () => {
  const { cam, devices } = setup();
  await cam.start('user');
  cam.release();
  cam.stopNow();
  assert.equal(cam.active, false);
  assert.equal(devices.tracks[0].stopped, true);
});

test('a track that ends marks the camera inactive', async () => {
  const { cam, devices } = setup();
  await cam.start('user');
  devices.tracks[0].fire('ended');
  assert.equal(cam.active, false);
  await cam.start('user');
  assert.equal(devices.calls.length, 2);
});

test('requestPermission opens and closes a stream', async () => {
  const { cam, devices } = setup();
  assert.equal(await cam.requestPermission(), true);
  assert.deepEqual(devices.calls[0], { video: true, audio: false });
  assert.equal(devices.tracks[0].stopped, true);
  assert.equal(cam.active, false);
});

test('requestPermission is false when refused', async () => {
  const { cam } = setup({ fail: { 1: 'NotAllowedError' } });
  assert.equal(await cam.requestPermission(), false);
});

test('requestPermission is true while active without a new stream', async () => {
  const { cam, devices } = setup();
  await cam.start('user');
  assert.equal(await cam.requestPermission(), true);
  assert.equal(devices.calls.length, 1);
});

test('without mediaDevices start rejects and permission is false', async () => {
  const cam = createCamera({ mediaDevices: null, document: fakeDocument(), clock: fakeClock() });
  assert.equal(cam.supported, false);
  await assert.rejects(cam.start('user'), (e) => e.name === 'NotSupportedError');
  assert.equal(await cam.requestPermission(), false);
});

test('concurrent starts are serialized; the last facing wins', async () => {
  const { cam, devices } = setup({ inputs: 2 });
  const a = cam.start('user');
  const b = cam.start('environment');
  await Promise.all([a, b]);
  assert.equal(cam.facing, 'environment');
  assert.equal(devices.tracks.filter((t) => !t.stopped).length, 1);
});

test('stopNow while a start is in flight leaves the camera stopped', async () => {
  const { cam, devices } = setup();
  const p = cam.start('user');
  cam.stopNow();
  await p.catch(() => {});
  assert.equal(cam.active, false);
  assert.ok(devices.tracks.every((t) => t.stopped));
});
