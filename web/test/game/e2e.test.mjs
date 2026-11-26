// End to end: whole games against a small scripted console behind the fake
// transport (join policy as in contract 7.3).

import { test } from 'node:test';
import assert from 'node:assert/strict';

import { createHarness, jpeg, cmd, PARAMS } from './harness.mjs';
import { createMemoryBackend } from '../../js/util/storage.js';
import { createFakeClock, flush } from '../../js/util/clock.js';
import { createReassembler, encodeChunks, parseTextFrame } from '../../js/net/frames.js';

const START = 1_800_000_000_000;

// A console shared by several phones. Phones attach their harness.
function createConsole({ sessionId = 4242, refuse = 0, pendingFrames = 0 } = {}) {
  const sessions = []; // { t, uid, playerId, slotId, uploads: [], open }
  const byUid = new Map();
  const images = new Map(); // guid -> bytes
  let nextPlayer = 2;
  let nextTid = 1;

  function send(s, json) {
    s.t.recvText(JSON.stringify({ t: 'msg', id: -7503, json }));
  }

  function attach(h) {
    h.factory.onCreate = (t) => {
      const s = { t, uid: null, msgs: [], uploads: [], pendingHeader: null, reasm: createReassembler(), open: true };
      sessions.push(s);
      const origText = t.sendText.bind(t);
      const origBin = t.sendBinary.bind(t);
      t.sendText = (text) => {
        const ok = origText(text);
        queueMicrotask(() => onText(s, text));
        return ok;
      };
      t.sendBinary = (b) => {
        const ok = origBin(b);
        const copy = new Uint8Array(b);
        queueMicrotask(() => onBinary(s, copy));
        return ok;
      };
      t.accept();
    };
  }

  function onText(s, text) {
    if (!s.open || s.t.closed) return;
    const o = JSON.parse(text);
    if (o.t === 'hello') {
      if (o.token !== PARAMS.t) {
        s.t.remoteClose();
        return;
      }
      s.uid = o.uid;
      if (refuse) {
        s.t.recvText(JSON.stringify({ t: 'refused', reason: refuse }));
        s.open = false;
        return;
      }
      for (const other of sessions) {
        if (other !== s && other.open && other.uid === o.uid) {
          other.open = false;
          other.t.recvText('{"t":"bye","reason":"replaced"}');
        }
      }
      for (let i = 0; i < pendingFrames; i++) s.t.recvText('{"t":"pending"}');
      s.t.recvText('{"t":"joined"}');
      send(s, { TypeString: 'InterfaceVersionMessage', InterfaceVersion: '2016-07-12-1645' });
      send(s, { TypeString: 'SessionStateMessage', SessionID: sessionId });
      return;
    }
    if (o.t !== 'msg') return;
    const m = o.json;
    s.msgs.push(m);
    switch (m.TypeString) {
      case 'ClientRequestPlayerIDMessage': {
        let p = byUid.get(m.UID);
        if (!p) {
          p = { playerId: nextPlayer++, slotId: byUid.size };
          byUid.set(m.UID, p);
        }
        s.playerId = p.playerId;
        send(s, { TypeString: 'AssignPlayerIDAndSlotMessage', PlayerID: p.playerId, SlotID: p.slotId, UDPPortOffset: 0, DisplayName: '', PSNID: '' });
        send(s, { TypeString: 'SyncJokersQuizStateMessage', NumJokers: 1 });
        send(s, { TypeString: 'ClientOnlineStateMessage', IsOnline: false });
        send(s, { TypeString: 'ClientUGCStateMessage', ugcState: 0 });
        send(s, { TypeString: 'ResourceRequirementsMessage', Requirements: [] });
        return;
      }
      case 'ClientImageResourceContentTransferMessage':
        s.pendingHeader = m;
        return;
      case 'RequestResourceMessage': {
        const bytes = images.get(m.ResourceGUID);
        if (!bytes) return;
        const tid = nextTid++;
        send(s, { TypeString: 'ImageResourceContentTransferMessage', TransferID: tid, ImageGUID: m.ResourceGUID, Acknowledge: true });
        for (const f of encodeChunks(tid, bytes)) s.t.recvBinary(f);
        return;
      }
      default:
    }
  }

  function onBinary(s, b) {
    const r = s.reasm.push(b);
    if (!r.done) return;
    const hd = s.pendingHeader;
    assert.ok(hd, 'binary announced by a header');
    assert.equal(hd.TransferID, r.transferId);
    s.pendingHeader = null;
    s.uploads.push({ guid: hd.ImageGUID, imgType: hd.ImgType, size: r.data.length });
    images.set(hd.ImageGUID, r.data);
  }

  return {
    sessions,
    images,
    attach,
    send,
    live: () => sessions.filter((s) => s.open && !s.t.closed),
    sessionOf: (h) => sessions.filter((s) => h.factory.created.includes(s.t)).at(-1),
    toAll(json) {
      for (const s of sessions) if (s.open && !s.t.closed) send(s, json);
    },
    sinceTypes(s, from = 0) {
      return s.msgs.slice(from).map((m) => m.TypeString);
    },
  };
}

