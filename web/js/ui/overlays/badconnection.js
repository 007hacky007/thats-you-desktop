// Small non-blocking indicator: nothing received for more than 5.7 s.

export function mount(ctx) {
  const { h, i18n, el, icon } = ctx;
  el.appendChild(h('div', { class: 'bad-connection', attrs: { role: 'status', 'aria-live': 'polite' } },
    icon('connection'), h('span', { text: i18n.t('port.badConnection') })));
  return { update() {}, unmount() {} };
}
