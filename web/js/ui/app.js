// Screen host: mounts the screen module of game.view, keeps the body test
// datasets, the nav bar, the scenario background and the overlays.

import { h, clear } from '../util/dom.js';
import { loadScreen, loadOverlay, OVERLAY_MODULES } from './registry.js';
import { iconFactory } from './icons.js';
import { silentSound } from './sound.js';
import { cssUrl } from '../assets/assets.js';

// Highest first. Popups always win; bad-connection is only an indicator.
export const OVERLAY_PRIORITY = ['popup', 'leave', 'reconnecting', 'pause', 'effects', 'bad-connection'];

export const BAD_CONNECTION_MS = 5700;

function rank(id) {
  const i = OVERLAY_PRIORITY.indexOf(id);
  return i < 0 ? OVERLAY_PRIORITY.length : i;
}

export function sortOverlays(ids) {
  return ids.slice().sort((a, b) => rank(a) - rank(b));
}

export function topOverlay(ids) {
  return ids && ids.length ? sortOverlays(ids)[0] : '';
}

export function bodyDataset(view, overlayIds) {
  return {
    screen: view && view.screen ? String(view.screen) : 'boot',
    variant: view && view.variant ? String(view.variant) : '',
    overlay: topOverlay(overlayIds || []),
  };
}

export function isSessionScreen(screen) {
  return !!screen && screen !== 'connect' && screen !== 'boot';
}

// The page-side bad-connection indicator: on a session screen, while the
// link is up, nothing has arrived for more than 5.7 s, and no reconnect
// overlay or popup already explains the situation.
export function shouldShowBadConnection({ screen, lastRxAgo, connected, overlayIds }) {
  if (!isSessionScreen(screen)) return false;
  if (connected === false) return false;
  const ids = overlayIds || [];
  if (ids.includes('reconnecting') || ids.includes('popup')) return false;
  return typeof lastRxAgo === 'number' && lastRxAgo > BAD_CONNECTION_MS;
}

// Active overlays as [{ id, data }], highest priority first.
export function overlayStack({ popup, gameOverlays, local, badConnection }) {
  const out = [];
  if (popup) out.push({ id: 'popup', data: popup });
  for (const o of gameOverlays || []) if (o && o.id) out.push({ id: o.id, data: o.data || {} });
  for (const [id, data] of local || []) out.push({ id, data: data || {} });
  if (badConnection) out.push({ id: 'bad-connection', data: {} });
  const seen = new Set();
  return out
    .filter((o) => (seen.has(o.id) ? false : (seen.add(o.id), true)))
    .sort((a, b) => rank(a.id) - rank(b.id));
}

export function applyBodyDataset(body, ds) {
  body.dataset.screen = ds.screen;
  body.dataset.variant = ds.variant;
  body.dataset.overlay = ds.overlay;
}

