// Clocks: the real one and a deterministic fake for tests.

export const realClock = {
  now: () => Date.now(),
  setTimeout: (fn, ms) => globalThis.setTimeout(fn, ms),
  clearTimeout: (id) => globalThis.clearTimeout(id),
  setInterval: (fn, ms) => globalThis.setInterval(fn, ms),
  clearInterval: (id) => globalThis.clearInterval(id),
};

// A fake clock. Timers only run inside advance()/advanceAsync(), in time
// order (ties in creation order). now() starts at `start`.
export function createFakeClock(start = 0) {
  let t = start;
  let nextId = 1;
  const timers = new Map();

  function add(fn, ms, interval) {
    const id = nextId++;
    const delay = Math.max(0, Number(ms) || 0);
    timers.set(id, { id, fn, at: t + delay, interval: interval ? Math.max(1, delay) : 0 });
    return id;
  }

  function clear(id) {
    timers.delete(id);
  }

  function nextDue(limit) {
    let best = null;
    for (const tm of timers.values()) {
      if (tm.at > limit) continue;
      if (!best || tm.at < best.at || (tm.at === best.at && tm.id < best.id)) best = tm;
    }
    return best;
  }

  function fire(tm) {
    t = Math.max(t, tm.at);
    if (tm.interval) tm.at += tm.interval;
    else timers.delete(tm.id);
    tm.fn();
  }

  function advance(ms = 0) {
    const target = t + Math.max(0, ms);
    for (;;) {
      const tm = nextDue(target);
      if (!tm) break;
      fire(tm);
    }
    t = target;
  }

  // Like advance() but lets pending promise callbacks run between timers.
  async function advanceAsync(ms = 0, step = 0) {
    const target = t + Math.max(0, ms);
    await flush();
    for (;;) {
      const limit = step > 0 ? Math.min(target, t + step) : target;
      const tm = nextDue(limit);
      if (!tm) {
        if (limit >= target) break;
        t = limit;
        await flush();
        continue;
      }
      fire(tm);
      await flush();
    }
    t = target;
    await flush();
  }

  return {
    now: () => t,
    setTimeout: (fn, ms) => add(fn, ms, false),
    clearTimeout: clear,
    setInterval: (fn, ms) => add(fn, ms, true),
    clearInterval: clear,
    advance,
    advanceAsync,
    pending: () => timers.size,
  };
}

// Let queued microtasks (promise callbacks) run.
export async function flush(rounds = 20) {
  for (let i = 0; i < rounds; i++) await Promise.resolve();
}
