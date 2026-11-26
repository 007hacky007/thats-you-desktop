// Selfie screen (PROFILE_PORTRAIT_PHOTO): front camera with effects; use calls
// game.actions.profilePhoto. Without a camera a placeholder portrait is offered.

import { createCameraView } from '../components/camera-view.js';
import { tr } from '../components/play-util.js';

export function mount(ctx, view) {
  const { h, game, i18n } = ctx;
  const profile = (game.state && game.state.profile) || {};
  const title = h('h1', { class: 'play-title', text: tr(i18n, 'TakePhotoTitle', 'TAKE A SELFIE') });
  const cam = createCameraView(ctx, {
    mode: 'selfie',
    facing: (view.data && view.data.facing) || 'user',
    placeholderName: profile.name || '',
  });
  const root = h('section', { class: 'play-screen play-photo play-selfie' }, title, cam.el);
  ctx.el.appendChild(root);
  return {
    update() {},
    unmount() {
      cam.destroy();
      root.remove();
    },
  };
}

export const demos = [{ variant: '', data: { facing: 'user' } }];