export function createApp(options) {
  const {
    root, game, media = null, i18n, assets, log = console, sound = silentSound,
    importer, timers = globalThis,
  } = options;
  const body = options.body || globalThis.document.body;
  const t = (k, ...a) => i18n.t(k, ...a);
  const icon = iconFactory(assets);

  const bg = h('div', { class: 'scenario-bg', attrs: { 'aria-hidden': 'true' } });
  const leaveBtn = h('button', {
    class: 'nav-leave', testid: 'leave', type: 'button', attrs: { 'aria-label': t('port.leave') },
    on: { click: () => { sound.play('BACK_BUTTON_CLICKED'); openLocal('leave'); } },
  }, icon('leave'));
  const nav = h('header', { class: 'navbar' }, leaveBtn);
  const host = h('main', { class: 'screen-host' });
  const overlayHost = h('div', { class: 'overlay-host' });
  const shell = h('div', { class: 'app' }, bg, nav, host, overlayHost);
  root.appendChild(shell);

  const local = new Map();
  const mountedOverlays = new Map(); // id -> { el, instance, pending }
  let current = null; // { screen, instance, el, view, pendingView, token }
  let shownView = null; // last view whose screen is on display
  let token = 0;
  let badConnection = false;
  let destroyed = false;
  const offs = [];

  function currentOverlayIds() {
    return overlayStack({ popup: game.popup, gameOverlays: game.overlays, local, badConnection }).map((o) => o.id);
  }

  function updateChrome() {
    const ids = currentOverlayIds();
    applyBodyDataset(body, bodyDataset(shownView, ids));
    const session = isSessionScreen(shownView ? shownView.screen : null);
    nav.hidden = !session;
    shell.classList.toggle('has-nav', session);
  }

  function makeCtx(el) {
    return {
      el, game, media, i18n, assets, h, icon, log, sound,
      overlay: { open: openLocal, close: closeLocal, isOpen: (id) => local.has(id) },
    };
  }

  function renderScreenError(el, id, err) {
    log.error && log.error(`screen ${id} failed: ${err && err.message ? err.message : err}`);
    clear(el);
    el.appendChild(h('div', { class: 'panel screen-error', testid: 'screen-error' },
      h('p', { class: 'title', text: t('port.screenError') }),
      h('p', { class: 'small', text: String(id) })));
  }

  async function showView(view) {
    if (destroyed || !view || !view.screen) return;
    if (current && current.screen === view.screen) {
      current.view = view;
      if (current.instance) {
        try { current.instance.update && current.instance.update(view); } catch (e) { log.error && log.error(`update ${view.screen}: ${e && e.message}`); }
        shownView = view;
        updateChrome();
      } else {
        current.pendingView = view;
      }
      return;
    }
    const my = ++token;
    if (current) {
      try { current.instance && current.instance.unmount && current.instance.unmount(); } catch (e) { log.warn && log.warn(`unmount ${current.screen}: ${e && e.message}`); }
      if (current.el) current.el.remove();
    }
    const el = h('section', { class: ['screen', `screen-${view.screen}`] });
    current = { screen: view.screen, instance: null, el, view, pendingView: null, token: my };
    let mod = null;
    let error = null;
    try {
      mod = await loadScreen(view.screen, importer);
    } catch (e) {
      error = e;
    }
    if (destroyed || my !== token) return;
    clear(host);
    host.appendChild(el);
    const entry = current;
    if (error) {
      renderScreenError(el, view.screen, error);
      entry.instance = { update() {}, unmount() {} };
    } else {
      try {
        entry.instance = mod.mount(makeCtx(el), view) || { update() {}, unmount() {} };
      } catch (e) {
        renderScreenError(el, view.screen, e);
        entry.instance = { update() {}, unmount() {} };
      }
    }
    if (entry.pendingView && entry.pendingView !== view) {
      entry.view = entry.pendingView;
      try { entry.instance.update && entry.instance.update(entry.pendingView); } catch (e) { log.error && log.error(`update ${view.screen}: ${e && e.message}`); }
    }
    entry.pendingView = null;
    shownView = entry.view;
    host.scrollTop = 0;
    updateChrome();
  }

  function overlayCtx(id, el) {
    return { ...makeCtx(el), close: () => closeLocal(id) };
  }

  function renderOverlays() {
    if (destroyed) return;
    const stack = overlayStack({ popup: game.popup, gameOverlays: game.overlays, local, badConnection });
    const wanted = new Map(stack.map((o) => [o.id, o.data]));
    for (const [id, m] of mountedOverlays) {
      if (wanted.has(id)) continue;
      try { m.instance && m.instance.unmount && m.instance.unmount(); } catch { /* ignore */ }
      m.el.remove();
      mountedOverlays.delete(id);
    }
    for (const { id, data } of stack) {
      if (!Object.prototype.hasOwnProperty.call(OVERLAY_MODULES, id)) continue; // e.g. effects: drawn by its screen
      const existing = mountedOverlays.get(id);
      if (existing) {
        existing.data = data;
        if (existing.instance) {
          try { existing.instance.update && existing.instance.update(data); } catch (e) { log.error && log.error(`overlay ${id}: ${e && e.message}`); }
        }
        continue;
      }
      const el = h('div', { class: ['overlay', `overlay-${id}`], style: { zIndex: String(100 - rank(id)) } });
      const entry = { el, instance: null, data };
      mountedOverlays.set(id, entry);
      overlayHost.appendChild(el);
      loadOverlay(id, importer).then((mod) => {
        if (mountedOverlays.get(id) !== entry) return;
        entry.instance = mod.mount(overlayCtx(id, el), entry.data) || null;
      }).catch((e) => {
        log.error && log.error(`overlay ${id} failed: ${e && e.message}`);
        if (mountedOverlays.get(id) === entry) el.appendChild(h('div', { class: 'panel', text: String(id) }));
      });
    }
    updateChrome();
  }

  function openLocal(id, data = {}) {
    local.set(id, data);
    renderOverlays();
  }

  function closeLocal(id) {
    if (local.delete(id)) renderOverlays();
  }

  function checkLink() {
    const screen = current ? current.screen : null;
    let lastRxAgo = null;
    try { lastRxAgo = game.link && typeof game.link.lastRxAgo === 'function' ? game.link.lastRxAgo() : null; } catch { lastRxAgo = null; }
    const ids = overlayStack({ popup: game.popup, gameOverlays: game.overlays, local, badConnection: false }).map((o) => o.id);
    const show = shouldShowBadConnection({ screen, lastRxAgo, connected: game.state ? game.state.connected : undefined, overlayIds: ids });
    if (show !== badConnection) {
      badConnection = show;
      renderOverlays();
    }
  }

  function applyScenario() {
    if (!assets || assets.mode !== 'art') return;
    const id = game.state && game.state.scenario !== undefined && game.state.scenario !== null ? game.state.scenario : -1;
    const s = assets.scenario(id) || assets.scenario(-1);
    if (s && s.background) {
      bg.style.backgroundImage = cssUrl(s.background);
      bg.classList.toggle('tiled', !!s.tiled);
    } else {
      bg.style.backgroundImage = '';
    }
  }

  applyBodyDataset(body, bodyDataset(null, []));
  nav.hidden = true;

  return {
    shell,
    get currentScreen() { return current ? current.screen : null; },
    start() {
      applyBodyDataset(body, bodyDataset(null, []));
      updateChrome();
      offs.push(game.on('view', (v) => { showView(v || game.view); }));
      offs.push(game.on('overlays', () => renderOverlays()));
      offs.push(game.on('popup', () => {
        if (game.popup) local.delete('leave');
        renderOverlays();
      }));
      offs.push(game.on('state', () => { applyScenario(); checkLink(); }));
      applyScenario();
      renderOverlays();
      const linkTimer = timers.setInterval(checkLink, 500);
      offs.push(() => timers.clearInterval(linkTimer));
      return showView(game.view);
    },
    openOverlay: openLocal,
    closeOverlay: closeLocal,
    checkLink,
    destroy() {
      destroyed = true;
      for (const off of offs) { try { if (typeof off === 'function') off(); } catch { /* ignore */ } }
      if (current && current.instance) { try { current.instance.unmount(); } catch { /* ignore */ } }
      for (const m of mountedOverlays.values()) { try { m.instance && m.instance.unmount(); } catch { /* ignore */ } }
      shell.remove();
    },
  };
}

