// QuickDraw holding: the next picture to draw, its heading and description,
// whose it is and how many drawings remain.

export const demos = [{
  variant: '',
  data: { imageGuid: 'demo-guid', heading: 'DRAW THIS', description: 'Add something\nfunny to it', remaining: 3, playerName: 'Sam' },
}];

export function remainingText(n, t) {
  const k = Number(n);
  if (Number.isInteger(k) && k >= 1 && k <= 5) return t(`DrawingsToGoMessage${k}`);
  if (Number.isInteger(k) && k > 5) return t('port.remaining', k);
  return '';
}

export function mount(ctx, view) {
  const { h, i18n, el, game, icon } = ctx;
  const t = (k, ...a) => i18n.t(k, ...a);
  let guid = null;

  const pic = h('div', { class: 'polaroid qd-picture' });
  const heading = h('h2', { class: 'title game-text' });
  const desc = h('p', { class: 'text game-text' });
  const owner = h('p', { class: 'small game-text' });
  const remaining = h('p', { class: 'pill' });
  el.appendChild(h('div', { class: 'stack center quickdraw-holding' }, heading, pic, owner, desc, remaining));

  function showPicture() {
    while (pic.firstChild) pic.removeChild(pic.firstChild);
    const url = guid && game.resources ? game.resources.url(guid) : null;
    if (url) pic.appendChild(h('img', { class: 'photo', attrs: { src: url, alt: '' } }));
    else pic.appendChild(h('div', { class: 'photo placeholder' }, icon('brush')));
  }
  const off = game.resources && game.resources.on ? game.resources.on('added', (g) => { if (g === guid) showPicture(); }) : null;

  function update(v) {
    const d = (v && v.data) || {};
    guid = d.imageGuid || null;
    heading.textContent = d.heading || '';
    heading.hidden = !d.heading;
    desc.textContent = d.description || '';
    desc.hidden = !d.description;
    owner.textContent = d.playerName ? t('port.quickdrawBy', d.playerName) : '';
    owner.hidden = !d.playerName;
    remaining.textContent = remainingText(d.remaining, t);
    remaining.hidden = !remaining.textContent;
    showPicture();
  }
  update(view);
  return { update, unmount() { if (typeof off === 'function') off(); } };
}
