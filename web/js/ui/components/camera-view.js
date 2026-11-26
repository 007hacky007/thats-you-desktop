// Camera view used by the selfie, photo and mimic screens: live feed with effects
// (media.createRenderer), flip, shutter, retake, use, effects panel and a no-camera
// path with media.placeholderPortrait. Browser only.

import { createPhotoFlow } from './photo-flow.js';
import { createEffectsPanel } from './effects-panel.js';
import { tr } from './play-util.js';
import {
  button, setShown, setEnabled, jpegUrl, playSound, setArtBackground,
} from './ui-bits.js';
import { attachFlick } from '../flick.js';
import { cameraOwners } from './camera-owner.js';

// opts: { mode: 'selfie'|'topdrawer'|'quickdraw'|'mimic', facing, effect, placeholderName, caption }
export function createCameraView(ctx, opts) {
  const { h, media, game, i18n, log } = ctx;
  const mode = opts.mode;
  let destroyed = false;
  let stillUrl = null;
  let busy = false;
  let renderer = null;
  const offs = [];
  const owner = cameraOwners.mount();

  try {
    renderer = media.createRenderer({ width: 480, height: 640 });
  } catch (e) {
    if (log) log.warn('camera renderer failed', String(e && e.message));
  }

  const flow = createPhotoFlow({
    mode,
    actions: game.actions,
    capture: () => (renderer ? renderer.capture() : Promise.reject(new Error('no renderer'))),
    onError: (e) => { if (log) log.warn('photo capture failed', String(e && e.message)); },
  });

  // viewport: live canvas, captured still, no-camera notice
  const live = h('div', { class: 'play-cam-live' });
  if (renderer && renderer.canvas) {
    renderer.canvas.classList.add('play-cam-canvas');
    live.appendChild(renderer.canvas);
  }
  const still = h('img', { class: 'play-cam-still', attrs: { alt: '', draggable: 'false' } });
  still.hidden = true;
  const flash = h('div', { class: 'play-cam-flash' });
  const noCamText = h('p', { class: 'play-cam-nocam-text', text: tr(i18n, 'port.noCamera', 'No camera available') });
  const noCamBtn = button(ctx, {
    testid: 'no-camera',
    cls: 'play-cam-nocam-btn',
    icon: 'camera',
    label: tr(i18n, 'port.noCameraUse', 'Continue without a photo'),
    onClick: () => usePlaceholder(),
  });
  const noCam = h('div', { class: 'play-cam-nocam' }, noCamText, noCamBtn);
  noCam.hidden = true;
  const viewport = h('div', { class: 'play-cam-viewport' }, live, still, noCam, flash);
  const caption = h('div', { class: 'play-cam-caption', text: opts.caption || '' });
  caption.hidden = !opts.caption;
  const frame = h('div', { class: `play-cam-frame play-polaroid play-cam-${mode}` }, viewport, caption);
  setArtBackground(frame, ctx.assets, 'ui.polaroid');

  // controls
  const effectsBtn = button(ctx, {
    testid: 'effects', cls: 'play-cam-side', icon: 'fx', sprite: 'icon_fx', title: 'Effects', onClick: () => togglePanel(),
  });
  const shutter = button(ctx, {
    testid: 'shutter', cls: 'play-cam-shutter', icon: 'camera', sprite: 'icon_camera', title: 'Take photo', onClick: () => doShutter(),
  });
  const flipBtn = button(ctx, {
    testid: 'flip', cls: 'play-cam-side', icon: 'flip', sprite: 'icon_flip', title: 'Switch camera', onClick: () => doFlip(),
  });
  const retake = button(ctx, {
    testid: 'retake', cls: 'play-cam-retake', icon: 'retake', sprite: 'icon_retake',
    label: tr(i18n, 'RetakePhotoButtonLabel', 'RETAKE'), onClick: () => doRetake(),
  });
  const use = button(ctx, {
    testid: 'use', cls: 'play-cam-use play-primary', icon: 'check', sprite: 'icon_confirm_tick',
    label: tr(i18n, 'port.confirm', 'OK'), onClick: () => doUse(),
  });
  flipBtn.hidden = true;
  const liveBar = h('div', { class: 'play-cam-bar' }, effectsBtn, shutter, flipBtn);
  const shotBar = h('div', { class: 'play-cam-bar play-cam-shotbar' }, retake, use);

  const panel = createEffectsPanel(ctx, {
    effects: media.EFFECTS,
    webgl: !!(renderer && renderer.webgl),
    current: 0,
    onPick: (id) => { setEffect(id); panel.hide(); },
    onClose: () => panel.hide(),
  });

  const el = h('div', { class: 'play-cam' }, frame, liveBar, shotBar, panel.el);

  offs.push(attachFlick(frame, {
    onFlick: (dir) => { if (dir === 'up' && flow.state === 'captured') doUse(); },
  }));
  offs.push(flow.on(() => render()));

  function setEffect(id) {
    if (!renderer) return;
    try { renderer.setEffect(id); } catch (e) { if (log) log.warn('setEffect failed', String(e && e.message)); }
    panel.setCurrent(id);
  }

  function setStill(jpeg) {
    if (stillUrl) { URL.revokeObjectURL(stillUrl); stillUrl = null; }
    if (jpeg) {
      stillUrl = jpegUrl(jpeg);
      still.src = stillUrl;
    } else {
      still.removeAttribute('src');
    }
  }

  function render() {
    if (destroyed) return;
    const c = flow.controls();
    const s = flow.state;
    const showStill = (s === 'captured' || s === 'used') && !!flow.still;
    setShown(still, showStill);
    setShown(live, !showStill && s !== 'nocamera');
    setShown(noCam, s === 'nocamera');
    setShown(liveBar, s === 'live' || s === 'capturing');
    setShown(shotBar, s === 'captured' || s === 'used');
    setEnabled(shutter, c.shutter && !busy);
    setEnabled(effectsBtn, c.effects && !busy);
    setEnabled(flipBtn, c.flip && !busy);
    setEnabled(retake, c.retake);
    setEnabled(use, c.use);
    el.dataset.state = s;
    if (!c.effects) panel.hide();
  }

  async function startCamera(facing) {
    if (!renderer) { flow.noCamera(); return; }
    try {
      await media.camera.start(facing || 'user');
      if (destroyed) { owner.settled(media.camera); return; }
      renderer.attach(media.camera);
      setEffect(Number.isInteger(opts.effect) ? opts.effect : 0);
      renderer.start();
      setShown(flipBtn, !!media.camera.canFlip);
    } catch (e) {
      if (destroyed) { owner.settled(media.camera); return; }
      if (log) log.warn('camera start failed', String(e && (e.name || e.message)));
      flow.noCamera();
    }
  }

  async function doShutter() {
    if (flow.state !== 'live' || busy) return;
    playSound(ctx, 'camera');
    flash.classList.remove('is-on');
    void flash.offsetWidth; // restart the CSS animation
    flash.classList.add('is-on');
    const ok = await flow.shutter();
    if (destroyed) return;
    if (ok) {
      setStill(flow.still.jpeg);
      if (renderer) renderer.stop();
    }
    render();
  }

  function doRetake() {
    if (!flow.retake()) return;
    setStill(null);
    if (renderer && flow.state === 'live') renderer.start();
    render();
  }

  function doUse() {
    if (flow.state !== 'captured') return;
    playSound(ctx, 'confirmSwipe');
    frame.classList.add('is-flicked');
    flow.use();
  }

  async function doFlip() {
    if (busy || !media.camera.canFlip || flow.state !== 'live') return;
    busy = true;
    render();
    try {
      await media.camera.flip();
      if (!destroyed && renderer) renderer.attach(media.camera);
    } catch (e) {
      if (log) log.warn('camera flip failed', String(e && e.message));
    }
    if (destroyed) { owner.settled(media.camera); return; }
    busy = false;
    render();
  }

  async function usePlaceholder() {
    if (busy) return;
    busy = true;
    setEnabled(noCamBtn, false);
    try {
      const jpeg = await media.placeholderPortrait(opts.placeholderName || '');
      if (destroyed) return;
      if (flow.usePlaceholder(jpeg)) setStill(jpeg);
    } catch (e) {
      if (log) log.warn('placeholder portrait failed', String(e && e.message));
    }
    busy = false;
    setEnabled(noCamBtn, true);
    render();
  }

  function togglePanel() {
    if (panel.open) panel.hide();
    else if (flow.controls().effects) panel.show();
  }

  render();
  startCamera(opts.facing);

  return {
    el,
    flow,
    setCaption(text) {
      caption.textContent = text || '';
      caption.hidden = !text;
    },
    // draft provider body ('photo'): the still, or a capture of the live view
    draft: (kind) => flow.draft(kind),
    destroy() {
      if (destroyed) return;
      destroyed = true;
      for (const off of offs) { try { off(); } catch { /* ignore */ } }
      panel.destroy();
      flow.destroy();
      if (renderer) {
        try { renderer.stop(); renderer.destroy(); } catch { /* ignore */ }
      }
      owner.unmount(media.camera);
      setStill(null);
    },
  };
}
