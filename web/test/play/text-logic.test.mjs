import { test } from 'node:test';
import assert from 'node:assert/strict';
import { clampCodePoints, codePointLength, createTextEntry } from '../../js/ui/components/text-logic.js';

function fakeActions() {
  const calls = [];
  return {
    calls,
    textChanged: (t) => calls.push(['textChanged', t]),
    submitText: (t) => calls.push(['submitText', t]),
  };
}

test('clampCodePoints counts code points, not UTF-16 units', () => {
  assert.equal(clampCodePoints('abc', 2), 'ab');
  assert.equal(clampCodePoints('abc', 80), 'abc');
  const smile = '\u{1F600}';
  assert.equal(codePointLength(smile.repeat(3)), 3);
  assert.equal(clampCodePoints(smile.repeat(81), 80), smile.repeat(80));
  assert.equal(clampCodePoints('a' + smile + 'b', 2), 'a' + smile);
  assert.equal(clampCodePoints(null, 5), '');
  assert.equal(clampCodePoints(undefined, 5), '');
  assert.equal(clampCodePoints(12345, 3), '123');
});

test('clampCodePoints never leaves a lone high surrogate', () => {
  // a broken string with a lone surrogate counts as one code point and stays intact
  assert.equal(clampCodePoints('\uD83D', 1), '\uD83D');
  assert.equal(clampCodePoints('x\uD83D\uDE00', 1), 'x');
});

test('text entry clamps to 80 and reports changes once per change', () => {
  const actions = fakeActions();
  const e = createTextEntry({ actions, maxLength: 80, initial: 'hi' });
  assert.equal(e.text, 'hi');
  assert.equal(e.remaining, 78);
  assert.equal(e.input('hello'), 'hello');
  assert.equal(e.input('hello'), 'hello');
  const long = 'x'.repeat(100);
  assert.equal(e.input(long), 'x'.repeat(80));
  assert.equal(e.remaining, 0);
  assert.deepEqual(actions.calls, [['textChanged', 'hello'], ['textChanged', 'x'.repeat(80)]]);
});

test('text entry clamps the initial text and defaults maxLength to 80', () => {
  const e = createTextEntry({ actions: fakeActions(), initial: 'y'.repeat(90) });
  assert.equal(e.maxLength, 80);
  assert.equal(e.text.length, 80);
});

test('text entry submit sends the text once', () => {
  const actions = fakeActions();
  const e = createTextEntry({ actions, maxLength: 80 });
  e.input('answer');
  assert.equal(e.submit(), true);
  assert.equal(e.submitted, true);
  assert.equal(e.submit(), false);
  assert.equal(e.input('changed'), 'answer', 'no edits after submit');
  assert.deepEqual(actions.calls, [['textChanged', 'answer'], ['submitText', 'answer']]);
});

test('text entry submits an empty text too', () => {
  const actions = fakeActions();
  const e = createTextEntry({ actions });
  e.submit();
  assert.deepEqual(actions.calls, [['submitText', '']]);
});

test('text entry setText from the view does not echo textChanged', () => {
  const actions = fakeActions();
  const e = createTextEntry({ actions });
  e.setText('from game');
  assert.equal(e.text, 'from game');
  assert.deepEqual(actions.calls, []);
});
