import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

import {
  build, checkGameMessage, isValidGameMessage, PHONE_TO_GAME, GAME_TO_PHONE, SENTINEL,
} from '../../js/net/messages.js';

const S = (o) => JSON.stringify(o);

test('phone -> game builders produce exact JSON (fields and order of 5.5)', () => {
  const cases = [
    [build.clientRequestPlayerId('u-1'), '{"TypeString":"ClientRequestPlayerIDMessage","UID":"u-1"}'],
    [build.deviceInfo({ DeviceSize: 1, DeviceOS: 2, DeviceModel: 'iPhone', DeviceType: 'Handheld', DeviceUID: 'u', DeviceOperatingSystem: 'iOS 18.1' }),
      '{"TypeString":"DeviceInfoMessage","Response":10,"DeviceSize":1,"DeviceOS":2,"DeviceModel":"iPhone","DeviceType":"Handheld","DeviceUID":"u","DeviceOperatingSystem":"iOS 18.1"}'],
    [build.clientPlayerProfile({ name: 'Ann', locale: 'en-US', isGuest: false, cardId: 'CARD_3' }),
      '{"TypeString":"ClientPlayerProfileMessage","playerName":"Ann","uppercasePlayerName":"ANN","deviceCultureName":"en-US","ugcInfo":{"scenarioInt":-1,"scenario":-100,"challengeID":"","creationID":"","set":false},"isGuest":0,"playerCardId":"CARD_3"}'],
    [build.clientDisconnect(), '{"TypeString":"ClientDisconnectMessage"}'],
    [build.answerIndex(3), '{"TypeString":"AnswerIndexResponseMessage","AnswerIndex":3}'],
    [build.answerIndex(-2), '{"TypeString":"AnswerIndexResponseMessage","AnswerIndex":-2}'],
    [build.jokerConsumed(true), '{"TypeString":"JokerConsumedResponseMessage","Response":2,"jokerConsumed":true}'],
    [build.continuePressed(), '{"TypeString":"ContinuePressedResponseMessage","Response":3}'],
    [build.usePicPressed(), '{"TypeString":"UsePicPressedResponseMessage","Response":4}'],
    [build.textAnswer(''), '{"TypeString":"TextAnswerReponseMessage","Response":5,"Text":""}'],
    [build.startGameButtonPressed(), '{"TypeString":"StartGameButtonPressedResponseMessage","Response":9}'],
    [build.topDrawerPhotoTaken(), '{"TypeString":"TopDrawerPhotoTakenResponseMessage","Response":11}'],
    [build.trophyPhotoFX('g', false), '{"TypeString":"TrophyPhotoFXInfoMessage","ImageGUID":"g","usesFX":false}'],
    [build.trophyDrawingColouring('g', 32766, 6), '{"TypeString":"TrophyDrawingColouringInfoMessage","ImageGUID":"g","ColoursAvailable":32766,"ColoursUsed":6}'],
    [build.imageTransfer(1001, 'g', 2), '{"TypeString":"ClientImageResourceContentTransferMessage","TransferID":1001,"ImageGUID":"g","ImgType":2}'],
    [build.requestResource('g'), '{"TypeString":"RequestResourceMessage","ResourceGUID":"g"}'],
    [build.allResourcesReceived([{ GUID: 'a', Rqrmnt: 1 }]), '{"TypeString":"AllResourcesReceivedMessage","Requirements":[{"GUID":"a","Rqrmnt":1}]}'],
    [build.allResourcesReceived([]), '{"TypeString":"AllResourcesReceivedMessage","Requirements":[]}'],
    [build.clientSendChatLog(2, 'hi', 5), '{"TypeString":"ClientSendChatLogMessage","PlayerId":2,"Message":"hi","TickTimestamp":5}'],
  ];
  for (const [got, want] of cases) assert.equal(S(got), want);
  const seen = new Set(cases.map(([o]) => o.TypeString));
  for (const t of PHONE_TO_GAME) assert.ok(seen.has(t), 'builder covered: ' + t);
});

