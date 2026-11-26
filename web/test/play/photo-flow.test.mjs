import { test } from 'node:test';
import assert from 'node:assert/strict';
import { createPhotoFlow, photoAction } from '../../js/ui/components/photo-flow.js';

function fakeActions() {
  const calls = [];
  const rec = (name) => (...args) => { calls.push([name, ...args]); };
  return { calls, photoTaken: rec('photoTaken'), submitPhoto: rec('submitPhoto'), profilePhoto: rec('profilePhoto') };
}

const JPEG = new Uint8Array([0xff, 0xd8, 0xff, 0xd9]);

function capturer(usesFX = true) {
  let n = 0;
  const fn = async () => { n += 1; return { jpeg: JPEG, usesFX, quality: 0.75 }; };
  fn.count = () => n;
  return fn;
}

test('photoAction maps modes to game actions', () => {
  assert.equal(photoAction('selfie'), 'profilePhoto');
  assert.equal(photoAction('topdrawer'), 'submitPhoto');
  assert.equal(photoAction('quickdraw'), 'submitPhoto');
  assert.equal(photoAction('mimic'), 'submitPhoto');
  assert.throws(() => photoAction('nope'));
});

test('topdrawer: photoTaken at the shutter, submitPhoto on use', async () => {
  const actions = fakeActions();
  const flow = createPhotoFlow({ mode: 'topdrawer', actions, capture: capturer() });
  assert.equal(flow.state, 'live');
  const p = flow.shutter();
  assert.deepEqual(actions.calls, [['photoTaken']], 'photoTaken is sent at the press');
  assert.equal(flow.state, 'capturing');
  assert.equal(await p, true);
  assert.equal(flow.state, 'captured');
  assert.deepEqual(flow.still, { jpeg: JPEG, usesFX: true });
  assert.equal(flow.use(), true);
  assert.equal(flow.state, 'used');
  assert.deepEqual(actions.calls, [['photoTaken'], ['submitPhoto', { jpeg: JPEG, usesFX: true }]]);
  assert.equal(flow.use(), false, 'used only once');
});

test('topdrawer: retake goes back to live and photoTaken is not repeated', async () => {
  const actions = fakeActions();
  const cap = capturer();
  const flow = createPhotoFlow({ mode: 'topdrawer', actions, capture: cap });
  await flow.shutter();
  assert.equal(flow.retake(), true);
  assert.equal(flow.state, 'live');
  assert.equal(flow.still, null);
  await flow.shutter();
  assert.equal(cap.count(), 2);
  assert.deepEqual(actions.calls, [['photoTaken']]);
});

test('quickdraw and mimic: no photoTaken, submitPhoto on use', async () => {
  for (const mode of ['quickdraw', 'mimic']) {
    const actions = fakeActions();
    const flow = createPhotoFlow({ mode, actions, capture: capturer(false) });
    await flow.shutter();
    flow.use();
    assert.deepEqual(actions.calls, [['submitPhoto', { jpeg: JPEG, usesFX: false }]], mode);
  }
});

test('selfie: use calls profilePhoto', async () => {
  const actions = fakeActions();
  const flow = createPhotoFlow({ mode: 'selfie', actions, capture: capturer(true) });
  await flow.shutter();
  flow.use();
  assert.deepEqual(actions.calls, [['profilePhoto', { jpeg: JPEG, usesFX: true }]]);
});

test('shutter is ignored unless live; use and retake are ignored unless captured', async () => {
  const actions = fakeActions();
  const flow = createPhotoFlow({ mode: 'quickdraw', actions, capture: capturer() });
  assert.equal(flow.use(), false);
  assert.equal(flow.retake(), false);
  const p = flow.shutter();
  assert.equal(await flow.shutter(), false, 'second press while capturing');
  await p;
  assert.equal(await flow.shutter(), false, 'press while captured');
  assert.deepEqual(actions.calls, []);
});

test('a failing capture returns to live and reports the error', async () => {
  const errors = [];
  const flow = createPhotoFlow({
    mode: 'selfie', actions: fakeActions(), capture: async () => { throw new Error('boom'); }, onError: (e) => errors.push(e.message),
  });
  assert.equal(await flow.shutter(), false);
  assert.equal(flow.state, 'live');
  assert.deepEqual(errors, ['boom']);
});

