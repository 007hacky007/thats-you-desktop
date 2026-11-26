// createGame (contract 5.4): connection policy, handshake, reconnects and
// popups around the screen state machine. The UI only talks to this object.

import { createEmitter } from '../util/emitter.js';
import { realClock } from '../util/clock.js';
import { createStorage } from '../util/storage.js';
import { nullLog } from '../util/log.js';
import { sameJoin } from '../net/params.js';
import { createLink, LINK_TIMING } from '../net/link.js';
import { createRtcTransport, watchVisibility, browserEnvironment } from '../net/transport.js';
import { build, checkGameMessage, INTERFACE_VERSION, DISCONNECT } from '../net/messages.js';
import { createBlobStore, createDownloads, createUploads, defaultUuid } from '../net/content.js';
import { createResources } from '../net/resources.js';
import { createProfile, REJOIN_MAX_AGE_MS } from './profile.js';
import { deviceInfoFields } from './device.js';
import { createMachine } from './machine.js';

export const GAME_TIMING = Object.freeze({
  handshake: 17000, // InterfaceVersion + SessionState after joined
  assign: 20000, // AssignPlayerIDAndSlotMessage after ClientRequestPlayerIDMessage
  reconnectDelays: [0, 1000, 2000, 4000, 8000],
  reconnectEvery: 15000,
  reconnectGiveUp: 120000,
  silentWake: 17000,
  leaveDrain: 500,
  touchJoinEvery: 60000,
});

const REFUSAL_POPUP = { 1: 'notallowed', 2: 'gamefull', 3: 'onlyone' };
const DISCONNECT_POPUP = {
  [DISCONNECT.GameFull]: 'gamefull',
  [DISCONNECT.VersionDisparity]: 'version',
  [DISCONNECT.OnlyOnePlayer]: 'onlyone',
  [DISCONNECT.ConnectionNotAllowed]: 'notallowed',
};

function paramsOf(o) {
  return { h: o.h, p: Number(o.p), u: o.u, w: o.w, f: o.f, t: o.t };
}

