// Views and outgoing messages of contract 5.6 (every row, every command rule).

import { test } from 'node:test';
import assert from 'node:assert/strict';

import { createHarness, jpeg, cmd } from './harness.mjs';
import { answerVariant } from '../../js/game/machine.js';
import { createMemoryBackend } from '../../js/util/storage.js';
import { flush } from '../../js/util/clock.js';

const holdingMsg = (type, text = '', other = 0) => ({
  TypeString: 'ClientHoldingScreenCommandMessage', action: 5, time: 0,
  HoldingScreenText: text, HoldingScreenType: type, OtherPlayerIndex: other, ShowPortraitPhotoControls: true,
});
const answerMsg = (roundID, guids = [], jokersEnabled = false, q = 'Who?') => ({
  TypeString: 'ClientAnswerCommandMessage', action: 3, time: 0, roundID, questionBarText: q, jokersEnabled, ImageGUIDs: guids,
});
const S = (o) => JSON.stringify(o);

async function joined() {
  const h = createHarness();
  await h.start();
  return h;
}

test('EditProfile sends DeviceInfoMessage and opens name entry', async () => {
  const h = await joined();
  h.cmd(31);
  const sent = h.take();
  assert.equal(S(sent[0]), '{"TypeString":"DeviceInfoMessage","Response":10,"DeviceSize":1,"DeviceOS":2,"DeviceModel":"iPhone","DeviceType":"Handheld","DeviceUID":"' + h.game.uid + '","DeviceOperatingSystem":"iOS 18.1"}');
  assert.equal(sent.length, 1);
  assert.equal(h.at, 'name');
  assert.deepEqual(h.view.data, { name: '', isGuest: false, maxLength: 10 });
});

test('profile flow: name -> card -> selfie -> confirm -> upload and profile', async () => {
  const h = await joined();
  h.cmd(31);
  h.take();
  const a = h.game.actions;
  a.profileName('   ', false);
  assert.equal(h.at, 'name');
  a.profileName('  Annabelle-Long ', true);
  assert.equal(h.at, 'card');
  assert.deepEqual(h.view.data, { cardId: '' });
  a.profileCard('CARD_99');
  assert.equal(h.at, 'card');
  a.profileCard('CARD_7');
  assert.equal(h.at, 'selfie');
  assert.deepEqual(h.view.data, { facing: 'user' });
  const pic = jpeg(2000);
  a.profilePhoto({ jpeg: pic, usesFX: true });
  assert.equal(h.at, 'confirm');
  assert.equal(h.view.data.name, 'Annabelle-');
  assert.equal(h.view.data.cardId, 'CARD_7');
  assert.ok(h.view.data.portraitUrl.startsWith('blob:'));
  a.profileEdit('card');
  assert.equal(h.at, 'card');
  assert.deepEqual(h.view.data, { cardId: 'CARD_7' });
  a.profileEdit('name');
  assert.deepEqual(h.view.data, { name: 'Annabelle-', isGuest: true, maxLength: 10 });
  a.profileEdit('photo');
  assert.equal(h.at, 'selfie');
  a.profilePhoto({ jpeg: pic, usesFX: true });
  a.profileConfirm();
  const sent = h.take();
  const guid = sent[0].ImageGUID;
  assert.deepEqual(sent, [
    { TypeString: 'ClientImageResourceContentTransferMessage', TransferID: 1000, ImageGUID: guid, ImgType: 2 },
    { TypeString: 'TrophyPhotoFXInfoMessage', ImageGUID: guid, usesFX: true },
    {
      TypeString: 'ClientPlayerProfileMessage', playerName: 'Annabelle-', uppercasePlayerName: 'ANNABELLE-', deviceCultureName: 'en-US',
      ugcInfo: { scenarioInt: -1, scenario: -100, challengeID: '', creationID: '', set: false }, isGuest: 1, playerCardId: 'CARD_7',
    },
  ]);
  const bins = h.tr().binaries();
  assert.equal(bins.length, 1);
  assert.equal(new DataView(bins[0].buffer).getInt32(0, true), 1000);
  // header, then binary, then the rest
  const order = h.tr().sent.slice(1).map((x) => (typeof x === 'string' ? JSON.parse(x).json?.TypeString || JSON.parse(x).t : 'bin'));
  assert.deepEqual(order.filter((x) => x !== 'ping').slice(-4),
    ['ClientImageResourceContentTransferMessage', 'bin', 'TrophyPhotoFXInfoMessage', 'ClientPlayerProfileMessage']);
  assert.equal(h.at, 'holding:general');
  assert.equal(h.backend.getItem('iwyk.profileDone'), '1');
  assert.equal(h.backend.getItem('iwyk.portraitId'), guid);
  assert.equal(h.backend.getItem('iwyk.name'), 'Annabelle-');
  assert.equal(h.backend.getItem('iwyk.card'), 'CARD_7');
  assert.equal(h.backend.getItem('iwyk.guest'), '1');
  assert.equal(Buffer.from(h.backend.getItem('iwyk.portrait'), 'base64').length, 2000);
  assert.ok(h.game.resources.has(guid));
  assert.equal(h.game.state.profile.name, 'Annabelle-');

  // EditProfile again (restart profile): confirm pre-filled
  h.cmd(31);
  assert.deepEqual(h.takeTypes(), ['DeviceInfoMessage']);
  assert.equal(h.at, 'confirm');
  assert.equal(h.view.data.name, 'Annabelle-');
  a.profileConfirm();
  assert.deepEqual(h.takeTypes(), ['ClientImageResourceContentTransferMessage', 'TrophyPhotoFXInfoMessage', 'ClientPlayerProfileMessage']);
});

