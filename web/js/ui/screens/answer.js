// Answer screen (CHOOSE_PLAYER_ANSWER): question, choice grid, joker switch,
// confirm by flicking the selected card or with answer-confirm.

import { createAnswerModel } from '../components/choice-logic.js';
import { createChoiceGrid } from '../components/choice-grid.js';
import { displayText, tr } from '../components/play-util.js';
import {
  button, setShown, setEnabled, setPressed, setArtBackground, playSound,
} from '../components/ui-bits.js';

export function mount(ctx, view) {
  const { h, game, i18n } = ctx;
  const model = createAnswerModel({ actions: game.actions, data: view.data });
  let variant = view.variant;

  const question = h('h1', { class: 'play-title play-question', text: displayText(model.data.question) });

  const grid = createChoiceGrid(ctx, {
    variant,
    choices: model.data.choices,
    selected: model.selected,
    onSelect: (pos) => model.select(pos),
    onConfirm: (pos) => confirm(pos),
  });

  // joker switch
  const jokerNum = h('span', { class: 'play-joker-num' });
  const jokerRemain = h('span', { class: 'play-joker-remain' });
  const jokerCount = h('span', { class: 'play-joker-count' }, jokerNum, jokerRemain);
  const jokerLabel = h('span', { class: 'play-joker-label' });
  const jokerArt = h('span', { class: 'play-joker-art' });
  setArtBackground(jokerArt, ctx.assets, 'ui.joker');
  const jokerKnob = h('span', { class: 'play-switch' }, h('span', { class: 'play-switch-knob' }));
  const jokerToggle = h('button', {
    class: 'play-btn play-joker-toggle',
    testid: 'joker-toggle',
    attrs: { type: 'button', role: 'switch', 'aria-checked': 'false' },
    on: { click: () => { if (model.toggleJoker()) playSound(ctx, 'joker'); } },
  }, jokerArt, jokerLabel, jokerKnob);
  const jokerHint = h('p', { class: 'play-joker-hint', text: tr(i18n, 'JokerSwitchInstruction', 'Turn jokers on and off with the switch') });
  const jokerRow = h('div', { class: 'play-joker' }, jokerToggle, jokerCount, jokerHint);

  const confirmBtn = button(ctx, {
    testid: 'answer-confirm', cls: 'play-confirm play-primary', icon: 'check', sprite: 'icon_confirm_tick',
    label: tr(i18n, 'port.confirm', 'OK'), onClick: () => confirm(),
  });

  const root = h('section', { class: `play-screen play-answer play-answer-${variant || 'images'}` },
    question, grid.el, jokerRow, h('div', { class: 'play-actions' }, confirmBtn));
  ctx.el.appendChild(root);

  function confirm(pos) {
    if (model.confirm(pos)) {
      playSound(ctx, 'confirmSwipe');
      const card = grid.cardFor(model.selected);
      if (card) card.classList.add('is-flicked');
    }
  }

  function render() {
    question.textContent = displayText(model.data.question);
    grid.setSelected(model.selected);
    grid.setEnabled(!model.confirmed);
    setEnabled(confirmBtn, model.canConfirm());
    const shown = model.jokerShown();
    setShown(jokerRow, shown);
    jokerToggle.setAttribute('aria-checked', model.jokerOn ? 'true' : 'false');
    setPressed(jokerToggle, model.jokerOn);
    setEnabled(jokerToggle, !model.confirmed);
    jokerLabel.textContent = tr(i18n, 'JokerSwitchTitle', 'JOKER');
    const n = Math.max(0, Number(model.data.jokers) || 0);
    jokerNum.textContent = String(n);
    jokerRemain.textContent = tr(i18n, 'JokerCounterLabel', 'JOKERS\nREMAINING');
    root.classList.toggle('is-confirmed', model.confirmed);
  }

  const off = model.on(render);
  render();

  return {
    update(next) {
      if (next.variant !== variant) {
        variant = next.variant;
        root.className = `play-screen play-answer play-answer-${variant || 'images'}`;
      }
      model.update(next.data);
      grid.update({ variant, choices: model.data.choices, selected: model.selected });
      render();
    },
    unmount() {
      off();
      grid.destroy();
      root.remove();
    },
  };
}

const PORTRAITS = [1, 2, 3, 4, 5, 6].map((i) => ({ position: i, guid: `demo-portrait-${i}`, text: ['ANNA', 'BEN', 'CHLOE', 'DAN', 'EVA', 'FINN'][i - 1] }));

export const demos = [
  {
    variant: 'portraits',
    data: {
      roundType: 2, question: 'WHO WOULD SURVIVE\nLONGEST ON A DESERT ISLAND?', jokersEnabled: true, jokers: 2, choices: PORTRAITS, selected: null, jokerOn: false,
    },
  },
  {
    variant: 'images',
    data: {
      roundType: 4, question: 'WHICH PICTURE IS THE MOST LIKE YOU?', jokersEnabled: false, jokers: 0,
      choices: [1, 2, 3, 4].map((i) => ({ position: i, guid: `demo-image-${i}`, text: '' })), selected: null, jokerOn: false,
    },
  },
  {
    variant: 'drawings',
    data: {
      roundType: 1, question: 'WHICH DRAWING IS THE BEST?', jokersEnabled: true, jokers: 1,
      choices: [1, 2, 3, 4, 5].map((i) => ({ position: i, guid: `demo-drawing-${i}`, text: '' })), selected: 2, jokerOn: false,
    },
  },
  {
    variant: 'texts',
    data: {
      roundType: 128, question: 'WOULD YOU RATHER...', jokersEnabled: false, jokers: 0,
      choices: [
        { position: 1, guid: '', text: 'Live in a castle\nwith no internet' },
        { position: 2, guid: '', text: 'Live in a tent\nwith perfect wifi' },
      ],
      selected: null, jokerOn: false,
    },
  },
];
