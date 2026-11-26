import { test } from 'node:test';
import assert from 'node:assert/strict';

import { createEmitter } from '../../js/util/emitter.js';
import { realClock, createFakeClock, flush } from '../../js/util/clock.js';
import { createStorage, createMemoryBackend } from '../../js/util/storage.js';
import { createLog } from '../../js/util/log.js';

test('emitter on/off/once/emit', () => {
  const em = createEmitter();
  const got = [];
  const off = em.on('a', (x, y) => got.push(['a', x, y]));
  em.once('a', (x) => got.push(['once', x]));
  em.emit('a', 1, 2);
  em.emit('a', 3, 4);
  off();
  em.emit('a', 5, 6);
  assert.deepEqual(got, [['a', 1, 2], ['once', 1], ['a', 3, 4]]);
  assert.equal(em.count('a'), 0);
});

test('emitter isolates listener errors', () => {
  const errors = [];
  const em = createEmitter({ onError: (e) => errors.push(e.message) });
  const got = [];
  em.on('x', () => { throw new Error('boom'); });
  em.on('x', () => got.push(1));
  em.emit('x');
  assert.deepEqual(got, [1]);
  assert.deepEqual(errors, ['boom']);
});

test('emitter: removing a listener during emit does not skip others', () => {
  const em = createEmitter();
  const got = [];
  let offB = null;
  em.on('x', () => { got.push('a'); offB(); });
  offB = em.on('x', () => got.push('b'));
  em.emit('x');
  em.emit('x');
  assert.deepEqual(got, ['a', 'b', 'a']);
});

test('realClock has the full surface', () => {
  for (const k of ['now', 'setTimeout', 'clearTimeout', 'setInterval', 'clearInterval']) {
    assert.equal(typeof realClock[k], 'function');
  }
  const id = realClock.setTimeout(() => {}, 1000);
  realClock.clearTimeout(id);
});

test('fake clock runs timers in order and intervals repeatedly', () => {
  const c = createFakeClock(1000);
  const got = [];
  c.setTimeout(() => got.push('t500@' + c.now()), 500);
  const iv = c.setInterval(() => got.push('iv@' + c.now()), 200);
  const gone = c.setTimeout(() => got.push('never'), 300);
  c.clearTimeout(gone);
  c.advance(450);
  assert.equal(c.now(), 1450);
  c.advance(100);
  c.clearInterval(iv);
  c.advance(1000);
  assert.deepEqual(got, ['iv@1200', 'iv@1400', 't500@1500']);
  assert.equal(c.pending(), 0);
});

test('fake clock: timers scheduled by timers inside the window run', () => {
  const c = createFakeClock();
  const got = [];
  c.setTimeout(() => {
    got.push(c.now());
    c.setTimeout(() => got.push(c.now()), 10);
  }, 10);
  c.advance(25);
  assert.deepEqual(got, [10, 20]);
});

test('fake clock advanceAsync lets promises settle between timers', async () => {
  const c = createFakeClock();
  const got = [];
  c.setTimeout(() => {
    Promise.resolve().then(() => {
      got.push('p@' + c.now());
      c.setTimeout(() => got.push('t@' + c.now()), 5);
    });
  }, 10);
  await c.advanceAsync(20);
  assert.deepEqual(got, ['p@10', 't@15']);
  await flush();
});

test('storage prefixes keys and parses JSON', () => {
  const be = createMemoryBackend();
  const s = createStorage(be);
  s.set('uid', 'abc');
  s.setJSON('obj', { a: 1 });
  assert.equal(be.getItem('iwyk.uid'), 'abc');
  assert.equal(s.get('uid'), 'abc');
  assert.deepEqual(s.getJSON('obj'), { a: 1 });
  assert.equal(s.get('missing'), null);
  assert.equal(s.getJSON('missing', 7), 7);
  be.setItem('iwyk.bad', '{nope');
  assert.equal(s.getJSON('bad', 'd'), 'd');
  s.remove('uid');
  assert.equal(s.get('uid'), null);
  assert.equal(be.getItem('iwyk.uid'), null);
});

test('storage falls back to memory when the backend throws', () => {
  const broken = {
    getItem() { throw new Error('denied'); },
    setItem() { throw new Error('quota'); },
    removeItem() { throw new Error('denied'); },
  };
  const s = createStorage(broken);
  assert.equal(s.set('k', 'v'), false);
  assert.equal(s.get('k'), 'v');
  s.remove('k');
  assert.equal(s.get('k'), null);
});

test('storage keeps a value whose write failed only for quota', () => {
  const be = createMemoryBackend();
  let full = false;
  const wrapped = {
    getItem: (k) => be.getItem(k),
    setItem: (k, v) => { if (full) throw new Error('quota'); be.setItem(k, v); },
    removeItem: (k) => be.removeItem(k),
  };
  const s = createStorage(wrapped);
  full = true;
  s.set('big', 'x');
  assert.equal(s.get('big'), 'x');
});

test('storage with a null backend works in memory', () => {
  const s = createStorage(null);
  s.setJSON('a', [1]);
  assert.deepEqual(s.getJSON('a'), [1]);
});

test('log keeps a ring buffer and emits entries', () => {
  let t = 0;
  const log = createLog({ capacity: 3, now: () => ++t });
  const seen = [];
  log.on('entry', (e) => seen.push(e.level));
  log.debug('a');
  log.info('b', { x: 1 });
  log.warn('c', 2);
  log.error(new Error('d'));
  const e = log.entries();
  assert.equal(e.length, 3);
  assert.deepEqual(e.map((x) => x.msg), ['b {"x":1}', 'c 2', 'Error: d']);
  assert.deepEqual(e.map((x) => x.level), ['info', 'warn', 'error']);
  assert.equal(e[0].t, 2);
  assert.deepEqual(seen, ['debug', 'info', 'warn', 'error']);
});

test('log formats cyclic objects without throwing', () => {
  const log = createLog();
  const o = {};
  o.self = o;
  log.info(o);
  assert.equal(log.entries()[0].msg, '[object Object]');
});