test('profile confirm without a photo does nothing; saved name is suggested', async () => {
  const h = createHarness({ backend: createMemoryBackend({ 'iwyk.name': 'Bo', 'iwyk.card': 'CARD_2', 'iwyk.guest': '1' }) });
  await h.start();
  h.cmd(31);
  h.take();
  assert.deepEqual(h.view.data, { name: 'Bo', isGuest: true, maxLength: 10 });
  h.game.actions.profileName('Bo', true);
  assert.deepEqual(h.view.data, { cardId: 'CARD_2' });
  h.game.actions.profileConfirm();
  assert.deepEqual(h.take(), []);
});

test('holding screen variants', async () => {
  const h = await joined();
  const table = [
    [holdingMsg(4), 'holding:look-at-tv', { text: '', playerName: '' }],
    [holdingMsg(1), 'holding:wait', { text: '', playerName: '' }],
    [holdingMsg(3, 'Bo', 2), 'holding:other-photo', { text: 'Bo', playerName: 'Bo' }],
    [holdingMsg(2, 'x', 1), 'holding:other-ugc', { text: 'x', playerName: '' }],
    [holdingMsg(8), 'holding:rejoin', { text: '', playerName: '' }],
    [holdingMsg(6, 'Hello'), 'holding:custom', { text: 'Hello', playerName: '' }],
    [holdingMsg(7, 'Q'), 'holding:custom', { text: 'Q', playerName: '' }],
    [holdingMsg(99, 'S'), 'holding:custom', { text: 'S', playerName: '' }],
    [holdingMsg(5), 'holding:general', { text: '', playerName: '' }],
  ];
  for (const [m, at, data] of table) {
    h.g2p(m);
    assert.equal(h.at, at);
    assert.deepEqual(h.view.data, data);
  }
  const seq = h.view.seq;
  h.g2p(holdingMsg(0));
  h.g2p(holdingMsg(3, 'Bo', 0));
  h.g2p(holdingMsg(4, '***NOTSETHOLDINGSCREENTEXT'));
  assert.equal(h.view.seq, seq, 'invalid holding messages are dropped');
  assert.deepEqual(h.take(), []);
});

