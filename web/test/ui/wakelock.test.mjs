import test from 'node:test';
import assert from 'node:assert/strict';
import { keepAwake } from '../../js/ui/wakelock.js';

function fakes({ api = true, fail = false } = {}) {
  const calls = [];
  const listeners = {};
  let sentinel = null;
  const nav = api ? {
    wakeLock: {
      request: async (type) => {
        calls.push(type);
        if (fail) throw new Error('NotAllowedError');
        sentinel = { released: false, release: async () => { sentinel.released = true; } };
        return sentinel;
      },
    },
  } : {};
  const doc = {
    visibilityState: 'visible',
    addEventListener: (k, fn) => { listeners[k] = fn; },
    removeEventListener: (k) => { delete listeners[k]; },
  };
  return { nav, doc, calls, listeners, lock: () => sentinel };
}

test('requests a screen wake lock', async () => {
  const f = fakes();
  const k = keepAwake(f.nav, f.doc);
  await k.ready;
  assert.deepEqual(f.calls, ['screen']);
  assert.equal(k.held(), true);
});

test('asks again when the page becomes visible after the browser dropped the lock', async () => {
  const f = fakes();
  const k = keepAwake(f.nav, f.doc);
  await k.ready;
  f.lock().released = true; // the browser releases it when the page is hidden
  f.doc.visibilityState = 'hidden';
  await f.listeners.visibilitychange();
  assert.equal(f.calls.length, 1);
  f.doc.visibilityState = 'visible';
  await f.listeners.visibilitychange();
  assert.deepEqual(f.calls, ['screen', 'screen']);
  assert.equal(k.held(), true);
});

test('stop releases the lock and stops listening', async () => {
  const f = fakes();
  const k = keepAwake(f.nav, f.doc);
  await k.ready;
  const lock = f.lock();
  await k.stop();
  assert.equal(lock.released, true);
  assert.equal(f.listeners.visibilitychange, undefined);
});

test('no API or a refused request is harmless', async () => {
  const none = fakes({ api: false });
  const k1 = keepAwake(none.nav, none.doc);
  await k1.ready;
  assert.equal(k1.held(), false);
  const refused = fakes({ fail: true });
  const k2 = keepAwake(refused.nav, refused.doc);
  await k2.ready;
  assert.equal(k2.held(), false);
});
