import test from 'node:test';
import assert from 'node:assert/strict';
import { installFakeDom, findTestId, allTestIds, fire, tick } from './fakedom.mjs';
import { h } from '../../js/util/dom.js';
import { createIcon } from '../../js/ui/icons.js';
import { EN } from '../../js/i18n/en.js';
import { formatText } from '../../js/i18n/i18n.js';
import * as connect from '../../js/ui/screens/connect.js';
import * as joined from '../../js/ui/screens/joined.js';
import * as name from '../../js/ui/screens/name.js';
import * as card from '../../js/ui/screens/card.js';
import * as confirm from '../../js/ui/screens/confirm.js';
import * as holding from '../../js/ui/screens/holding.js';
import * as qd from '../../js/ui/screens/quickdrawholding.js';
import * as cont from '../../js/ui/screens/continue.js';
import * as joker from '../../js/ui/screens/joker.js';
import * as popup from '../../js/ui/overlays/popup.js';
import * as pause from '../../js/ui/overlays/pause.js';
import * as reconnecting from '../../js/ui/overlays/reconnecting.js';
import * as badconnection from '../../js/ui/overlays/badconnection.js';

const t = (k, ...a) => formatText(EN[k] ?? k, a);
const i18n = { lang: 'en', locale: 'en-US', dir: 'ltr', t, has: (k) => k in EN, addFallback() {} };
const neutral = { mode: 'neutral', url: () => null, sprite: () => null, applySprite: () => false, card: () => null, scenario: () => null, logo: () => null, sound: () => null };
const sound = { played: [], unlocked: false, unlock() { this.unlocked = true; }, play(e) { this.played.push(e); } };
const log = { debug() {}, info() {}, warn() {}, error() {} };

function fakeGame() {
  const calls = [];
  const listeners = {};
  return {
    calls,
    state: { profile: { name: 'Alex' }, connected: true },
    resources: {
      urls: {},
      url(g) { return this.urls[g] || null; },
      on(ev, fn) { listeners[ev] = fn; return () => { delete listeners[ev]; }; },
      fire(g) { listeners.added && listeners.added(g); },
    },
    actions: new Proxy({}, { get: (_, n) => (...a) => { calls.push([n, ...a]); return Promise.resolve(); } }),
  };
}

function ctxFor(document, extra = {}) {
  const el = document.createElement('section');
  document.body.appendChild(el);
  return { el, game: fakeGame(), media: null, i18n, assets: neutral, h, icon: (n) => createIcon(n), log, sound, overlay: { open() {}, close() {} }, ...extra };
}

test('every demo of every U5 screen mounts and updates', () => {
  for (const mod of [connect, joined, name, card, confirm, holding, qd, cont, joker]) {
    for (const demo of mod.demos) {
      const document = installFakeDom();
      const ctx = ctxFor(document);
      const view = { screen: 'x', variant: demo.variant, data: demo.data, seq: 1 };
      const inst = mod.mount(ctx, view);
      assert.equal(typeof inst.update, 'function');
      inst.update(view);
      inst.unmount();
    }
  }
});

test('connect: PLAY asks for the camera first, then plays, even when refused', async () => {
  for (const outcome of ['granted', 'denied', 'throws', 'nomedia']) {
    const document = installFakeDom();
    const order = [];
    const media = outcome === 'nomedia' ? null : {
      camera: {
        requestPermission() {
          order.push('perm');
          if (outcome === 'throws') return Promise.reject(Object.assign(new Error('x'), { name: 'NotAllowedError' }));
          return Promise.resolve(outcome === 'granted');
        },
      },
    };
    const ctx = ctxFor(document, { media });
    ctx.game.actions = { play() { order.push('play'); return Promise.resolve(); } };
    connect.mount(ctx, { screen: 'connect', data: { state: 'ready' } });
    const btn = findTestId(ctx.el, 'connect-play');
    assert.equal(btn.textContent, EN.JoinPS4ButtonLabel);
    fire(btn, 'click');
    fire(btn, 'click'); // double tap is ignored
    for (let i = 0; i < 5; i++) await tick();
    assert.deepEqual(order, outcome === 'nomedia' ? ['play'] : ['perm', 'play'], outcome);
  }
});