test('QuickDraw holding (sentinel text allowed) and QuickDraw drawing background', async () => {
  const h = await joined();
  h.g2p({
    TypeString: 'ClientQuickDrawHoldingScreenCommandMessage', action: 5, time: 0, HoldingScreenText: '***NOTSETHOLDINGSCREENTEXT',
    HoldingScreenType: 5, OtherPlayerIndex: 0, ShowPortraitPhotoControls: true,
    ImageGUID: 'qd-1', Heading: 'H', Description: 'D', NumberOfDrawingsRemaining: 3, PlayerName: 'Bo',
  });
  assert.equal(h.at, 'quickdraw-holding');
  assert.deepEqual(h.view.data, { imageGuid: 'qd-1', heading: 'H', description: 'D', remaining: 3, playerName: 'Bo' });
  h.cmd(27);
  assert.equal(h.at, 'draw:quickdraw');
  assert.deepEqual(h.view.data, { backgroundGuid: 'qd-1' });
  h.game.actions.submitDrawing({ jpeg: jpeg(500), coloursAvailable: 32766, coloursUsed: 6 });
  const sent = h.take();
  assert.equal(sent[0].ImgType, 5);
  assert.deepEqual(sent[1], { TypeString: 'TrophyDrawingColouringInfoMessage', ImageGUID: sent[0].ImageGUID, ColoursAvailable: 32766, ColoursUsed: 6 });
  assert.equal(h.at, 'holding:look-at-tv');
  h.cmd(28);
  assert.deepEqual(h.take(), [], 'already submitted: not sent twice');
  assert.equal(h.at, 'holding:look-at-tv');
});

test('continue and start', async () => {
  const h = await joined();
  h.cmd(4);
  assert.equal(h.at, 'continue:continue');
  assert.deepEqual(h.view.data, {});
  h.game.actions.startPressed();
  assert.deepEqual(h.take(), []);
  h.game.actions.continuePressed();
  assert.deepEqual(h.take(), [{ TypeString: 'ContinuePressedResponseMessage', Response: 3 }]);
  assert.equal(h.at, 'holding:general');
  h.cmd(14);
  assert.equal(h.at, 'continue:start');
  h.game.actions.continuePressed();
  h.game.actions.startPressed();
  assert.deepEqual(h.take(), [{ TypeString: 'StartGameButtonPressedResponseMessage', Response: 9 }]);
  assert.equal(h.at, 'holding:general');
});

test('joker screen and joker counts', async () => {
  const h = await joined();
  h.g2p({ TypeString: 'ClientJokerCommandMessage', action: 2, time: -1, PreNumJokers: 1, CurNumJokers: 2 });
  assert.equal(h.at, 'joker');
  assert.deepEqual(h.view.data, { pre: 1, cur: 2 });
  assert.equal(h.game.state.jokers, 2);
  h.g2p({ TypeString: 'SyncJokersQuizStateMessage', NumJokers: 0 });
  assert.equal(h.game.state.jokers, 0);
});

test('answer variant from roundID', () => {
  const table = [[2, 'portraits'], [16, 'portraits'], [4, 'images'], [1, 'drawings'], [8, 'drawings'], [64, 'drawings'],
    [32, 'texts'], [128, 'texts'], [256, 'drawings'], [1024, 'images']];
  for (const [r, v] of table) assert.equal(answerVariant(r), v, String(r));
});