async function settle(h, ms = 0) {
  await flush(200);
  await h.tick(ms);
  await flush(200);
}

let phones = 0;
async function phone(con, { backend = createMemoryBackend(), clock } = {}) {
  const h = createHarness({ backend, clock, seed: ++phones });
  con.attach(h);
  return h;
}

async function makeProfile(h, name, card) {
  h.game.actions.profileName(name, false);
  h.game.actions.profileCard(card);
  h.game.actions.profilePhoto({ jpeg: jpeg(5000, name.length), usesFX: false });
  h.game.actions.profileConfirm();
  await settle(h);
}

test('a whole game: join, profiles, every round type, end of game', async () => {
  const clock = createFakeClock(START);
  const con = createConsole();
  const a = await phone(con, { clock });
  const b = await phone(con, { clock });
  for (const h of [a, b]) {
    h.game.boot(PARAMS);
    h.game.actions.play();
  }
  await settle(a);
  assert.equal(a.at, 'joined');
  assert.equal(b.at, 'joined');
  assert.equal(a.game.state.playerId, 2);
  assert.equal(b.game.state.playerId, 3);
  const [sa, sb] = con.sessions;
  assert.deepEqual(con.sinceTypes(sa), ['ClientRequestPlayerIDMessage', 'AllResourcesReceivedMessage']);

  // profiles
  con.toAll(cmd(31));
  await settle(a);
  assert.equal(a.at, 'name');
  await makeProfile(a, 'Ann', 'CARD_1');
  await makeProfile(b, 'Bo', 'CARD_2');
  assert.deepEqual(sa.uploads.map((u) => u.imgType), [2]);
  assert.ok(con.sinceTypes(sa).includes('ClientPlayerProfileMessage'));
  assert.ok(con.sinceTypes(sa).includes('DeviceInfoMessage'));
  const portraitA = sa.uploads[0].guid;
  const portraitB = sb.uploads[0].guid;

  // lobby -> start
  con.toAll({ TypeString: 'ClientHoldingScreenCommandMessage', action: 5, time: 0, HoldingScreenText: '', HoldingScreenType: 4, OtherPlayerIndex: 0, ShowPortraitPhotoControls: true });
  con.toAll(cmd(14));
  await settle(a);
  assert.equal(b.at, 'continue:start');
  b.game.actions.startPressed();
  await settle(a);
  assert.ok(con.sinceTypes(sb).includes('StartGameButtonPressedResponseMessage'));
  con.toAll({ TypeString: 'ClientGameIDMessage', GameID: '2xyz' });

  // That's You: portraits are required; phone a holds only its own
  let mark = sa.msgs.length;
  con.toAll({ TypeString: 'QuestionDataMessage', RoundType: 2, QuestionText: 'Who sings?' });
  con.toAll({ TypeString: 'ResourceRequirementsMessage', Requirements: [{ GUID: portraitA, Rqrmnt: 1 }, { GUID: portraitB, Rqrmnt: 1 }] });
  await settle(a);
  assert.deepEqual(con.sinceTypes(sa, mark), ['RequestResourceMessage', 'AllResourcesReceivedMessage']);
  assert.ok(a.game.resources.url(portraitB));
  con.toAll({ TypeString: 'ClientAnswerCommandMessage', action: 3, time: 0, roundID: 2, questionBarText: 'Who sings?', jokersEnabled: true, ImageGUIDs: [portraitA, portraitB] });
  await settle(a);
  assert.equal(a.at, 'answer:portraits');
  a.game.actions.setJoker(true);
  a.game.actions.confirmAnswer(2);
  con.toAll(cmd(16));
  con.toAll(cmd(17));
  await settle(a);
  assert.deepEqual(sa.msgs.slice(-3), [
    { TypeString: 'AnswerIndexResponseMessage', AnswerIndex: 2 },
    { TypeString: 'AnswerIndexResponseMessage', AnswerIndex: 2 },
    { TypeString: 'JokerConsumedResponseMessage', Response: 2, jokerConsumed: true },
  ]);
  assert.deepEqual(sb.msgs.slice(-2), [
    { TypeString: 'AnswerIndexResponseMessage', AnswerIndex: -2 },
    { TypeString: 'JokerConsumedResponseMessage', Response: 2, jokerConsumed: false },
  ]);
  con.toAll(cmd(4));
  await settle(a);
  a.game.actions.continuePressed();
  b.game.actions.continuePressed();
  await settle(a);

  // Blank You
  con.toAll({ TypeString: 'QuestionDataMessage', RoundType: 32, QuestionText: '___' });
  con.toAll({ TypeString: 'ClientTextEntryCommandMessage', action: 8, time: 0, TitleText: 'Fill in' });
  await settle(a);
  a.game.actions.textChanged('Pizza');
  a.game.actions.submitText('Pizza');
  con.toAll(cmd(18));
  con.toAll({ TypeString: 'AnswerTextQuizStateMessage', AnswerText: 'Pizza', PlayerID: 1 });
  con.toAll({ TypeString: 'AnswerTextQuizStateMessage', AnswerText: 'Tacos', PlayerID: 0 });
  con.toAll({ TypeString: 'ClientAnswerCommandMessage', action: 3, time: 0, roundID: 32, questionBarText: '___', jokersEnabled: false, ImageGUIDs: [] });
  await settle(a);
  assert.equal(a.at, 'answer:texts');
  a.game.actions.confirmAnswer(2);
  await settle(a);
  assert.deepEqual(sa.msgs.filter((m) => m.TypeString === 'TextAnswerReponseMessage'), [{ TypeString: 'TextAnswerReponseMessage', Response: 5, Text: 'Pizza' }]);
  assert.deepEqual(sa.msgs.at(-1), { TypeString: 'AnswerIndexResponseMessage', AnswerIndex: 0 });

  // Would You Rather
  con.toAll({ TypeString: 'QuestionDataMessage', RoundType: 128, QuestionText: 'WYR' });
  con.toAll({ TypeString: 'WouldYouRatherAnswerTextQuizMessage', AnswerText: 'A', AnswerId: 1 });
  con.toAll({ TypeString: 'WouldYouRatherAnswerTextQuizMessage', AnswerText: 'B', AnswerId: 2 });
  con.toAll({ TypeString: 'ClientAnswerCommandMessage', action: 3, time: 0, roundID: 128, questionBarText: 'WYR', jokersEnabled: false, ImageGUIDs: [] });
  await settle(a);
  a.game.actions.selectAnswer(1);
  con.toAll(cmd(16));
  await settle(a);
  assert.deepEqual(sa.msgs.at(-1), { TypeString: 'AnswerIndexResponseMessage', AnswerIndex: 1 });

  // Top Drawer: a is the target, b waits
  con.toAll({ TypeString: 'QuestionDataMessage', RoundType: 1, QuestionText: 'TD' });
  con.send(sa, { TypeString: 'ClientTopDrawerTakePhotoCommandMessage', action: 12, time: 0, CommandText: 'Photo of {0}', PlayerName: 'Bo' });
  con.send(sb, { TypeString: 'ClientHoldingScreenCommandMessage', action: 5, time: 0, HoldingScreenText: 'Ann', HoldingScreenType: 3, OtherPlayerIndex: 2, ShowPortraitPhotoControls: true });
  await settle(a);
  assert.equal(a.at, 'photo:topdrawer');
  assert.equal(b.at, 'holding:other-photo');
  a.game.actions.photoTaken();
  a.game.actions.submitPhoto({ jpeg: jpeg(40000), usesFX: true });
  await settle(a);
  assert.equal(sa.uploads.at(-1).imgType, 3);
  assert.deepEqual(con.sinceTypes(sa).slice(-4),
    ['TopDrawerPhotoTakenResponseMessage', 'ClientImageResourceContentTransferMessage', 'TrophyPhotoFXInfoMessage', 'UsePicPressedResponseMessage']);
  const canvas = sa.uploads.at(-1).guid;
  con.toAll({ TypeString: 'ResourceRequirementsMessage', Requirements: [{ GUID: canvas, Rqrmnt: 1 }] });
  con.toAll({ TypeString: 'ClientStartDrawingCommandMessage', action: 7, time: 0, CanvasGUID: canvas });
  await settle(a);
  assert.equal(b.at, 'draw:topdrawer');
  assert.ok(b.game.resources.url(canvas), 'b downloaded the canvas');
  a.game.actions.submitDrawing({ jpeg: jpeg(20000), coloursAvailable: 32766, coloursUsed: 4 });
  b.game.setDraftProvider(async () => ({ jpeg: jpeg(21000), coloursAvailable: 32766, coloursUsed: 0 }));
  con.toAll(cmd(19));
  await settle(a);
  assert.equal(sa.uploads.at(-1).imgType, 1);
  assert.equal(sb.uploads.at(-1).imgType, 1);
  assert.equal(sa.uploads.filter((u) => u.imgType === 1).length, 1);
  assert.equal(b.at, 'holding:look-at-tv');
  b.game.setDraftProvider(null);
  const drawings = [sb.uploads.at(-1).guid, sa.uploads.at(-1).guid];
  con.toAll({ TypeString: 'ClientAnswerCommandMessage', action: 3, time: 0, roundID: 1, questionBarText: 'TD', jokersEnabled: false, ImageGUIDs: drawings });
  await settle(a);
  assert.equal(a.at, 'answer:drawings');
  a.game.actions.confirmAnswer(1);
  await settle(a);
  assert.deepEqual(sa.msgs.at(-1), { TypeString: 'AnswerIndexResponseMessage', AnswerIndex: 1 });

  // Mimic This
  con.images.set('mt-q', jpeg(30000));
  con.toAll({ TypeString: 'QuestionDataMessage', RoundType: 64, QuestionText: 'MT' });
  con.toAll({ TypeString: 'ResourceRequirementsMessage', Requirements: [{ GUID: 'mt-q', Rqrmnt: 1 }] });
  con.toAll({ TypeString: 'ClientMimicThisCommandMessage', action: 23, time: 0, MimicPictureGUID: 'mt-q' });
  await settle(a);
  assert.equal(a.at, 'mimic');
  assert.ok(a.game.resources.url('mt-q'));
  a.game.actions.submitPhoto({ jpeg: jpeg(30000), usesFX: false });
  b.game.setDraftProvider(async (kind) => (kind === 'photo' ? { jpeg: jpeg(31000), usesFX: true } : null));
  con.toAll(cmd(24));
  await settle(a);
  assert.equal(sa.uploads.at(-1).imgType, 4);
  assert.equal(sb.uploads.at(-1).imgType, 4);
  b.game.setDraftProvider(null);

  // QuickDraw: selfie, drawing chain, vote (arrives as CanvasOpinion)
  con.toAll({ TypeString: 'QuestionDataMessage', RoundType: 256, QuestionText: 'QD' });
  con.toAll({ TypeString: 'ClientQuickDrawTakePhotoCommandMessage', action: 25, time: 0, CommandText: 'Selfie!' });
  await settle(a);
  assert.equal(a.at, 'photo:quickdraw');
  a.game.actions.submitPhoto({ jpeg: jpeg(25000), usesFX: false });
  b.game.actions.submitPhoto({ jpeg: jpeg(26000), usesFX: true });
  await settle(a);
  const selfieB = sb.uploads.at(-1).guid;
  assert.equal(sa.uploads.at(-1).imgType, 5);
  con.send(sa, { TypeString: 'ResourceRequirementsMessage', Requirements: [{ GUID: selfieB, Rqrmnt: 1 }] });
  con.send(sa, {
    TypeString: 'ClientQuickDrawHoldingScreenCommandMessage', action: 5, time: 0, HoldingScreenText: '***NOTSETHOLDINGSCREENTEXT',
    HoldingScreenType: 5, OtherPlayerIndex: 0, ShowPortraitPhotoControls: true, ImageGUID: selfieB, Heading: 'Draw on Bo',
    Description: 'Go', NumberOfDrawingsRemaining: 1, PlayerName: 'Bo',
  });
  await settle(a);
  assert.equal(a.at, 'quickdraw-holding');
  con.send(sa, cmd(27));
  await settle(a);
  assert.equal(a.at, 'draw:quickdraw');
  assert.equal(a.view.data.backgroundGuid, selfieB);
  assert.ok(a.game.resources.url(selfieB));
  a.game.setDraftProvider(async () => ({ jpeg: jpeg(15000), coloursAvailable: 32766, coloursUsed: 8 }));
  con.send(sa, cmd(28));
  await settle(a);
  assert.equal(sa.uploads.at(-1).imgType, 5);
  assert.equal(a.at, 'holding:look-at-tv');
  a.game.setDraftProvider(null);

  // Canvas Opinion
  con.images.set('co-q', jpeg(40000));
  con.toAll({ TypeString: 'QuestionDataMessage', RoundType: 8, QuestionText: 'CO' });
  con.toAll({ TypeString: 'ResourceRequirementsMessage', Requirements: [{ GUID: 'co-q', Rqrmnt: 1 }] });
  con.toAll({ TypeString: 'ClientStartDrawingCommandMessage', action: 7, time: 0, CanvasGUID: 'co-q' });
  await settle(a);
  assert.equal(a.at, 'draw:canvas');
  a.game.actions.submitDrawing({ jpeg: jpeg(18000), coloursAvailable: 32766, coloursUsed: 2 });
  await settle(a);
  assert.equal(sa.uploads.at(-1).imgType, 1);

  // Picture This
  con.toAll({ TypeString: 'ClientAnswerCommandMessage', action: 3, time: 0, roundID: 4, questionBarText: 'PT', jokersEnabled: false, ImageGUIDs: ['mt-q', 'co-q'] });
  await settle(a);
  assert.equal(a.at, 'answer:images');

  // jokers, pause, end of game
  con.toAll({ TypeString: 'ClientJokerCommandMessage', action: 2, time: -1, PreNumJokers: 1, CurNumJokers: 2 });
  await settle(a);
  assert.equal(a.at, 'joker');
  con.toAll(cmd(29));
  await settle(a);
  assert.deepEqual(a.game.overlays.map((o) => o.id), ['pause']);
  con.toAll(cmd(30));
  con.toAll({ TypeString: 'ClientScenarioTransitionCommandMessage', action: 9, time: 0, Scenario: -1 });
  con.toAll({ TypeString: 'PlayerLeftMessage', PlayerID: 3 });
  con.toAll({ TypeString: 'ClientGameIDMessage', GameID: '1abc' });
  con.toAll(cmd(15));
  await settle(a);
  assert.equal(a.at, 'joined');
  assert.equal(a.game.state.phase, 'lobby');
  assert.equal(a.game.state.scenario, -1);
  // restart profile: confirm pre-filled
  con.toAll(cmd(31));
  await settle(a);
  assert.equal(a.at, 'confirm');
  assert.equal(a.view.data.name, 'Ann');

  // every upload stayed within the cap and was paired with its header
  for (const s of [sa, sb]) for (const u of s.uploads) assert.ok(u.size <= 128000);
  // pings keep flowing; nothing was sent before joined
  for (const s of [sa, sb]) {
    const frames = s.t.sent.filter((x) => typeof x === 'string').map(parseTextFrame);
    assert.equal(JSON.parse(s.t.sent[0]).t, 'hello');
    assert.ok(frames.every(Boolean));
  }
});

