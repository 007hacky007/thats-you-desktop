// Screen state machine (contract 5.6): turns validated game messages and
// player actions into views and outgoing messages. Connection handling
// (handshake, reconnects, popups) lives in game.js.

import { createEmitter } from '../util/emitter.js';
import { nullLog } from '../util/log.js';
import { build, ACTION, HOLDING, ROUND, IMG } from '../net/messages.js';
import {
  createGameState, clampText, isCardId, NAME_MAX, TEXT_MAX, rosterJoined, rosterName, rosterLeft,
} from './state.js';

export const DRAFT_TIMEOUT_MS = 5000;

const HOLDING_VARIANT = {
  [HOLDING.BLANK]: 'wait',
  [HOLDING.OTHER_PLAYER_CHOOSING_UGC]: 'other-ugc',
  [HOLDING.OTHER_PLAYER_TAKING_PHOTO]: 'other-photo',
  [HOLDING.LOOK_AT_TV]: 'look-at-tv',
  [HOLDING.CUSTOM_MESSAGE]: 'custom',
  [HOLDING.QUICK_DRAW_MESSAGE]: 'custom',
  [HOLDING.REJOIN]: 'rejoin',
  [HOLDING.STRING_HACKS]: 'custom',
};

export function answerVariant(roundID) {
  switch (roundID) {
    case ROUND.ThatsYou:
    case ROUND.ThatsYouGC:
      return 'portraits';
    case ROUND.PictureThis:
      return 'images';
    case ROUND.TopDrawer:
    case ROUND.CanvasOpinion:
    case ROUND.MimicThis:
    case ROUND.QuickDraw:
      return 'drawings';
    case ROUND.BlankYou:
    case ROUND.WouldYouRather:
      return 'texts';
    default:
      return 'images';
  }
}

const str = (v) => (typeof v === 'string' ? v : '');
const int = (v, d = 0) => (Number.isInteger(v) ? v : d);