test('portraits: select, confirm sends the 1-based position at once, SendAnswer resends', async () => {
  const h = await joined();
  h.g2p({ TypeString: 'QuestionDataMessage', RoundType: 2, QuestionText: 'Who is it?' });
  h.g2p(answerMsg(2, ['g1', 'g2', 'g3'], false, 'WHO?'));
  assert.equal(h.at, 'answer:portraits');
  assert.deepEqual(h.view.data, {
    roundType: 2, question: 'WHO?', jokersEnabled: false, jokers: 0,
    choices: [
      { position: 1, guid: 'g1', text: '', value: 1 }, { position: 2, guid: 'g2', text: '', value: 2 }, { position: 3, guid: 'g3', text: '', value: 3 },
    ],
    selected: null, jokerOn: false,
  });
  h.game.actions.selectAnswer(5);
  assert.equal(h.view.data.selected, null);
  h.game.actions.selectAnswer(2);
  assert.equal(h.view.data.selected, 2);
  assert.deepEqual(h.take(), []);
  h.game.actions.confirmAnswer(3);
  assert.deepEqual(h.take(), [{ TypeString: 'AnswerIndexResponseMessage', AnswerIndex: 3 }]);
  assert.equal(h.at, 'holding:general');
  h.cmd(16);
  assert.deepEqual(h.take(), [{ TypeString: 'AnswerIndexResponseMessage', AnswerIndex: 3 }]);
  assert.equal(h.at, 'holding:general');
});

test('SendAnswer without a selection sends -2; SendJoker sends jokerOn', async () => {
  const h = await joined();
  h.g2p({ TypeString: 'SyncJokersQuizStateMessage', NumJokers: 2 });
  h.g2p(answerMsg(4, ['a', 'b'], true));
  assert.equal(h.at, 'answer:images');
  assert.equal(h.view.data.jokers, 2);
  h.game.actions.setJoker(true);
  assert.equal(h.view.data.jokerOn, true);
  h.cmd(16);
  h.cmd(17);
  assert.deepEqual(h.take(), [
    { TypeString: 'AnswerIndexResponseMessage', AnswerIndex: -2 },
    { TypeString: 'JokerConsumedResponseMessage', Response: 2, jokerConsumed: true },
  ]);
  assert.equal(h.at, 'holding:general');
});

test('joker toggle needs jokersEnabled and jokers > 0', async () => {
  const h = await joined();
  h.g2p(answerMsg(2, ['a'], true));
  h.game.actions.setJoker(true);
  assert.equal(h.view.data.jokerOn, false, 'no jokers');
  h.g2p({ TypeString: 'SyncJokersQuizStateMessage', NumJokers: 1 });
  h.g2p(answerMsg(2, ['a'], false));
  h.game.actions.setJoker(true);
  assert.equal(h.view.data.jokerOn, false, 'not enabled');
  h.cmd(17);
  assert.deepEqual(h.take().at(-1), { TypeString: 'JokerConsumedResponseMessage', Response: 2, jokerConsumed: false });
});

test('Blank You: choices are the AnswerText cards, AnswerIndex is the PlayerID label', async () => {
  const h = await joined();
  h.g2p({ TypeString: 'QuestionDataMessage', RoundType: 32, QuestionText: '___ loves cheese' });
  h.g2p({ TypeString: 'AnswerTextQuizStateMessage', AnswerText: 'Bo', PlayerID: 7 });
  h.g2p({ TypeString: 'AnswerTextQuizStateMessage', AnswerText: '***NOTSETANSWERTEXT', PlayerID: 8 });
  h.g2p(answerMsg(32, [], false, ''));
  assert.equal(h.at, 'answer:texts');
  assert.equal(h.view.data.question, '___ loves cheese');
  h.g2p({ TypeString: 'AnswerTextQuizStateMessage', AnswerText: 'Cy', PlayerID: 4 });
  assert.deepEqual(h.view.data.choices, [
    { position: 1, guid: null, text: 'Bo', value: 7 }, { position: 2, guid: null, text: 'Cy', value: 4 },
  ]);
  h.game.actions.confirmAnswer(2);
  assert.deepEqual(h.take(), [{ TypeString: 'AnswerIndexResponseMessage', AnswerIndex: 4 }]);
  // a new question clears the cards
  h.g2p({ TypeString: 'QuestionDataMessage', RoundType: 32, QuestionText: 'next' });
  h.g2p(answerMsg(32, [], false, 'q'));
  assert.deepEqual(h.view.data.choices, []);
});

