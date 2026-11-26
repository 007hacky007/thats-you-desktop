// Page boot: i18n, assets, then the game and camera modules (dynamic imports,
// so a broken module shows a plain error screen instead of a blank page).
//
// Fragment keys handled here (everything else is passed to the join parser):
//   demo=<screen>[:<variant>]   render one screen from its module's demos
//   demo                        list every demo
//   overlay=<id>[:<arg>]        with demo: also show an overlay
//                               (popup:<reason>[:reconnect], pause, leave,
//                               reconnecting, bad-connection)
//   debug=1                     show the log overlay

import { h, clear } from './util/dom.js';
import { loadI18n } from './i18n/i18n.js';
import { loadAssets, neutralAssets } from './assets/assets.js';
import { createApp, createDemoGame, applyBodyDataset } from './ui/app.js';
import { createSound } from './ui/sound.js';
import { loadScreen, SCREEN_MODULES } from './ui/registry.js';

const LOCAL_KEYS = new Set(['demo', 'overlay', 'debug']);

export function parseFragment(hash) {
  const raw = String(hash || '').replace(/^#/, '');
  const out = { demo: null, overlay: null, debug: false, joinHash: '' };
  const rest = [];
  for (const part of raw.split('&')) {
    if (!part) continue;
    const eq = part.indexOf('=');
    const key = eq < 0 ? part : part.slice(0, eq);
    let value = eq < 0 ? '' : part.slice(eq + 1);
    try { value = decodeURIComponent(value); } catch { /* keep raw */ }
    if (!LOCAL_KEYS.has(key)) { rest.push(part); continue; }
    if (key === 'debug') out.debug = value === '1' || value === 'true' || value === '';
    if (key === 'demo') {
      if (!value) out.demo = { index: true, screen: null, variant: null };
      else {
        const i = value.indexOf(':');
        out.demo = { index: false, screen: i < 0 ? value : value.slice(0, i), variant: i < 0 ? null : value.slice(i + 1) };
      }
    }
    if (key === 'overlay' && value) {
      const [id, ...args] = value.split(':');
      out.overlay = { id, args };
    }
  }
  out.joinHash = rest.length ? `#${rest.join('&')}` : '';
  return out;
}

export function shouldRegisterSw(loc) {
  if (!loc) return false;
  if (loc.protocol === 'https:') return true;
  return loc.protocol === 'http:' && ['localhost', '127.0.0.1', '[::1]', '::1'].includes(loc.hostname);
}

// The demo view for #demo=screen[:variant] from a module's `demos` export.
export function demoView(screen, variant, demos) {
  const list = Array.isArray(demos) ? demos : [];
  const pick = (variant !== null && variant !== undefined ? list.find((d) => (d.variant || '') === variant) : null) || list[0] || null;
  return {
    screen,
    variant: pick ? pick.variant || '' : variant || '',
    data: pick && pick.data ? pick.data : {},
    seq: 1,
  };
}

// What a demo overlay request means for the demo game.
export function demoOverlay(overlay) {
  const res = { popup: null, overlays: [], local: null, lastRxAgo: 0 };
  if (!overlay) return res;
  const { id, args = [] } = overlay;
  if (id === 'popup') res.popup = { reason: args[0] || 'failed', reconnect: args.includes('reconnect') };
  else if (id === 'pause' || id === 'reconnecting') res.overlays = [{ id, data: {} }];
  else if (id === 'leave' || id === 'effects') res.local = id;
  else if (id === 'bad-connection') res.lastRxAgo = 9000;
  return res;
}

function simpleLog() {
  const entries = [];
  const subs = new Set();
  const add = (level) => (...args) => {
    const e = { t: Date.now(), level, msg: args.map(String).join(' ') };
    entries.push(e);
    if (entries.length > 500) entries.shift();
    for (const fn of subs) fn(e);
    const c = globalThis.console;
    if (c && c[level === 'debug' ? 'debug' : level]) c[level === 'debug' ? 'debug' : level](`[iwyk] ${e.msg}`);
  };
  return {
    debug: add('debug'), info: add('info'), warn: add('warn'), error: add('error'),
    entries: () => entries.slice(),
    on(ev, fn) { if (ev === 'entry') subs.add(fn); return () => subs.delete(fn); },
  };
}

export function formatLogEntry(e) {
  if (e === null || e === undefined) return '';
  if (typeof e !== 'object') return String(e);
  const time = e.t ?? e.time ?? e.at;
  const stamp = typeof time === 'number' ? new Date(time).toISOString().slice(11, 23) : '';
  const level = e.level || e.lvl || '';
  const msg = e.msg !== undefined ? e.msg : e.message !== undefined ? e.message : e.text;
  const extra = e.data !== undefined ? ` ${safeJson(e.data)}` : '';
  return `${stamp} ${level} ${msg !== undefined ? String(msg) : safeJson(e)}${extra}`.trim();
}

function safeJson(v) {
  try { return JSON.stringify(v); } catch { return String(v); }
}

function mountDebugLog(log, i18n) {
  const list = h('ol', { class: 'debug-lines' });
  const panel = h('aside', { class: 'debug-log', attrs: { 'aria-label': i18n ? i18n.t('port.debugLog') : 'Log' } },
    h('button', { class: 'debug-toggle', type: 'button', text: 'log', on: { click: () => panel.classList.toggle('collapsed') } }),
    list);
  const add = (e) => {
    list.appendChild(h('li', { text: formatLogEntry(e) }));
    while (list.childNodes.length > 300) list.removeChild(list.firstChild);
    list.scrollTop = list.scrollHeight;
  };
  try { for (const e of log.entries()) add(e); } catch { /* no entries */ }
  try { log.on('entry', add); } catch { /* no events */ }
  document.body.appendChild(panel);
}

function showFatal(root, i18n, err, log) {
  if (log && log.error) log.error(`boot failed: ${err && err.message ? err.message : err}`);
  const t = (k, fb) => (i18n ? i18n.t(k) : fb);
  clear(root);
  root.appendChild(h('div', { class: 'app fatal' },
    h('main', { class: 'screen-host' },
      h('div', { class: 'stack center' },
        h('div', { class: 'panel', testid: 'boot-error' },
          h('h2', { class: 'title', text: t('port.loadError', 'The page could not start') }),
          h('p', { class: 'text', text: t('port.loadErrorHelp', 'Reload the page.') }),
          h('p', { class: 'small mono', text: String(err && err.message ? err.message : err) }),
          h('button', { class: 'btn btn-primary', testid: 'reload', type: 'button', text: 'Reload', on: { click: () => location.reload() } }))))));
  applyBodyDataset(document.body, { screen: 'error', variant: '', overlay: '' });
}

function mediaStub(log) {
  return {
    EFFECTS: [{ id: 0, name: 'None', key: 'CameraEffectName_None', kind: 'none' }],
    camera: {
      facing: 'user', active: false, canFlip: false, video: null,
      requestPermission: async () => false,
      start: async () => { throw new Error('no camera module'); },
      flip: async () => {}, release() {}, stopNow() {},
    },
    createRenderer() { throw new Error('no camera module'); },
    createDrawingSurface() { throw new Error('no drawing module'); },
    encodeJpeg: async () => { throw new Error('no camera module'); },
    placeholderPortrait: async () => { if (log && log.warn) log.warn('no camera module'); return new Uint8Array(0); },
  };
}

async function loadLog() {
  try {
    const mod = await import('./util/log.js');
    return mod.createLog({ capacity: 500 });
  } catch {
    return simpleLog();
  }
}

async function renderDemoIndex(root, i18n) {
  const items = [];
  for (const id of Object.keys(SCREEN_MODULES)) {
    let demos = [];
    try { demos = (await loadScreen(id)).demos || []; } catch { demos = null; }
    const links = demos === null
      ? [h('span', { class: 'small', text: 'missing' })]
      : (demos.length ? demos : [{ variant: '' }]).map((d) => h('a', {
        class: 'pill', attrs: { href: `#demo=${id}${d.variant ? `:${d.variant}` : ''}` }, text: d.variant || 'default',
      }));
    items.push(h('li', {}, h('strong', { text: id }), ' ', ...links));
  }
  const overlays = ['popup:gamefull', 'popup:version', 'popup:onlyone', 'popup:notallowed', 'popup:failed',
    'popup:failed:reconnect', 'popup:nogame', 'popup:replaced', 'popup:shutdown', 'pause', 'leave', 'reconnecting', 'bad-connection'];
  items.push(h('li', {}, h('strong', { text: 'overlays' }), ' ',
    ...overlays.map((o) => h('a', { class: 'pill', attrs: { href: `#demo=holding:general&overlay=${o}` }, text: o }))));
  clear(root);
  root.appendChild(h('div', { class: 'app demo-index' },
    h('main', { class: 'screen-host' },
      h('div', { class: 'stack' }, h('h2', { class: 'title', text: i18n.t('port.demoIndex') }), h('ul', { class: 'demo-list' }, ...items)))));
  applyBodyDataset(document.body, { screen: 'demo', variant: '', overlay: '' });
}

export async function boot() {
  const root = document.getElementById('app') || document.body;
  const frag = parseFragment(location.hash);
  const log = await loadLog();
  globalThis.addEventListener('error', (e) => log.error(`error: ${e && e.message}`));
  globalThis.addEventListener('unhandledrejection', (e) => log.error(`unhandled: ${e && e.reason && e.reason.message ? e.reason.message : e && e.reason}`));

  let i18n = null;
  try {
    i18n = await loadI18n({});
  } catch (e) {
    log.warn(`i18n: ${e && e.message}`);
    i18n = await loadI18n({ fetch: async () => ({ ok: false }) });
  }
  document.documentElement.lang = i18n.lang;
  document.documentElement.dir = i18n.dir;
  document.title = i18n.t('port.title');
  if (frag.debug) mountDebugLog(log, i18n);

  let assets;
  try {
    assets = await loadAssets({});
  } catch {
    assets = neutralAssets();
  }
  assets.applyTheme(document.body);
  assets.fonts.load(document.documentElement).catch(() => {});
  log.info(`page lang ${i18n.lang} (${i18n.locale}), ${assets.mode} mode`);
  const sound = createSound({ assets, log });

  if (shouldRegisterSw(location) && navigator.serviceWorker && !frag.demo) {
    navigator.serviceWorker.register('sw.js').catch((e) => log.warn(`service worker: ${e && e.message}`));
  }

  if (frag.demo) {
    globalThis.addEventListener('hashchange', () => location.reload());
    if (frag.demo.index || !frag.demo.screen) {
      await renderDemoIndex(root, i18n);
      return;
    }
    let mod = null;
    try { mod = await loadScreen(frag.demo.screen); } catch (e) { log.warn(`demo: ${e && e.message}`); }
    const view = demoView(frag.demo.screen, frag.demo.variant, mod && mod.demos);
    const ov = demoOverlay(frag.overlay);
    let media;
    try {
      const mm = await import('./camera/media.js');
      media = mm.createMedia({ assets, log });
    } catch (e) {
      log.warn(`demo without camera module: ${e && e.message}`);
      media = mediaStub(log);
    }
    const game = createDemoGame({ view, popup: ov.popup, overlays: ov.overlays, log, lastRxAgo: ov.lastRxAgo });
    const app = createApp({ root, game, media, i18n, assets, log, sound });
    await app.start();
    if (ov.local) app.openOverlay(ov.local);
    if (ov.lastRxAgo) app.checkLink();
    globalThis.iwykDemo = { game, app };
    return;
  }

  let game;
  let media;
  let params = null;
  try {
    const [gameMod, mediaMod, storageMod, clockMod, paramsMod] = await Promise.all([
      import('./game/game.js'),
      import('./camera/media.js'),
      import('./util/storage.js'),
      import('./util/clock.js'),
      import('./net/params.js'),
    ]);
    media = mediaMod.createMedia({ assets, log });
    game = gameMod.createGame({
      storage: storageMod.createStorage(),
      clock: clockMod.realClock,
      log,
      locale: i18n.locale,
    });
    params = frag.joinHash ? paramsMod.parseJoinParams(frag.joinHash) : null;
  } catch (e) {
    showFatal(root, i18n, e, log);
    return;
  }
  const app = createApp({ root, game, media, i18n, assets, log, sound });
  await app.start();
  globalThis.addEventListener('hashchange', () => {
    // a new QR code scanned while the page is open
    const next = parseFragment(location.hash);
    if (next.joinHash !== frag.joinHash) location.reload();
  });
  game.boot(params);
}

if (typeof document !== 'undefined' && typeof location !== 'undefined' && !globalThis.IWYK_NO_BOOT) {
  boot().catch((e) => {
    const root = document.getElementById('app') || document.body;
    showFatal(root, null, e, null);
  });
}
