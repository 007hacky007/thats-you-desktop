// Shown while the page reconnects after a transport loss.

export function mount(ctx) {
  const { h, i18n, el } = ctx;
  el.appendChild(h('div', { class: 'scrim' },
    h('div', { class: 'panel reconnecting', attrs: { role: 'status', 'aria-live': 'polite' } },
      h('div', { class: 'spinner', attrs: { 'aria-hidden': 'true' } }),
      h('h2', { class: 'title', text: i18n.t('port.reconnecting') }),
      h('p', { class: 'small', text: i18n.t('port.reconnectingHelp') }))));
  return { update() {}, unmount() {} };
}
