// localStorage wrapper: keys are prefixed "iwyk.", every access is guarded,
// and values are kept in memory too so a broken or full backend degrades to
// a per-page-load store instead of failing.

const PREFIX = 'iwyk.';

function defaultBackend() {
  try {
    return globalThis.localStorage || null;
  } catch {
    return null;
  }
}

export function createStorage(backend) {
  if (backend === undefined) backend = defaultBackend();
  const mem = new Map();

  function get(key) {
    const k = PREFIX + key;
    if (backend) {
      try {
        const v = backend.getItem(k);
        if (v !== null && v !== undefined) return String(v);
      } catch {
        // fall through to memory
      }
    }
    return mem.has(k) ? mem.get(k) : null;
  }

  function set(key, value) {
    const k = PREFIX + key;
    const v = String(value);
    mem.set(k, v);
    if (!backend) return false;
    try {
      backend.setItem(k, v);
      return true;
    } catch {
      return false;
    }
  }

  function remove(key) {
    const k = PREFIX + key;
    mem.delete(k);
    if (!backend) return;
    try {
      backend.removeItem(k);
    } catch {
      // ignore
    }
  }

  function getJSON(key, dflt = null) {
    const v = get(key);
    if (v === null) return dflt;
    try {
      return JSON.parse(v);
    } catch {
      return dflt;
    }
  }

  function setJSON(key, value) {
    return set(key, JSON.stringify(value));
  }

  return { get, set, remove, getJSON, setJSON };
}

// A Storage-like in-memory backend (tests, private mode).
export function createMemoryBackend(initial = {}) {
  const m = new Map(Object.entries(initial));
  return {
    getItem: (k) => (m.has(k) ? m.get(k) : null),
    setItem: (k, v) => { m.set(k, String(v)); },
    removeItem: (k) => { m.delete(k); },
    clear: () => m.clear(),
    key: (i) => [...m.keys()][i] ?? null,
    get length() { return m.size; },
    dump: () => Object.fromEntries(m),
  };
}
