import { test } from 'node:test';
import assert from 'node:assert/strict';

import { createLink } from '../../js/net/link.js';
import { createFakeClock, flush } from '../../js/util/clock.js';
import { encodeChunks } from '../../js/net/frames.js';
import { createFakeTransportFactory } from './fake-transport.mjs';

const PARAMS = { h: '10.0.0.2', p: 47000, u: 'uf', w: 'pw', f: 'x'.repeat(43), t: 'TOK' };

async function setup({ open = true } = {}) {
  const clock = createFakeClock(1000);
  const factory = createFakeTransportFactory();
  const link = createLink({ params: PARAMS, uid: 'UID-1', transportFactory: factory, clock });
  const events = [];
  for (const ev of ['state', 'open', 'pending', 'joined', 'msg', 'content', 'end']) {
    link.on(ev, (...a) => events.push([ev, ...a]));
  }
  link.connect();
  const tr = factory.last();
  if (open) {
    tr.accept();
    await flush();
  }
  return { clock, factory, link, tr, events, ends: () => events.filter((e) => e[0] === 'end').map((e) => e[1]) };
}

test('hello is the first frame, joined enables msg', async () => {
  const { link, tr, events } = await setup();
  assert.equal(tr.sent[0], '{"t":"hello","token":"TOK","uid":"UID-1"}');
  assert.equal(link.state, 'hello');
  assert.equal(link.sendMsg({ TypeString: 'X' }), false);
  tr.recvText('{"t":"msg","id":-7503,"json":{"TypeString":"Early"}}');
  tr.recvText('{"t":"joined"}');
  assert.equal(link.state, 'joined');
  tr.recvText('{"t":"msg","id":-7503,"json":{"TypeString":"InterfaceVersionMessage","InterfaceVersion":"v"}}');
  tr.recvText('{"t":"msg","id":1,"json":{"TypeString":"Ignored"}}');
  tr.recvText('{"t":"whatever"}');
  tr.recvText('garbage');
  const msgs = events.filter((e) => e[0] === 'msg').map((e) => e[1].TypeString);
  assert.deepEqual(msgs, ['InterfaceVersionMessage']);
  assert.equal(link.sendMsg({ TypeString: 'ClientDisconnectMessage' }), true);
  assert.equal(tr.sent.at(-1), '{"t":"msg","id":-7503,"json":{"TypeString":"ClientDisconnectMessage"}}');
});

test('pings every 2 s from channel open', async () => {
  const { clock, tr } = await setup();
  clock.advance(1999);
  assert.equal(tr.texts().filter((s) => s === '{"t":"ping"}').length, 0);
  clock.advance(1);
  clock.advance(4000);
  assert.equal(tr.texts().filter((s) => s === '{"t":"ping"}').length, 3);
});

test('channel open timeout 17 s', async () => {
  const { clock, tr, ends } = await setup({ open: false });
  clock.advance(16999);
  assert.deepEqual(ends(), []);
  clock.advance(1);
  assert.deepEqual(ends(), [{ cause: 'open-timeout', reason: undefined, wasJoined: false }]);
  assert.equal(tr.closed, true);
});

test('open failure ends the link', async () => {
  const { tr, ends } = await setup({ open: false });
  tr.fail();
  await flush();
  assert.equal(ends()[0].cause, 'open-failed');
});

test('hello reply timeout 17 s', async () => {
  const { clock, tr, ends } = await setup();
  clock.advance(16000);
  tr.recvText('{"t":"ping"}');
  clock.advance(1000);
  // pings from the game do not count as a reply to hello
  assert.equal(ends()[0].cause, 'hello-timeout');
});

test('pending waits up to 30 s, then joined or timeout', async () => {
  const a = await setup();
  a.tr.recvText('{"t":"pending"}');
  assert.equal(a.link.state, 'pending');
  for (let i = 0; i < 14; i++) {
    a.clock.advance(2000);
    a.tr.recvText('{"t":"ping"}');
  }
  a.tr.recvText('{"t":"joined"}');
  assert.equal(a.link.state, 'joined');

  const b = await setup();
  b.tr.recvText('{"t":"pending"}');
  for (let i = 0; i < 15; i++) {
    b.clock.advance(2000);
    b.tr.recvText('{"t":"ping"}');
  }
  assert.equal(b.ends()[0].cause, 'pending-timeout');
});

test('refused and bye end the link with their reason', async () => {
  const a = await setup();
  a.tr.recvText('{"t":"refused","reason":2}');
  assert.deepEqual(a.ends(), [{ cause: 'refused', reason: 2, wasJoined: false }]);
  assert.equal(a.tr.closed, true);
  const b = await setup();
  b.tr.recvText('{"t":"joined"}');
  b.tr.recvText('{"t":"bye","reason":"replaced"}');
  assert.deepEqual(b.ends(), [{ cause: 'bye', reason: 'replaced', wasJoined: true }]);
});

