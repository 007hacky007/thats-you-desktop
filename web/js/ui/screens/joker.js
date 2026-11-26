// Joker earned (JOKER_EARNED).

export const demos = [
  { variant: '', data: { pre: 1, cur: 2 } },
  { variant: 'plural', data: { pre: 0, cur: 2 } },
];

export function jokerTitleKey(pre, cur) {
  const added = Number(cur) - Number(pre);
  return added > 1 ? 'JokerAddedPluralMessage' : 'JokerAddedMessage';
}

export function mount(ctx, view) {
  const { h, i18n, el, assets, icon, sound } = ctx;
  const title = h('h2', { class: 'title' });
  const art = h('div', { class: 'joker-art' });
  const count = h('p', { class: 'joker-count' });
  const label = h('p', { class: 'small joker-label' });
  el.appendChild(h('div', { class: 'stack center joker' }, title, art, h('div', { class: 'joker-counter' }, count, label)));

  const artUrl = assets && assets.url ? assets.url('ui.joker') : null;
  if (artUrl) art.appendChild(h('img', { class: 'joker-img', attrs: { src: artUrl, alt: '' } }));
  else art.appendChild(icon('joker'));
  sound.play('GAIN_JOKER');

  function update(v) {
    const d = (v && v.data) || {};
    title.textContent = i18n.t(jokerTitleKey(d.pre, d.cur));
    const cur = Number.isInteger(d.cur) && d.cur >= 0 ? d.cur : 0;
    count.textContent = String(cur);
    label.textContent = i18n.t('JokerCounterLabel');
  }
  update(view);
  return { update, unmount() {} };
}