test('Would You Rather: AnswerIndex is the AnswerId', async () => {
  const h = await joined();
  h.g2p({ TypeString: 'QuestionDataMessage', RoundType: 128, QuestionText: 'WYR' });
  h.g2p({ TypeString: 'WouldYouRatherAnswerTextQuizMessage', AnswerText: 'Fly', AnswerId: 2 });
  h.g2p({ TypeString: 'WouldYouRatherAnswerTextQuizMessage', AnswerText: 'Swim', AnswerId: 1 });
  h.g2p({ TypeString: 'WouldYouRatherAnswerTextQuizMessage', AnswerText: '---NOTSETTEXT---', AnswerId: 3 });
  h.g2p(answerMsg(128, [], false, 'WYR'));
  assert.deepEqual(h.view.data.choices, [
    { position: 1, guid: null, text: 'Swim', value: 1 }, { position: 2, guid: null, text: 'Fly', value: 2 },
  ]);
  h.game.actions.selectAnswer(2);
  h.cmd(16);
  assert.deepEqual(h.take(), [{ TypeString: 'AnswerIndexResponseMessage', AnswerIndex: 2 }]);
});

test('Blank You text entry: textChanged / submitText, sent on SendTextEntry', async () => {
  const h = await joined();
  h.g2p({ TypeString: 'ClientTextEntryCommandMessage', action: 8, time: 0, TitleText: 'Finish the sentence' });
  assert.equal(h.at, 'text');
  assert.deepEqual(h.view.data, { title: 'Finish the sentence', maxLength: 80, text: '' });
  h.game.actions.textChanged('x'.repeat(100));
  h.cmd(18);
  assert.deepEqual(h.take(), [{ TypeString: 'TextAnswerReponseMessage', Response: 5, Text: 'x'.repeat(80) }]);
  assert.equal(h.at, 'holding:look-at-tv');

  h.g2p({ TypeString: 'ClientTextEntryCommandMessage', action: 8, time: 0, TitleText: 'Again' });
  h.game.actions.submitText('done');
  assert.equal(h.at, 'holding:look-at-tv');
  assert.deepEqual(h.take(), []);
  h.cmd(18);
  assert.deepEqual(h.take(), [{ TypeString: 'TextAnswerReponseMessage', Response: 5, Text: 'done' }]);

  h.g2p({ TypeString: 'ClientTextEntryCommandMessage', action: 8, time: 0, TitleText: 'Empty' });
  h.cmd(18);
  assert.deepEqual(h.take(), [{ TypeString: 'TextAnswerReponseMessage', Response: 5, Text: '' }]);
});

test('drawing: topdrawer variant in Top Drawer, canvas otherwise; submit uploads ImgType 1', async () => {
  const h = await joined();
  h.g2p({ TypeString: 'QuestionDataMessage', RoundType: 1, QuestionText: 'td' });
  h.g2p({ TypeString: 'ClientStartDrawingCommandMessage', action: 7, time: 0, CanvasGUID: 'canvas-1' });
  assert.equal(h.at, 'draw:topdrawer');
  assert.deepEqual(h.view.data, { backgroundGuid: 'canvas-1' });
  h.g2p({ TypeString: 'QuestionDataMessage', RoundType: 8, QuestionText: 'co' });
  h.g2p({ TypeString: 'ClientStartDrawingCommandMessage', action: 7, time: 0, CanvasGUID: 'co-img' });
  assert.equal(h.at, 'draw:canvas');
  h.game.actions.submitDrawing({ jpeg: jpeg(800), coloursAvailable: 32766, coloursUsed: 2 });
  const sent = h.take();
  assert.deepEqual(sent.map((m) => m.TypeString), ['ClientImageResourceContentTransferMessage', 'TrophyDrawingColouringInfoMessage']);
  assert.equal(sent[0].ImgType, 1);
  assert.equal(h.at, 'holding:look-at-tv');
  h.cmd(19);
  assert.deepEqual(h.take(), []);
});

