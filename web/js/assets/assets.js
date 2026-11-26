// Build-time art (from the person's own APK) described by art/manifest.json.
// Without it every lookup returns null and the page uses its neutral style.

const SAFE_PATH = /^[A-Za-z0-9._~ ()+,-]+(\/[A-Za-z0-9._~ ()+,-]+)*$/;

export function isSafePath(p) {
  if (typeof p !== 'string' || !p || !SAFE_PATH.test(p)) return false;
  return !p.split('/').some((seg) => seg === '..' || seg === '.');
}

function num(v) {
  return typeof v === 'number' && Number.isFinite(v) ? v : NaN;
}

// Border of a 9-sliced sprite. Arrays are [left, right, top, bottom], the
// order of the prefab fields (borderLeft, borderRight, borderTop, borderBottom).
export function normalizeBorder(b) {
  const out = { left: 0, right: 0, top: 0, bottom: 0 };
  const take = (v) => (Number.isFinite(v) && v > 0 ? v : 0);
  if (Array.isArray(b)) {
    out.left = take(b[0]); out.right = take(b[1]); out.top = take(b[2]); out.bottom = take(b[3]);
  } else if (b && typeof b === 'object') {
    out.left = take(b.left); out.right = take(b.right); out.top = take(b.top); out.bottom = take(b.bottom);
  }
  return out;
}

function pathMap(obj) {
  const out = {};
  if (!obj || typeof obj !== 'object' || Array.isArray(obj)) return out;
  for (const [k, v] of Object.entries(obj)) if (isSafePath(v)) out[k] = v;
  return out;
}

export function parseManifest(json) {
  if (!json || typeof json !== 'object' || Array.isArray(json) || json.schema !== 1) return null;
  let atlas = null;
  const a = json.atlas;
  if (a && typeof a === 'object' && isSafePath(a.image) && num(a.width) > 0 && num(a.height) > 0) {
    const sprites = {};
    for (const [name, s] of Object.entries(a.sprites || {})) {
      if (!s || typeof s !== 'object') continue;
      const x = num(s.x), y = num(s.y), w = num(s.w), hh = num(s.h);
      if (!(x >= 0 && y >= 0 && w > 0 && hh > 0 && x + w <= a.width && y + hh <= a.height)) continue;
      sprites[name] = { x, y, w, h: hh, border: normalizeBorder(s.border) };
    }
    atlas = { image: a.image, width: a.width, height: a.height, sprites };
  }
  const ui = {};
  if (json.ui && typeof json.ui === 'object') {
    for (const [k, v] of Object.entries(json.ui)) {
      if (isSafePath(v)) ui[k] = v;
      else if (Array.isArray(v)) ui[k] = v.filter(isSafePath);
    }
  }
  const scenarios = {};
  if (json.scenarios && typeof json.scenarios === 'object') {
    for (const [id, s] of Object.entries(json.scenarios)) {
      if (!s || typeof s !== 'object') continue;
      const background = isSafePath(s.background) ? s.background : null;
      const card = isSafePath(s.card) ? s.card : null;
      if (!background && !card) continue;
      scenarios[String(id)] = { background, card, tiled: s.tiled === true };
    }
  }
  return {
    schema: 1,
    atlas,
    ui,
    effects: pathMap(json.effects),
    cards: pathMap(json.cards),
    scenarios,
    logos: pathMap(json.logos),
    fonts: pathMap(json.fonts),
    sounds: pathMap(json.sounds),
  };
}

export function cssUrl(u) {
  const enc = String(u).startsWith('blob:') ? String(u) : encodeURI(String(u));
  return `url("${enc.replace(/["\\]/g, (c) => `\\${c}`)}")`;
}

function pct(v) {
  const r = Math.round(v * 10000) / 10000;
  return `${r}%`;
}