test('a capture without bytes counts as a failure', async () => {
  const flow = createPhotoFlow({ mode: 'selfie', actions: fakeActions(), capture: async () => ({ jpeg: null }) });
  assert.equal(await flow.shutter(), false);
  assert.equal(flow.state, 'live');
});

test('no camera: placeholder still can be used (selfie)', async () => {
  const actions = fakeActions();
  const flow = createPhotoFlow({ mode: 'selfie', actions, capture: capturer() });
  flow.noCamera();
  assert.equal(flow.state, 'nocamera');
  assert.equal(await flow.shutter(), false);
  assert.equal(flow.usePlaceholder(JPEG), true);
  assert.equal(flow.state, 'captured');
  assert.equal(flow.placeholder, true);
  flow.use();
  assert.deepEqual(actions.calls, [['profilePhoto', { jpeg: JPEG, usesFX: false }]]);
});

test('retake after a placeholder goes back to the no-camera state', () => {
  const flow = createPhotoFlow({ mode: 'selfie', actions: fakeActions(), capture: capturer() });
  flow.noCamera();
  flow.usePlaceholder(JPEG);
  flow.retake();
  assert.equal(flow.state, 'nocamera');
});

test('controls reflect the state', async () => {
  const flow = createPhotoFlow({ mode: 'selfie', actions: fakeActions(), capture: capturer() });
  assert.deepEqual(flow.controls(), { shutter: true, effects: true, flip: true, retake: false, use: false, noCamera: false });
  await flow.shutter();
  assert.deepEqual(flow.controls(), { shutter: false, effects: false, flip: false, retake: true, use: true, noCamera: false });
  flow.use();
  assert.deepEqual(flow.controls(), { shutter: false, effects: false, flip: false, retake: false, use: false, noCamera: false });
  const f2 = createPhotoFlow({ mode: 'selfie', actions: fakeActions(), capture: capturer() });
  f2.noCamera();
  assert.deepEqual(f2.controls(), { shutter: false, effects: false, flip: false, retake: false, use: false, noCamera: true });
});

test('draft returns the captured still, captures from live, null otherwise', async () => {
  const cap = capturer(true);
  const flow = createPhotoFlow({ mode: 'mimic', actions: fakeActions(), capture: cap });
  assert.equal(await flow.draft('drawing'), null);
  const live = await flow.draft('photo');
  assert.deepEqual(live, { jpeg: JPEG, usesFX: true });
  assert.equal(flow.state, 'live', 'a draft capture leaves the live view alone');
  await flow.shutter();
  assert.equal(cap.count(), 2);
  assert.deepEqual(await flow.draft('photo'), { jpeg: JPEG, usesFX: true });
  assert.equal(cap.count(), 2, 'captured still reused');
  flow.use();
  assert.deepEqual(await flow.draft('photo'), { jpeg: JPEG, usesFX: true });
  const f2 = createPhotoFlow({ mode: 'mimic', actions: fakeActions(), capture: capturer() });
  f2.noCamera();
  assert.equal(await f2.draft('photo'), null);
  const f3 = createPhotoFlow({ mode: 'mimic', actions: fakeActions(), capture: async () => { throw new Error('x'); } });
  assert.equal(await f3.draft('photo'), null);
});

test('destroyed flow ignores late captures and actions', async () => {
  const actions = fakeActions();
  let resolve;
  const flow = createPhotoFlow({ mode: 'topdrawer', actions, capture: () => new Promise((r) => { resolve = r; }) });
  const p = flow.shutter();
  flow.destroy();
  resolve({ jpeg: JPEG, usesFX: false });
  assert.equal(await p, false);
  assert.equal(flow.use(), false);
  assert.deepEqual(actions.calls, [['photoTaken']]);
});

test('change listeners fire on transitions', async () => {
  const flow = createPhotoFlow({ mode: 'selfie', actions: fakeActions(), capture: capturer() });
  const states = [];
  const off = flow.on((s) => states.push(s));
  await flow.shutter();
  flow.retake();
  off();
  flow.noCamera();
  assert.deepEqual(states, ['capturing', 'captured', 'live']);
});
