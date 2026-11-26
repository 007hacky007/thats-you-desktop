// Connection policy of createGame (contract 5.4) driven through a fake transport.

import { test } from 'node:test';
import assert from 'node:assert/strict';

import { createHarness, PARAMS, START, jpeg } from './harness.mjs';
import { createMemoryBackend } from '../../js/util/storage.js';
import { createFakeClock, flush } from '../../js/util/clock.js';

const UIDRE = /^00000000-0000-4000-8000-0+1$/;

test('boot without params: noparams, or idle with a saved join', async () => {
  const h = createHarness();
  h.game.boot(null);
  assert.deepEqual(h.view.data, { state: 'noparams' });
  h.game.actions.play();
  assert.equal(h.factory.created.length, 0);

  const backend = createMemoryBackend({ 'iwyk.lastJoin': JSON.stringify({ ...PARAMS, at: 1, active: false }) });
  const h2 = createHarness({ backend });
  h2.game.boot(null);
  assert.equal(h2.view.screen, 'connect');
  assert.deepEqual(h2.view.data, { state: 'idle' });
  await h2.play();
  assert.equal(h2.factory.created.length, 1);
  assert.deepEqual(h2.tr().params, PARAMS);
});

test('first connect: ready, connecting, waiting, joined; handshake messages exact', async () => {
  const h = createHarness();
  await h.boot();
  assert.deepEqual(h.view, { screen: 'connect', variant: '', data: { state: 'ready' }, seq: 1 });
  await h.play();
  assert.deepEqual(h.view.data, { state: 'connecting' });
  await h.open();
  const hello = JSON.parse(h.tr().sent[0]);
  assert.equal(hello.t, 'hello');
  assert.equal(hello.token, PARAMS.t);
  assert.match(hello.uid, UIDRE);
  assert.equal(h.game.uid, hello.uid);
  h.frame({ t: 'pending' });
  assert.deepEqual(h.view.data, { state: 'waiting' });
  h.frame({ t: 'joined' });
  assert.equal(h.game.link.state, 'joined');
  assert.deepEqual(h.sent(), [], 'no msg before the version and session');
  h.g2p({ TypeString: 'SessionStateMessage', SessionID: 77 });
  h.g2p({ TypeString: 'SessionStateMessage', SessionID: 78 });
  assert.deepEqual(h.sent(), []);
  h.g2p({ TypeString: 'InterfaceVersionMessage', InterfaceVersion: '2016-07-12-1645' });
  assert.deepEqual(h.tr().texts().filter((s) => s.includes('"msg"')),
    ['{"t":"msg","id":-7503,"json":{"TypeString":"ClientRequestPlayerIDMessage","UID":"' + hello.uid + '"}}']);
  assert.equal(h.backend.getItem('iwyk.session'), '77');
  h.g2p({ TypeString: 'AssignPlayerIDAndSlotMessage', PlayerID: 2, SlotID: 1, UDPPortOffset: 0, DisplayName: '', PSNID: '' });
  assert.equal(h.at, 'joined');
  assert.deepEqual(h.view.data, { playerId: 2, slotId: 1 });
  assert.equal(h.game.state.playerId, 2);
  assert.equal(h.game.state.phase, 'lobby');
  assert.equal(h.game.state.connected, true);
  // repeated assignment updates the slot without leaving the current view
  h.cmd(14);
  h.g2p({ TypeString: 'AssignPlayerIDAndSlotMessage', PlayerID: 2, SlotID: 0, UDPPortOffset: 0, DisplayName: '', PSNID: '' });
  assert.equal(h.at, 'continue:start');
  assert.equal(h.game.state.slotId, 0);
  const last = JSON.parse(h.backend.getItem('iwyk.lastJoin'));
  assert.equal(last.active, true);
  assert.equal(last.at, START);
  assert.equal(h.backend.getItem('iwyk.playerId'), '2');
});

test('the first assignment does not replace a view set by a command', async () => {
  const h = createHarness();
  await h.boot();
  await h.play();
  await h.open();
  h.frame({ t: 'joined' });
  h.g2p({ TypeString: 'InterfaceVersionMessage', InterfaceVersion: '2016-07-12-1645' });
  h.g2p({ TypeString: 'SessionStateMessage', SessionID: 5 });
  h.cmd(4);
  h.g2p({ TypeString: 'AssignPlayerIDAndSlotMessage', PlayerID: 2, SlotID: 0, UDPPortOffset: 0, DisplayName: '', PSNID: '' });
  assert.equal(h.at, 'continue:continue');
});

