import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  displayText, tr, cssUrl, artUrl, effectEntries, EFFECT_NAMES,
} from '../../js/ui/components/play-util.js';

test('displayText renders game strings as plain text', () => {
  assert.equal(displayText('WHO IS\nTHE BEST?'), 'WHO IS\nTHE BEST?');
  assert.equal(displayText(null), '');
  assert.equal(displayText(undefined), '');
  assert.equal(displayText(7), '7');
  assert.equal(displayText('<b>x</b>'), '<b>x</b>', 'markup is not interpreted, only shown');
});

test('tr uses i18n when it has the key, else the fallback', () => {
  const i18n = { has: (k) => k === 'A', t: (k, ...a) => `T:${k}:${a.join(',')}` };
  assert.equal(tr(i18n, 'A', 'fb', 1), 'T:A:1');
  assert.equal(tr(i18n, 'B', 'fb'), 'fb');
  assert.equal(tr(null, 'B', 'fb'), 'fb');
  const noHas = { t: (k) => (k === 'A' ? 'yes' : k) };
  assert.equal(tr(noHas, 'A', 'fb'), 'yes');
  assert.equal(tr(noHas, 'B', 'fb'), 'fb', 't returning the key means missing');
  const throws = { has: () => { throw new Error('x'); }, t: () => 'z' };
  assert.equal(tr(throws, 'A', 'fb'), 'fb');
});

test('cssUrl quotes and escapes', () => {
  assert.equal(cssUrl('art/ui/a.png'), 'url("art/ui/a.png")');
  assert.equal(cssUrl('a"b\\c\nd'), 'url("a\\"b\\\\c\\a d")');
  assert.equal(cssUrl(''), 'none');
  assert.equal(cssUrl(null), 'none');
});

test('artUrl returns null in neutral mode or when unknown', () => {
  assert.equal(artUrl(null, 'ui.textCard'), null);
  assert.equal(artUrl({ mode: 'neutral', url: () => 'x' }, 'ui.textCard'), null);
  const assets = { mode: 'art', url: (k) => (k === 'ui.textCard' ? 'art/ui/TextEntryCard.png' : null) };
  assert.equal(artUrl(assets, 'ui.textCard'), 'art/ui/TextEntryCard.png');
  const short = { mode: 'art', url: (k) => (k === 'textCard' ? 'art/ui/t.png' : null) };
  assert.equal(artUrl(short, 'ui.textCard'), 'art/ui/t.png');
  const viaManifest = { mode: 'art', url: () => null, manifest: { ui: { textCard: 'art/ui/m.png' } } };
  assert.equal(artUrl(viaManifest, 'ui.textCard'), 'art/ui/m.png');
  const broken = { mode: 'art', url: () => { throw new Error('x'); } };
  assert.equal(artUrl(broken, 'ui.textCard'), null);
});

test('effect names cover ids 0..11', () => {
  assert.equal(EFFECT_NAMES.length, 12);
  assert.equal(EFFECT_NAMES[0], 'None');
  assert.equal(EFFECT_NAMES[8], 'V2Mirror');
  assert.equal(EFFECT_NAMES[11], 'Hourglass');
});

test('effectEntries lists effect-0..11 with CameraEffectName keys', () => {
  const list = effectEntries(null, { webgl: true });
  assert.equal(list.length, 12);
  assert.deepEqual(list[0], { id: 0, name: 'None', key: 'CameraEffectName_None', testid: 'effect-0' });
  assert.equal(list[11].testid, 'effect-11');
  assert.equal(list[5].key, 'CameraEffectName_Kaleidoscope');
});

test('effectEntries uses media.EFFECTS, sorts and drops bad ids', () => {
  const effects = [
    { id: 2, name: 'Warp', key: 'CameraEffectName_Warp' },
    { id: 0, name: 'None', key: 'CameraEffectName_None' },
    { id: 12, name: 'Bogus' },
    { id: 1, name: 'Mirror' },
  ];
  const list = effectEntries(effects, { webgl: true });
  assert.deepEqual(list.map((e) => e.id), [0, 1, 2]);
  assert.equal(list[1].key, 'CameraEffectName_Mirror');
});

test('effectEntries without WebGL offers only None', () => {
  assert.deepEqual(effectEntries(null, { webgl: false }).map((e) => e.id), [0]);
});
