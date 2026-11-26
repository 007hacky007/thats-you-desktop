// Pause overlay (commands 29 / 30).

export function mount(ctx) {
  const { h, i18n, el, icon } = ctx;
  el.appendChild(h('div', { class: 'scrim' },
    h('div', { class: 'panel pause', attrs: { role: 'dialog', 'aria-modal': 'true' } },
      h('div', { class: 'badge-icon' }, icon('pause')),
      h('h2', { class: 'title', text: i18n.t('PausedTitle') }),
      h('p', { class: 'text', text: i18n.t('PausedMessage') }))));
  return { update() {}, unmount() {} };
}