test('reload rejoin: same QR, active and younger than 6 h connects at once', async () => {
  const fresh = { ...PARAMS, at: START - 1000, active: true };
  const h = createHarness({ backend: createMemoryBackend({ 'iwyk.lastJoin': JSON.stringify(fresh) }) });
  await h.boot();
  assert.equal(h.factory.created.length, 1);
  assert.deepEqual(h.view.data, { state: 'connecting' });

  for (const last of [
    { ...PARAMS, at: START - 6 * 3600 * 1000, active: true },
    { ...PARAMS, at: START - 1000, active: false },
    { ...PARAMS, t: 'OTHER', at: START - 1000, active: true },
  ]) {
    const h2 = createHarness({ backend: createMemoryBackend({ 'iwyk.lastJoin': JSON.stringify(last) }) });
    await h2.boot();
    assert.equal(h2.factory.created.length, 0);
    assert.deepEqual(h2.view.data, { state: 'ready' });
  }
});

test('session restore keeps the profile for the same SessionID', async () => {
  const backend = createMemoryBackend({
    'iwyk.session': '77', 'iwyk.playerId': '5', 'iwyk.portraitId': 'pg-1', 'iwyk.portrait': '/9j/AQI=',
    'iwyk.card': 'CARD_4', 'iwyk.name': 'Ann', 'iwyk.guest': '0', 'iwyk.profileDone': '1',
  });
  const h = createHarness({ backend });
  await h.start({ sessionId: 77 });
  assert.deepEqual({ ...h.game.state.profile, portraitUrl: !!h.game.state.profile.portraitUrl },
    { name: 'Ann', cardId: 'CARD_4', isGuest: false, portraitUrl: true });
  assert.ok(h.game.resources.has('pg-1'));

  const backend2 = createMemoryBackend({
    'iwyk.session': '77', 'iwyk.playerId': '5', 'iwyk.portraitId': 'pg-1', 'iwyk.portrait': '/9j/AQI=',
    'iwyk.card': 'CARD_4', 'iwyk.name': 'Ann', 'iwyk.guest': '1', 'iwyk.profileDone': '1',
  });
  const h2 = createHarness({ backend: backend2 });
  await h2.start({ sessionId: 99 });
  assert.equal(backend2.getItem('iwyk.portrait'), null);
  assert.equal(backend2.getItem('iwyk.profileDone'), null);
  assert.equal(backend2.getItem('iwyk.portraitId'), null);
  assert.equal(backend2.getItem('iwyk.name'), 'Ann');
  assert.equal(backend2.getItem('iwyk.session'), '99');
  assert.deepEqual(h2.game.state.profile, { name: 'Ann', cardId: 'CARD_4', isGuest: true, portraitUrl: null });
});

test('refusals show their popup and never reconnect', async () => {
  for (const [reason, popup] of [[1, 'notallowed'], [2, 'gamefull'], [3, 'onlyone'], [9, 'failed']]) {
    const h = createHarness();
    await h.boot();
    await h.play();
    await h.open();
    h.frame({ t: 'refused', reason });
    assert.deepEqual(h.game.popup, { reason: popup, reconnect: false });
    assert.deepEqual(h.view.data, { state: 'ready' });
    assert.equal(h.tr().closed, true);
    await h.tick(200000);
    assert.equal(h.factory.created.length, 1);
    h.game.actions.dismissPopup();
    assert.equal(h.game.popup, null);
  }
});

test('refusal after a join marks lastJoin inactive', async () => {
  const h = createHarness();
  await h.start();
  h.tr().remoteClose();
  await h.tick(0);
  assert.equal(h.factory.created.length, 2);
  await h.open();
  h.frame({ t: 'refused', reason: 1 });
  assert.equal(JSON.parse(h.backend.getItem('iwyk.lastJoin')).active, false);
  assert.deepEqual(h.game.overlays, []);
  assert.deepEqual(h.game.popup, { reason: 'notallowed', reconnect: false });
});

