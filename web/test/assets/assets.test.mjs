import test from 'node:test';
import assert from 'node:assert/strict';
import {
  parseManifest, spriteBackground, nineSlice, normalizeBorder, logoCode, loadAssets, cssUrl, isSafePath,
} from '../../js/assets/assets.js';

const MANIFEST = {
  schema: 1,
  atlas: {
    image: 'art/gui/atlas.png', width: 2048, height: 2048,
    sprites: {
      icon_camera: { x: 338, y: 922, w: 348, h: 282, border: [0, 0, 0, 0] },
      popup: { x: 0, y: 8, w: 152, h: 98, border: [0, 0, 70, 5] },
      full: { x: 0, y: 0, w: 2048, h: 2048 },
    },
  },
  ui: { polaroid: 'art/ui/blankPolaroid.png', joker: 'art/ui/Joker_01_Albedo.png', newspaper: ['art/ui/a.png', 'art/ui/b.png'] },
  effects: { Warp: 'art/fx/Warp.png', Evil: 'https://evil.invalid/x.png' },
  cards: { CARD_1: 'art/cards/card_artist.png', CARD_2: '../escape.png' },
  scenarios: {
    '-1': { background: 'art/scenarios/Home_BG_Tiled.png', card: 'art/scenarios/Wild.png', tiled: true },
    '3': { background: 'art/scenarios/ScenarioBackground_3_Train.png', tiled: false },
  },
  logos: { EN: 'art/logos/mainLogo_EN.png', PT: 'art/logos/mainLogo_PT.png' },
  fonts: { heading: 'art/fonts/H.otf', body: 'art/fonts/B.ttf', text: 'art/fonts/T.ttf', cyrillic: 'art/fonts/C.ttf' },
  sounds: { GENERIC_BUTTON_CLICKED: 'art/sounds/IWYK_Button_Generic.ogg' },
};

test('parseManifest accepts schema 1 and drops unsafe paths', () => {
  const m = parseManifest(MANIFEST);
  assert.ok(m);
  assert.equal(m.atlas.sprites.icon_camera.w, 348);
  assert.equal(m.effects.Warp, 'art/fx/Warp.png');
  assert.equal(m.effects.Evil, undefined);
  assert.equal(m.cards.CARD_1, 'art/cards/card_artist.png');
  assert.equal(m.cards.CARD_2, undefined);
});

test('parseManifest rejects garbage', () => {
  assert.equal(parseManifest(null), null);
  assert.equal(parseManifest('x'), null);
  assert.equal(parseManifest({ schema: 2 }), null);
  assert.equal(parseManifest([]), null);
});

test('parseManifest tolerates missing sections', () => {
  const m = parseManifest({ schema: 1 });
  assert.ok(m);
  assert.equal(m.atlas, null);
  assert.deepEqual(m.cards, {});
});

test('parseManifest drops sprites with bad rects', () => {
  const m = parseManifest({ schema: 1, atlas: { image: 'art/gui/atlas.png', width: 100, height: 100,
    sprites: { ok: { x: 0, y: 0, w: 10, h: 10 }, neg: { x: -1, y: 0, w: 10, h: 10 }, big: { x: 95, y: 0, w: 10, h: 10 }, nan: { x: 'a' } } } });
  assert.deepEqual(Object.keys(m.atlas.sprites), ['ok']);
});

test('isSafePath', () => {
  assert.equal(isSafePath('art/ui/a.png'), true);
  assert.equal(isSafePath('/art/a.png'), false);
  assert.equal(isSafePath('../a.png'), false);
  assert.equal(isSafePath('art/../../a.png'), false);
  assert.equal(isSafePath('http://x/a.png'), false);
  assert.equal(isSafePath('data:image/png;base64,AA'), false);
  assert.equal(isSafePath('art/a").png'), false);
  assert.equal(isSafePath(''), false);
  assert.equal(isSafePath(3), false);
});

test('border normalisation: array is [left, right, top, bottom] as in the prefab', () => {
  assert.deepEqual(normalizeBorder([1, 2, 3, 4]), { left: 1, right: 2, top: 3, bottom: 4 });
  assert.deepEqual(normalizeBorder({ left: 1, right: 2, top: 3, bottom: 4 }), { left: 1, right: 2, top: 3, bottom: 4 });
  assert.deepEqual(normalizeBorder(undefined), { left: 0, right: 0, top: 0, bottom: 0 });
  assert.deepEqual(normalizeBorder([1, 'x', -3]), { left: 1, right: 0, top: 0, bottom: 0 });
});