test('death after 17 s without any received frame; any frame resets it', async () => {
  const { clock, tr, ends, link } = await setup();
  tr.recvText('{"t":"joined"}');
  clock.advance(16000);
  tr.recvBinary(encodeChunks(5, new Uint8Array([1, 2, 3]))[0]);
  clock.advance(16000);
  assert.equal(link.lastRxAgo(), 16000);
  assert.deepEqual(ends(), []);
  clock.advance(1000);
  assert.equal(ends()[0].cause, 'death');
  assert.equal(link.lastRxAgo(), 0);
});

test('transport close ends the link once', async () => {
  const { tr, ends, link } = await setup();
  tr.recvText('{"t":"joined"}');
  tr.remoteClose();
  tr.remoteClose();
  link.close();
  assert.deepEqual(ends(), [{ cause: 'transport', reason: undefined, wasJoined: true }]);
  assert.equal(link.state, 'closed');
});

test('binary content is reassembled after joined only', async () => {
  const { tr, events } = await setup();
  const data = new Uint8Array(20000).map((_, i) => i & 0xff);
  for (const f of encodeChunks(9, data)) tr.recvBinary(f);
  tr.recvText('{"t":"joined"}');
  for (const f of encodeChunks(10, data)) tr.recvBinary(f);
  const got = events.filter((e) => e[0] === 'content');
  assert.equal(got.length, 1);
  assert.equal(got[0][1], 10);
  assert.deepEqual(got[0][2], data);
});

test('content chunks go out back to back with backpressure', async () => {
  const { clock, tr, link } = await setup();
  tr.recvText('{"t":"joined"}');
  const n0 = tr.sent.length;
  link.sendMsg({ TypeString: 'Header' });
  tr.bufferedAmount = 0;
  const done = link.sendContent(1001, new Uint8Array(40000));
  // first chunk goes; then the buffer is over the high-water mark
  assert.equal(tr.binaries().length, 3);
  tr.sent.length = n0;
  tr.bufferedAmount = 2 << 20;
  const done2 = link.sendContent(1002, new Uint8Array(20000));
  link.sendMsg({ TypeString: 'After' });
  assert.equal(tr.sent.length, n0, 'nothing sent while over the high-water mark');
  clock.advance(2000); // a ping is queued, not interleaved
  assert.equal(tr.sent.length, n0);
  tr.bufferedAmount = 0;
  clock.advance(20);
  const kinds = tr.sent.slice(n0).map((x) => (typeof x === 'string' ? JSON.parse(x).t + (x.includes('After') ? ':After' : '') : 'bin'));
  assert.deepEqual(kinds, ['bin', 'bin', 'msg:After', 'ping']);
  assert.equal(await done, true);
  assert.equal(await done2, true);
});

test('sendContent before joined or with bad input resolves false', async () => {
  const { tr, link } = await setup();
  assert.equal(await link.sendContent(1, new Uint8Array(3)), false);
  tr.recvText('{"t":"joined"}');
  assert.equal(await link.sendContent(0, new Uint8Array(3)), false);
  assert.equal(await link.sendContent(1, new Uint8Array(131073)), false);
});

test('pending content resolves false when the link ends', async () => {
  const { tr, link } = await setup();
  tr.recvText('{"t":"joined"}');
  tr.bufferedAmount = 2 << 20;
  const p = link.sendContent(3, new Uint8Array(10));
  tr.remoteClose();
  assert.equal(await p, false);
});

test('drain waits for the buffer to empty, max time', async () => {
  const { clock, tr, link } = await setup();
  tr.recvText('{"t":"joined"}');
  tr.bufferedAmount = 100;
  let r = null;
  link.drain(500).then((v) => { r = v; });
  await clock.advanceAsync(100);
  assert.equal(r, null);
  tr.bufferedAmount = 0;
  await clock.advanceAsync(20);
  assert.equal(r, true);
  tr.bufferedAmount = 5;
  let r2 = null;
  link.drain(500).then((v) => { r2 = v; });
  await clock.advanceAsync(520);
  assert.equal(r2, false);
});

test('a link is single use', async () => {
  const { link } = await setup();
  assert.throws(() => link.connect());
});

test('oversized messages are rejected', async () => {
  const { tr, link } = await setup();
  tr.recvText('{"t":"joined"}');
  assert.equal(link.sendMsg({ TypeString: 'X', Text: 'x'.repeat(70000) }), false);
});
