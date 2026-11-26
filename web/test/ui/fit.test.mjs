import test from 'node:test';
import assert from 'node:assert/strict';
import { fitScale, fitStyle } from '../../js/ui/fit.js';
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
  assert.match(css, /orientation:\s*landscape[^{]*\{[^}]*\.rotate-hint/);
});
