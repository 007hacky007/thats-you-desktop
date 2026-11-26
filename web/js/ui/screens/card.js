// Card choice (CARD_ENTRY): 12 personality cards, tap to pick, flick up or
// press the confirm button.

import { attachFlick } from '../flick.js';

export const CARD_IDS = Array.from({ length: 12 }, (_, i) => `CARD_${i + 1}`);

export const demos = [
  { variant: '', data: { cardId: null } },
  { variant: 'picked', data: { cardId: 'CARD_3' } },
];

export function cardNumber(cardId) {
  const m = /^CARD_(\d{1,2})$/.exec(String(cardId || ''));
  const n = m ? Number(m[1]) : 0;
  return n >= 1 && n <= 12 ? n : 0;
}

// One card face: the app's card art, or a neutral numbered tile.
export function cardFace(ctx, cardId, extraClass) {
  const { h, assets, i18n } = ctx;
  const n = cardNumber(cardId);
  const url = assets && assets.card ? assets.card(cardId) : null;
  if (url) {
    return h('img', { class: ['card-face', 'card-art', extraClass], attrs: { src: url, alt: i18n.t('port.card', n), draggable: 'false' } });
  }
  return h('div', { class: ['card-face', 'card-tile', `card-tile-${n}`, extraClass], attrs: { role: 'img', 'aria-label': i18n.t('port.card', n) } },
    h('span', { class: 'card-num', text: String(n) }));
}

export function mount(ctx, view) {
  const { h, i18n, el, game, sound, icon } = ctx;
  let selected = null;
  let sent = false;

  const preview = h('div', { class: 'card-preview' });
  const confirm = h('button', { class: 'btn btn-primary btn-icon', testid: 'card-confirm', type: 'button' },
    icon('check'), h('span', { text: i18n.t('port.confirm') }));
  const buttons = CARD_IDS.map((id, i) => {
    const b = h('button', { class: 'card-choice', testid: `card-${i + 1}`, type: 'button', attrs: { 'aria-pressed': 'false' } },
      cardFace(ctx, id, 'small'));
    b.addEventListener('click', () => select(id));
    return b;
  });

  const submit = () => {
    if (sent || !selected) return;
    sent = true;
    render();
    sound.play('CONFIRM_SWIPE');
    game.actions.profileCard(selected);
  };
  confirm.addEventListener('click', submit);
  const detachFlick = attachFlick(preview, { onFlick: (dir) => { if (dir === 'up') submit(); } });

  function select(id) {
    if (sent) return;
    selected = id;
    sound.play('PROFILE_CARD_SELECT');
    render();
  }

  function render() {
    buttons.forEach((b, i) => {
      const on = CARD_IDS[i] === selected;
      b.classList.toggle('selected', on);
      b.setAttribute('aria-pressed', on ? 'true' : 'false');
      b.disabled = sent;
    });
    while (preview.firstChild) preview.removeChild(preview.firstChild);
    if (selected) preview.appendChild(cardFace(ctx, selected, 'large'));
    preview.hidden = !selected;
    confirm.disabled = sent || !selected;
  }

  el.appendChild(h('div', { class: 'stack card-screen' },
    h('h2', { class: 'title', text: i18n.t('SelectCardTitle') }),
    preview,
    h('div', { class: 'card-grid' }, ...buttons),
    confirm));

  function update(v) {
    const id = v && v.data && v.data.cardId;
    selected = cardNumber(id) ? id : selected;
    sent = false;
    render();
  }
  update(view);
  return { update, unmount() { detachFlick(); } };
}
