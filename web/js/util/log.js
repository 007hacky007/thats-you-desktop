// Ring-buffer log. Entries are { t, level, msg }. Never log the join
// parameters (token, ICE password) or the page URL.

import { createEmitter } from './emitter.js';

const LEVELS = ['debug', 'info', 'warn', 'error'];

function fmt(arg) {
  if (typeof arg === 'string') return arg;
  if (arg instanceof Error) return arg.name + ': ' + arg.message;
  if (arg === undefined) return 'undefined';
  try {
    const s = JSON.stringify(arg);
    return s === undefined ? String(arg) : s;
  } catch {
    return String(arg);
  }
}

export function createLog({ capacity = 500, now = () => Date.now(), echo = false } = {}) {
  const cap = Math.max(1, capacity | 0);
  const buf = [];
  const em = createEmitter();

  function write(level, args) {
    const entry = { t: now(), level, msg: args.map(fmt).join(' ') };
    buf.push(entry);
    if (buf.length > cap) buf.splice(0, buf.length - cap);
    if (echo && globalThis.console) {
      const fn = globalThis.console[level] || globalThis.console.log;
      fn.call(globalThis.console, '[' + level + '] ' + entry.msg);
    }
    em.emit('entry', entry);
    return entry;
  }

  const log = {
    entries: () => buf.slice(),
    on: (ev, fn) => em.on(ev, fn),
    clear: () => { buf.length = 0; },
  };
  for (const level of LEVELS) log[level] = (...args) => write(level, args);
  return log;
}

// A log that drops everything (default for modules created without one).
export const nullLog = {
  debug() {}, info() {}, warn() {}, error() {},
  entries: () => [],
  on: () => () => {},
  clear() {},
};
