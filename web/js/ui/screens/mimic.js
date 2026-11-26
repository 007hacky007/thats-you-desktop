// Mimic screen (MIMIC_PHOTO): the reference picture plus the front camera with
// effect None at start. Draft provider 'photo' returns the captured still or a
// capture of the live view (SendMimicThisPhoto).

import { createCameraView } from '../components/camera-view.js';
import { createResourceImage } from '../components/resource-image.js';

export function mount(ctx, view) {
  const { h, game } = ctx;
  const data = view.data || {};
  const ref = createResourceImage(ctx, data.referenceGuid, { cls: 'play-mimic-ref-img' });
  const refCard = h('div', { class: 'play-mimic-ref play-polaroid-mini' }, ref.el);
  const cam = createCameraView(ctx, {
    mode: 'mimic',
    facing: data.facing || 'user',
    effect: 0,
    placeholderName: (game.state && game.state.profile && game.state.profile.name) || '',
  });
  const root = h('section', { class: 'play-screen play-photo play-mimic' }, refCard, cam.el);
  ctx.el.appendChild(root);
  game.setDraftProvider((kind) => (kind === 'photo' ? cam.draft('photo') : Promise.resolve(null)));

  return {
    update(next) {
      const g = next.data && next.data.referenceGuid;
      if ((g || null) !== ref.guid) ref.setGuid(g);
    },
    unmount() {
      game.setDraftProvider(null);
      ref.destroy();
      cam.destroy();
      root.remove();
    },
  };
}

export const demos = [{ variant: '', data: { referenceGuid: 'demo-mimic-reference', facing: 'user' } }];