test('profile upper-cases with the locale', () => {
  const p = build.clientPlayerProfile({ name: 'istanbul', locale: 'tr-TR', isGuest: true, cardId: 'CARD_1' });
  assert.equal(p.uppercasePlayerName, 'İSTANBUL');
  assert.equal(p.isGuest, 1);
  const q = build.clientPlayerProfile({ name: 'x', locale: 'not a locale!', isGuest: 0, cardId: 'CARD_1' });
  assert.equal(q.uppercasePlayerName, 'X');
});

function valid(m) { assert.equal(checkGameMessage(m), null, JSON.stringify(m)); }
function invalid(m) { assert.notEqual(checkGameMessage(m), null, JSON.stringify(m)); }
const T = (TypeString, o = {}) => ({ TypeString, ...o });
const CMD = (TypeString, action, o = {}) => ({ TypeString, action, time: 0, ...o });

test('generic shape rules', () => {
  invalid(null);
  invalid([]);
  invalid({});
  invalid(T('NoSuchMessage'));
  assert.equal(isValidGameMessage(T('JoinLeaveMessage')), true);
});

test('InterfaceVersionMessage: not null', () => {
  valid(T('InterfaceVersionMessage', { InterfaceVersion: '2016-07-12-1645' }));
  valid(T('InterfaceVersionMessage', { InterfaceVersion: 'other' }));
  invalid(T('InterfaceVersionMessage', { InterfaceVersion: null }));
});

test('SessionStateMessage: SessionID > 0', () => {
  valid(T('SessionStateMessage', { SessionID: 1 }));
  invalid(T('SessionStateMessage', { SessionID: 0 }));
  invalid(T('SessionStateMessage', { SessionID: '5' }));
});

test('AssignPlayerIDAndSlotMessage: three ints >= 0', () => {
  const base = { PlayerID: 2, SlotID: 0, UDPPortOffset: 0, DisplayName: '', PSNID: '' };
  valid(T('AssignPlayerIDAndSlotMessage', base));
  invalid(T('AssignPlayerIDAndSlotMessage', { ...base, PlayerID: -1 }));
  invalid(T('AssignPlayerIDAndSlotMessage', { ...base, SlotID: -1 }));
  invalid(T('AssignPlayerIDAndSlotMessage', { ...base, UDPPortOffset: -1 }));
});

test('always-valid messages', () => {
  for (const t of ['DisconnectCommandMessage', 'ClientOnlineStateMessage', 'ClientUGCStateMessage', 'JoinLeaveMessage',
    'ClientReceiveChatLogMessage', 'QuestionDataMessage', 'ResourceRequirementsMessage']) valid(T(t));
});

test('roster messages', () => {
  valid(T('PlayerJoinedMessage', { CurrentPlayerID: 3, OldPlayerID: 3, SlotID: 1 }));
  invalid(T('PlayerJoinedMessage', { CurrentPlayerID: -1, OldPlayerID: 3, SlotID: 1 }));
  invalid(T('PlayerJoinedMessage', { CurrentPlayerID: 3, OldPlayerID: 3, SlotID: -1 }));
  valid(T('PlayerNameQuizStateMessage', { PlayerName: 'Bo', PlayerID: 0 }));
  invalid(T('PlayerNameQuizStateMessage', { PlayerName: SENTINEL.playerName, PlayerID: 3 }));
  invalid(T('PlayerNameQuizStateMessage', { PlayerName: 'Bo', PlayerID: -1 }));
  valid(T('PlayerLeftMessage', { PlayerID: 4 }));
  invalid(T('PlayerLeftMessage', { PlayerID: -1 }));
  valid(T('RejoiningClientOwnProfileMessage', { Name: 'A', PortraitGUID: 'g', CardID: 'CARD_1' }));
  invalid(T('RejoiningClientOwnProfileMessage', { Name: '', PortraitGUID: 'g', CardID: 'CARD_1' }));
  invalid(T('RejoiningClientOwnProfileMessage', { Name: 'A', PortraitGUID: 'g', CardID: '' }));
  valid(T('ClientGameIDMessage', { GameID: '2abc' }));
  invalid(T('ClientGameIDMessage', { GameID: '' }));
  valid(T('SyncJokersQuizStateMessage', { NumJokers: 0 }));
  invalid(T('SyncJokersQuizStateMessage', { NumJokers: -1 }));
});

