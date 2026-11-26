// Pure logic of the answer screen: choice numbering, grid layout, joker toggle
// visibility and the select / confirm / joker model. No DOM, imports in Node.

export function numberChoices(choices) {
  const list = Array.isArray(choices) ? choices : [];
  const explicit = list.map((c) => (c && Number.isInteger(c.position) && c.position > 0 ? c.position : null));
  const distinct = explicit.every((p) => p !== null) && new Set(explicit).size === explicit.length;
  return list.map((c, i) => {
    const pos = distinct ? explicit[i] : i + 1;
    return {
      pos,
      testid: `answer-${pos}`,
      guid: c && c.guid != null ? String(c.guid) : '',
      text: c && c.text != null ? String(c.text) : '',
    };
  });
}

export function choiceKind(variant) {
  return variant === 'texts' ? 'text' : 'image';
}

export function gridLayout(count, kind) {
  const n = Math.max(0, count | 0);
  let columns;
  if (kind === 'text' || n <= 1) columns = 1;
  else if (n <= 6) columns = 2;
  else columns = 3;
  return { columns, rows: Math.ceil(n / columns) };
}

export function jokerVisible(data) {
  return !!(data && data.jokersEnabled && Number(data.jokers) > 0);
}

// actions: game.actions (selectAnswer, confirmAnswer, setJoker)
export function createAnswerModel({ actions, data }) {
  const listeners = new Set();
  const m = {
    items: [],
    data: {},
    selected: null,
    jokerOn: false,
    confirmed: false,
    on(fn) { listeners.add(fn); return () => listeners.delete(fn); },
    has(pos) { return m.items.some((c) => c.pos === pos); },
    update(next) {
      m.data = next || {};
      m.items = numberChoices(m.data.choices);
      if (m.data.selected != null && m.has(m.data.selected)) m.selected = m.data.selected;
      else if (m.selected != null && !m.has(m.selected)) m.selected = null;
      if (typeof m.data.jokerOn === 'boolean') m.jokerOn = m.data.jokerOn;
      emit();
    },
    select(pos) {
      if (m.confirmed || !m.has(pos)) return false;
      m.selected = pos;
      actions.selectAnswer(pos);
      emit();
      return true;
    },
    canConfirm() { return !m.confirmed && m.selected != null; },
    // confirm(pos) selects pos first (flick on a card); confirm() uses the selection
    confirm(pos) {
      if (m.confirmed) return false;
      if (pos != null && pos !== m.selected && !m.select(pos)) return false;
      if (m.selected == null) return false;
      m.confirmed = true;
      actions.confirmAnswer(m.selected);
      emit();
      return true;
    },
    jokerShown() { return jokerVisible(m.data); },
    toggleJoker() {
      if (m.confirmed || !m.jokerShown()) return false;
      m.jokerOn = !m.jokerOn;
      actions.setJoker(m.jokerOn);
      emit();
      return true;
    },
  };
  function emit() { for (const fn of [...listeners]) fn(m); }
  m.update(data);
  return m;
}
