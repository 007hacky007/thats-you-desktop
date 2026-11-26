// Holding screens: general, wait, look-at-tv, other-photo, other-ugc, rejoin
// and custom. Game texts are shown with textContent (white-space: pre-line).

export const demos = [
  { variant: 'general', data: {} },
  { variant: 'wait', data: {} },
  { variant: 'look-at-tv', data: {} },
  { variant: 'other-photo', data: { playerName: 'Sam' } },
  { variant: 'other-ugc', data: {} },
  { variant: 'rejoin', data: {} },
  { variant: 'custom', data: { text: 'The host is choosing\nthe next round' } },
];

// What one variant shows: { icon, title, body } with texts already resolved.
export function holdingContent(variant, data, t) {
  const d = data || {};
  switch (variant) {
    case 'wait':
      return { icon: null, title: '', body: '', dots: true };
    case 'look-at-tv':
      return { icon: 'tv', title: t('LookAtTvInstruction'), body: '', dots: false };
    case 'other-photo':
      return { icon: 'camera', title: t('OtherPersonTakingPhotoMessage', d.playerName || d.text || ''), body: t('OtherPersonTakingPhotoInstruction'), dots: false };
    case 'other-ugc':
      return { icon: 'person', title: t('port.otherUgc'), body: d.text || '', dots: true };
    case 'rejoin':
      return { icon: 'check', title: t('JoinedGameMessage'), body: t('WaitForNextQuestionInstruction'), dots: true };
    case 'custom':
      return { icon: null, title: '', body: d.text || '', dots: false };
    case 'general':
    default:
      return { icon: null, title: d.text || '', body: d.text ? '' : t('port.wait'), dots: true };
  }
}

export function mount(ctx, view) {
  const { h, i18n, el, icon } = ctx;
  const t = (k, ...a) => i18n.t(k, ...a);
  const iconSlot = h('div', { class: 'badge-icon big' });
  const title = h('h2', { class: 'title game-text' });
  const body = h('p', { class: 'text game-text' });
  const dots = h('div', { class: 'dots', attrs: { 'aria-hidden': 'true' } }, h('span'), h('span'), h('span'));
  const box = h('div', { class: 'stack center holding' }, iconSlot, title, body, dots);
  el.appendChild(box);

  function update(v) {
    const variant = (v && v.variant) || 'general';
    const c = holdingContent(variant, v && v.data, t);
    while (iconSlot.firstChild) iconSlot.removeChild(iconSlot.firstChild);
    if (c.icon) iconSlot.appendChild(icon(c.icon));
    iconSlot.hidden = !c.icon;
    title.textContent = c.title;
    title.hidden = !c.title;
    body.textContent = c.body;
    body.hidden = !c.body;
    dots.hidden = !c.dots;
    box.dataset.variant = variant;
  }
  update(view);
  return { update, unmount() {} };
}