test('connect: PLAY unlocks sounds', async () => {
  const document = installFakeDom();
  const snd = { unlocked: false, unlock() { this.unlocked = true; }, play() {} };
  const ctx = ctxFor(document, { sound: snd });
  connect.mount(ctx, { screen: 'connect', data: { state: 'ready' } });
  fire(findTestId(ctx.el, 'connect-play'), 'click');
  assert.equal(snd.unlocked, true);
});

test('connect: states', () => {
  const document = installFakeDom();
  const ctx = ctxFor(document);
  const inst = connect.mount(ctx, { screen: 'connect', data: { state: 'noparams' } });
  const btn = findTestId(ctx.el, 'connect-play');
  assert.equal(btn.hidden, true);
  assert.ok(ctx.el.textContent.includes(EN['port.scanQr']));
  inst.update({ screen: 'connect', data: { state: 'connecting' } });
  assert.equal(btn.hidden, false);
  assert.equal(btn.disabled, true);
  assert.ok(ctx.el.textContent.includes(EN['port.connecting']));
  inst.update({ screen: 'connect', data: { state: 'waiting' } });
  assert.ok(ctx.el.textContent.includes(EN['port.waiting']));
  inst.update({ screen: 'connect', data: { state: 'idle' } });
  assert.equal(btn.disabled, false);
  assert.equal(btn.textContent, EN['port.rejoin']);
});

test('name: limited to 10 code points, guest flag, trimmed', () => {
  const document = installFakeDom();
  const ctx = ctxFor(document);
  name.mount(ctx, { screen: 'name', data: { name: '', isGuest: false, maxLength: 10 } });
  const input = findTestId(ctx.el, 'name-input');
  const next = findTestId(ctx.el, 'name-next');
  assert.equal(next.disabled, true);
  input.value = '   ';
  fire(input, 'input');
  assert.equal(next.disabled, true);
  // 12 code points, two of them astral (surrogate pairs)
  input.value = '\u{1F600}bcdefghi\u{1F601}kl';
  fire(input, 'input');
  assert.equal(Array.from(input.value).length, 10);
  assert.equal(input.value, '\u{1F600}bcdefghi\u{1F601}');
  input.value = ` ${input.value.slice(0, -2)} `;
  fire(input, 'input');
  findTestId(ctx.el, 'name-guest').checked = true;
  fire(next, 'click');
  fire(next, 'click');
  assert.deepEqual(ctx.game.calls, [['profileName', '\u{1F600}bcdefghi', true]]);
});

test('name: pre-filled from data and Enter submits', () => {
  const document = installFakeDom();
  const ctx = ctxFor(document);
  name.mount(ctx, { screen: 'name', data: { name: 'Robin', isGuest: false, maxLength: 10 } });
  const input = findTestId(ctx.el, 'name-input');
  assert.equal(input.value, 'Robin');
  fire(input, 'keydown', { key: 'Enter' });
  assert.deepEqual(ctx.game.calls, [['profileName', 'Robin', false]]);
});

test('card: twelve cards, pick then confirm', () => {
  const document = installFakeDom();
  const ctx = ctxFor(document);
  card.mount(ctx, { screen: 'card', data: { cardId: null } });
  for (let i = 1; i <= 12; i++) assert.ok(findTestId(ctx.el, `card-${i}`), `card-${i}`);
  const ok = findTestId(ctx.el, 'card-confirm');
  assert.equal(ok.disabled, true);
  fire(findTestId(ctx.el, 'card-3'), 'click');
  assert.equal(ok.disabled, false);
  fire(findTestId(ctx.el, 'card-7'), 'click');
  fire(ok, 'click');
  fire(ok, 'click');
  assert.deepEqual(ctx.game.calls, [['profileCard', 'CARD_7']]);
});

test('card: pre-selected card from data', () => {
  const document = installFakeDom();
  const ctx = ctxFor(document);
  card.mount(ctx, { screen: 'card', data: { cardId: 'CARD_12' } });
  assert.equal(findTestId(ctx.el, 'card-12').getAttribute('aria-pressed'), 'true');
  assert.equal(card.cardNumber('CARD_13'), 0);
  assert.equal(card.cardNumber('CARD_1'), 1);
});