test('SendDrawing forces the draft from the draft provider', async () => {
  const h = await joined();
  h.g2p({ TypeString: 'ClientStartDrawingCommandMessage', action: 7, time: 0, CanvasGUID: 'c' });
  const kinds = [];
  h.game.setDraftProvider(async (kind) => {
    kinds.push(kind);
    return { jpeg: jpeg(300), coloursAvailable: 32766, coloursUsed: 0 };
  });
  h.cmd(19);
  await flush();
  assert.deepEqual(kinds, ['drawing']);
  assert.deepEqual(h.takeTypes(), ['ClientImageResourceContentTransferMessage', 'TrophyDrawingColouringInfoMessage']);
  assert.equal(h.at, 'holding:look-at-tv');
});

test('SendDrawing without a provider sends nothing and holds', async () => {
  const h = await joined();
  h.g2p({ TypeString: 'ClientStartDrawingCommandMessage', action: 7, time: 0, CanvasGUID: 'c' });
  h.game.setDraftProvider(null);
  h.cmd(19);
  await flush();
  assert.deepEqual(h.take(), []);
  assert.equal(h.at, 'holding:look-at-tv');
});

test('a forced draft that resolves late does not override a newer view', async () => {
  const h = await joined();
  h.g2p({ TypeString: 'ClientStartDrawingCommandMessage', action: 7, time: 0, CanvasGUID: 'c' });
  let release;
  h.game.setDraftProvider(() => new Promise((r) => { release = r; }));
  h.cmd(19);
  h.game.actions.submitDrawing({ jpeg: jpeg(10), coloursAvailable: 1, coloursUsed: 1 });
  assert.deepEqual(h.take(), [], 'no double send while the forced draft is pending');
  h.cmd(4);
  release({ jpeg: jpeg(300), coloursAvailable: 32766, coloursUsed: 0 });
  await flush();
  assert.deepEqual(h.takeTypes(), ['ClientImageResourceContentTransferMessage', 'TrophyDrawingColouringInfoMessage']);
  assert.equal(h.at, 'continue:continue');
});

test('a draft provider that never answers times out', async () => {
  const h = createHarness({ draftTimeoutMs: 3000 });
  await h.start();
  h.g2p({ TypeString: 'ClientMimicThisCommandMessage', action: 23, time: 0, MimicPictureGUID: 'm' });
  h.game.setDraftProvider(() => new Promise(() => {}));
  h.cmd(24);
  await h.tick(2999);
  assert.equal(h.at, 'mimic');
  await h.tick(1);
  assert.equal(h.at, 'holding:look-at-tv');
  assert.deepEqual(h.take().filter((m) => m.TypeString !== 'AllResourcesReceivedMessage'), []);
});

test('Top Drawer photo: {0} replaced, shutter, use -> ImgType 3, FX, UsePic', async () => {
  const h = await joined();
  h.g2p({ TypeString: 'ClientTopDrawerTakePhotoCommandMessage', action: 12, time: 0, CommandText: 'Snap {0} now', PlayerName: 'Bo' });
  assert.equal(h.at, 'photo:topdrawer');
  assert.deepEqual(h.view.data, { commandText: 'Snap Bo now', facing: 'environment' });
  h.g2p({ TypeString: 'ClientTopDrawerTakePhotoCommandMessage', action: 12, time: 0, CommandText: 'Snap {0}!' });
  assert.equal(h.view.data.commandText, 'Snap !');
  h.game.actions.photoTaken();
  assert.deepEqual(h.take(), [{ TypeString: 'TopDrawerPhotoTakenResponseMessage', Response: 11 }]);
  h.game.actions.submitPhoto({ jpeg: jpeg(900), usesFX: false });
  const sent = h.take();
  assert.deepEqual(sent.map((m) => m.TypeString),
    ['ClientImageResourceContentTransferMessage', 'TrophyPhotoFXInfoMessage', 'UsePicPressedResponseMessage']);
  assert.equal(sent[0].ImgType, 3);
  assert.deepEqual(sent[1], { TypeString: 'TrophyPhotoFXInfoMessage', ImageGUID: sent[0].ImageGUID, usesFX: false });
  assert.equal(h.at, 'holding:general');
});