test('content headers', () => {
  valid(T('ImageResourceContentTransferMessage', { TransferID: 1, ImageGUID: 'g', Acknowledge: true }));
  invalid(T('ImageResourceContentTransferMessage', { TransferID: 0, ImageGUID: 'g' }));
  invalid(T('ImageResourceContentTransferMessage', { TransferID: 1, ImageGUID: '' }));
  valid(T('PortraitThumbnailContentTransferMessage', { TransferID: 1, ClientUID: 'u' }));
  invalid(T('PortraitThumbnailContentTransferMessage', { TransferID: -7503, ClientUID: 'u' }));
  invalid(T('PortraitThumbnailContentTransferMessage', { TransferID: 1, ClientUID: '' }));
});

test('answer texts and their sentinels', () => {
  valid(T('AnswerTextQuizStateMessage', { AnswerText: 'x', PlayerID: 0 }));
  invalid(T('AnswerTextQuizStateMessage', { AnswerText: SENTINEL.answerText, PlayerID: 1 }));
  invalid(T('AnswerTextQuizStateMessage', { AnswerText: 'x', PlayerID: -1 }));
  valid(T('WouldYouRatherAnswerTextQuizMessage', { AnswerText: 'x', AnswerId: 1 }));
  invalid(T('WouldYouRatherAnswerTextQuizMessage', { AnswerText: SENTINEL.wyrText, AnswerId: 1 }));
  invalid(T('WouldYouRatherAnswerTextQuizMessage', { AnswerText: 'x', AnswerId: -1 }));
});

test('base command: action != -1', () => {
  valid(CMD('ClientQuizCommandMessage', 31));
  invalid(CMD('ClientQuizCommandMessage', -1));
  invalid(T('ClientQuizCommandMessage', { time: 0 }));
});

test('holding command rules', () => {
  const h = (o) => CMD('ClientHoldingScreenCommandMessage', 5, { HoldingScreenText: '', HoldingScreenType: 4, OtherPlayerIndex: 0, ShowPortraitPhotoControls: false, ...o });
  valid(h({}));
  valid(h({ HoldingScreenType: 3, OtherPlayerIndex: 2, HoldingScreenText: 'Bo' }));
  invalid(h({ action: 4 }));
  invalid(h({ HoldingScreenText: SENTINEL.holdingText }));
  invalid(h({ HoldingScreenType: 0 }));
  invalid(h({ HoldingScreenType: 2, OtherPlayerIndex: 0 }));
  invalid(h({ HoldingScreenType: 3, OtherPlayerIndex: 0 }));
});

test('QuickDraw holding: action 5 and type 5, sentinel text allowed', () => {
  const q = (o) => CMD('ClientQuickDrawHoldingScreenCommandMessage', 5, {
    HoldingScreenText: SENTINEL.holdingText, HoldingScreenType: 5, OtherPlayerIndex: 0,
    ImageGUID: 'g', Heading: 'h', Description: 'd', NumberOfDrawingsRemaining: 2, PlayerName: 'Bo', ...o,
  });
  valid(q({}));
  invalid(q({ HoldingScreenType: 4 }));
  invalid(q({ action: 7 }));
});

test('text entry, answer and the other command subclasses', () => {
  valid(CMD('ClientTextEntryCommandMessage', 8, { TitleText: 'q' }));
  invalid(CMD('ClientTextEntryCommandMessage', 8, { TitleText: SENTINEL.textEntryTitle }));
  invalid(CMD('ClientTextEntryCommandMessage', 5, { TitleText: 'q' }));
  const a = (o) => CMD('ClientAnswerCommandMessage', 3, { roundID: 2, questionBarText: 'q', jokersEnabled: false, ImageGUIDs: [], ...o });
  valid(a({}));
  invalid(a({ questionBarText: SENTINEL.questionBar }));
  invalid(a({ roundID: 512 }));
  invalid(a({ action: -1 }));
  const one = [
    ['ClientStartDrawingCommandMessage', 7], ['ClientTopDrawerTakePhotoCommandMessage', 12],
    ['ClientMimicThisCommandMessage', 23], ['ClientQuickDrawTakePhotoCommandMessage', 25],
    ['ClientScenarioTransitionCommandMessage', 9], ['ClientSelectedUGCRequestMessage', 20],
  ];
  for (const [t, act] of one) {
    valid(CMD(t, act));
    invalid(CMD(t, act + 1));
  }
  valid(CMD('ClientJokerCommandMessage', 2, { PreNumJokers: 0, CurNumJokers: 1 }));
  invalid(CMD('ClientJokerCommandMessage', 2, { PreNumJokers: -1, CurNumJokers: 1 }));
  invalid(CMD('ClientJokerCommandMessage', 2, { PreNumJokers: 0, CurNumJokers: -1 }));
  invalid(CMD('ClientJokerCommandMessage', 3, { PreNumJokers: 0, CurNumJokers: 1 }));
});