test('confirm: edit buttons and confirm', () => {
  const document = installFakeDom();
  const ctx = ctxFor(document);
  confirm.mount(ctx, { screen: 'confirm', data: { name: 'Alex', cardId: 'CARD_2', portraitUrl: 'blob:x' } });
  assert.ok(ctx.el.textContent.includes('Alex'));
  fire(findTestId(ctx.el, 'profile-edit-name'), 'click');
  fire(findTestId(ctx.el, 'profile-edit-card'), 'click');
  fire(findTestId(ctx.el, 'profile-edit-photo'), 'click');
  fire(findTestId(ctx.el, 'profile-confirm'), 'click');
  fire(findTestId(ctx.el, 'profile-confirm'), 'click');
  assert.deepEqual(ctx.game.calls, [['profileEdit', 'name'], ['profileEdit', 'card'], ['profileEdit', 'photo'], ['profileConfirm']]);
});

test('holding: every variant', () => {
  const c = (v, d) => holding.holdingContent(v, d, t);
  assert.equal(c('look-at-tv').title, EN.LookAtTvInstruction);
  assert.equal(c('other-photo', { playerName: 'Sam' }).title, formatText(EN.OtherPersonTakingPhotoMessage, ['Sam']));
  assert.equal(c('rejoin').body, EN.WaitForNextQuestionInstruction);
  assert.equal(c('custom', { text: 'a\nb' }).body, 'a\nb');
  assert.equal(c('wait').title, '');
  assert.equal(c('general').body, EN['port.wait']);
  assert.equal(c('other-ugc').title, EN['port.otherUgc']);
  const document = installFakeDom();
  const ctx = ctxFor(document);
  const inst = holding.mount(ctx, { screen: 'holding', variant: 'custom', data: { text: '<b>x</b>' } });
  assert.ok(ctx.el.textContent.includes('<b>x</b>'));
  inst.update({ screen: 'holding', variant: 'look-at-tv', data: {} });
  assert.ok(ctx.el.textContent.includes(EN.LookAtTvInstruction));
  assert.ok(!ctx.el.textContent.includes('<b>x</b>'));
});

test('quickdraw holding: remaining text and late picture', () => {
  assert.equal(qd.remainingText(1, t), EN.DrawingsToGoMessage1);
  assert.equal(qd.remainingText(5, t), EN.DrawingsToGoMessage5);
  assert.equal(qd.remainingText(7, t), formatText(EN['port.remaining'], [7]));
  assert.equal(qd.remainingText(0, t), '');
  const document = installFakeDom();
  const ctx = ctxFor(document);
  qd.mount(ctx, { screen: 'quickdraw-holding', data: { imageGuid: 'g1', heading: 'H', description: 'D', remaining: 2, playerName: 'Sam' } });
  const imgs = () => { const out = []; const st = [ctx.el]; while (st.length) { const n = st.pop(); if (n.tagName === 'IMG') out.push(n); st.push(...n.childNodes); } return out; };
  assert.equal(imgs().length, 0);
  ctx.game.resources.urls.g1 = 'blob:g1';
  ctx.game.resources.fire('g1');
  assert.equal(imgs()[0].getAttribute('src'), 'blob:g1');
});

test('continue: tap once', () => {
  const document = installFakeDom();
  const ctx = ctxFor(document);
  const inst = cont.mount(ctx, { screen: 'continue', variant: 'continue', data: {} });
  const b = findTestId(ctx.el, 'continue');
  fire(b, 'click');
  fire(b, 'click');
  assert.deepEqual(ctx.game.calls, [['continuePressed']]);
  inst.update({ screen: 'continue', variant: 'start', data: {} });
  assert.equal(findTestId(ctx.el, 'continue'), null);
  assert.ok(findTestId(ctx.el, 'start'));
  inst.unmount();
});

test('continue: ring offset', () => {
  assert.equal(cont.ringOffset(0), cont.RING_LENGTH);
  assert.equal(cont.ringOffset(1), 0);
  assert.ok(Math.abs(cont.ringOffset(0.5) - cont.RING_LENGTH / 2) < 1e-9);
  assert.equal(cont.ringOffset(7), 0);
  assert.equal(cont.HOLD_MS, 2000);
});

