// Small DOM helpers. No HTML strings anywhere: elements are built with
// createElement and text goes through text nodes only.

function doc() {
  return globalThis.document;
}

function applyProps(el, props) {
  if (!props) return;
  for (const [key, value] of Object.entries(props)) {
    if (value === undefined || value === null) continue;
    switch (key) {
      case 'class':
        if (Array.isArray(value)) {
          for (const c of value) if (c) el.classList.add(c);
        } else if (value) {
          for (const c of String(value).split(/\s+/)) if (c) el.classList.add(c);
        }
        break;
      case 'testid':
        el.setAttribute('data-testid', String(value));
        break;
      case 'text':
        el.textContent = String(value);
        break;
      case 'attrs':
        for (const [name, v] of Object.entries(value)) {
          if (v === false || v === undefined || v === null) continue;
          el.setAttribute(name, v === true ? '' : String(v));
        }
        break;
      case 'dataset':
        for (const [name, v] of Object.entries(value)) {
          if (v === undefined || v === null) continue;
          el.dataset[name] = String(v);
        }
        break;
      case 'style':
        for (const [name, v] of Object.entries(value)) {
          if (v === undefined || v === null) continue;
          if (name.startsWith('--')) el.style.setProperty(name, String(v));
          else el.style[name] = String(v);
        }
        break;
      case 'on':
        for (const [type, fn] of Object.entries(value)) {
          if (typeof fn === 'function') el.addEventListener(type, fn);
        }
        break;
      default:
        // Plain DOM properties such as type, value, disabled, maxLength.
        el[key] = value;
    }
  }
}

function appendChildren(el, children) {
  for (const child of children) {
    if (child === undefined || child === null || child === false) continue;
    if (Array.isArray(child)) {
      appendChildren(el, child);
    } else if (typeof child === 'string' || typeof child === 'number') {
      el.appendChild(doc().createTextNode(String(child)));
    } else {
      el.appendChild(child);
    }
  }
}

// h('button', { class: 'btn', testid: 'x', on: { click } }, 'Label')
export function h(tag, props, ...children) {
  const el = doc().createElement(tag);
  applyProps(el, props);
  appendChildren(el, children);
  return el;
}

export function clear(el) {
  if (!el) return el;
  while (el.firstChild) el.removeChild(el.firstChild);
  return el;
}

export function setText(el, s) {
  if (el) el.textContent = s === undefined || s === null ? '' : String(s);
  return el;
}

// Number of Unicode code points (not UTF-16 units).
export function codePointLength(s) {
  return Array.from(String(s ?? '')).length;
}

// Cut a string to at most n code points without splitting surrogate pairs.
export function clampCodePoints(s, n) {
  const chars = Array.from(String(s ?? ''));
  return chars.length <= n ? String(s ?? '') : chars.slice(0, n).join('');
}

// Find a descendant by data-testid (tree walk, no selector strings needed).
export function byTestId(root, id) {
  if (!root) return null;
  const stack = [root];
  while (stack.length) {
    const node = stack.pop();
    if (node.getAttribute && node.getAttribute('data-testid') === id) return node;
    const kids = node.childNodes || [];
    for (let i = kids.length - 1; i >= 0; i--) stack.push(kids[i]);
  }
  return null;
}
