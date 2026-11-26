// Text entry logic: clamp to N code points, report changes, submit once.
// No DOM, imports in Node.

export function codePointLength(s) {
  let n = 0;
  for (const _ of String(s)) n += 1;
  return n;
}

export function clampCodePoints(s, max) {
  if (s == null) return '';
  const str = String(s);
  let n = 0;
  let end = 0;
  for (const ch of str) {
    if (n >= max) return str.slice(0, end);
    n += 1;
    end += ch.length;
  }
  return str;
}

// actions = game.actions (textChanged, submitText)
export function createTextEntry({ actions, maxLength = 80, initial = '' }) {
  const max = Number.isInteger(maxLength) && maxLength > 0 ? maxLength : 80;
  const e = {
    maxLength: max,
    text: clampCodePoints(initial, max),
    submitted: false,
    get remaining() { return max - codePointLength(e.text); },
    // returns the clamped value the input element should show
    input(raw) {
      if (e.submitted) return e.text;
      const next = clampCodePoints(raw, max);
      if (next !== e.text) {
        e.text = next;
        actions.textChanged(next);
      }
      return e.text;
    },
    setText(s) {
      if (!e.submitted) e.text = clampCodePoints(s, max);
    },
    submit() {
      if (e.submitted) return false;
      e.submitted = true;
      actions.submitText(e.text);
      return true;
    },
  };
  return e;
}