test('sprite background math in percentages', () => {
  const s = { url: 'art/gui/atlas.png', x: 338, y: 922, w: 348, h: 282, sheetW: 2048, sheetH: 2048 };
  const bg = spriteBackground(s);
  assert.equal(bg.backgroundImage, 'url("art/gui/atlas.png")');
  assert.equal(bg.backgroundRepeat, 'no-repeat');
  const [sw, sh] = bg.backgroundSize.split(' ').map(parseFloat);
  assert.ok(Math.abs(sw - (2048 / 348) * 100) < 1e-3);
  assert.ok(Math.abs(sh - (2048 / 282) * 100) < 1e-3);
  const [px, py] = bg.backgroundPosition.split(' ').map(parseFloat);
  assert.ok(Math.abs(px - (338 / (2048 - 348)) * 100) < 1e-3);
  assert.ok(Math.abs(py - (922 / (2048 - 282)) * 100) < 1e-3);
});

test('sprite background for a sprite as large as the sheet', () => {
  const bg = spriteBackground({ url: 'a.png', x: 0, y: 0, w: 64, h: 64, sheetW: 64, sheetH: 64 });
  assert.equal(bg.backgroundSize, '100% 100%');
  assert.equal(bg.backgroundPosition, '0% 0%');
});

test('nine slice css', () => {
  const s = { url: 'a.png', x: 0, y: 8, w: 152, h: 98, sheetW: 2048, sheetH: 2048, border: { left: 10, right: 12, top: 70, bottom: 5 } };
  const n = nineSlice(s, 0.5);
  assert.equal(n.borderImageSlice, '70 12 5 10 fill');
  assert.equal(n.borderWidth, '35px 6px 2.5px 5px');
  assert.equal(n.borderStyle, 'solid');
  assert.equal(nineSlice({ ...s, border: { left: 0, right: 0, top: 0, bottom: 0 } }), null);
  assert.equal(nineSlice(null), null);
});

test('cssUrl escapes quotes and backslashes', () => {
  assert.equal(cssUrl('a b.png'), 'url("a%20b.png")');
  assert.equal(cssUrl('blob:x"y'), 'url("blob:x\\"y")');
});

test('logo codes', () => {
  assert.equal(logoCode('en'), 'EN');
  assert.equal(logoCode('en-GB'), 'EN');
  assert.equal(logoCode('es-MX'), 'ES');
  assert.equal(logoCode('fr-CA'), 'FR');
  assert.equal(logoCode('pt-BR'), 'PT');
  assert.equal(logoCode('ru'), 'RU');
  assert.equal(logoCode('it'), 'IT');
  assert.equal(logoCode('pl'), 'PL');
  assert.equal(logoCode('de'), 'EN');
  assert.equal(logoCode('GBR'), 'EN');
  assert.equal(logoCode(undefined), 'EN');
});

function fetchOf(files) {
  return async (url) => {
    if (!(url in files)) return { ok: false, status: 404, json: async () => { throw new Error(); } };
    return { ok: true, status: 200, json: async () => files[url] };
  };
}

test('neutral mode without a manifest', async () => {
  const a = await loadAssets({ fetch: fetchOf({}) });
  assert.equal(a.mode, 'neutral');
  assert.equal(a.sprite('icon_camera'), null);
  assert.equal(a.card('CARD_1'), null);
  assert.equal(a.scenario(-1), null);
  assert.equal(a.effectMap('Warp'), null);
  assert.equal(a.logo('en'), null);
  assert.equal(a.sound('GENERIC_BUTTON_CLICKED'), null);
  assert.equal(a.url('ui.polaroid'), null);
  assert.equal(a.applySprite({ style: {} }, 'icon_camera'), false);
  assert.equal(a.fonts.heading, null);
  await a.fonts.load();
});

test('neutral mode when fetch throws or the manifest is broken', async () => {
  const a = await loadAssets({ fetch: async () => { throw new Error('offline'); } });
  assert.equal(a.mode, 'neutral');
  const b = await loadAssets({ fetch: fetchOf({ 'art/manifest.json': { schema: 9 } }) });
  assert.equal(b.mode, 'neutral');
});