test('bye replaced: popup with reconnect, no automatic reconnect; reconnect() joins again', async () => {
  const h = createHarness();
  await h.start();
  h.frame({ t: 'bye', reason: 'replaced' });
  assert.deepEqual(h.game.popup, { reason: 'replaced', reconnect: true });
  assert.equal(h.at, 'connect');
  assert.equal(JSON.parse(h.backend.getItem('iwyk.lastJoin')).active, false);
  await h.tick(200000);
  assert.equal(h.factory.created.length, 1);
  h.game.actions.reconnect();
  assert.equal(h.factory.created.length, 2);
  assert.equal(h.game.popup, null);
});

test('bye shutdown shows its popup', async () => {
  const h = createHarness();
  await h.start();
  h.frame({ t: 'bye', reason: 'shutdown' });
  assert.deepEqual(h.game.popup, { reason: 'shutdown', reconnect: false });
});

test('DisconnectCommandMessage reasons', async () => {
  const table = [
    [2, 'gamefull', false], [5, 'version', false], [6, 'onlyone', false], [7, 'notallowed', false],
    [3, 'failed', true], [0, 'failed', false], [1, 'failed', false], [4, 'failed', false], [42, 'failed', false],
  ];
  for (const [msg, reason, reconnect] of table) {
    const h = createHarness();
    await h.start();
    h.cmd(29);
    h.g2p({ TypeString: 'DisconnectCommandMessage', Msg: msg });
    assert.deepEqual(h.game.popup, { reason, reconnect }, 'Msg ' + msg);
    assert.equal(h.at, 'connect');
    assert.deepEqual(h.game.overlays, []);
    assert.equal(h.tr().closed, true);
    assert.equal(JSON.parse(h.backend.getItem('iwyk.lastJoin')).active, false);
    await h.tick(200000);
    assert.equal(h.factory.created.length, 1);
  }
});

test('interface version mismatch closes with popup version', async () => {
  const h = createHarness();
  await h.boot();
  await h.play();
  await h.open();
  h.frame({ t: 'joined' });
  h.g2p({ TypeString: 'InterfaceVersionMessage', InterfaceVersion: '2099-01-01-0000' });
  assert.deepEqual(h.game.popup, { reason: 'version', reconnect: false });
  assert.equal(h.tr().closed, true);
  assert.deepEqual(h.sent(), []);
});

test('first connect timeouts show nogame', async () => {
  // channel never opens
  let h = createHarness();
  await h.boot();
  await h.play();
  await h.tick(17000);
  assert.deepEqual(h.game.popup, { reason: 'nogame', reconnect: false });
  // no reply to hello
  h = createHarness();
  await h.boot();
  await h.play();
  await h.open();
  await h.tick(17000);
  assert.equal(h.game.popup.reason, 'nogame');
  // pending for 30 s
  h = createHarness();
  await h.boot();
  await h.play();
  await h.open();
  h.frame({ t: 'pending' });
  for (let i = 0; i < 15; i++) {
    await h.tick(2000);
    h.frame({ t: 'ping' });
  }
  assert.equal(h.game.popup.reason, 'nogame');
  // joined, but no version/session within 17 s
  h = createHarness();
  await h.boot();
  await h.play();
  await h.open();
  h.frame({ t: 'joined' });
  for (let i = 0; i < 8; i++) {
    await h.tick(2000);
    h.frame({ t: 'ping' });
  }
  assert.equal(h.game.popup, null);
  await h.tick(1000);
  assert.deepEqual(h.game.popup, { reason: 'failed', reconnect: true }, 'after joined the game exists');
});

test('silent closed lobby: no assignment within 20 s', async () => {
  const h = createHarness();
  await h.boot();
  await h.play();
  await h.open();
  h.frame({ t: 'joined' });
  h.g2p({ TypeString: 'InterfaceVersionMessage', InterfaceVersion: '2016-07-12-1645' });
  h.g2p({ TypeString: 'SessionStateMessage', SessionID: 3 });
  for (let i = 0; i < 9; i++) {
    await h.tick(2000);
    h.frame({ t: 'ping' });
  }
  assert.equal(h.game.popup, null);
  await h.tick(2000);
  assert.deepEqual(h.game.popup, { reason: 'failed', reconnect: true });
  assert.equal(h.at, 'connect');
});