// CSS background showing one atlas sprite stretched over the whole element,
// independent of the element size.
export function spriteBackground(s) {
  const sizeX = (s.sheetW / s.w) * 100;
  const sizeY = (s.sheetH / s.h) * 100;
  const posX = s.sheetW === s.w ? 0 : (s.x / (s.sheetW - s.w)) * 100;
  const posY = s.sheetH === s.h ? 0 : (s.y / (s.sheetH - s.h)) * 100;
  return {
    backgroundImage: cssUrl(s.url),
    backgroundRepeat: 'no-repeat',
    backgroundSize: `${pct(sizeX)} ${pct(sizeY)}`,
    backgroundPosition: `${pct(posX)} ${pct(posY)}`,
  };
}

// border-image settings for a 9-sliced sprite (the image itself must be the
// cropped sprite, see spriteImageUrl). `scale` = CSS px per sprite pixel.
export function nineSlice(s, scale = 1) {
  if (!s || !s.border) return null;
  const b = s.border;
  if (!(b.top || b.right || b.bottom || b.left)) return null;
  const px = (v) => `${Math.round(v * scale * 1000) / 1000}px`;
  return {
    borderStyle: 'solid',
    borderImageSlice: `${b.top} ${b.right} ${b.bottom} ${b.left} fill`,
    borderWidth: `${px(b.top)} ${px(b.right)} ${px(b.bottom)} ${px(b.left)}`,
  };
}

const LOGO_CODES = { es: 'ES', fr: 'FR', it: 'IT', pl: 'PL', pt: 'PT', ru: 'RU' };

export function logoCode(lang) {
  const prim = String(lang || '').toLowerCase().split('-')[0];
  return LOGO_CODES[prim] || 'EN';
}

const FONT_FAMILIES = {
  heading: 'IwykHeading', body: 'IwykBody', text: 'IwykText', slab: 'IwykSlab',
  cyrillic: 'IwykCyrillic', typewriter: 'IwykTypewriter', symbols: 'IwykSymbols',
};

const FALLBACK_STACK = 'system-ui, -apple-system, "Segoe UI", Roboto, sans-serif';