// Cross-check against protocol/messages.json (U8) when it exists.
const here = path.dirname(fileURLToPath(import.meta.url));
const catalogue = path.resolve(here, '../../../protocol/messages.json');

test('builders match protocol/messages.json', { skip: !existsSync(catalogue) && 'protocol/messages.json not present' }, () => {
  const cat = JSON.parse(readFileSync(catalogue, 'utf8'));
  const msgs = cat.messages || {};
  function fieldNames(name, seen = new Set()) {
    const m = msgs[name];
    if (!m || seen.has(name)) return [];
    seen.add(name);
    const base = m.base ? fieldNames(m.base, seen) : [];
    return [...base, ...(m.fields || []).filter((f) => !f.optional).map((f) => f.name)];
  }
  const ops = {
    eq: (a, b) => a === b, ne: (a, b) => a !== b, gt: (a, b) => a > b, ge: (a, b) => a >= b,
    lt: (a, b) => a < b, le: (a, b) => a <= b, in: (a, b) => b.includes(a),
    nonempty: (a) => typeof a === 'string' && a.length > 0,
  };
  const get = (o, f) => f.split('.').reduce((v, k) => (v == null ? v : v[k]), o);
  const holds = (r, o) => ops[r.op](get(o, r.field), r.value);
  const samples = {
    ClientRequestPlayerIDMessage: build.clientRequestPlayerId('u'),
    DeviceInfoMessage: build.deviceInfo({ DeviceSize: 1, DeviceOS: 5, DeviceModel: 'Desktop', DeviceType: 'Desktop', DeviceUID: 'u', DeviceOperatingSystem: 'Web' }),
    ClientPlayerProfileMessage: build.clientPlayerProfile({ name: 'a', locale: 'en', cardId: 'CARD_1' }),
    ClientDisconnectMessage: build.clientDisconnect(),
    AnswerIndexResponseMessage: build.answerIndex(1),
    JokerConsumedResponseMessage: build.jokerConsumed(false),
    ContinuePressedResponseMessage: build.continuePressed(),
    UsePicPressedResponseMessage: build.usePicPressed(),
    TextAnswerReponseMessage: build.textAnswer('x'),
    StartGameButtonPressedResponseMessage: build.startGameButtonPressed(),
    TopDrawerPhotoTakenResponseMessage: build.topDrawerPhotoTaken(),
    TrophyPhotoFXInfoMessage: build.trophyPhotoFX('g', true),
    TrophyDrawingColouringInfoMessage: build.trophyDrawingColouring('g', 1, 1),
    ClientImageResourceContentTransferMessage: build.imageTransfer(1, 'g', 1),
    RequestResourceMessage: build.requestResource('g'),
    AllResourcesReceivedMessage: build.allResourcesReceived([]),
    ClientSendChatLogMessage: build.clientSendChatLog(1, 'm', 1),
  };
  for (const [name, obj] of Object.entries(samples)) {
    assert.ok(msgs[name], 'catalogue lists ' + name);
    const want = fieldNames(name).filter((n) => n !== 'TypeString').sort();
    const got = Object.keys(obj).filter((n) => n !== 'TypeString').sort();
    assert.deepEqual(got, want, name);
    assert.equal(msgs[name].dir, 'p2g', name);
    for (const r of msgs[name].valid || []) {
      if (!ops[r.op]) continue;
      if (r.when && !holds(r.when, obj)) continue;
      assert.ok(holds(r, obj), name + ' rule ' + JSON.stringify(r));
    }
  }
  for (const name of GAME_TO_PHONE) assert.ok(msgs[name], 'catalogue lists ' + name);
});