test('transport loss: reconnecting overlay, backoff 0 1 2 4 8 then 15 s, give up at 120 s', async () => {
  const h = createHarness();
  await h.start();
  h.cmd(4);
  const t0 = h.clock.now();
  const times = [];
  h.factory.onCreate = (t) => {
    times.push(h.clock.now() - t0);
    t.fail();
  };
  h.tr().remoteClose();
  await h.tick(0);
  assert.deepEqual(h.game.overlays.map((o) => o.id), ['reconnecting']);
  assert.equal(h.at, 'continue:continue', 'the screen stays under the overlay');
  assert.equal(h.game.state.connected, false);
  await h.tick(119999);
  assert.deepEqual(times, [0, 1000, 3000, 7000, 15000, 30000, 45000, 60000, 75000, 90000, 105000]);
  assert.equal(h.game.popup, null);
  await h.tick(1);
  assert.deepEqual(h.game.popup, { reason: 'failed', reconnect: true });
  assert.deepEqual(h.game.overlays, []);
  assert.deepEqual(h.view.data, { state: 'ready' });
  await h.tick(100000);
  assert.equal(times.length, 11);
});

test('reconnect succeeds: overlay removed on assignment, same uid', async () => {
  const h = createHarness();
  await h.start();
  const uid = h.game.uid;
  h.cmd(5);
  h.g2p({ TypeString: 'ClientHoldingScreenCommandMessage', action: 5, time: 0, HoldingScreenText: '', HoldingScreenType: 4, OtherPlayerIndex: 0, ShowPortraitPhotoControls: true });
  h.tr().remoteClose();
  await h.tick(0);
  assert.equal(h.factory.created.length, 2);
  await h.join({ playerId: 2, slotId: 0 });
  assert.equal(JSON.parse(h.tr().sent[0]).uid, uid);
  assert.deepEqual(h.game.overlays, []);
  assert.equal(h.at, 'joined');
  assert.equal(h.game.state.connected, true);
});

test('death after 17 s of silence and bye closed both reconnect', async () => {
  const h = createHarness();
  await h.start();
  for (let i = 0; i < 8; i++) await h.tick(2000); // our pings go out, nothing comes back
  assert.equal(h.factory.created.length, 1);
  assert.ok(h.game.link.lastRxAgo() >= 16000);
  await h.tick(1000);
  assert.equal(h.factory.created.length, 2);
  assert.deepEqual(h.game.overlays.map((o) => o.id), ['reconnecting']);

  const h2 = createHarness();
  await h2.start();
  h2.frame({ t: 'bye', reason: 'closed' });
  await h2.tick(0);
  assert.equal(h2.factory.created.length, 2);
  assert.equal(h2.game.popup, null);
});

test('pause stays across a reconnect and is cleared on disconnect', async () => {
  const h = createHarness();
  await h.start();
  h.cmd(29);
  h.tr().remoteClose();
  await h.tick(0);
  assert.deepEqual(h.game.overlays.map((o) => o.id), ['pause', 'reconnecting']);
  h.g2p; // second transport not opened: give up path clears everything
  await h.tick(120000);
  assert.deepEqual(h.game.overlays, []);
});

test('wake during the backoff wait reconnects at once', async () => {
  const h = createHarness();
  await h.start();
  let created = 0;
  h.factory.onCreate = (t) => {
    created++;
    t.fail();
  };
  h.tr().remoteClose();
  await h.tick(0); // attempt 1 at 0 fails, next at +1 s
  await h.tick(1000); // attempt 2 fails, next at +2 s
  assert.equal(created, 2);
  h.game.wake();
  await flush();
  assert.equal(created, 3);
});

test('leave sends ClientDisconnectMessage, drains and closes', async () => {
  const h = createHarness();
  await h.start();
  h.tr().bufferedAmount = 10;
  const p = h.game.actions.leave();
  assert.deepEqual(h.takeTypes(), ['ClientDisconnectMessage']);
  assert.equal(h.tr().closed, false);
  await h.tick(100);
  h.tr().bufferedAmount = 0;
  await h.tick(20);
  await p;
  assert.equal(h.tr().closed, true);
  assert.deepEqual(h.view.data, { state: 'ready' });
  assert.equal(h.game.popup, null);
  assert.equal(JSON.parse(h.backend.getItem('iwyk.lastJoin')).active, false);
  await h.tick(200000);
  assert.equal(h.factory.created.length, 1);
});