function createAssets(manifest, base) {
  const m = manifest;
  const full = (p) => (p ? `${base}${p}` : null);
  const spriteImageCache = new Map();

  const assets = {
    mode: m ? 'art' : 'neutral',
    manifest: m,
    url(key) {
      if (!m || typeof key !== 'string') return null;
      let cur = m;
      for (const part of key.split('.')) {
        if (cur === null || typeof cur !== 'object') return null;
        cur = cur[part];
      }
      return typeof cur === 'string' && isSafePath(cur) ? full(cur) : null;
    },
    sprite(name) {
      const s = m && m.atlas && Object.prototype.hasOwnProperty.call(m.atlas.sprites, name) ? m.atlas.sprites[name] : null;
      if (!s) return null;
      return { url: full(m.atlas.image), x: s.x, y: s.y, w: s.w, h: s.h, sheetW: m.atlas.width, sheetH: m.atlas.height, border: { ...s.border } };
    },
    applySprite(el, name) {
      const s = assets.sprite(name);
      if (!s || !el || !el.style) return false;
      Object.assign(el.style, spriteBackground(s));
      if (el.classList) el.classList.add('sprite');
      return true;
    },
    // A cropped copy of one sprite as a blob URL (for border-image 9-slices).
    spriteImageUrl(name) {
      const s = assets.sprite(name);
      if (!s) return Promise.resolve(null);
      if (!spriteImageCache.has(name)) spriteImageCache.set(name, cropSprite(s).catch(() => null));
      return spriteImageCache.get(name);
    },
    async applyNineSlice(el, name, scale = 0.5) {
      const s = assets.sprite(name);
      const css = nineSlice(s, scale);
      if (!css || !el) return false;
      const u = await assets.spriteImageUrl(name);
      if (!u) return false;
      Object.assign(el.style, css);
      el.style.borderImageSource = cssUrl(u);
      if (el.classList) el.classList.add('nine');
      return true;
    },
    card(cardId) {
      return m && Object.prototype.hasOwnProperty.call(m.cards, cardId) ? full(m.cards[cardId]) : null;
    },
    scenario(id) {
      const s = m && m.scenarios[String(id)];
      return s ? { background: full(s.background), card: full(s.card), tiled: s.tiled } : null;
    },
    effectMap(name) {
      return m && Object.prototype.hasOwnProperty.call(m.effects, name) ? full(m.effects[name]) : null;
    },
    logo(langCode) {
      if (!m) return null;
      return full(m.logos[logoCode(langCode)] || m.logos.EN || null);
    },
    sound(event) {
      return m && Object.prototype.hasOwnProperty.call(m.sounds, event) ? full(m.sounds[event]) : null;
    },
    fonts: {
      heading: m ? full(m.fonts.heading) : null,
      body: m ? full(m.fonts.body) : null,
      text: m ? full(m.fonts.text) : null,
      // Load the font files and expose them as CSS variables on `root`.
      async load(root) {
        if (!m) return false;
        const FF = globalThis.FontFace;
        const doc = globalThis.document;
        if (!FF || !doc || !doc.fonts) return false;
        const loaded = new Set();
        await Promise.all(Object.entries(FONT_FAMILIES).map(async ([key, family]) => {
          const u = m.fonts[key];
          if (!u) return;
          try {
            const face = new FF(family, cssUrl(full(u)), { display: 'swap' });
            doc.fonts.add(face);
            await face.load();
            loaded.add(key);
          } catch {
            // a broken font file just keeps the fallback stack
          }
        }));
        const fam = (key) => (loaded.has(key) ? `"${FONT_FAMILIES[key]}", ` : '');
        const cyr = fam('cyrillic');
        const target = root || doc.documentElement;
        target.style.setProperty('--font-heading', `${fam('heading')}${cyr}${FALLBACK_STACK}`);
        target.style.setProperty('--font-body', `${fam('body')}${cyr}${FALLBACK_STACK}`);
        target.style.setProperty('--font-text', `${fam('text')}${cyr}${FALLBACK_STACK}`);
        return loaded.size > 0;
      },
    },
    // Body class and CSS variables for the art theme.
    applyTheme(body) {
      const b = body || globalThis.document?.body;
      if (!b) return;
      b.classList.toggle('art', !!m);
      b.classList.toggle('neutral', !m);
      if (!m) return;
      const set = (name, u) => { if (u) b.style.setProperty(name, cssUrl(u)); };
      set('--art-table', assets.url('ui.tableTop'));
      set('--art-polaroid', assets.url('ui.polaroid'));
      set('--art-polaroid-light', assets.url('ui.polaroidLight'));
      set('--art-photo-frame', assets.url('ui.photoFrame'));
      set('--art-text-card', assets.url('ui.textCard'));
      set('--art-joker', assets.url('ui.joker'));
      set('--art-feed', assets.url('ui.blankFeed'));
    },
  };
  return assets;
}

async function cropSprite(s) {
  const res = await fetch(s.url);
  if (!res.ok) return null;
  const bmp = await createImageBitmap(await res.blob(), s.x, s.y, s.w, s.h);
  const canvas = globalThis.OffscreenCanvas ? new OffscreenCanvas(s.w, s.h) : Object.assign(document.createElement('canvas'), { width: s.w, height: s.h });
  canvas.getContext('2d').drawImage(bmp, 0, 0);
  const blob = canvas.convertToBlob ? await canvas.convertToBlob({ type: 'image/png' })
    : await new Promise((r) => canvas.toBlob(r, 'image/png'));
  return blob ? URL.createObjectURL(blob) : null;
}

export async function loadAssets({ fetch: fetchFn, base = '' } = {}) {
  const f = fetchFn || globalThis.fetch?.bind(globalThis);
  let manifest = null;
  if (f) {
    try {
      const res = await f(`${base}art/manifest.json`, { cache: 'no-cache' });
      if (res && res.ok) manifest = parseManifest(await res.json());
    } catch {
      manifest = null;
    }
  }
  return createAssets(manifest, base);
}

// Neutral assets without any fetch (used when loading itself fails).
export function neutralAssets() {
  return createAssets(null, '');
}
