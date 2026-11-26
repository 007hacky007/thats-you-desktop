import { test } from 'node:test';
import assert from 'node:assert/strict';

import {
  helloFrame, pingFrame, msgFrame, parseTextFrame, encodeChunks, createReassembler, decodeChunkHeader,
  utf8Length, joinedFrame, refusedFrame, byeFrame, pendingFrame, CHUNK,
} from '../../js/net/frames.js';

test('text frame builders have the exact forms of 2.1', () => {
  assert.equal(helloFrame('tok', 'uid-1'), '{"t":"hello","token":"tok","uid":"uid-1"}');
  assert.equal(pingFrame(), '{"t":"ping"}');
  assert.equal(msgFrame({ TypeString: 'ClientDisconnectMessage' }),
    '{"t":"msg","id":-7503,"json":{"TypeString":"ClientDisconnectMessage"}}');
  assert.equal(joinedFrame(), '{"t":"joined"}');
  assert.equal(pendingFrame(), '{"t":"pending"}');
  assert.equal(refusedFrame(2), '{"t":"refused","reason":2}');
  assert.equal(byeFrame('replaced'), '{"t":"bye","reason":"replaced"}');
});

test('parseTextFrame', () => {
  assert.deepEqual(parseTextFrame('{"t":"joined"}'), { t: 'joined' });
  assert.deepEqual(parseTextFrame('{"t":"pending","x":1}'), { t: 'pending' });
  assert.deepEqual(parseTextFrame('{"t":"ping"}'), { t: 'ping' });
  assert.deepEqual(parseTextFrame('{"t":"refused","reason":3}'), { t: 'refused', reason: 3 });
  assert.deepEqual(parseTextFrame('{"t":"refused"}'), { t: 'refused', reason: 0 });
  assert.deepEqual(parseTextFrame('{"t":"bye","reason":"closed"}'), { t: 'bye', reason: 'closed' });
  assert.deepEqual(parseTextFrame('{"t":"msg","id":-7503,"json":{"TypeString":"X","a":1}}'),
    { t: 'msg', json: { TypeString: 'X', a: 1 } });
  assert.equal(parseTextFrame('{"t":"msg","id":5,"json":{}}'), null);
  assert.equal(parseTextFrame('{"t":"msg","id":-7503,"json":"{}"}'), null);
  assert.equal(parseTextFrame('{"t":"msg","id":-7503,"json":[1]}'), null);
  assert.deepEqual(parseTextFrame('{"t":"future"}'), { t: 'future', unknown: true });
  assert.equal(parseTextFrame('nope'), null);
  assert.equal(parseTextFrame('[1]'), null);
  assert.equal(parseTextFrame('{"x":1}'), null);
  assert.equal(parseTextFrame(''), null);
  assert.equal(parseTextFrame('{"t":"ping","pad":"' + 'x'.repeat(65536) + '"}'), null);
});

test('utf8Length counts bytes', () => {
  assert.equal(utf8Length('a\u00e9\u20ac'), 6);
});

function bytes(n, seed = 1) {
  const b = new Uint8Array(n);
  for (let i = 0; i < n; i++) b[i] = (i * 31 + seed) & 0xff;
  return b;
}

test('encodeChunks writes exact little-endian headers', () => {
  const frames = encodeChunks(0x01020304, bytes(CHUNK * 2 + 5));
  assert.equal(frames.length, 3);
  assert.deepEqual([...frames[0].subarray(0, 12)], [4, 3, 2, 1, 5, 0x80, 0, 0, 0, 0, 0, 0]);
  assert.deepEqual([...frames[1].subarray(8, 12)], [0, 0x40, 0, 0]);
  assert.deepEqual([...frames[2].subarray(8, 12)], [0, 0x80, 0, 0]);
  assert.equal(frames[2].length, 12 + 5);
  assert.equal(frames[0].length, 12 + CHUNK);
  assert.throws(() => encodeChunks(0, bytes(1)));
  assert.throws(() => encodeChunks(2147483648, bytes(1)));
  assert.throws(() => encodeChunks(1, new Uint8Array(0)));
  assert.throws(() => encodeChunks(1, bytes(131073)));
  assert.equal(encodeChunks(1, bytes(131072)).length, 8);
});

test('reassembler round-trips chunked content, interleaved transfers allowed', () => {
  const r = createReassembler();
  const a = bytes(40000, 1);
  const b = bytes(100, 2);
  const fa = encodeChunks(7, a);
  const fb = encodeChunks(8, b);
  assert.deepEqual(r.push(fa[0]), { done: false });
  const rb = r.push(fb[0]);
  assert.equal(rb.done, true);
  assert.equal(rb.transferId, 8);
  assert.deepEqual(rb.data, b);
  r.push(fa[1]);
  const ra = r.push(fa[2]);
  assert.equal(ra.transferId, 7);
  assert.deepEqual(ra.data, a);
  assert.equal(r.partialCount, 0);
});

function frame(id, total, offset, n) {
  const f = new Uint8Array(12 + n);
  const dv = new DataView(f.buffer);
  dv.setInt32(0, id, true);
  dv.setUint32(4, total, true);
  dv.setUint32(8, offset, true);
  return f;
}

test('reassembler enforces every rule of 2.2', () => {
  const warns = [];
  const log = { warn: (m) => warns.push(m) };
  const cases = [
    ['short frame', [new Uint8Array(11)]],
    ['bad transfer id', [frame(0, 10, 0, 10)]],
    ['bad transfer id', [frame(-1, 10, 0, 10)]],
    ['bad total length', [frame(1, 0, 0, 1)]],
    ['bad total length', [frame(1, 131073, 0, 10)]],
    ['bad chunk size', [frame(1, 10, 0, 0)]],
    ['bad chunk size', [frame(1, 20000, 0, 16385)]],
    ['chunk past end', [frame(1, 10, 0, 11)]],
    ['offset gap', [frame(1, 100, 10, 10)]],
    ['offset gap', [frame(1, 100, 0, 10), frame(1, 100, 20, 10)]],
    ['total length changed', [frame(1, 100, 0, 10), frame(1, 200, 10, 10)]],
    ['too many partial transfers', [frame(1, 100, 0, 1), frame(2, 100, 0, 1), frame(3, 100, 0, 1),
      frame(4, 100, 0, 1), frame(5, 100, 0, 1)]],
  ];
  for (const [why, frames] of cases) {
    const r = createReassembler({ log });
    let last;
    for (const f of frames) last = r.push(f);
    assert.equal(last.error, why, why);
  }
  assert.ok(warns.length >= cases.length);
});

test('reassembler drops a broken transfer and recovers', () => {
  const r = createReassembler();
  r.push(frame(1, 100, 0, 10));
  assert.equal(r.push(frame(1, 100, 50, 10)).error, 'offset gap');
  assert.equal(r.partialCount, 0);
  const ok = encodeChunks(1, bytes(30));
  assert.equal(r.push(ok[0]).done, true);
  const hd = decodeChunkHeader(ok[0]);
  assert.equal(hd.transferId, 1);
  assert.equal(hd.total, 30);
});
