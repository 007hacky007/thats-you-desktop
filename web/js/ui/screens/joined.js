// "You're in" lobby screen (JOINED_GAME).

export const demos = [{ variant: '', data: { playerId: 2, slotId: 0 } }];

export function mount(ctx, view) {
  const { h, i18n, el, icon } = ctx;
  const name = h('p', { class: 'player-name' });
  el.appendChild(h('div', { class: 'stack center joined' },
    h('div', { class: 'badge-icon' }, icon('check')),
    h('h2', { class: 'title', text: i18n.t('JoinedGameMessage') }),
    name,
    h('p', { class: 'text', text: i18n.t('WaitForGameStartInstruction') }),
    h('div', { class: 'dots', attrs: { 'aria-hidden': 'true' } }, h('span'), h('span'), h('span'))));

  function update() {
    const p = ctx.game && ctx.game.state && ctx.game.state.profile;
    name.textContent = p && p.name ? p.name : '';
    name.hidden = !name.textContent;
  }
  update(view);
  return { update, unmount() {} };
}
