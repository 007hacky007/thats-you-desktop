// Every screen id of the view machine mapped to its module. Each module is
// loaded with a dynamic import, so a missing or broken module only breaks its
// own screen.

export const SCREEN_MODULES = {
  connect: './screens/connect.js',
  joined: './screens/joined.js',
  name: './screens/name.js',
  card: './screens/card.js',
  selfie: './screens/selfie.js',
  confirm: './screens/confirm.js',
  holding: './screens/holding.js',
  'quickdraw-holding': './screens/quickdrawholding.js',
  continue: './screens/continue.js',
  joker: './screens/joker.js',
  answer: './screens/answer.js',
  draw: './screens/draw.js',
  text: './screens/text.js',
  photo: './screens/photo.js',
  mimic: './screens/mimic.js',
};

export const OVERLAY_MODULES = {
  pause: './overlays/pause.js',
  leave: './overlays/leave.js',
  popup: './overlays/popup.js',
  reconnecting: './overlays/reconnecting.js',
  'bad-connection': './overlays/badconnection.js',
};

const cache = new Map();

function load(table, kind, id, importer) {
  const path = Object.prototype.hasOwnProperty.call(table, id) ? table[id] : null;
  if (!path) return Promise.reject(new Error(`unknown ${kind} ${id}`));
  const key = `${kind}:${id}`;
  if (!cache.has(key)) {
    const p = Promise.resolve().then(() => importer(path)).then((mod) => {
      if (!mod || typeof mod.mount !== 'function') throw new Error(`${kind} ${id} has no mount()`);
      return mod;
    });
    p.catch(() => cache.delete(key));
    cache.set(key, p);
  }
  return cache.get(key);
}

// The specifier is resolved relative to this module (ui/), as import() does.
const defaultImporter = (path) => import(path);

export function loadScreen(id, importer = defaultImporter) {
  return load(SCREEN_MODULES, 'screen', id, importer);
}

export function loadOverlay(id, importer = defaultImporter) {
  return load(OVERLAY_MODULES, 'overlay', id, importer);
}

export function clearRegistryCache() {
  cache.clear();
}
