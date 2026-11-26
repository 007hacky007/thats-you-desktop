import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  numberChoices, gridLayout, choiceKind, jokerVisible, createAnswerModel,
} from '../../js/ui/components/choice-logic.js';

function fakeActions() {
  const calls = [];
  const rec = (name) => (...args) => { calls.push([name, ...args]); };
  return {
    calls,
    selectAnswer: rec('selectAnswer'),
    confirmAnswer: rec('confirmAnswer'),
    setJoker: rec('setJoker'),
  };
}

test('numberChoices numbers 1-based in view.data.choices order', () => {
  const items = numberChoices([
    { position: 1, guid: 'a', text: '' },
    { position: 2, guid: 'b' },
    { position: 3, text: 'Card' },
  ]);
  assert.deepEqual(items.map((c) => c.pos), [1, 2, 3]);
  assert.deepEqual(items.map((c) => c.testid), ['answer-1', 'answer-2', 'answer-3']);
  assert.equal(items[0].guid, 'a');
  assert.equal(items[2].text, 'Card');
  assert.equal(items[1].text, '');
});

test('numberChoices falls back to the index when positions are missing or broken', () => {
  assert.deepEqual(numberChoices([{ guid: 'x' }, { guid: 'y' }]).map((c) => c.pos), [1, 2]);
  assert.deepEqual(numberChoices([{ position: 0 }, { position: -3 }, { position: 'z' }]).map((c) => c.pos), [1, 2, 3]);
  // duplicate positions: order wins
  assert.deepEqual(numberChoices([{ position: 1 }, { position: 1 }]).map((c) => c.pos), [1, 2]);
  assert.deepEqual(numberChoices(null), []);
  assert.deepEqual(numberChoices(undefined), []);
});

test('numberChoices keeps explicit distinct positions', () => {
  assert.deepEqual(numberChoices([{ position: 2 }, { position: 1 }]).map((c) => c.pos), [2, 1]);
});

test('numberChoices turns non-string text and guid into strings', () => {
  const [c] = numberChoices([{ guid: null, text: 42 }]);
  assert.equal(c.guid, '');
  assert.equal(c.text, '42');
});

test('choiceKind maps variants', () => {
  assert.equal(choiceKind('portraits'), 'image');
  assert.equal(choiceKind('images'), 'image');
  assert.equal(choiceKind('drawings'), 'image');
  assert.equal(choiceKind('texts'), 'text');
  assert.equal(choiceKind(''), 'image');
});

test('gridLayout picks columns per kind and count', () => {
  assert.deepEqual(gridLayout(0, 'image'), { columns: 1, rows: 0 });
  assert.deepEqual(gridLayout(1, 'image'), { columns: 1, rows: 1 });
  assert.deepEqual(gridLayout(2, 'image'), { columns: 2, rows: 1 });
  assert.deepEqual(gridLayout(3, 'image'), { columns: 2, rows: 2 });
  assert.deepEqual(gridLayout(4, 'image'), { columns: 2, rows: 2 });
  assert.deepEqual(gridLayout(6, 'image'), { columns: 2, rows: 3 });
  assert.deepEqual(gridLayout(7, 'image'), { columns: 3, rows: 3 });
  assert.deepEqual(gridLayout(9, 'image'), { columns: 3, rows: 3 });
  assert.deepEqual(gridLayout(5, 'text'), { columns: 1, rows: 5 });
});

test('jokerVisible needs jokersEnabled and jokers > 0', () => {
  assert.equal(jokerVisible({ jokersEnabled: true, jokers: 1 }), true);
  assert.equal(jokerVisible({ jokersEnabled: true, jokers: 0 }), false);
  assert.equal(jokerVisible({ jokersEnabled: false, jokers: 3 }), false);
  assert.equal(jokerVisible({ jokersEnabled: 1, jokers: 2 }), true);
  assert.equal(jokerVisible({}), false);
  assert.equal(jokerVisible(null), false);
});

test('answer model selects, confirms once and toggles the joker', () => {
  const actions = fakeActions();
  const m = createAnswerModel({ actions, data: { choices: [{ position: 1 }, { position: 2 }], jokersEnabled: true, jokers: 2, jokerOn: false } });
  assert.equal(m.selected, null);
  assert.equal(m.canConfirm(), false);
  assert.equal(m.confirm(), false);
  m.select(2);
  assert.equal(m.selected, 2);
  assert.equal(m.canConfirm(), true);
  m.toggleJoker();
  assert.equal(m.jokerOn, true);
  assert.equal(m.confirm(), true);
  assert.equal(m.confirmed, true);
  assert.equal(m.confirm(), false);
  m.select(1);
  assert.equal(m.selected, 2, 'no selection change after confirm');
  assert.deepEqual(actions.calls, [['selectAnswer', 2], ['setJoker', true], ['confirmAnswer', 2]]);
});

test('answer model ignores unknown positions and confirms a flicked card', () => {
  const actions = fakeActions();
  const m = createAnswerModel({ actions, data: { choices: [{ position: 1 }, { position: 2 }] } });
  m.select(5);
  assert.equal(m.selected, null);
  assert.equal(m.confirm(1), true);
  assert.deepEqual(actions.calls, [['selectAnswer', 1], ['confirmAnswer', 1]]);
});

test('answer model does not toggle the joker when it is not offered', () => {
  const actions = fakeActions();
  const m = createAnswerModel({ actions, data: { choices: [{ position: 1 }], jokersEnabled: true, jokers: 0 } });
  m.toggleJoker();
  assert.equal(m.jokerOn, false);
  assert.deepEqual(actions.calls, []);
});

test('answer model update takes selection and joker state from the view', () => {
  const actions = fakeActions();
  const m = createAnswerModel({ actions, data: { choices: [{ position: 1 }, { position: 2 }] } });
  m.select(1);
  m.update({ choices: [{ position: 1 }, { position: 2 }, { position: 3 }], selected: 3, jokerOn: true, jokersEnabled: true, jokers: 1 });
  assert.equal(m.selected, 3);
  assert.equal(m.jokerOn, true);
  assert.equal(m.items.length, 3);
  // view without a selection keeps the local one
  m.update({ choices: [{ position: 1 }, { position: 2 }, { position: 3 }], selected: null });
  assert.equal(m.selected, 3);
  // a selection that no longer exists is dropped
  m.update({ choices: [{ position: 1 }] });
  assert.equal(m.selected, null);
});

test('answer model change listeners fire and unsubscribe', () => {
  const m = createAnswerModel({ actions: fakeActions(), data: { choices: [{ position: 1 }] } });
  let n = 0;
  const off = m.on(() => { n += 1; });
  m.select(1);
  off();
  m.select(1);
  assert.equal(n, 1);
});