test('reload rejoins automatically with the saved profile', async () => {
  const clock = createFakeClock(START);
  const backend = createMemoryBackend();
  const con = createConsole({ sessionId: 9 });
  const a = await phone(con, { backend, clock });
  a.game.boot(PARAMS);
  a.game.actions.play();
  await settle(a);
  con.toAll(cmd(31));
  await settle(a);
  await makeProfile(a, 'Ann', 'CARD_5');
  const pid = a.game.state.playerId;
  a.game.destroy();
  // the old tab is gone; reload with the same fragment
  const a2 = await phone(con, { backend, clock });
  a2.game.boot(PARAMS);
  await settle(a2);
  assert.equal(a2.factory.created.length, 1, 'connected without a tap');
  assert.equal(a2.at, 'joined');
  assert.equal(a2.game.state.playerId, pid);
  assert.equal(a2.game.uid, a.game.uid);
  assert.equal(a2.game.state.profile.name, 'Ann');
  assert.ok(a2.game.state.profile.portraitUrl);
  con.send(con.sessionOf(a2), cmd(31));
  await settle(a2);
  assert.equal(a2.at, 'confirm');
  assert.deepEqual({ ...a2.view.data, portraitUrl: !!a2.view.data.portraitUrl }, { name: 'Ann', cardId: 'CARD_5', portraitUrl: true });
});

