import test from 'node:test';
import assert from 'node:assert/strict';
import { existsSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { SCREEN_MODULES, OVERLAY_MODULES, loadScreen, clearRegistryCache } from '../../js/ui/registry.js';
import {
  bodyDataset, topOverlay, shouldShowBadConnection, overlayStack, isSessionScreen, createApp, createDemoGame,
} from '../../js/ui/app.js';
import { EN } from '../../js/i18n/en.js';
import { installFakeDom, findTestId, fire, tick } from './fakedom.mjs';

// Section 5.6 of the contract.
const SCREEN_IDS = ['connect', 'joined', 'name', 'card', 'selfie', 'confirm', 'holding', 'quickdraw-holding',
  'continue', 'joker', 'answer', 'draw', 'text', 'photo', 'mimic'];
const U5_SCREENS = ['connect', 'joined', 'name', 'card', 'confirm', 'holding', 'quickdraw-holding', 'continue', 'joker'];

const i18n = { lang: 'en', locale: 'en-US', dir: 'ltr', t: (k, ...a) => (EN[k] ?? k).replace(/\{(\d)\}/g, (_, i) => String(a[i] ?? '')).replace(/\{n\}/g, '\n'), has: (k) => k in EN, addFallback() {} };
const neutral = { mode: 'neutral', url: () => null, sprite: () => null, applySprite: () => false, card: () => null, scenario: () => null, effectMap: () => null, logo: () => null, sound: () => null };
const quietLog = { debug() {}, info() {}, warn() {}, error() {} };

test('registry maps every screen id of the view machine', () => {
  assert.deepEqual(Object.keys(SCREEN_MODULES).sort(), SCREEN_IDS.slice().sort());
  for (const p of Object.values(SCREEN_MODULES)) assert.match(p, /^\.\/screens\/[a-z]+\.js$/);
});

test('registry maps every overlay the page renders', () => {
  assert.deepEqual(Object.keys(OVERLAY_MODULES).sort(), ['bad-connection', 'leave', 'pause', 'popup', 'reconnecting']);
});

test('U5 screen and overlay modules exist and export mount and demos', async () => {
  for (const id of U5_SCREENS) {
    const file = fileURLToPath(new URL(`../../js/ui/${SCREEN_MODULES[id].slice(2)}`, import.meta.url));
    assert.ok(existsSync(file), file);
    const mod = await loadScreen(id);
    assert.equal(typeof mod.mount, 'function', id);
    assert.ok(Array.isArray(mod.demos) && mod.demos.length > 0, id);
  }
  for (const p of Object.values(OVERLAY_MODULES)) {
    const mod = await import(new URL(`../../js/ui/${p.slice(2)}`, import.meta.url));
    assert.equal(typeof mod.mount, 'function', p);
  }
});

test('registry rejects unknown ids and modules without mount, and retries after failure', async () => {
  clearRegistryCache();
  await assert.rejects(loadScreen('nope'));
  let calls = 0;
  const bad = async () => { calls++; return {}; };
  await assert.rejects(loadScreen('answer', bad));
  await assert.rejects(loadScreen('answer', bad));
  assert.equal(calls, 2);
  clearRegistryCache();
});

test('body dataset helper', () => {
  assert.deepEqual(bodyDataset(null, []), { screen: 'boot', variant: '', overlay: '' });
  assert.deepEqual(bodyDataset({ screen: 'holding', variant: 'look-at-tv' }, ['pause']), { screen: 'holding', variant: 'look-at-tv', overlay: 'pause' });
  assert.deepEqual(bodyDataset({ screen: 'card', variant: undefined }, ['bad-connection', 'popup', 'leave']), { screen: 'card', variant: '', overlay: 'popup' });
});

test('overlay priority', () => {
  assert.equal(topOverlay([]), '');
  assert.equal(topOverlay(['pause', 'reconnecting']), 'reconnecting');
  assert.equal(topOverlay(['effects', 'leave']), 'leave');
  assert.equal(topOverlay(['bad-connection', 'effects']), 'effects');
  assert.equal(topOverlay(['something', 'bad-connection']), 'bad-connection');
  const stack = overlayStack({ popup: { reason: 'failed' }, gameOverlays: [{ id: 'pause', data: {} }], local: new Map([['leave', {}]]), badConnection: true });
  assert.deepEqual(stack.map((o) => o.id), ['popup', 'leave', 'pause', 'bad-connection']);
});

test('session screens and bad connection rule', () => {
  assert.equal(isSessionScreen('connect'), false);
  assert.equal(isSessionScreen('boot'), false);
  assert.equal(isSessionScreen('joined'), true);
  const base = { screen: 'holding', connected: true, overlayIds: [] };
  assert.equal(shouldShowBadConnection({ ...base, lastRxAgo: 5000 }), false);
  assert.equal(shouldShowBadConnection({ ...base, lastRxAgo: 5701 }), true);
  assert.equal(shouldShowBadConnection({ ...base, lastRxAgo: 9000, screen: 'connect' }), false);
  assert.equal(shouldShowBadConnection({ ...base, lastRxAgo: 9000, connected: false }), false);
  assert.equal(shouldShowBadConnection({ ...base, lastRxAgo: 9000, overlayIds: ['reconnecting'] }), false);
  assert.equal(shouldShowBadConnection({ ...base, lastRxAgo: 9000, overlayIds: ['popup'] }), false);
  assert.equal(shouldShowBadConnection({ ...base, lastRxAgo: null }), false);
});

function fakeTimers() {
  const intervals = [];
  return { setInterval: (fn) => { intervals.push(fn); return intervals.length; }, clearInterval() {}, run: () => intervals.forEach((f) => f()) };
}

async function settle() { for (let i = 0; i < 5; i++) await tick(); }

test('app mounts screens, keeps datasets and shows the nav bar on session screens', async () => {
  const document = installFakeDom();
  const game = createDemoGame({ view: { screen: 'connect', data: { state: 'ready' } }, log: quietLog });
  const timers = fakeTimers();
  const app = createApp({ root: document.body, body: document.body, game, i18n, assets: neutral, log: quietLog, timers });
  assert.equal(document.body.dataset.screen, 'boot');
  await app.start();
  assert.equal(document.body.dataset.screen, 'connect');
  assert.equal(document.body.dataset.variant, '');
  assert.equal(document.body.dataset.overlay, '');
  const nav = findTestId(document.body, 'leave');
  assert.ok(nav);
  assert.equal(nav.parentNode.hidden, true);

  game.view = { screen: 'holding', variant: 'look-at-tv', data: {}, seq: 2 };
  game.emit('view', game.view);
  await settle();
  assert.equal(document.body.dataset.screen, 'holding');
  assert.equal(document.body.dataset.variant, 'look-at-tv');
  assert.equal(nav.parentNode.hidden, false);
  assert.equal(findTestId(document.body, 'connect-play'), null);

  // leave flow through the nav bar
  fire(nav, 'click');
  await settle();
  assert.equal(document.body.dataset.overlay, 'leave');
  fire(findTestId(document.body, 'leave-cancel'), 'click');
  await settle();
  assert.equal(document.body.dataset.overlay, '');
  fire(nav, 'click');
  await settle();
  fire(findTestId(document.body, 'leave-confirm'), 'click');
  await settle();
  assert.deepEqual(game.calls.map((c) => c[0]), ['leave']);
  assert.equal(document.body.dataset.overlay, '');

  // game overlays and popups
  game.overlays = [{ id: 'pause', data: {} }];
  game.emit('overlays', game.overlays);
  await settle();
  assert.equal(document.body.dataset.overlay, 'pause');
  game.popup = { reason: 'failed', reconnect: true };
  game.emit('popup', game.popup);
  await settle();
  assert.equal(document.body.dataset.overlay, 'popup');
  const popup = findTestId(document.body, 'popup');
  assert.equal(popup.getAttribute('data-reason'), 'failed');
  fire(findTestId(document.body, 'popup-reconnect'), 'click');
  assert.equal(game.calls.at(-1)[0], 'reconnect');
  fire(findTestId(document.body, 'popup-close'), 'click');
  await settle();
  assert.equal(game.calls.at(-1)[0], 'dismissPopup');
  assert.equal(document.body.dataset.overlay, 'pause');
  game.overlays = [];
  game.emit('overlays', []);
  await settle();
  assert.equal(document.body.dataset.overlay, '');
  app.destroy();
});

test('app shows the bad-connection indicator from link silence', async () => {
  const document = installFakeDom();
  let silent = 0;
  const game = createDemoGame({ view: { screen: 'joined', data: {} }, log: quietLog });
  game.link.lastRxAgo = () => silent;
  const timers = fakeTimers();
  const app = createApp({ root: document.body, body: document.body, game, i18n, assets: neutral, log: quietLog, timers });
  await app.start();
  timers.run();
  await settle();
  assert.equal(document.body.dataset.overlay, '');
  silent = 6000;
  timers.run();
  await settle();
  assert.equal(document.body.dataset.overlay, 'bad-connection');
  silent = 100;
  timers.run();
  await settle();
  assert.equal(document.body.dataset.overlay, '');
  app.destroy();
});

test('a broken screen module only breaks its own screen', async () => {
  const document = installFakeDom();
  clearRegistryCache();
  const game = createDemoGame({ view: { screen: 'answer', variant: 'texts', data: {} }, log: quietLog });
  const importer = async (path) => {
    if (path.includes('answer')) throw new Error('boom');
    return import(new URL(`../../js/ui/${path.slice(2)}`, import.meta.url));
  };
  const app = createApp({ root: document.body, body: document.body, game, i18n, assets: neutral, log: quietLog, timers: fakeTimers(), importer });
  await app.start();
  assert.equal(document.body.dataset.screen, 'answer');
  assert.ok(findTestId(document.body, 'screen-error'));
  game.view = { screen: 'joined', variant: '', data: {}, seq: 3 };
  game.emit('view', game.view);
  await settle();
  assert.equal(document.body.dataset.screen, 'joined');
  assert.equal(findTestId(document.body, 'screen-error'), null);
  app.destroy();
  clearRegistryCache();
});

test('the newest view wins when modules load out of order', async () => {
  const document = installFakeDom();
  clearRegistryCache();
  const game = createDemoGame({ view: { screen: 'connect', data: { state: 'ready' } }, log: quietLog });
  let releaseSlow;
  const slow = new Promise((r) => { releaseSlow = r; });
  const importer = async (path) => {
    if (path.includes('joker')) await slow;
    return import(new URL(`../../js/ui/${path.slice(2)}`, import.meta.url));
  };
  const app = createApp({ root: document.body, body: document.body, game, i18n, assets: neutral, log: quietLog, timers: fakeTimers(), importer });
  await app.start();
  game.emit('view', { screen: 'joker', variant: '', data: { pre: 0, cur: 1 }, seq: 2 });
  game.emit('view', { screen: 'continue', variant: 'continue', data: {}, seq: 3 });
  await settle();
  releaseSlow();
  await settle();
  assert.equal(document.body.dataset.screen, 'continue');
  assert.ok(findTestId(document.body, 'continue'));
  app.destroy();
  clearRegistryCache();
});
