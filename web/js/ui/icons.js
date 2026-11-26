// Our own simple line icons (24x24, stroke = currentColor). In art mode an icon
// with an atlas counterpart shows that sprite instead (an SVG whose viewBox
// crops the atlas), so callers always get an SVGElement.

const NS = 'http://www.w3.org/2000/svg';

const FILL = { fill: 'currentColor', stroke: 'none' };

export const ICONS = {
  camera: [
    ['rect', { x: 2, y: 7, width: 20, height: 14, rx: 2 }],
    ['path', { d: 'M8 7l2-3h4l2 3' }],
    ['circle', { cx: 12, cy: 14, r: 4 }],
  ],
  flip: [
    ['path', { d: 'M4 12a8 8 0 0 1 14-5.3' }],
    ['path', { d: 'M18 3v4h-4' }],
    ['path', { d: 'M20 12a8 8 0 0 1-14 5.3' }],
    ['path', { d: 'M6 21v-4h4' }],
  ],
  fx: [
    ['path', { d: 'M12 3l1.8 5.2L19 10l-5.2 1.8L12 17l-1.8-5.2L5 10l5.2-1.8z' }],
    ['path', { d: 'M19 16l.7 1.8 1.8.7-1.8.7L19 21l-.7-1.8-1.8-.7 1.8-.7z' }],
  ],
  undo: [
    ['path', { d: 'M9 14L4 9l5-5' }],
    ['path', { d: 'M4 9h10a6 6 0 0 1 0 12h-3' }],
  ],
  eraser: [
    ['path', { d: 'M4 16l9-9 6 6-7 7H8z' }],
    ['path', { d: 'M8.5 11.5l6 6' }],
    ['path', { d: 'M12 20h8' }],
  ],
  brush: [
    ['path', { d: 'M9 15L19 5l1 1-10 10' }],
    ['path', { d: 'M9 15c-2 0-3 1-3 3s-1 2-2 2c3 1 7 0 7-3z' }],
  ],
  spray: [
    ['rect', { x: 7, y: 10, width: 8, height: 11, rx: 1.5 }],
    ['rect', { x: 9, y: 6, width: 4, height: 4 }],
    ['circle', { cx: 18, cy: 4, r: 0.9, ...FILL }],
    ['circle', { cx: 20, cy: 7, r: 0.9, ...FILL }],
    ['circle', { cx: 17, cy: 8, r: 0.9, ...FILL }],
  ],
  tv: [
    ['rect', { x: 2, y: 4, width: 20, height: 14, rx: 2 }],
    ['path', { d: 'M8 21h8M12 18v3' }],
  ],
  leave: [
    ['path', { d: 'M10 4H5v16h5' }],
    ['path', { d: 'M15 8l4 4-4 4' }],
    ['path', { d: 'M19 12H9' }],
  ],
  check: [
    ['path', { d: 'M4 12.5l5 5L20 6.5' }],
  ],
  retake: [
    ['path', { d: 'M3.5 12a8.5 8.5 0 1 0 2.6-6.1' }],
    ['path', { d: 'M3 3v5h5' }],
  ],
  joker: [
    ['rect', { x: 5, y: 2.5, width: 14, height: 19, rx: 2 }],
    ['path', { d: 'M12 7.5l1.3 2.8 3 .3-2.3 2 .7 3L12 14l-2.7 1.6.7-3-2.3-2 3-.3z' }],
  ],
  pause: [
    ['rect', { x: 6, y: 5, width: 4, height: 14, rx: 1, ...FILL }],
    ['rect', { x: 14, y: 5, width: 4, height: 14, rx: 1, ...FILL }],
  ],
  warning: [
    ['path', { d: 'M12 3l10 18H2z' }],
    ['path', { d: 'M12 10v5' }],
    ['circle', { cx: 12, cy: 18, r: 0.8, ...FILL }],
  ],
  connection: [
    ['path', { d: 'M2 9a15 15 0 0 1 20 0' }],
    ['path', { d: 'M5.5 12.5a10 10 0 0 1 13 0' }],
    ['path', { d: 'M9 16a5 5 0 0 1 6 0' }],
    ['circle', { cx: 12, cy: 19.5, r: 1.2, ...FILL }],
  ],
  close: [
    ['path', { d: 'M6 6l12 12M18 6L6 18' }],
  ],
  person: [
    ['circle', { cx: 12, cy: 8, r: 4 }],
    ['path', { d: 'M4 21c1-4.5 4-7 8-7s7 2.5 8 7' }],
  ],
};

// Atlas sprite names (from the app's GUI atlas) for art mode.
export const ICON_SPRITES = {
  camera: 'icon_camera',
  flip: 'icon_flip',
  fx: 'icon_fx',
  undo: 'icon_undo',
  eraser: 'icon_eraser',
  brush: 'icon_pencil',
  spray: 'icon_spray',
  tv: 'icon_tv',
  leave: 'icon_leave',
  check: 'icon_confirm_tick',
  retake: 'icon_retake',
  warning: 'icon_warning',
  connection: 'icon_connection1',
  close: 'icon_dismiss_cross',
};

function svgEl(tag, attrs) {
  const el = globalThis.document.createElementNS(NS, tag);
  for (const [k, v] of Object.entries(attrs || {})) el.setAttribute(k, String(v));
  return el;
}

export function createIcon(name, { assets } = {}) {
  const svg = svgEl('svg', { class: `icon icon-${name}`, 'aria-hidden': 'true', focusable: 'false' });
  const spriteName = assets && assets.mode === 'art' ? ICON_SPRITES[name] : null;
  const sprite = spriteName && assets.sprite ? assets.sprite(spriteName) : null;
  if (sprite) {
    // The outer viewBox is the sprite size; the inner svg crops the atlas and
    // clips it, so neighbouring sprites never show around the icon.
    svg.setAttribute('viewBox', `0 0 ${sprite.w} ${sprite.h}`);
    svg.setAttribute('preserveAspectRatio', 'xMidYMid meet');
    svg.classList.add('icon-art');
    const crop = svgEl('svg', { x: 0, y: 0, width: sprite.w, height: sprite.h, viewBox: `${sprite.x} ${sprite.y} ${sprite.w} ${sprite.h}`, overflow: 'hidden' });
    crop.appendChild(svgEl('image', { href: sprite.url, x: 0, y: 0, width: sprite.sheetW, height: sprite.sheetH }));
    svg.appendChild(crop);
    return svg;
  }
  svg.setAttribute('viewBox', '0 0 24 24');
  svg.setAttribute('fill', 'none');
  svg.setAttribute('stroke', 'currentColor');
  svg.setAttribute('stroke-width', '2');
  svg.setAttribute('stroke-linecap', 'round');
  svg.setAttribute('stroke-linejoin', 'round');
  const shapes = ICONS[name] || [['circle', { cx: 12, cy: 12, r: 8 }]];
  for (const [tag, attrs] of shapes) svg.appendChild(svgEl(tag, attrs));
  return svg;
}

// Bound helper for ctx.icon.
export function iconFactory(assets) {
  return (name) => createIcon(name, { assets });
}