// A stand-in game for #demo=...: no connection, actions are only logged.
export function createDemoGame({ view, popup = null, overlays = [], log = console, lastRxAgo = 0, state = {} } = {}) {
  const listeners = new Map();
  const game = {
    view: { seq: 1, variant: '', data: {}, ...view },
    overlays,
    popup,
    state: {
      phase: 'lobby', playerId: 2, slotId: 0, jokers: 2, scenario: -1, roundType: 0, question: '',
      profile: { name: 'Alex', cardId: 'CARD_3', isGuest: false, portraitUrl: null }, connected: true,
      ...state,
    },
    resources: { url: () => null, has: () => false, on: () => () => {} },
    thumbnails: { url: () => null },
    link: { state: 'open', lastRxAgo: () => lastRxAgo },
    on(event, fn) {
      if (!listeners.has(event)) listeners.set(event, new Set());
      listeners.get(event).add(fn);
      return () => listeners.get(event).delete(fn);
    },
    emit(event, ...args) {
      for (const fn of listeners.get(event) || []) fn(...args);
    },
    setDraftProvider() {},
    boot() {},
    calls: [],
  };
  game.actions = new Proxy({}, {
    get(_, name) {
      if (typeof name !== 'string') return undefined;
      return (...args) => {
        game.calls.push([name, ...args]);
        if (log && log.info) log.info(`demo action ${name}`);
        if (name === 'dismissPopup') { game.popup = null; game.emit('popup', null); }
        return Promise.resolve(true);
      };
    },
  });
  return game;
}