test('joker: singular and plural', () => {
  assert.equal(joker.jokerTitleKey(1, 2), 'JokerAddedMessage');
  assert.equal(joker.jokerTitleKey(0, 2), 'JokerAddedPluralMessage');
});

test('popup: keys for every reason', () => {
  for (const r of popup.REASONS) {
    for (const rc of [false, true]) {
      const c = popup.popupContent(r, rc);
      for (const k of [c.title, c.body, c.close, c.reconnect]) if (k) assert.equal(typeof EN[k], 'string', `${r} ${k}`);
    }
  }
  assert.equal(popup.popupContent('failed', true).reconnect, 'DisconnectedPopup_Reconnect');
  assert.equal(popup.popupContent('failed', false).reconnect, null);
  assert.equal(popup.popupContent('failed', false).body, 'ConnectionFailedPopupMessage');
  assert.equal(popup.popupContent('replaced', false).reconnect, 'port.reconnect');
  assert.equal(popup.popupContent('nogame', false).title, 'ConnectionWarning_NoGameMessage');
});

test('popup overlay renders test ids and reason', () => {
  const document = installFakeDom();
  const ctx = ctxFor(document);
  const inst = popup.mount(ctx, { reason: 'gamefull', reconnect: false });
  const p = findTestId(ctx.el, 'popup');
  assert.equal(p.getAttribute('data-reason'), 'gamefull');
  assert.equal(findTestId(ctx.el, 'popup-reconnect'), null);
  assert.ok(findTestId(ctx.el, 'popup-close'));
  inst.update({ reason: 'failed', reconnect: true });
  assert.equal(p.getAttribute('data-reason'), 'failed');
  fire(findTestId(ctx.el, 'popup-reconnect'), 'click');
  fire(findTestId(ctx.el, 'popup-close'), 'click');
  assert.deepEqual(ctx.game.calls.map((c) => c[0]), ['reconnect', 'dismissPopup']);
});

test('simple overlays mount', () => {
  for (const mod of [pause, reconnecting, badconnection]) {
    const document = installFakeDom();
    const ctx = ctxFor(document);
    mod.mount(ctx, {});
    assert.ok(ctx.el.textContent.length > 0);
  }
});

test('contract test ids are present', () => {
  const document = installFakeDom();
  const ids = (mod, view) => { const ctx = ctxFor(document); mod.mount(ctx, view); return allTestIds(ctx.el); };
  assert.ok(ids(connect, { data: { state: 'ready' } }).includes('connect-play'));
  assert.deepEqual(ids(name, { data: {} }).sort(), ['name-guest', 'name-input', 'name-next']);
  const cardIds = ids(card, { data: {} });
  assert.equal(cardIds.filter((x) => /^card-\d+$/.test(x)).length, 12);
  assert.ok(cardIds.includes('card-confirm'));
  assert.deepEqual(ids(confirm, { data: { name: 'a', cardId: 'CARD_1' } }).sort(), ['profile-confirm', 'profile-edit-card', 'profile-edit-name', 'profile-edit-photo']);
  assert.deepEqual(ids(cont, { variant: 'continue', data: {} }), ['continue']);
  assert.deepEqual(ids(cont, { variant: 'start', data: {} }), ['start']);
});

test('icons: neutral SVG and art sprite crop', () => {
  installFakeDom();
  const n = createIcon('camera');
  assert.equal(n.getAttribute('viewBox'), '0 0 24 24');
  assert.ok(n.childNodes.length >= 2);
  const art = { mode: 'art', sprite: (name) => (name === 'icon_camera' ? { url: 'art/gui/atlas.png', x: 10, y: 20, w: 30, h: 40, sheetW: 2048, sheetH: 2048 } : null) };
  const a = createIcon('camera', { assets: art });
  assert.equal(a.getAttribute('viewBox'), '0 0 30 40');
  const crop = a.childNodes[0];
  assert.equal(crop.getAttribute('viewBox'), '10 20 30 40');
  assert.equal(crop.childNodes[0].getAttribute('href'), 'art/gui/atlas.png');
  const fallback = createIcon('pause', { assets: art });
  assert.equal(fallback.getAttribute('viewBox'), '0 0 24 24');
});