// deps: { clock, log, locale, profile, uploads, store, sendMsg(obj) -> bool,
//         deviceFields() -> DeviceInfo fields, draftTimeoutMs }
export function createMachine(deps) {
  const { clock, log = nullLog, profile, uploads, store, sendMsg } = deps;
  const locale = deps.locale || 'en-US';
  const draftTimeoutMs = deps.draftTimeoutMs || DRAFT_TIMEOUT_MS;
  const em = createEmitter({ onError: (e) => log.error('listener: ' + (e && e.stack || e)) });

  const state = createGameState();
  let view = { screen: 'connect', variant: '', data: { state: 'noparams' }, seq: 0 };
  let paused = false;
  let commandViewThisConnection = false;

  let answer = null; // { variant, roundID, question, choices, selected, jokerOn, jokersEnabled }
  let blankTexts = []; // [{ label, text }]
  let wyrTexts = []; // [{ id, text }]
  let latestText = '';
  let draw = null; // { variant, imgType, submitted }
  let mimic = null; // { submitted }
  let quickdrawImage = '';
  let draft = null; // profile draft { name, isGuest, cardId, jpeg, usesFX, portraitUrl }
  let draftProvider = null;

  // ---------------------------------------------------------------- output

  function setView(screen, variant, data, fromCommand = false) {
    view = { screen, variant: variant || '', data: data || {}, seq: view.seq + 1 };
    if (fromCommand) commandViewThisConnection = true;
    em.emit('view', view);
  }

  function holding(variant, data, fromCommand = false) {
    setView('holding', variant, { text: '', playerName: '', ...data }, fromCommand);
  }

  function changed() {
    em.emit('state', state);
  }

  function setPaused(on) {
    if (paused === on) return;
    paused = on;
    em.emit('overlays');
  }

  // ---------------------------------------------------------------- profile

  function editProfile() {
    sendMsg(build.deviceInfo(deps.deviceFields()));
    const saved = profile.saved();
    if (saved.profileDone && saved.name && isCardId(saved.cardId) && saved.portrait && saved.portrait.length) {
      draft = {
        name: saved.name, isGuest: saved.isGuest, cardId: saved.cardId,
        jpeg: saved.portrait, usesFX: false, portraitUrl: portraitUrlFor(saved.portrait),
      };
      setView('confirm', '', confirmData(), true);
      return;
    }
    draft = {
      name: clampText(saved.name, NAME_MAX), isGuest: saved.isGuest, cardId: isCardId(saved.cardId) ? saved.cardId : '',
      jpeg: null, usesFX: false, portraitUrl: null,
    };
    setView('name', '', nameData(), true);
  }

  function portraitUrlFor(jpeg) {
    return store.put('local:portrait-draft', jpeg, { own: true });
  }

  function nameData() {
    return { name: draft.name, isGuest: draft.isGuest, maxLength: NAME_MAX };
  }

  function confirmData() {
    return { name: draft.name, cardId: draft.cardId, portraitUrl: draft.portraitUrl };
  }

  function ensureDraft() {
    if (!draft) draft = { name: '', isGuest: false, cardId: '', jpeg: null, usesFX: false, portraitUrl: null };
    return draft;
  }

  const profileActions = {
    profileName(name, isGuest) {
      const n = clampText(String(name === undefined || name === null ? '' : name).trim(), NAME_MAX);
      if (!n) {
        log.warn('empty name ignored');
        return;
      }
      const d = ensureDraft();
      d.name = n;
      d.isGuest = !!isGuest;
      setView('card', '', { cardId: d.cardId });
    },
    profileCard(cardId) {
      if (!isCardId(cardId)) {
        log.warn('bad card id ' + cardId);
        return;
      }
      const d = ensureDraft();
      d.cardId = cardId;
      setView('selfie', '', { facing: 'user' });
    },
    profilePhoto({ jpeg, usesFX } = {}) {
      if (!(jpeg instanceof Uint8Array) || jpeg.length === 0) {
        log.warn('no portrait');
        return;
      }
      const d = ensureDraft();
      d.jpeg = jpeg;
      d.usesFX = !!usesFX;
      d.portraitUrl = portraitUrlFor(jpeg);
      setView('confirm', '', confirmData());
    },
    profileEdit(step) {
      const d = ensureDraft();
      if (step === 'name') setView('name', '', nameData());
      else if (step === 'card') setView('card', '', { cardId: d.cardId });
      else if (step === 'photo') setView('selfie', '', { facing: 'user' });
      else log.warn('unknown profile step ' + step);
    },
    profileConfirm() {
      const d = draft;
      if (!d || !d.name || !isCardId(d.cardId) || !d.jpeg) {
        log.warn('profile incomplete');
        return;
      }
      const msg = build.clientPlayerProfile({ name: d.name, locale, isGuest: d.isGuest, cardId: d.cardId });
      const up = uploads.portrait({ jpeg: d.jpeg, usesFX: d.usesFX, profile: msg });
      if (!up) return;
      profile.saveProfile({ name: d.name, cardId: d.cardId, isGuest: d.isGuest, portraitId: up.guid, jpeg: d.jpeg });
      state.profile = { name: d.name, cardId: d.cardId, isGuest: d.isGuest, portraitUrl: store.url(up.guid) };
      changed();
      holding('general');
    },
  };

  // Called after SessionStateMessage with the result of the session check.
  function onSession() {
    const s = profile.saved();
    let portraitUrl = null;
    if (s.profileDone && s.portrait && s.portraitId) portraitUrl = store.put(s.portraitId, s.portrait, { own: true });
    state.profile = { name: s.name, cardId: s.cardId, isGuest: s.isGuest, portraitUrl };
    changed();
  }

  // ---------------------------------------------------------------- answers

  function answerChoices() {
    if (!answer) return [];
    if (answer.roundID === ROUND.BlankYou) {
      return blankTexts.map((t, i) => ({ position: i + 1, guid: null, text: t.text, value: t.label }));
    }
    if (answer.roundID === ROUND.WouldYouRather) {
      return wyrTexts.slice().sort((a, b) => a.id - b.id)
        .map((t, i) => ({ position: i + 1, guid: null, text: t.text, value: t.id }));
    }
    return answer.guids.map((g, i) => ({ position: i + 1, guid: g, text: '', value: i + 1 }));
  }

  function answerData() {
    return {
      roundType: answer.roundID,
      question: answer.question,
      jokersEnabled: answer.jokersEnabled,
      jokers: state.jokers,
      choices: answer.choices,
      selected: answer.selected,
      jokerOn: answer.jokerOn,
    };
  }

  function refreshAnswer() {
    if (!answer) return;
    answer.choices = answerChoices();
    if (answer.selected !== null && !answer.choices.some((c) => c.position === answer.selected)) answer.selected = null;
    if (view.screen === 'answer' && !answer.confirmed) setView('answer', answer.variant, answerData());
  }

  function selectedValue() {
    if (!answer || answer.selected === null) return -2;
    const c = answer.choices.find((x) => x.position === answer.selected);
    return c ? c.value : -2;
  }

  const answerActions = {
    selectAnswer(position) {
      if (view.screen !== 'answer' || !answer) return;
      if (!answer.choices.some((c) => c.position === position)) return;
      answer.selected = position;
      setView('answer', answer.variant, answerData());
    },
    confirmAnswer(position) {
      if (view.screen !== 'answer' || !answer) return;
      if (position !== undefined && position !== null) {
        if (!answer.choices.some((c) => c.position === position)) return;
        answer.selected = position;
      }
      if (answer.selected === null) return;
      sendMsg(build.answerIndex(selectedValue()));
      answer.confirmed = true;
      holding('general');
    },
    setJoker(on) {
      if (!answer || !answer.jokersEnabled) return;
      const v = !!on && state.jokers > 0;
      if (answer.jokerOn === v) return;
      answer.jokerOn = v;
      if (view.screen === 'answer') setView('answer', answer.variant, answerData());
    },
  };

  // ---------------------------------------------------------------- creation

  // Ask the mounted creation screen for its draft (forced send).
  function forced(kind, onPayload) {
    const provider = draftProvider;
    const mySeq = view.seq;
    const finish = () => {
      if (view.seq === mySeq) holding('look-at-tv', {}, true);
    };
    if (!provider) {
      log.info('forced ' + kind + ': no draft');
      finish();
      return Promise.resolve(false);
    }
    let p;
    try {
      p = Promise.resolve(provider(kind));
    } catch (e) {
      p = Promise.reject(e);
    }
    let timer = null;
    const timeout = new Promise((resolve) => {
      timer = clock.setTimeout(() => {
        log.warn('forced ' + kind + ': draft timed out');
        resolve(null);
      }, draftTimeoutMs);
    });
    return Promise.race([p, timeout]).catch((e) => {
      log.warn('forced ' + kind + ': ' + (e && e.message));
      return null;
    }).then((payload) => {
      clock.clearTimeout(timer);
      let sent = false;
      if (payload && payload.jpeg) sent = !!onPayload(payload);
      finish();
      return sent;
    });
  }

  function sendDrawing(quick) {
    if (draw && draw.submitted) {
      holding('look-at-tv', {}, true);
      return;
    }
    const imgType = quick ? IMG.QuickDraw : (draw ? draw.imgType : IMG.PlayerDrawing);
    if (draw) draw.submitted = true;
    forced('drawing', (pl) => uploads.drawing({
      jpeg: pl.jpeg, coloursAvailable: pl.coloursAvailable, coloursUsed: pl.coloursUsed, imgType,
    }));
  }

  function sendMimic() {
    if (mimic && mimic.submitted) {
      holding('look-at-tv', {}, true);
      return;
    }
    if (mimic) mimic.submitted = true;
    forced('photo', (pl) => uploads.photo({ jpeg: pl.jpeg, usesFX: pl.usesFX, imgType: IMG.MimicPhoto }));
  }

  const createActions = {
    textChanged(text) {
      latestText = clampText(text, TEXT_MAX);
    },
    submitText(text) {
      if (text !== undefined) latestText = clampText(text, TEXT_MAX);
      if (view.screen === 'text') holding('look-at-tv');
    },
    submitDrawing({ jpeg, coloursAvailable, coloursUsed } = {}) {
      if (view.screen !== 'draw' || !draw || draw.submitted) return;
      const up = uploads.drawing({ jpeg, coloursAvailable, coloursUsed, imgType: draw.imgType });
      if (!up) return;
      draw.submitted = true;
      holding('look-at-tv');
    },
    photoTaken() {
      if (view.screen === 'photo' && view.variant === 'topdrawer') sendMsg(build.topDrawerPhotoTaken());
    },
    submitPhoto({ jpeg, usesFX } = {}) {
      if (view.screen === 'photo' && view.variant === 'topdrawer') {
        if (uploads.photo({ jpeg, usesFX, imgType: IMG.DrawingCanvas, usePic: true })) holding('general');
      } else if (view.screen === 'photo' && view.variant === 'quickdraw') {
        if (uploads.photo({ jpeg, usesFX, imgType: IMG.QuickDraw, usePic: true })) holding('look-at-tv');
      } else if (view.screen === 'mimic' && mimic && !mimic.submitted) {
        if (uploads.photo({ jpeg, usesFX, imgType: IMG.MimicPhoto })) {
          mimic.submitted = true;
          holding('look-at-tv');
        }
      }
    },
  };

  const flowActions = {
    continuePressed() {
      if (view.screen !== 'continue' || view.variant !== 'continue') return;
      sendMsg(build.continuePressed());
      holding('general');
    },
    startPressed() {
      if (view.screen !== 'continue' || view.variant !== 'start') return;
      sendMsg(build.startGameButtonPressed());
      holding('general');
    },
  };

  // ---------------------------------------------------------------- commands

  function baseCommand(m) {
    switch (m.action) {
      case ACTION.EditProfile: editProfile(); return;
      case ACTION.SendAnswer:
        sendMsg(build.answerIndex(selectedValue()));
        holding('general', {}, true);
        return;
      case ACTION.SendJoker:
        sendMsg(build.jokerConsumed(!!(answer && answer.jokerOn)));
        holding('general', {}, true);
        return;
      case ACTION.SendTextEntry:
        sendMsg(build.textAnswer(latestText));
        holding('look-at-tv', {}, true);
        return;
      case ACTION.SendDrawing: sendDrawing(false); return;
      case ACTION.SendQuickDrawDrawing: sendDrawing(true); return;
      case ACTION.SendMimicThisPhoto: sendMimic(); return;
      case ACTION.QuickDrawDrawing:
        draw = { variant: 'quickdraw', imgType: IMG.QuickDraw, submitted: false };
        setView('draw', 'quickdraw', { backgroundGuid: quickdrawImage }, true);
        return;
      case ACTION.EndOfGameMessage:
        state.phase = 'lobby';
        changed();
        setView('joined', '', { playerId: state.playerId, slotId: state.slotId }, true);
        return;
      case ACTION.Pause: setPaused(true); return;
      case ACTION.Unpause: setPaused(false); return;
      case ACTION.ContinueButton: setView('continue', 'continue', {}, true); return;
      case ACTION.StartGameButtonPrompt: setView('continue', 'start', {}, true); return;
      default:
        log.debug('command ' + m.action + ' ignored');
    }
  }

  function handle(m) {
    switch (m.TypeString) {
      case 'ClientQuizCommandMessage':
        baseCommand(m);
        return true;
      case 'ClientHoldingScreenCommandMessage': {
        const type = m.HoldingScreenType;
        const text = str(m.HoldingScreenText);
        const variant = HOLDING_VARIANT[type] || 'general';
        holding(variant, { text, playerName: type === HOLDING.OTHER_PLAYER_TAKING_PHOTO ? text : '' }, true);
        return true;
      }
      case 'ClientQuickDrawHoldingScreenCommandMessage':
        quickdrawImage = str(m.ImageGUID);
        setView('quickdraw-holding', '', {
          imageGuid: quickdrawImage,
          heading: str(m.Heading),
          description: str(m.Description),
          remaining: int(m.NumberOfDrawingsRemaining),
          playerName: str(m.PlayerName),
        }, true);
        return true;
      case 'ClientTextEntryCommandMessage':
        latestText = '';
        setView('text', '', { title: str(m.TitleText), maxLength: TEXT_MAX, text: '' }, true);
        return true;
      case 'ClientAnswerCommandMessage': {
        answer = {
          variant: answerVariant(m.roundID),
          roundID: m.roundID,
          question: str(m.questionBarText) || state.question,
          guids: (Array.isArray(m.ImageGUIDs) ? m.ImageGUIDs : []).filter((g) => typeof g === 'string'),
          jokersEnabled: !!m.jokersEnabled,
          selected: null,
          jokerOn: false,
          confirmed: false,
          choices: [],
        };
        answer.choices = answerChoices();
        setView('answer', answer.variant, answerData(), true);
        return true;
      }
      case 'ClientStartDrawingCommandMessage': {
        const variant = state.roundType === ROUND.TopDrawer ? 'topdrawer' : 'canvas';
        draw = { variant, imgType: IMG.PlayerDrawing, submitted: false };
        setView('draw', variant, { backgroundGuid: str(m.CanvasGUID) }, true);
        return true;
      }
      case 'ClientTopDrawerTakePhotoCommandMessage': {
        const text = str(m.CommandText).split('{0}').join(str(m.PlayerName));
        setView('photo', 'topdrawer', { commandText: text, facing: 'environment' }, true);
        return true;
      }
      case 'ClientQuickDrawTakePhotoCommandMessage':
        setView('photo', 'quickdraw', { commandText: str(m.CommandText), facing: 'user' }, true);
        return true;
      case 'ClientMimicThisCommandMessage':
        mimic = { submitted: false };
        setView('mimic', '', { referenceGuid: str(m.MimicPictureGUID), facing: 'user' }, true);
        return true;
      case 'ClientScenarioTransitionCommandMessage':
        state.scenario = int(m.Scenario, -1);
        changed();
        return true;
      case 'ClientJokerCommandMessage':
        state.jokers = m.CurNumJokers;
        changed();
        setView('joker', '', { pre: m.PreNumJokers, cur: m.CurNumJokers }, true);
        return true;
      case 'ClientSelectedUGCRequestMessage':
        log.info('UGC request ignored (not ported)');
        return true;
      case 'QuestionDataMessage':
        state.roundType = int(m.RoundType);
        state.question = str(m.QuestionText);
        blankTexts = [];
        wyrTexts = [];
        changed();
        return true;
      case 'AnswerTextQuizStateMessage':
        blankTexts = blankTexts.filter((t) => t.label !== m.PlayerID);
        blankTexts.push({ label: m.PlayerID, text: m.AnswerText });
        if (answer && answer.roundID === ROUND.BlankYou) refreshAnswer();
        return true;
      case 'WouldYouRatherAnswerTextQuizMessage':
        wyrTexts = wyrTexts.filter((t) => t.id !== m.AnswerId);
        wyrTexts.push({ id: m.AnswerId, text: m.AnswerText });
        if (answer && answer.roundID === ROUND.WouldYouRather) refreshAnswer();
        return true;
      case 'SyncJokersQuizStateMessage':
        state.jokers = m.NumJokers;
        if (answer && state.jokers === 0) answer.jokerOn = false;
        changed();
        if (view.screen === 'answer' && answer && !answer.confirmed) setView('answer', answer.variant, answerData());
        return true;
      case 'ClientGameIDMessage':
        if (m.GameID[0] === '2') state.phase = 'game';
        else if (m.GameID[0] === '1') state.phase = 'lobby';
        state.gameId = m.GameID;
        changed();
        return true;
      case 'RejoiningClientOwnProfileMessage': {
        const url = m.PortraitGUID ? store.url(m.PortraitGUID) : null;
        state.profile = { ...state.profile, name: m.Name, cardId: m.CardID, portraitUrl: url || state.profile.portraitUrl };
        changed();
        return true;
      }
      case 'PlayerJoinedMessage': rosterJoined(state, m); changed(); return true;
      case 'PlayerNameQuizStateMessage': rosterName(state, m); changed(); return true;
      case 'PlayerLeftMessage': rosterLeft(state, m); changed(); return true;
      case 'ClientOnlineStateMessage': state.online = !!m.IsOnline; changed(); return true;
      case 'ClientUGCStateMessage': state.ugcState = int(m.ugcState); changed(); return true;
      case 'JoinLeaveMessage':
      case 'ClientReceiveChatLogMessage':
        return true;
      default:
        return false;
    }
  }

  // ---------------------------------------------------------------- connection hooks

  function onAssigned(m, first) {
    state.playerId = m.PlayerID;
    state.slotId = m.SlotID;
    state.connected = true;
    if (state.phase === 'none') state.phase = 'lobby';
    profile.setPlayerId(m.PlayerID);
    changed();
    if (first && !commandViewThisConnection) setView('joined', '', { playerId: m.PlayerID, slotId: m.SlotID });
  }

  function newConnection() {
    commandViewThisConnection = false;
  }

  function setConnected(on) {
    if (state.connected === on) return;
    state.connected = on;
    changed();
  }

  // Back to the connect screen (disconnect, refusal, leave).
  function toConnect(connectState) {
    setPaused(false);
    answer = null;
    draw = null;
    mimic = null;
    draft = null;
    state.connected = false;
    changed();
    setView('connect', '', { state: connectState });
  }

  function showConnect(connectState) {
    setView('connect', '', { state: connectState });
  }

  return {
    get view() { return view; },
    get paused() { return paused; },
    state,
    on: (ev, fn) => em.on(ev, fn),
    handle,
    onSession,
    onAssigned,
    newConnection,
    setConnected,
    toConnect,
    showConnect,
    setDraftProvider(fn) {
      draftProvider = typeof fn === 'function' ? fn : null;
    },
    actions: { ...profileActions, ...answerActions, ...createActions, ...flowActions },
  };
}
