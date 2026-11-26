// Photo screen (QUICK_DRAW_PHOTO): Top Drawer (photoTaken at the shutter,
// submitPhoto on use) and QuickDraw (submitPhoto on use) variants.

import { createCameraView } from '../components/camera-view.js';
import { displayText } from '../components/play-util.js';

export function mount(ctx, view) {
  const { h, game } = ctx;
  const data = view.data || {};
  const mode = view.variant === 'quickdraw' ? 'quickdraw' : 'topdrawer';
  const title = h('h1', { class: 'play-title play-command', text: displayText(data.commandText) });
  const cam = createCameraView(ctx, {
    mode,
    facing: data.facing || (mode === 'quickdraw' ? 'user' : 'environment'),
    placeholderName: (game.state && game.state.profile && game.state.profile.name) || '',
  });
  const root = h('section', { class: `play-screen play-photo play-photo-${mode}` }, title, cam.el);
  ctx.el.appendChild(root);
  return {
    update(next) {
      title.textContent = displayText(next.data && next.data.commandText);
    },
    unmount() {
      cam.destroy();
      root.remove();
    },
  };
}

export const demos = [
  { variant: 'topdrawer', data: { commandText: 'TAKE A PHOTO OF ANNA\nLOOKING SURPRISED', facing: 'environment' } },
  { variant: 'quickdraw', data: { commandText: 'PULL YOUR BEST\nSUPERHERO POSE', facing: 'user' } },
];
