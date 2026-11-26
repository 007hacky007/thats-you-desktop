// Profile confirmation (CONFIRM_PROFILE): name, card and selfie, with buttons
// to edit each step and to confirm (also by flicking the card up).

import { attachFlick } from '../flick.js';
import { cardFace } from './card.js';

export const demos = [{ variant: '', data: { name: 'Alex', cardId: 'CARD_3', portraitUrl: null } }];

export function mount(ctx, view) {
  const { h, i18n, el, game, sound, icon } = ctx;
  let sent = false;

  const photo = h('div', { class: 'polaroid' });
  const nameEl = h('p', { class: 'polaroid-caption' });
  const cardSlot = h('div', { class: 'confirm-card' });
  const profile = h('div', { class: 'confirm-profile' }, h('div', { class: 'polaroid-frame' }, photo, nameEl), cardSlot);

  const editBtn = (testid, label, step, ic) => {
    const b = h('button', { class: 'btn btn-small btn-icon', testid, type: 'button' }, icon(ic), h('span', { text: i18n.t(label) }));
    b.addEventListener('click', () => {
      if (sent) return;
      sound.play('BACK_BUTTON_CLICKED');
      game.actions.profileEdit(step);
    });
    return b;
  };
  const editName = editBtn('profile-edit-name', 'port.editName', 'name', 'person');
  const editCard = editBtn('profile-edit-card', 'port.editCard', 'card', 'joker');
  const editPhoto = editBtn('profile-edit-photo', 'port.editPhoto', 'photo', 'camera');
  const confirm = h('button', { class: 'btn btn-primary btn-icon', testid: 'profile-confirm', type: 'button' },
    icon('check'), h('span', { text: i18n.t('port.confirm') }));

  const submit = () => {
    if (sent) return;
    sent = true;
    for (const b of [editName, editCard, editPhoto, confirm]) b.disabled = true;
    sound.play('CONFIRM_PROFILE_CONFIRM_SWISH');
    game.actions.profileConfirm();
  };
  confirm.addEventListener('click', submit);
  const detach = attachFlick(profile, { onFlick: (d) => { if (d === 'up') submit(); } });

  el.appendChild(h('div', { class: 'stack confirm' },
    h('h2', { class: 'title', text: i18n.t('ConfirmProfileTitle') }),
    profile,
    h('div', { class: 'row wrap center' }, editName, editCard, editPhoto),
    confirm));

  function update(v) {
    const d = (v && v.data) || {};
    while (photo.firstChild) photo.removeChild(photo.firstChild);
    if (d.portraitUrl) photo.appendChild(h('img', { class: 'photo', attrs: { src: d.portraitUrl, alt: '' } }));
    else photo.appendChild(h('div', { class: 'photo placeholder' }, icon('person')));
    nameEl.textContent = d.name || '';
    while (cardSlot.firstChild) cardSlot.removeChild(cardSlot.firstChild);
    if (d.cardId) cardSlot.appendChild(cardFace(ctx, d.cardId, 'medium'));
    sent = false;
    for (const b of [editName, editCard, editPhoto, confirm]) b.disabled = false;
  }
  update(view);
  return { update, unmount() { detach(); } };
}
