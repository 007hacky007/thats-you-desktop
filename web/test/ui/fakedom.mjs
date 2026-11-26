// Minimal DOM stand-in for node --test. Only what our UI modules use.

class FakeEvent {
  constructor(type, props = {}) {
    this.type = type;
    this.defaultPrevented = false;
    this.propagationStopped = false;
    Object.assign(this, props);
  }
  preventDefault() { this.defaultPrevented = true; }
  stopPropagation() { this.propagationStopped = true; }
}

class Node {
  constructor(ownerDocument) {
    this.ownerDocument = ownerDocument;
    this.parentNode = null;
    this.childNodes = [];
    this._listeners = new Map();
  }
  get firstChild() { return this.childNodes[0] || null; }
  get lastChild() { return this.childNodes[this.childNodes.length - 1] || null; }
  get isConnected() {
    let n = this;
    while (n.parentNode) n = n.parentNode;
    return n === this.ownerDocument;
  }
  appendChild(child) {
    if (child.parentNode) child.parentNode.removeChild(child);
    child.parentNode = this;
    this.childNodes.push(child);
    return child;
  }
  append(...kids) { for (const k of kids) this.appendChild(typeof k === 'string' ? this.ownerDocument.createTextNode(k) : k); }
  insertBefore(child, ref) {
    if (!ref) return this.appendChild(child);
    if (child.parentNode) child.parentNode.removeChild(child);
    const i = this.childNodes.indexOf(ref);
    child.parentNode = this;
    this.childNodes.splice(i < 0 ? this.childNodes.length : i, 0, child);
    return child;
  }
  removeChild(child) {
    const i = this.childNodes.indexOf(child);
    if (i < 0) throw new Error('not a child');
    this.childNodes.splice(i, 1);
    child.parentNode = null;
    return child;
  }
  remove() { if (this.parentNode) this.parentNode.removeChild(this); }
  replaceChildren(...kids) {
    while (this.childNodes.length) this.removeChild(this.childNodes[0]);
    this.append(...kids);
  }
  contains(other) {
    for (let n = other; n; n = n.parentNode) if (n === this) return true;
    return false;
  }
  get textContent() {
    if (this.nodeType === 3) return this.data;
    return this.childNodes.map((c) => c.textContent).join('');
  }
  set textContent(v) {
    if (this.nodeType === 3) { this.data = String(v); return; }
    while (this.childNodes.length) this.removeChild(this.childNodes[0]);
    if (v !== '') this.appendChild(this.ownerDocument.createTextNode(String(v)));
  }
  addEventListener(type, fn, opts) {
    if (!this._listeners.has(type)) this._listeners.set(type, []);
    this._listeners.get(type).push({ fn, once: !!(opts && opts.once) });
  }
  removeEventListener(type, fn) {
    const list = this._listeners.get(type);
    if (!list) return;
    const i = list.findIndex((l) => l.fn === fn);
    if (i >= 0) list.splice(i, 1);
  }
  dispatchEvent(ev) {
    if (!ev.target) ev.target = this;
    ev.currentTarget = this;
    const list = (this._listeners.get(ev.type) || []).slice();
    for (const l of list) {
      if (l.once) this.removeEventListener(ev.type, l.fn);
      l.fn.call(this, ev);
    }
    if (ev.bubbles !== false && !ev.propagationStopped && this.parentNode) this.parentNode.dispatchEvent(ev);
    return !ev.defaultPrevented;
  }
}

class Text extends Node {
  constructor(doc, data) { super(doc); this.nodeType = 3; this.data = String(data); }
}

function makeClassList(el) {
  const set = new Set();
  return {
    add: (...c) => c.forEach((x) => set.add(x)),
    remove: (...c) => c.forEach((x) => set.delete(x)),
    toggle: (c, force) => {
      const on = force === undefined ? !set.has(c) : !!force;
      if (on) set.add(c); else set.delete(c);
      return on;
    },
    contains: (c) => set.has(c),
    get length() { return set.size; },
    toString: () => Array.from(set).join(' '),
    [Symbol.iterator]: () => set.values(),
  };
}

function makeStyle() {
  const props = {};
  return new Proxy(props, {
    get(target, key) {
      if (key === 'setProperty') return (n, v) => { target[n] = String(v); };
      if (key === 'getPropertyValue') return (n) => target[n] ?? '';
      if (key === 'removeProperty') return (n) => { delete target[n]; };
      return target[key] ?? '';
    },
    set(target, key, v) { target[key] = v; return true; },
  });
}

class Element extends Node {
  constructor(doc, tag, ns) {
    super(doc);
    this.nodeType = 1;
    this.tagName = String(tag).toUpperCase();
    this.localName = String(tag);
    this.namespaceURI = ns || 'http://www.w3.org/1999/xhtml';
    this.attributes = new Map();
    this.classList = makeClassList(this);
    this.dataset = {};
    this.style = makeStyle();
    this.value = '';
    this.disabled = false;
    this.hidden = false;
  }
  setAttribute(n, v) { this.attributes.set(n, String(v)); }
  getAttribute(n) { return this.attributes.has(n) ? this.attributes.get(n) : null; }
  hasAttribute(n) { return this.attributes.has(n); }
  removeAttribute(n) { this.attributes.delete(n); }
  get className() { return this.classList.toString(); }
  focus() { this.ownerDocument.activeElement = this; }
  blur() { if (this.ownerDocument.activeElement === this) this.ownerDocument.activeElement = null; }
  click() { this.dispatchEvent(new FakeEvent('click', { bubbles: true })); }
  getBoundingClientRect() { return { left: 0, top: 0, width: 100, height: 100, right: 100, bottom: 100 }; }
  setPointerCapture() {}
  releasePointerCapture() {}
  hasPointerCapture() { return false; }
}

export class FakeDocument extends Node {
  constructor() {
    super(null);
    this.ownerDocument = this;
    this.nodeType = 9;
    this.documentElement = new Element(this, 'html');
    this.appendChild(this.documentElement);
    this.head = new Element(this, 'head');
    this.body = new Element(this, 'body');
    this.documentElement.appendChild(this.head);
    this.documentElement.appendChild(this.body);
    this.activeElement = null;
    this.visibilityState = 'visible';
  }
  createElement(tag) { return new Element(this, tag); }
  createElementNS(ns, tag) { return new Element(this, tag, ns); }
  createTextNode(s) { return new Text(this, s); }
}

export function installFakeDom() {
  const document = new FakeDocument();
  globalThis.document = document;
  return document;
}

export function fire(el, type, props = {}) {
  const ev = new FakeEvent(type, { bubbles: true, ...props });
  el.dispatchEvent(ev);
  return ev;
}

export function findTestId(root, id) {
  const stack = [root];
  while (stack.length) {
    const n = stack.pop();
    if (n.getAttribute && n.getAttribute('data-testid') === id) return n;
    for (let i = n.childNodes.length - 1; i >= 0; i--) stack.push(n.childNodes[i]);
  }
  return null;
}

export function allTestIds(root) {
  const out = [];
  const stack = [root];
  while (stack.length) {
    const n = stack.pop();
    const id = n.getAttribute && n.getAttribute('data-testid');
    if (id) out.push(id);
    for (let i = n.childNodes.length - 1; i >= 0; i--) stack.push(n.childNodes[i]);
  }
  return out;
}

export const tick = () => new Promise((r) => setTimeout(r, 0));
