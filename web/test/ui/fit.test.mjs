import test from 'node:test';
import assert from 'node:assert/strict';
import { fitScale, fitStyle, isLandscapeDevice, typing } from '../../js/ui/fit.js';
import { EN } from '../../js/i18n/en.js';
import { readFileSync } from 'node:fs';

test('content that fits is not scaled', () => {
  assert.equal(fitScale(667, 600), 1);
  assert.equal(fitScale(667, 667.5), 1);
});

test('taller content is scaled to the available height', () => {
  assert.equal(fitScale(600, 800), 0.75);
});

test('scale never drops below the floor', () => {
  assert.equal(fitScale(100, 1000), 0.5);
});

test('bad measurements leave the screen alone', () => {
  assert.equal(fitScale(0, 500), 1);
  assert.equal(fitScale(500, 0), 1);
  assert.equal(fitScale(NaN, 500), 1);
});

test('fit style keeps the visual height equal to the available height', () => {
  assert.deepEqual(fitStyle(1), { transform: '', height: '', flex: '' });
  const s = fitStyle(0.75, 800);
  assert.equal(s.transform, 'scale(0.75)');
  assert.equal(s.height, '800px');
  assert.equal(s.flex, 'none');
});

test('rotate hint has an English text and a landscape rule', () => {
  assert.ok(EN['port.rotate']);
  const css = readFileSync(new URL('../../css/screens.css', import.meta.url), 'utf8');
  assert.match(css, /body\.landscape-device \.rotate-hint/);
});

test('landscape comes from the device orientation, not the viewport shape', () => {
  // keyboard open in portrait: viewport is wide, device is upright
  assert.equal(isLandscapeDevice({ orientationType: 'portrait-primary', coarse: true, screenW: 390, screenH: 844 }), false);
  assert.equal(isLandscapeDevice({ orientationType: 'landscape-primary', coarse: true, screenW: 844, screenH: 390 }), true);
  assert.equal(isLandscapeDevice({ windowOrientation: 90, coarse: true, screenW: 390, screenH: 844 }), true);
  assert.equal(isLandscapeDevice({ windowOrientation: 0, coarse: true, screenW: 390, screenH: 844 }), false);
});

test('desktops and tablets never get the rotate hint', () => {
  assert.equal(isLandscapeDevice({ orientationType: 'landscape-primary', coarse: false, screenW: 1920, screenH: 1080 }), false);
  assert.equal(isLandscapeDevice({ orientationType: 'landscape-primary', coarse: true, screenW: 1366, screenH: 1024 }), false);
});

test('no refit while typing', () => {
  assert.equal(typing({ activeElement: { tagName: 'INPUT' } }), true);
  assert.equal(typing({ activeElement: { tagName: 'TEXTAREA' } }), true);
  assert.equal(typing({ activeElement: { tagName: 'BODY' } }), false);
  assert.equal(typing({}), false);
});
