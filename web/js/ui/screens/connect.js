// Connect screen (replaces the app's start, search and console picker).
// data.state: noparams | idle | ready | connecting | waiting

export const demos = [
  { variant: '', data: { state: 'ready' } },
  { variant: 'noparams', data: { state: 'noparams' } },
  { variant: 'idle', data: { state: 'idle' } },
  { variant: 'connecting', data: { state: 'connecting' } },
  { variant: 'waiting', data: { state: 'waiting' } },
];

export function connectLabels(state) {
  switch (state) {
    case 'noparams': return { button: null, status: '', help: 'port.scanQr', busy: false };
    case 'idle': return { button: 'port.rejoin', status: '', help: 'port.rejoinHelp', busy: false };
    case 'connecting': return { button: 'JoinPS4ButtonLabel', status: 'port.connecting', help: '', busy: true };
    case 'waiting': return { button: 'JoinPS4ButtonLabel', status: 'port.waiting', help: '', busy: true };
    case 'ready':
    default: return { button: 'JoinPS4ButtonLabel', status: '', help: '', busy: false };
  }
}

// The PLAY tap: unlock sounds, ask for the camera inside the user gesture
// (iOS needs that), then start the connection whatever the answer was.
export async function playTapped({ media, game, sound, log }) {
  if (sound) {
    sound.unlock();
    sound.play('JOIN_PS4_BUTTON_CLICKED');
  }
  let allowed = false;
  try {
    const req = media && media.camera && typeof media.camera.requestPermission === 'function'
      ? media.camera.requestPermission() : null;
    allowed = req ? !!(await req) : false;
  } catch (e) {
    allowed = false;
    if (log && log.warn) log.warn(`camera permission: ${e && e.name ? e.name : e}`);
  }
  if (log && log.info) log.info(`camera permission ${allowed ? 'granted' : 'not granted'}`);
  return game.actions.play();
}

export function mount(ctx, view) {
  const { h, i18n, assets, el } = ctx;
  const t = (k, ...a) => i18n.t(k, ...a);

  const logoUrl = assets && assets.logo ? assets.logo(i18n.lang) : null;
  const brand = logoUrl
    ? h('img', { class: 'logo', attrs: { src: logoUrl, alt: t('port.title') } })
    : h('h1', { class: 'brand', text: t('port.title') });
  const subtitle = logoUrl ? null : h('p', { class: 'brand-sub', text: t('port.subtitle') });
  const status = h('p', { class: 'status', attrs: { role: 'status', 'aria-live': 'polite' } });
  const spinner = h('div', { class: 'spinner', attrs: { 'aria-hidden': 'true' } });
  const help = h('p', { class: 'help text' });
  let tapping = false;
  let state = 'ready';
  const play = h('button', { class: 'btn btn-primary btn-play', testid: 'connect-play', type: 'button' });
  play.addEventListener('click', () => {
    if (tapping || connectLabels(state).busy) return;
    tapping = true;
    render();
    Promise.resolve(playTapped(ctx)).catch((e) => {
      if (ctx.log && ctx.log.error) ctx.log.error(`play: ${e && e.message ? e.message : e}`);
    }).finally(() => {
      tapping = false;
      render();
    });
  });

  el.appendChild(h('div', { class: 'stack center connect' },
    h('div', { class: 'brand-box' }, brand, subtitle),
    h('div', { class: 'connect-actions' }, play, spinner, status),
    help));

  function render() {
    const l = connectLabels(state);
    play.hidden = !l.button;
    play.textContent = l.button ? t(l.button) : '';
    play.disabled = l.busy || tapping;
    spinner.hidden = !l.busy;
    status.textContent = l.status ? t(l.status) : '';
    help.textContent = l.help ? t(l.help) : '';
    help.hidden = !l.help;
  }

  function update(v) {
    state = (v && v.data && v.data.state) || 'ready';
    render();
  }

  update(view);
  return { update, unmount() {} };
}
