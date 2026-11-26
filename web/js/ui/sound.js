// Optional UI sounds. Only active when the build bundled the app's sounds
// (assets.sound(event) returns a URL); otherwise every call is a no-op.
// Browsers block audio until a user gesture: unlock() is called from the
// PLAY tap on the connect screen.

export function createSound({ assets, log, AudioCtor } = {}) {
  const Ctor = AudioCtor || globalThis.Audio;
  const players = new Map();
  let unlocked = false;
  let muted = false;

  const playerFor = (event) => {
    if (players.has(event)) return players.get(event);
    const url = assets && typeof assets.sound === 'function' ? assets.sound(event) : null;
    let p = null;
    if (url && Ctor) {
      try {
        p = new Ctor(url);
        p.preload = 'auto';
      } catch {
        p = null;
      }
    }
    players.set(event, p);
    return p;
  };

  return {
    get enabled() { return !!(assets && assets.mode === 'art' && Ctor); },
    get unlocked() { return unlocked; },
    get muted() { return muted; },
    setMuted(v) { muted = !!v; },
    unlock() {
      unlocked = true;
    },
    play(event) {
      if (!unlocked || muted) return false;
      const p = playerFor(event);
      if (!p) return false;
      try {
        p.currentTime = 0;
        const r = p.play();
        if (r && typeof r.catch === 'function') r.catch((e) => log && log.debug && log.debug(`sound ${event}: ${e && e.name}`));
        return true;
      } catch {
        return false;
      }
    },
  };
}

export const silentSound = { enabled: false, unlocked: false, muted: true, setMuted() {}, unlock() {}, play() { return false; } };