export function createGame(opts = {}) {
  const storage = opts.storage || createStorage();
  const clock = opts.clock || realClock;
  const log = opts.log || nullLog;
  const transportFactory = opts.transportFactory || createRtcTransport;
  const env = opts.device || browserEnvironment();
  const locale = opts.locale || env.language || 'en-US';
  const uuid = opts.uuid || defaultUuid;
  const timing = { ...GAME_TIMING, ...(opts.timing || {}) };
  const linkTiming = { ...LINK_TIMING, ...(opts.linkTiming || {}) };
  const visibility = opts.visibility === undefined ? watchVisibility : opts.visibility;

  const em = createEmitter({ onError: (e) => log.error('listener: ' + (e && e.stack || e)) });
  const profile = createProfile({ storage, uuid, clock });
  const store = createBlobStore({ makeUrl: opts.makeUrl, revokeUrl: opts.revokeUrl, log });
  const thumbs = createBlobStore({ makeUrl: opts.makeUrl, revokeUrl: opts.revokeUrl, log });

  let link = null;
  let uid = '';
  let params = null; // join params in use (from the QR or lastJoin)
  let everJoined = false;
  let popup = null;
  let recon = null; // { startedAt, attempt, retry, deadline }
  let conn = null; // per link: { gotVersion, gotSession, sessionId, requested, assigned, timer, lastTouch }
  let unwatch = null;

  const sendMsg = (m) => !!link && link.sendMsg(m);
  const uploads = createUploads({ getLink: () => link, store, log, random: opts.random, uuid });
  const resources = createResources({ store, sendMsg, clock, log });
  const downloads = createDownloads({
    clock, log, onImage: (g, b) => store.put(g, b), onThumbnail: (u, b) => thumbs.put(u, b),
  });
  const machine = createMachine({
    clock, log, locale, profile, uploads, store, sendMsg,
    deviceFields: () => deviceInfoFields(env, uid),
    draftTimeoutMs: opts.draftTimeoutMs,
  });

  machine.on('view', (v) => em.emit('view', v));
  machine.on('state', (s) => em.emit('state', s));
  machine.on('overlays', () => em.emit('overlays', overlays()));

  function overlays() {
    const list = [];
    if (machine.paused) list.push({ id: 'pause', data: {} });
    if (recon) list.push({ id: 'reconnecting', data: { since: recon.startedAt } });
    return list;
  }

  function setPopup(reason, reconnect = false) {
    popup = reason ? { reason, reconnect: !!reconnect } : null;
    em.emit('popup', popup);
  }

  // ---------------------------------------------------------------- links

  function clearConnTimer() {
    if (conn && conn.timer !== null) {
      clock.clearTimeout(conn.timer);
      conn.timer = null;
    }
  }

  function connTimer(ms, why) {
    clearConnTimer();
    const my = link;
    conn.timer = clock.setTimeout(() => {
      conn.timer = null;
      if (link === my) {
        log.warn('timeout: ' + why);
        my.close('timeout');
      }
    }, ms);
  }

  function dropLink() {
    const l = link;
    clearConnTimer();
    link = null;
    conn = null;
    if (l) l.close('local');
  }

  function connectOnce(mode) {
    dropLink();
    downloads.reset();
    resources.reset();
    machine.newConnection();
    conn = { mode, gotVersion: false, gotSession: false, sessionId: 0, requested: false, assigned: false, timer: null, lastTouch: 0 };
    const my = createLink({ params, uid, transportFactory, clock, log, timing: linkTiming });
    link = my;
    my.on('pending', () => {
      if (link === my && mode === 'first') machine.showConnect('waiting');
    });
    my.on('joined', () => link === my && onJoined());
    my.on('msg', (m) => link === my && onMsg(m));
    my.on('content', (tid, data) => link === my && downloads.binary(tid, data));
    my.on('end', (e) => link === my && onEnd(e));
    my.on('state', () => em.emit('link', my.state));
    if (mode === 'first') machine.showConnect('connecting');
    log.info('connecting (' + mode + ')');
    my.connect();
  }

  function onJoined() {
    everJoined = true;
    profile.saveLastJoin(params, true);
    conn.lastTouch = clock.now();
    connTimer(timing.handshake, 'InterfaceVersion/SessionState');
  }

  function maybeRequestId() {
    if (!conn.gotVersion || !conn.gotSession || conn.requested) return;
    const restored = profile.applySession(conn.sessionId);
    log.info('session ' + (restored ? 'restored' : 'new'));
    machine.onSession(restored);
    conn.requested = true;
    sendMsg(build.clientRequestPlayerId(uid));
    connTimer(timing.assign, 'AssignPlayerIDAndSlotMessage');
  }

  function onMsg(m) {
    const why = checkGameMessage(m);
    if (why) {
      log.debug('dropped: ' + why);
      return;
    }
    const now = clock.now();
    if (now - conn.lastTouch >= timing.touchJoinEvery) {
      conn.lastTouch = now;
      profile.touchJoin();
    }
    switch (m.TypeString) {
      case 'InterfaceVersionMessage':
        if (conn.gotVersion) return;
        if (m.InterfaceVersion !== INTERFACE_VERSION) {
          log.warn('interface version mismatch');
          endSession('version', false);
          return;
        }
        conn.gotVersion = true;
        maybeRequestId();
        return;
      case 'SessionStateMessage':
        if (conn.gotSession) return;
        conn.gotSession = true;
        conn.sessionId = m.SessionID;
        maybeRequestId();
        return;
      case 'AssignPlayerIDAndSlotMessage': {
        const first = !conn.assigned;
        conn.assigned = true;
        clearConnTimer();
        if (recon) stopReconnect();
        machine.onAssigned(m, first);
        return;
      }
      case 'DisconnectCommandMessage': {
        const code = Number.isInteger(m.Msg) ? m.Msg : 0;
        log.info('disconnect command ' + code);
        profile.setActive(false);
        if (code === DISCONNECT.ConnectionFailed) endSession('failed', true);
        else endSession(DISCONNECT_POPUP[code] || 'failed', false);
        return;
      }
      case 'ResourceRequirementsMessage':
        resources.onManifest(m);
        return;
      case 'ImageResourceContentTransferMessage':
      case 'PortraitThumbnailContentTransferMessage':
        downloads.header(m);
        return;
      default:
        if (!machine.handle(m)) log.debug('unhandled ' + m.TypeString);
    }
  }

  // Close the session for good: back to connect with an optional popup.
  function endSession(reason, reconnect) {
    stopReconnect();
    dropLink();
    machine.toConnect('ready');
    setPopup(reason, reconnect);
  }

  function onEnd(e) {
    clearConnTimer();
    const wasAssigned = conn && conn.assigned;
    link = null;
    conn = null;
    machine.setConnected(false);
    if (e.cause === 'refused') {
      profile.setActive(false);
      endSession(REFUSAL_POPUP[e.reason] || 'failed', false);
      return;
    }
    if (e.cause === 'bye' && e.reason === 'replaced') {
      profile.setActive(false);
      endSession('replaced', true);
      return;
    }
    if (e.cause === 'bye' && e.reason === 'shutdown') {
      profile.setActive(false);
      endSession('shutdown', false);
      return;
    }
    const loss = e.cause === 'transport' || e.cause === 'death' || e.cause === 'bye';
    if (recon) {
      scheduleAttempt();
      return;
    }
    if (loss && (e.wasJoined || wasAssigned)) {
      startReconnect();
      return;
    }
    endSession(everJoined ? 'failed' : 'nogame', everJoined);
  }

  // ---------------------------------------------------------------- reconnect

  function startReconnect() {
    log.info('connection lost, reconnecting');
    recon = { startedAt: clock.now(), attempt: 0, retry: null, deadline: null };
    recon.deadline = clock.setTimeout(giveUp, timing.reconnectGiveUp);
    em.emit('overlays', overlays());
    scheduleAttempt();
  }

  function scheduleAttempt() {
    if (!recon) return;
    const d = recon.attempt < timing.reconnectDelays.length ? timing.reconnectDelays[recon.attempt] : timing.reconnectEvery;
    recon.attempt++;
    if (recon.retry !== null) clock.clearTimeout(recon.retry);
    recon.retry = clock.setTimeout(() => {
      recon.retry = null;
      connectOnce('reconnect');
    }, d);
  }

  function stopReconnect() {
    if (!recon) return;
    if (recon.retry !== null) clock.clearTimeout(recon.retry);
    clock.clearTimeout(recon.deadline);
    recon = null;
    em.emit('overlays', overlays());
  }

  function giveUp() {
    log.warn('reconnect gave up');
    endSession('failed', true);
  }

  // The page came back to the foreground.
  function wake() {
    if (recon && recon.retry !== null) {
      clock.clearTimeout(recon.retry);
      recon.retry = null;
      connectOnce('reconnect');
      return;
    }
    if (link && link.state === 'joined' && link.silentFor() > timing.silentWake) {
      log.info('link silent after wake');
      link.close('transport');
    }
  }

  // ---------------------------------------------------------------- API

  function boot(p) {
    uid = profile.uid();
    if (visibility && !unwatch) unwatch = visibility(() => wake());
    const last = profile.lastJoin();
    if (p) {
      params = paramsOf(p);
      if (last && sameJoin(last, p) && last.active === true && clock.now() - last.at < REJOIN_MAX_AGE_MS) {
        connectOnce('first');
        return;
      }
      machine.showConnect('ready');
      return;
    }
    if (last) {
      params = paramsOf(last);
      machine.showConnect('idle');
      return;
    }
    params = null;
    machine.showConnect('noparams');
  }

  function play() {
    if (!params) {
      const last = profile.lastJoin();
      if (!last) return;
      params = paramsOf(last);
    }
    if (link && link.state !== 'closed') return;
    stopReconnect();
    setPopup(null);
    connectOnce('first');
  }

  async function leave() {
    stopReconnect();
    const l = link;
    if (l && l.state === 'joined') {
      l.sendMsg(build.clientDisconnect());
      await l.drain(timing.leaveDrain);
    }
    if (link === l) dropLink();
    profile.setActive(false);
    machine.toConnect('ready');
    setPopup(null);
  }

  const actions = {
    play,
    reconnect: play,
    dismissPopup: () => setPopup(null),
    leave,
    ...machine.actions,
  };

  return {
    boot,
    wake,
    get view() { return machine.view; },
    get overlays() { return overlays(); },
    get popup() { return popup; },
    get state() { return machine.state; },
    resources: {
      url: (guid) => store.url(guid),
      has: (guid) => store.has(guid),
      on: (ev, fn) => store.on(ev, fn),
    },
    thumbnails: {
      url: (clientUid) => thumbs.url(clientUid),
      has: (clientUid) => thumbs.has(clientUid),
      on: (ev, fn) => thumbs.on(ev, fn),
    },
    link: {
      get state() { return link ? link.state : 'idle'; },
      lastRxAgo: () => (link ? link.lastRxAgo() : 0),
    },
    get uid() { return uid; },
    on: (ev, fn) => em.on(ev, fn),
    setDraftProvider: (fn) => machine.setDraftProvider(fn),
    actions,
    destroy() {
      stopReconnect();
      dropLink();
      if (unwatch) unwatch();
      unwatch = null;
    },
  };
}
