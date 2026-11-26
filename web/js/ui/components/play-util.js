// Small helpers shared by the play screens. No DOM, imports in Node.

export const EFFECT_NAMES = [
  'None', 'Mirror', 'Warp', 'Shrink', 'Stretch', 'Kaleidoscope',
  'Bulge', 'Twist', 'V2Mirror', 'Square', 'Grid', 'Hourglass',
];

// Game-supplied strings are shown as text only (textContent, CSS pre-line).
export function displayText(s) {
  if (s == null) return '';
  return String(s);
}

// Translate key with i18n when it knows the key, else our own fallback wording.
export function tr(i18n, key, fallback, ...args) {
  if (!i18n || typeof i18n.t !== 'function') return fallback;
  try {
    if (typeof i18n.has === 'function') return i18n.has(key) ? i18n.t(key, ...args) : fallback;
    const s = i18n.t(key, ...args);
    return s && s !== key ? s : fallback;
  } catch {
    return fallback;
  }
}

export function cssUrl(u) {
  if (!u) return 'none';
  const esc = String(u).replace(/[\\"]/g, (c) => `\\${c}`).replace(/\n/g, '\\a ');
  return `url("${esc}")`;
}

// Art file URL for a manifest key such as 'ui.textCard'; null in neutral mode.
export function artUrl(assets, key) {
  if (!assets || assets.mode !== 'art') return null;
  try {
    const short = key.includes('.') ? key.slice(key.lastIndexOf('.') + 1) : key;
    if (typeof assets.url === 'function') {
      const u = assets.url(key) || (short !== key ? assets.url(short) : null);
      if (u) return u;
    }
    const m = assets.manifest;
    if (m) {
      let v = m;
      for (const part of key.split('.')) v = v == null ? v : v[part];
      if (typeof v === 'string' && v) return v;
    }
  } catch {
    return null;
  }
  return null;
}

export function effectEntries(effects, { webgl = true } = {}) {
  const src = Array.isArray(effects) && effects.length
    ? effects
    : EFFECT_NAMES.map((name, id) => ({ id, name }));
  const out = [];
  for (const e of src) {
    if (!e || !Number.isInteger(e.id) || e.id < 0 || e.id > 11) continue;
    if (!webgl && e.id !== 0) continue;
    const name = e.name || EFFECT_NAMES[e.id];
    out.push({ id: e.id, name, key: e.key || `CameraEffectName_${name}`, testid: `effect-${e.id}` });
  }
  out.sort((a, b) => a.id - b.id);
  return out;
}
