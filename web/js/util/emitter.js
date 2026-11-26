// Tiny event emitter. Listener errors are reported and never break emit().

export function createEmitter({ onError } = {}) {
  const listeners = new Map();
  const report = onError || ((err) => {
    if (globalThis.console) globalThis.console.error(err);
  });

  function on(event, fn) {
    if (typeof fn !== 'function') throw new TypeError('listener must be a function');
    let list = listeners.get(event);
    if (!list) {
      list = [];
      listeners.set(event, list);
    }
    const entry = { fn };
    list.push(entry);
    return () => {
      const cur = listeners.get(event);
      if (!cur) return;
      const i = cur.indexOf(entry);
      if (i >= 0) cur.splice(i, 1);
    };
  }

  function once(event, fn) {
    const off = on(event, (...args) => {
      off();
      fn(...args);
    });
    return off;
  }

  function emit(event, ...args) {
    const list = listeners.get(event);
    if (!list || list.length === 0) return;
    for (const entry of list.slice()) {
      try {
        entry.fn(...args);
      } catch (err) {
        report(err);
      }
    }
  }

  function count(event) {
    const list = listeners.get(event);
    return list ? list.length : 0;
  }

  return { on, once, emit, count };
}