test('art mode lookups', async () => {
  const a = await loadAssets({ fetch: fetchOf({ 'art/manifest.json': MANIFEST }) });
  assert.equal(a.mode, 'art');
  assert.deepEqual(a.sprite('icon_camera'), {
    url: 'art/gui/atlas.png', x: 338, y: 922, w: 348, h: 282, sheetW: 2048, sheetH: 2048,
    border: { left: 0, right: 0, top: 0, bottom: 0 },
  });
  assert.deepEqual(a.sprite('popup').border, { left: 0, right: 0, top: 70, bottom: 5 });
  assert.equal(a.sprite('nope'), null);
  assert.equal(a.card('CARD_1'), 'art/cards/card_artist.png');
  assert.equal(a.card('CARD_9'), null);
  assert.deepEqual(a.scenario(-1), { background: 'art/scenarios/Home_BG_Tiled.png', card: 'art/scenarios/Wild.png', tiled: true });
  assert.deepEqual(a.scenario('3'), { background: 'art/scenarios/ScenarioBackground_3_Train.png', card: null, tiled: false });
  assert.equal(a.scenario(7), null);
  assert.equal(a.effectMap('Warp'), 'art/fx/Warp.png');
  assert.equal(a.logo('pt-BR'), 'art/logos/mainLogo_PT.png');
  assert.equal(a.logo('de'), 'art/logos/mainLogo_EN.png');
  assert.equal(a.logo('ru'), 'art/logos/mainLogo_EN.png');
  assert.equal(a.sound('GENERIC_BUTTON_CLICKED'), 'art/sounds/IWYK_Button_Generic.ogg');
  assert.equal(a.sound('NOPE'), null);
  assert.equal(a.url('ui.polaroid'), 'art/ui/blankPolaroid.png');
  assert.equal(a.url('ui.newspaper.1'), 'art/ui/b.png');
  assert.equal(a.url('ui'), null);
  assert.equal(a.url('cards.CARD_1'), 'art/cards/card_artist.png');
  assert.equal(a.fonts.heading, 'art/fonts/H.otf');
});

test('base prefix applies to the manifest fetch and to every url', async () => {
  const a = await loadAssets({ fetch: fetchOf({ 'p/art/manifest.json': MANIFEST }), base: 'p/' });
  assert.equal(a.mode, 'art');
  assert.equal(a.card('CARD_1'), 'p/art/cards/card_artist.png');
  assert.equal(a.sprite('popup').url, 'p/art/gui/atlas.png');
});

test('applySprite sets background styles', async () => {
  const a = await loadAssets({ fetch: fetchOf({ 'art/manifest.json': MANIFEST }) });
  const el = { style: {}, classList: { add() {} } };
  assert.equal(a.applySprite(el, 'icon_camera'), true);
  assert.equal(el.style.backgroundImage, 'url("art/gui/atlas.png")');
  assert.ok(el.style.backgroundSize.endsWith('%'));
});

function fakeBody() {
  const props = {};
  const classes = new Set();
  return {
    props,
    classes,
    style: { setProperty(k, v) { props[k] = v; } },
    classList: { toggle(c, on) { if (on) classes.add(c); else classes.delete(c); } },
  };
}

test('applyTheme writes absolute art urls into CSS variables', async () => {
  const MAN = { ...MANIFEST, ui: { ...MANIFEST.ui, tableTop: 'art/ui/TableTop2_Albedo.png' } };
  const a = await loadAssets({ fetch: fetchOf({ 'art/manifest.json': MAN }) });
  const prev = globalThis.document;
  globalThis.document = { baseURI: 'http://127.0.0.1:8088/' };
  try {
    const b = fakeBody();
    a.applyTheme(b);
    assert.ok(b.classes.has('art'));
    assert.equal(b.props['--art-table'], 'url("http://127.0.0.1:8088/art/ui/TableTop2_Albedo.png")');
    assert.equal(b.props['--art-polaroid'], 'url("http://127.0.0.1:8088/art/ui/blankPolaroid.png")');
  } finally {
    if (prev === undefined) delete globalThis.document; else globalThis.document = prev;
  }
});

test('applyTheme keeps relative urls without a document', async () => {
  const MAN = { ...MANIFEST, ui: { ...MANIFEST.ui, tableTop: 'art/ui/TableTop2_Albedo.png' } };
  const a = await loadAssets({ fetch: fetchOf({ 'art/manifest.json': MAN }) });
  const b = fakeBody();
  a.applyTheme(b);
  assert.equal(b.props['--art-table'], 'url("art/ui/TableTop2_Albedo.png")');
});

test('applyTheme does not double-encode spaces in absolute urls', async () => {
  const MAN = { ...MANIFEST, ui: { tableTop: 'art/ui/Table Top.png' } };
  const a = await loadAssets({ fetch: fetchOf({ 'art/manifest.json': MAN }) });
  const b = fakeBody();
  b.ownerDocument = { baseURI: 'https://example.invalid/iwyk/index.html' };
  a.applyTheme(b);
  assert.equal(b.props['--art-table'], 'url("https://example.invalid/iwyk/art/ui/Table%20Top.png")');
});
