// Keeps the phone screen on while playing. Players spend long stretches looking at the TV;
// a locked screen pauses the page, the game stops hearing from the phone and drops it.
// Screen Wake Lock API (Chrome 84+, Safari 16.4+). The browser releases the lock whenever
// the page is hidden, so it is requested again when the page becomes visible.

export function keepAwake(nav = globalThis.navigator, doc = globalThis.document) {
  let lock = null;
  let stopped = false;
  const api = nav && nav.wakeLock && typeof nav.wakeLock.request === 'function' ? nav.wakeLock : null;

  async function acquire() {
    if (!api || stopped || (lock && !lock.released) || doc.visibilityState !== 'visible') return;
    try {
      lock = await api.request('screen');
    } catch {
      lock = null; // not allowed (no user gesture yet, battery saver, ...): try again on the next visibility change
    }
  }

  const onVisibility = () => acquire();
  if (api && doc && doc.addEventListener) doc.addEventListener('visibilitychange', onVisibility);
  const ready = acquire();

  return {
    ready,
    held: () => !!(lock && !lock.released),
    async stop() {
      stopped = true;
      if (doc && doc.removeEventListener) doc.removeEventListener('visibilitychange', onVisibility);
      if (lock && !lock.released) {
        try { await lock.release(); } catch { /* already gone */ }
      }
      lock = null;
    },
  };
}