test('leave gives up draining after 500 ms', async () => {
  const h = createHarness();
  await h.start();
  h.tr().bufferedAmount = 10;
  let done = false;
  h.game.actions.leave().then(() => { done = true; });
  await h.tick(480);
  assert.equal(done, false);
  await h.tick(40);
  assert.equal(done, true);
  assert.equal(h.tr().closed, true);
});

test('invalid messages are dropped', async () => {
  const h = createHarness();
  await h.boot();
  await h.play();
  await h.open();
  h.frame({ t: 'joined' });
  h.g2p({ TypeString: 'InterfaceVersionMessage', InterfaceVersion: '2016-07-12-1645' });
  h.g2p({ TypeString: 'SessionStateMessage', SessionID: 0 });
  assert.deepEqual(h.sent(), []);
  h.g2p({ TypeString: 'SessionStateMessage', SessionID: 1 });
  assert.deepEqual(h.types(), ['ClientRequestPlayerIDMessage']);
  h.g2p({ TypeString: 'AssignPlayerIDAndSlotMessage', PlayerID: -1, SlotID: 0, UDPPortOffset: 0 });
  assert.equal(h.view.screen, 'connect');
});

test('downloads: image and thumbnail headers with binary in both orders', async () => {
  const h = createHarness();
  await h.start();
  const img = jpeg(30000);
  h.g2p({ TypeString: 'ImageResourceContentTransferMessage', TransferID: 11, ImageGUID: 'q-1', Acknowledge: true });
  h.content(11, img);
  assert.ok(h.game.resources.url('q-1').startsWith('blob:'));
  h.content(12, jpeg(50));
  assert.equal(h.game.thumbnails.url('client-a'), null);
  h.g2p({ TypeString: 'PortraitThumbnailContentTransferMessage', TransferID: 12, ClientUID: 'client-a' });
  assert.ok(h.game.thumbnails.url('client-a'));
});

test('resource handshake through the game', async () => {
  const h = createHarness();
  await h.start();
  const added = [];
  h.game.resources.on('added', (g) => added.push(g));
  h.g2p({ TypeString: 'ResourceRequirementsMessage', Requirements: [] });
  assert.deepEqual(h.take(), [{ TypeString: 'AllResourcesReceivedMessage', Requirements: [] }]);
  h.g2p({ TypeString: 'ResourceRequirementsMessage', Requirements: [{ GUID: 'p-1', Rqrmnt: 1 }, { GUID: 'p-2', Rqrmnt: 1 }] });
  assert.deepEqual(h.take(), [
    { TypeString: 'RequestResourceMessage', ResourceGUID: 'p-1' },
    { TypeString: 'RequestResourceMessage', ResourceGUID: 'p-2' },
  ]);
  h.g2p({ TypeString: 'ImageResourceContentTransferMessage', TransferID: 1, ImageGUID: 'p-1', Acknowledge: true });
  h.content(1, jpeg(10));
  assert.deepEqual(h.take(), []);
  h.content(2, jpeg(10));
  h.g2p({ TypeString: 'ImageResourceContentTransferMessage', TransferID: 2, ImageGUID: 'p-2', Acknowledge: true });
  assert.deepEqual(h.take(), [{
    TypeString: 'AllResourcesReceivedMessage',
    Requirements: [{ GUID: 'p-1', Rqrmnt: 1 }, { GUID: 'p-2', Rqrmnt: 1 }],
  }]);
  assert.deepEqual(added, ['p-1', 'p-2']);
});

test('lastJoin.at is refreshed while joined', async () => {
  const clock = createFakeClock(START);
  const h = createHarness({ clock });
  await h.start();
  for (let i = 0; i < 31; i++) {
    await h.tick(2000);
    h.frame({ t: 'ping' });
  }
  h.g2p({ TypeString: 'JoinLeaveMessage' });
  assert.ok(JSON.parse(h.backend.getItem('iwyk.lastJoin')).at >= START + 60000);
});
