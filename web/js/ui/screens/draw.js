// Drawing screen (DRAWING): surface over the game-sent background, palette,
// tools, sizes, undo; confirm with draw-confirm or a flick of the send tab.

import { createDrawToolbar } from '../components/draw-toolbar.js';
import { initialDrawState, applyToSurface } from '../components/draw-state.js';
import { tr } from '../components/play-util.js';
import { button, setEnabled, setArtBackground, playSound } from '../components/ui-bits.js';
import { attachFlick } from '../flick.js';

export function mount(ctx, view) {
  const { h, game, media, i18n, log } = ctx;
  let destroyed = false;
  let submitted = false;
  let backgroundGuid = null;
  let offAdded = null;
  const offs = [];

  const surface = media.createDrawingSurface();
  surface.element.setAttribute('data-testid', 'draw-canvas');
  surface.element.classList.add('play-draw-canvas');

  const toolbar = createDrawToolbar(ctx, {
    state: initialDrawState(),
    onChange: (s) => applyToSurface(s, surface),
    onUndo: () => { if (surface.canUndo) surface.undo(); },
  });
  applyToSurface(toolbar.state, surface);

  const title = h('h1', { class: 'play-title play-draw-title', text: tr(i18n, 'GetReadyToDrawInstruction', 'GET READY TO DRAW') });
  const paper = h('div', { class: `play-draw-paper play-draw-${view.variant || 'canvas'}` }, surface.element);
  if (view.variant === 'canvas') setArtBackground(paper, ctx.assets, 'ui.newspaperCanvas');
  else setArtBackground(paper, ctx.assets, 'ui.polaroid');

  const confirmBtn = button(ctx, {
    testid: 'draw-confirm', cls: 'play-confirm play-primary', icon: 'check', sprite: 'icon_confirm_tick',
    label: tr(i18n, 'port.confirm', 'OK'), onClick: () => submit(),
  });
  const flickTab = h('div', { class: 'play-flick-tab', attrs: { 'aria-hidden': 'true' } }, h('span', { class: 'play-flick-arrow' }));
  const actions = h('div', { class: 'play-actions play-draw-actions' }, flickTab, confirmBtn);

  const root = h('section', { class: 'play-screen play-draw' }, title, paper, toolbar.el, actions);
  ctx.el.appendChild(root);

  offs.push(attachFlick(flickTab, { onFlick: (dir) => { if (dir === 'up') submit(); } }));
  offs.push(attachFlick(confirmBtn, { onFlick: (dir) => { if (dir === 'up') submit(); } }));
  offs.push(surface.on('change', () => {
    toolbar.dispatch({ type: 'surface', canUndo: surface.canUndo, isEmpty: surface.isEmpty });
  }));

  function exportDrawing() {
    if (destroyed) return Promise.resolve(null);
    return surface.export();
  }

  game.setDraftProvider((kind) => (kind === 'drawing' ? exportDrawing() : Promise.resolve(null)));

  async function submit() {
    if (submitted || destroyed) return;
    submitted = true;
    toolbar.dispatch({ type: 'busy', busy: true });
    setEnabled(confirmBtn, false);
    try {
      const payload = await surface.export();
      if (destroyed) return;
      playSound(ctx, 'confirmSwipe');
      paper.classList.add('is-flicked');
      game.actions.submitDrawing(payload);
    } catch (e) {
      if (log) log.warn('drawing export failed', String(e && e.message));
      submitted = false;
      toolbar.dispatch({ type: 'busy', busy: false });
      setEnabled(confirmBtn, true);
    }
  }

  function setBackground(guid) {
    backgroundGuid = guid || null;
    if (offAdded) { offAdded(); offAdded = null; }
    const url = backgroundGuid && game.resources ? game.resources.url(backgroundGuid) : null;
    if (url || !backgroundGuid) {
      surface.setBackground(url || null).catch((e) => { if (log) log.warn('background failed', String(e && e.message)); });
      return;
    }
    paper.classList.add('is-loading');
    offAdded = game.resources.on('added', (g) => {
      if (g !== backgroundGuid) return;
      const u = game.resources.url(g);
      if (!u) return;
      paper.classList.remove('is-loading');
      if (offAdded) { offAdded(); offAdded = null; }
      surface.setBackground(u).catch((e) => { if (log) log.warn('background failed', String(e && e.message)); });
    });
  }

  setBackground(view.data && view.data.backgroundGuid);

  return {
    update(next) {
      const g = next.data && next.data.backgroundGuid;
      if ((g || null) !== backgroundGuid) setBackground(g);
      for (const v of ['topdrawer', 'canvas', 'quickdraw']) paper.classList.toggle(`play-draw-${v}`, (next.variant || 'canvas') === v);
    },
    unmount() {
      destroyed = true;
      game.setDraftProvider(null);
      if (offAdded) offAdded();
      for (const off of offs) { try { off(); } catch { /* ignore */ } }
      try { surface.destroy(); } catch { /* ignore */ }
      root.remove();
    },
  };
}

export const demos = [
  { variant: 'topdrawer', data: { backgroundGuid: 'demo-topdrawer-photo' } },
  { variant: 'canvas', data: { backgroundGuid: 'demo-canvas-image' } },
  { variant: 'quickdraw', data: { backgroundGuid: 'demo-quickdraw-image' } },
];
