// Drawing toolbar: palette (colour-1..14), tools, sizes and undo. Browser only.

import { initialDrawState, drawReducer, toolbarModel } from './draw-state.js';
import { button, setPressed, setEnabled, playSound } from './ui-bits.js';

const TOOL_ICONS = {
  brush: ['brush', 'icon_pencil'],
  eraser: ['eraser', 'icon_eraser'],
  spray: ['spray', 'icon_spray'],
};

// opts: { state, onChange(state), onUndo() }
export function createDrawToolbar(ctx, opts) {
  const { h } = ctx;
  let state = opts.state || initialDrawState();
  const model0 = toolbarModel(state);

  const colourBtns = model0.colours.map((c) => {
    const b = h('button', {
      class: 'play-swatch',
      testid: c.testid,
      attrs: { type: 'button', 'aria-label': c.name.toLowerCase().replace('_', ' '), 'aria-pressed': 'false' },
      style: { backgroundColor: c.css },
      dataset: { colour: String(c.id) },
      on: { click: () => dispatch({ type: 'colour', colour: c.id }) },
    });
    return b;
  });
  const toolBtns = model0.tools.map((t) => button(ctx, {
    testid: t.testid,
    cls: 'play-tool',
    icon: TOOL_ICONS[t.id][0],
    sprite: TOOL_ICONS[t.id][1],
    title: t.id,
    onClick: () => dispatch({ type: 'tool', tool: t.id }),
  }));
  const sizeDots = [];
  const sizeBtns = model0.sizes.map((s) => {
    const dot = h('span', { class: 'play-size-dot' });
    sizeDots.push(dot);
    return h('button', {
      class: 'play-btn play-size',
      testid: s.testid,
      attrs: { type: 'button', 'aria-label': `size ${s.id}`, 'aria-pressed': 'false' },
      on: { click: () => dispatch({ type: 'size', size: s.id }) },
    }, dot);
  });
  const undo = button(ctx, {
    testid: 'undo', cls: 'play-tool play-undo', icon: 'undo', sprite: 'icon_undo', title: 'Undo',
    onClick: () => { playSound(ctx, 'button'); if (opts.onUndo) opts.onUndo(); },
  });

  const palette = h('div', { class: 'play-palette' }, ...colourBtns);
  const tools = h('div', { class: 'play-tools' }, ...toolBtns, h('span', { class: 'play-tools-gap' }), ...sizeBtns, h('span', { class: 'play-tools-gap' }), undo);
  const el = h('div', { class: 'play-toolbar' }, palette, tools);

  function render() {
    const m = toolbarModel(state);
    m.colours.forEach((c, i) => { setPressed(colourBtns[i], c.selected); setEnabled(colourBtns[i], m.coloursEnabled); });
    m.tools.forEach((t, i) => { setPressed(toolBtns[i], t.selected); setEnabled(toolBtns[i], !state.busy); });
    m.sizes.forEach((s, i) => {
      setPressed(sizeBtns[i], s.selected);
      setEnabled(sizeBtns[i], !state.busy);
      // preview scaled from output pixels into a 6..30 px dot
      const px = Math.max(6, Math.min(30, Math.round(s.diameter * 0.6)));
      sizeDots[i].style.width = `${px}px`;
      sizeDots[i].style.height = `${px}px`;
    });
    setEnabled(undo, m.undoEnabled);
    el.dataset.tool = state.tool;
    el.style.setProperty('--play-ink', m.colours[state.colour - 1].css);
  }

  function dispatch(action) {
    const next = drawReducer(state, action);
    if (next === state) return;
    state = next;
    render();
    if (opts.onChange && action.type !== 'surface' && action.type !== 'busy') {
      playSound(ctx, 'button');
      opts.onChange(state);
    }
  }

  render();
  return {
    el,
    get state() { return state; },
    dispatch,
    model: () => toolbarModel(state),
  };
}
