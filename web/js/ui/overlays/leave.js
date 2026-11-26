// Leave confirmation (opened by the nav bar leave button).

export function mount(ctx) {
  const { h, i18n, el, game, sound, icon } = ctx;
  const confirm = h('button', { class: 'btn btn-danger', testid: 'leave-confirm', type: 'button', text: i18n.t('PopupLeaveButtonLabel') });
  const cancel = h('button', { class: 'btn btn-primary', testid: 'leave-cancel', type: 'button', text: i18n.t('PopupCancelButtonLabel') });
  confirm.addEventListener('click', () => {
    confirm.disabled = true;
    sound.play('GENERIC_BUTTON_CLICKED');
    ctx.close();
    game.actions.leave();
  });
  cancel.addEventListener('click', () => {
    sound.play('BACK_BUTTON_CLICKED');
    ctx.close();
  });
  const panel = h('div', { class: 'panel', attrs: { role: 'alertdialog', 'aria-modal': 'true' } },
    h('div', { class: 'badge-icon' }, icon('leave')),
    h('p', { class: 'text', text: i18n.t('LeavePartyGamePopupMessage') }),
    h('div', { class: 'row center wrap' }, cancel, confirm));
  el.appendChild(h('div', { class: 'scrim' }, panel));
  return { update() {}, unmount() {} };
}
