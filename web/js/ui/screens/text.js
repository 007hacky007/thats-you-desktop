// Text entry screen (TEXT_ENTRY): title from the game, a text card with the
// input (80 code points), confirm with text-confirm or a flick of the card.

import { createTextEntry } from '../components/text-logic.js';
import { displayText, tr } from '../components/play-util.js';
import { button, setEnabled, setArtBackground, playSound } from '../components/ui-bits.js';
import { attachFlick } from '../flick.js';

export function mount(ctx, view) {
  const { h, game, i18n } = ctx;
  const data = view.data || {};
  const entry = createTextEntry({ actions: game.actions, maxLength: data.maxLength || 80, initial: data.text || '' });
  let composing = false;

  const title = h('h1', { class: 'play-title play-text-title', text: displayText(data.title) });
  const input = h('textarea', {
    class: 'play-text-input',
    testid: 'text-input',
    attrs: {
      rows: '4', autocomplete: 'off', autocapitalize: 'sentences', spellcheck: 'true', enterkeyhint: 'done', 'aria-label': displayText(data.title),
    },
  });
  input.value = entry.text;
  const counter = h('span', { class: 'play-text-counter' });
  const card = h('div', { class: 'play-text-card' }, input, counter);
  setArtBackground(card, ctx.assets, 'ui.textCard');

  const confirmBtn = button(ctx, {
    testid: 'text-confirm', cls: 'play-confirm play-primary', icon: 'check', sprite: 'icon_confirm_tick',
    label: tr(i18n, 'port.confirm', 'OK'), onClick: () => submit(),
  });
  const root = h('section', { class: 'play-screen play-text' }, title, card, h('div', { class: 'play-actions' }, confirmBtn));
  ctx.el.appendChild(root);

  function syncInput() {
    if (composing) return;
    const shown = entry.input(input.value);
    if (shown !== input.value) {
      const pos = shown.length;
      input.value = shown;
      try { input.setSelectionRange(pos, pos); } catch { /* ignore */ }
    }
    renderCounter();
  }

  function renderCounter() {
    counter.textContent = String(entry.remaining);
    counter.classList.toggle('is-low', entry.remaining <= 10);
  }

  function submit() {
    if (composing) { composing = false; }
    syncInput();
    if (!entry.submit()) return;
    playSound(ctx, 'confirmSwipe');
    input.blur();
    input.readOnly = true;
    setEnabled(confirmBtn, false);
    card.classList.add('is-flicked');
  }

  input.addEventListener('input', syncInput);
  input.addEventListener('compositionstart', () => { composing = true; });
  input.addEventListener('compositionend', () => { composing = false; syncInput(); });
  input.addEventListener('keydown', (ev) => {
    if (ev.key === 'Enter' && !ev.isComposing) {
      ev.preventDefault();
      input.blur();
    }
  });
  const detach = attachFlick(card, { onFlick: (dir) => { if (dir === 'up') submit(); } });
  renderCounter();

  return {
    update(next) {
      const d = next.data || {};
      title.textContent = displayText(d.title);
      input.setAttribute('aria-label', displayText(d.title));
    },
    unmount() {
      detach();
      root.remove();
    },
  };
}

export const demos = [
  { variant: '', data: { title: 'FINISH THE SENTENCE:\nMY SECRET TALENT IS...', maxLength: 80, text: '' } },
];