test('QuickDraw photo: front camera, ImgType 5, FX, UsePic, look-at-tv; no shutter message', async () => {
  const h = await joined();
  h.g2p({ TypeString: 'ClientQuickDrawTakePhotoCommandMessage', action: 25, time: 0, CommandText: 'Pull a face' });
  assert.equal(h.at, 'photo:quickdraw');
  assert.deepEqual(h.view.data, { commandText: 'Pull a face', facing: 'user' });
  h.game.actions.photoTaken();
  assert.deepEqual(h.take(), []);
  h.game.actions.submitPhoto({ jpeg: jpeg(900), usesFX: true });
  const sent = h.take();
  assert.deepEqual(sent.map((m) => m.TypeString),
    ['ClientImageResourceContentTransferMessage', 'TrophyPhotoFXInfoMessage', 'UsePicPressedResponseMessage']);
  assert.equal(sent[0].ImgType, 5);
  assert.equal(sent[1].usesFX, true);
  assert.equal(h.at, 'holding:look-at-tv');
});

test('Mimic This: submit uploads ImgType 4; SendMimicThisPhoto then sends nothing more', async () => {
  const h = await joined();
  h.g2p({ TypeString: 'ClientMimicThisCommandMessage', action: 23, time: 0, MimicPictureGUID: 'mt-1' });
  assert.equal(h.at, 'mimic');
  assert.deepEqual(h.view.data, { referenceGuid: 'mt-1', facing: 'user' });
  h.game.actions.submitPhoto({ jpeg: jpeg(900), usesFX: true });
  const sent = h.take();
  assert.deepEqual(sent.map((m) => m.TypeString), ['ClientImageResourceContentTransferMessage', 'TrophyPhotoFXInfoMessage']);
  assert.equal(sent[0].ImgType, 4);
  assert.equal(h.at, 'holding:look-at-tv');
  h.cmd(24);
  await flush();
  assert.deepEqual(h.take(), []);
});

test('SendMimicThisPhoto forces the photo draft; null payload sends nothing', async () => {
  const h = await joined();
  h.g2p({ TypeString: 'ClientMimicThisCommandMessage', action: 23, time: 0, MimicPictureGUID: 'mt-1' });
  h.game.setDraftProvider(async (kind) => (kind === 'photo' ? { jpeg: jpeg(400), usesFX: false } : null));
  h.cmd(24);
  await flush();
  assert.deepEqual(h.takeTypes(), ['ClientImageResourceContentTransferMessage', 'TrophyPhotoFXInfoMessage']);
  h.g2p({ TypeString: 'ClientMimicThisCommandMessage', action: 23, time: 0, MimicPictureGUID: 'mt-2' });
  h.game.setDraftProvider(async () => null);
  h.cmd(24);
  await flush();
  assert.deepEqual(h.take(), []);
  assert.equal(h.at, 'holding:look-at-tv');
});

test('uploads over 128000 bytes are rejected and the screen stays', async () => {
  const h = await joined();
  h.g2p({ TypeString: 'ClientMimicThisCommandMessage', action: 23, time: 0, MimicPictureGUID: 'mt-1' });
  h.game.actions.submitPhoto({ jpeg: jpeg(128001), usesFX: false });
  assert.deepEqual(h.take(), []);
  assert.equal(h.at, 'mimic');
});

test('pause and unpause are overlays and never replace the screen', async () => {
  const h = await joined();
  h.cmd(4);
  const seq = h.view.seq;
  h.cmd(29);
  assert.deepEqual(h.game.overlays, [{ id: 'pause', data: {} }]);
  assert.equal(h.view.seq, seq);
  h.cmd(30);
  assert.deepEqual(h.game.overlays, []);
  assert.deepEqual(h.overlays, [['pause'], []]);
});

