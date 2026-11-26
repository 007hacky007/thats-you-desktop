// Service worker: offline support for the phone page.
//
// Built page (precache.json present): every listed file is cached in
// "iwyk-<version>" at install; requests are served cache-first with network
// fallback (and cache fill); navigations get the cached index.html. Opening
// the page also checks precache.json in the background and, when its version
// changed, caches the new version and drops the old one (next load uses it).
//
// Plain web/ (no precache.json, development): network first, runtime cache
// "iwyk-runtime" as the offline fallback.

const CACHE_PREFIX = 'iwyk-';
const RUNTIME_CACHE = 'iwyk-runtime';
const VERSIONED = /^iwyk-[0-9a-f]{16}$/;
const UPDATE_EVERY_MS = 60 * 1000;

function cacheNameFor(version) {
  return CACHE_PREFIX + version;
}

function isSafeRelative(p) {
  return typeof p === 'string' && p.length > 0 && p.length < 1024 && !/^[a-z][a-z0-9+.-]*:/i.test(p)
    && !p.startsWith('/') && !p.includes('\\') && !p.split('/').some((s) => s === '..');
}

// precache.json -> { version, files } or null when unusable.
function parsePrecache(json) {
  if (!json || typeof json !== 'object' || typeof json.version !== 'string' || !/^[0-9a-f]{16}$/.test(json.version)) return null;
  if (!Array.isArray(json.files)) return null;
  const files = [];
  for (const f of json.files) {
    if (!isSafeRelative(f)) return null;
    if (f === 'sw.js' || f === 'precache.json') continue;
    if (!files.includes(f)) files.push(f);
  }
  return { version: json.version, files };
}

// Our caches other than `keep` (other apps on the origin are left alone).
function cachesToDelete(names, keep) {
  return names.filter((n) => n.startsWith(CACHE_PREFIX) && n !== keep);
}

function pickCurrent(names) {
  const v = names.filter((n) => VERSIONED.test(n));
  return v.length ? v[v.length - 1] : null;
}

function scopeUrl(path) {
  return new URL(path, self.registration.scope).href;
}

let activeCache = null;
let lastUpdateCheck = 0;
let updating = null;

async function fetchPrecache() {
  try {
    const res = await fetch(scopeUrl('precache.json'), { cache: 'no-store' });
    if (!res.ok) return null;
    return parsePrecache(await res.json());
  } catch {
    return null;
  }
}

async function fillCache(pc) {
  const name = cacheNameFor(pc.version);
  const cache = await caches.open(name);
  try {
    await Promise.all(pc.files.map(async (f) => {
      const url = scopeUrl(f);
      const res = await fetch(url, { cache: 'no-store' });
      if (!res.ok) throw new Error(`precache ${f}: ${res.status}`);
      await cache.put(url, res);
    }));
  } catch (e) {
    await caches.delete(name);
    throw e;
  }
  return name;
}

async function currentCache() {
  if (activeCache) return activeCache;
  activeCache = pickCurrent(await caches.keys());
  return activeCache;
}

async function dropOthers(keep) {
  const names = await caches.keys();
  await Promise.all(cachesToDelete(names, keep).map((n) => caches.delete(n)));
}

// Background check for a newer build (runs at most once a minute).
function checkForUpdate(now) {
  if (updating || now - lastUpdateCheck < UPDATE_EVERY_MS) return updating;
  lastUpdateCheck = now;
  updating = (async () => {
    const pc = await fetchPrecache();
    const cur = await currentCache();
    if (!pc || cacheNameFor(pc.version) === cur) return cur;
    const name = await fillCache(pc);
    activeCache = name;
    await dropOthers(name);
    return name;
  })().catch(() => null).finally(() => { updating = null; });
  return updating;
}

async function fromCache(name, request) {
  const cache = await caches.open(name);
  return cache.match(request);
}

async function handle(request, event) {
  const isNav = request.mode === 'navigate';
  const name = await currentCache();
  if (name) {
    if (isNav) {
      const p = checkForUpdate(Date.now());
      if (p && event && event.waitUntil) event.waitUntil(p);
      const hit = (await fromCache(name, request)) || (await fromCache(name, scopeUrl('index.html')));
      if (hit) return hit;
    } else {
      const hit = await fromCache(name, request);
      if (hit) return hit;
    }
    try {
      const res = await fetch(request);
      if (res && res.status === 200 && res.type === 'basic' && !isNav) {
        const cache = await caches.open(name);
        await cache.put(request, res.clone());
      }
      return res;
    } catch (e) {
      if (isNav) {
        const idx = await fromCache(name, scopeUrl('index.html'));
        if (idx) return idx;
      }
      throw e;
    }
  }
  // development / no precache: network first, runtime cache as fallback
  try {
    const res = await fetch(request);
    if (res && res.status === 200 && res.type === 'basic') {
      const cache = await caches.open(RUNTIME_CACHE);
      await cache.put(request, res.clone());
    }
    return res;
  } catch (e) {
    const hit = await fromCache(RUNTIME_CACHE, request);
    if (hit) return hit;
    if (isNav) {
      const idx = (await fromCache(RUNTIME_CACHE, scopeUrl('index.html'))) || (await fromCache(RUNTIME_CACHE, scopeUrl('./')));
      if (idx) return idx;
    }
    throw e;
  }
}

function shouldHandle(request) {
  if (request.method !== 'GET') return false;
  const url = new URL(request.url);
  if (url.origin !== self.location.origin) return false;
  if (request.headers && request.headers.get && request.headers.get('range')) return false;
  const path = url.pathname;
  return !path.endsWith('/sw.js') && !path.endsWith('/precache.json');
}

self.addEventListener('install', (event) => {
  event.waitUntil((async () => {
    const pc = await fetchPrecache();
    if (pc) {
      activeCache = await fillCache(pc);
    }
    await self.skipWaiting();
  })());
});

self.addEventListener('activate', (event) => {
  event.waitUntil((async () => {
    const keep = await currentCache();
    if (keep) await dropOthers(keep);
    await self.clients.claim();
  })());
});

self.addEventListener('fetch', (event) => {
  if (!shouldHandle(event.request)) return;
  event.respondWith(handle(event.request, event));
});
