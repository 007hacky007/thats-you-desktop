// Continue (tap) and start (press and hold about 2 s, charging a ring).

import { attachHold } from '../hold.js';

export const HOLD_MS = 2000;

export const demos = [
  { variant: 'continue', data: {} },
  { variant: 'start', data: {} },
];

const SVG = 'http://www.w3.org/2000/svg';
const RING_R = 54;
export const RING_LENGTH = 2 * Math.PI * RING_R;

export function ringOffset(p) {
  const q = Math.min(1, Math.max(0, Number(p) || 0));
  return RING_LENGTH * (1 - q);
}

function svg(tag, attrs) {
  const e = globalThis.document.createElementNS(SVG, tag);
  for (const [k, v] of Object.entries(attrs)) e.setAttribute(k, String(v));
  return e;
}

export function mount(ctx, view) {
  const { h, i18n, el, game, sound } = ctx;
  const t = (k, ...a) => i18n.t(k, ...a);
  let variant = null;
  let detachHold = null;
  let pressed = false;

  function build() {
    if (detachHold) { detachHold(); detachHold = null; }
    while (el.firstChild) el.removeChild(el.firstChild);
    pressed = false;
    if (variant === 'start') {
      const ring = svg('svg', { class: 'ring', viewBox: '0 0 120 120', 'aria-hidden': 'true' });
      ring.appendChild(svg('circle', { class: 'ring-bg', cx: 60, cy: 60, r: RING_R }));
      const bar = svg('circle', {
        class: 'ring-bar', cx: 60, cy: 60, r: RING_R,
        'stroke-dasharray': RING_LENGTH.toFixed(2), 'stroke-dashoffset': RING_LENGTH.toFixed(2),
        transform: 'rotate(-90 60 60)',
      });
      ring.appendChild(bar);
      const btn = h('button', { class: 'hold-button', testid: 'start', type: 'button', attrs: { 'aria-label': t('port.holdToStart') } },
        ring, h('span', { class: 'hold-label', text: t('port.start') }));
      const hint = h('p', { class: 'small', text: t('port.holdToStart') });
      el.appendChild(h('div', { class: 'stack center continue start' },
        h('h2', { class: 'title', text: t('PartyReadyToStartMessage') }), btn, hint));
      detachHold = attachHold(btn, {
        ms: HOLD_MS,
        onProgress: (p) => {
          bar.setAttribute('stroke-dashoffset', ringOffset(p).toFixed(2));
          if (p > 0 && p < 1) btn.classList.add('charging');
        },
        onCancel: () => btn.classList.remove('charging'),
        onDone: () => {
          if (pressed) return;
          pressed = true;
          btn.classList.remove('charging');
          btn.classList.add('done');
          btn.disabled = true;
          sound.play('START_BUTTON_FULL');
          game.actions.startPressed();
        },
      });
      btn.addEventListener('pointerdown', () => sound.play('START_BUTTON_HELD'));
    } else {
      const btn = h('button', { class: 'btn btn-primary btn-big', testid: 'continue', type: 'button', text: t('PartyContinueButtonLabel') });
      btn.addEventListener('click', () => {
        if (pressed) return;
        pressed = true;
        btn.disabled = true;
        sound.play('CONTINUE_BUTTON_SELECT');
        game.actions.continuePressed();
      });
      el.appendChild(h('div', { class: 'stack center continue' }, btn));
      sound.play('CONTINUE_BUTTON_ARRIVE');
    }
  }

  function update(v) {
    const next = v && v.variant === 'start' ? 'start' : 'continue';
    if (next !== variant) {
      variant = next;
      build();
    }
  }
  update(view);
  return { update, unmount() { if (detachHold) detachHold(); } };
}
