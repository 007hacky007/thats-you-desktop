import { test } from 'node:test';
import assert from 'node:assert/strict';

import { createBlobStore, createDownloads, createUploads, defaultUuid, MAX_UPLOAD } from '../../js/net/content.js';
import { createResources } from '../../js/net/resources.js';
import { createFakeClock } from '../../js/util/clock.js';

function fakeUrls() {
  let n = 0;
  const revoked = [];
  return {
    makeUrl: () => 'blob:test/' + (++n),
    revokeUrl: (u) => revoked.push(u),
    revoked,
  };
}

test('blob store: put, url, replace, remove, events', () => {
  const u = fakeUrls();
  const s = createBlobStore({ ...u });
  const added = [];
  s.on('added', (k) => added.push(k));
  assert.equal(s.put('g1', new Uint8Array([1])), 'blob:test/1');
  assert.equal(s.url('g1'), 'blob:test/1');
  assert.equal(s.url('nope'), null);
  assert.equal(s.put('g1', new Uint8Array([2])), 'blob:test/2');
  assert.deepEqual(u.revoked, ['blob:test/1']);
  assert.ok(s.has('g1'));
  assert.equal(s.remove('g1'), true);
  assert.equal(s.remove('g1'), false);
  assert.deepEqual(u.revoked, ['blob:test/1', 'blob:test/2']);
  assert.deepEqual(added, ['g1', 'g1']);
});

test('blob store: LRU eviction skips required entries', () => {
  const u = fakeUrls();
  const s = createBlobStore({ capacity: 3, ...u });
  s.put('a', new Uint8Array(1));
  s.put('b', new Uint8Array(1));
  s.put('c', new Uint8Array(1));
  s.setRequired(['a']);
  s.url('b'); // b becomes most recently used
  s.put('d', new Uint8Array(1));
  assert.deepEqual(s.keys().sort(), ['a', 'b', 'd']);
  s.setRequired(['a', 'b', 'd']);
  s.put('e', new Uint8Array(1));
  assert.deepEqual(s.keys().sort(), ['a', 'b', 'd', 'e']);
  s.setRequired([]);
  assert.equal(s.size, 3);
  assert.ok(!s.has('a'));
});

test('blob store: default URLs work in Node', () => {
  const s = createBlobStore();
  const url = s.put('x', new Uint8Array([0xff, 0xd8]));
  assert.ok(url === null || url.startsWith('blob:'));
  s.clear();
  assert.equal(s.size, 0);
});

test('downloads pair header and binary in both orders', () => {
  const clock = createFakeClock();
  const got = [];
  const d = createDownloads({
    clock,
    onImage: (g, b) => got.push(['img', g, b.length]),
    onThumbnail: (u, b) => got.push(['thumb', u, b.length]),
  });
  d.header({ TypeString: 'ImageResourceContentTransferMessage', TransferID: 5, ImageGUID: 'g5', Acknowledge: true });
  d.binary(5, new Uint8Array(3));
  d.binary(6, new Uint8Array(4));
  clock.advance(9999);
  d.header({ TypeString: 'PortraitThumbnailContentTransferMessage', TransferID: 6, ClientUID: 'u6' });
  d.binary(7, new Uint8Array(1));
  clock.advance(10000);
  d.header({ TypeString: 'ImageResourceContentTransferMessage', TransferID: 7, ImageGUID: 'g7' });
  assert.deepEqual(got, [['img', 'g5', 3], ['thumb', 'u6', 4]]);
  assert.equal(d.pending, 1);
  d.reset();
  assert.equal(d.pending, 0);
  assert.equal(d.header({ TypeString: 'Other' }), false);
});

function fakeLink() {
  const sent = [];
  return {
    state: 'joined',
    sent,
    sendMsg: (m) => { sent.push(JSON.stringify(m)); return true; },
    sendContent: (tid, b) => { sent.push('bin:' + tid + ':' + b.length); return Promise.resolve(true); },
  };
}

