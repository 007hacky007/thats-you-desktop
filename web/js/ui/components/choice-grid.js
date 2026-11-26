// Choice grid of the answer screen: portraits, question images, drawings or photos
// (game.resources by GUID, placeholders until they arrive) or text cards.
// Tap selects, flicking a card up confirms it. Browser only.

import { numberChoices, gridLayout, choiceKind } from './choice-logic.js';
import { displayText } from './play-util.js';
import { setPressed, setEnabled, setArtBackground, playSound } from './ui-bits.js';
import { createResourceImage } from './resource-image.js';
import { attachFlick } from '../flick.js';

function sameItems(a, b) {
  if (a.length !== b.length) return false;
  for (let i = 0; i < a.length; i += 1) {
    if (a[i].pos !== b[i].pos || a[i].guid !== b[i].guid || a[i].text !== b[i].text) return false;
  }
  return true;
}

// opts: { variant, choices, selected, onSelect(pos), onConfirm(pos) }
export function createChoiceGrid(ctx, opts) {
  const { h } = ctx;
  const el = h('div', { class: 'play-grid' });
  let items = [];
  let cards = [];
  let enabled = true;
  let kind = choiceKind(opts.variant);

  function clearCards() {
    for (const c of cards) {
      if (c.image) c.image.destroy();
      if (c.detach) c.detach();
    }
    cards = [];
    el.replaceChildren();
  }

  function build() {
    clearCards();
    const { columns, rows } = gridLayout(items.length, kind);
    el.style.setProperty('--play-grid-cols', String(columns));
    el.style.setProperty('--play-grid-rows', String(Math.max(1, rows)));
    el.dataset.kind = kind;
    el.dataset.count = String(items.length);
    for (const item of items) {
      const kids = [];
      let image = null;
      if (kind === 'image') {
        image = createResourceImage(ctx, item.guid, { cls: 'play-choice-img' });
        kids.push(image.el);
        if (item.text) kids.push(h('span', { class: 'play-choice-caption', text: displayText(item.text) }));
      } else {
        kids.push(h('span', { class: 'play-choice-text', text: displayText(item.text) }));
      }
      const card = h('button', {
        class: `play-choice play-choice-${kind} play-choice-${opts.variant || 'images'}`,
        testid: item.testid,
        attrs: { type: 'button', 'aria-pressed': 'false' },
        dataset: { pos: String(item.pos) },
        on: {
          click: () => {
            if (!enabled) return;
            playSound(ctx, 'button');
            if (opts.onSelect) opts.onSelect(item.pos);
          },
        },
      }, ...kids);
      if (kind === 'text') setArtBackground(card, ctx.assets, 'ui.textCard');
      else if (opts.variant === 'drawings' || opts.variant === 'portraits') setArtBackground(card, ctx.assets, 'ui.polaroidMini');
      const detach = attachFlick(card, {
        onFlick: (dir) => {
          if (!enabled || dir !== 'up') return;
          if (opts.onConfirm) opts.onConfirm(item.pos);
        },
      });
      cards.push({ item, el: card, image, detach });
      el.appendChild(card);
    }
  }

  function setSelected(sel) {
    for (const c of cards) setPressed(c.el, c.item.pos === sel);
    el.classList.toggle('has-selection', sel != null);
  }

  function update({ variant, choices, selected }) {
    if (variant !== undefined && variant !== opts.variant) {
      opts.variant = variant;
      kind = choiceKind(variant);
      items = [];
    }
    const next = numberChoices(choices);
    if (!sameItems(items, next)) {
      items = next;
      build();
    }
    setSelected(selected);
  }

  update({ choices: opts.choices, selected: opts.selected });

  return {
    el,
    update,
    setSelected,
    setEnabled(on) {
      enabled = !!on;
      el.classList.toggle('is-disabled', !enabled);
      for (const c of cards) setEnabled(c.el, enabled);
    },
    cardFor(pos) {
      const c = cards.find((x) => x.item.pos === pos);
      return c ? c.el : null;
    },
    destroy() { clearCards(); },
  };
}
