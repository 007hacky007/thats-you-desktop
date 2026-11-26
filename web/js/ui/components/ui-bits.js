// DOM helpers shared by the play screens and components (browser only).
// Everything goes through ctx.h and textContent; no HTML strings.

import { artUrl, cssUrl, displayText } from './play-util.js';

// Icon for a button: the art sprite in art mode when it exists, else ctx.icon.
export function iconEl(ctx, name, sprite) {
  const { h, assets } = ctx;
  if (sprite && assets && assets.mode === 'art' && typeof assets.sprite === 'function') {
    try {
      if (assets.sprite(sprite)) {
        const el = h('span', { class: 'play-sprite', attrs: { 'aria-hidden': 'true' } });
        assets.applySprite(el, sprite);
        return el;
      }
    } catch { /* fall through to the neutral icon */ }
  }
  try {
    const svg = ctx.icon ? ctx.icon(name) : null;
    if (svg) return svg;
  } catch { /* no icon */ }
  return h('span', { class: 'play-icon-missing', attrs: { 'aria-hidden': 'true' } });
}

// A button with an optional icon and label; label is set through textContent.
export function button(ctx, { testid, cls = '', icon, sprite, label, title, onClick }) {
  const { h } = ctx;
  const kids = [];
  if (icon) kids.push(iconEl(ctx, icon, sprite));
  if (label != null) kids.push(h('span', { class: 'play-btn-label', text: displayText(label) }));
  const attrs = { type: 'button' };
  if (title) attrs['aria-label'] = title;
  const el = h('button', {
    class: `play-btn ${cls}`.trim(), testid, attrs, on: onClick ? { click: (ev) => { if (!el.disabled) onClick(ev); } } : undefined,
  }, ...kids);
  return el;
}

export function setShown(el, shown) {
  if (el) el.hidden = !shown;
}

export function setEnabled(el, enabled) {
  if (el) {
    el.disabled = !enabled;
    el.setAttribute('aria-disabled', enabled ? 'false' : 'true');
  }
}

export function setPressed(el, on) {
  if (!el) return;
  el.setAttribute('aria-pressed', on ? 'true' : 'false');
  el.classList.toggle('is-selected', !!on);
}

export function setArtBackground(el, assets, key) {
  const u = artUrl(assets, key);
  if (u) {
    el.style.backgroundImage = cssUrl(u);
    el.classList.add('has-art');
  }
  return !!u;
}

export function jpegUrl(jpeg) {
  return URL.createObjectURL(new Blob([jpeg], { type: 'image/jpeg' }));
}

export function playSound(ctx, event) {
  try {
    if (ctx.sound && typeof ctx.sound.play === 'function') ctx.sound.play(event);
  } catch { /* sounds are optional */ }
}

// Local overlay marker on body.dataset.overlay (contract 5.10); returns restore().
export function markOverlay(id) {
  const body = globalThis.document && document.body;
  if (!body) return () => {};
  const prev = body.dataset.overlay || '';
  body.dataset.overlay = id;
  return () => {
    if (body.dataset.overlay === id) body.dataset.overlay = prev;
  };
}