test('upload sequences and transfer ids from a random start', () => {
  const link = fakeLink();
  const store = createBlobStore({ ...fakeUrls() });
  let g = 0;
  const up = createUploads({ getLink: () => link, store, random: () => 0.5, uuid: () => 'guid-' + (++g) });
  assert.equal(up.peekTransferId(), 1000 + 499500);
  const jpeg = new Uint8Array(10);
  const p = up.photo({ jpeg, usesFX: true, imgType: 3, usePic: true });
  assert.equal(p.guid, 'guid-1');
  assert.equal(p.transferId, 500500);
  up.drawing({ jpeg, coloursAvailable: 32766, coloursUsed: 2, imgType: 5 });
  up.portrait({ jpeg, usesFX: false, profile: { TypeString: 'ClientPlayerProfileMessage' } });
  up.photo({ jpeg, usesFX: false, imgType: 4 });
  assert.deepEqual(link.sent, [
    '{"TypeString":"ClientImageResourceContentTransferMessage","TransferID":500500,"ImageGUID":"guid-1","ImgType":3}',
    'bin:500500:10',
    '{"TypeString":"TrophyPhotoFXInfoMessage","ImageGUID":"guid-1","usesFX":true}',
    '{"TypeString":"UsePicPressedResponseMessage","Response":4}',
    '{"TypeString":"ClientImageResourceContentTransferMessage","TransferID":500501,"ImageGUID":"guid-2","ImgType":5}',
    'bin:500501:10',
    '{"TypeString":"TrophyDrawingColouringInfoMessage","ImageGUID":"guid-2","ColoursAvailable":32766,"ColoursUsed":2}',
    '{"TypeString":"ClientImageResourceContentTransferMessage","TransferID":500502,"ImageGUID":"guid-3","ImgType":2}',
    'bin:500502:10',
    '{"TypeString":"TrophyPhotoFXInfoMessage","ImageGUID":"guid-3","usesFX":false}',
    '{"TypeString":"ClientPlayerProfileMessage"}',
    '{"TypeString":"ClientImageResourceContentTransferMessage","TransferID":500503,"ImageGUID":"guid-4","ImgType":4}',
    'bin:500503:10',
    '{"TypeString":"TrophyPhotoFXInfoMessage","ImageGUID":"guid-4","usesFX":false}',
  ]);
  // own uploads are registered
  for (const k of ['guid-1', 'guid-2', 'guid-3', 'guid-4']) assert.ok(store.has(k));
  assert.ok(store.isOwn('guid-1'));
});

test('transfer id range bounds', () => {
  const store = createBlobStore({ ...fakeUrls() });
  const lo = createUploads({ getLink: () => null, store, random: () => 0 });
  const hi = createUploads({ getLink: () => null, store, random: () => 0.9999999999 });
  assert.equal(lo.peekTransferId(), 1000);
  assert.equal(hi.peekTransferId(), 999999);
});

test('uploads over 128000 bytes, empty or offline are rejected', () => {
  const link = fakeLink();
  const store = createBlobStore({ ...fakeUrls() });
  const up = createUploads({ getLink: () => link, store });
  assert.equal(up.photo({ jpeg: new Uint8Array(MAX_UPLOAD + 1), usesFX: false, imgType: 4 }), null);
  assert.equal(up.photo({ jpeg: new Uint8Array(0), usesFX: false, imgType: 4 }), null);
  assert.equal(up.photo({ jpeg: null, usesFX: false, imgType: 4 }), null);
  assert.ok(up.photo({ jpeg: new Uint8Array(MAX_UPLOAD), usesFX: false, imgType: 4 }));
  link.state = 'closed';
  assert.equal(up.drawing({ jpeg: new Uint8Array(5), coloursAvailable: 0, coloursUsed: 0 }), null);
  assert.equal(link.sent.length, 3);
});

test('defaultUuid is a v4 UUID', () => {
  assert.match(defaultUuid(), /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/);
});

