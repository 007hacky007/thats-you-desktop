// Camera effects panel: a sheet with one button per effect (effect-0 .. effect-11),
// names from CameraEffectName_<name>. Browser only.

import { effectEntries, tr } from './play-util.js';
import { button, setPressed, markOverlay, playSound } from './ui-bits.js';

const FALLBACK_NAMES = { V2Mirror: 'Double Mirror' };

export function createEffectsPanel(ctx, { effects, webgl = true, current = 0, onPick, onClose }) {
  const { h, i18n } = ctx;
  const entries = effectEntries(effects, { webgl });
  const buttons = new Map();
  let restoreOverlay = null;

  const list = h('div', { class: 'play-fx-list', attrs: { role: 'listbox' } });
  for (const e of entries) {
    const label = tr(i18n, e.key, FALLBACK_NAMES[e.name] || e.name);
    const b = button(ctx, {
      testid: e.testid,
      cls: 'play-fx-item',
      label,
      onClick: () => {
        playSound(ctx, 'button');
        setCurrent(e.id);
        if (onPick) onPick(e.id);
      },
    });
    b.dataset.effect = String(e.id);
    buttons.set(e.id, b);
    list.appendChild(b);
  }
  const close = button(ctx, {
    testid: 'effects-close', cls: 'play-fx-close', icon: 'check', sprite: 'icon_dismiss_cross', title: 'Close',
    onClick: () => { if (onClose) onClose(); },
  });
  const sheet = h('div', { class: 'play-fx-sheet' }, list, close);
  const el = h('div', { class: 'play-fx', attrs: { role: 'dialog' } }, sheet);
  el.hidden = true;
  el.addEventListener('click', (ev) => {
    if (ev.target === el && onClose) onClose();
  });

  function setCurrent(id) {
    for (const [eid, b] of buttons) setPressed(b, eid === id);
  }
  setCurrent(current);

  return {
    el,
    entries,
    get open() { return !el.hidden; },
    show() {
      if (!el.hidden) return;
      el.hidden = false;
      restoreOverlay = markOverlay('effects');
      const sel = [...buttons.values()].find((b) => b.classList.contains('is-selected'));
      if (sel && sel.scrollIntoView) sel.scrollIntoView({ block: 'nearest', inline: 'center' });
    },
    hide() {
      if (el.hidden) return;
      el.hidden = true;
      if (restoreOverlay) { restoreOverlay(); restoreOverlay = null; }
    },
    setCurrent,
    destroy() {
      if (restoreOverlay) { restoreOverlay(); restoreOverlay = null; }
    },
  };
}