test('a second tab with the same uid replaces the first, which does not reconnect', async () => {
  const clock = createFakeClock(START);
  const backend = createMemoryBackend();
  const con = createConsole();
  const a = await phone(con, { backend, clock });
  a.game.boot(PARAMS);
  a.game.actions.play();
  await settle(a);
  const b = await phone(con, { backend, clock });
  b.game.boot(PARAMS); // active lastJoin: auto connect
  await settle(b);
  assert.equal(b.at, 'joined');
  assert.deepEqual(a.game.popup, { reason: 'replaced', reconnect: true });
  assert.equal(a.at, 'connect');
  await a.tick(60000);
  assert.equal(a.factory.created.length, 1);
  // tapping reconnect on the old tab takes the session back
  a.game.actions.reconnect();
  await settle(a);
  assert.equal(a.at, 'joined');
  assert.equal(b.game.popup.reason, 'replaced');
});

test('pending then joined, and a refusing console', async () => {
  const con = createConsole({ pendingFrames: 1 });
  const a = await phone(con);
  a.game.boot(PARAMS);
  a.game.actions.play();
  await settle(a);
  assert.equal(a.at, 'joined');
  assert.ok(a.views.some((v) => v.screen === 'connect' && v.data.state === 'waiting'));

  const con2 = createConsole({ refuse: 2 });
  const b = await phone(con2);
  b.game.boot(PARAMS);
  b.game.actions.play();
  await settle(b);
  assert.deepEqual(b.game.popup, { reason: 'gamefull', reconnect: false });
});

test('a lost connection mid-round reconnects to the same player', async () => {
  const clock = createFakeClock(START);
  const con = createConsole();
  const a = await phone(con, { clock });
  a.game.boot(PARAMS);
  a.game.actions.play();
  await settle(a);
  const pid = a.game.state.playerId;
  con.toAll({ TypeString: 'ClientMimicThisCommandMessage', action: 23, time: 0, MimicPictureGUID: 'x' });
  await settle(a);
  con.live()[0].t.remoteClose();
  await settle(a);
  assert.deepEqual(a.game.overlays.map((o) => o.id), []);
  assert.equal(a.at, 'joined');
  assert.equal(a.game.state.playerId, pid);
  assert.equal(a.factory.created.length, 2);
  con.send(con.live()[0], { TypeString: 'ClientHoldingScreenCommandMessage', action: 5, time: 0, HoldingScreenText: '', HoldingScreenType: 8, OtherPlayerIndex: 0, ShowPortraitPhotoControls: false });
  await settle(a);
  assert.equal(a.at, 'holding:rejoin');
});