function resSetup() {
  const clock = createFakeClock(0);
  const store = createBlobStore({ ...fakeUrls() });
  const sent = [];
  let online = true;
  const res = createResources({ store, clock, sendMsg: (m) => { if (online) sent.push(JSON.stringify(m)); return online; } });
  return { clock, store, sent, res, setOnline: (v) => { online = v; } };
}

test('empty manifest is answered at once', () => {
  const { sent, res } = resSetup();
  res.onManifest({ TypeString: 'ResourceRequirementsMessage', Requirements: [] });
  assert.deepEqual(sent, ['{"TypeString":"AllResourcesReceivedMessage","Requirements":[]}']);
});

test('manifest: request missing, confirm when all held, ignore SafeToUnload', () => {
  const { clock, store, sent, res } = resSetup();
  store.put('have', new Uint8Array(1));
  store.put('old', new Uint8Array(1));
  store.put('keep', new Uint8Array(1));
  res.onManifest({ Requirements: [
    { GUID: 'have', Rqrmnt: 1 }, { GUID: 'a', Rqrmnt: 1 }, { GUID: 'b', Rqrmnt: 1 },
    { GUID: 'old', Rqrmnt: 2 }, { GUID: 'keep', Rqrmnt: 0 }, { GUID: '', Rqrmnt: 1 }, null,
  ] });
  assert.deepEqual(sent, [
    '{"TypeString":"RequestResourceMessage","ResourceGUID":"a"}',
    '{"TypeString":"RequestResourceMessage","ResourceGUID":"b"}',
  ]);
  assert.ok(!store.has('old'));
  assert.ok(store.has('keep'));
  sent.length = 0;
  store.put('a', new Uint8Array(1));
  assert.deepEqual(sent, []); // b not yet held, requested < 5 s ago
  clock.advance(5000);
  store.put('unrelated', new Uint8Array(1));
  assert.deepEqual(sent, ['{"TypeString":"RequestResourceMessage","ResourceGUID":"b"}']);
  sent.length = 0;
  store.put('b', new Uint8Array(1));
  assert.deepEqual(sent, [
    '{"TypeString":"AllResourcesReceivedMessage","Requirements":[{"GUID":"have","Rqrmnt":1},{"GUID":"a","Rqrmnt":1},{"GUID":"b","Rqrmnt":1}]}',
  ]);
  sent.length = 0;
  store.put('c', new Uint8Array(1));
  assert.deepEqual(sent, [], 'confirmed manifests are not answered twice');
  // a resent manifest is answered again
  res.onManifest({ Requirements: [{ GUID: 'a', Rqrmnt: 1 }] });
  assert.deepEqual(sent, ['{"TypeString":"AllResourcesReceivedMessage","Requirements":[{"GUID":"a","Rqrmnt":1}]}']);
});

test('manifest: SafeToUnload that is also Required is kept; required protects from LRU', () => {
  const { store, res } = resSetup();
  store.put('x', new Uint8Array(1));
  res.onManifest({ Requirements: [{ GUID: 'x', Rqrmnt: 2 }, { GUID: 'x', Rqrmnt: 1 }] });
  assert.ok(store.has('x'));
});

test('manifest: a resent manifest re-requests after 5 s; offline sends are retried', () => {
  const { clock, sent, res, setOnline } = resSetup();
  setOnline(false);
  res.onManifest({ Requirements: [{ GUID: 'z', Rqrmnt: 1 }] });
  assert.deepEqual(sent, []);
  setOnline(true);
  res.onManifest({ Requirements: [{ GUID: 'z', Rqrmnt: 1 }] });
  assert.equal(sent.length, 1);
  clock.advance(1000);
  res.onManifest({ Requirements: [{ GUID: 'z', Rqrmnt: 1 }] });
  assert.equal(sent.length, 1);
  clock.advance(4000);
  res.onManifest({ Requirements: [{ GUID: 'z', Rqrmnt: 1 }] });
  assert.equal(sent.length, 2);
  res.reset();
  res.onManifest({ Requirements: [{ GUID: 'z', Rqrmnt: 1 }] });
  assert.equal(sent.length, 3);
});
