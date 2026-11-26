import test from 'node:test';
import assert from 'node:assert/strict';
import { classifyFlick, attachFlick } from '../../js/ui/flick.js';
import { holdProgress, attachHold } from '../../js/ui/hold.js';
import { installFakeDom, fire } from './fakedom.mjs';

const S = (x, y, t) => ({ x, y, t });

test('flick directions', () => {
  assert.equal(classifyFlick([S(100, 300, 0), S(102, 200, 120)]), 'up');
  assert.equal(classifyFlick([S(100, 100, 0), S(98, 200, 120)]), 'down');
  assert.equal(classifyFlick([S(100, 100, 0), S(20, 110, 120)]), 'left');
  assert.equal(classifyFlick([S(100, 100, 0), S(190, 90, 120)]), 'right');
});

test('flick too short, too slow or diagonal is ignored', () => {
  assert.equal(classifyFlick([S(0, 100, 0), S(0, 50, 100)]), null);
  assert.equal(classifyFlick([S(0, 300, 0), S(0, 100, 900)]), null);
  assert.equal(classifyFlick([S(0, 0, 0), S(80, 80, 100)]), null);
  assert.equal(classifyFlick([]), null);
  assert.equal(classifyFlick([S(0, 0, 0)]), null);
});

test('only the last maxMs of a slow drag count', () => {
  // drag slowly for a second, then flick up fast
  const samples = [S(0, 400, 0), S(0, 390, 500), S(0, 385, 1000), S(0, 300, 1100), S(0, 250, 1150)];
  assert.equal(classifyFlick(samples, { maxMs: 600 }), 'up');
  // the slow part alone moved 15 px: nothing
  assert.equal(classifyFlick(samples.slice(0, 3), { maxMs: 600 }), null);
});

test('custom minDistance', () => {
  assert.equal(classifyFlick([S(0, 100, 0), S(0, 70, 50)], { minDistance: 20 }), 'up');
});

test('holdProgress clamps', () => {
  assert.equal(holdProgress(1000, 1000, 2000), 0);
  assert.equal(holdProgress(1000, 2000, 2000), 0.5);
  assert.equal(holdProgress(1000, 5000, 2000), 1);
  assert.equal(holdProgress(1000, 500, 2000), 0);
  assert.equal(holdProgress(0, 10, 0), 1);
});

function fakeClock() {
  let now = 0;
  let id = 0;
  const timers = new Map();
  return {
    now: () => now,
    setTimeout(fn, ms) { timers.set(++id, { fn, at: now + ms }); return id; },
    clearTimeout(i) { timers.delete(i); },
    advance(ms) {
      const end = now + ms;
      for (;;) {
        let next = null;
        for (const [i, tm] of timers) if (tm.at <= end && (!next || tm.at < next[1].at)) next = [i, tm];
        if (!next) break;
        timers.delete(next[0]);
        now = next[1].at;
        next[1].fn();
      }
      now = end;
    },
  };
}

test('attachFlick reports a pointer flick', () => {
  const document = installFakeDom();
  const el = document.createElement('div');
  const clock = fakeClock();
  const seen = [];
  const detach = attachFlick(el, { onFlick: (d) => seen.push(d), clock });
  fire(el, 'pointerdown', { pointerId: 1, clientX: 50, clientY: 300, isPrimary: true, button: 0 });
  clock.advance(50);
  fire(el, 'pointermove', { pointerId: 1, clientX: 50, clientY: 250 });
  clock.advance(50);
  fire(el, 'pointerup', { pointerId: 1, clientX: 52, clientY: 180 });
  assert.deepEqual(seen, ['up']);
  detach();
  fire(el, 'pointerdown', { pointerId: 2, clientX: 50, clientY: 300, isPrimary: true, button: 0 });
  fire(el, 'pointerup', { pointerId: 2, clientX: 50, clientY: 100 });
  assert.deepEqual(seen, ['up']);
});

test('attachFlick ignores cancelled pointers and other pointer ids', () => {
  const document = installFakeDom();
  const el = document.createElement('div');
  const clock = fakeClock();
  const seen = [];
  attachFlick(el, { onFlick: (d) => seen.push(d), clock });
  fire(el, 'pointerdown', { pointerId: 1, clientX: 50, clientY: 300, isPrimary: true, button: 0 });
  fire(el, 'pointercancel', { pointerId: 1 });
  fire(el, 'pointerup', { pointerId: 1, clientX: 50, clientY: 100 });
  fire(el, 'pointerdown', { pointerId: 3, clientX: 50, clientY: 300, isPrimary: true, button: 0 });
  fire(el, 'pointerup', { pointerId: 4, clientX: 50, clientY: 100 });
  assert.deepEqual(seen, []);
});

test('attachHold completes after ms and reports progress', () => {
  const document = installFakeDom();
  const el = document.createElement('button');
  const clock = fakeClock();
  const events = [];
  attachHold(el, {
    ms: 2000, clock, raf: null,
    onProgress: (p) => events.push(['p', Math.round(p * 100) / 100]),
    onDone: () => events.push(['done']),
    onCancel: () => events.push(['cancel']),
  });
  fire(el, 'pointerdown', { pointerId: 1, button: 0, isPrimary: true });
  clock.advance(1000);
  assert.ok(events.some(([k, p]) => k === 'p' && p >= 0.45 && p <= 0.55), JSON.stringify(events));
  clock.advance(1000);
  assert.deepEqual(events.at(-1), ['done']);
  assert.ok(events.some(([k, p]) => k === 'p' && p === 1));
  fire(el, 'pointerup', { pointerId: 1 });
  assert.ok(!events.some(([k]) => k === 'cancel'));
});

test('attachHold cancels on early release', () => {
  const document = installFakeDom();
  const el = document.createElement('button');
  const clock = fakeClock();
  const events = [];
  attachHold(el, { ms: 2000, clock, raf: null, onProgress: (p) => events.push(p), onDone: () => events.push('done'), onCancel: () => events.push('cancel') });
  fire(el, 'pointerdown', { pointerId: 1, button: 0, isPrimary: true });
  clock.advance(500);
  fire(el, 'pointerup', { pointerId: 1 });
  clock.advance(3000);
  assert.ok(events.includes('cancel'));
  assert.ok(!events.includes('done'));
  assert.equal(events.at(-1), 'cancel');
  assert.ok(events.includes(0));
});

test('attachHold works with keyboard and detaches', () => {
  const document = installFakeDom();
  const el = document.createElement('button');
  const clock = fakeClock();
  const events = [];
  const detach = attachHold(el, { ms: 100, clock, raf: null, onDone: () => events.push('done') });
  fire(el, 'keydown', { key: ' ' });
  clock.advance(150);
  assert.deepEqual(events, ['done']);
  detach();
  fire(el, 'pointerdown', { pointerId: 1, button: 0, isPrimary: true });
  clock.advance(150);
  assert.deepEqual(events, ['done']);
});
