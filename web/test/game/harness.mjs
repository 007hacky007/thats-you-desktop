// Drives createGame with a fake transport and a fake clock, playing the
// game side by hand.

import { createGame } from '../../js/game/game.js';
import { createFakeClock, flush } from '../../js/util/clock.js';
import { createStorage, createMemoryBackend } from '../../js/util/storage.js';
import { createLog } from '../../js/util/log.js';
import { encodeChunks } from '../../js/net/frames.js';
import { createFakeTransportFactory } from '../net/fake-transport.mjs';

export const FP = 'AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8';
export const PARAMS = { h: '192.168.1.23', p: 47000, u: 'AbCd1234', w: 'abcdefghijklmnopqrstuvwx', f: FP, t: 'TOKEN0123456789' };
export const IPHONE_UA = 'Mozilla/5.0 (iPhone; CPU iPhone OS 18_1 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/18.1 Mobile/15E148 Safari/604.1';
export const START = 1_800_000_000_000;

export function jpeg(n = 100, seed = 7) {
  const b = new Uint8Array(n);
  for (let i = 0; i < n; i++) b[i] = (i * 13 + seed) & 0xff;
  b[0] = 0xff;
  b[1] = 0xd8;
  return b;
}

export const cmd = (action, extra = {}) => ({ TypeString: 'ClientQuizCommandMessage', action, time: 0, ...extra });

export function createHarness({ backend = createMemoryBackend(), clock = createFakeClock(START), draftTimeoutMs } = {}) {
  const factory = createFakeTransportFactory();
  const log = createLog({ now: () => clock.now() });
  let n = 0;
  let urls = 0;
  const revoked = [];
  const game = createGame({
    storage: createStorage(backend),
    clock,
    log,
    locale: 'en-US',
    transportFactory: factory,
    device: { ua: IPHONE_UA, screenWidth: 390, screenHeight: 844, maxTouchPoints: 5 },
    uuid: () => '00000000-0000-4000-8000-' + String(++n).padStart(12, '0'),
    random: () => 0,
    makeUrl: () => 'blob:t/' + (++urls),
    revokeUrl: (u) => revoked.push(u),
    visibility: null,
    draftTimeoutMs,
  });
  const views = [];
  const popups = [];
  const overlays = [];
  game.on('view', (v) => views.push(v));
  game.on('popup', (p) => popups.push(p));
  game.on('overlays', (o) => overlays.push(o.map((x) => x.id)));

  const h = {
    game, clock, backend, factory, log, views, popups, overlays, revoked,
    tr: () => factory.last(),
    get view() { return game.view; },
    // screen[:variant] of the current view
    get at() { return game.view.screen + (game.view.variant ? ':' + game.view.variant : ''); },
    async open() {
      h.tr().accept();
      await flush();
    },
    frame(o) { h.tr().recvText(typeof o === 'string' ? o : JSON.stringify(o)); },
    g2p(json) { h.tr().recvText(JSON.stringify({ t: 'msg', id: -7503, json })); },
    cmd(action, extra) { h.g2p(cmd(action, extra)); },
    content(transferId, bytes) { for (const f of encodeChunks(transferId, bytes)) h.tr().recvBinary(f); },
    sent() { return h.tr().msgs(); },
    types() { return h.tr().types(); },
    // messages sent since the last call to take()
    take() {
      const t = h.tr();
      const all = t.msgs();
      const from = t.taken || 0;
      t.taken = all.length;
      return all.slice(from);
    },
    takeTypes() { return h.take().map((m) => m.TypeString); },
    // Full join: open, joined, version, session, assignment.
    async join({ sessionId = 77, playerId = 2, slotId = 0 } = {}) {
      await h.open();
      h.frame({ t: 'joined' });
      h.g2p({ TypeString: 'InterfaceVersionMessage', InterfaceVersion: '2016-07-12-1645' });
      h.g2p({ TypeString: 'SessionStateMessage', SessionID: sessionId });
      h.g2p({ TypeString: 'AssignPlayerIDAndSlotMessage', PlayerID: playerId, SlotID: slotId, UDPPortOffset: 0, DisplayName: '', PSNID: '' });
      await flush();
    },
    async boot(params = PARAMS) {
      game.boot(params);
      await flush();
    },
    async play() {
      game.actions.play();
      await flush();
    },
    // boot + play + join
    async start(opts) {
      await h.boot();
      await h.play();
      await h.join(opts);
      h.take();
    },
    async tick(ms) {
      await clock.advanceAsync(ms);
    },
  };
  return h;
}
