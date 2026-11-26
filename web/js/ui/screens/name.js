// Name entry (NAME_ENTRY). Limited to data.maxLength code points (10).

import { clampCodePoints } from '../../util/dom.js';

export const demos = [
  { variant: '', data: { name: '', isGuest: false, maxLength: 10 } },
  { variant: 'filled', data: { name: 'Alex', isGuest: true, maxLength: 10 } },
];

export function mount(ctx, view) {
  const { h, i18n, el, game, sound } = ctx;
  let maxLength = 10;
  let sent = false;

  const input = h('input', {
    class: 'text-field', testid: 'name-input', type: 'text',
    attrs: {
      autocomplete: 'off', autocorrect: 'off', autocapitalize: 'words', spellcheck: 'false',
      enterkeyhint: 'done', 'aria-label': i18n.t('EnterNameTitle'), placeholder: i18n.t('port.namePlaceholder'),
    },
  });
  const guest = h('input', { type: 'checkbox', testid: 'name-guest', class: 'toggle-input' });
  const guestRow = h('label', { class: 'toggle' }, guest, h('span', { class: 'toggle-track', attrs: { 'aria-hidden': 'true' } }), h('span', { text: i18n.t('port.guest') }));
  const counter = h('span', { class: 'counter small', attrs: { 'aria-hidden': 'true' } });
  const next = h('button', { class: 'btn btn-primary', testid: 'name-next', type: 'button', text: i18n.t('port.next') });

  const clean = () => input.value.trim();
  const refresh = () => {
    const n = Array.from(input.value).length;
    counter.textContent = `${n}/${maxLength}`;
    next.disabled = sent || clean().length === 0;
  };
  const submit = () => {
    if (sent || !clean()) return;
    sent = true;
    refresh();
    sound.play('CONFIRM_BUTTON_CLICKED');
    if (input.blur) input.blur();
    game.actions.profileName(clampCodePoints(clean(), maxLength), !!guest.checked);
  };

  input.addEventListener('input', () => {
    const v = clampCodePoints(input.value, maxLength);
    if (v !== input.value) input.value = v;
    refresh();
  });
  input.addEventListener('keydown', (ev) => {
    if (ev.key === 'Enter') { ev.preventDefault(); submit(); }
  });
  next.addEventListener('click', submit);

  el.appendChild(h('div', { class: 'stack form name' },
    h('h2', { class: 'title', text: i18n.t('EnterNameTitle') }),
    h('div', { class: 'card-panel' }, h('div', { class: 'field-row' }, input, counter), guestRow),
    next));

  function update(v) {
    const d = (v && v.data) || {};
    maxLength = Number.isInteger(d.maxLength) && d.maxLength > 0 ? d.maxLength : 10;
    input.setAttribute('maxlength', String(maxLength * 2));
    if (globalThis.document && globalThis.document.activeElement !== input) {
      input.value = clampCodePoints(d.name || '', maxLength);
    }
    guest.checked = !!d.isGuest;
    sent = false;
    refresh();
  }
  update(view);
  return { update, unmount() {} };
}