test('end of game: joined view, phase lobby; game id and scenario', async () => {
  const h = await joined();
  h.g2p({ TypeString: 'ClientGameIDMessage', GameID: '2abc' });
  assert.equal(h.game.state.phase, 'game');
  h.g2p({ TypeString: 'ClientScenarioTransitionCommandMessage', action: 9, time: 0, Scenario: 4 });
  assert.equal(h.game.state.scenario, 4);
  h.cmd(15);
  assert.equal(h.at, 'joined');
  assert.deepEqual(h.view.data, { playerId: 2, slotId: 0 });
  assert.equal(h.game.state.phase, 'lobby');
  h.g2p({ TypeString: 'ClientGameIDMessage', GameID: '1def' });
  assert.equal(h.game.state.phase, 'lobby');
});

test('ignored commands leave the view alone', async () => {
  const h = await joined();
  h.cmd(4);
  const seq = h.view.seq;
  for (const a of [1, 2, 3, 5, 7, 11, 13, 21, 22]) h.cmd(a);
  h.g2p({ TypeString: 'ClientSelectedUGCRequestMessage', action: 20, time: 0, UGCCreationId: 'x' });
  h.g2p({ TypeString: 'JoinLeaveMessage' });
  h.g2p({ TypeString: 'ClientReceiveChatLogMessage', PlayerId: 3, Message: 'hi' });
  h.g2p({ TypeString: 'NoSuchMessage' });
  assert.equal(h.view.seq, seq);
  assert.deepEqual(h.take(), []);
});

test('roster, online, ugc state and own profile on rejoin', async () => {
  const h = await joined();
  h.g2p({ TypeString: 'PlayerJoinedMessage', CurrentPlayerID: 3, OldPlayerID: 3, SlotID: 1 });
  h.g2p({ TypeString: 'PlayerNameQuizStateMessage', PlayerName: 'Bo', PlayerID: 3 });
  h.g2p({ TypeString: 'PlayerJoinedMessage', CurrentPlayerID: 4, OldPlayerID: 4, SlotID: 2 });
  h.g2p({ TypeString: 'PlayerLeftMessage', PlayerID: 4 });
  assert.deepEqual(h.game.state.players, [{ playerId: 3, slotId: 1, name: 'Bo' }]);
  h.g2p({ TypeString: 'ClientOnlineStateMessage', IsOnline: false });
  h.g2p({ TypeString: 'ClientUGCStateMessage', ugcState: 4 });
  assert.equal(h.game.state.online, false);
  assert.equal(h.game.state.ugcState, 4);
  h.g2p({ TypeString: 'ImageResourceContentTransferMessage', TransferID: 3, ImageGUID: 'pp', Acknowledge: true });
  h.content(3, jpeg(20));
  h.g2p({ TypeString: 'RejoiningClientOwnProfileMessage', Name: 'Ann', PortraitGUID: 'pp', CardID: 'CARD_3' });
  assert.equal(h.game.state.profile.name, 'Ann');
  assert.equal(h.game.state.profile.cardId, 'CARD_3');
  assert.equal(h.game.state.profile.portraitUrl, h.game.resources.url('pp'));
});

test('newest view wins: commands apply in arrival order', async () => {
  const h = await joined();
  h.g2p(holdingMsg(4));
  h.g2p(answerMsg(2, ['a']));
  h.g2p({ TypeString: 'ClientStartDrawingCommandMessage', action: 7, time: 0, CanvasGUID: 'c' });
  assert.equal(h.at, 'draw:canvas');
  // an answer confirm after the view moved on does nothing
  h.game.actions.confirmAnswer(1);
  assert.deepEqual(h.take(), []);
  const screens = h.views.slice(-3).map((v) => v.screen);
  assert.deepEqual(screens, ['holding', 'answer', 'draw']);
  const seqs = h.views.map((v) => v.seq);
  assert.deepEqual(seqs, seqs.slice().sort((a, b) => a - b));
});

test('base command form with a stale action is still handled by action', async () => {
  const h = await joined();
  h.g2p(cmd(31));
  assert.equal(h.at, 'name');
});
